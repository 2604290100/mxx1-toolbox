#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Test-Quick.ps1 -- 提交前的本地闸门（约 1 分钟）

    它跑的是"这次改动影响得到的东西"，不是全套回归：

      ① 编码红线体检 + 内联脚本体检   几秒；任何改动都跑（这两样抓的是"静默毁功能"：
                                      BOM 掉了中文变乱码、清单 JSON 坏了按钮整页消失）
      ② build.ps1                     改过 .cs 就得重编，否则命令行回归测的还是旧 exe
      ③ Test-Cli.ps1 -Only <组>       组由 tests\test-map.json 按改动文件映射出来
      ④ Test-Gui.ps1 -Only <组>       默认**不跑**（它要你没有开着工具箱），加 -Gui 才跑

    用法:
      powershell -ExecutionPolicy Bypass -File tools\Test-Quick.ps1
      powershell -ExecutionPolicy Bypass -File tools\Test-Quick.ps1 -List
      powershell -ExecutionPolicy Bypass -File tools\Test-Quick.ps1 -Changed src\MainForm.cs
      powershell -ExecutionPolicy Bypass -File tools\Test-Quick.ps1 -Gui
      powershell -ExecutionPolicy Bypass -File tools\Test-Quick.ps1 -Full
      powershell -ExecutionPolicy Bypass -File tools\Test-Quick.ps1 -Force

    两条省时间的规矩（2026-10-06 加的，起因是用户问"耗时最长的步骤是什么，感觉很浪费时间"）：
      · **同一套输入 30 分钟内跑通过 → 套件那两段跳过**（改动文件的大小/时间戳 + exe + 组名单都没变；
        L0 照旧每次都跑，它只要 8 秒）。命令行与界面各记各的，先不带 -Gui 跑一次、再带 -Gui 跑一次时，
        命令行那半不会重跑。要强制重跑：-Force。
      · **全套要说明白为什么**：哪条规则把这次判成"全套"，它会把规则原文打出来；映射表自己的回归是
        "组名核对 + 覆盖率核对"两道自检，不再靠"改映射表就跑一遍全套"（套件根本不读映射表，跑不出它的错）。

    退出码: 0 = 全过, 1 = 有失败
