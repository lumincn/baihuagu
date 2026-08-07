# bh.ps1 集成测试（手动运行，会真实启停服务）
# 运行: .\tests\bh.ps1.integration.ps1
# ⚠️ 会停止/启动真实百花服务，请确认当前无人在用

$script:ScriptPath = 'C:\Users\lumin\src\baihuagu\bh.ps1'
$Failed = 0
$Passed = 0

function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    Write-Host "▶ $Name" -ForegroundColor Cyan
    try {
        & $Body
        Write-Host "  ✓ PASS" -ForegroundColor Green
        $script:Passed++
    } catch {
        Write-Host "  ✗ FAIL: $($_.Exception.Message)" -ForegroundColor Red
        $script:Failed++
    }
}

function Assert-True {
    param([bool]$Cond, [string]$Msg)
    if (-not $Cond) { throw $Msg }
}

# Write-Host 走信息流（6），必须 6>&1 才能捕获
function Get-BhStatus {
    return & $script:ScriptPath status 6>&1 2>&1 | Out-String
}

# 1. 初始状态：4 服务应运行
Test-Case "初始状态检查" {
    $out = Get-BhStatus
    foreach ($s in @('ai', 'vault', 'family', 'webui')) {
        Assert-True ($out -match "$s : running") "服务 $s 应运行"
    }
}

# 2. stop webui 单服务（回归 8/6 的"stop 全停"坑）
Test-Case "stop webui 只停 webui" {
    & $script:ScriptPath stop webui 6>&1 2>&1 | Out-Null
    Start-Sleep 1
    $out = Get-BhStatus
    Assert-True ($out -match 'webui : stopped') "webui 应停止"
    Assert-True ($out -match 'ai : running') "ai 应仍运行"
    Assert-True ($out -match 'family : running') "family 应仍运行"
}

# 3. restart webui 恢复
Test-Case "restart webui 恢复" {
    & $script:ScriptPath restart webui 6>&1 2>&1 | Out-Null
    Start-Sleep 3
    $out = Get-BhStatus
    Assert-True ($out -match 'webui : running') "webui 应恢复运行"
}

# 4. 健康检查真实可达
Test-Case "4 服务健康检查 200" {
    $urls = @(
        'http://127.0.0.1:8791/api/ai/config/providers',
        'http://127.0.0.1:8790/mg/vaults',
        'http://127.0.0.1:8788/api/capability',
        'http://127.0.0.1:5177/login'
    )
    foreach ($u in $urls) {
        $r = Invoke-WebRequest -Uri $u -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
        Assert-True ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400) "$u 返回 $($r.StatusCode)"
    }
}

# 5. logs 正常读取（非跟随模式）
Test-Case "logs webui 非跟随读取" {
    $out = & $script:ScriptPath logs webui 5 6>&1 2>&1 | Out-String
    Assert-True ($out -match 'last 5 lines') "应显示 last 5 lines"
}

# 6. 未知服务名容错
Test-Case "stop 未知服务名提示" {
    $out = & $script:ScriptPath stop nonexistent 6>&1 2>&1 | Out-String
    Assert-True ($out -match '未知服务') "应提示未知服务"
}

# 7. restart 未知服务名提示
Test-Case "restart 未知服务名提示" {
    $out = & $script:ScriptPath restart nonexistent 6>&1 2>&1 | Out-String
    Assert-True ($out -match '未知服务') "应提示未知服务"
}

Write-Host ""
Write-Host "=== 集成测试结果 ===" -ForegroundColor Cyan
Write-Host "  Passed: $Passed / Failed: $Failed" -ForegroundColor $(if ($Failed -eq 0) { 'Green' } else { 'Red' })
if ($Failed -gt 0) { exit 1 } else { exit 0 }
