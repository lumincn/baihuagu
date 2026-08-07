<#
百花 Family 版 - Windows (PowerShell) 轻量 CLI
用法: .\bh.ps1 [command]
  bh.ps1                 打开 dashboard（自动检测代码更新，有新提交时重编译重启）
  bh.ps1 setup           首次配置（交互）
  bh.ps1 start           启动服务（在后台运行 dotnet run）
  bh.ps1 stop            停止服务
  bh.ps1 status          查看服务状态
  bh.ps1 restart         重启服务
  bh.ps1 logs [name]     查看日志（taskrunner, webui, ai, vault）
  bh.ps1 open            打开 Web 管理界面 (http://localhost:5177)
  bh.ps1 dev             开发模式（dotnet watch，改代码自动热重载）
  bh.ps1 observe         启动 OpenObserve 可观测平台（Docker）并打开 Web UI
  bh.ps1 all             启动全部服务（.NET 服务 + OpenObserve + hostmetrics）

说明:
- 该脚本为简易移植，依赖 PowerShell (推荐 pwsh) 和 dotnet SDK
- 后台进程 PID 与日志保存在 $env:TEMP\bh-[service].*
- dashboard 命令会比较当前 git HEAD 与上次启动时的 commit，不同则自动重编译重启
- dev 命令用 dotnet watch run 启动每个服务（Debug 配置），改 .cs/.razor 自动热重载/重启，不依赖自定义文件监听
- observe 命令使用 docker compose 启动 OpenObserve（端口 5082/5083）
- all 命令启动所有 .NET 服务（ai, vault, taskrunner, webui）和 Docker 监控容器（openobserve, hostmetrics）
#>
param(
	[string]$Command = 'dashboard',
	[string]$Arg,
	[string]$Browser = ''
)

chcp 65001 | Out-Null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

function Get-Help {
	Write-Host ""
	Write-Host "百花 Family 版 - Windows (PowerShell) 轻量 CLI" -ForegroundColor Cyan
	Write-Host "================================================="
	Write-Host ""
	Write-Host "用法: .\bh.ps1 [command]"
	Write-Host ""
	Write-Host "Commands:"
	Write-Host "  dashboard             打开管理面板（默认，自动检测更新重编译）"
	Write-Host "  setup                 首次配置（交互）"
	Write-Host "  start                 启动服务（后台运行 dotnet run）"
	Write-Host "  stop                  停止服务"
	Write-Host "  restart               重启服务"
	Write-Host "  status                查看服务状态"
	Write-Host "  logs [name]           查看日志（taskrunner, webui, ai, vault）"
	Write-Host "  open                  打开 Web 管理界面 (http://localhost:5177)"
	Write-Host "  dev                   开发模式（dotnet watch，改代码自动热重载）"
	Write-Host "  observe               启动 OpenObserve 可观测平台（Docker）"
	Write-Host "  all                   启动全部服务（.NET + OpenObserve + hostmetrics）"
	Write-Host ""
	Write-Host "说明:"
	Write-Host "  - 日志与 PID 文件保存在 $env:TEMP\bh-[service].*"
	Write-Host "  - dashboard 命令会比较 git HEAD，有更新时自动重编译重启"
	Write-Host ""
}

function Get-HgRoot {
	if ($PSScriptRoot) { return $PSScriptRoot }
	if ($MyInvocation -and $MyInvocation.MyCommand -and $MyInvocation.MyCommand.Path) {
		return Split-Path -Parent $MyInvocation.MyCommand.Path
	}
	return (Get-Location).Path
}

$HG_ROOT = Get-HgRoot
$TEMP_DIR = $env:TEMP

# 启动顺序：被依赖的先启动（AI → Vault → TaskRunner → WebUI）
$ServiceOrder = @('ai', 'vault', 'taskrunner', 'webui')
# 停止顺序：依赖别人的先停止（WebUI → TaskRunner → Vault → AI）
$StopOrder = @('webui', 'taskrunner', 'vault', 'ai')
$Services = @{ 
	ai         = "services/Baihua.AI";
	vault      = "services/Baihua.Vault";
	taskrunner = "services/Baihua.Family";
	webui      = "services/Baihua.Web";
}

# 服务健康检查 URL（用轻量端点，避免认证拦截）
$HealthUrls = @{
	ai         = 'http://127.0.0.1:8791/api/ai/config/providers'
	vault      = 'http://127.0.0.1:8790/mg/vaults'
	taskrunner = 'http://127.0.0.1:8788/api/capability'
	webui      = 'http://127.0.0.1:5177/login'
}

$ServicePorts = @{
	ai         = 8791
	vault      = 8790
	taskrunner = 8788
	webui      = 5177
}

function Get-LogPath($name){ Join-Path $TEMP_DIR "bh-$name.log" }
function Get-PidPath($name){ Join-Path $TEMP_DIR "bh-$name.pid" }
function Get-CommitPath{ Join-Path $TEMP_DIR "bh-git-commit.txt" }

function Get-CurrentGitCommit{
	try {
		$commit = git -C $HG_ROOT rev-parse HEAD 2>$null
		if ($commit) { return $commit.Trim() }
	} catch {}
	return $null
}

function Get-SavedGitCommit{
	$path = Get-CommitPath
	if (Test-Path $path) {
		$content = Get-Content $path -ErrorAction SilentlyContinue
		if ($content) { return $content.Trim() }
	}
	return $null
}

function Save-GitCommit{
	$commit = Get-CurrentGitCommit
	if ($commit) {
		Set-Content -Path (Get-CommitPath) -Value $commit -Force
	}
}

function Test-NeedsRebuild{
	$current = Get-CurrentGitCommit
	$saved = Get-SavedGitCommit
	if (-not $current) { return $false }
	if (-not $saved) { return $true }
	if ($current -ne $saved) { return $true }
	try {
		$dirty = git -C $HG_ROOT status --short 2>$null
		if ($dirty -and $dirty.Trim().Length -gt 0) { return $true }
	} catch {}
	return $false
}

function Start-ServiceProc($name, $projRelPath, $preferConfig = 'Release'){
	$projPath = Join-Path $HG_ROOT $projRelPath
	if (-not (Test-Path $projPath)){
		Write-Host "[!] 项目未找到: $projPath" -ForegroundColor Yellow
		return
	}
	$log = Get-LogPath $name
	$errLog = "$log.err"
	$pidFile = Get-PidPath $name

	if (Test-Path $pidFile) {
		$existingPid = Get-Content $pidFile -ErrorAction SilentlyContinue
		if ($existingPid -and (Get-Process -Id $existingPid -ErrorAction SilentlyContinue)) {
			Write-Host "[INFO] $name is already running (PID $existingPid)"
			return
		} else {
			Remove-Item $pidFile -ErrorAction SilentlyContinue
		}
	}

	$port = $ServicePorts[$name]
	if ($port) {
		$portProc = netstat -ano 2>$null | Select-String ":${port}\s" | Select-String "LISTENING"
		if ($portProc) {
			Write-Host "[WARN] Port :${port} already in use, attempting to free..." -ForegroundColor Yellow
			Stop-ServiceByPort $name
			Start-Sleep -Seconds 1
		}
	}

	Write-Host "Starting $name -> $projPath"
	$args = @('run', '--project', "$projPath", '--no-launch-profile')

	try {
		$prevEnv = $env:ASPNETCORE_ENVIRONMENT
		$env:ASPNETCORE_ENVIRONMENT = 'Development'
		# ⚠️ 启动优先用 preferConfig 对应的 exe（默认 Release），否则改了代码服务跑旧版
		# 2026-08-06 踩坑：dotnet build -c Release 编译，但这里只查 bin\Debug → 修复没生效
		# dev 模式传 preferConfig='Debug'，优先 Debug 产物（编译快、符号全）
		$exePath = Join-Path $projPath "bin\$preferConfig\net10.0\bh-$name.exe"
		if (-not (Test-Path $exePath)) {
			$fallback = if ($preferConfig -eq 'Release') { 'Debug' } else { 'Release' }
			$exePath = Join-Path $projPath "bin\$fallback\net10.0\bh-$name.exe"
		}
		if (Test-Path $exePath) {
			$port = $ServicePorts[$name]
			if ($port) { $env:ASPNETCORE_URLS = "http://0.0.0.0:$port" }
			$proc = Start-Process -FilePath $exePath -RedirectStandardOutput $log -RedirectStandardError $errLog -NoNewWindow -PassThru
		} else {
			$proc = Start-Process -FilePath 'dotnet' -ArgumentList $args -RedirectStandardOutput $log -RedirectStandardError $errLog -NoNewWindow -PassThru
		}
		if ($null -ne $prevEnv) { $env:ASPNETCORE_ENVIRONMENT = $prevEnv } else { Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue }
		Start-Sleep -Milliseconds 200
		$procId = $proc.Id
		Set-Content -Path $pidFile -Value $procId
		Write-Host "Started $name (PID $procId), log: $log (stderr: $errLog)"
	} catch {
		Write-Host "ERROR: failed to start ${name}: ${_}"
	}
}

function Start-WatchProc($name, $projRelPath){
	$projPath = Join-Path $HG_ROOT $projRelPath
	if (-not (Test-Path $projPath)){
		Write-Host "[!] 项目未找到: $projPath" -ForegroundColor Yellow
		return
	}
	$log = Get-LogPath $name
	$errLog = "$log.err"
	$pidFile = Get-PidPath $name

	# 清理旧 PID 文件（若进程已死）
	if (Test-Path $pidFile) {
		$existingPid = Get-Content $pidFile -ErrorAction SilentlyContinue
		if ($existingPid -and (Get-Process -Id $existingPid -ErrorAction SilentlyContinue)) {
			Write-Host "[INFO] $name 已在运行 (PID $existingPid)"
			return
		} else {
			Remove-Item $pidFile -ErrorAction SilentlyContinue
		}
	}

	$port = $ServicePorts[$name]
	if ($port) {
		$portProc = netstat -ano 2>$null | Select-String ":${port}\s" | Select-String "LISTENING"
		if ($portProc) {
			Write-Host "[WARN] Port :${port} 被占用，尝试释放..." -ForegroundColor Yellow
			Stop-ServiceByPort $name
			Start-Sleep -Seconds 1
		}
	}

	Write-Host "Starting $name (dotnet watch) -> $projPath"
	$prevEnv = $env:ASPNETCORE_ENVIRONMENT
	$env:ASPNETCORE_ENVIRONMENT = 'Development'
	if ($port) { $env:ASPNETCORE_URLS = "http://0.0.0.0:$port" }

	# dotnet watch run：监听项目源文件，热重载/自动重启
	# --non-interactive 防止等待键盘输入（后台运行时必须）
	# --no-launch-profile 忽略 launchSettings.json（端口由 ASPNETCORE_URLS 控制）
	$watchArgs = @('watch', 'run', '--project', "$projPath", '--no-launch-profile', '--non-interactive', '-c', 'Debug')
	$proc = Start-Process -FilePath 'dotnet' -ArgumentList $watchArgs -RedirectStandardOutput $log -RedirectStandardError $errLog -NoNewWindow -PassThru
	if ($null -ne $prevEnv) { $env:ASPNETCORE_ENVIRONMENT = $prevEnv } else { Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue }
	Start-Sleep -Milliseconds 300
	Set-Content -Path $pidFile -Value $proc.Id
	Write-Host "Started $name (watch PID $($proc.Id)), log: $log (stderr: $errLog)"
}

function Stop-ServiceProc($name){
	$pidFile = Get-PidPath $name
	if (-not (Test-Path $pidFile)){
		Write-Host "[i] $name 未运行 (无 PID 文件)" -ForegroundColor Yellow
		return
	}
	$existingPid = Get-Content $pidFile -ErrorAction SilentlyContinue
	if ($existingPid -and (Get-Process -Id $existingPid -ErrorAction SilentlyContinue)) {
		try {
			# 优先杀进程树（dotnet watch 会 spawn 子进程，只杀父进程会残留）
			$isWatch = (Get-Process -Id $existingPid -ErrorAction SilentlyContinue).ProcessName -eq 'dotnet'
			if ($isWatch) {
				taskkill /T /F /PID $existingPid 2>$null | Out-Null
			} else {
				Stop-Process -Id $existingPid -Force -ErrorAction Stop
			}
			$sw = [System.Diagnostics.Stopwatch]::StartNew()
			while ((Get-Process -Id $existingPid -ErrorAction SilentlyContinue) -and $sw.Elapsed.TotalSeconds -lt 10) {
				Start-Sleep -Milliseconds 200
			}
			Remove-Item $pidFile -ErrorAction SilentlyContinue
			if (Get-Process -Id $existingPid -ErrorAction SilentlyContinue) {
				Write-Host "Stopped ${name} (PID $existingPid) - process still exiting..." -ForegroundColor Yellow
			} else {
				Write-Host "Stopped ${name} (PID $existingPid)"
			}
		} catch {
			Write-Host "ERROR: failed to stop ${name} by PID: ${_}" -ForegroundColor Red
			Stop-ServiceByPort $name
		}
	} else {
		Remove-Item $pidFile -ErrorAction SilentlyContinue
		Write-Host "$name is not running (cleaned pidfile)"
	}
}

function Stop-ServiceByPort($name){
	$port = $ServicePorts[$name]
	if (-not $port) { return }
	$connections = netstat -ano 2>$null | Select-String ":${port}\s" | Select-String "LISTENING"
	foreach ($conn in $connections) {
		$parts = $conn.ToString().Trim() -split '\s+'
		$foundPid = $parts[-1]
		if ($foundPid -match '^\d+$' -and $foundPid -ne '0') {
			try {
				$proc = Get-Process -Id $foundPid -ErrorAction Stop
				if ($proc.ProcessName -eq 'svchost') {
					Write-Host "  Port :${port} held by svchost (PID $foundPid), skipping" -ForegroundColor Yellow
					continue
				}
				Write-Host "  Killing process on port :${port} (PID $foundPid, $($proc.ProcessName))" -ForegroundColor Yellow
				Stop-Process -Id $foundPid -Force -ErrorAction Stop
				Start-Sleep -Milliseconds 500
			} catch {
				Write-Host "  Failed to kill PID $foundPid on port :${port}: ${_}" -ForegroundColor Red
			}
		}
	}
	Remove-Item (Get-PidPath $name) -ErrorAction SilentlyContinue
}

function Get-RealServicePid($name){
	$port = $ServicePorts[$name]
	if (-not $port) { return $null }
	$connections = netstat -ano 2>$null | Select-String ":${port}\s" | Select-String "LISTENING"
	foreach ($conn in $connections) {
		$parts = $conn.ToString().Trim() -split '\s+'
		$foundPid = $parts[-1]
		if ($foundPid -match '^\d+$' -and $foundPid -ne '0') {
			try {
				$proc = Get-Process -Id $foundPid -ErrorAction Stop
				if ($proc.ProcessName -ne 'svchost') { return $foundPid }
			} catch {}
		}
	}
	return $null
}

function Test-ServiceRunning($name){
	$pidFile = Get-PidPath $name
	if (Test-Path $pidFile) {
		$existingPid = Get-Content $pidFile -ErrorAction SilentlyContinue
		if ($existingPid -and (Get-Process -Id $existingPid -ErrorAction SilentlyContinue)) {
			$proc = Get-Process -Id $existingPid -ErrorAction SilentlyContinue
			if ($proc.ProcessName -eq 'svchost') {
				Remove-Item $pidFile -ErrorAction SilentlyContinue
				Write-Host "  $name : stale PID (svchost), cleaning" -ForegroundColor Yellow
			} else {
				return $true
			}
		} else {
			Remove-Item $pidFile -ErrorAction SilentlyContinue
		}
	}
	$realPid = Get-RealServicePid $name
	if ($realPid) {
		Set-Content -Path $pidFile -Value $realPid -Force
		return $true
	}
	return $false
}

function Show-Status(){
	foreach ($k in $ServiceOrder){
		$status = "$k : stopped" 
		$color = [ConsoleColor]::DarkYellow
		if (Test-ServiceRunning $k){
			$pidFile = Get-PidPath $k
			$existingPid = Get-Content $pidFile -ErrorAction SilentlyContinue
			$healthUrl = $HealthUrls[$k]
			$healthy = $false
			if ($healthUrl) {
				try {
					$resp = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
					$healthy = $resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400
				} catch { $healthy = $false }
			}
			if ($healthy) {
				$status = "$k : running (PID $existingPid) ✓ healthy"
				$color = [ConsoleColor]::Green
			} else {
				$status = "$k : running (PID $existingPid) ⚠ not ready"
				$color = [ConsoleColor]::Yellow
			}
		}
		Write-Host $status -ForegroundColor $color
	}
}

function Tail-Log($name){
	$log = Get-LogPath $name
	$errLog = "$log.err"
	if (-not (Test-Path $log)) { Write-Host "Log not found: $log"; if (Test-Path $errLog){ Write-Host "But stderr exists: $errLog" }; return }
	Write-Host "Tailing log: $log (Ctrl+C to stop)"
	if (Test-Path $errLog) { Write-Host "Also monitoring stderr: $errLog" }
	Get-Content -Path $log -Tail 50 -Wait -Encoding UTF8
}

function Cmd-Setup {
	Write-Host "*** 首次配置向导（简化）" -ForegroundColor Cyan
	$vault = Read-Host "Enter vault path (e.g. C:\Users\you\MyNotes)"
	if (-not [string]::IsNullOrWhiteSpace($vault)) {
		if (-not (Test-Path $vault)) { New-Item -ItemType Directory -Path $vault -Force | Out-Null; Write-Host "Created: $vault" }
		$cfgPath = Join-Path $HG_ROOT 'local.config.json'
		$obj = @{ vault = $vault }
		$obj | ConvertTo-Json | Set-Content -Path $cfgPath -Encoding UTF8
		Write-Host "Saved config: $cfgPath"
	} else { Write-Host "Vault not set, abort." }
}

function Open-InBrowser([string]$url){
	if ($Browser) {
		Write-Host "Opening: $url (browser: $Browser)"
		try { Start-Process $Browser $url } catch { Write-Host "Cannot launch browser '${Browser}': ${_}" }
	} else {
		Write-Host "Opening: $url"
		try { Start-Process $url } catch { Write-Host "Cannot open browser: ${_}" }
	}
}

function Open-Dashboard {
	Open-InBrowser 'http://127.0.0.1:5177'
}

function Ensure-ServiceRunning($name){
	if (Test-ServiceRunning $name) {
		Write-Host "Service $name already running"
		return $true
	}
	Write-Host "Service $name not running, starting..."
	Start-ServiceProc $name $Services[$name]
	return $false
}

function Test-TcpPort([string]$hostname, [int]$port, [int]$timeoutMs = 2000){
	try {
		$tcp = New-Object System.Net.Sockets.TcpClient
		$async = $tcp.BeginConnect($hostname, $port, $null, $null)
		$wait = $async.AsyncWaitHandle.WaitOne($timeoutMs, $false)
		if ($wait -and $tcp.Connected) { $tcp.Close(); return $true }
		$tcp.Close(); return $false
	} catch { return $false }
}

function Wait-For-Url([string]$url, [int]$timeoutSec = 30){
	$sw = [System.Diagnostics.Stopwatch]::StartNew()
	$firstAttempt = $true
	while ($sw.Elapsed.TotalSeconds -lt $timeoutSec){
		try{
			$resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
			if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400){
				return $true
			}
		} catch {
			if ($firstAttempt) {
				Write-Host "  (waiting for $url ...)" -ForegroundColor DarkGray
				$firstAttempt = $false
			}
		}
		Start-Sleep -Seconds 2
	}
	return $false
}