#>
[CmdletBinding()]
param(
    # 手工指定"这次改了什么"（相对仓库根；不写就从 git 里自己读）
    [string[]]$Changed = @(),

    # 从哪个提交往后算改动（默认 HEAD = 工作区还没提交的那些）
    [string]$Since = 'HEAD',

    # 打印映射表就退出
    [switch]$List,

    # 把映射到的界面组也跑掉（前提：工具箱没开着，否则整套假红）
    [switch]$Gui,

    # 全套（含界面组另算），不看映射
    [switch]$Full,

    # 不重编（只在确认 bin 里的 exe 是最新的时用）
    [switch]$NoBuild,

    # 无视"刚跑过"的记录，强制重跑套件（默认：同一套输入 30 分钟内跑通过就跳过套件那两段）
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$hostExe = (Get-Process -Id $PID).Path
$mapPath = Join-Path $root 'tests\test-map.json'
$cliScript = Join-Path $root 'tests\Test-Cli.ps1'
$guiScript = Join-Path $root 'tests\Test-Gui.ps1'

if (-not (Test-Path -LiteralPath $mapPath)) { Write-Host ('[FAIL] 找不到映射表 ' + $mapPath); exit 1 }
$map = Get-Content -LiteralPath $mapPath -Raw -Encoding UTF8 | ConvertFrom-Json

# 套件里真实存在的组标记 —— 用来把"映射表里写错组名"当场抓出来。
# （写错的组名不会报错，只会让那一组永远不跑 = 静默漏洞，所以这里必须拦住。）
function Get-GroupTokens {
    param([string]$Path)
    $tokens = @()
    if (-not (Test-Path -LiteralPath $Path)) { return $tokens }
    foreach ($line in (Get-Content -LiteralPath $Path)) {
        $m = [regex]::Match($line, '^# (?:-{4,}|={4,})\s*([A-Z][0-9]*)(?![0-9A-Za-z])')
        if ($m.Success) { $tokens += $m.Groups[1].Value }
    }
    return @($tokens | Sort-Object -Unique)
}
$allCli = @(Get-GroupTokens $cliScript)
$allGui = @(Get-GroupTokens $guiScript)

# 挑组是**前缀匹配**（`-Only A` 会把 A14 一起跑，见 Test-Cli/Test-Gui 里的 Test-GroupSelected）。
# 所以"某个组够不够得着 / 跑没跑到"一律用这个函数判：只要有一个组是它的前缀就算够得着。
# 2026-10-06 两处都栽在这上面过：① 闸门把明明跑过的 A14 记成"本次不跑"（假欠账）；
# ② 覆盖率核对要按前缀算，否则 `A` 和 `A14` 会互相判成"没覆盖"。
function Test-CoveredBy {
    param([string]$Group, [string[]]$Selected)
    foreach ($s in @($Selected)) {
        if (-not $s) { continue }
        if ($Group.ToUpper().StartsWith($s.ToUpper())) { return $true }
    }
    return $false
}

# 映射表自检：组名必须真实存在，内容也不能为空
$mapProblems = @()
foreach ($rule in $map.rules) {
    foreach ($g in @($rule.cli)) { if ($allCli -notcontains $g) { $mapProblems += ('cli 组 ' + $g + ' 不存在（规则：' + ($rule.paths -join ',') + '）') } }
    foreach ($g in @($rule.gui)) { if ($allGui -notcontains $g) { $mapProblems += ('gui 组 ' + $g + ' 不存在（规则：' + ($rule.paths -join ',') + '）') } }
}
foreach ($g in @($map.prereq.cli)) { if ($allCli -notcontains $g) { $mapProblems += ('前置 cli 组 ' + $g + ' 不存在') } }
foreach ($g in @($map.prereq.gui)) { if ($allGui -notcontains $g) { $mapProblems += ('前置 gui 组 ' + $g + ' 不存在') } }

# ---- 覆盖率核对：每个组都要被某条规则"够得着"（前缀匹配），否则那一组**只在全套里才会跑** ——
# 改了跟它相关的东西也跑不到它，等于没有映射。这一道 + 上面"组名写错"那一道就是**映射表自己的
# 回归**：两个套件根本不读映射表，所以"改映射表就跑一遍全套"什么也验不出来 —— 2026-10-06 实测
# 白花 115 秒（那一轮把这条废规则删了，换成这两道自检，见 docs\DESIGN.md §15.3）。
$coveredCli = @(@($map.prereq.cli) + @($map.rules | ForEach-Object { @($_.cli) }) | Where-Object { $_ } | Sort-Object -Unique)
$coveredGui = @(@($map.prereq.gui) + @($map.rules | ForEach-Object { @($_.gui) }) | Where-Object { $_ } | Sort-Object -Unique)
foreach ($g in $allCli) {
    if (-not (Test-CoveredBy $g $coveredCli)) { $mapProblems += ('cli 组 ' + $g + ' 在映射表里从没出现过：改了跟它相关的东西也跑不到它（补一条规则）') }
}
foreach ($g in $allGui) {
    if (-not (Test-CoveredBy $g $coveredGui)) { $mapProblems += ('gui 组 ' + $g + ' 在映射表里从没出现过：改了跟它相关的东西也跑不到它（补一条规则）') }
}

function Show-Map {
    Write-Host ''
    Write-Host '改哪块 → 跑哪些组（tests\test-map.json）'
    Write-Host ('命令行的组共 ' + $allCli.Count + ' 个：' + ($allCli -join ' / '))
    Write-Host ('界面的组共 ' + $allGui.Count + ' 个：' + ($allGui -join ' / '))
    $preCli = '（无）'; if (@($map.prereq.cli).Count -gt 0) { $preCli = @($map.prereq.cli) -join ',' }
    $preGui = '（无）'; if (@($map.prereq.gui).Count -gt 0) { $preGui = @($map.prereq.gui) -join ',' }
    Write-Host ('公共前置：命令行 = ' + $preCli + '；界面 = ' + $preGui + '（挑任何组都会自动带上）')
    Write-Host ''
    foreach ($rule in $map.rules) {
        $note = ''
        if ($rule.note) { $note = '   ' + $rule.note }
        Write-Host ('  ' + ($rule.paths -join ', '))
        Write-Host ('      命令行: ' + (@($rule.cli) -join ',') + '   界面: ' + (@($rule.gui) -join ',') + $note)
    }
    Write-Host ''
    Write-Host ('  纯文档（不跑组，只跑编码 + 内联体检）：' + (@($map.docs_only) -join ', '))
    Write-Host '  没命中上面任何一条的改动 = 跑全套（宁多勿少）'
    Write-Host ''
}

if ($mapProblems.Count -gt 0) {
    foreach ($p in $mapProblems) { Write-Host ('[FAIL] 映射表有问题：' + $p) }
    exit 1
}

Write-Host ''
Write-Host '##########################################################'
Write-Host ' 萌新工具箱 · 提交前闸门（只跑受影响的组）'
Write-Host '##########################################################'

if ($List) { Show-Map; exit 0 }

# ---------------------------------------------------------------- 这次改了什么
function Get-ChangedPaths {
    if ($Changed.Count -gt 0) { return @($Changed) }
    # ⚠️ 这一段必须**把 ErrorActionPreference 放开**（函数作用域，脚本其余部分照旧 Stop）。
    # 原因：PowerShell 5.1 在 $ErrorActionPreference='Stop' 下，会把**原生命令写到 stderr 的每一行**
    # 当成终止性错误 —— 而 git 只要工作区里有 CRLF 差异就会写
    # `warning: in the working copy of 'X.md', CRLF will be replaced by LF …`，
    # **`2>$null` 也拦不住**：命令当场被中断、$LASTEXITCODE 变成 -1、改动集读成空。
    # 2026-10-06 实测踩到：闸门每次都报「自动读不出改动」，然后**静默退回全套跑** ——
    # 表面看是"宁多勿少"很安全，实际是把 tests\test-map.json 整个架空，而且给出的原因是误导的。
    # 自检在 tools\Test-InlineSyntax.ps1 末尾（用假 git 往 stderr 写一行 warning，断言仍读得出文件名）。
    $ErrorActionPreference = 'Continue'
    $script:GitNote = ''
    $names = @()
    try {
        $names += @(& git -C $root diff --name-only $Since 2>$null)
        $diffExit = $LASTEXITCODE
        $names += @(& git -C $root ls-files --others --exclude-standard 2>$null)
        $lsExit = $LASTEXITCODE
    } catch {
        $script:GitNote = $_.Exception.Message
        return @()
    }
    if ($diffExit -ne 0 -or $lsExit -ne 0) {
        $script:GitNote = ('git 退出码 diff=' + $diffExit + ' ls-files=' + $lsExit)
        return @()
    }
    if (@($names | Where-Object { $_ }).Count -eq 0) {
        # 工作区是干净的 → 用"还没推到远端的提交"当改动集（提交之后才发现问题的那种跑法）
        $names += @(& git -C $root log --name-only --pretty=format: 'origin/main..HEAD' 2>$null)
        if (@($names | Where-Object { $_ }).Count -gt 0) {
            Write-Host '（工作区是干净的，按"还没推到远端的提交"算改动）'
        }
    }
    return @($names | Where-Object { $_ } | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
}

$changedPaths = @(Get-ChangedPaths)
if (-not $Full -and $changedPaths.Count -eq 0) {
    if ($script:GitNote) {
        Write-Host ('（读不出 git 改动：' + $script:GitNote + ' —— 按全套跑）')
    } else {
        Write-Host '（自动读不出改动，也没有 -Changed —— 按全套跑）'
    }
    $Full = $true
}

if ($Full) {
    $cliGroups = @($allCli)
    $guiGroups = @($allGui)
    $whyFullCli = @('你带了 -Full（或者改动读不出来，按全套跑）')
    $whyFullGui = @('你带了 -Full（或者改动读不出来，按全套跑）')
} else {
    Write-Host ''
    Write-Host ('这次改了 ' + $changedPaths.Count + ' 个文件：')
    foreach ($p in $changedPaths) { Write-Host ('  ' + $p) }

    $docsOnly = @($map.docs_only)
    $cliGroups = @()
    $guiGroups = @()
    $unknown = @()
    # "为什么这次要跑全套"要说清楚：不然一次 2 分钟的全套跑起来，没人知道是规则要求还是程序瞎跑
    # （2026-10-06 用户直接问过"耗时最长的步骤是什么，感觉很浪费时间" —— 那次就是规则太粗，
    # 改一个界面测试文件拖出了整套命令行回归）。
    $whyFullCli = @()
    $whyFullGui = @()
    foreach ($p in $changedPaths) {
        $norm = $p.Replace('\', '/')
        $isDoc = $false
        foreach ($pat in $docsOnly) { if ($norm -like $pat) { $isDoc = $true; break } }
        if ($isDoc) { continue }
        $hit = $false
        foreach ($rule in $map.rules) {
            foreach ($pat in $rule.paths) {
                if ($norm -like $pat) {
                    $hit = $true
                    $cliGroups += @($rule.cli)
                    $guiGroups += @($rule.gui)
                    if (@($rule.cli).Count -ge $allCli.Count) { $whyFullCli += ($norm + ' → ' + $rule.note) }
                    if (@($rule.gui).Count -ge $allGui.Count) { $whyFullGui += ($norm + ' → ' + $rule.note) }
                    break
                }
            }
        }
        if (-not $hit) { $unknown += $norm }
    }

    if ($unknown.Count -gt 0) {
        Write-Host ''
        Write-Host ('有 ' + $unknown.Count + ' 个改动没命中映射表，这次按全套跑（顺手把规则补进 tests\test-map.json）：')
        foreach ($u in $unknown) { Write-Host ('  ' + $u) }
        $cliGroups = @($allCli)
        $guiGroups = @($allGui)
        $whyFullCli += ('有改动没命中映射表（宁多勿少）')
        $whyFullGui += ('有改动没命中映射表（宁多勿少）')
    }

    $cliGroups = @(@($map.prereq.cli) + $cliGroups | Where-Object { $_ } | Sort-Object -Unique)
    $guiGroups = @(@($map.prereq.gui) + $guiGroups | Where-Object { $_ } | Sort-Object -Unique)
    if ($cliGroups.Count -eq 0 -and $guiGroups.Count -eq 0) {
        Write-Host ''
        Write-Host '（这次改的都是文档，只跑编码体检 + 内联体检）'
    }
}

# "哪些组没跑到"按**前缀**算（Test-CoveredBy 定义在文件开头，覆盖率核对也要用它）。
# 2026-10-06 实测踩到：`-Only A,B,…` 那一跑明明带了 A14，闸门却把它列进"本次不跑" ——
# 这种**假欠账**会让人白跑一次 -Full（或者更糟：让人以为某组没测过）。
$cliNotRun = @($allCli | Where-Object { -not (Test-CoveredBy $_ $cliGroups) })
$guiNotRun = @($allGui | Where-Object { -not (Test-CoveredBy $_ $guiGroups) })

Write-Host ''
Write-Host ('要跑的命令行组：' + $(if ($cliGroups.Count -gt 0) { $cliGroups -join ',' } else { '（无）' }))
Write-Host ('要跑的界面组：  ' + $(if ($guiGroups.Count -gt 0) { $guiGroups -join ',' } else { '（无）' }))
# 全套跑一趟大约两分钟，所以**为什么是全套**要写在明面上（不然只会觉得"这东西怎么老是这么慢"）
if ($cliGroups.Count -ge $allCli.Count -and $whyFullCli.Count -gt 0) {
    Write-Host ('  ↑ 命令行是**全套**（' + $allCli.Count + ' 组，约 2 分钟）—— 原因：')
    foreach ($r in @($whyFullCli | Sort-Object -Unique)) { Write-Host ('      ' + $r) }
}
if ($guiGroups.Count -ge $allGui.Count -and $whyFullGui.Count -gt 0) {
    Write-Host ('  ↑ 界面是**全套**（' + $allGui.Count + ' 组，约 2.5 分钟）—— 原因：')
    foreach ($r in @($whyFullGui | Sort-Object -Unique)) { Write-Host ('      ' + $r) }
}
if ($cliNotRun.Count -gt 0) { Write-Host ('本次不跑的命令行组：' + ($cliNotRun -join ',') + '（--Full 可全跑）') }
if ($guiNotRun.Count -gt 0) { Write-Host ('本次不跑的界面组：  ' + ($guiNotRun -join ',') + '（-Gui 可跑映射到的组）') }

# ---------------------------------------------------------------- 刚跑过就跳过
# 为什么要有这一段（2026-10-06 用户问「刚刚耗时最长的步骤是什么，感觉很浪费时间！」）：
# 那一轮我在"改测试/工具脚本 → 跑闸门 → 再改 → 再跑"里连着跑了三遍全套命令行（102 / 115 / 118 秒），
# 其中两遍纯属重复劳动。判据很硬：**同一套输入**（改动文件的大小 + 时间戳、要跑的组、-NoBuild 开关）
# 在 30 分钟内已经**跑通**过 → 套件那两段直接跳过。
# 三条边界：① L0（编码 + 内联）**照旧每次都跑** —— 它只要 8 秒，而"BOM 被 edit 吃掉"正是改完文件
# 最该抓的；② 只有**通过**过的那一跑才写记录（失败就把记录删掉）；③ -Force / -Full 一律重跑。
# 命令行与界面**各记各的**：先跑不带 -Gui 的一次、再跑带 -Gui 的一次，命令行那半不会再跑一遍。
# 两个坑（都在 2026-10-06 当场踩过）：
#   · 指纹**不能算 exe**：csc 每次编译都写新的 PE 时间戳，重编一次 exe 的时间戳/内容就变了 ——
#     拿它当输入等于永远不命中（第一版就是这样，第二跑照样重跑）。真正的输入是那些**改过的文件**。
#   · 指纹要在 **L0 + 编译之后**再算：L0 的 `Test-Encoding.ps1 -Fix` 会补 BOM（改了文件的时间戳），
#     build.ps1 也会 —— 算早了，下一次跑就对不上。
$statePath = Join-Path $env:TEMP 'mxx1-testquick-state.json'

function Get-SuiteFingerprint {
    param([string]$Which, [string[]]$Groups)
    $lines = New-Object System.Collections.ArrayList
    foreach ($p in @($changedPaths | Sort-Object)) {
        $full = Join-Path $root ($p -replace '/', '\')
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            $fi = Get-Item -LiteralPath $full
            [void]$lines.Add($p + '|' + $fi.Length + '|' + $fi.LastWriteTimeUtc.Ticks)
        } else {
            [void]$lines.Add($p + '|(已经不在了)')
        }
    }
    [void]$lines.Add($Which + '=' + ($Groups -join ','))
    # 注意 [int]$NoBuild 会抛 "Cannot convert the False value of type SwitchParameter to type Int32"
    # （PS 5.1 里 SwitchParameter 能隐式当布尔用，但不能直接转整数）—— 得写 .IsPresent
    [void]$lines.Add('nobuild=' + $NoBuild.IsPresent)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))) }
    finally { $sha.Dispose() }
    return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Test-StateFresh {
    param($Entry, [string]$Fp)
    if (-not $Entry) { return $false }
    if ([string]$Entry.fp -ne $Fp) { return $false }
    try { $when = [datetime]::Parse([string]$Entry.when, [System.Globalization.CultureInfo]::InvariantCulture) }
    catch { return $false }
    $ageMin = ((Get-Date) - $when).TotalMinutes
    return (($ageMin -ge 0) -and ($ageMin -lt 30))
}

