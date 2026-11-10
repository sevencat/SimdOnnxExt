<#
  OnnxUtil 进度工具（PowerShell 5.1 / 7 通用）

  单一事实源 : docs/PLAN.md              （唯一可手改；任务表格）
  生成物     : docs/PROGRESS.md          （只能由本脚本生成，禁止手改）
  追加日志   : docs/progress/YYYY-MM.md  （append-only）

  用法：
    powershell -File tools\progress.ps1 -Write
    powershell -File tools\progress.ps1 -Check
    powershell -File tools\progress.ps1 -Set M1-03 doing -Note "letterbox 用 ResizeLinear"

  说明：本文件含中文，必须保存为 UTF-8 with BOM；Windows PowerShell 5.1 会把无 BOM 的 .ps1
        按 ANSI 解码，中文会变成乱码并写进 PROGRESS.md。
#>
param(
    [switch]$Write,
    [switch]$Check,
    [Parameter(Position = 0)][string[]]$Set,
    [Parameter(Position = 1)][string]$Status,
    [string]$Note = '',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

$root         = Split-Path -Parent $PSScriptRoot
$planPath     = Join-Path $root 'docs\PLAN.md'
$progressPath = Join-Path $root 'docs\PROGRESS.md'
$logDir       = Join-Path $root 'docs\progress'
$utf8NoBom    = New-Object System.Text.UTF8Encoding($false)
$validStatus  = @('todo', 'doing', 'blocked', 'done')

function Read-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "not found: $path" }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

function Write-Text([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, $utf8NoBom)
}

function Split-Lines([string]$text) { return ($text -split "`r?`n") }

function Join-Lines([string[]]$lines) { return ($lines -join "`n") }

function Get-LogPath { return (Join-Path $logDir ((Get-Date).ToString('yyyy-MM') + '.md')) }

function Get-Plan {
    $text = Read-Text $planPath
    $lines = Split-Lines $text
    $milestone = '(未分组)'
    $tasks = New-Object System.Collections.ArrayList
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^##\s+(.+?)\s*$') { $milestone = $Matches[1]; continue }
        if ($line -notmatch '^\s*\|') { continue }
        $cells = @($line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
        if ($cells.Count -lt 3) { continue }
        if ($cells[0] -notmatch '^[A-Z][A-Z0-9]*-\d+$') { continue }
        while ($cells.Count -lt 5) { $cells += '' }
        $status = $cells[2].ToLowerInvariant()
        if ($validStatus -notcontains $status) {
            throw ("docs/PLAN.md line {0}: task {1} has invalid status '{2}' (allowed: {3})" -f ($i + 1), $cells[0], $cells[2], ($validStatus -join ' / '))
        }
        [void]$tasks.Add([pscustomobject]@{
            Id = $cells[0]; Task = $cells[1]; Status = $status; Accept = $cells[3]; Note = $cells[4]
            Milestone = $milestone; LineIndex = $i
        })
    }
    if ($tasks.Count -eq 0) { throw 'docs/PLAN.md: no task rows found (format: | ID | task | status | acceptance | note |)' }
    return [pscustomobject]@{ Lines = $lines; Tasks = $tasks }
}

