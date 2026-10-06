#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Sync-Skill.ps1 -- 把 skill「mxx1-toolbox」在多处的副本同步成字节一致

    这个 skill 有 3 份副本（仓库里那份是正本，另外两份在仓库外、被 DSH 直接加载）：

      <仓库>\skill\mxx1-toolbox\*.md                      ← 正本（跟 git 走）
      <仓库的上一级>\.dsh\skills\mxx1-toolbox\*.md         ← 工作区级 skill
      %USERPROFILE%\.dsh\skills\mxx1-toolbox\*.md         ← 用户级 skill

    只改一份、忘了另外两份 = 新会话加载到的是旧规则（2026-10-05 踩过）。所以：

      改完 skill  →  powershell -ExecutionPolicy Bypass -File tools\Sync-Skill.ps1
      只想核对    →  powershell -ExecutionPolicy Bypass -File tools\Sync-Skill.ps1 -Check

    退出码: 0 = 三处一致（或已同步成一致）, 1 = 有不一致（-Check 模式）/ 同步失败
#>
[CmdletBinding()]
param(
    # 只核对、不复制
    [switch]$Check,
    # 目标目录（默认那两处；给测试用，可以指到临时目录）
    [string[]]$Targets = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src = Join-Path $root 'skill\mxx1-toolbox'
$name = Split-Path -Leaf $src

if (-not (Test-Path -LiteralPath $src)) { Write-Host ('[FAIL] 找不到 skill 正本 ' + $src); exit 1 }

$srcFiles = @(Get-ChildItem -LiteralPath $src -File -Filter *.md | Sort-Object Name)
if ($srcFiles.Count -eq 0) { Write-Host '[FAIL] skill 正本里一个 .md 都没有'; exit 1 }

if ($Targets.Count -eq 0) {
    # 仓库外那两处：工作区级（仓库的上一级）与用户级。别写字面量绝对路径 —— 编码体检会拦。
    $Targets = @(
        (Join-Path (Split-Path -Parent $root) ('.dsh\skills\' + $name)),
        (Join-Path $env:USERPROFILE ('.dsh\skills\' + $name))
    )
}

function Get-FileHashOf {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

$problems = @()
$copied = 0

foreach ($dir in $Targets) {
    if (-not (Test-Path -LiteralPath $dir)) {
        if ($Check) { $problems += ('目标目录不存在：' + $dir); continue }
        [void][System.IO.Directory]::CreateDirectory($dir)
    }
    foreach ($f in $srcFiles) {
        $dst = Join-Path $dir $f.Name
        if (Test-Path -LiteralPath $dst) {
            if ((Get-FileHashOf $f.FullName) -eq (Get-FileHashOf $dst)) { continue }
        }
        if ($Check) {
            if (Test-Path -LiteralPath $dst) { $problems += ('内容不一致：' + $dst) }
            else { $problems += ('缺文件：' + $dst) }
            continue
        }
        Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
        $copied++
    }
    # 目标里多出来的 .md（正本里已经没有的那份）：留着会被加载成旧规则，报出来
    foreach ($g in @(Get-ChildItem -LiteralPath $dir -File -Filter *.md)) {
        if (@($srcFiles | Where-Object { $_.Name -eq $g.Name }).Count -eq 0) {
            $problems += ('目标里多出一个正本没有的文件：' + $g.FullName)
        }
    }
}

# 复制完再逐个核对一遍（复制不对要当场发现，而不是等新会话加载到旧规则）
Write-Host ''
Write-Host 'skill 三处一致性核对'
Write-Host ('  正本：' + $src)
foreach ($f in $srcFiles) {
    $h = Get-FileHashOf $f.FullName
    Write-Host ('    {0}  {1} 字节  {2}' -f $f.Name, $f.Length, $h.Substring(0, 16))
    foreach ($dir in $Targets) {
        $dst = Join-Path $dir $f.Name
        if (-not (Test-Path -LiteralPath $dst)) { continue }
        $dh = Get-FileHashOf $dst
        if ($dh -ne $h) { $problems += ('复制后仍不一致：' + $dst) }
    }
}
foreach ($dir in $Targets) { Write-Host ('  副本：' + $dir) }

if ($Check -and $copied -eq 0) { Write-Host ''; Write-Host '  （-Check：没有复制任何东西）' }
if ($copied -gt 0) { Write-Host ('  已复制 ' + $copied + ' 个文件') }

Write-Host ''
if ($problems.Count -gt 0) {
    foreach ($p in $problems) { Write-Host ('[FAIL] ' + $p) }
    Write-Host (' 三处不一致：' + $problems.Count + ' 处。跑一次不带参数的本脚本即可同步。')
    exit 1
}
Write-Host ' 三处一致。'
exit 0