function Get-StateAge {
    param($Entry)
    try { return [int](((Get-Date) - [datetime]::Parse([string]$Entry.when, [System.Globalization.CultureInfo]::InvariantCulture)).TotalMinutes) }
    catch { return -1 }
}

$oldState = $null
try {
    if (Test-Path -LiteralPath $statePath) {
        $oldState = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
} catch { $oldState = $null }

# 指纹在这之后（L0 + 编译跑完）才算 —— 见上面那段注释里的第二个坑。
$cliFp = ''
$guiFp = ''
$skipCli = $false
$skipGui = $false

# ---------------------------------------------------------------- 跑
$script:Failed = @()
$ranCli = $false
$ranGui = $false

function Invoke-Stage {
    param([string]$Title, [string]$ScriptPath, [string[]]$Extra = @())
    Write-Host ''
    Write-Host ('===== ' + $Title + ' =====')
    $t0 = Get-Date
    $argv = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + $Extra
    # 子进程的 stdout 必须**显式转出来**：它原来是直接进 Invoke-Stage 的输出流，而调用方写的是
    # `[void](Invoke-Stage …)` —— 于是每一段测试的输出全被吞掉了：跑 2 分钟什么都看不见（像卡住），
    # 失败时也只剩一句"闸门没过：命令行回归"，没有任何细节可查（2026-10-06 实测发现）。
    # 走 Write-Host 打到宿主 stdout，`[void]` 就只吞掉函数返回的退出码。
    & $hostExe @argv | Write-Host
    $code = $LASTEXITCODE
    $spent = ((Get-Date) - $t0).TotalSeconds
    Write-Host ('  （用时 {0:n1} 秒，退出码 {1}）' -f $spent, $code)
    if ($code -ne 0) { $script:Failed += $Title }
    return $code
}

$tAll = Get-Date
# 编码体检带 -Fix：`edit` 类工具会静默吃掉 .ps1 / .cs 的 BOM，而 build.ps1 本来也会自动补 ——
# 闸门跟它一样自动修，免得"BOM 被吃"变成一次假红（改了什么会打印出来，是可见的）。
[void](Invoke-Stage '编码红线体检（-Fix 自动补 BOM）' (Join-Path $root 'tools\Test-Encoding.ps1') @('-Fix'))
[void](Invoke-Stage '内联脚本与清单体检' (Join-Path $root 'tools\Test-InlineSyntax.ps1'))

if ($NoBuild) {
    Write-Host ''
    Write-Host '===== 编译 ====='
    Write-Host '  -NoBuild 指定了，跳过（注意：命令行回归测的是 bin 里那个 exe）'
} else {
    [void](Invoke-Stage '编译（build.ps1）' (Join-Path $root 'build.ps1'))
}

# ---- "刚跑过就跳过"的判断放在这里：L0 与编译都跑完了，文件的时间戳不会再被它们改（见上面的注释）
$cliFp = Get-SuiteFingerprint 'cli' $cliGroups
$guiFp = Get-SuiteFingerprint 'gui' $guiGroups
$canSkip = ((-not $Force) -and (-not $Full) -and ($changedPaths.Count -gt 0) -and ($oldState -ne $null))
if ($canSkip) {
    $skipCli = Test-StateFresh $oldState.cli $cliFp
    $skipGui = Test-StateFresh $oldState.gui $guiFp
}

if ($skipCli -and $cliGroups.Count -gt 0) {
    Write-Host ''
    Write-Host '===== 命令行回归 ====='
    Write-Host ('  ' + (Get-StateAge $oldState.cli) + ' 分钟前刚跑通同一套（改动文件 + 组名单 + 开关都没变），跳过。')
    Write-Host '  上面"要跑的命令行组"就是那次已经跑过的组；要强制重跑加 -Force（或 -Full）。'
} elseif ($cliGroups.Count -gt 0) {
    [void](Invoke-Stage '命令行回归（只跑映射到的组）' $cliScript @('-Only', ($cliGroups -join ',')))
    $ranCli = $true
} else {
    Write-Host ''
    Write-Host '===== 命令行回归 ====='
    Write-Host '  这次改动不需要跑命令行组，跳过。'
}

if ($Gui -and $guiGroups.Count -gt 0) {
    if ($skipGui) {
        Write-Host ''
        Write-Host '===== 界面回归 ====='
        Write-Host ('  ' + (Get-StateAge $oldState.gui) + ' 分钟前刚跑通同一套（改动文件 + 组名单 + 开关都没变），跳过。')
        Write-Host '  要强制重跑加 -Force。'
    } else {
        $running = @(Get-Process -Name Mxx1Toolbox -ErrorAction SilentlyContinue)
        if ($running.Count -gt 0) {
            Write-Host ''
            Write-Host ('[注意] 有 ' + $running.Count + ' 个 Mxx1Toolbox 正在运行 —— 界面回归会整套假红，这一组没跑。')
            Write-Host '        关掉工具箱再跑一次（不要杀掉它，是用户自己开着的）。'
            $script:Failed += '界面回归（工具箱开着，没跑）'
        } else {
            [void](Invoke-Stage '界面回归（只跑映射到的组）' $guiScript @('-Only', ($guiGroups -join ',')))
            $ranGui = $true
        }
    }
} elseif ($guiGroups.Count -gt 0) {
    Write-Host ''
    Write-Host '===== 界面回归 ====='
    Write-Host ('  映射到界面组 ' + ($guiGroups -join ',') + '，但没带 -Gui，跳过。')
    Write-Host '  跑法（先确认工具箱没开着）：'
    Write-Host ('    powershell -ExecutionPolicy Bypass -File tests\Test-Gui.ps1 -Only ' + ($guiGroups -join ','))
}

# ---------------------------------------------------------------- 收尾
$spentAll = ((Get-Date) - $tAll).TotalSeconds
Write-Host ''
Write-Host '##########################################################'
if ($script:Failed.Count -eq 0) {
    Write-Host (' 闸门通过（{0:n1} 秒）' -f $spentAll)
} else {
    Write-Host (' 闸门没过：' + ($script:Failed -join ' / '))
}
Write-Host '##########################################################'
# 只在**整轮通过**时写"刚跑过"的记录；没过就把旧记录删掉，免得下次凭一条过期的"通过"跳过套件。
if ($script:Failed.Count -eq 0) {
    $cliEntry = $null
    if ($ranCli) { $cliEntry = [pscustomobject]@{ fp = $cliFp; when = (Get-Date).ToString('o') } }
    elseif ($skipCli -and $oldState) { $cliEntry = $oldState.cli }
    $guiEntry = $null
    if ($ranGui) { $guiEntry = [pscustomobject]@{ fp = $guiFp; when = (Get-Date).ToString('o') } }
    elseif ($skipGui -and $oldState) { $guiEntry = $oldState.gui }
    try {
        $stateObj = [pscustomobject]@{ cli = $cliEntry; gui = $guiEntry; note = 'tools\Test-Quick.ps1 的"刚跑过就跳过"记录（%TEMP%）' }
        [System.IO.File]::WriteAllText($statePath, ($stateObj | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
    } catch { }
} else {
    try { if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath -Force } } catch { }
}
if ($cliNotRun.Count -gt 0 -or $guiNotRun.Count -gt 0) {
    Write-Host ''
    Write-Host '这次**没跑**的组（汇报和提交信息里要写出来，别只说"测过了"）：'
    if ($cliNotRun.Count -gt 0) { Write-Host ('  命令行：' + ($cliNotRun -join ',')) }
    if ($guiNotRun.Count -gt 0) { Write-Host ('  界面：  ' + ($guiNotRun -join ',')) }
    Write-Host '发版前要把这份欠账清掉：不带任何参数跑一次 tests\Test-All.ps1（界面那半要你没开着工具箱）。'
}
Write-Host ''
if ($script:Failed.Count -gt 0) { exit 1 }
exit 0