function New-ProgressText($plan) {
    $tasks = $plan.Tasks
    $total = $tasks.Count
    $done = @($tasks | Where-Object { $_.Status -eq 'done' }).Count
    $doing = @($tasks | Where-Object { $_.Status -eq 'doing' })
    $blocked = @($tasks | Where-Object { $_.Status -eq 'blocked' })

    $pct = 0
    if ($total -gt 0) { $pct = [int][math]::Round(100.0 * $done / $total) }
    $width = 32
    $filled = 0
    if ($total -gt 0) { $filled = [int][math]::Round($width * $done / $total) }
    $bar = ('#' * $filled) + ('-' * ($width - $filled))

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('# 进度快照（自动生成）')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('> 生成时间：' + (Get-Date).ToString('yyyy-MM-dd HH:mm'))
    [void]$sb.AppendLine('> 由 tools/progress.ps1 -Write 生成，请勿手改；计划与验收口径改 docs/PLAN.md，规范见 AGENTS.md。')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('**总进度：' + $done + '/' + $total + '（' + $pct + '%）**  [' + $bar + ']')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| 里程碑 | 完成 | 总计 | 状态 |')
    [void]$sb.AppendLine('|---|---:|---:|---|')
    foreach ($g in ($tasks | Group-Object Milestone)) {
        $gDone = @($g.Group | Where-Object { $_.Status -eq 'done' }).Count
        $gBlocked = @($g.Group | Where-Object { $_.Status -eq 'blocked' }).Count
        $gState = '未开始'
        if ($gDone -eq $g.Count) { $gState = '完成' }
        elseif ($gDone -gt 0 -or @($g.Group | Where-Object { $_.Status -ne 'todo' }).Count -gt 0) { $gState = '进行中' }
        if ($gBlocked -gt 0) { $gState = $gState + '，有阻塞' }
        [void]$sb.AppendLine('| ' + $g.Name + ' | ' + $gDone + ' | ' + $g.Count + ' | ' + $gState + ' |')
    }

    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## 进行中（doing）')
    [void]$sb.AppendLine('')
    if ($doing.Count -eq 0) { [void]$sb.AppendLine('- （无）') }
    foreach ($t in $doing) {
        $line = '- ' + $t.Id + ' · ' + $t.Task
        if ($t.Note) { $line = $line + ' ｜ ' + $t.Note }
        [void]$sb.AppendLine($line)
    }

    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## 受阻（blocked）')
    [void]$sb.AppendLine('')
    if ($blocked.Count -eq 0) { [void]$sb.AppendLine('- （无）') }
    foreach ($t in $blocked) {
        $line = '- ' + $t.Id + ' · ' + $t.Task
        if ($t.Note) { $line = $line + ' ｜ 原因：' + $t.Note }
        [void]$sb.AppendLine($line)
    }

    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## 最近变更（' + (Get-Date).ToString('yyyy-MM') + '）')
    [void]$sb.AppendLine('')
    $logPath = Get-LogPath
    $recent = @()
    if (Test-Path -LiteralPath $logPath) {
        $recent = @((Split-Lines (Read-Text $logPath)) | Where-Object { $_ -like '- *' } | Select-Object -Last 10)
    }
    if ($recent.Count -eq 0) { [void]$sb.AppendLine('- （本月暂无记录）') }
    foreach ($l in $recent) { [void]$sb.AppendLine($l) }

    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## 建议下一步')
    [void]$sb.AppendLine('')
    $next = $tasks | Where-Object { $_.Status -ne 'done' } | Select-Object -First 1
    if ($null -eq $next) {
        [void]$sb.AppendLine('- 全部任务已完成。')
    }
    else {
        [void]$sb.AppendLine('- ' + $next.Id + ' · ' + $next.Task)
        if ($next.Accept) { [void]$sb.AppendLine('  - 验收：' + $next.Accept) }
        if ($next.Note) { [void]$sb.AppendLine('  - 备注：' + $next.Note) }
    }
    return $sb.ToString()
}

function Get-ProgressDiff([string]$expected) {
    if (-not (Test-Path -LiteralPath $progressPath)) { return 'docs/PROGRESS.md is missing; run -Write' }
    $a = @((Split-Lines (Read-Text $progressPath)) | Where-Object { $_ -notmatch '^> 生成时间：' })
    $w = @((Split-Lines $expected) | Where-Object { $_ -notmatch '^> 生成时间：' })
    $n = [math]::Max($a.Count, $w.Count)
    for ($i = 0; $i -lt $n; $i++) {
        $av = ''
        if ($i -lt $a.Count) { $av = $a[$i] }
        $wv = ''
        if ($i -lt $w.Count) { $wv = $w[$i] }
        if ($av -ne $wv) {
            $msg = 'docs/PROGRESS.md is out of date (first difference at line ' + ($i + 1) + '):' + [Environment]::NewLine
            $msg = $msg + '  actual  : ' + $av + [Environment]::NewLine
            $msg = $msg + '  expected: ' + $wv + [Environment]::NewLine
            $msg = $msg + 'run: powershell -File tools\progress.ps1 -Write'
            return $msg
        }
    }
    return $null
}

