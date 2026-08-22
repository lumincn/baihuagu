# 向 SharedResources.resx 与 zh-CN.resx 补充 LogErrors_* 键（审计 P1：/log-errors 整页显示原始键名）
$ErrorActionPreference = 'Stop'
$dir = 'C:\Users\lumin\src\baihua\services\Baihua.Web\Localization'

# 键表：[英文值, 中文值]
$keys = @{
    'LogErrors_PageTitle'            = @('Error Logs', '错误日志')
    'LogErrors_Title'                = @('Error Logs', '错误日志')
    'LogErrors_RefreshLocal'         = @('Refresh Local', '刷新本地日志')
    'LogErrors_OpenObserveErrors'    = @('Open OpenObserve Errors', '打开 OpenObserve 错误')
    'LogErrors_ClearLocal'           = @('Clear Local', '清理本地日志')
    'LogErrors_ClearOpenObserve'     = @('Clear OpenObserve', '清理 OpenObserve')
    'LogErrors_OpenObserveUI'        = @('Open OpenObserve UI', '打开 OpenObserve 界面')
    'LogErrors_NoErrors'             = @('No errors found', '暂无错误记录')
    'LogErrors_CurrentSource'        = @('Source: ', '来源：')
    'LogErrors_SourceLocalFiles'     = @('Local log files', '本地日志文件')
    'LogErrors_StatusLocal'          = @('{0} local error entries', '本地错误日志 {0} 条')
    'LogErrors_ReadFailedEntry'      = @('Failed to read: {0}', '读取失败：{0}')
    'LogErrors_StatusError'          = @('Failed to load logs', '加载日志失败')
    'LogErrors_OpenObserveNoStream'  = @('OpenObserve stream unavailable', 'OpenObserve 数据流不可用')
    'LogErrors_OpenObserveQueryFailed' = @('OpenObserve query failed (HTTP {0}): {1}', 'OpenObserve 查询失败（HTTP {0}）：{1}')
    'LogErrors_OpenObserveNoErrors'  = @('No errors in OpenObserve', 'OpenObserve 中暂无错误')
    'LogErrors_StatusOpenObserve'    = @('{0} error entries from OpenObserve', '从 OpenObserve 获取 {0} 条错误')
    'LogErrors_QueryFailedEntry'     = @('Query failed: {0}', '查询失败：{0}')
    'LogErrors_ConfirmClearLocal'    = @('Clear all local error logs? This cannot be undone.', '确定要清理所有本地错误日志吗？此操作不可恢复。')
    'LogErrors_ClearedLocalLogs'     = @('Cleared {0} local log entries', '已清理 {0} 条本地日志')
    'LogErrors_ConfirmClearOpenObserve' = @('Clear all error streams in OpenObserve? This cannot be undone.', '确定要清理 OpenObserve 中的错误数据流吗？此操作不可恢复。')
    'LogErrors_OpenObserveStreamDeleted' = @('OpenObserve stream deleted', 'OpenObserve 数据流已删除')
    'LogErrors_DeleteFailed'         = @('Delete failed (HTTP {0}): {1}', '删除失败（HTTP {0}）：{1}')
    'LogErrors_RequestFailed'        = @('Request failed: {0}', '请求失败：{0}')
}

function Add-ResxKeys($path, $lang) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $text = $text.TrimStart([char]0xFEFF)

    $missing = @()
    foreach ($k in $keys.Keys) {
        if ($text -match "<data name=`"$k`"") { continue }
        $missing += $k
    }
    if ($missing.Count -eq 0) { Write-Host "$path : 已全部存在，跳过"; return }

    $sb = New-Object System.Text.StringBuilder
    foreach ($k in ($missing | Sort-Object)) {
        $val = if ($lang -eq 'zh') { $keys[$k][1] } else { $keys[$k][0] }
        # XML 转义
        $valEsc = $val.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
        [void]$sb.AppendLine("  <data name=`"$k`" xml:space=`"preserve`">")
        [void]$sb.AppendLine("    <value>$valEsc</value>")
        [void]$sb.AppendLine("  </data>")
    }
    $newText = $text.Replace('</root>', $sb.ToString() + '</root>')
    $enc = New-Object System.Text.UTF8Encoding($hasBom)
    [System.IO.File]::WriteAllText($path, $newText, $enc)
    Write-Host "$path : 新增 $($missing.Count) 个键"
}

Add-ResxKeys (Join-Path $dir 'SharedResources.resx') 'en'
Add-ResxKeys (Join-Path $dir 'SharedResources.zh-CN.resx') 'zh'
