#requires -Version 5.1
<#
  baihua - Windows + dotnet native CLI
  Cell of the matrix: OS=windows, deployment=dotnet-native
  Manages the 4 .NET services (vault/ai/family/webui) as local processes.

  Usage: .\tools\bh\win\native\bh.ps1 <command> [args]
    build [svc...]      dotnet publish to out/native/（可指定服务，默认全部 4 个）
    start [svc...]      start services（可指定服务，默认全部，按依赖顺序 ai→vault→family→webui）
    stop [svc...]       stop services（可指定服务，默认全部，逆依赖顺序）
    restart [svc...]    stop + start 指定服务（默认全部）
    update              git pull 最新代码 + 重建 + 重启（局域网机器一键升级）
    status              show port/process state per service
    status --json       machine-readable JSON（供 DSH 桥插件）
    logs <svc> [n]      tail service log (default 50 lines)
    dashboard           open browser with cli-token auto-login
    open                open browser to http://localhost:5177
    help                this help
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = 'help',
    [Parameter(Position = 1)]
    [string]$Arg1 = '',
    [Parameter(Position = 2)]
    [string]$Arg2 = '',
    # build/start/stop/restart 可指定多个服务：bh build ai vault
    [Parameter(Position = 3, ValueFromRemainingArguments = $true)]
    [string[]]$MoreArgs = @()
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSCommandPath
$Root = Split-Path -Parent $Root
$Root = Split-Path -Parent $Root
$Root = Split-Path -Parent $Root
$Root = Split-Path -Parent $Root
$OutDir = Join-Path $Root 'out\native'
$PidDir = Join-Path $OutDir 'pids'
$LogDir = Join-Path $OutDir 'logs'
$DataHome = if ($env:BAIHUA_HOME) { $env:BAIHUA_HOME } else { Join-Path $HOME '.baihua' }

# 依赖链：ai → vault → family → webui
#  - vault 的语义搜索（EmbeddingService）经 HTTP 调 AI 服务（/api/embedding/config + embedding API）
#  - family 转发 /mg/* 到 vault、调 AI（BAIHUA_VAULT_URL / BAIHUA_AI_URL）
#  - webui 调 family/ai/vault（FamilyApi/AiApi/VaultApi）
# 数组即启动顺序（被依赖的先启动）；Stop-Services 逆序遍历（依赖者先停）→ webui → family → vault → ai
$Services = @(
    @{ Name = 'ai';     Project = 'services\Baihua.AI';     Exe = 'bh-ai.exe';     Port = 8791 },
    @{ Name = 'vault';  Project = 'services\Baihua.Vault';  Exe = 'bh-vault.exe';  Port = 8790 },
    @{ Name = 'family'; Project = 'services\Baihua.Family'; Exe = 'bh-family.exe'; Port = 8788 },
    @{ Name = 'webui';  Project = 'services\Baihua.Web';    Exe = 'bh-webui.exe';  Port = 5177 }
)

function Help-Text {
    # 只取头部注释里的用法行（4 空格缩进）；否则函数体内的缩进行也会匹配输出
    Get-Content $PSCommandPath | Select-Object -First 20 | Where-Object { $_ -match '^\s{4}[a-z]' } | ForEach-Object { $_.Trim() }
}

function Ensure-Dotnet {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) { return }
    Write-Host '[deps] dotnet 缺失，自动安装（winget install Microsoft.DotNet.SDK.10）...'
    & winget install --id Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements --silent
    if ($LASTEXITCODE -ne 0) {
        Write-Host '[deps] winget 安装失败，请手动安装 .NET SDK 10: https://dotnet.microsoft.com/download'
        exit 1
    }
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    Write-Host '[deps] dotnet 安装完成'
}