function Wait-For-Service([string]$name, [int]$timeoutSec = 20, [bool]$wasJustStarted = $false){
	$healthUrl = $HealthUrls[$name]
	if (-not $healthUrl) { 
		Write-Host "  $name : no health check URL, skipping wait"
		return $true 
	}
	# 仅对新启动的服务等待 5 秒让 dotnet run 进程稳定（编译+启动）
	# 已运行的服务直接做健康检查，无需等待
	if ($wasJustStarted) {
		Start-Sleep -Seconds 3
	}
	$sw = [System.Diagnostics.Stopwatch]::StartNew()
	$crashCheckDone = $false
	while ($sw.Elapsed.TotalSeconds -lt $timeoutSec){
		if (-not $crashCheckDone) {
			$realPid = Get-RealServicePid $name
			if ($realPid) {
				$pidFile = Get-PidPath $name
				Set-Content -Path $pidFile -Value $realPid -Force
			} else {
				$pidFile = Get-PidPath $name
				if (Test-Path $pidFile) {
					$srvPid = Get-Content $pidFile -ErrorAction SilentlyContinue
					if ($srvPid -and -not (Get-Process -Id $srvPid -ErrorAction SilentlyContinue)) {
						Write-Host "  $name : ✗ process crashed" -ForegroundColor Red
						$errLog = "$(Get-LogPath $name).err"
						if (Test-Path $errLog) {
							Write-Host "  Last 5 lines of error log:" -ForegroundColor Yellow
							Get-Content $errLog -Tail 5 -Encoding UTF8 | ForEach-Object { Write-Host "    $_" }
						}
						return $false
					}
				}
			}
			$crashCheckDone = $true
		}
		try{
			$resp = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
			if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500){
				Write-Host "  $name : ✓ ready" -ForegroundColor Green
				return $true
			}
		} catch {
			# retry
		}
		Write-Host "." -NoNewline
		Start-Sleep -Seconds 1
	}
	Write-Host ""
	Write-Host "  $name : ⚠ timeout after ${timeoutSec}s" -ForegroundColor Yellow
	return $false
}

