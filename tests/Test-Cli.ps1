#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Test-Cli.ps1 -- 工具箱命令行回归测试

    覆盖：
      * list / status 的机器可读输出（按钮数、页签分布、占位按钮数）
      * 占位按钮点击路径（run 一个 placeholder 必须有反应、写日志、退出码 0）
      * 「右键增强」真按钮：调用隔壁 permanent-delete-menu 的 PermanentDeleteSetup.exe
      * 输出必须是 UTF-8（中文按钮名不能变成乱码）
      * 错误用法返回退出码 2

    用法: powershell -File tests\Test-Cli.ps1
    退出码: 0 = 全绿, 1 = 有失败
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Exe = Join-Path $root 'bin\Mxx1Toolbox.exe'

$script:Pass = 0
$script:Fail = 0

function Check {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    if ($Ok) { $script:Pass++ } else { $script:Fail++ }
    $flag = 'PASS'; if (-not $Ok) { $flag = 'FAIL' }
    Write-Host ("  [{0}] {1}{2}" -f $flag, $Name, $(if ($Detail) { "   ($Detail)" } else { '' }))
}

if (-not (Test-Path -LiteralPath $Exe)) { throw ('找不到 exe（先跑 build.ps1）: ' + $Exe) }

Write-Host ''
Write-Host '=========================================================='
Write-Host ' 萌新工具箱 · 命令行回归测试'
Write-Host '=========================================================='
Write-Host (' exe : ' + $Exe)
Write-Host ''

# GUI 子系统程序：必须自己起进程、边跑边读，输出按 UTF-8 解
function Invoke-Exe {
    param([string]$ArgLine, [int]$TimeoutSec = 120)
    $si = New-Object System.Diagnostics.ProcessStartInfo
    $si.FileName = $Exe
    $si.Arguments = $ArgLine
    $si.UseShellExecute = $false
    $si.RedirectStandardOutput = $true
    $si.RedirectStandardError = $true
    $si.CreateNoWindow = $true
    $si.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $si.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $si
    [void]$p.Start()
    $tOut = $p.StandardOutput.ReadToEndAsync()
    $tErr = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        try { $p.Kill() } catch { }
        return @{ Code = 'TIMEOUT'; Out = ''; Err = '' }
    }
    $out = ''; $err = ''
    try { $out = $tOut.Result } catch { }
    try { $err = $tErr.Result } catch { }
    return @{ Code = $p.ExitCode; Out = $out; Err = $err }
}

function Get-Key {
    param([string]$Text, [string]$Key)
    $m = [regex]::Match($Text, '(?m)^' + [regex]::Escape($Key) + '=([^\r\n]*)')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return ''
}

# ---------------------------------------------------------------- A 组：status / list
Write-Host 'A 组 · status 与 list'

$status = Invoke-Exe 'status'
Check 'A01 status 退出码 0' ($status.Code -eq 0) ('exit=' + $status.Code)
Check 'A02 中文输出没有乱码（UTF-8）' ($status.Out -match 'name=萌新工具箱') ('name=' + (Get-Key $status.Out 'name'))
Check 'A03 版本号 1.0.0' ((Get-Key $status.Out 'version') -eq '1.0.0') (Get-Key $status.Out 'version')
Check 'A04 按钮总数 61' ((Get-Key $status.Out 'buttons') -eq '61') (Get-Key $status.Out 'buttons')
Check 'A05 占位按钮 52 个' ((Get-Key $status.Out 'placeholders') -eq '52') (Get-Key $status.Out 'placeholders')
Check 'A06 危险按钮 7 个' ((Get-Key $status.Out 'dangerous') -eq '7') (Get-Key $status.Out 'dangerous')

$tabExpect = @{ 'common' = 32; 'rightmenu' = 8; 'cleanup' = 8; 'system' = 12; 'mine' = 1 }
$tabOk = $true
$tabDetail = @()
foreach ($k in $tabExpect.Keys) {
    $v = Get-Key $status.Out ('tab.' + $k)
    $tabDetail += ($k + '=' + $v)
    if ($v -ne [string]$tabExpect[$k]) { $tabOk = $false }
}
Check 'A07 五个页签的按钮数正确（32/8/8/12/1）' $tabOk ($tabDetail -join ' ')

$list = Invoke-Exe 'list'
Check 'A08 list 退出码 0' ($list.Code -eq 0) ('exit=' + $list.Code)
Check 'A09 list 报的按钮数一致' ((Get-Key $list.Out 'buttons') -eq '61') (Get-Key $list.Out 'buttons')
$lines = @($list.Out -split "`r?`n" | Where-Object { $_ -match "`t" })
Check 'A10 list 打出 61 行按钮' ($lines.Count -eq 61) ('lines=' + $lines.Count)