function Invoke-Build {
    param([string[]]$Names = @())
    Ensure-Dotnet
    $targets = @(Resolve-ServiceList $Names)
    if ($targets.Count -eq 0) { return }
    if ($targets.Count -eq $Services.Count) {
        Write-Host '[build] all services'
    } else {
        Write-Host "[build] targets: $($targets.Name -join ', ')"
    }
    # 服务进程会锁住自己 out/native/<name> 下的 dll，publish 前必须先停（只停要构建的）
    for ($i = $targets.Count - 1; $i -ge 0; $i--) { Stop-One $targets[$i] }
    foreach ($svc in $targets) {
        if (-not (Wait-PortClosed $svc.Port 15)) {
            Write-Warning "[$($svc.Name)] port $($svc.Port) 15s 内未释放（有残留进程？请检查）"
        }
    }
    foreach ($svc in $targets) {
        Write-Host "[build] $($svc.Name) ..."
        # Project 是相对仓库根的路径，必须 Join-Path $Root（脚本可能从任意目录执行）
        $proj = Join-Path $Root $svc.Project
        $out = & dotnet publish $proj -c Release -r win-x64 --self-contained false -o (Join-Path $OutDir $svc.Name) 2>&1
        if ($LASTEXITCODE -ne 0) { throw "publish failed: $($svc.Name)`n$($out | Select-Object -Last 6)" }
    }
    Write-Host "[build] done -> $OutDir"
}

# 一键更新：git pull 最新代码 → 重新构建 → 重启 + 放行防火墙（局域网算力池入口 8788）
function Update-Services {
    Write-Host '[update] git pull origin main ...'
    git -C $Root pull origin main
    if ($LASTEXITCODE -ne 0) { throw 'git pull 失败，请检查网络/代理' }
    Invoke-Build
    Start-Services
    # Windows 防火墙默认拦截局域网入站：放行 family 8788（算力池/互联入口）——尽力而为，失败不阻断更新
    $rule = netsh advfirewall firewall show rule name='Baihua Family 8788' 2>$null
    if ($LASTEXITCODE -ne 0) {
        netsh advfirewall firewall add rule name='Baihua Family 8788' dir=in action=allow protocol=TCP localport=8788 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host '[update] 已放行防火墙 TCP 8788（局域网算力池/互联入口）'
        } else {
            Write-Warning '[update] 放行防火墙 TCP 8788 失败（需要管理员权限），局域网算力池/互联可能不可达'
        }
    }
    # 清除 netsh 退出码残留，避免被定位器链的 exit $LASTEXITCODE 误判为失败
    $LASTEXITCODE = 0
    Write-Host '[update] done'
}

function Get-PidFile($name) { Join-Path $PidDir "$name.pid" }

function Test-PortOpen($port) {
    $c = New-Object Net.Sockets.TcpClient
    try { $c.Connect('127.0.0.1', $port); return $true } catch { return $false } finally { $c.Dispose() }
}

function Wait-Port($port, $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-PortOpen $port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Wait-PortClosed($port, $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-PortOpen $port)) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

function Wait-ProcessExit($pid2, $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Id $pid2 -ErrorAction SilentlyContinue)) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

# ---- OpenVINO Model Server（Windows SCM 服务 ovms，REST :8000）----
# 独立系统服务：安装/卸载/启停由用户手动（管理员权限）管理
# （scripts/install-openvino-ovms-service.ps1），bh 不参与启停，仅在 status 中展示状态。
$OpenVinoServiceName = 'ovms'
$OpenVinoPort = 8000

function Get-OpenVinoHostService {
    Get-Service -Name $OpenVinoServiceName -ErrorAction SilentlyContinue
}

