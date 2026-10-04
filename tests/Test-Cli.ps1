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
Check 'A04 按钮总数 66（测试期间用户层的按钮会暂停：常用 31 + 系统工具 25 + 清理 8 + 右键 1 + 我的 1）' ((Get-Key $status.Out 'buttons') -eq '66') (Get-Key $status.Out 'buttons')
Check 'A05 内置清单里没有灰色占位按钮了（两个「资源管理器」也接上了真功能；灰规则改由 B 组注入验证）' ((Get-Key $status.Out 'placeholders') -eq '0') (Get-Key $status.Out 'placeholders')
Check 'A06 危险按钮 3 个' ((Get-Key $status.Out 'dangerous') -eq '3') (Get-Key $status.Out 'dangerous')

$tabExpect = @{ 'common' = 31; 'rightmenu' = 1; 'cleanup' = 8; 'system' = 25; 'mine' = 1 }
$tabOk = $true
$tabDetail = @()
foreach ($k in $tabExpect.Keys) {
    $v = Get-Key $status.Out ('tab.' + $k)
    $tabDetail += ($k + '=' + $v)
    if ($v -ne [string]$tabExpect[$k]) { $tabOk = $false }
}
Check 'A07 五个页签的按钮数正确（31/1/8/25/1）' $tabOk ($tabDetail -join ' ')

$list = Invoke-Exe 'list'
Check 'A08 list 退出码 0' ($list.Code -eq 0) ('exit=' + $list.Code)
Check 'A09 list 报的按钮数一致' ((Get-Key $list.Out 'buttons') -eq '66') (Get-Key $list.Out 'buttons')
$lines = @($list.Out -split "`r?`n" | Where-Object { $_ -match "`t" })
Check 'A10 list 打出 66 行按钮' ($lines.Count -eq 66) ('lines=' + $lines.Count)

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

# 灰按钮样本从清单里现取，**不要写死某个按钮**：它哪天被接上真功能，这里就会假红
# （2026-10-04 踩过：「任务栏从不合并」接上真功能后，B 组连带红了 4 条）。
$phId = ''
$phName = ''
foreach ($line in ((Invoke-Exe 'list').Out -split "`r?`n")) {
    if ($line -match 'placeholder') {
        $cells = $line -split "`t"
        $phId = $cells[0]; $phName = $cells[2]; break
    }
}
# 2026-10-04：两个「资源管理器」也接上真功能后，内置清单里一个灰色占位都不剩了。
# 「灰按钮禁止点击」这条规则本身还在（用户自己在 tools.json 里写 placeholder:true 就会灰掉），
# 所以这里临时往用户层塞一个占位按钮来测这条路径，跑完立刻删掉（用户原来的 tools.json 早就请到一边了）。
$script:InjectedPh = $false
if ($phId.Length -eq 0) {
    try {
        [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $UserToolsJson))
        [System.IO.File]::WriteAllText($UserToolsJson,
            '{ "tools": [ { "id": "test.placeholder", "tab": "common", "segment": 2, "order": 999, "name": "占位自检", "kind": "builtin", "module": "todo", "action": "todo", "placeholder": true, "hint": "测试用的占位按钮" } ] }',
            (New-Object System.Text.UTF8Encoding($false)))
        $script:InjectedPh = $true
        foreach ($line in ((Invoke-Exe 'list').Out -split "`r?`n")) {
            if ($line -match 'placeholder') { $cells = $line -split "`t"; $phId = $cells[0]; $phName = $cells[2]; break }
        }
    } catch {
        Write-Host ('  注入占位按钮失败：' + $_.Exception.Message)
    }
}
Check 'B00 能找到灰色占位按钮（内置的没有了就用临时注入的；B01–B05 用它）' ($phId.Length -gt 0) ('id=' + $phId + ' name=' + $phName + ' injected=' + $script:InjectedPh)

$ph = Invoke-Exe ('run ' + $phId)
Check 'B01 灰色按钮 run 退出码 0' ($ph.Code -eq 0) ('exit=' + $ph.Code)
$phHint = Get-Key $ph.Out 'placeholder'
Check 'B02 输出里标明是占位按钮' ($phHint.Length -gt 0) ('placeholder=' + $phHint)
Check 'B03 没有真的执行（命令带"暂不执行"）' ($ph.Out -match '暂不执行') ''
$phDry = Invoke-Exe ('run ' + $phId + ' --dry')
Check 'B03b --dry 明说它没有目标（kind=none）' `
    (((Get-Key $phDry.Out 'kind') -eq 'none') -and ((Get-Key $phDry.Out 'exists') -eq 'no')) `
    ('kind=' + (Get-Key $phDry.Out 'kind') + ' exists=' + (Get-Key $phDry.Out 'exists'))

