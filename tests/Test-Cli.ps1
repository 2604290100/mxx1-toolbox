#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Test-Cli.ps1 -- 工具箱命令行回归测试

    覆盖：
      * list / status 的机器可读输出（按钮数、页签分布、灰色占位按钮数）
      * 占位按钮点击路径（run 一个 placeholder 必须有反应、写日志、退出码 0）
      * 「右键增强」那一个按钮：解析出隔壁 permanent-delete-menu 的 PermanentDeleteSetup.exe
      * 「系统工具」12 个按钮：--dry 必须解析出目标，缺组件必须说明原因（不静默失灵）
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
    param([string]$ArgLine, [int]$TimeoutSec = 120, [string]$FilePath = $Exe)
    $si = New-Object System.Diagnostics.ProcessStartInfo
    $si.FileName = $FilePath
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
Check 'A03 版本号 1.1.0' ((Get-Key $status.Out 'version') -eq '1.1.0') (Get-Key $status.Out 'version')
Check 'A04 按钮总数 54（右键增强只留 1 个）' ((Get-Key $status.Out 'buttons') -eq '54') (Get-Key $status.Out 'buttons')
Check 'A05 灰色占位按钮 41 个' ((Get-Key $status.Out 'placeholders') -eq '41') (Get-Key $status.Out 'placeholders')
Check 'A06 危险按钮 7 个' ((Get-Key $status.Out 'dangerous') -eq '7') (Get-Key $status.Out 'dangerous')

$tabExpect = @{ 'common' = 32; 'rightmenu' = 1; 'cleanup' = 8; 'system' = 12; 'mine' = 1 }
$tabOk = $true
$tabDetail = @()
foreach ($k in $tabExpect.Keys) {
    $v = Get-Key $status.Out ('tab.' + $k)
    $tabDetail += ($k + '=' + $v)
    if ($v -ne [string]$tabExpect[$k]) { $tabOk = $false }
}
Check 'A07 五个页签的按钮数正确（32/1/8/12/1）' $tabOk ($tabDetail -join ' ')

$list = Invoke-Exe 'list'
Check 'A08 list 退出码 0' ($list.Code -eq 0) ('exit=' + $list.Code)
Check 'A09 list 报的按钮数一致' ((Get-Key $list.Out 'buttons') -eq '54') (Get-Key $list.Out 'buttons')
$lines = @($list.Out -split "`r?`n" | Where-Object { $_ -match "`t" })
Check 'A10 list 打出 54 行按钮' ($lines.Count -eq 54) ('lines=' + $lines.Count)

$rmList = Invoke-Exe 'list --tab rightmenu'
Check 'A11 右键增强只有 1 个按钮' ((Get-Key $rmList.Out 'shown') -eq '1') (Get-Key $rmList.Out 'shown')
Check 'A12 右键增强里的按钮是"真功能"（不带 placeholder 标记）' (-not ($rmList.Out -match 'placeholder')) ''
Check 'A13 右键增强那个按钮叫「永久删除工具」' ($rmList.Out -match '永久删除工具') (($rmList.Out -split "`r?`n" | Where-Object { $_ -match "`t" }) -join '')

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
Write-Host 'C 组 · 「右键增强」那一个按钮（调隔壁 permanent-delete-menu，零改动集成）'