function Start-One($svc) {
    $exe = Join-Path $OutDir "$($svc.Name)\$($svc.Exe)"
    if (-not (Test-Path $exe)) { throw "not built: $exe (run 'build' first)" }
    if (Test-PortOpen $svc.Port) {
        # 端口被占：若是我们的残留进程（bh-*.exe，restart 时 Stop-Process 未杀透/pid 文件过期），
        # 补杀后重试；否则（外部进程占用）才跳过。
        $conn = Get-NetTCPConnection -LocalPort $svc.Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        $owner = if ($conn) { Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue } else { $null }
        if ($owner -and $owner.ProcessName -like 'bh-*') {
            Write-Warning "[$($svc.Name)] port $($svc.Port) 被残留进程 $($owner.ProcessName)($($owner.Id)) 占用，补杀后重试"
            Stop-Process -Id $owner.Id -Force -ErrorAction SilentlyContinue
            if (-not (Wait-PortClosed $svc.Port 10)) { Write-Warning "[$($svc.Name)] port $($svc.Port) 仍被占用，跳过"; return }
        } else {
            Write-Warning "[$($svc.Name)] port $($svc.Port) already in use, skip"
            return
        }
    }
    # family 是跨机入口（算力池 /mg/capabilities、/mg/ai/、/mg/pool/、服务器互联），
    # 必须绑定 0.0.0.0 才能被局域网内其他百花服务器访问；其余服务保持回环。
    $bind = '127.0.0.1'
    if ($svc.Name -eq 'family') { $bind = '0.0.0.0' }
    $envBlock = @{
        BAIHUA_HOME = $DataHome
        BAIHUA_SKIP_MUTEX = 'true'
        ASPNETCORE_URLS = "http://$bind`:$($svc.Port)"
        OpenObserve__Enabled = 'true'
    }
    # OpenObserve 客户端凭据：优先从 BAIHUA_HOME 的密码文件注入（轮换后不再依赖 appsettings 默认值）
    $ooPassFile = Join-Path $DataHome 'openobserve-password.txt'
    if (Test-Path $ooPassFile) {
        $envBlock['OpenObserve__Password'] = (Get-Content $ooPassFile -Raw).Trim()
    }
    if ($svc.Name -eq 'family') {
        $envBlock['BAIHUA_VAULT_URL'] = 'http://127.0.0.1:8790'
        $envBlock['BAIHUA_AI_URL'] = 'http://127.0.0.1:8791'

        # 算力池跨机鉴权 token：优先从 BAIHUA_HOME 的密钥文件注入（对端 DSH/百花用同值）
        $aiTokenFile = Join-Path $DataHome 'ai-external-token.txt'
        if (Test-Path $aiTokenFile) {
            $envBlock['BAIHUA_AI_EXTERNAL_TOKEN'] = (Get-Content $aiTokenFile -Raw).Trim()
        }
    }
    if ($svc.Name -eq 'webui') {
        $envBlock['WEBUI_CONFIG_DIR'] = $DataHome
        $envBlock['FamilyApi__BaseUrl'] = 'http://127.0.0.1:8788/'
        $envBlock['AiApi__BaseUrl'] = 'http://127.0.0.1:8791/'
        $envBlock['VaultApi__BaseUrl'] = 'http://127.0.0.1:8790/'
    }
    foreach ($k in $envBlock.Keys) { Set-Item -Path ('Env:' + $k) -Value $envBlock[$k] }
    New-Item -ItemType Directory -Force -Path $PidDir, $LogDir | Out-Null
    $logFile = Join-Path $LogDir "$($svc.Name).log"
    $p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" -PassThru -WindowStyle Hidden
    Set-Content -Path (Get-PidFile $svc.Name) -Value $p.Id
    Write-Host "[$($svc.Name)] started pid=$($p.Id) port=$($svc.Port) log=$logFile"
}

function Start-Services {
    New-Item -ItemType Directory -Force -Path $DataHome | Out-Null
    foreach ($svc in $Services) { Start-One $svc }
    Write-Host "[start] waiting for health ..."
    $ok = $true
    foreach ($svc in $Services) {
        if (-not (Wait-Port $svc.Port 60)) { Write-Warning "[$($svc.Name)] port $($svc.Port) not ready in 60s"; $ok = $false }
        else { Write-Host "[$($svc.Name)] ready on $($svc.Port)" }
    }
    if ($ok) { Write-Host "[start] all services up. WebUI: http://localhost:5177" }
}

function Stop-One($svc) {
    $pf = Get-PidFile $svc.Name
    $stopped = $false
    if (Test-Path $pf) {
        $pid2 = [int](Get-Content $pf)
        $proc = Get-Process -Id $pid2 -ErrorAction SilentlyContinue
        if ($proc) {
            Stop-Process -Id $pid2 -Force -ErrorAction SilentlyContinue
            # 等待进程真正退出（TerminateProcess 后进程对象退出/句柄清理有延迟，
            # 不等待的话 restart 立即 Start-One 会误判端口 already in use）
            if (-not (Wait-ProcessExit $pid2 10)) { Write-Warning "[$($svc.Name)] pid $pid2 10s 内未退出" }
            $stopped = $true
        }
        Remove-Item $pf -Force -ErrorAction SilentlyContinue
    }
    if (-not $stopped -and (Test-PortOpen $svc.Port)) {
        # 兜底：pid 文件缺失但端口被占（如进程被外部启动），按端口杀
        $conn = Get-NetTCPConnection -LocalPort $svc.Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($conn) { Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue; Write-Host "[$($svc.Name)] stopped by port pid=$($conn.OwningProcess)" }
    }
}

