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

# 用户自己加的按钮（%LOCALAPPDATA%\mxx1-toolbox\tools.json）会让按钮数变得不确定，
# 所以先把它请到一边，跑完在最后一段复原（用户可能正开着界面在用，别删）。
$UserToolsJson = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\tools.json'
$UserToolsPaused = $UserToolsJson + '.paused-by-test'
# 自愈：上一次跑测试如果被中断（Ctrl+C / 卡住 / 被沙箱杀掉），用户自己的按钮清单会留在
# ".paused-by-test" 上回不来 —— 用户会以为"我建的按钮没了"。开工前先把它放回去。
if ((Test-Path -LiteralPath $UserToolsPaused) -and (-not (Test-Path -LiteralPath $UserToolsJson))) {
    Move-Item -LiteralPath $UserToolsPaused -Destination $UserToolsJson -Force
    Write-Host '（上一次测试留下的暂停文件已自动放回 tools.json）'
}
$script:UserToolsHad = Test-Path -LiteralPath $UserToolsJson
if ($script:UserToolsHad) {
    if (Test-Path -LiteralPath $UserToolsPaused) { Remove-Item -LiteralPath $UserToolsPaused -Force }
    Move-Item -LiteralPath $UserToolsJson -Destination $UserToolsPaused -Force
}

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
Check 'A03 版本号 1.2.0' ((Get-Key $status.Out 'version') -eq '1.2.0') (Get-Key $status.Out 'version')
Check 'A04 按钮总数 53（右键增强 1 个、Windows 激活已删）' ((Get-Key $status.Out 'buttons') -eq '53') (Get-Key $status.Out 'buttons')
Check 'A05 灰色占位按钮 18 个（界面上点不动）' ((Get-Key $status.Out 'placeholders') -eq '18') (Get-Key $status.Out 'placeholders')
Check 'A06 危险按钮 6 个' ((Get-Key $status.Out 'dangerous') -eq '6') (Get-Key $status.Out 'dangerous')

$tabExpect = @{ 'common' = 31; 'rightmenu' = 1; 'cleanup' = 8; 'system' = 12; 'mine' = 1 }
$tabOk = $true
$tabDetail = @()
foreach ($k in $tabExpect.Keys) {
    $v = Get-Key $status.Out ('tab.' + $k)
    $tabDetail += ($k + '=' + $v)
    if ($v -ne [string]$tabExpect[$k]) { $tabOk = $false }
}
Check 'A07 五个页签的按钮数正确（31/1/8/12/1）' $tabOk ($tabDetail -join ' ')

$list = Invoke-Exe 'list'
Check 'A08 list 退出码 0' ($list.Code -eq 0) ('exit=' + $list.Code)
Check 'A09 list 报的按钮数一致' ((Get-Key $list.Out 'buttons') -eq '53') (Get-Key $list.Out 'buttons')
$lines = @($list.Out -split "`r?`n" | Where-Object { $_ -match "`t" })
Check 'A10 list 打出 53 行按钮' ($lines.Count -eq 53) ('lines=' + $lines.Count)

$rmList = Invoke-Exe 'list --tab rightmenu'
Check 'A11 右键增强只有 1 个按钮' ((Get-Key $rmList.Out 'shown') -eq '1') (Get-Key $rmList.Out 'shown')
Check 'A12 右键增强里的按钮是"真功能"（不带 placeholder 标记）' (-not ($rmList.Out -match 'placeholder')) ''
Check 'A13 右键增强那个按钮叫「永久删除工具」' ($rmList.Out -match '永久删除工具') (($rmList.Out -split "`r?`n" | Where-Object { $_ -match "`t" }) -join '')

# ---------------------------------------------------------------- B 组：灰色占位按钮
Write-Host ''
Write-Host 'B 组 · 灰色占位按钮（界面上禁止点击；命令行只解释、绝不执行）'

$logPath = Get-Key $status.Out 'log'
$logBefore = 0
if (Test-Path -LiteralPath $logPath) {
    $logBefore = @(Get-Content -LiteralPath $logPath -Encoding UTF8 -ErrorAction SilentlyContinue).Count
}

