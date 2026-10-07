#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Test-InlineSyntax.ps1 -- 用 PowerShell 自己的解析器检查 tools\*.json 里每个内联脚本的语法。

    为什么需要它：按钮的内联脚本被 to-base64 之后交给 PowerShell 5.1 跑，语法错误只有在用户
    点按钮的那一刻才会暴露（而那时人已经坐在屏幕前了）。这里只解析、不执行，所以可以安全地
    在 CI / 每批改动后跑一遍。用的是 Windows PowerShell 5.1 自带的那套解析器，和运行环境一致。

    同时顺手体检几个容易写错的地方：
      · shell=cmd 的脚本里出现了 PowerShell 方言（$env: / Test-Path / -LiteralPath …）
      · 内联脚本里的 %变量%（cmd 展开语法）—— PowerShell 不展开它
      · 引号数量不成对（单引号计数为奇数）

    另外：仓库里**每个 .ps1**（build.ps1 / tools\*.ps1 / tests\*.ps1）也用同一个解析器解析一遍。
    为什么要有这一段：2026-10-05 在打包脚本里把 `$files.Count` 写成了 `files.Count`，那是**解析期**
    错误，而 tools\Make-Package.ps1 只有"真要发版打包"的那一刻才会被执行到 —— 平时谁也不碰它，
    错误就一直躺着（是打包测试跑起来才炸出来的）。脚本的语法体检比什么注释都便宜。

    用法: powershell -File tools\Test-InlineSyntax.ps1
    退出码: 0 = 全部通过, 1 = 有问题
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$files = @(Get-ChildItem (Join-Path $root 'tools\*.json') -ErrorAction SilentlyContinue | Sort-Object Name)
if ($files.Count -eq 0) { Write-Host '[FAIL] 找不到 tools\*.json'; exit 1 }

$checked = 0
$problems = 0

function Report-Problem {
    param([string]$File, [string]$Id, [string]$What)
    Write-Host ('  FAIL  ' + $Id + '  (' + $File + ')')
    Write-Host ('        ' + $What)
}

foreach ($f in $files) {
    # 清单本身必须能解析：运行时遇到坏 JSON 会把**整个文件**丢掉（那一页的按钮会全部消失，
    # 只在日志里留一条"警告"），所以在测试里直接当失败报出来。
    # 2026-10-04 踩过：往清单末尾追加一条时忘了给上一条补逗号，整页按钮就没了。
    $json = $null
    try {
        $json = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        $problems++
        Report-Problem $f.Name '(整个文件)' ('清单 JSON 解析失败：' + $_.Exception.Message + '  —— 运行时会把这一页的按钮全部丢掉')
        continue
    }
    $list = $json.tools
    if (-not $list) {
        $problems++
        Report-Problem $f.Name '(整个文件)' '清单里没有 tools 数组'
        continue
    }
    foreach ($t in $list) {
        # ---- 文案三层体检：说明文案只写一半，比不写更糟（用户以为那就是全部了）。
        # 「悬停提示」= hint（鼠标停住看的那一句，必须短）；「详情说明」= about（右键
        # 「功能说明…」窗口里的整段：怎么用 / 什么时候适合用 / 安全保障）。两者都在清单里，
        # 界面和命令行读的是同一份（MainForm.TipFor / MainForm.HelpText）。
        $id = [string]$t.id
        if ($t.about) {
            if (-not $t.hint) {
                $problems++
                Report-Problem $f.Name $id '写了 about（详情说明）却没有 hint（悬停提示那一句）—— 悬停是用户最先看到的那一层'
            }
            if (([string]$t.about).Trim().Length -lt 20) {
                $problems++
                Report-Problem $f.Name $id 'about（详情说明）短于 20 字：要么把它写清楚（怎么用 / 注意什么），要么就别写这个字段'
            }
        }
        if ($t.hint -and ([string]$t.hint).Length -gt 120) {
            $problems++
            Report-Problem $f.Name $id ('hint（悬停提示）有 ' + ([string]$t.hint).Length + ' 个字 —— 悬停是一行一句，太长了在 tooltip 里就是一堵墙，详情请写进 about')
        }

        if (-not $t.inline) { continue }
        $checked++
        $isCmd = ($t.shell -eq 'cmd')

        if ($isCmd) {
            # cmd 脚本不归 PowerShell 解析器管，只做它的方言体检
            foreach ($bad in @('$env:', 'Test-Path', '-LiteralPath', 'Get-ChildItem', 'New-Item', '[Environment]')) {
                if ($t.inline.Contains($bad)) {
                    $problems++
                    Report-Problem $f.Name $id ("shell=cmd 的脚本里出现了 PowerShell 方言：" + $bad)
                }
            }
            continue
        }

        # PowerShell 内联脚本：真解析一遍（和运行时一样带上静音进度的那句前缀）
        $src = '$ProgressPreference=''SilentlyContinue'';' + $t.inline
        $errs = $null
        [void][System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$null, [ref]$errs)
        if ($errs -and $errs.Count -gt 0) {
            $problems++
            foreach ($e in $errs) {
                Report-Problem $f.Name $id ($e.Message + '  @ 第 ' + $e.Extent.StartLineNumber + ' 行: ' + $e.Extent.Text)
            }
        }

        # %VAR% 只有 cmd 会展开，PowerShell 会原样传给程序
        if ($t.inline -match '%[A-Za-z_][A-Za-z0-9_]*%') {
            $problems++
            Report-Problem $f.Name $id '%变量% 在 PowerShell 里不会被展开（只有 cmd 会）'
        }

        # 单引号数量为奇数 = 很可能少了闭合引号（解析器未必报错，但语义已经歪了）
        $q = ($t.inline.ToCharArray() | Where-Object { $_ -eq "'" }).Count
        if (($q % 2) -ne 0) {
            $problems++
            Report-Problem $f.Name $id ('单引号有 ' + $q + ' 个（奇数），可能少了一个闭合引号')
        }
    }
}