function Stop-Services {
    # 停止顺序与启动相反：先停依赖者（webui/family），被依赖的（ai/vault）最后停，
    # 避免停止过程中仍有服务在调用已死的下游（如 family 转发 /mg/* 到 vault）。
    for ($i = $Services.Count - 1; $i -ge 0; $i--) { Stop-One $Services[$i] }
    # 等待端口全部释放：进程退出后监听 socket 关闭有延迟，
    # restart/update 紧接着 Start-Services 会误判 already in use 而 skip。
    foreach ($svc in $Services) {
        if (-not (Wait-PortClosed $svc.Port 15)) {
            Write-Warning "[$($svc.Name)] port $($svc.Port) 15s 内未释放（有残留进程？请检查）"
        }
    }
    Write-Host '[stop] done'
}

# 解析单服务参数（vault|ai|family|webui），返回 $true 表示已处理（含报错）
function Resolve-SingleService($name, [ref]$svcRef) {
    if (-not $name) { return $false }
    $svc = $Services | Where-Object { $_.Name -eq $name.ToLower() }
    if (-not $svc) { Write-Host "unknown service: $name (vault|ai|family|webui)" -ForegroundColor Yellow; return $true }
    $svcRef.Value = $svc
    return $true
}

# 解析服务名列表（可多个），按 $Services 依赖顺序返回去重结果；未知名字黄字提示并跳过。
# 空列表 = 全部服务。返回 @() 表示没有可用目标。
function Resolve-ServiceList {
    param([string[]]$Names = @())
    if ($Names.Count -eq 0) { return $Services }
    $found = @()
    $unknown = @()
    foreach ($n in $Names) {
        if ([string]::IsNullOrWhiteSpace($n)) { continue }
        $svc = $Services | Where-Object { $_.Name -eq $n.ToLower() }
        if ($svc) { $found += $svc } else { $unknown += $n }
    }
    if ($unknown.Count -gt 0) {
        Write-Host "unknown service: $($unknown -join ', ') (vault|ai|family|webui)" -ForegroundColor Yellow
    }
    if ($found.Count -eq 0) { return @() }
    # 按 $Services 顺序（依赖先）去重：保证 start 先启动被依赖者
    return @($Services | Where-Object { $_.Name -in ($found.Name) })
}

# 收集命令剩余参数为服务名列表
function Get-ServiceArgs {
    return @($Arg1) + @($Arg2) + $MoreArgs | Where-Object { $_ }
}

function Get-PortOwnerProcess($port) {
    $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $conn) { return $null }
    return Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
}

function Show-Status {
    foreach ($svc in $Services) {
        $portOpen = Test-PortOpen $svc.Port
        $pf = Get-PidFile $svc.Name
        $pidAlive = $false
        if (Test-Path $pf) {
            $pid2 = [int](Get-Content $pf)
            $pidAlive = [bool](Get-Process -Id $pid2 -ErrorAction SilentlyContinue)
        }
        # 端口占用方进程名：区分 release exe（bh-*.exe）与 dotnet run（bh-*.exe 父进程为 dotnet.exe）
        $owner = if ($portOpen) { Get-PortOwnerProcess $svc.Port } else { $null }
        $ownerName = if ($owner) { $owner.ProcessName } else { '' }
        $isReleaseExe = $ownerName -like 'bh-*'
        $isDotnetRun = $false
        if ($owner) {
            try {
                $ownerProc = Get-CimInstance Win32_Process -Filter "ProcessId=$($owner.Id)" -ErrorAction Stop
                $parent = Get-CimInstance Win32_Process -Filter "ProcessId=$($ownerProc.ParentProcessId)" -ErrorAction Stop
                $isDotnetRun = $parent.Name -eq 'dotnet.exe'
            } catch { $isDotnetRun = $false }
        }

        $state = if ($portOpen -and $pidAlive) { 'RUNNING (release)' }
                 elseif ($portOpen -and $isDotnetRun) { 'RUNNING (dotnet run)' }
                 elseif ($portOpen -and $isReleaseExe) { 'RUNNING (release, 外部启动)' }
                 elseif ($portOpen) { "PORT-OPEN (foreign:$ownerName)" }
                 elseif ($pidAlive) { 'PROC-ALIVE' }
                 else { 'stopped' }
        Write-Host ("{0,-8} port={1,-5} {2}" -f $svc.Name, $svc.Port, $state)
    }
    # OpenVINO 宿主（Windows 服务 + 端口）
    $ovSvc = Get-OpenVinoHostService
    $ovState = if ($ovSvc) { $ovSvc.Status.ToString() } else { 'not installed' }
    if (Test-PortOpen $OpenVinoPort) { $ovState = 'RUNNING (port 8000)' }
    Write-Host ("{0,-8} port={1,-5} {2}" -f 'openvino', $OpenVinoPort, $ovState)
}