$rmList = Invoke-Exe 'list --tab rightmenu'
Check 'A11 list --tab 只列这个页签的按钮' ((Get-Key $rmList.Out 'shown') -eq '8') (Get-Key $rmList.Out 'shown')
Check 'A12 右键增强里的按钮是"真功能"（不带 placeholder 标记）' (-not ($rmList.Out -match 'placeholder')) ''

# ---------------------------------------------------------------- B 组：占位按钮路径
Write-Host ''
Write-Host 'B 组 · 占位按钮（点一下必须有反应、写日志、退出码 0）'

$logPath = Get-Key $status.Out 'log'
$logBefore = 0
if (Test-Path -LiteralPath $logPath) {
    $logBefore = @(Get-Content -LiteralPath $logPath -Encoding UTF8 -ErrorAction SilentlyContinue).Count
}

$ph = Invoke-Exe 'run flush-dns'
Check 'B01 占位按钮 run 退出码 0' ($ph.Code -eq 0) ('exit=' + $ph.Code)
$phHint = Get-Key $ph.Out 'placeholder'
Check 'B02 输出里标明是占位按钮' ($phHint.Length -gt 0) ('placeholder=' + $phHint)
Check 'B03 没有真的执行（命令带"暂不执行"）' ($ph.Out -match '暂不执行') ''

Start-Sleep -Milliseconds 400
$logAfter = @(Get-Content -LiteralPath $logPath -Encoding UTF8 -ErrorAction SilentlyContinue)
Check 'B04 运行写进了日志（最新一条）' ($logAfter.Count -gt $logBefore) ('before=' + $logBefore + ' after=' + $logAfter.Count)
$newest = ''
if ($logAfter.Count -gt 0) { $newest = $logAfter[$logAfter.Count - 1] }
Check 'B05 日志里能看到「刷新 DNS 缓存」和"功能待接入"' (($newest -match '刷新 DNS 缓存') -and ($newest -match '功能待接入')) ($newest.Trim())

# ---------------------------------------------------------------- C 组：真按钮（调隔壁 exe）
Write-Host ''
Write-Host 'C 组 · 「右键增强」真按钮（零改动集成隔壁 permanent-delete-menu）'

$permdel = Get-Key $status.Out 'permdelExe'
if ($permdel -eq '(未找到)' -or $permdel.Length -eq 0) {
    Check 'C01 找到 PermanentDeleteSetup.exe' $false '没找到（自家工程之外运行时会自动跳过）'
    Check 'C02 permdel.status 能拿到真实状态' $false 'skipped'
} else {
    Check 'C01 找到 PermanentDeleteSetup.exe' $true $permdel
    $st = Invoke-Exe 'run permdel.status'
    Check 'C02 permdel.status 退出码 0' ($st.Code -eq 0) ('exit=' + $st.Code)
    Check 'C03 输出里有引擎的真实字段（installed=）' ($st.Out -match '(?m)^installed=') (($st.Out -split "`r?`n" | Where-Object { $_ -match '^installed=' }) -join '')
    $upd = Invoke-Exe 'run permdel.checkupdate'
    Check 'C04 permdel.checkupdate 退出码 0' ($upd.Code -eq 0) ('exit=' + $upd.Code)
    Check 'C05 输出里有 update= 状态行' ($upd.Out -match '(?m)^update=') (($upd.Out -split "`r?`n" | Where-Object { $_ -match '^update=' }) -join '')
}

# ---------------------------------------------------------------- D 组：用法与错误
Write-Host ''
Write-Host 'D 组 · 错误用法'

$bad = Invoke-Exe 'run no.such.button'
Check 'D01 不存在的按钮返回退出码 2' ($bad.Code -eq 2) ('exit=' + $bad.Code)
$nocommand = Invoke-Exe 'wat'
Check 'D02 不认识的命令返回退出码 2' ($nocommand.Code -eq 2) ('exit=' + $nocommand.Code)
$help = Invoke-Exe 'help'
Check 'D03 help 退出码 0 且有用法' (($help.Code -eq 0) -and ($help.Out -match '用法')) ('exit=' + $help.Code)
$chk = Invoke-Exe 'checkupdate'
Check 'D04 checkupdate 只读、不下载' (($chk.Code -eq 0) -and ($chk.Out -match 'update=disabled')) (($chk.Out -split "`r?`n" | Where-Object { $_ -match '^update=' }) -join '')

# ---------------------------------------------------------------- 汇总
Write-Host ''
Write-Host '----------------------------------------------------------'
Write-Host (" 命令行回归: 通过 {0} 项, 失败 {1} 项" -f $script:Pass, $script:Fail)
Write-Host '----------------------------------------------------------'
if ($script:Fail -gt 0) { exit 1 }
exit 0