$ph = Invoke-Exe 'run taskbar-never-combine'
Check 'B01 灰色按钮 run 退出码 0' ($ph.Code -eq 0) ('exit=' + $ph.Code)
$phHint = Get-Key $ph.Out 'placeholder'
Check 'B02 输出里标明是占位按钮' ($phHint.Length -gt 0) ('placeholder=' + $phHint)
Check 'B03 没有真的执行（命令带"暂不执行"）' ($ph.Out -match '暂不执行') ''
$phDry = Invoke-Exe 'run taskbar-never-combine --dry'
Check 'B03b --dry 明说它没有目标（kind=none）' `
    (((Get-Key $phDry.Out 'kind') -eq 'none') -and ((Get-Key $phDry.Out 'exists') -eq 'no')) `
    ('kind=' + (Get-Key $phDry.Out 'kind') + ' exists=' + (Get-Key $phDry.Out 'exists'))

Start-Sleep -Milliseconds 400
$logAfter = @(Get-Content -LiteralPath $logPath -Encoding UTF8 -ErrorAction SilentlyContinue)
Check 'B04 运行写进了日志（最新一条）' ($logAfter.Count -gt $logBefore) ('before=' + $logBefore + ' after=' + $logAfter.Count)
$newest = ''
if ($logAfter.Count -gt 0) { $newest = $logAfter[$logAfter.Count - 1] }
Check 'B05 日志里能看到「任务栏从不合并」和"功能待接入"' (($newest -match '任务栏从不合并') -and ($newest -match '功能待接入')) ($newest.Trim())

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

# 参数里的 %变量% 必须展开。path 一直是展开的，args 曾经原样传给程序 ——
# 「hosts 修改」于是让记事本去开一个字面量路径 "%SystemRoot%\System32\drivers\etc\hosts"，
# 用户看到的是"hosts 修改没有正常打开"（2026-10-04 实测报的）。
# 只查 kind=exe：脚本的 inline 交给 cmd 执行时，%VAR% 本来就该由 cmd 自己展开。
$allList = Invoke-Exe 'list'
$realIds = @()
foreach ($line in ($allList.Out -split "`r?`n")) {
    if (($line -match "`t") -and ($line -notmatch 'placeholder')) { $realIds += ($line -split "`t")[0] }
}
$leftover = @()
foreach ($id in $realIds) {
    $d = Invoke-Exe ('run ' + $id + ' --dry')
    if ((Get-Key $d.Out 'kind') -ne 'exe') { continue }
    $cmd = Get-Key $d.Out 'command'
    if ($cmd -match '%[A-Za-z_][A-Za-z0-9_]*%') { $leftover += ($id + ' -> ' + $cmd) }
}
Check ('D06 真按钮的参数里不残留 %变量%（查了 {0} 个真按钮）' -f $realIds.Count) ($leftover.Count -eq 0) ($leftover -join ' / ')

$hostsDry = Invoke-Exe 'run hosts-edit --dry'
Check 'D07 hosts 修改的参数展开成真的 hosts 路径' `
    (((Get-Key $hostsDry.Out 'command') -match 'drivers\\etc\\hosts$') -and ((Get-Key $hostsDry.Out 'command') -notmatch '%')) `
    (Get-Key $hostsDry.Out 'command')

# ---------------------------------------------------------------- F 组：工具目录（bin-tools）
Write-Host ''
Write-Host 'F 组 · 外部工具目录 bin-tools（外部工具丢进去就能用）'

$toolDir = Get-Key $status.Out 'toolDir'
Check 'F01 status 报出工具目录，名字是 bin-tools' `
    (($toolDir.Length -gt 0) -and ($toolDir -match 'bin-tools$') -and ((Get-Key $status.Out 'toolDirName') -eq 'bin-tools')) `
    $toolDir