function Set-TableCell([string]$line, [int]$cell, [string]$value) {
    $pipes = New-Object System.Collections.ArrayList
    for ($k = 0; $k -lt $line.Length; $k++) {
        if ($line[$k] -eq '|') { [void]$pipes.Add($k) }
    }
    if ($pipes.Count -lt ($cell + 1)) { return $line }
    $left = $line.Substring(0, $pipes[$cell - 1] + 1)
    $right = $line.Substring($pipes[$cell])
    return $left + ' ' + $value + ' ' + $right
}

function Invoke-Set([string]$id, [string]$status, [string]$note) {
    $plan = Get-Plan
    $status = $status.ToLowerInvariant()
    if ($validStatus -notcontains $status) {
        throw ("invalid status '{0}' (allowed: {1})" -f $status, ($validStatus -join ' / '))
    }
    $task = $plan.Tasks | Where-Object { $_.Id -eq $id } | Select-Object -First 1
    if ($null -eq $task) { throw "task not found in docs/PLAN.md: $id" }
    $old = $task.Status

    $line = $plan.Lines[$task.LineIndex]
    if ($old -ne $status) { $line = Set-TableCell $line 3 $status }
    if ($note) { $line = Set-TableCell $line 5 $note }
    $plan.Lines[$task.LineIndex] = $line
    Write-Text $planPath ((Join-Lines $plan.Lines) + "`n")

    if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }
    $logPath = Get-LogPath
    if (-not (Test-Path -LiteralPath $logPath)) {
        $head = '# ' + (Get-Date).ToString('yyyy-MM') + ' 变更日志' + "`n`n" + '> append-only：由 tools/progress.ps1 -Set 追加；历史行不要改。' + "`n`n"
        Write-Text $logPath $head
    }
    $entry = '- ' + (Get-Date).ToString('yyyy-MM-dd HH:mm') + ' · ' + $id + ' · ' + $old + ' → ' + $status
    if ($note) { $entry = $entry + ' · ' + $note }
    [IO.File]::AppendAllText($logPath, $entry + "`n", $utf8NoBom)

    Write-Text $progressPath (New-ProgressText (Get-Plan))
    Write-Host ('[progress] ' + $id + ': ' + $old + ' -> ' + $status)
}

$setArgs = @()
if ($Set) { $setArgs += @($Set) }
if ($Status) { $setArgs += $Status }
if ($Rest) { $setArgs += @($Rest) }
$setArgs = @($setArgs | Where-Object { $_ -ne $null -and $_ -ne '' })

if ($setArgs.Count -gt 0) {
    if ($setArgs.Count -lt 2) { throw 'usage: -Set <taskId> <status> [-Note "text"]' }
    Invoke-Set $setArgs[0] $setArgs[1] $Note
}
elseif ($Write) {
    $docsDir = Split-Path -Parent $progressPath
    if (-not (Test-Path -LiteralPath $docsDir)) { New-Item -ItemType Directory -Force $docsDir | Out-Null }
    Write-Text $progressPath (New-ProgressText (Get-Plan))
    Write-Host '[progress] wrote docs/PROGRESS.md'
}
elseif ($Check) {
    $diff = Get-ProgressDiff (New-ProgressText (Get-Plan))
    if ($diff) { Write-Host $diff; exit 1 }
    Write-Host '[progress] docs/PROGRESS.md is up to date'
}
else {
    Write-Host 'usage: powershell -File tools\progress.ps1 [-Write | -Check | -Set <taskId> <status> [-Note "text"]]'
}
