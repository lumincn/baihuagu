# ============================================================
# 注册/更新 百花 OpenVINO Host Windows 服务
# 用法（管理员 PowerShell）:  powershell -ExecutionPolicy Bypass -File install-openvino-host-service.ps1
# 说明：服务由 openvino_host_service.py（pywin32 ServiceFramework）实现，
#       SCM 正确注册，避免"直接跑 python 脚本被 SCM 杀掉"的问题。
# ============================================================
$ErrorActionPreference = "Stop"

$PY      = "C:\Users\lumin\AppData\Local\Programs\Python\Python312\python.exe"
$SERVICE_SCRIPT = "C:\Users\lumin\src\baihuagu\services\Baihua.AI.Provider\LocalVision\openvino_host_service.py"
$SERVICE = "BaihuaOpenVinoHost"

# --- 0. 管理员检查 ---
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[X] 需要管理员权限！请右键 PowerShell -> 以管理员身份运行" -ForegroundColor Red
    exit 1
}

# --- 1. 停掉手动跑的 openvino_host.py（避免 8866 端口冲突） ---
Write-Host "[1/5] 停止手动运行的 openvino_host.py ..."
Get-CimInstance Win32_Process -Filter "Name='python.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match "openvino_host" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue; Write-Host "     已停止 PID $($_.ProcessId)" }

# --- 2. 删除旧服务（若存在，避免残留错误配置） ---
$existing = Get-Service -Name $SERVICE -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[2/5] 服务已存在，先停止并删除 ..."
    sc.exe stop $SERVICE 2>$null | Out-Null
    Start-Sleep -Seconds 1
    sc.exe delete $SERVICE | Out-Null
    Start-Sleep -Seconds 2
} else {
    Write-Host "[2/5] 无旧服务 ..."
}

# --- 3. 用 pywin32 安装服务（HandleCommandLine install 自动设置开机自启） ---
Write-Host "[3/5] 安装服务 $SERVICE (pywin32) ..."
& $PY $SERVICE_SCRIPT install | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "[X] 服务安装失败（exit=$LASTEXITCODE）" -ForegroundColor Red
    exit 1
}
Start-Sleep -Seconds 1

# --- 4. 启动服务 ---
Write-Host "[4/5] 启动服务 ..."
sc.exe start $SERVICE | Out-Null
Start-Sleep -Seconds 6

# --- 5. 验证 ---
Write-Host "[5/5] 验证 ..."
$svc = Get-Service -Name $SERVICE -ErrorAction SilentlyContinue
Write-Host "     服务状态: $($svc.Status)"
try {
    $h = Invoke-RestMethod -Uri "http://127.0.0.1:8866/health" -TimeoutSec 8
    Write-Host "[OK] openvino-host 已就绪: $($h.service)" -ForegroundColor Green
    $s = Invoke-RestMethod -Uri "http://127.0.0.1:8866/status" -TimeoutSec 8
    Write-Host "     实例: $($s.instances.Count) 个 (端口 $(( $s.instances | ForEach-Object { $_.port }) -join ', '))"
} catch {
    Write-Host "[!] 服务状态 $($svc.Status)，但 8866 未就绪" -ForegroundColor Yellow
    Write-Host "     请检查服务日志: 事件查看器 -> Windows 日志 -> 应用程序 (来源: BaihuaOpenVinoHost)"
    Write-Host "     或前台调试: cd services\Baihua.AI.Provider\LocalVision; python openvino_host_service.py debug"
}

Write-Host ""
Write-Host "完成。开机自启已启用（Automatic）。" -ForegroundColor Cyan