function Show-StatusJson {
    # 机器可读状态（供 DSH 桥插件 bh_status / 状态卡片消费），schema 对齐 Linux k8s 的 status --json：
    #   cell / namespace / updatedAt / git{head,branch,dirty} /
    #   services[{name,ready,replicas,image,age,restarts,phase,imageCommit,upToDate}] / summary{ready,total}
    $gitHead = 'unknown'; $gitBranch = 'unknown'; $gitDirty = $false
    if (Test-Path (Join-Path $Root '.git')) {
        try {
            $h = git -C $Root rev-parse --short HEAD 2>$null
            if ($h) { $gitHead = ($h | Select-Object -First 1) }
            $b = git -C $Root rev-parse --abbrev-ref HEAD 2>$null
            if ($b) { $gitBranch = ($b | Select-Object -First 1) }
            $d = git -C $Root status --porcelain 2>$null
            if ($d) { $gitDirty = $true }
        } catch { }
    }
    $entries = @()
    foreach ($svc in $Services) {
        $portOpen = Test-PortOpen $svc.Port
        $pf = Get-PidFile $svc.Name
        $pidAlive = $false
        if (Test-Path $pf) {
            $pid2 = [int](Get-Content $pf)
            $pidAlive = [bool](Get-Process -Id $pid2 -ErrorAction SilentlyContinue)
        }
        $running = $portOpen -and $pidAlive
        $phase = if ($running) { 'Running' } elseif ($portOpen) { 'PortOpen' } elseif ($pidAlive) { 'ProcAlive' } else { 'Stopped' }
        $entries += [pscustomobject]@{
            name = $svc.Name
            ready = if ($running) { 1 } else { 0 }
            replicas = 1
            image = 'native'
            age = ''
            restarts = 0
            phase = $phase
            imageCommit = $gitHead
            upToDate = $true
        }
    }
    # OpenVINO 宿主（Windows 服务 + 端口）
    $ovSvc = Get-OpenVinoHostService
    $ovRunning = Test-PortOpen $OpenVinoPort
    $ovPhase = if ($ovRunning) { 'Running' } elseif ($ovSvc) { $ovSvc.Status.ToString() } else { 'not installed' }
    $entries += [pscustomobject]@{
        name = 'openvino'
        ready = if ($ovRunning) { 1 } else { 0 }
        replicas = 1
        image = 'ovms'
        age = ''
        restarts = 0
        phase = $ovPhase
        imageCommit = $gitHead
        upToDate = $true
    }
    $readyTotal = @($entries | Where-Object { $_.ready -eq 1 }).Count
    [pscustomobject]@{
        cell = 'native'
        namespace = 'windows'
        updatedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        git = [pscustomobject]@{ head = $gitHead; branch = $gitBranch; dirty = $gitDirty }
        services = $entries
        summary = [pscustomobject]@{ ready = $readyTotal; total = $entries.Count }
    } | ConvertTo-Json -Depth 5
}