Start-Sleep -Milliseconds 400
$logAfter = @(Get-Content -LiteralPath $logPath -Encoding UTF8 -ErrorAction SilentlyContinue)
Check 'B04 运行写进了日志（最新一条）' ($logAfter.Count -gt $logBefore) ('before=' + $logBefore + ' after=' + $logAfter.Count)
$newest = ''
if ($logAfter.Count -gt 0) { $newest = $logAfter[$logAfter.Count - 1] }
if ($script:InjectedPh) {
    Remove-Item -LiteralPath $UserToolsJson -Force -ErrorAction SilentlyContinue
    Write-Host '  （临时注入的占位按钮已删除，用户原来的 tools.json 留到最后一段复原）'
}
Check ('B05 日志里能看到「{0}」和""功能待接入""' -f $phName) (($newest -match [regex]::Escape($phName)) -and ($newest -match '功能待接入')) ($newest.Trim())

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
Write-Host 'D 组 · 「系统工具」25 个按钮（Windows 自带组件 + 修复/诊断，--dry 只解析不启动）'

Check 'D01 status 报 14 个系统工具动作、0 个缺失' `
    (((Get-Key $status.Out 'systemTargets') -eq '14') -and ((Get-Key $status.Out 'systemMissing') -eq '0')) `
    ('targets=' + (Get-Key $status.Out 'systemTargets') + ' missing=' + (Get-Key $status.Out 'systemMissing'))

