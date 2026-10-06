#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Test-All.ps1 -- 一条命令跑完工具箱的全部测试

      1) tools\Test-Encoding.ps1   编码红线体检（BOM / 纯 ASCII / 硬编码路径）
      2) tools\Test-InlineSyntax.ps1  内联脚本语法 + 清单 JSON 体检
3) tests\Test-Cli.ps1        命令行回归
      4) tests\Test-Gui.ps1        界面回归（需要交互式桌面；无桌面返回 3 = 跳过）

    用法:
      powershell -File tests\Test-All.ps1
      powershell -File tests\Test-All.ps1 -SkipGui      # 无桌面环境（CI）用这个
      powershell -File tests\Test-All.ps1 -Only M,N     # 两层回归都只跑这几组（其他组会列在"跳过"里）

    退出码: 0 = 全绿, 1 = 有失败
#>
[CmdletBinding()]
param(
    [switch]$SkipGui,
    # 挑组执行：只跑这几组（透传给两个套件，写法 -Only M,N）
    [string[]]$Only = @(),
    # 除了这几组，别的都跑
    [string[]]$Skip = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$hostExe = (Get-Process -Id $PID).Path

$script:Pass = 0
$script:Fail = 0
$script:Skipped = @()

function Show-Line {
    param([string]$Text)
    Write-Host $Text
}

function Invoke-Suite {
    param([string]$Title, [string]$ScriptPath, [string[]]$ExtraArgs = @())
    Write-Host ''
    Write-Host ('===== ' + $Title + ' =====')
    if (-not (Test-Path -LiteralPath $ScriptPath)) {
        Write-Host ('  [FAIL] 找不到 ' + $ScriptPath)
        $script:Fail++
        return
    }
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + $ExtraArgs
    $output = & $hostExe @arguments 2>&1
    $code = $LASTEXITCODE
    foreach ($line in $output) { Write-Host ('  ' + $line) }

    if ($code -eq 3) {
        Write-Host '  -> 环境不满足，跳过（不算失败）'
        $script:Skipped += $Title
        return
    }

    $p = 0; $f = 0
    foreach ($line in $output) {
        $m = [regex]::Match([string]$line, '通过 (\d+) 项, 失败 (\d+) 项')
        if ($m.Success) {
            $p = [int]$m.Groups[1].Value
            $f = [int]$m.Groups[2].Value
        }
    }
    $script:Pass += $p
    $script:Fail += $f
    if ($code -ne 0 -and $f -eq 0) { $script:Fail++ }
}

Write-Host ''
Write-Host '##########################################################'
Write-Host ' 萌新工具箱 · 全套测试'
Write-Host '##########################################################'
Write-Host (' 工程: ' + $root)

Invoke-Suite -Title '编码红线体检' -ScriptPath (Join-Path $root 'tools\Test-Encoding.ps1')
Invoke-Suite -Title '内联脚本与清单体检' -ScriptPath (Join-Path $root 'tools\Test-InlineSyntax.ps1')

# 挑组：透传给两个套件（组标记各自独立，所以同一个 -Only 会同时作用在两层上；
# 命令行那边的公共前置 A 组由 Test-Cli 自己说明、由 tools\Test-Quick.ps1 自动补）
$groupArgs = @()
if (@($Only | Where-Object { $_ }).Count -gt 0) { $groupArgs += @('-Only', (@($Only) -join ',')) }
if (@($Skip | Where-Object { $_ }).Count -gt 0) { $groupArgs += @('-Skip', (@($Skip) -join ',')) }

Invoke-Suite -Title '命令行回归' -ScriptPath (Join-Path $root 'tests\Test-Cli.ps1') -ExtraArgs $groupArgs
if ($SkipGui) {
    Write-Host ''
    Write-Host '===== 界面回归 ====='
    Write-Host '  -SkipGui 指定了，跳过。'
    $script:Skipped += '界面回归'
} else {
    Invoke-Suite -Title '界面回归' -ScriptPath (Join-Path $root 'tests\Test-Gui.ps1') -ExtraArgs $groupArgs
}

Write-Host ''
Write-Host '##########################################################'
Write-Host (" 合计: 通过 {0} 项, 失败 {1} 项" -f $script:Pass, $script:Fail)
if ($script:Skipped.Count -gt 0) {
    Write-Host (' 跳过: ' + ($script:Skipped -join ' / '))
}
Write-Host '##########################################################'
if ($script:Fail -gt 0) { exit 1 }
exit 0