# 工具目录优先：把隔壁那份临时拷进 bin-tools，按钮就应该指向工具目录里的那一份
# （只 --dry 解析，绝不执行；跑完删掉副本）
if ($permdel -ne '(未找到)' -and $permdel.Length -gt 0 -and $toolDir.Length -gt 0) {
    $probeCopy = Join-Path $toolDir 'PermanentDeleteSetup.exe'
    $createdDir = -not (Test-Path -LiteralPath $toolDir)
    try {
        [void][System.IO.Directory]::CreateDirectory($toolDir)
        Copy-Item -LiteralPath $permdel -Destination $probeCopy -Force
        $d2 = Invoke-Exe 'run permdel.gui --dry'
        $t2 = Get-Key $d2.Out 'target'
        Check 'F02 工具目录里的 exe 优先于隔壁仓库那份' ($t2 -match 'bin-tools') $t2
    } finally {
        Remove-Item -LiteralPath $probeCopy -Force -ErrorAction SilentlyContinue
        if ($createdDir) { Remove-Item -LiteralPath $toolDir -Force -ErrorAction SilentlyContinue }
    }
    $d3 = Invoke-Exe 'run permdel.gui --dry'
    Check 'F03 删掉副本后又回到隔壁仓库那份（查找顺序没写死）' `
        ((Get-Key $d3.Out 'target') -notmatch 'bin-tools') (Get-Key $d3.Out 'target')
} else {
    Check 'F02 工具目录里的 exe 优先于隔壁仓库那份' $false '没找到隔壁 exe 或工具目录，跳过'
    Check 'F03 删掉副本后又回到隔壁仓库那份（查找顺序没写死）' $false 'skipped'
}

# 相对路径按"工具箱目录 / bin-tools"解析（写一个临时用户层 tools.json，跑完按原样复原）
$userTools = Get-Key $status.Out 'userTools'
if ($userTools.Length -gt 0 -and $toolDir.Length -gt 0) {
    $userBackup = $null
    $hadUser = Test-Path -LiteralPath $userTools
    if ($hadUser) { $userBackup = [System.IO.File]::ReadAllText($userTools, [System.Text.Encoding]::UTF8) }
    $createdDir2 = -not (Test-Path -LiteralPath $toolDir)
    try {
        [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $userTools))
        [void][System.IO.Directory]::CreateDirectory($toolDir)
        [System.IO.File]::WriteAllText($userTools,
            '{ "tools": [ { "id": "test.rel", "tab": "mine", "name": "rel-probe", "kind": "exe", "path": "__probe__.txt" } ] }',
            (New-Object System.Text.UTF8Encoding($false)))
        [System.IO.File]::WriteAllText((Join-Path $toolDir '__probe__.txt'), 'probe', (New-Object System.Text.UTF8Encoding($false)))
        $rel = Invoke-Exe 'run test.rel --dry'
        $t3 = Get-Key $rel.Out 'target'
        Check 'F04 相对路径按工具目录解析（不再是进程当前目录）' `
            (($rel.Code -eq 0) -and ($t3 -match 'bin-tools') -and ((Get-Key $rel.Out 'exists') -eq 'yes')) $t3
    } finally {
        Remove-Item -LiteralPath (Join-Path $toolDir '__probe__.txt') -Force -ErrorAction SilentlyContinue
        if ($createdDir2) { Remove-Item -LiteralPath $toolDir -Force -ErrorAction SilentlyContinue }
        if ($hadUser) { [System.IO.File]::WriteAllText($userTools, $userBackup, (New-Object System.Text.UTF8Encoding($false))) }
        else { Remove-Item -LiteralPath $userTools -Force -ErrorAction SilentlyContinue }
    }
} else {
    Check 'F04 相对路径按工具目录解析（不再是进程当前目录）' $false 'status 没给出 userTools / toolDir'
}

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
if ($script:UserToolsHad -and (Test-Path -LiteralPath $UserToolsPaused)) {
    if (Test-Path -LiteralPath $UserToolsJson) { Remove-Item -LiteralPath $UserToolsJson -Force }
    Move-Item -LiteralPath $UserToolsPaused -Destination $UserToolsJson -Force
    Write-Host '（用户自己的 tools.json 已复原）'
}
if ($script:Fail -gt 0) { exit 1 }
exit 0