function Invoke-BuildIfNeeded {
	$needsRebuild = Test-NeedsRebuild
	if ($needsRebuild) {
		$curr = Get-CurrentGitCommit
		$saved = Get-SavedGitCommit
		Write-Host "[i] 检测到代码更新" -ForegroundColor Yellow
		if ($saved) {
			Write-Host "    上次: $($saved.Substring(0,8))"
			Write-Host "    当前: $($curr.Substring(0,8))"
		}
		Write-Host "[...] dotnet build..." -ForegroundColor Cyan
		$buildResult = dotnet build (Join-Path $HG_ROOT 'services\BaiHua.slnx') -c Release 2>&1
		$buildExit = $LASTEXITCODE
		if ($buildExit -ne 0) {
			Write-Host "[X] 编译失败!" -ForegroundColor Red
			$buildResult | Select-Object -Last 10 | ForEach-Object { Write-Host "    $_" }
			return $false
		}
		Write-Host "[v] 编译成功" -ForegroundColor Green
		Save-GitCommit
	}
	return $true
}

function Cmd-Start {
	if (-not (Invoke-BuildIfNeeded)) { return }
	foreach ($k in $ServiceOrder){
		Start-ServiceProc $k $Services[$k]
		Write-Host "  $k : " -NoNewline
		if ($k -eq 'webui') {
			Start-Sleep -Seconds 3
			if (Wait-For-Url 'http://127.0.0.1:5177/login' 30) {
				Write-Host "ready" -ForegroundColor Green
			} else {
				Write-Host "not ready" -ForegroundColor Red
			}
		} else {
			Wait-For-Service $k 30 -wasJustStarted $true | Out-Null
		}
	}
	if (-not (Test-NeedsRebuild)) { Save-GitCommit }
}

