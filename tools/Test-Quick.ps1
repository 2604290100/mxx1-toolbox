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
    [switch]$NoBuild
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

# 映射表自检：组名必须真实存在，内容也不能为空
$mapProblems = @()
foreach ($rule in $map.rules) {
    foreach ($g in @($rule.cli)) { if ($allCli -notcontains $g) { $mapProblems += ('cli 组 ' + $g + ' 不存在（规则：' + ($rule.paths -join ',') + '）') } }
    foreach ($g in @($rule.gui)) { if ($allGui -notcontains $g) { $mapProblems += ('gui 组 ' + $g + ' 不存在（规则：' + ($rule.paths -join ',') + '）') } }
}
foreach ($g in @($map.prereq.cli)) { if ($allCli -notcontains $g) { $mapProblems += ('前置 cli 组 ' + $g + ' 不存在') } }
foreach ($g in @($map.prereq.gui)) { if ($allGui -notcontains $g) { $mapProblems += ('前置 gui 组 ' + $g + ' 不存在') } }

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
} else {
    Write-Host ''
    Write-Host ('这次改了 ' + $changedPaths.Count + ' 个文件：')
    foreach ($p in $changedPaths) { Write-Host ('  ' + $p) }

    $docsOnly = @($map.docs_only)
    $cliGroups = @()
    $guiGroups = @()
    $unknown = @()
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
    }

    $cliGroups = @(@($map.prereq.cli) + $cliGroups | Where-Object { $_ } | Sort-Object -Unique)
    $guiGroups = @(@($map.prereq.gui) + $guiGroups | Where-Object { $_ } | Sort-Object -Unique)
    if ($cliGroups.Count -eq 0 -and $guiGroups.Count -eq 0) {
        Write-Host ''
        Write-Host '（这次改的都是文档，只跑编码体检 + 内联体检）'
    }
}

# "哪些组没跑到"要按**前缀**算：两个套件的挑组都是前缀匹配（`-Only A` 会把 A14 一起跑，
# 见 Test-Cli/Test-Gui 里的 Test-GroupSelected），所以只要有一个选中的组是它的前缀，它就跑到了。
# 2026-10-06 实测踩到：`-Only A,B,…` 那一跑明明带了 A14，闸门却把它列进"本次不跑" ——
# 这种**假欠账**会让人白跑一次 -Full（或者更糟：让人以为某组没测过）。
function Test-CoveredBy {
    param([string]$Group, [string[]]$Selected)
    foreach ($s in @($Selected)) {
        if (-not $s) { continue }
        if ($Group.ToUpper().StartsWith($s.ToUpper())) { return $true }
    }
    return $false
}
$cliNotRun = @($allCli | Where-Object { -not (Test-CoveredBy $_ $cliGroups) })
$guiNotRun = @($allGui | Where-Object { -not (Test-CoveredBy $_ $guiGroups) })

Write-Host ''
Write-Host ('要跑的命令行组：' + $(if ($cliGroups.Count -gt 0) { $cliGroups -join ',' } else { '（无）' }))
Write-Host ('要跑的界面组：  ' + $(if ($guiGroups.Count -gt 0) { $guiGroups -join ',' } else { '（无）' }))
if ($cliNotRun.Count -gt 0) { Write-Host ('本次不跑的命令行组：' + ($cliNotRun -join ',') + '（--Full 可全跑）') }
if ($guiNotRun.Count -gt 0) { Write-Host ('本次不跑的界面组：  ' + ($guiNotRun -join ',') + '（-Gui 可跑映射到的组）') }

# ---------------------------------------------------------------- 跑
$script:Failed = @()

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

if ($cliGroups.Count -gt 0) {
    [void](Invoke-Stage '命令行回归（只跑映射到的组）' $cliScript @('-Only', ($cliGroups -join ',')))
} else {
    Write-Host ''
    Write-Host '===== 命令行回归 ====='
    Write-Host '  这次改动不需要跑命令行组，跳过。'
}

if ($Gui -and $guiGroups.Count -gt 0) {
    $running = @(Get-Process -Name Mxx1Toolbox -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Write-Host ''
        Write-Host ('[注意] 有 ' + $running.Count + ' 个 Mxx1Toolbox 正在运行 —— 界面回归会整套假红，这一组没跑。')
        Write-Host '        关掉工具箱再跑一次（不要杀掉它，是用户自己开着的）。'
        $script:Failed += '界面回归（工具箱开着，没跑）'
    } else {
        [void](Invoke-Stage '界面回归（只跑映射到的组）' $guiScript @('-Only', ($guiGroups -join ',')))
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
