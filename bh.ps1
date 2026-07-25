<#
百花谷 Family 版 - Windows (PowerShell) 轻量 CLI
用法: .\bh.ps1 [command]
  bh.ps1                 打开 dashboard（自动检测代码更新，有新提交时重编译重启）
  bh.ps1 setup           首次配置（交互）
  bh.ps1 start           启动服务（在后台运行 dotnet run）
  bh.ps1 stop            停止服务
  bh.ps1 status          查看服务状态
  bh.ps1 restart         重启服务
  bh.ps1 logs [name]     查看日志（taskrunner, webui, ai, vault）
  bh.ps1 open            打开 Web 管理界面 (http://localhost:5177)
  bh.ps1 dev             开发模式（监听文件变动自动重编译重启）
  bh.ps1 observe         启动 OpenObserve 可观测平台（Docker）并打开 Web UI
  bh.ps1 all             启动全部服务（.NET 服务 + OpenObserve + hostmetrics）

说明:
- 该脚本为简易移植，依赖 PowerShell (推荐 pwsh) 和 dotnet SDK
- 后台进程 PID 与日志保存在 $env:TEMP\bh-[service].*
- dashboard 命令会比较当前 git HEAD 与上次启动时的 commit，不同则自动重编译重启
- dev 命令监听 services/ 下 .cs/.razor 文件变动，2秒防抖后自动重编译重启
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
	Write-Host "百花谷 Family 版 - Windows (PowerShell) 轻量 CLI" -ForegroundColor Cyan
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
	Write-Host "  dev                   开发模式（监听文件变动自动重编译重启）"
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
	ai         = "services/TaskRunner.AI";
	vault      = "services/TaskRunner.Vault";
	taskrunner = "services/TaskRunner.Family";
	webui      = "services/WebUI.Family";
}

# 服务健康检查 URL（用轻量端点，避免认证拦截）
$HealthUrls = @{
	ai         = 'http://127.0.0.1:8791/api/ai/config/providers'
	vault      = 'http://127.0.0.1:8790/mg/vaults'
	taskrunner = 'http://127.0.0.1:8788/api/capability'
	webui      = 'http://127.0.0.1:5177/login'
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

function Start-ServiceProc($name, $projRelPath){
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

	Write-Host "Starting $name -> $projPath"
	$args = @('run', '--project', "$projPath", '--no-launch-profile')

	try {
		$prevEnv = $env:ASPNETCORE_ENVIRONMENT
		$prevDataDir = $env:YJ_DATA_DIR
		$env:ASPNETCORE_ENVIRONMENT = 'Development'
		$env:YJ_DATA_DIR = Join-Path $HG_ROOT 'data'
		$proc = Start-Process -FilePath 'dotnet' -ArgumentList $args -RedirectStandardOutput $log -RedirectStandardError $errLog -NoNewWindow -PassThru
		if ($null -ne $prevEnv) { $env:ASPNETCORE_ENVIRONMENT = $prevEnv } else { Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue }
		if ($null -ne $prevDataDir) { $env:YJ_DATA_DIR = $prevDataDir } else { Remove-Item Env:\YJ_DATA_DIR -ErrorAction SilentlyContinue }
		Start-Sleep -Milliseconds 200
		$procId = $proc.Id
		Set-Content -Path $pidFile -Value $procId
		Write-Host "Started $name (PID $procId), log: $log (stderr: $errLog)"
	} catch {
		Write-Host "ERROR: failed to start ${name}: ${_}"
	}
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
			Stop-Process -Id $existingPid -Force -ErrorAction Stop
			# 等待进程完全退出（最多 10 秒）
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
			Write-Host "ERROR: failed to stop ${name}: ${_}"
		}
	} else {
		Remove-Item $pidFile -ErrorAction SilentlyContinue
		Write-Host "$name is not running (cleaned pidfile)"
	}
}

