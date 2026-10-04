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
        if (-not $t.inline) { continue }
        $checked++
        $id = [string]$t.id
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

Write-Host ('检查了 ' + $checked + ' 个内联脚本（' + $files.Count + ' 个清单文件）')
if ($problems -gt 0) {
    Write-Host ('[FAIL] ' + $problems + ' 个问题')
    exit 1
}
Write-Host '[PASS] 内联脚本语法全部通过'
exit 0