function Cmd-Stop {
	if ($Arg) {
		$name = $Arg.ToLower()
		if ($Services.ContainsKey($name)) {
			Stop-ServiceProc $name
		} else {
			Write-Host "未知服务: $name（可选: $($ServiceOrder -join ', ')）" -ForegroundColor Yellow
		}
		return
	}
	foreach ($k in $StopOrder){ Stop-ServiceProc $k }
}

function Cmd-Observe {
	$composeFile = Join-Path $HG_ROOT 'docker\docker-compose.observability.yml'
	if (-not (Test-Path $composeFile)) {
		Write-Host "[!] docker-compose.observability.yml not found: $composeFile" -ForegroundColor Red
		return
	}
	$dockerCmd = $null
	foreach ($cmd in @('docker', 'docker.exe')) {
		try { Get-Command $cmd -ErrorAction Stop | Out-Null; $dockerCmd = $cmd; break } catch {}
	}
	if (-not $dockerCmd) {
		Write-Host "[!] Docker not found. Install Docker Desktop first." -ForegroundColor Red
		return
	}
	try {
		$null = & $dockerCmd info 2>&1
	} catch {
		Write-Host "[!] Docker daemon not running. Start Docker Desktop first." -ForegroundColor Red
		return
	}
	Write-Host "Starting OpenObserve..." -ForegroundColor Cyan
	& $dockerCmd compose -f $composeFile up -d openobserve 2>&1 | ForEach-Object { Write-Host "    $_" }
	if ($LASTEXITCODE -ne 0) {
		Write-Host "[X] Failed to start OpenObserve" -ForegroundColor Red
		return
	}
	if (Test-TcpPort '127.0.0.1' 5082) {
		Write-Host "OpenObserve already running at http://127.0.0.1:5082" -ForegroundColor Green
		Open-InBrowser 'http://127.0.0.1:5082'
		return
	}
	Write-Host "Waiting for OpenObserve to be ready..." -ForegroundColor DarkGray
	$sw = [System.Diagnostics.Stopwatch]::StartNew()
	while ($sw.Elapsed.TotalSeconds -lt 60) {
		if (Test-TcpPort '127.0.0.1' 5082) {
			Write-Host "OpenObserve ready at http://127.0.0.1:5082" -ForegroundColor Green
			Open-InBrowser 'http://127.0.0.1:5082'
			return
		}
		Start-Sleep -Seconds 2
	}
	Write-Host "[!] OpenObserve not responding on port 5082 after 60s" -ForegroundColor Yellow
	Write-Host "    Check: docker logs bh-openobserve" -ForegroundColor Yellow
}