function Show-Status(){
	foreach ($k in $ServiceOrder){
		$pidFile = Get-PidPath $k
		$status = "$k : stopped" 
		$color = [ConsoleColor]::DarkYellow
		if (Test-Path $pidFile){
			$existingPid = Get-Content $pidFile -ErrorAction SilentlyContinue
			if ($existingPid -and (Get-Process -Id $existingPid -ErrorAction SilentlyContinue)){
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
			} else {
				$status = "$k : pidfile exists but process not found"
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
	$pidFile = Get-PidPath $name
	$isRunning = $false
	if (Test-Path $pidFile){
		$existingPid = Get-Content $pidFile -ErrorAction SilentlyContinue
		if ($existingPid -and (Get-Process -Id $existingPid -ErrorAction SilentlyContinue)){
			$isRunning = $true
		}
	}
	if (-not $isRunning){
		Write-Host "Service $name not running, starting..."
		Start-ServiceProc $name $Services[$name]
		return $false
	} else {
		Write-Host "Service $name already running"
		return $true
	}
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
			$pidFile = Get-PidPath $name
			if (Test-Path $pidFile){
				$srvPid = Get-Content $pidFile -ErrorAction SilentlyContinue
				if ($srvPid -and -not (Get-Process -Id $srvPid -ErrorAction SilentlyContinue)){
					Write-Host "  $name : ✗ process crashed" -ForegroundColor Red
					$errLog = "$(Get-LogPath $name).err"
					if (Test-Path $errLog) {
						Write-Host "  Last 5 lines of error log:" -ForegroundColor Yellow
						Get-Content $errLog -Tail 5 -Encoding UTF8 | ForEach-Object { Write-Host "    $_" }
					}
					return $false
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

function Cmd-Start {
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
}

function Cmd-Stop {
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
		$pidFile = Get-PidPath $name
		if (Test-Path $pidFile) {
			$srvPid = Get-Content $pidFile -ErrorAction SilentlyContinue
			if ($srvPid -and -not (Get-Process -Id $srvPid -ErrorAction SilentlyContinue)) {
				Remove-Item $pidFile -ErrorAction SilentlyContinue
			}
		}
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

		# 清理僵尸进程
		foreach ($name in $ServiceOrder) {
			$pidFile = Get-PidPath $name
			if (Test-Path $pidFile) {
				$srvPid = Get-Content $pidFile -ErrorAction SilentlyContinue
				if ($srvPid -and -not (Get-Process -Id $srvPid -ErrorAction SilentlyContinue)) {
					Remove-Item $pidFile -ErrorAction SilentlyContinue
				}
			}
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
		Write-Host "=== 百花 Dev Mode (auto-rebuild on change) ===" -ForegroundColor Cyan
		Write-Host "  Watching: $HG_ROOT\services\*.cs, *.razor" -ForegroundColor DarkGray
		Write-Host "  Press Ctrl+C to stop" -ForegroundColor DarkGray
		Write-Host ""

		# 首次编译+启动
		Cmd-Stop
		Start-Sleep -Seconds 1
		Write-Host "[...] dotnet build..." -ForegroundColor Cyan
		dotnet build (Join-Path $HG_ROOT 'services\BaiHua.slnx') -c Release 2>&1 | Select-Object -Last 3 | ForEach-Object { Write-Host "    $_" }
		if ($LASTEXITCODE -ne 0) { Write-Host "[X] Build failed" -ForegroundColor Red; break }
		Write-Host "[v] Build OK" -ForegroundColor Green
		Save-GitCommit

		foreach ($k in $ServiceOrder){
			Start-ServiceProc $k $Services[$k]
			if ($k -ne 'webui') {
				Write-Host "  $k : " -NoNewline
				Wait-For-Service $k 30 -wasJustStarted $true | Out-Null
			}
		}
		Start-Sleep -Seconds 3
		Write-Host "  webui : " -NoNewline
		if (Wait-For-Url 'http://127.0.0.1:5177/login' 20) {
			Write-Host "v ready" -ForegroundColor Green
		} else {
			Write-Host "X not ready" -ForegroundColor Red
		}

		# 文件监听
		$watcher = New-Object System.IO.FileSystemWatcher
		$watcher.Path = Join-Path $HG_ROOT 'services'
		$watcher.Filter = '*.*'
		$watcher.IncludeSubdirectories = $true
		$watcher.EnableRaisingEvents = $true

		$exts = @('.cs', '.razor', '.cshtml', '.css', '.js')
		$changeTimer = $null
		$debounceMs = 2000

		$action = {
			$ext = [System.IO.Path]::GetExtension($Event.SourceEventArgs.Name)
			if ($ext -in $exts) {
				if ($null -ne $changeTimer) { $changeTimer.Dispose() }
				$changeTimer = New-Object System.Timers.Timer($debounceMs)
				$changeTimer.AutoReset = $false
				Register-ObjectEvent -InputObject $changeTimer -EventName Elapsed -Action {
					Write-Host ""
					Write-Host "[i] Change detected, rebuilding..." -ForegroundColor Yellow
					Cmd-Stop
					Start-Sleep -Seconds 1
					dotnet build (Join-Path $HG_ROOT 'services\BaiHua.slnx') -c Release 2>&1 | Select-Object -Last 3 | ForEach-Object { Write-Host "    $_" }
					if ($script:LASTEXITCODE -ne 0) { Write-Host "[X] Build failed" -ForegroundColor Red; return }
					Write-Host "[v] Build OK, restarting..." -ForegroundColor Green
					Save-GitCommit
					foreach ($k in $ServiceOrder){
						Start-ServiceProc $k $Services[$k]
					}
				} | Out-Null
				$changeTimer.Start()
			}
		}

		Register-ObjectEvent -InputObject $watcher -EventName Changed -Action $action | Out-Null
		Register-ObjectEvent -InputObject $watcher -EventName Created -Action $action | Out-Null
		Register-ObjectEvent -InputObject $watcher -EventName Renamed -Action $action | Out-Null

		Write-Host "[v] Watching for changes... (debounce ${debounceMs}ms)" -ForegroundColor Green
		try {
			while ($true) { Start-Sleep -Seconds 1 }
		} finally {
			$watcher.Dispose()
			Cmd-Stop
		}
		break
	}
}