$sysList = Invoke-Exe 'list --tab system'
Check 'D02 系统工具页签 25 个按钮、没有 placeholder' `
    (((Get-Key $sysList.Out 'shown') -eq '25') -and (-not ($sysList.Out -match 'placeholder'))) ''

$sysIds = @()
foreach ($line in ($sysList.Out -split "`r?`n")) {
    if ($line -match "`t") { $sysIds += ($line -split "`t")[0] }
}
Check 'D03 读到 25 个系统工具 id' ($sysIds.Count -eq 25) ($sysIds -join ' ')

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
Check 'D04 25 个系统工具都有目标、且缺了就说明原因' ($bad.Count -eq 0) (($bad -join ' ') + ' ' + ($detail -join ' '))

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

# ---------------------------------------------------------------- G 组：拖进来的东西变成什么按钮
Write-Host ''
Write-Host 'G 组 · 拖进来的东西会变成什么按钮（draft，用户报过快捷方式进来就失败）'

$gTmp = Join-Path $env:TEMP ('mxx1-g-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[void][System.IO.Directory]::CreateDirectory($gTmp)
try {
    $gBat = Join-Path $gTmp 'probe.bat'
    [System.IO.File]::WriteAllText($gBat, "@echo off`r`n", (New-Object System.Text.UTF8Encoding($false)))
    $gTxt = Join-Path $gTmp 'probe.txt'
    [System.IO.File]::WriteAllText($gTxt, 'hi', (New-Object System.Text.UTF8Encoding($false)))

    $dFolder = Invoke-Exe ('draft "' + $gTmp + '"')
    Check 'G01 拖一个文件夹 → 打开它（kind=open）' `
        (((Get-Key $dFolder.Out 'kind') -eq 'open') -and ((Get-Key $dFolder.Out 'target') -eq $gTmp)) `
        ('kind=' + (Get-Key $dFolder.Out 'kind') + ' target=' + (Get-Key $dFolder.Out 'target'))

    $dBat = Invoke-Exe ('draft "' + $gBat + '"')
    Check 'G02 拖一个 .bat → 用 cmd 跑它（kind=script / shell=cmd）' `
        (((Get-Key $dBat.Out 'kind') -eq 'script') -and ((Get-Key $dBat.Out 'shell') -eq 'cmd') -and ((Get-Key $dBat.Out 'path') -eq $gBat)) `
        ('kind=' + (Get-Key $dBat.Out 'kind') + ' shell=' + (Get-Key $dBat.Out 'shell'))

    $dTxt = Invoke-Exe ('draft "' + $gTxt + '"')
    Check 'G03 拖一个普通文件 → 像双击那样打开（kind=open）' `
        (((Get-Key $dTxt.Out 'kind') -eq 'open') -and ((Get-Key $dTxt.Out 'target') -eq $gTxt)) `
        ('kind=' + (Get-Key $dTxt.Out 'kind') + ' target=' + (Get-Key $dTxt.Out 'target'))

    # 快捷方式必须解析成它指向的真程序：用户实测"拖入快捷方式图标程序会失败"，
    # 原来是直接把 .lnk 路径存成按钮（快捷方式一挪就废）。
    $gLnkOk = $false
    $gLnk = Join-Path $gTmp 'probe.lnk'
    try {
        $ws = New-Object -ComObject WScript.Shell
        $sc = $ws.CreateShortcut($gLnk)
        $sc.TargetPath = (Join-Path $env:SystemRoot 'system32\notepad.exe')
        $sc.Arguments = '--lnk-arg'
        $sc.WorkingDirectory = $env:SystemRoot
        $sc.Save()
        $gLnkOk = Test-Path -LiteralPath $gLnk
    } catch { $gLnkOk = $false }

    if ($gLnkOk) {
        $dLnk = Invoke-Exe ('draft "' + $gLnk + '"')
        $lnkPath = Get-Key $dLnk.Out 'path'
        Check 'G04 拖一个快捷方式 → 指向它真正指向的 exe（不是 .lnk 本身）' `
            (((Get-Key $dLnk.Out 'kind') -eq 'exe') -and ($lnkPath -match 'notepad\.exe$')) `
            ('kind=' + (Get-Key $dLnk.Out 'kind') + ' path=' + $lnkPath)
        Check 'G05 快捷方式上带的参数也带过来' ((Get-Key $dLnk.Out 'args') -eq '--lnk-arg') (Get-Key $dLnk.Out 'args')
    } else {
        Check 'G04 拖一个快捷方式 → 指向它真正指向的 exe（不是 .lnk 本身）' $false '这台机器上建不出 .lnk（COM 不可用），跳过'
        Check 'G05 快捷方式上带的参数也带过来' $false '跳过'
    }

    $dNone = Invoke-Exe 'draft'
    Check 'G06 draft 不带参数 → 退出码 2 并给用法' (($dNone.Code -eq 2) -and ($dNone.Err -match '用法')) ('exit=' + $dNone.Code)
} finally {
    Remove-Item -LiteralPath $gTmp -Recurse -Force -ErrorAction SilentlyContinue
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

# ---------------------------------------------------------------- H 组：鼠标悬停说明
Write-Host ''
Write-Host 'H 组 · 悬停说明（用户 2026-10-04 报过「鼠标悬停的说明没有做好」）'

# 悬停说明原来是「按钮名 · 直接可跑的那条命令」：内联脚本按钮于是把整段 PowerShell 摊成一行
# （「一键清理垃圾」有 700 多个字符），而真正写给人的那句 hint 反而不显示。
# tip 命令打印的就是界面塞给 ToolTip 的那个字符串，所以这里能直接断言，不用去动真鼠标。
$tipsAll = Invoke-Exe 'tip'
Check 'H01 tip 退出码 0' ($tipsAll.Code -eq 0) ('exit=' + $tipsAll.Code)
Check 'H02 tip 覆盖了每个按钮（66 个）' ((Get-Key $tipsAll.Out 'tips') -eq '66') (Get-Key $tipsAll.Out 'tips')

$blocks = @{}
$curId = ''
$curLines = @()
foreach ($line in ($tipsAll.Out -split "`r?`n")) {
    if ($line -match '^--- (.+)$') {
        if ($curId) { $blocks[$curId] = $curLines }
        $curId = $Matches[1]; $curLines = @()
        continue
    }
    if ($curId -and ($line -notmatch '^tips=')) { $curLines += $line }
}
if ($curId) { $blocks[$curId] = $curLines }

$hintSource = @{}
foreach ($f in @(Get-ChildItem (Join-Path $root 'tools\*.json'))) {
    $j = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($e in $j.tools) { if ($e.id) { $hintSource[[string]$e.id] = [string]$e.hint } }
}

$noHint = @(); $tooLong = @(); $scriptDump = @()
foreach ($id in @($blocks.Keys)) {
    $lines = @($blocks[$id])
    $joined = ($lines -join "`n")
    if ($hintSource.ContainsKey($id) -and $hintSource[$id].Length -gt 0) {
        if ($joined.IndexOf($hintSource[$id]) -lt 0) { $noHint += $id }
    }
    foreach ($l in $lines) { if ($l.Length -gt 110) { $tooLong += ($id + '(' + $l.Length + '字)') } }
    if ($joined -match 'powershell -Command|EncodedCommand') { $scriptDump += $id }
}
Check ('H03 每个按钮的悬停说明里都有它自己的 hint 文案（查了 {0} 个）' -f $blocks.Count) ($noHint.Count -eq 0) ($noHint -join ' ')
Check 'H04 悬停说明里不再摊开内联脚本正文' ($scriptDump.Count -eq 0) ($scriptDump -join ' ')
Check 'H05 悬停说明每行都不超过 110 字（ToolTip 不换行，太长了会顶出屏幕）' ($tooLong.Count -eq 0) ($tooLong -join ' ')

$tipOne = Invoke-Exe 'tip clean-junk'
Check 'H06 危险按钮的说明写明了"会改动系统、先弹确认框"' ($tipOne.Out -match '会改动系统') ''
Check 'H07 要管理员权限的按钮写明了"会弹 UAC 窗口"' ($tipOne.Out -match 'UAC') ''
$tipMissing = Invoke-Exe 'tip no.such.button'
Check 'H08 tip 一个不存在的 id → 退出码 2' ($tipMissing.Code -eq 2) ('exit=' + $tipMissing.Code)

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