function Cmd-Start-Observability {
	$composeFile = Join-Path $HG_ROOT 'docker\docker-compose.observability.yml'
	if (-not (Test-Path $composeFile)) {
		Write-Host "[!] docker-compose.observability.yml not found: $composeFile" -ForegroundColor Red
		return $false
	}
	$dockerCmd = $null
	foreach ($cmd in @('docker', 'docker.exe')) {
		try { Get-Command $cmd -ErrorAction Stop | Out-Null; $dockerCmd = $cmd; break } catch {}
	}
	if (-not $dockerCmd) {
		Write-Host "  Docker: ⚠ not found, skipping observability" -ForegroundColor Yellow
		return $false
	}
	try {
		$null = & $dockerCmd info 2>&1
	} catch {
		Write-Host "  Docker: ⚠ daemon not running, skipping observability" -ForegroundColor Yellow
		return $false
	}
	Write-Host "  Starting OpenObserve + hostmetrics..." -ForegroundColor Cyan
	& $dockerCmd compose -f $composeFile up -d 2>&1 | ForEach-Object { Write-Host "    $_" }
	if ($LASTEXITCODE -ne 0) {
		Write-Host "  Docker: ⚠ failed (network issue or image not available)" -ForegroundColor Yellow
		Write-Host "  Docker:   Try again later, or start manually: docker compose -f docker/docker-compose.observability.yml up -d" -ForegroundColor DarkGray
		return $false
	}
	return $true
}

