# bh.ps1 全面测试套件（Pester 3.4 兼容）
# 运行: Invoke-Pester .\bh.ps1.tests.ps1
# 覆盖: 静态检查 / 函数单元测试 / 命令冒烟测试

$script:ScriptPath = 'C:\Users\lumin\src\baihuagu\bh.ps1'
$script:ScriptDir = Split-Path -Parent $script:ScriptPath

Describe "静态检查" {
    It "文件存在" {
        Test-Path $script:ScriptPath | Should Be $true
    }
    It "UTF-8 with BOM（PS 5.1 兼容硬要求）" {
        $bytes = [System.IO.File]::ReadAllBytes($script:ScriptPath)
        $bytes[0] | Should Be 239
        $bytes[1] | Should Be 187
        $bytes[2] | Should Be 191
    }
    It "语法可解析" {
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$null, [ref]$errors) | Out-Null
        $errors.Count | Should Be 0
    }
    It "包含主入口守卫（可测试性：dot-source 不执行）" {
        $content = Get-Content $script:ScriptPath -Raw
        $content -match "InvocationName -eq '\x2E'" | Should Be $true
    }
    It "包含版本号" {
        $content = Get-Content $script:ScriptPath -Raw
        $content -match "SCRIPT_VERSION = '\d+\.\d+\.\d+'" | Should Be $true
    }
    It "包含统一服务配置（Get-ServiceConfig 单一数据源）" {
        $content = Get-Content $script:ScriptPath -Raw
        $content -match 'function Get-ServiceConfig' | Should Be $true
    }
    It "服务端口只定义在 Get-ServiceConfig（无散落硬编码）" {
        $content = Get-Content $script:ScriptPath -Raw
        # 移除 Get-ServiceConfig 函数体 + 块注释 + 帮助文本（文档文字允许提及端口）
        $cfgStart = $content.IndexOf('function Get-ServiceConfig')
        $cfgEnd = $content.IndexOf('function Get-LogPath')
        $noCfg = $content.Remove($cfgStart, $cfgEnd - $cfgStart)
        # 去掉头部块注释 <# ... #> 和所有行注释
        $noCfg = $noCfg -replace '(?s)<#.*?#>', ''
        $codeOnly = ($noCfg -split "`n" | Where-Object { $_ -notmatch '^\s*#' -and $_ -notmatch 'Write-Host' }) -join "`n"
        $codeOnly -match '8791|8790|8788|5177' | Should Be $false
    }
}

Describe "函数单元测试" {
    # dot-source 加载函数（守卫阻止执行主逻辑）
    BeforeAll {
        . $script:ScriptPath
    }

    It "Get-HgRoot 返回脚本目录" {
        (Get-HgRoot) | Should Be $script:ScriptDir
    }

    It "Get-ServiceConfig 包含 4 个服务" {
        $cfg = Get-ServiceConfig
        $cfg.Keys.Count | Should Be 4
        $cfg.ContainsKey('ai') | Should Be $true
        $cfg.ContainsKey('vault') | Should Be $true
        $cfg.ContainsKey('family') | Should Be $true
        $cfg.ContainsKey('webui') | Should Be $true
    }

    It "Get-ServiceConfig 每项含 Project/Health/Port" {
        $cfg = Get-ServiceConfig
        foreach ($k in $cfg.Keys) {
            $cfg[$k].Project | Should Not BeNullOrEmpty
            $cfg[$k].Health | Should Not BeNullOrEmpty
            $cfg[$k].Port | Should BeGreaterThan 0
        }
    }

    It "Get-ServiceConfig 端口唯一" {
        $cfg = Get-ServiceConfig
        $ports = $cfg.Values | ForEach-Object { $_.Port }
        ($ports | Select-Object -Unique).Count | Should Be 4
    }

    It "ServiceOrder/StopOrder 正确" {
        ($script:ServiceOrder -join ',') | Should Be 'ai,vault,family,webui'
        ($script:StopOrder -join ',') | Should Be 'webui,family,vault,ai'
    }

    It "Get-LogPath / Get-PidPath 使用 TEMP 目录" {
        (Get-LogPath 'ai') | Should Be (Join-Path $env:TEMP 'bh-ai.log')
        (Get-PidPath 'ai') | Should Be (Join-Path $env:TEMP 'bh-ai.pid')
    }

    It "Test-TcpPort 对未知端口返回 false" {
        Test-TcpPort '127.0.0.1' 1 -timeoutMs 200 | Should Be $false
    }

    It "Find-ServiceExe 对未知服务名返回 null（不抛异常）" {
        $proj = Join-Path $script:ScriptDir 'services\Baihua.AI'
        { Find-ServiceExe 'nonexistent' $proj 'Release' } | Should Not Throw
        (Find-ServiceExe 'nonexistent' $proj 'Release') | Should Be $null
    }

    It "Find-ServiceExe 对已知服务返回路径或 null（不抛异常）" {
        $cfg = (Get-ServiceConfig)['ai']
        $proj = Join-Path $script:ScriptDir $cfg.Project
        { $result = Find-ServiceExe 'ai' $proj 'Release' } | Should Not Throw
    }

    It "Test-NeedsRebuild 在 git 不可用时容错返回 false" {
        # 模拟 git 不在 PATH：临时改 PATH 为空再调用（不依赖 Mock 外部命令）
        $origPath = $env:PATH
        try {
            $env:PATH = 'C:\Windows\System32'
            $result = Test-NeedsRebuild
            $result | Should Be $false
        } finally {
            $env:PATH = $origPath
        }
    }
}

Describe "命令冒烟测试" {
    function Invoke-BhCmd {
        param([string[]]$CmdArgs)
        # 安全保护：冒烟测试只允许安全命令，绝不触发 start/dashboard/all/dev（会停服务）
        if ($CmdArgs[0] -in @('dashboard', 'start', 'all', 'dev', 'observe', 'restart', 'stop', 'setup')) {
            throw "冒烟测试禁止执行危险命令: $($CmdArgs[0])"
        }
        $out = & $script:ScriptPath @CmdArgs 6>&1 2>&1 | Out-String
        return $out
    }

    It "version 输出版本号" {
        $out = Invoke-BhCmd @('version')
        $out | Should Match 'bh\.ps1 v\d+\.\d+\.\d+'
    }
    It "help 显示用法" {
        $out = Invoke-BhCmd @('help')
        $out | Should Match '用法:'
        $out | Should Match '服务名:'
    }
    It "未知命令提示错误且不打开浏览器" {
        $out = Invoke-BhCmd @('statsu')
        $out | Should Match '未知命令'
    }
    It "logs 无参数提示用法" {
        $out = Invoke-BhCmd @('logs')
        $out | Should Match '请指定服务名'
    }
    It "logs 未知服务提示" {
        $out = Invoke-BhCmd @('logs', 'nonexistent')
        $out | Should Match '未知服务'
    }
    It "logs webui 支持行数参数" {
        $out = Invoke-BhCmd @('logs', 'webui', '3')
        $out | Should Match 'last 3 lines'
    }
    It "status 输出端口信息" {
        $out = Invoke-BhCmd @('status')
        $out | Should Match 'port:'
    }
}
