$ErrorActionPreference = 'SilentlyContinue'

function Show-Models($path, $label) {
    Write-Host "=== $label ===" -ForegroundColor Cyan
    Write-Host "路径: $path"
    if (-not (Test-Path $path)) {
        Write-Host "  目录不存在`n"
        return
    }
    $dirs = Get-ChildItem $path -Directory
    if ($dirs.Count -eq 0) {
        Write-Host "  无子目录`n"
        return
    }
    $rows = foreach ($d in $dirs) {
        $size = (Get-ChildItem $d.FullName -Recurse -File | Measure-Object -Property Length -Sum).Sum
        [PSCustomObject]@{ Name = $d.Name; SizeMB = [math]::Round($size / 1MB, 1); LastWrite = $d.LastWriteTime.ToString('yyyy-MM-dd') }
    }
    $total = ($rows | Measure-Object -Property SizeMB -Sum).Sum
    $rows | Sort-Object SizeMB -Descending | Format-Table -AutoSize
    Write-Host ("合计: {0} MB ({1} GB)`n" -f $total, [math]::Round($total/1024, 2))
}

Show-Models 'C:\Users\lumin\.cache\modelscope\models' 'modelscope 缓存（可清理）'
Show-Models 'C:\Users\lumin\.baihua\models' 'baihua 模型（需保留）'