# ---- 仓库里每个 .ps1 也真解析一遍（脚本的语法错误只在"那一刻"才会炸） -------------------
$scriptFiles = New-Object System.Collections.ArrayList
foreach ($pat in @('build.ps1', 'tools\*.ps1', 'tests\*.ps1')) {
    foreach ($f in @(Get-ChildItem (Join-Path $root $pat) -ErrorAction SilentlyContinue | Sort-Object Name)) {
        [void]$scriptFiles.Add($f)
    }
}
$parsedScripts = 0
foreach ($f in $scriptFiles) {
    $parsedScripts++
    $errs = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$errs)
    if ($errs -and $errs.Count -gt 0) {
        $problems++
        foreach ($e in $errs) {
            Report-Problem $f.Name '(整份脚本)' ($e.Message + '  @ 第 ' + $e.Extent.StartLineNumber + ' 行: ' + $e.Extent.Text.Trim())
        }
    }
}

# ---- 闸门"从 git 读改动"那段的真跑自检（用假 git 复现那个现场） ---------------------------
# 为什么单测这一段：2026-10-06 实测踩到过 —— tools\Test-Quick.ps1 开头设了
# $ErrorActionPreference='Stop'，而 PowerShell 5.1 在 Stop 下会把**原生命令写到 stderr 的每一行**
# 当成终止性错误：git 只要工作区里有 CRLF 差异就会往 stderr 写
# “warning: … CRLF will be replaced by LF …”，于是 `2>$null` **也拦不住** ——
# 命令当场被中断、$LASTEXITCODE 变成 -1、改动集读成空 → 闸门**静默退回全套跑**
# （表面"宁多勿少"很安全，实际把 tests\test-map.json 整个架空，报出来的原因还是误导的）。
# 这种错只有真跑一次才现形，所以这里造一个**假 git**：它先往 stderr 写一行 warning，再往 stdout
# 报一个文件名 —— 闸门那段函数必须仍然读出这个文件名（读不出就说明 EAP 那道防线又被拿掉了）。
$quickPath = Join-Path $root 'tools\Test-Quick.ps1'
if (-not (Test-Path -LiteralPath $quickPath)) {
    $problems++
    Report-Problem 'Test-Quick.ps1' '(整份脚本)' '找不到这个文件（提交前闸门）'
} else {
    $fnAsts = @()
    try {
        $quickAst = [System.Management.Automation.Language.Parser]::ParseFile($quickPath, [ref]$null, [ref]$null)
        $fnAsts = @($quickAst.FindAll({ param($n)
            ($n -is [System.Management.Automation.Language.FunctionDefinitionAst]) -and ($n.Name -eq 'Get-ChangedPaths')
        }, $true))
    } catch { }
    if ($fnAsts.Count -eq 0) {
        $problems++
        Report-Problem 'Test-Quick.ps1' 'Get-ChangedPaths' '找不到这个函数（闸门"从 git 读改动"那段被改名或删掉了？自检没法做）'
    } else {
        $mockDir = Join-Path $env:TEMP 'mxx1-gitmock-selfcheck'
        $probeOk = $false
        $probeOkCjk = $false
        $probeDetail = ''
        try {
            if (Test-Path -LiteralPath $mockDir) { Remove-Item -LiteralPath $mockDir -Recurse -Force }
            [void][System.IO.Directory]::CreateDirectory($mockDir)
            # 假 git 干两件事，各对应一条真实踩过的坑：
            #   ① stderr 一行 warning（照抄 git 在 CRLF 差异下的真实输出）—— 闸门那段必须仍然读得出改动；
            #   ② stdout **按 git 的样子写原始 UTF-8 字节**，其中一个是中文名文件 —— 闸门必须读出原名
            #      （2026-10-06 晚五实测：git 默认把非 ASCII 路径输出成 `"docs/\345\276\205…"`，
            #      闸门既匹配不上规则、又拿这个串去 Test-Path → `Illegal characters in path` **当场崩**）。
            #      这里故意**不用 cmd 的 echo**（那玩意按控制台代码页写字节，测不出编码）：直接
            #      `[Console]::OpenStandardOutput()` 写 UTF-8 字节，跨代码页都稳。
            $mockPs1 = @'
[Console]::Error.WriteLine("warning: in the working copy of 'x.md', CRLF will be replaced by LF the next time Git touches it")
$utf8 = New-Object System.Text.UTF8Encoding($false)
$bytes = $utf8.GetBytes("src/MainForm.cs`ndocs/待做的新工具任务书.md`n")
$so = [Console]::OpenStandardOutput()
$so.Write($bytes, 0, $bytes.Length)
$so.Flush()
'@
            [System.IO.File]::WriteAllText((Join-Path $mockDir 'mockgit.ps1'), $mockPs1, (New-Object System.Text.UTF8Encoding($true)))
            $mockCmd = "@echo off`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0mockgit.ps1`"`r`nexit /b %ERRORLEVEL%`r`n"
            [System.IO.File]::WriteAllText((Join-Path $mockDir 'git.cmd'), $mockCmd, [System.Text.Encoding]::ASCII)
            # 注意 $root 不能写成盘符开头的字面路径：编码体检查"绝对路径"（闸门只用它拼 git 命令，
            # 而 git 已经是假的了，所以这里随便给个相对名字就行）。
            $probe = "`$ErrorActionPreference = 'Stop'`r`n" +
                     "`$root = 'no-such-repo'`r`n`$Since = 'HEAD'`r`n`$Changed = @()`r`n" +
                     "`$env:PATH = '" + $mockDir + ";' + `$env:PATH`r`n" +
                     $fnAsts[0].Extent.Text + "`r`n" +
                     "`$r = @(Get-ChangedPaths)`r`n" +
                     "Write-Output ('COUNT=' + `$r.Count)`r`n" +
                     "foreach (`$x in `$r) { Write-Output ('NAME=' + `$x) }`r`n"
            [System.IO.File]::WriteAllText((Join-Path $mockDir 'probe.ps1'), $probe, (New-Object System.Text.UTF8Encoding($true)))
            $out = @(& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $mockDir 'probe.ps1') 2>&1)
            $outText = (@($out | ForEach-Object { [string]$_ }) -join ' / ')
            $probeOk = ($outText -match 'NAME=src/MainForm\.cs')
            $probeOkCjk = ($outText -match 'NAME=docs/待做的新工具任务书\.md')
            $probeDetail = $outText
        } catch {
            $probeDetail = ('自检自己出错：' + $_.Exception.Message)
        } finally {
            if (Test-Path -LiteralPath $mockDir) { Remove-Item -LiteralPath $mockDir -Recurse -Force -ErrorAction SilentlyContinue }
        }
        if ($probeOk -and $probeOkCjk) {
            Write-Host '  （闸门读 git 的自检：假 git 往 stderr 写了一行 warning + 报了一个**中文名**文件，两个名字都原样读出来了）'
        } else {
            $problems++
            Report-Problem 'Test-Quick.ps1' 'Get-ChangedPaths' ('假 git 写了 stderr warning / 报中文名文件之后读不出改动 —— 闸门会静默退回全套跑、甚至当场崩（EAP 防线或 UTF-8 解码那道没了？）  实测输出: ' + $probeDetail)
        }
    }
}

Write-Host ('检查了 ' + $checked + ' 个内联脚本（' + $files.Count + ' 个清单文件）+ ' + $parsedScripts + ' 个 .ps1 脚本')
if ($problems -gt 0) {
    Write-Host ('[FAIL] ' + $problems + ' 个问题')
    exit 1
}
Write-Host '[PASS] 内联脚本语法全部通过'
exit 0