function Cmd-All {
	Write-Host "=== 百花 - 启动全部服务 ===" -ForegroundColor Cyan
	Write-Host ""

	$needsRebuild = Test-NeedsRebuild
	if ($needsRebuild) {
		$curr = Get-CurrentGitCommit
		$saved = Get-SavedGitCommit
		Write-Host "[i] 检测到代码更新" -ForegroundColor Yellow
		if ($saved) {
			Write-Host "    上次: $($saved.Substring(0,8))"
			Write-Host "    当前: $($curr.Substring(0,8))"
		}
		Write-Host "[...] 停止旧服务并重新编译..." -ForegroundColor Cyan
		Cmd-Stop
		Start-Sleep -Seconds 1

		Write-Host "[...] dotnet build..." -ForegroundColor Cyan
		$buildResult = dotnet build (Join-Path $HG_ROOT 'services\BaiHua.slnx') -c Release 2>&1
		$buildExit = $LASTEXITCODE
		if ($buildExit -ne 0) {
			Write-Host "[X] 编译失败!" -ForegroundColor Red
			$buildResult | Select-Object -Last 10 | ForEach-Object { Write-Host "    $_" }
			return
		}
		Write-Host "[v] 编译成功" -ForegroundColor Green
		Save-GitCommit
	}

	foreach ($name in $ServiceOrder) {
		Test-ServiceRunning $name | Out-Null
	}

	Write-Host ""
	Write-Host "[1/2] 启动 .NET 服务..." -ForegroundColor Cyan
	$failedServices = @()
	foreach ($name in @('ai', 'vault', 'taskrunner')) {
		$wasRunning = Ensure-ServiceRunning $name
		Write-Host "  $name : " -NoNewline
		if (-not (Wait-For-Service $name 30 -wasJustStarted:(-not $wasRunning))) { $failedServices += $name }
	}

	$webuiWasRunning = Ensure-ServiceRunning 'webui'
	Write-Host "  webui : " -NoNewline
	if (-not $webuiWasRunning) { Start-Sleep -Seconds 3 }
	if (-not (Wait-For-Url 'http://127.0.0.1:5177/login' 20)){
		Write-Host "X not ready" -ForegroundColor Red
		$failedServices += 'webui'
	} else {
		Write-Host "v ready" -ForegroundColor Green
	}

	if (-not $needsRebuild) { Save-GitCommit }

	Write-Host ""
	Write-Host "[2/2] 启动可观测性服务 (Docker)..." -ForegroundColor Cyan
	if (Cmd-Start-Observability) {
		if (Test-TcpPort '127.0.0.1' 5082) {
			Write-Host "  OpenObserve: v running at http://127.0.0.1:5082" -ForegroundColor Green
		} else {
			Write-Host "  OpenObserve: ⚠ starting..." -ForegroundColor Yellow
		}
	}

	Write-Host ""
	try {
		$resp = Invoke-WebRequest -Uri 'http://127.0.0.1:5177/api/auth/cli-token' -Method POST -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
		$json = $resp.Content | ConvertFrom-Json
		$token = $json.token
		if ($token) {
			$dashboardUrl = "http://127.0.0.1:5177/?cli-token=$token"
			Write-Host "Opening dashboard with CLI token..."
			Open-InBrowser $dashboardUrl
		} else {
			Open-Dashboard
		}
	} catch {
		Write-Host "Failed to get CLI token, opening without auto-login"
		Open-Dashboard
	}

	if ($failedServices.Count -gt 0) {
		Write-Host ""
		Write-Host "! Some services failed: $($failedServices -join ', ')" -ForegroundColor Yellow
		Write-Host "  Check logs: .\bh.ps1 logs <name>" -ForegroundColor Yellow
	}
}