$permdel = Get-Key $status.Out 'permdelExe'
if ($permdel -eq '(未找到)' -or $permdel.Length -eq 0) {
    Check 'C01 找到 PermanentDeleteSetup.exe' $false '没找到（自家工程之外运行时会自动跳过）'
    Check 'C02 按钮解析出隔壁的 exe' $false 'skipped'
    Check 'C03 隔壁 exe 的 status 能跑' $false 'skipped'
} else {
    Check 'C01 找到 PermanentDeleteSetup.exe' $true $permdel
    $guiDry = Invoke-Exe 'run permdel.gui --dry'
    Check 'C02 按钮解析出隔壁的 exe（--dry 不真的启动）' `
        (($guiDry.Code -eq 0) -and ($guiDry.Out -match '(?m)^kind=exe') -and ((Get-Key $guiDry.Out 'exists') -eq 'yes')) `
        ((Get-Key $guiDry.Out 'target'))
    # 直接问隔壁程序：这样"工具箱找得到它、它也真能跑"两件事都被证明了一次
    $st = Invoke-Exe 'status' 120 $permdel
    Check 'C03 隔壁 exe 的 status 能跑（read-only）' (($st.Code -eq 0) -and ($st.Out -match '(?m)^installed=')) `
        (($st.Out -split "`r?`n" | Where-Object { $_ -match '^installed=' }) -join '')
}

# ---------------------------------------------------------------- D 组：系统工具
Write-Host ''
Write-Host 'D 组 · 「系统工具」12 个按钮（Windows 自带组件，--dry 只解析不启动）'

Check 'D01 status 报 12 个系统工具、0 个缺失' `
    (((Get-Key $status.Out 'systemTargets') -eq '12') -and ((Get-Key $status.Out 'systemMissing') -eq '0')) `
    ('targets=' + (Get-Key $status.Out 'systemTargets') + ' missing=' + (Get-Key $status.Out 'systemMissing'))

$sysList = Invoke-Exe 'list --tab system'
Check 'D02 系统工具页签 12 个按钮、没有 placeholder' `
    (((Get-Key $sysList.Out 'shown') -eq '12') -and (-not ($sysList.Out -match 'placeholder'))) ''

$sysIds = @()
foreach ($line in ($sysList.Out -split "`r?`n")) {
    if ($line -match "`t") { $sysIds += ($line -split "`t")[0] }
}
Check 'D03 读到 12 个系统工具 id' ($sysIds.Count -eq 12) ($sysIds -join ' ')

$bad = @()
$detail = @()
foreach ($id in $sysIds) {    $d = Invoke-Exe ('run ' + $id + ' --dry')
    $exists = Get-Key $d.Out 'exists'
    $hint = Get-Key $d.Out 'hint'
    $target = Get-Key $d.Out 'target'
    if (($d.Code -ne 0) -or ($target.Length -eq 0)) { $bad += ($id + ':解析失败'); continue }
    # 要么目标在这台机器上存在，要么必须给出一句"为什么没有"的说明 —— 不许静默失灵
    if ($exists -ne 'yes' -and $hint.Length -eq 0) { $bad += ($id + ':没有解释'); continue }
    $detail += ($id + '=' + (Get-Key $d.Out 'kind'))
}
Check 'D04 12 个系统工具都有目标、且缺了就说明原因' ($bad.Count -eq 0) (($bad -join ' ') + ' ' + ($detail -join ' '))

$dryMissing = Invoke-Exe 'run no.such.button --dry'
Check 'D05 不存在的按钮 --dry 也是退出码 2' ($dryMissing.Code -eq 2) ('exit=' + $dryMissing.Code)

# ---------------------------------------------------------------- E 组：用法与错误
Write-Host ''
Write-Host 'E 组 · 错误用法'

$badRun = Invoke-Exe 'run no.such.button'
Check 'E01 不存在的按钮返回退出码 2' ($badRun.Code -eq 2) ('exit=' + $badRun.Code)
$nocommand = Invoke-Exe 'wat'
Check 'E02 不认识的命令返回退出码 2' ($nocommand.Code -eq 2) ('exit=' + $nocommand.Code)
$help = Invoke-Exe 'help'
Check 'E03 help 退出码 0 且有用法' (($help.Code -eq 0) -and ($help.Out -match '用法')) ('exit=' + $help.Code)
$chk = Invoke-Exe 'checkupdate'
Check 'E04 checkupdate 只读、不下载' (($chk.Code -eq 0) -and ($chk.Out -match 'update=disabled')) (($chk.Out -split "`r?`n" | Where-Object { $_ -match '^update=' }) -join '')

# ---------------------------------------------------------------- 汇总
Write-Host ''
Write-Host '----------------------------------------------------------'
Write-Host (" 命令行回归: 通过 {0} 项, 失败 {1} 项" -f $script:Pass, $script:Fail)
Write-Host '----------------------------------------------------------'
if ($script:Fail -gt 0) { exit 1 }
exit 0