function Show-Logs($svcName, $n) {
    $svc = $Services | Where-Object { $_.Name -eq $svcName }
    if (-not $svc) { Write-Host "unknown service: $svcName (vault|ai|family|webui)"; return }
    $log = Join-Path $LogDir "$svcName.log"
    if (-not (Test-Path $log)) { Write-Host "no log yet: $log"; return }
    # 编码容错：Windows 控制台中文环境（GBK）下 Get-Content -Tail 会因非法 UTF-8 静默返回空，
    # 改为整读 + 严格 UTF-8 解码，失败回退系统 ANSI 代码页。
    # FileShare.ReadWrite：服务进程正在写 log 时 ReadAllBytes 会独占失败。
    $fs = [System.IO.File]::Open($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $bytes = New-Object byte[] ([int]$fs.Length)
        [void]$fs.Read($bytes, 0, $bytes.Length)
    } finally { $fs.Dispose() }
    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    try { $text = $utf8.GetString($bytes) }
    catch { $text = [System.Text.Encoding]::GetEncoding([System.Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage).GetString($bytes) }
    ($text -split "\r?\n") | Where-Object { $_ -ne '' } | Select-Object -Last $n
}

function Open-Dashboard {
    try {
        $resp = Invoke-WebRequest -Uri 'http://127.0.0.1:5177/api/auth/cli-token' -Method POST -UseBasicParsing -TimeoutSec 5
        $token = ($resp.Content | ConvertFrom-Json).token
        Start-Process "http://127.0.0.1:5177/?cli-token=$token"
        Write-Host "[dashboard] opened with cli-token"
    } catch {
        Write-Host "[dashboard] cli-token failed ($($_.Exception.Message)), opening plain URL"
        Start-Process 'http://127.0.0.1:5177'
    }
}

switch ($Command.ToLower()) {
    'build'     { Invoke-Build (Get-ServiceArgs) }
    'start'     {
        if ($Arg1) {
            $targets = Resolve-ServiceList (Get-ServiceArgs)
            if ($targets.Count -eq 0) { break }
            foreach ($svc in $targets) { Start-One $svc; Write-Host "[start] $($svc.Name) starting..." }
            Write-Host "[start] waiting for health ..."
            foreach ($svc in $targets) {
                if (-not (Wait-Port $svc.Port 60)) { Write-Warning "[$($svc.Name)] port $($svc.Port) not ready in 60s" }
                else { Write-Host "[$($svc.Name)] ready on $($svc.Port)" }
            }
        } else { Start-Services }
    }
    'stop'      {
        if ($Arg1) {
            $targets = Resolve-ServiceList (Get-ServiceArgs)
            if ($targets.Count -eq 0) { break }
            # 逆依赖顺序停
            for ($i = $targets.Count - 1; $i -ge 0; $i--) { Stop-One $targets[$i] }
            Write-Host "[stop] done: $($targets.Name -join ', ')"
        } else { Stop-Services }
    }
    'restart'   {
        if ($Arg1) {
            $targets = Resolve-ServiceList (Get-ServiceArgs)
            if ($targets.Count -eq 0) { break }
            for ($i = $targets.Count - 1; $i -ge 0; $i--) { Stop-One $targets[$i] }
            foreach ($svc in $targets) {
                if (-not (Wait-PortClosed $svc.Port 15)) { Write-Warning "[$($svc.Name)] port $($svc.Port) 15s 内未释放" }
            }
            foreach ($svc in $targets) { Start-One $svc }
            Write-Host "[restart] restarting: $($targets.Name -join ', ')"
        } else { Stop-Services; Start-Services }
    }
    'update'    { Update-Services }
    'status'    { if ($Arg1 -eq '--json') { Show-StatusJson } else { Show-Status } }
    'logs'      { $count = 50; if ($Arg2) { $count = [int]$Arg2 }; Show-Logs $Arg1 $count }
    'dashboard' { Open-Dashboard }
    'open'      { Start-Process 'http://127.0.0.1:5177' }
    'help'      { Help-Text }
    default     { Help-Text }
}

# 正常完成统一退出 0（错误路径已由 throw / exit 1 提前结束）
# 避免残留 $LASTEXITCODE 沿定位器链（native -> tools/bh/bh.ps1 -> ~/.local/bin/bh.ps1）的 exit $LASTEXITCODE 传播，把成功误报为失败
exit 0

