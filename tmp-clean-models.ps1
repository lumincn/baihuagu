$ErrorActionPreference = 'Stop'
$msBase = 'C:\Users\lumin\.cache\modelscope\models'
$bhBase = 'C:\Users\lumin\.baihua\models'

Write-Host '=== 0. 前置检查 ===' -ForegroundColor Cyan

# 检查 kokoro/tts 进程
$procs = Get-Process | Where-Object { $_.ProcessName -match 'kokoro|tts' -or ($_.Path -and $_.Path -match 'kokoro') }
if ($procs) {
    Write-Host '[警告] 发现 kokoro/tts 相关进程，可能锁定文件:' -ForegroundColor Yellow
    $procs | Select-Object Id, ProcessName, Path | Format-Table -AutoSize
    Write-Host '建议先停止 Kokoro TTS server 再继续。是否继续？(y/N)'
    $ans = Read-Host
    if ($ans -ne 'y') { Write-Host '已取消'; exit 0 }
} else {
    Write-Host '  无 kokoro/tts 进程，可安全操作'
}

# 磁盘空间（删除前）
$drive = Get-PSDrive C
$freeBefore = [math]::Round($drive.Free / 1GB, 2)
Write-Host ("  C 盘可用空间（删除前）: {0} GB" -f $freeBefore)

# 1. 删除 modelscope Qwen3.5 (9 GB)
Write-Host "`n=== 1. 删除 modelscope Qwen3.5 副本 (9 GB) ===" -ForegroundColor Cyan
$qwenPath = Join-Path $msBase 'OpenVINO--Qwen3.5-9B-int8-ov'
if (Test-Path $qwenPath) {
    Write-Host "  删除: $qwenPath"
    Remove-Item $qwenPath -Recurse -Force
    Write-Host '  [OK] 已删除' -ForegroundColor Green
} else {
    Write-Host '  跳过（不存在）'
}

# 2. 迁移 Kokoro：junction -> 实体目录
Write-Host "`n=== 2. 迁移 Kokoro（modelscope -> baihua 实体）===" -ForegroundColor Cyan
$kokoMs = Join-Path $msBase 'OpenVINO--Kokoro-82M-int8-ov\snapshots\master'
$kokoBh = Join-Path $bhBase 'Kokoro-82M-int8-ov\1'

if (-not (Test-Path $kokoMs)) {
    Write-Host '  modelscope Kokoro 不存在，跳过'
} else {
    # 2a. 删除 baihua junction
    $item = Get-Item $kokoBh -ErrorAction SilentlyContinue
    if ($item -and $item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        Write-Host "  删除 junction: $kokoBh -> $($item.Target)"
        (Get-Item $kokoBh).Delete()
        Write-Host '  [OK] junction 已删除' -ForegroundColor Green
    } elseif (Test-Path $kokoBh) {
        Write-Host '  [警告] baihua Kokoro\1 已是实体目录（非 junction），跳过迁移' -ForegroundColor Yellow
    } else {
        Write-Host '  baihua junction 不存在，直接迁移'
    }

    # 2b. 移动 modelscope master -> baihua Kokoro\1（同盘移动，秒级）
    if (-not (Test-Path $kokoBh)) {
        Write-Host "  移动: $kokoMs -> $kokoBh"
        Move-Item $kokoMs $kokoBh
        Write-Host '  [OK] 已移动' -ForegroundColor Green
    }

    # 2c. 删除 modelscope Kokoro 空壳
    $kokoMsRoot = Join-Path $msBase 'OpenVINO--Kokoro-82M-int8-ov'
    if (Test-Path $kokoMsRoot) {
        Write-Host "  删除空壳: $kokoMsRoot"
        Remove-Item $kokoMsRoot -Recurse -Force
        Write-Host '  [OK] 已删除' -ForegroundColor Green
    }
}

# 3. 验证 + 磁盘空间（删除后）
Write-Host "`n=== 3. 验证 ===" -ForegroundColor Cyan

# 验证 baihua Qwen3.5 仍在
if (Test-Path (Join-Path $bhBase 'Qwen3.5-9B-int8-ov\openvino_language_model.bin')) {
    Write-Host '  [OK] baihua Qwen3.5 完好' -ForegroundColor Green
} else {
    Write-Host '  [错误] baihua Qwen3.5 缺失！' -ForegroundColor Red
}

# 验证 baihua Kokoro 仍可访问
if (Test-Path (Join-Path $kokoBh 'openvino_model.bin')) {
    Write-Host '  [OK] baihua Kokoro 实体目录完好' -ForegroundColor Green
} else {
    Write-Host '  [错误] baihua Kokoro 缺失！' -ForegroundColor Red
}

# 磁盘空间（删除后）
$drive2 = Get-PSDrive C
$freeAfter = [math]::Round($drive2.Free / 1GB, 2)
$freed = [math]::Round($freeAfter - $freeBefore, 2)
Write-Host ("`n  C 盘可用空间（删除后）: {0} GB" -f $freeAfter)
Write-Host ("  释放空间: {0} GB" -f $freed) -ForegroundColor Green