# ============================================================
# 注册/更新 百花 OpenVINO Host Windows 服务
# 用法（管理员 PowerShell）:  powershell -ExecutionPolicy Bypass -File install-openvino-host-service.ps1
# ============================================================
$ErrorActionPreference = "Stop"

$PY      = "C:\Users\lumin\AppData\Local\Programs\Python\Python312\python.exe"
$SCRIPT  = "C:\Users\lumin\src\baihuagu\services\Baihua.AI.Provider\LocalVision\openvino_host.py"
$SERVICE = "BaihuaOpenVinoHost"

# --- 0. 管理员检查 ---
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[X] 需要管理员权限！请右键 PowerShell -> 以管理员身份运行" -ForegroundColor Red
    exit 1
}

# --- 1. 停掉手动跑的 openvino_host.py（避免 8866 端口冲突） ---
Write-Host "[1/4] 停止手动运行的 openvino_host.py ..."
Get-CimInstance Win32_Process -Filter "Name='python.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match "openvino_host" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue; Write-Host "     已停止 PID $($_.ProcessId)" }

# --- 2. 删除旧服务（若存在） ---
$existing = Get-Service -Name $SERVICE -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[2/4] 服务已存在，先删除旧服务 ..."
    sc.exe stop $SERVICE | Out-Null
    sc.exe delete $SERVICE | Out-Null
    Start-Sleep -Seconds 2
} else {
    Write-Host "[2/4] 无旧服务，直接创建 ..."
}

# --- 3. 创建服务（New-Service 正确处理带引号的 binPath） ---
Write-Host "[3/4] 创建服务 $SERVICE ..."
$binPath = "`"$PY`" `"$SCRIPT`" --port 8866 --bind 127.0.0.1"
New-Service -Name $SERVICE `
    -DisplayName "Baihua OpenVINO Host" `
    -BinaryPathName $binPath `
    -StartupType Automatic | Out-Null

# 服务描述（sc.exe description 对中文支持良好，前提是控制台代码页正常）
sc.exe description $SERVICE "Baihua OpenVINO LLM/Embedding host (openvino_llm_server.py: 8000 chat / 8001 code / 8002 embedding)" | Out-Null

# --- 4. 启动 + 验证 ---
Write-Host "[4/4] 启动服务并验证 ..."
sc.exe start $SERVICE | Out-Null
Start-Sleep -Seconds 4

try {
    $h = Invoke-RestMethod -Uri "http://127.0.0.1:8866/health" -TimeoutSec 8
    Write-Host "[OK] openvino-host 已就绪: $($h.service)" -ForegroundColor Green
    $s = Invoke-RestMethod -Uri "http://127.0.0.1:8866/status" -TimeoutSec 8
    Write-Host "     实例: $($s.instances.Count) 个 (端口 $(( $s.instances | ForEach-Object { $_.port }) -join ', '))"
} catch {
    Write-Host "[!] 服务已启动但 8866 未就绪，请检查: sc.exe query $SERVICE" -ForegroundColor Yellow
    Write-Host "     $($_.Exception.Message)"
}

Write-Host ""
Write-Host "完成。开机自启已启用（Automatic）。" -ForegroundColor Cyan