switch ($Command.ToLower()){
	'help' { Get-Help; break }
	'setup' { Cmd-Setup; break }
	'start' {
		Cmd-Start
		break
	}
	'stop' {
		Cmd-Stop
		break
	}
	'restart' {
		Cmd-Stop
		Write-Host "Waiting for ports to release..."
		Start-Sleep -Seconds 1
		if (-not (Invoke-BuildIfNeeded)) { break }
		Cmd-Start
		break
	}
	'status' { Show-Status; break }
	'logs' {
		if (-not $Arg){ Write-Host "请指定服务名: taskrunner, webui, ai, vault" -ForegroundColor Yellow; break }
		Tail-Log $Arg; break
	}
	'open' { Open-Dashboard; break }
	'observe' { Cmd-Observe; break }
	'all' { Cmd-All; break }
	'dashboard' {
		Write-Host "=== 百花 Dashboard ===" -ForegroundColor Cyan

		# 检测是否需要重新编译
		$needsRebuild = Test-NeedsRebuild
		if ($needsRebuild) {
			$curr = Get-CurrentGitCommit
			$saved = Get-SavedGitCommit
			Write-Host ""
			if ($saved) {
				Write-Host "[i] 检测到代码更新" -ForegroundColor Yellow
				Write-Host "    上次: $($saved.Substring(0,8))"
				Write-Host "    当前: $($curr.Substring(0,8))"
			} else {
				Write-Host "[i] 首次运行或无构建记录" -ForegroundColor Yellow
			}
			Write-Host "[...] 停止旧服务并重新编译..." -ForegroundColor Cyan
			Cmd-Stop
			Write-Host "Waiting for ports to release..."
			Start-Sleep -Seconds 1

			Write-Host "[...] dotnet build..." -ForegroundColor Cyan
			$buildResult = dotnet build (Join-Path $HG_ROOT 'services\BaiHua.slnx') -c Release 2>&1
			$buildExit = $LASTEXITCODE
			if ($buildExit -ne 0) {
				Write-Host "[X] 编译失败!" -ForegroundColor Red
				$buildResult | Select-Object -Last 10 | ForEach-Object { Write-Host "    $_" }
				break
			}
			Write-Host "[v] 编译成功" -ForegroundColor Green
			Save-GitCommit
		}

		# 清理僵尸进程 & 修正 PID
		foreach ($name in $ServiceOrder) {
			Test-ServiceRunning $name | Out-Null
		}

		# 按顺序启动并等待每个后端服务就绪
		Write-Host ""
		$failedServices = @()
		foreach ($name in @('ai', 'vault', 'taskrunner')) {
			$wasRunning = Ensure-ServiceRunning $name
			Write-Host "  $name : " -NoNewline
			if (-not (Wait-For-Service $name 30 -wasJustStarted:(-not $wasRunning))) { $failedServices += $name }
		}

		# 启动 WebUI
		$webuiWasRunning = Ensure-ServiceRunning 'webui'
		Write-Host "  webui : " -NoNewline
		if (-not $webuiWasRunning) { Start-Sleep -Seconds 3 }
		if (-not (Wait-For-Url 'http://127.0.0.1:5177/login' 20)){
			Write-Host "  webui : X not ready. Check: .\bh.ps1 logs webui" -ForegroundColor Red
			$failedServices += 'webui'
		} else {
			Write-Host "  webui : v ready" -ForegroundColor Green
		}

		# 首次启动保存 commit
		if (-not $needsRebuild) { Save-GitCommit }

		# 获取 CLI token 并打开浏览器
		Write-Host ""
		try {
			$resp = Invoke-WebRequest -Uri 'http://127.0.0.1:5177/api/auth/cli-token' -Method POST -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
			$json = $resp.Content | ConvertFrom-Json
			$token = $json.token
			if ($token) {
				$dashboardUrl = "http://127.0.0.1:5177/?cli-token=$token"
				Write-Host "Opening dashboard with CLI token..."
				Open-InBrowser $dashboardUrl
			} else {
				Open-Dashboard
			}
		} catch {
			Write-Host "Failed to get CLI token, opening without auto-login"
			Open-Dashboard
		}

		if ($failedServices.Count -gt 0) {
			Write-Host ""
			Write-Host "! Some services failed: $($failedServices -join ', ')" -ForegroundColor Yellow
			Write-Host "  Check logs: .\bh.ps1 logs <name>" -ForegroundColor Yellow
		}
		break
	}
	default { Open-Dashboard }
	'dev' {
		Write-Host "=== 百花 Dev Mode (dotnet watch, auto hot-reload) ===" -ForegroundColor Cyan
		Write-Host "  Watching: each service project (.cs/.razor, native dotnet watch)" -ForegroundColor DarkGray
		Write-Host "  Press Ctrl+C to stop" -ForegroundColor DarkGray
		Write-Host ""

		# 停止旧服务，清理 PID/端口（dev 用 dotnet watch 启动）
		Cmd-Stop
		Start-Sleep -Seconds 1

		foreach ($k in $ServiceOrder){
			Start-WatchProc $k $Services[$k]
			if ($k -ne 'webui') {
				Write-Host "  $k : " -NoNewline
				Wait-For-Service $k 60 -wasJustStarted $true | Out-Null
			}
		}
		Start-Sleep -Seconds 3
		Write-Host "  webui : " -NoNewline
		if (Wait-For-Url 'http://127.0.0.1:5177/login' 20) {
			Write-Host "v ready" -ForegroundColor Green
			# 自动登录
			Write-Host "[i] Auto-login with CLI token..." -ForegroundColor DarkGray
			try {
				$resp = Invoke-WebRequest -Uri 'http://127.0.0.1:5177/api/auth/cli-token' -Method POST -UseBasicParsing -TimeoutSec 5
				if ($resp.StatusCode -eq 200) {
					$token = ($resp.Content | ConvertFrom-Json).token
					$dashboardUrl = "http://127.0.0.1:5177/?cli-token=$token"
					Write-Host "[v] Auto-login OK" -ForegroundColor Green
					Start-Process $dashboardUrl
				} else {
					Write-Host "[!] Auto-login failed (status $($resp.StatusCode)), open manually" -ForegroundColor Yellow
					Start-Process 'http://127.0.0.1:5177'
				}
			} catch {
				Write-Host "[!] Auto-login failed: $($_.Exception.Message), open manually" -ForegroundColor Yellow
				Start-Process 'http://127.0.0.1:5177'
			}
		} else {
			Write-Host "X not ready" -ForegroundColor Red
		}

		Write-Host "[v] dotnet watch running: edit code, hot-reload auto-applies (per-service)" -ForegroundColor Green
		try {
			while ($true) { Start-Sleep -Seconds 1 }
		} finally {
			Cmd-Stop
		}
		break
	}
}
