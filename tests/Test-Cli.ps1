#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Test-Cli.ps1 -- 工具箱命令行回归测试

    覆盖：
      * list / status 的机器可读输出（按钮数、页签分布、灰色占位按钮数、系统版本、条款状态）
      * 占位按钮点击路径（run 一个 placeholder 必须有反应、写日志、退出码 0）
      * 「右键增强」那一个按钮：解析出隔壁 permanent-delete-menu 的 PermanentDeleteSetup.exe
      * 「系统工具」12 个按钮：--dry 必须解析出目标，缺组件必须说明原因（不静默失灵）
      * 「工具目录自动长按钮」（R 组）：tool.json / 光一个 exe / 多个 exe 说不清 / id 撞车 / 坏 JSON
      * 条款确认门与更新检查（S 组）：disclaimer / consent / checkupdate，用本机假接口不碰外网
      * 兼容性（A03b–A03d / D01）：按这台机器是哪一版 Windows 分叉断言（见 docs\DESIGN.md §16）
      * 输出必须是 UTF-8（中文按钮名不能变成乱码）
      * 错误用法返回退出码 2

    用法: powershell -File tests\Test-Cli.ps1
    退出码: 0 = 全绿（含"环境不满足、跳过"）, 1 = 有失败
    环境不满足的项走 Skip()：打印 [SKIP]、计入跳过数，**不算失败**（在别人的机器上不会假红）。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Exe = Join-Path $root 'bin\Mxx1Toolbox.exe'

$script:Pass = 0
$script:Fail = 0
$script:Skip = 0

function Check {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    if ($Ok) { $script:Pass++ } else { $script:Fail++ }
    $flag = 'PASS'; if (-not $Ok) { $flag = 'FAIL' }
    Write-Host ("  [{0}] {1}{2}" -f $flag, $Name, $(if ($Detail) { "   ($Detail)" } else { '' }))
}

# 环境不满足、或者"再往下做就要动用户文件"的项走这里：**不装作通过，也不误报失败**。
# 原来这些地方写的是 Check ... $false 'skipped' —— 那是把"没测到"记成"失败"，
# 在别人机器上（没有隔壁仓库、工具目录里已经放了 exe、没装 .NET 的 COM 等）会一片假红。
function Skip {
    param([string]$Name, [string]$Reason = '')
    $script:Skip++
    Write-Host ("  [SKIP] {0}{1}" -f $Name, $(if ($Reason) { "   ($Reason)" } else { '' }))
}

if (-not (Test-Path -LiteralPath $Exe)) { throw ('找不到 exe（先跑 build.ps1）: ' + $Exe) }

# 命令行套件里**任何**调用工具箱的路径都不许去修补用户真实的右键菜单：pin / unpin 会走
# RightMenu.SyncIfInstalled()，那条路 2026-10-04 真的把用户真实菜单的 Icon 和占位符改掉了
# ——当时的 M22 只比"键名"，所以没拦住（现在 M22c 连键值一起比）。
# M20c 专门测那条修补路径，它在隔离根里自己把开关打开（MXX1_NO_RIGHTMENU_SYNC=0）。
$script:SyncHad = Test-Path Env:MXX1_NO_RIGHTMENU_SYNC
$script:SyncOld = $env:MXX1_NO_RIGHTMENU_SYNC
$env:MXX1_NO_RIGHTMENU_SYNC = '1'

# 用户自己加的按钮（%LOCALAPPDATA%\mxx1-toolbox\tools.json）会让按钮数变得不确定，
# 所以先把它请到一边，跑完在最后一段复原（用户可能正开着界面在用，别删）。
$UserToolsJson = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\tools.json'
$UserToolsPaused = $UserToolsJson + '.paused-by-test'

# ---- 用户层"防串味"三道闸（2026-10-04 真出过事，所以写死在这儿）----------------------------
# 事故现场：测试中途崩在 F 组（脚本级错误 → 收尾没跑到），把**注入的测试按钮**留在了用户的
# tools.json 里；下一次跑测试看到"暂停文件在、用户文件也在"，于是删掉暂停文件、把**注入的那份**
# 当成用户内容挪走 —— 用户自己那个「EcoPaste」按钮就这么没了（后来从 tools.json.broken-bak 里捞回来）。
# 闸 ①：只有 id 以 test. 开头（那是我们注入的）的文件，才允许被丢；
# 闸 ②：暂停文件里只要有一个**不是** test.* 的项，它就是用户真实内容，谁都不许删；
# 闸 ③：开工先把注入按钮从用户文件里清掉 —— 崩在哪儿都别指望"收尾那段"能跑到。
#
# 解析前先把"看得懂但不合法"的转义补成合法的（2026-10-04 真踩过：用户 tools.json 的 `_comment`
# 里写了一个 `\*`，PowerShell 的 ConvertFrom-Json 直接抛错 → 这道闸整段跳过 → 连"把暂停文件放回
# tools.json"都做不成，用户会以为自己的按钮没了）。只影响解析，不动用户文件。
function Repair-JsonEscapes([string]$Text) {
    $sb = New-Object System.Text.StringBuilder
    $i = 0
    while ($i -lt $Text.Length) {
        $ch = $Text[$i]
        if ($ch -ne '\') { [void]$sb.Append($ch); $i++; continue }
        $next = ''
        if (($i + 1) -lt $Text.Length) { $next = [string]$Text[$i + 1] }
        if (($next.Length -gt 0) -and ('"\bfnrtu/'.IndexOf($next) -ge 0)) {
            [void]$sb.Append('\'); [void]$sb.Append($next)
        } else {
            [void]$sb.Append('\\'); [void]$sb.Append($next)
        }
        $i += 2
    }
    return $sb.ToString()
}

function Read-JsonLoose([string]$Path) {
    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    try { return ($raw | ConvertFrom-Json) } catch { }
    return ((Repair-JsonEscapes $raw) | ConvertFrom-Json)
}

function Test-OnlyInjectedFixtures([string]$Path) {
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return $false }
        $obj = Read-JsonLoose $Path
        $tools = @($obj.tools)
        if ($tools.Count -eq 0) { return $false }
        foreach ($t in $tools) { if ("$($t.id)" -notlike 'test.*') { return $false } }
        return $true
    } catch { return $false }
}

function Clear-InjectedFixtures([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        $obj = Read-JsonLoose $Path
        $tools = @($obj.tools)
        $keep = @($tools | Where-Object { "$($_.id)" -notlike 'test.*' })
        if ($keep.Count -eq $tools.Count) { return }          # 没有我们注入的东西，什么都不动
        if ($keep.Count -eq 0) {
            Remove-Item -LiteralPath $Path -Force
            Write-Host '（上一次测试没收拾干净：整个 tools.json 都是注入的测试按钮，已删除）'
        } else {
            $obj.tools = $keep
            $json = ($obj | ConvertTo-Json -Depth 8)
            [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
            Write-Host ('（上一次测试没收拾干净：从 tools.json 里清掉了 ' + ($tools.Count - $keep.Count) + ' 个注入按钮）')
        }
    } catch { Write-Host ('（清理注入按钮时出错，跳过：' + $_.Exception.Message + '）') }
}

# 闸 ①②：上一次跑测试如果被中断（Ctrl+C / 卡住 / 被沙箱杀掉 / 脚本级错误），用户自己的按钮清单
# 会留在 ".paused-by-test" 上回不来 —— 用户会以为"我建的按钮没了"。开工前先把它放回去；
# 但"两个文件都在"时要按内容判断谁是真的（注入的那份全是 test.*，直接丢）。
if (Test-Path -LiteralPath $UserToolsPaused) {
    if (Test-OnlyInjectedFixtures $UserToolsPaused) {
        Remove-Item -LiteralPath $UserToolsPaused -Force
        Write-Host '（暂停文件里全是注入的测试按钮，已丢弃）'
    } else {
        if (Test-Path -LiteralPath $UserToolsJson) {
            if (Test-OnlyInjectedFixtures $UserToolsJson) { Remove-Item -LiteralPath $UserToolsJson -Force }
            else { Remove-Item -LiteralPath $UserToolsPaused -Force }   # 两个都像真的：以先在的那个为准
        }
        if (Test-Path -LiteralPath $UserToolsPaused) {
            Move-Item -LiteralPath $UserToolsPaused -Destination $UserToolsJson -Force
            Write-Host '（上一次测试留下的暂停文件已自动放回 tools.json）'
        }
    }
}
# 闸 ③：把上次崩掉留下的注入按钮从用户文件里清掉（只删 test.*，用户自己的项一个都不动）
Clear-InjectedFixtures $UserToolsJson

$script:UserToolsHad = Test-Path -LiteralPath $UserToolsJson
if ($script:UserToolsHad) {
    if (Test-Path -LiteralPath $UserToolsPaused) { Remove-Item -LiteralPath $UserToolsPaused -Force }
    Move-Item -LiteralPath $UserToolsJson -Destination $UserToolsPaused -Force
}

# 脚本级兜底（2026-10-04 补：Test-Gui 早就有，这边没有 —— 而这个套件同样会把用户的 tools.json
# 暂停到一边，脚本级错误一出现，"收尾那段"就永远不执行，用户会以为自己的按钮没了）。
# 只复原用户层，别的事情什么都不做。
trap {
    Write-Host ''
    Write-Host (' 脚本出错，先把用户的 tools.json 放回去：' + $_.Exception.Message)
    if ($script:UserToolsHad -and (Test-Path -LiteralPath $UserToolsPaused)) {
        if (Test-Path -LiteralPath $UserToolsJson) { Remove-Item -LiteralPath $UserToolsJson -Force }
        Move-Item -LiteralPath $UserToolsPaused -Destination $UserToolsJson -Force
        Write-Host '（用户自己的 tools.json 已复原）'
    }
    exit 1
}

Write-Host ''
Write-Host '=========================================================='
Write-Host ' 萌新工具箱 · 命令行回归测试'
Write-Host '=========================================================='
Write-Host (' exe : ' + $Exe)
Write-Host ''

# GUI 子系统程序：必须自己起进程、边跑边读，输出按 UTF-8 解
function Invoke-Exe {
    param([string]$ArgLine, [int]$TimeoutSec = 120, [string]$FilePath = $Exe, [hashtable]$Env = $null)
    $si = New-Object System.Diagnostics.ProcessStartInfo
    $si.FileName = $FilePath
    $si.Arguments = $ArgLine
    $si.UseShellExecute = $false
    $si.RedirectStandardOutput = $true
    $si.RedirectStandardError = $true
    $si.CreateNoWindow = $true
    $si.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $si.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    if ($Env) { foreach ($k in $Env.Keys) { $si.EnvironmentVariables[$k] = [string]$Env[$k] } }
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

# 读文件头几个字节（判 .ico 的 00 00 01 00 用）。
# PS 5.1 是 `-Encoding Byte`，PowerShell 7 改成了 `-AsByteStream` —— 写成 5.1 那一套在 pwsh 里
# 会当场抛 'Byte' is not a supported encoding name 并把整个套件打断（2026-10-04 用 pwsh 跑测试时踩到，
# 那一次用户的 tools.json 就留在暂停状态了）。所以两边都认。
function Get-FirstBytes {
    param([string]$Path, [int]$Count = 4)
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        return [byte[]](Get-Content -LiteralPath $Path -AsByteStream -TotalCount $Count -ErrorAction SilentlyContinue)
    }
    return [byte[]](Get-Content -LiteralPath $Path -Encoding Byte -TotalCount $Count -ErrorAction SilentlyContinue)
}

# 用户真实右键菜单里那 8 个键的**值**快照（名字 / 图标 / 命令）。只比"键名"是不够的：
# 2026-10-04 就是这样漏掉了一次 —— 测试里 K 组的 pin 走了 SyncIfInstalled，把用户真实菜单的
# Icon 和占位符改掉了，而当时的 M22 只比键名，全绿。
function Get-RightMenuSnapshot {
    $snap = New-Object System.Collections.ArrayList
    foreach ($r in @('*', 'Directory', 'Directory\Background', 'DesktopBackground')) {
        foreach ($v in @('Mxx1Unlock', 'Mxx1Common')) {
            $p = "HKCU:\Software\Classes\$r\shell\$v"
            if (Test-Path -LiteralPath $p) {
                $it = Get-Item -LiteralPath $p
                $c = ''
                if (Test-Path -LiteralPath (Join-Path $p 'command')) { $c = [string](Get-Item -LiteralPath (Join-Path $p 'command')).GetValue('') }
                [void]$snap.Add(($r + '\' + $v + '|' + [string]$it.GetValue('MUIVerb') + '|' + [string]$it.GetValue('Icon') + '|' + $c))
            } else {
                [void]$snap.Add($r + '\' + $v + '|（不存在）')
            }
        }
    }
    return ($snap -join "`n")
}

# 「改动前的原值」两个文件的指纹。自检会临时把它们挪走再放回来，所以比对指纹才知道有没有动过
# 用户的记录 —— 用户自己用过隐私开关 / 系统设置按钮时这两个文件本来就该存在。
function Get-BackupHash {
    $files = @(
        (Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\privacy-original.tsv'),
        (Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\sysreg-original.tsv')
    )
    $parts = @()
    foreach ($f in $files) {
        if (Test-Path -LiteralPath $f) { $parts += (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash }
        else { $parts += 'none' }
    }
    return ($parts -join '|')
}

# ---------------------------------------------------------------- A 组：status / list
Write-Host 'A 组 · status 与 list'

$status = Invoke-Exe 'status'
Check 'A01 status 退出码 0' ($status.Code -eq 0) ('exit=' + $status.Code)
Check 'A02 中文输出没有乱码（UTF-8）' ($status.Out -match 'name=萌新工具箱') ('name=' + (Get-Key $status.Out 'name'))
Check 'A03 版本号 1.5.3' ((Get-Key $status.Out 'version') -eq '1.5.3') (Get-Key $status.Out 'version')

# 兼容性（v1.5.3）：只保证 Win7 / Win10 / Win11。系统工具页里 7 个按钮走的是 ms-settings:
# 这个协议 —— 那是 Windows 10 起才有的「设置」应用，Win7 的注册表里根本没有它。
# 这里按"这台机器是哪一版"分别断言：Win7 上它们必须被算成"这台系统没有"（点下去会得到
# 一句"去控制面板哪儿找"），Win10/11 上必须是可用。
$winName = Get-Key $status.Out 'windows'
$settingsApp = Get-Key $status.Out 'settingsApp'
$sysMissing = 0
[void][int]::TryParse((Get-Key $status.Out 'systemMissing'), [ref]$sysMissing)
Check 'A03b 报得出这台是哪一版 Windows（带清单才拿得到真实版本号）' ($winName -like 'Windows*') ('windows=' + $winName)
if ($winName -eq 'Windows 7') {
    Check 'A03c Win7：没有「设置」应用' ($settingsApp -eq 'no') ('settingsApp=' + $settingsApp)
    Check 'A03d Win7：7 个 ms-settings: 按钮算作这台系统没有（不是静默失败）' ($sysMissing -ge 7) ('systemMissing=' + $sysMissing)
} else {
    Check 'A03c Win10/11：有「设置」应用，ms-settings: 按钮可用' ($settingsApp -eq 'yes') ('settingsApp=' + $settingsApp)
    Check 'A03d 有「设置」应用时它们不算缺组件' ($sysMissing -eq 0) ('systemMissing=' + $sysMissing)
}

# bin-tools 里的工具文件夹会自动长出按钮（v1.5.3，R 组专门测它）。这台机器的工具目录里可能有
# 用户自己放的工具，所以数量基准写成「112 + 自动按钮数」——别把用户的东西当成测试失败。
$autoBase = 0
$autoByTab = @{}
foreach ($line in ($status.Out -split "`r?`n")) {
    if ($line -notmatch '^autoButton=') { continue }
    $cells = ($line.Substring('autoButton='.Length)) -split "`t"
    if ($cells.Count -ge 1 -and $cells[0].Length -gt 0) {
        $autoByTab[$cells[0]] = 1 + [int]$autoByTab[$cells[0]]
        $autoBase++
    }
}

Check ('A04 按钮总数 112 + 工具目录里自动加载的 {0} 个（测试期间用户层的按钮会暂停：常用 33 + 系统工具 26 + 隐私 29 + 应用 5 + 清理 8 + 右键 8 + 我的 3）' -f $autoBase) `
    ((Get-Key $status.Out 'buttons') -eq [string](112 + $autoBase)) (Get-Key $status.Out 'buttons')
Check 'A05 内置清单里没有灰色占位按钮了（两个「资源管理器」也接上了真功能；灰规则改由 B 组注入验证）' ((Get-Key $status.Out 'placeholders') -eq '0') (Get-Key $status.Out 'placeholders')
Check 'A06 危险按钮 3 个' ((Get-Key $status.Out 'dangerous') -eq '3') (Get-Key $status.Out 'dangerous')

# 「常用」页签是合成的（置顶 + 最近使用），清单里没有它的按钮，所以是 0
$tabExpect = @{ 'recent' = 0; 'common' = 33; 'mine' = 3; 'system' = 26; 'cleanup' = 8; 'privacy' = 29; 'apps' = 5; 'rightmenu' = 8 }
$tabOk = $true
$tabDetail = @()
foreach ($k in $tabExpect.Keys) {
    $v = Get-Key $status.Out ('tab.' + $k)
    $tabDetail += ($k + '=' + $v)
    if ($v -ne [string]([int]$tabExpect[$k] + [int]$autoByTab[$k])) { $tabOk = $false }
}
Check 'A07 八个页签的按钮数正确（0/33/3/26/8/29/5/8，加上自动按钮）' $tabOk ($tabDetail -join ' ')

$list = Invoke-Exe 'list'
Check 'A08 list 退出码 0' ($list.Code -eq 0) ('exit=' + $list.Code)
Check 'A09 list 报的按钮数一致' ((Get-Key $list.Out 'buttons') -eq [string](112 + $autoBase)) (Get-Key $list.Out 'buttons')
$lines = @($list.Out -split "`r?`n" | Where-Object { $_ -match "`t" })
Check ('A10 list 打出 {0} 行按钮' -f (112 + $autoBase)) ($lines.Count -eq (112 + $autoBase)) ('lines=' + $lines.Count)

$rmList = Invoke-Exe 'list --tab rightmenu'
Check 'A11 右键增强 8 个按钮（7 个右键菜单 + 隔壁永久删除工具）' ((Get-Key $rmList.Out 'shown') -eq [string](8 + [int]$autoByTab['rightmenu'])) (Get-Key $rmList.Out 'shown')
Check 'A12 右键增强里的按钮是"真功能"（不带 placeholder 标记）' (-not ($rmList.Out -match 'placeholder')) ''
Check 'A13 右键增强那个按钮叫「永久删除工具」' ($rmList.Out -match '永久删除工具') (($rmList.Out -split "`r?`n" | Where-Object { $_ -match "`t" }) -join '')

# ---- A14–A17：exe 自己那张图标（用户 2026-10-04 报「编译好的 Mxx1Toolbox.exe 没有图标」）----
# 根因：build.ps1 里那行 `/win32icon:assets\app.ico` 要的文件**根本不存在** —— 等于从来没写过。
# 一条链上三个环节都得盯着：① app.ico 在且是真 ico；② build.ps1 真的把它交给 csc 了；
# ③ 编出来的 exe 上真能取到那张图（蓝底 + 白方块，和工具箱自己的图标一样 —— .NET 那个默认图标
# 一点纯白都没有，所以"白色像素 > 0"就是判据）。窗口标题栏 / 任务栏那一份由 Test-Gui 的 A04b 盯。
$appIcoPath = Join-Path $root 'assets\app.ico'
Check 'A14 assets\app.ico 在（build.ps1 的 /win32icon 靠它，缺了 exe 就是没图标）' `
    (Test-Path -LiteralPath $appIcoPath) $appIcoPath
if (Test-Path -LiteralPath $appIcoPath) {
    $appIcoBytes = [System.IO.File]::ReadAllBytes($appIcoPath)
    $icoFrames = [int]$appIcoBytes[4] + ([int]$appIcoBytes[5] * 256)
    Check 'A15 app.ico 是真 ico 而且尺寸齐（16/20/24/32/48/64/128/256）' `
        (($appIcoBytes.Length -gt 20000) -and ($appIcoBytes[0] -eq 0) -and ($appIcoBytes[1] -eq 0) -and
         ($appIcoBytes[2] -eq 1) -and ($appIcoBytes[3] -eq 0) -and ($icoFrames -eq 8)) `
        ('bytes=' + $appIcoBytes.Length + ' frames=' + $icoFrames)
} else {
    Skip 'A15 app.ico 是真 ico 而且尺寸齐（16/20/24/32/48/64/128/256）' 'assets\app.ico 不在（先跑 tools\Make-AppIcon.ps1）'
}
$buildSrc = Get-Content -LiteralPath (Join-Path $root 'build.ps1') -Raw -Encoding UTF8
Check 'A16 build.ps1 确实会把 app.ico 交给 csc（/win32icon 那行还在）' `
    (($buildSrc -match '/win32icon') -and ($buildSrc -match 'app\.ico')) ''
Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
$iconBmp = $null
try { $iconBmp = [System.Drawing.Icon]::ExtractAssociatedIcon($Exe).ToBitmap() } catch { $iconBmp = $null }
$iconBlue = 0; $iconWhite = 0; $iconOpaque = 0
if ($iconBmp -ne $null) {
    for ($iy = 0; $iy -lt $iconBmp.Height; $iy++) {
        for ($ix = 0; $ix -lt $iconBmp.Width; $ix++) {
            $ic = $iconBmp.GetPixel($ix, $iy)
            if ($ic.A -gt 200) {
                $iconOpaque++
                if (($ic.B -gt 140) -and ($ic.R -lt 110)) { $iconBlue++ }
                if (($ic.R -gt 220) -and ($ic.G -gt 220) -and ($ic.B -gt 220)) { $iconWhite++ }
            }
        }
    }
    $iconBmp.Dispose()
}
Check 'A17 exe 上真带着工具箱的图标（蓝底 + 白方块，不是 .NET 那个默认的空图标）' `
    (($iconBlue -gt 40) -and ($iconWhite -gt 40)) `
    ('opaque=' + $iconOpaque + ' blue=' + $iconBlue + ' white=' + $iconWhite)

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
Check ('B05 日志里能看到「{0}」和""功能待接入""' -f $phName) `
    (@($logAfter | Select-Object -Last 40 | Where-Object { ($_ -match [regex]::Escape($phName)) -and ($_ -match '功能待接入') }).Count -ge 1) `
    ('最后一行=' + $newest.Trim())

# ---------------------------------------------------------------- C 组：真按钮（调隔壁 exe）
Write-Host ''
Write-Host 'C 组 · 「右键增强」那一个按钮（调隔壁 permanent-delete-menu，零改动集成）'

$permdel = Get-Key $status.Out 'permdelExe'
if ($permdel -eq '(未找到)' -or $permdel.Length -eq 0) {
    # 克隆本仓库的人没有隔壁工程，这三项本来就测不了 —— 记成跳过，不是失败。
    Skip 'C01 找到 PermanentDeleteSetup.exe' '没找到（不在自家工程里跑就会这样：把 exe 放进 bin-tools\ 或设置里指定路径就能测）'
    Skip 'C02 按钮解析出隔壁的 exe' '同上'
    Skip 'C03 隔壁 exe 的 status 能跑' '同上'
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

# 缺组件的口径按系统版本分叉：Win7 上那 7 个 ms-settings: 按钮天生打不开（算"没有"），
# Win10/11 上一个都不该缺。这样才能在两种机器上跑同一套测试。
$missOk = if ($winName -eq 'Windows 7') { $sysMissing -ge 7 } else { $sysMissing -eq 0 }
$d01detail = 'targets=' + (Get-Key $status.Out 'systemTargets') + ' missing=' + (Get-Key $status.Out 'systemMissing') + ' windows=' + $winName
Check 'D01 status 报 20 个系统工具动作（12 组件 + 2 诊断 + 4 权限页 + 2 应用页）、该有的都在' `
    (((Get-Key $status.Out 'systemTargets') -eq '20') -and $missOk) $d01detail

$sysList = Invoke-Exe 'list --tab system'
Check 'D02 系统工具页签 26 个按钮（25 + 系统体检）、没有 placeholder' `
    (((Get-Key $sysList.Out 'shown') -eq '26') -and (-not ($sysList.Out -match 'placeholder'))) ''

$sysIds = @()
foreach ($line in ($sysList.Out -split "`r?`n")) {
    if ($line -match "`t") { $sysIds += ($line -split "`t")[0] }
}
Check 'D03 读到 26 个系统工具 id' ($sysIds.Count -eq 26) ($sysIds -join ' ')

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
Check 'D04 26 个系统工具都有目标、且缺了就说明了原因' ($bad.Count -eq 0) (($bad -join ' ') + ' ' + ($detail -join ' '))

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
    # 工具目录里**本来就有**这一份（用户自己拷进去的）：那就别"拷贝"（源和目标是同一个文件，
    # Copy-Item 会报错），更不许在收尾时把它删掉 —— 那是用户的文件，不是测试的副本。
    $alreadyThere = (($permdel -eq $probeCopy) -or (Test-Path -LiteralPath $probeCopy))
    try {
        [void][System.IO.Directory]::CreateDirectory($toolDir)
        if (-not $alreadyThere) { Copy-Item -LiteralPath $permdel -Destination $probeCopy -Force }
        $d2 = Invoke-Exe 'run permdel.gui --dry'
        $t2 = Get-Key $d2.Out 'target'
        Check 'F02 工具目录里的 exe 优先于隔壁仓库那份' ($t2 -match 'bin-tools') $t2
    } finally {
        if (-not $alreadyThere) { Remove-Item -LiteralPath $probeCopy -Force -ErrorAction SilentlyContinue }
        if ($createdDir) { Remove-Item -LiteralPath $toolDir -Force -ErrorAction SilentlyContinue }
    }
    if ($alreadyThere) {
        # 工具目录里已经有用户自己放的那一份（比如 build.ps1 -Package 拷过、或用户手动放的）：
        # 这一项要求"把副本删掉再看它退回隔壁那份"，而删别人的文件是绝对不做的（见本文件开头那条约定）。
        Skip 'F03 删掉副本后又回到隔壁仓库那份（查找顺序没写死）' `
            '工具目录里本来就有 PermanentDeleteSetup.exe（不是测试拷的），不能删用户的文件；"bin-tools 优先"已由 F02 覆盖'
    } else {
        $d3 = Invoke-Exe 'run permdel.gui --dry'
        Check 'F03 删掉副本后又回到隔壁仓库那份（查找顺序没写死）' `
            ((Get-Key $d3.Out 'target') -notmatch 'bin-tools') (Get-Key $d3.Out 'target')
    }
} else {
    Skip 'F02 工具目录里的 exe 优先于隔壁仓库那份' '没找到隔壁 exe 或工具目录（克隆仓库的人会这样）'
    Skip 'F03 删掉副本后又回到隔壁仓库那份（查找顺序没写死）' '同上'
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

# ---------------------------------------------------------------- R 组：工具目录里的工具自动长出按钮
Write-Host ''
Write-Host 'R 组 · bin-tools 自动按钮（整个文件夹丢进去就有一个按钮，不用自己写清单）'
# 用户 2026-10-04 问「bin-tools 里面的工具是不是应该自动加载一个按钮？」→ v1.5.3 实现，规则见 src\ToolFolders.cs：
#   ① 文件夹里有 tool.json 就按它建按钮（字段和 tools\*.json 一样）；
#   ② 没有 tool.json、但只有一个 exe（或正好有个和文件夹同名的 exe）→ 也建一个；
#   ③ 好几个 exe 又对不上名字 → **不猜**，只在日志里说一句；
#   ④ 自动按钮**绝不覆盖**已有按钮（内置清单 / 用户层），重名就跳过并说明；
#   ⑤ 一个文件夹的清单坏了只跳过它自己，不能连累别的按钮。
# 测试只建自己那几个 `__mxx1-autotest-*` 文件夹，收尾也只删自己建的那几个。

$fixtureExe = ''
foreach ($cand in @((Join-Path $env:SystemRoot 'System32\where.exe'), (Join-Path $env:SystemRoot 'System32\cmd.exe'))) {
    if (Test-Path -LiteralPath $cand) { $fixtureExe = $cand; break }
}

# 1x1 的透明 PNG —— 只用来证明"文件夹里的 PNG 会被当成按钮图标"，不参与画图
$png1x1 = [byte[]]@(
    0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A, 0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
    0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,
    0x89,0x00,0x00,0x00,0x0A,0x49,0x44,0x41,0x54,0x78,0x9C,0x63,0x00,0x01,0x00,0x00,
    0x05,0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,
    0x42,0x60,0x82)
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$fixtureNames = @('__mxx1-autotest-a__', '__mxx1-autotest-b__', '__mxx1-autotest-c__', '__mxx1-autotest-d__', '__mxx1-autotest-e__')
$fixtureDirs = @($fixtureNames | ForEach-Object { Join-Path $toolDir $_ })
$fixturePreexisting = @($fixtureDirs | Where-Object { Test-Path -LiteralPath $_ })

if ($fixtureExe.Length -eq 0) {
    Skip 'R01 造夹具文件夹（一个真 exe + 几种 tool.json）' '这台机器上找不到可以当夹具的 exe（where.exe / cmd.exe 都没有？）'
} elseif ($fixturePreexisting.Count -gt 0) {
    # 只碰自己建的东西：同名文件夹本来就在，就不敢删（可能是用户的），整组跳过
    Skip 'R01 造夹具文件夹（一个真 exe + 几种 tool.json）' ('这些文件夹本来就在，不敢删：' + ($fixturePreexisting -join ' '))
} else {
    $userTools = Get-Key $status.Out 'userTools'
    $userBackupR = $null
    $hadUserR = Test-Path -LiteralPath $userTools
    if ($hadUserR) { $userBackupR = [System.IO.File]::ReadAllText($userTools, [System.Text.Encoding]::UTF8) }
    $createdRoot = -not (Test-Path -LiteralPath $toolDir)
    try {
        [void][System.IO.Directory]::CreateDirectory($toolDir)
        foreach ($d in $fixtureDirs) { [void][System.IO.Directory]::CreateDirectory($d) }

        # A：完整的 tool.json（自己写 id / 名字 / 相对路径），图标也自己指定
        $a = $fixtureNames[0]
        [System.IO.File]::WriteAllText((Join-Path $toolDir ($a + '\tool.json')),
            '{ "id": "test.auto1", "tab": "mine", "name": "自动按钮A", "kind": "exe", "path": "' + $a + '\\probe.exe", "icon": "' + $a + '\\probe.png", "hint": "R 组夹具" }',
            $utf8NoBom)
        Copy-Item -LiteralPath $fixtureExe -Destination (Join-Path $toolDir ($a + '\probe.exe')) -Force
        [System.IO.File]::WriteAllBytes((Join-Path $toolDir ($a + '\probe.png')), $png1x1)

        # B：没有 tool.json，只有一个和文件夹同名的 exe + 同名 png → 照样长出按钮（id / 名字 / 路径 / 图标全自动）
        $b = $fixtureNames[1]
        Copy-Item -LiteralPath $fixtureExe -Destination (Join-Path $toolDir ($b + '\' + $b + '.exe')) -Force
        [System.IO.File]::WriteAllBytes((Join-Path $toolDir ($b + '\' + $b + '.png')), $png1x1)

        # C：没有 tool.json，两个 exe 又都对不上文件夹名 → 有歧义，**不猜**（不建按钮）
        $c = $fixtureNames[2]
        Copy-Item -LiteralPath $fixtureExe -Destination (Join-Path $toolDir ($c + '\alpha.exe')) -Force
        Copy-Item -LiteralPath $fixtureExe -Destination (Join-Path $toolDir ($c + '\beta.exe')) -Force

        # D：tool.json 想用内置按钮的 id → 必须被拒绝（丢个文件夹进来不能偷偷换掉「+ 新建按钮」）
        $d = $fixtureNames[3]
        [System.IO.File]::WriteAllText((Join-Path $toolDir ($d + '\tool.json')),
            '{ "id": "app.newtool", "name": "偷偷换掉新建按钮" }', $utf8NoBom)

        # E：tool.json 语法坏了 → 只跳过它自己（末尾多一个逗号，严格 JSON 里是错的）
        $e = $fixtureNames[4]
        [System.IO.File]::WriteAllText((Join-Path $toolDir ($e + '\tool.json')),
            '{ "name": "坏清单", }', $utf8NoBom)

        $s2 = Invoke-Exe 'status'
        $listMineR = Invoke-Exe 'list --tab mine'
        $auto2 = [int](Get-Key $s2.Out 'autoButtons')
        Check 'R01 工具目录里的文件夹被自动扫出按钮（A + B 两个，C/D/E 不算）' ($auto2 -eq ($autoBase + 2)) ('auto=' + $auto2 + ' before=' + $autoBase)
        Check 'R02 自动按钮的来源写清了是哪个文件夹（右键「查看定义」显示的就是它）' `
            (($s2.Out -match [regex]::Escape($a + '\tool.json')) -and ($s2.Out -match [regex]::Escape($b + '（没有 tool.json'))) `
            (@(($s2.Out -split "`r?`n") | Where-Object { $_ -match '^autoButton=' }) -join ' | ')
        Check 'R03 有歧义的文件夹（两个 exe，都对不上名字）不建按钮、也不瞎猜' (-not ($s2.Out -match [regex]::Escape($c))) ''
        Check 'R04 重名的自动按钮被拒绝（不能覆盖内置按钮的 id）' `
            ((-not ($s2.Out -match [regex]::Escape($d))) -and (-not ($listMineR.Out -match '偷偷换掉')) -and ($listMineR.Out -match '\+ 新建按钮')) ''
        Check 'R05 坏清单只跳过它自己，别的按钮照常在' `
            ((-not ($s2.Out -match [regex]::Escape($e))) -and ([int](Get-Key $s2.Out 'buttons') -ge 112)) (Get-Key $s2.Out 'buttons')
        Check 'R06 坏清单在日志里有说明（不是悄悄吞掉）' `
            (($s2.Err -match [regex]::Escape($e)) -or ($s2.Err -match 'tool\.json')) `
            (($s2.Err -split "`r?`n" | Select-Object -First 3) -join ' | ')

        $dryA = Invoke-Exe 'run test.auto1 --dry'
        $tA = Get-Key $dryA.Out 'target'
        Check 'R07 tool.json 里写的按钮真能用（--dry 指到工具目录里的 exe）' `
            (($dryA.Code -eq 0) -and ($tA -match 'bin-tools') -and ($tA -match 'probe\.exe$') -and ((Get-Key $dryA.Out 'exists') -eq 'yes') -and ((Get-Key $dryA.Out 'name') -eq '自动按钮A')) $tA
        Check 'R08 图标指向文件夹里那张 PNG（tool.json 自己指定的）' ((Get-Key $dryA.Out 'icon') -match 'probe\.png$') (Get-Key $dryA.Out 'icon')

        $dryB = Invoke-Exe ('run auto.' + $b + ' --dry')
        $tB = Get-Key $dryB.Out 'target'
        Check 'R09 没有 tool.json 时 id 自动是 auto.<文件夹名>、名字就是文件夹名' `
            (($dryB.Code -eq 0) -and ((Get-Key $dryB.Out 'id') -eq ('auto.' + $b)) -and ((Get-Key $dryB.Out 'name') -eq $b)) (Get-Key $dryB.Out 'name')
        Check 'R10 自动按钮的 exe 路径写的是相对路径又解析对了' `
            (($tB -match 'bin-tools') -and ($tB -match ([regex]::Escape($b + '\' + $b) + '\.exe$')) -and ((Get-Key $dryB.Out 'exists') -eq 'yes')) $tB
        Check 'R11 文件夹里同名的 PNG 自动当图标（不用手写 icon）' ((Get-Key $dryB.Out 'icon') -match ([regex]::Escape($b) + '\.png$')) (Get-Key $dryB.Out 'icon')

        # 用户层同 id 覆盖自动按钮：用户自己写的永远赢（和内置按钮同一条规矩）
        [System.IO.File]::WriteAllText($userTools,
            '{ "tools": [ { "id": "test.auto1", "tab": "mine", "name": "用户层覆盖", "kind": "exe", "path": "' + $a + '\\probe.exe" } ] }',
            $utf8NoBom)
        $s3 = Invoke-Exe 'status'
        $listMine = Invoke-Exe 'list --tab mine'
        Check 'R12 用户层写同一个 id 时覆盖自动按钮（用户自己的按钮永远赢）' `
            (($listMine.Out -match '用户层覆盖') -and (-not ($listMine.Out -match '自动按钮A'))) `
            (($listMine.Out -split "`r?`n" | Where-Object { $_ -match "`t" }) -join ' | ')
        Check 'R13 被覆盖的那条不再算自动按钮（不重复计数）' ([int](Get-Key $s3.Out 'autoButtons') -eq ($autoBase + 1)) (Get-Key $s3.Out 'autoButtons')
    } finally {
        if ($hadUserR) { [System.IO.File]::WriteAllText($userTools, $userBackupR, $utf8NoBom) }
        else { Remove-Item -LiteralPath $userTools -Force -ErrorAction SilentlyContinue }
        foreach ($d in $fixtureDirs) { Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue }
        if ($createdRoot) { Remove-Item -LiteralPath $toolDir -Force -ErrorAction SilentlyContinue }
    }
    $s4 = Invoke-Exe 'status'
    Check 'R14 夹具删干净了，按钮数回到测试前（只删自己建的文件夹）' `
        ([int](Get-Key $s4.Out 'autoButtons') -eq $autoBase) ('auto=' + (Get-Key $s4.Out 'autoButtons') + ' before=' + $autoBase)
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
        Skip 'G04 拖一个快捷方式 → 指向它真正指向的 exe（不是 .lnk 本身）' '这台机器上建不出 .lnk（WScript.Shell COM 不可用）'
        Skip 'G05 快捷方式上带的参数也带过来' '同上'
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
Check 'E03b help 里写明了更新检查的三条底线（只读版本号 / 不下载不替换 / 可关掉）' `
    (($help.Out -match '不下载不替换') -and ($help.Out -match 'MXX1_NO_UPDATE=1')) ''
$chk = Invoke-Exe 'checkupdate' 60 $Exe @{ MXX1_NO_UPDATE = '1' }
Check 'E04 checkupdate 只读、不下载（关掉联网时一个请求都不发）' `
    (($chk.Code -eq 1) -and ($chk.Out -match 'update=disabled') -and ($chk.Out -match 'MXX1_NO_UPDATE')) `
    (($chk.Out -split "`r?`n" | Where-Object { $_ -match '^update=' }) -join '')

# ---------------------------------------------------------------- H 组：鼠标悬停说明
Write-Host ''
Write-Host 'H 组 · 悬停说明（用户 2026-10-04 报过「鼠标悬停的说明没有做好」）'

# 悬停说明原来是「按钮名 · 直接可跑的那条命令」：内联脚本按钮于是把整段 PowerShell 摊成一行
# （「一键清理垃圾」有 700 多个字符），而真正写给人的那句 hint 反而不显示。
# tip 命令打印的就是界面塞给 ToolTip 的那个字符串，所以这里能直接断言，不用去动真鼠标。
$tipsAll = Invoke-Exe 'tip'
Check 'H01 tip 退出码 0' ($tipsAll.Code -eq 0) ('exit=' + $tipsAll.Code)
Check ('H02 tip 覆盖了每个按钮（112 + 自动 {0} 个）' -f $autoBase) ((Get-Key $tipsAll.Out 'tips') -eq [string](112 + $autoBase)) (Get-Key $tipsAll.Out 'tips')

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

# ---------------------------------------------------------------- I 组：隐私设置页签
Write-Host ''
Write-Host 'I 组 · 隐私设置（成对开关 + 一键还原；写注册表之前的原值会被记下来）'

$pvItems = Invoke-Exe 'privacy items'
Check 'I01 privacy items 退出码 0' ($pvItems.Code -eq 0) ('exit=' + $pvItems.Code)
$pvLines = @($pvItems.Out -split "`r?`n" | Where-Object { $_ -match "`t" })
Check 'I02 11 组隐私开关都在表里' ($pvLines.Count -eq 11) ('数=' + $pvLines.Count)

# 每一组都必须有成对的「关闭 X / 开启 X」按钮 —— 用户拍板的就是"成对开关 + 能一键还原"
$pvList = Invoke-Exe 'list --tab privacy'
$pvIds = @()
foreach ($line in ($pvList.Out -split "`r?`n")) { if ($line -match "`t") { $pvIds += ($line -split "`t")[0] } }
Check 'I03 隐私页签 29 个按钮' ($pvIds.Count -eq 29) ('数=' + $pvIds.Count)
# 配对不按 id 猜（error-report 那组的 id 其实是 privacy-errorreport-off），而是看按钮自己的
# 说明里有没有「关闭「X」」和「开启「X」」—— 这也正是用户看到的那两个按钮。
$missingPair = @()
foreach ($line in $pvLines) {
    $itemName = ($line -split "`t")[1]
    $offN = 0
    $onN = 0
    foreach ($id in $pvIds) {
        if (-not $blocks.ContainsKey($id)) { continue }
        $tip = ($blocks[$id] -join "`n")
        if ($tip.IndexOf('关闭「' + $itemName + '」') -ge 0) { $offN++ }
        if ($tip.IndexOf('开启「' + $itemName + '」') -ge 0) { $onN++ }
    }
    if ($offN -ne 1) { $missingPair += ($itemName + ':关闭按钮 ' + $offN + ' 个') }
    if ($onN -ne 1) { $missingPair += ($itemName + ':开启按钮 ' + $onN + ' 个') }
}
Check 'I04 每一组都有配对的「关闭 / 开启」两个按钮' ($missingPair.Count -eq 0) ($missingPair -join ' ')

# 合规底线：隐私页签上不许出现"关掉安全防线"的按钮（Defender / 防火墙 / UAC / SmartScreen / 实时防护）。
# 这条是从"Windows 激活已删除"那条线延续下来的：宁可少一个按钮，也不代关防线。
$forbidden = @('Defender', '防火墙', 'UAC', 'SmartScreen', '实时防护', '篡改')
$bad = @()
foreach ($id in $pvIds) {
    if (-not $blocks.ContainsKey($id)) { continue }
    # 去掉"需要管理员权限：会弹 UAC 窗口"那行标记：它说的是"会弹 UAC 确认框"，
    # 不是"关闭 UAC"。剩下的文字里出现安全防线关键词才算违规。
    $tip = (($blocks[$id] | Where-Object { $_ -notmatch '需要管理员权限' }) -join "`n")
    foreach ($w in $forbidden) { if ($tip -match $w) { $bad += ($id + '→' + $w) } }
}
Check 'I05 隐私页签上没有任何"关掉安全防线"的按钮（合规底线）' ($bad.Count -eq 0) ($bad -join ' ')

# 只读的状态报告：必须把每个开关都念一遍，而且一个字节都不改
$pvStatus = Invoke-Exe 'privacy status'
Check 'I06 privacy status 退出码 0（只读）' ($pvStatus.Code -eq 0) ('exit=' + $pvStatus.Code)
$notListed = @()
foreach ($line in $pvLines) {
    $name = ($line -split "`t")[1]
    if ($pvStatus.Out.IndexOf($name) -lt 0) { $notListed += $name }
}
Check 'I07 状态报告把每一组开关都念到了' ($notListed.Count -eq 0) ($notListed -join ' ')

# 写入 / 读回 / 还原 这条链路：用工具箱自己的测试键自检，不碰任何真实设置
# 用户自己可能已经有原值记录（用过隐私开关就会生成），所以自检前后要比对文件内容 ——
# 不能简单断言"文件不存在"（用户有记录时那是唯一正确的状态）。
$pvBackupBefore = Get-BackupHash
$pvSelf = Invoke-Exe 'privacy selftest'
Check 'I08 自检通过（记原值 → 写入 → 读回核对 → 还原，含"原来没有这个值"的分支）' `
    (($pvSelf.Code -eq 0) -and ($pvSelf.Out -match 'selftest=pass')) `
    (($pvSelf.Out -split "`r?`n" | Where-Object { $_ -match 'selftest=' }) -join '')
Check 'I09 自检没留下垃圾（测试键已删、用户自己的原值记录原样放回）' `
    ((-not (Test-Path -LiteralPath 'HKCU:\SOFTWARE\mxx1-toolbox\privacy-selftest')) -and `
     ((Get-BackupHash) -eq $pvBackupBefore)) ('before=' + $pvBackupBefore + ' after=' + (Get-BackupHash))

# 命令行故意不提供"真的去改隐私设置"的入口：那只能从界面点（要么弹确认框、要么是可还原的成对开关）
$pvWrite = Invoke-Exe 'privacy set off telemetry'
Check 'I10 命令行没有"直接改隐私设置"的入口（退出码 2）' ($pvWrite.Code -eq 2) ('exit=' + $pvWrite.Code)

# 一键优化必须带确认（它会一次改掉一整页的开关）
$optLine = @($pvList.Out -split "`r?`n" | Where-Object { $_ -match "privacy-optimize`t" })
$optDry = Invoke-Exe 'run privacy-optimize --dry'
Check 'I11 「隐私一键优化」--dry 解析成注册表动作，说明里点明了可还原' `
    (((Get-Key $optDry.Out 'kind') -eq 'registry') -and ((Get-Key $optDry.Out 'target') -match '可一键还原')) `
    ('kind=' + (Get-Key $optDry.Out 'kind') + ' target=' + (Get-Key $optDry.Out 'target'))

# ---------------------------------------------------------------- J 组：应用管理页签
Write-Host ''
Write-Host 'J 组 · 应用管理（只读 + 单个卸载；不做批量、不碰 Edge）'

$appList = Invoke-Exe 'list --tab apps'
$appIds = @()
foreach ($line in ($appList.Out -split "`r?`n")) { if ($line -match "`t") { $appIds += ($line -split "`t")[0] } }
Check 'J01 应用管理页签 5 个按钮' ($appIds.Count -eq 5) ($appIds -join ' ')

# 只读的两个：真的跑一遍（查看启动项 / 查看已安装应用都不改任何东西）
$startup = Invoke-Exe 'run apps-startup'
Check 'J02 「查看启动项」跑得通且只读' (($startup.Code -eq 0) -and ($startup.Out -match 'result=ok')) ('exit=' + $startup.Code)
$listApps = Invoke-Exe 'run apps-list' 300
Check 'J03 「查看已安装应用」跑得通（Appx 列表）' `
    (($listApps.Code -eq 0) -and (($listApps.Out -match '已安装的商店应用') -or ($listApps.Out -match '读不到商店应用'))) `
    (($listApps.Out -split "`r?`n" | Where-Object { $_ -match '已安装的商店应用|读不到商店应用' }) -join '')

# 两个"打开官方页面"的按钮必须解析成 ms-settings 目标
foreach ($pair in @(@('apps-default', 'defaultapps'), @('apps-features', 'appsfeatures'))) {
    $d = Invoke-Exe ('run ' + $pair[0] + ' --dry')
    Check ('J04 「' + $pair[0] + '」--dry 解析成设置页 URI') `
        (((Get-Key $d.Out 'kind') -eq 'url') -and ((Get-Key $d.Out 'target') -match $pair[1])) `
        ('kind=' + (Get-Key $d.Out 'kind') + ' target=' + (Get-Key $d.Out 'target'))
}

# 单个卸载：--dry 只解析，绝不执行（执行会弹一个模态窗口，测试里不能点）
$unDry = Invoke-Exe 'run apps-uninstall --dry'
Check 'J05 「卸载单个应用」--dry 只解析（kind=script）' ((Get-Key $unDry.Out 'kind') -eq 'script') ('kind=' + (Get-Key $unDry.Out 'kind'))

# 合规/安全底线（直接读清单，不靠运行时）：
#  ① 不许有"批量卸载 / 一键卸载 / 卸载 Edge"这种不可逆或破坏系统的按钮；
#  ② 单个卸载只允许 Remove-AppxPackage（当前用户），不许出现 -AllUsers；
#  ③ 卸载前必须二次确认（confirm: true）。
$appsJson = Get-Content -LiteralPath (Join-Path $root 'tools\apps.json') -Raw -Encoding UTF8
$appTools = (ConvertFrom-Json $appsJson).tools
$badNames = @($appTools | Where-Object { $_.name -match '批量|全部|Edge|一键卸载' })
Check 'J06 没有"批量卸载 / 卸载 Edge"这类按钮' ($badNames.Count -eq 0) (($badNames | ForEach-Object { $_.name }) -join ' ')
$un = @($appTools | Where-Object { $_.id -eq 'apps-uninstall' })[0]
Check 'J07 单个卸载只用 Remove-AppxPackage（不带 -AllUsers，只影响当前用户）' `
    (($un.inline -match 'Remove-AppxPackage') -and ($un.inline -notmatch '-AllUsers')) ''
Check 'J08 单个卸载带二次确认（confirm: true）' ($un.confirm -eq $true) ('confirm=' + $un.confirm)

# 那个窗口以前列表里全是英文包名（Microsoft.WindowsCalculator 这种），用户问过
# 「卸载单个应用里面的窗口是不能显示中文是？」。现在从「开始菜单」（shell:AppsFolder /
# Get-StartApps）取中文名，系统组件标【系统组件】并排在最后。
# MXX1_PICKER_LIST_ONLY=1 让脚本只打印列表、不弹窗，所以这条能在命令行回归里真跑一遍。
$pick = Invoke-Exe 'run apps-uninstall' 120 $Exe @{ MXX1_PICKER_LIST_ONLY = '1' }
$pl = @(($pick.Out -split "`r?`n") | Where-Object { $_ -match '　·　' -and $_ -notmatch '^(command|id|name|result|message|exit)=' })
$pickFirst = ''
if ($pl.Count -gt 0) { $pickFirst = $pl[0] }
if ($pl.Count -eq 0) {
    # 列表是空的说明这台机器读不到商店应用（Server 版 / 精简版 Windows，CI 的 runner 也是）：
    # 这两项没得可测 → 跳过，不算失败。J03 已经断言过"读不到商店应用"这条降级路径。
    Skip 'J09 「卸载单个应用」的列表显示中文名（不再是一屏英文包名）' '这台机器上读不到商店应用，列表是空的'
    Skip 'J10 系统组件排在最后，并且标了【系统组件】' '同上'
} else {
    Check 'J09 「卸载单个应用」的列表显示中文名（不再是一屏英文包名）' `
        (($pl.Count -ge 5) -and ($pickFirst -match '（') -and ($pickFirst -match '[^\x00-\x7F]')) `
        ('列表行=' + $pl.Count + '  第一行=' + $pickFirst)
    $pickLast = ''
    if ($pl.Count -gt 0) { $pickLast = $pl[$pl.Count - 1] }
    Check 'J10 系统组件排在最后，并且标了【系统组件】' `
        ((@($pl | Where-Object { $_ -like '【系统组件】*' }).Count -ge 1) -and ($pickLast -like '【系统组件】*')) `
        ('最后一行=' + $pickLast)
}
Check 'J11 列表模式只列不卸（没有真的执行卸载）' `
    (($pick.Code -eq 0) -and (@(($pick.Out -split "`r?`n") | Where-Object { $_ -match '^(已卸载|卸载失败)：' }).Count -eq 0)) `
    ('exit=' + $pick.Code)

# ---------------------------------------------------------------- K 组：自助功能
Write-Host ''
Write-Host 'K 组 · 置顶 / 导入导出 / 提权状态 / 系统体检'

Check 'K01 status 报出当前是否以管理员运行' ((Get-Key $status.Out 'admin') -match '^(yes|no)$') (Get-Key $status.Out 'admin')
Check 'K02 status 报出置顶列表（可能是空）' ($status.Out -match '(?m)^pinned=') ((Get-Key $status.Out 'pinned'))

# 置顶往返：pin → status 里能看到 → unpin → 回到原样。跑完把用户原来的 pinned.txt 放回去。
$pinnedFile = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\pinned.txt'
$pinnedBackup = $null
$hadPinned = Test-Path -LiteralPath $pinnedFile
if ($hadPinned) { $pinnedBackup = [System.IO.File]::ReadAllText($pinnedFile, [System.Text.Encoding]::UTF8) }
try {
    $pinOk = Invoke-Exe 'pin devmgmt'
    $afterPin = Invoke-Exe 'status'
    Check 'K03 pin 一个按钮之后 status 里能看到' `
        (($pinOk.Code -eq 0) -and ((Get-Key $afterPin.Out 'pinned') -match 'devmgmt')) `
        ('pin退出=' + $pinOk.Code + ' pinned=' + (Get-Key $afterPin.Out 'pinned'))
    $unOk = Invoke-Exe 'unpin devmgmt'
    $afterUn = Invoke-Exe 'status'
    Check 'K04 unpin 之后就不在置顶列表里了' `
        (($unOk.Code -eq 0) -and ((Get-Key $afterUn.Out 'pinned') -notmatch 'devmgmt')) `
        ('pinned=' + (Get-Key $afterUn.Out 'pinned'))
    $pinBad = Invoke-Exe 'pin no.such.button'
    Check 'K05 pin 一个不存在的按钮 → 退出码 2' ($pinBad.Code -eq 2) ('exit=' + $pinBad.Code)
} finally {
    if ($hadPinned -and $pinnedBackup -ne $null) { [System.IO.File]::WriteAllText($pinnedFile, $pinnedBackup, (New-Object System.Text.UTF8Encoding($false))) }
    elseif (Test-Path -LiteralPath $pinnedFile) { Remove-Item -LiteralPath $pinnedFile -Force -ErrorAction SilentlyContinue }
}

# 导出 / 导入：这时用户层的按钮被暂停了，所以先看"没东西可导出"这条路是否老实报错
$kTmp = Join-Path $env:TEMP ('mxx1-k-' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.json')
try {
    $noExport = Invoke-Exe ('export "' + $kTmp + '"')
    Check 'K06 没有自建按钮时 export 老实报错（退出码 1）' `
        (($noExport.Code -eq 1) -and ($noExport.Err -match '没什么可导出')) ('exit=' + $noExport.Code)

    # 造一个用户层（两条按钮），再导出 / 导入往返
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $UserToolsJson))
    [System.IO.File]::WriteAllText($UserToolsJson,
        '{ "tools": [ { "id": "test.k1", "tab": "mine", "name": "K1", "kind": "exe", "path": "C:\\Windows\\notepad.exe" }, { "id": "test.k2", "tab": "mine", "name": "K2", "kind": "exe", "path": "C:\\Windows\\notepad.exe" } ] }',
        (New-Object System.Text.UTF8Encoding($false)))
    $ex = Invoke-Exe ('export "' + $kTmp + '"')
    $exported = ''
    try { $exported = (Get-Content -LiteralPath $kTmp -Raw -Encoding UTF8 | ConvertFrom-Json).tools.Count } catch { $exported = '解析失败' }
    Check 'K07 有按钮时 export 写出两份、文件是合法 JSON' (($ex.Code -eq 0) -and ($exported -eq 2)) ('exported=' + $exported)

    $im = Invoke-Exe ('import "' + $kTmp + '"')
    Check 'K08 import 同一份文件 → 全部按 id 覆盖（added=0 replaced=2）' `
        (($im.Code -eq 0) -and ((Get-Key $im.Out 'added') -eq '0') -and ((Get-Key $im.Out 'replaced') -eq '2')) `
        ('added=' + (Get-Key $im.Out 'added') + ' replaced=' + (Get-Key $im.Out 'replaced'))

    $imBad = Invoke-Exe 'import no-such-file-xyz.json'
    Check 'K09 import 一个不存在的文件 → 退出码 1 且说明原因' `
        (($imBad.Code -eq 1) -and ($imBad.Err -match '找不到')) ('exit=' + $imBad.Code)
} finally {
    Remove-Item -LiteralPath $kTmp -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $UserToolsJson -Force -ErrorAction SilentlyContinue
}

$health = Invoke-Exe 'run sys-health' 300
Check 'K10 「系统体检」跑得通而且是只读的' (($health.Code -eq 0) -and ($health.Out -match '系统：')) ('exit=' + $health.Code)
$need = @('系统：', '激活：', '内存：', '磁盘 ', '开机自启项', 'hosts', '管理员：')
$miss = @($need | Where-Object { $health.Out.IndexOf($_) -lt 0 })
Check 'K11 体检报告包含系统/激活/内存/磁盘/自启项/hosts/管理员' ($miss.Count -eq 0) ('缺=' + ($miss -join ' '))

# ---------------------------------------------------------------- L 组：系统设置改动（sysreg）
# 「常用设置」里那 6 对写注册表的按钮现在和隐私开关共用一套「记原值 + 读回核对 + 一键还原」的机制。
Write-Host ''
Write-Host 'L 组 · 系统设置改动（记原值 / 读回核对 / 一键还原；只读命令 + 自检）'

$srItems = Invoke-Exe 'sysreg items'
Check 'L01 sysreg items 退出码 0' ($srItems.Code -eq 0) ('exit=' + $srItems.Code)
$srLines = @($srItems.Out -split "`r?`n" | Where-Object { $_ -match "`t" })
Check 'L02 6 个系统设置开关都在表里（任务栏/开始菜单/驱动/内核隔离/资源管理器/右键菜单）' ($srLines.Count -eq 6) ('数=' + $srLines.Count)

# 每一对都必须是「一个 off 按钮 + 一个 on 按钮」；按钮的 options 不在 list 输出里，所以按
# 方向数数：两个方向的按钮数必须一样多，而且加起来就是这一页那 12 个成对按钮。
$srList = Invoke-Exe 'list --tab common'
$srOn = @($srList.Out -split "`r?`n" | Where-Object { $_ -match 'sysreg/on' })
$srOff = @($srList.Out -split "`r?`n" | Where-Object { $_ -match 'sysreg/off' })
Check 'L03 6 对开关 = 12 个按钮（任务栏/开始菜单/驱动/内核隔离/资源管理器/右键菜单）' `
    (($srOn.Count -eq 6) -and ($srOff.Count -eq 6)) ('on=' + $srOn.Count + ' off=' + $srOff.Count)
Check 'L04 on / off 两个方向的按钮数一样多（成对）' ($srOn.Count -eq $srOff.Count) ('on=' + $srOn.Count + ' off=' + $srOff.Count)
$srExtra = @($srList.Out -split "`r?`n" | Where-Object { $_ -match 'sysreg/(status|restore)' })
Check 'L04b 另有「查看设置改动 / 还原设置改动」两个入口按钮' ($srExtra.Count -eq 2) ('找到=' + $srExtra.Count)

$srStatus = Invoke-Exe 'sysreg status'
Check 'L05 sysreg status 退出码 0（只读）' ($srStatus.Code -eq 0) ('exit=' + $srStatus.Code)
Check 'L06 状态报告里每个开关都有一行' `
    ((@('任务栏按钮合并方式', '开始菜单对齐方式', '驱动自动安装', '内核隔离', '资源管理器样式', '右键菜单样式') | Where-Object { $srStatus.Out.IndexOf($_) -lt 0 }).Count -eq 0) ''

# 命令行故意不提供"直接改系统设置"的入口（和隐私开关一样：只能从界面点成对按钮 + 一键还原）
$srWrite = Invoke-Exe 'sysreg set off taskbar-combine'
Check 'L07 命令行没有"直接改系统设置"的入口（退出码 2）' ($srWrite.Code -eq 2) ('exit=' + $srWrite.Code)

# 自检：DWORD / 字符串 / 整棵键（CLSID 那种覆盖）三种值各走一遍「记原值 → 写入 → 读回 → 还原」
$srBackupBefore = Get-BackupHash
$srSelf = Invoke-Exe 'sysreg selftest'
Check 'L08 自检通过（DWORD / 字符串 / 整棵键三种值都走完记原值 → 写入 → 读回 → 还原）' `
    (($srSelf.Code -eq 0) -and ($srSelf.Out -match 'selftest=pass')) ('exit=' + $srSelf.Code + ' ' + (($srSelf.Out -split "`r?`n" | Select-Object -Last 1)))
Check 'L09 自检覆盖了「原来没有这个值」的分支' ($srSelf.Out -match '原来没有这个值') ''
Check 'L10 自检没留下垃圾（测试键已删、用户自己的原值记录原样放回）' `
    ((-not (Test-Path -LiteralPath 'HKCU:\SOFTWARE\mxx1-toolbox\sysreg-selftest')) -and `
     ((Get-BackupHash) -eq $srBackupBefore)) ('before=' + $srBackupBefore + ' after=' + (Get-BackupHash))

# 合规底线：系统设置这张表里也不许出现安全防线（Defender / 防火墙 / UAC / SmartScreen / 实时防护）
$srText = ($srItems.Out + $srStatus.Out)
$srBad = @('Defender', '防火墙', 'UAC', 'SmartScreen', '实时防护', '篡改') | Where-Object { $srText.IndexOf($_) -ge 0 }
Check 'L11 系统设置开关里没有任何"关掉安全防线"的东西（合规底线）' ($srBad.Count -eq 0) ($srBad -join ' ')

# 只读的那条腿：--dry 把 sysreg 按钮解析成注册表动作，不真的写
$srDry = Invoke-Exe 'run taskbar-never-combine --dry'
Check 'L12 sysreg 按钮 --dry 解析成注册表动作，说明里点明了可一键还原' `
    (((Get-Key $srDry.Out 'kind') -eq 'registry') -and ((Get-Key $srDry.Out 'target') -match '可一键还原')) `
    ('kind=' + (Get-Key $srDry.Out 'kind') + ' target=' + (Get-Key $srDry.Out 'target'))

# ---------------------------------------------------------------- M 组：右键增强（HKCU 右键菜单）
# 2026-10-04 用户定的方案（docs\DESIGN.md §14）：把「解除文件占用」和「常用功能」级联子菜单装进
# Windows 右键菜单，只写 HKCU\Software\Classes（不要管理员、不装 shell 扩展 DLL、不起服务）。
# 这一组盯五件事：
#   ① 只读命令能跑（items / status / help），而且状态里念得出装没装、子菜单几项、上限 30；
#   ② 查占用真能认出占用者（自己锁一个文件，看它认不认那个 PID）—— 用的是 Windows 自带的
#      Restart Manager，不装 handle.exe；
#   ③ 装 / 卸的键结构和微软文档那套写法一致，而且**测试用的根是隔离的**；
#   ④ 命令行没有"直接写注册表"的入口（和 sysreg 同一条规矩）；
#   ⑤ 全程不碰用户真实的右键菜单，收尾把测试根和"原值记录"都还原。
Write-Host ''
Write-Host 'M 组 · 右键增强（装 / 卸 / 状态 / 查占用；写注册表只在界面里点）'

$rmItems = Invoke-Exe 'rightmenu items'
Check 'M01 rightmenu items 退出码 0（只读）' ($rmItems.Code -eq 0) ('exit=' + $rmItems.Code)
$rmLoc = @($rmItems.Out -split "`r?`n" | Where-Object { $_ -match "`t" })
Check 'M02 装 4 个位置（任意文件 / 文件夹 / 文件夹里的空白处 / 桌面空白处）' ($rmLoc.Count -eq 4) ('数=' + $rmLoc.Count)
Check 'M03 两项的名字对得上（解除文件占用 / 常用功能）' `
    (($rmItems.Out.IndexOf('解除文件占用') -ge 0) -and ($rmItems.Out.IndexOf('常用功能') -ge 0)) ''

$rmList = Invoke-Exe 'list --tab rightmenu'
$rmBtns = @($rmList.Out -split "`r?`n" | Where-Object { $_ -match '^rightmenu\.' })
Check 'M04 「右键增强」页签新增 7 个按钮（加上隔壁永久删除工具 = 8 个）' ($rmBtns.Count -eq 7) ('新按钮=' + $rmBtns.Count)
$rmOnOff = @($rmBtns | Where-Object { $_ -match 'rightmenu/(unlock|common)\.(on|off)' })
Check 'M05 装 / 撤是成对的（解除占用一对 + 常用功能一对）' ($rmOnOff.Count -eq 4) ('数=' + $rmOnOff.Count)

$rmStatus = Invoke-Exe 'rightmenu status'
Check 'M06 rightmenu status 退出码 0（只读）' ($rmStatus.Code -eq 0) ('exit=' + $rmStatus.Code)
$rmNeed = @('解除文件占用', '常用功能 子菜单', '菜单里的 exe', '最近使用最多留 30 个')
$rmMiss = @($rmNeed | Where-Object { $rmStatus.Out.IndexOf($_) -lt 0 })
Check 'M07 状态里念了：装没装 / 子菜单几项 / 菜单里的 exe / 最近使用上限 30' ($rmMiss.Count -eq 0) ('缺=' + ($rmMiss -join ' '))

$rmHelp = Invoke-Exe 'rightmenu help'
Check 'M08 说明里写清了怎么卸干净 + 四条底线（系统关键进程不能结束）' `
    (($rmHelp.Code -eq 0) -and ($rmHelp.Out.IndexOf('怎么卸干净') -ge 0) -and ($rmHelp.Out.IndexOf('系统关键进程') -ge 0)) ('exit=' + $rmHelp.Code)

$rmWrite = Invoke-Exe 'rightmenu install'
Check 'M09 命令行没有"直接装右键菜单"的入口（退出码 2）' ($rmWrite.Code -eq 2) ('exit=' + $rmWrite.Code)

$rmDry = Invoke-Exe 'run rightmenu.unlock.on --dry'
Check 'M10 右键增强按钮 --dry 解析成注册表动作（不真的写）' ((Get-Key $rmDry.Out 'kind') -eq 'registry') ('kind=' + (Get-Key $rmDry.Out 'kind'))

# ---- 查占用：自己锁一个文件，看它认不认得（Restart Manager）
$rmDir = Join-Path $env:TEMP 'mxx1-rightmenu-check'
if (Test-Path -LiteralPath $rmDir) { Remove-Item -LiteralPath $rmDir -Recurse -Force }
New-Item -ItemType Directory -Path $rmDir | Out-Null
$rmFile = Join-Path $rmDir 'locked.txt'
Set-Content -LiteralPath $rmFile -Value 'x' -Encoding UTF8
Set-Content -LiteralPath (Join-Path $rmDir 'other.txt') -Value 'y' -Encoding UTF8
$rmChild = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-Command', ("`$fs=[System.IO.File]::Open('" + $rmFile + "','Open','ReadWrite','None'); Start-Sleep 90"))
Start-Sleep -Seconds 2
try {
    $rmQ = Invoke-Exe ('rightmenu unlock --query-only "' + $rmFile + '"')
    Check 'M11 查占用：认出占着文件的那个进程（自己锁的文件报出自己的 PID）' `
        (($rmQ.Code -eq 0) -and ($rmQ.Out -match ('pid=' + $rmChild.Id + '\b'))) `
        ('lockers=' + (Get-Key $rmQ.Out 'lockers') + ' 期望 pid=' + $rmChild.Id)
    Check 'M12 查占用是只读的：没有结束任何进程（那个子进程还活着）' (-not $rmChild.HasExited) ''
    $rmFolder = Invoke-Exe ('rightmenu unlock --query-only "' + $rmDir + '"')
    Check 'M13 文件夹被占用也能查（按里面的文件查，目录本身不登记给系统）' `
        (($rmFolder.Code -eq 0) -and ([int](Get-Key $rmFolder.Out 'lockers') -ge 1)) ('lockers=' + (Get-Key $rmFolder.Out 'lockers'))
}
finally {
    if (-not $rmChild.HasExited) { Stop-Process -Id $rmChild.Id -Force -ErrorAction SilentlyContinue }
}
Start-Sleep -Milliseconds 500
$rmAfter = Invoke-Exe ('rightmenu unlock --query-only "' + $rmFile + '"')
Check 'M14 占用没了就查不到（不谎报还占着）' ((Get-Key $rmAfter.Out 'lockers') -eq '0') ('lockers=' + (Get-Key $rmAfter.Out 'lockers'))

# 没查到人时必须给"确定结论"，而不是一句"查不到"（用户 2026-10-04 就是被这句话弄懵的）：
# SelfCheck 自己去独占打开一次文件 —— 能打开 = 真的没人在用。
Check 'M14b 没查到人时给确定结论：自查能独占打开它，所以"现在真的没人在用"' `
    (((Get-Key $rmAfter.Out 'verdictlocked') -eq 'no') -and ((Get-Key $rmAfter.Out 'verdict') -match '独占打开')) `
    ('verdict=' + (Get-Key $rmAfter.Out 'verdict'))

# 路径压根没传过来（旧版被装到「文件夹里的空白处」/「桌面空白处」时会这样：那两个位置
# 资源管理器不替换 %1，会把字面量传进来）→ 要如实说"路径不存在"，并点明背景位置要用 %V。
$rmPct = Invoke-Exe 'rightmenu unlock --query-only %1'
Check 'M14c 路径没传过来（字面量 %1）时如实说路径不存在，并提示背景位置要用 %V' `
    (((Get-Key $rmPct.Out 'exists') -eq 'no') -and ((Get-Key $rmPct.Out 'verdict') -match '%V')) `
    ('exists=' + (Get-Key $rmPct.Out 'exists') + ' verdict=' + (Get-Key $rmPct.Out 'verdict'))

# ---- 文件夹要往下扫：这是用户报的"右键一个文件夹，没扫描到占用文件"那条。
#      原来只登记文件夹里第一层的文件，第一层只有子文件夹时直接放弃 —— 而占用它的多半是
#      子文件夹里的 Office / PDF 文件。现在按层往下扫（深度 ≤4、≤400 个文件），并且要指名
#      到底是哪个文件被占着。
$rmDeep = Join-Path $env:TEMP 'mxx1-rightmenu-deep'
if (Test-Path -LiteralPath $rmDeep) { Remove-Item -LiteralPath $rmDeep -Recurse -Force }
$rmDeepSub = Join-Path $rmDeep '年报资料'
New-Item -ItemType Directory -Path $rmDeepSub -Force | Out-Null
$rmDeepDoc = Join-Path $rmDeepSub 'Q3报告.txt'
Set-Content -LiteralPath $rmDeepDoc -Value 'x' -Encoding UTF8
$rmDeepChild = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-Command', ("`$fs=[System.IO.File]::Open('" + $rmDeepDoc + "','Open','ReadWrite','None'); Start-Sleep 90"))
Start-Sleep -Seconds 2
try {
    $rmDeepQ = Invoke-Exe ('rightmenu unlock --query-only "' + $rmDeep + '"')
    Check 'M14d 右键文件夹：往下扫到子文件夹里的占用（并指名是哪个文件被占着）' `
        (([int](Get-Key $rmDeepQ.Out 'hits') -ge 1) -and ((Get-Key $rmDeepQ.Out 'file') -match 'Q3报告') -and `
         ([int](Get-Key $rmDeepQ.Out 'scanned') -ge 1) -and ($rmDeepQ.Out -match ('pid=' + $rmDeepChild.Id + '\b'))) `
        ('hits=' + (Get-Key $rmDeepQ.Out 'hits') + ' scanned=' + (Get-Key $rmDeepQ.Out 'scanned') + ' file=' + (Get-Key $rmDeepQ.Out 'file'))
} finally {
    if (-not $rmDeepChild.HasExited) { Stop-Process -Id $rmDeepChild.Id -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $rmDeep) { Remove-Item -LiteralPath $rmDeep -Recurse -Force -ErrorAction SilentlyContinue }
}

# ---- 「它自己在运行」这条线索（2026-10-04 加）：正在运行的程序**不持有文件句柄**（可执行文件是
#      内存映射，加载器读完就把句柄关了），所以 Restart Manager 报不出来、"我自己独占打开试试"
#      也照样成功 —— 可它让文件删不掉、让文件夹松不开。用户报的「右键文件夹说有程序占用着但
#      找不到进程」就是这种：那个文件夹里放着一个正在跑的安装包。
$rmRun = Join-Path $env:TEMP 'mxx1-rightmenu-run'
if (Test-Path -LiteralPath $rmRun) { Remove-Item -LiteralPath $rmRun -Recurse -Force }
New-Item -ItemType Directory -Path $rmRun | Out-Null
Set-Content -LiteralPath (Join-Path $rmRun 'doc.txt') -Value 'x' -Encoding UTF8
$rmRunHolder = Join-Path $rmRun 'holder.exe'
$rmRunSrc = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
try { New-Item -ItemType HardLink -Path $rmRunHolder -Target $rmRunSrc -ErrorAction Stop | Out-Null }
catch { Copy-Item -LiteralPath $rmRunSrc -Destination $rmRunHolder -Force }
$rmRunProc = Start-Process -FilePath $rmRunHolder -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-Command', 'Start-Sleep 90')
Start-Sleep -Seconds 2
try {
    # 等那个进程真的起来再断言：慢机器（CI 的 runner）上 PowerShell 冷启动可能超过 2 秒，
    # 这时候"查不到"是**还没起来**，不是功能坏了 —— 所以轮询到看见它为止（最多约 12 秒）。
    $rmRunQ = $null
    for ($ri = 0; $ri -lt 16; $ri++) {
        $rmRunQ = Invoke-Exe ('rightmenu unlock --query-only "' + $rmRun + '"')
        if ([int](Get-Key $rmRunQ.Out 'run') -ge 1) { break }
        Start-Sleep -Milliseconds 750
    }
    # 注意：RM 有时**也能**把"正在运行的 exe 自己的镜像文件"报成占用（这台机器上实测会），
    # 所以这里不断言 lockers=0，只断言我们这条新线索确实点名了那个进程。
    Check 'M14e 文件夹里有正在运行的程序：单独点出「它自己在运行」（句柄类接口看不见它）' `
        (([int](Get-Key $rmRunQ.Out 'run') -ge 1) -and ($rmRunQ.Out -match ('run\tpid=' + $rmRunProc.Id + '\b'))) `
        ('run=' + (Get-Key $rmRunQ.Out 'run') + ' lockers=' + (Get-Key $rmRunQ.Out 'lockers') + ' 期望 pid=' + $rmRunProc.Id + `
         ' 进程还活着=' + $(if ($rmRunProc -and -not $rmRunProc.HasExited) { 'yes' } else { 'no' }) + `
         ' scanned=' + (Get-Key $rmRunQ.Out 'scanned') + ' truncated=' + (Get-Key $rmRunQ.Out 'truncated') + ' note=' + (Get-Key $rmRunQ.Out 'note'))
} finally {
    if ($rmRunProc -and -not $rmRunProc.HasExited) { Stop-Process -Id $rmRunProc.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 400
    if (Test-Path -LiteralPath $rmRun) { Remove-Item -LiteralPath $rmRun -Recurse -Force -ErrorAction SilentlyContinue }
}

# ---- 结束进程要**连它启动的子进程一起**（用户 2026-10-04 实测：跑 qingjian 安装包，右键结束进程后
#      文件锁松开了（父进程死了）、窗口却还在（窗口是父进程拉起来的那个子进程的））。命令行这边
#      只做**只读预览**（child= 行），真正动手的是界面上那个按钮（GUI 套件 N08 盯着确认框和"取消"）。
$rmTree = Join-Path $env:TEMP 'mxx1-rightmenu-tree'
if (Test-Path -LiteralPath $rmTree) { Remove-Item -LiteralPath $rmTree -Recurse -Force }
New-Item -ItemType Directory -Path $rmTree | Out-Null
$rmTreeFile = Join-Path $rmTree 'locked.txt'
Set-Content -LiteralPath $rmTreeFile -Value 'x' -Encoding UTF8
$rmTreePidFile = Join-Path $rmTree 'child-pid.txt'
$rmTreeHelper = Join-Path $rmTree 'child.ps1'
Set-Content -LiteralPath $rmTreeHelper -Encoding UTF8 -Value `
    ("`$PID | Set-Content -LiteralPath '" + $rmTreePidFile + "'" + "`r`nStart-Sleep 90")
$rmTreeParent = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-Command', ("`$fs=[System.IO.File]::Open('" + $rmTreeFile + "','Open','ReadWrite','None'); " +
        "Start-Process powershell.exe -WindowStyle Hidden -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','" + $rmTreeHelper +
        "'; Start-Sleep 90"))
$rmTreeChild = 0
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 400
    if (Test-Path -LiteralPath $rmTreePidFile) {
        $raw = (Get-Content -LiteralPath $rmTreePidFile -ErrorAction SilentlyContinue | Select-Object -First 1)
        if ($raw -match '(\d+)') { $rmTreeChild = [int]$Matches[1]; break }
    }
}
try {
    $rmTreeQ = Invoke-Exe ('rightmenu unlock --query-only "' + $rmTreeFile + '"')
    Check 'M14f 结束前列出「会连带结束的子进程」（用户报的"锁解开了、窗口还在"就是它）' `
        (($rmTreeChild -gt 0) -and ($rmTreeQ.Out -match ('child\tpid=' + $rmTreeChild + '\b')) -and `
         ([int](Get-Key $rmTreeQ.Out 'lockers') -ge 1)) `
        ('child=' + $rmTreeChild + ' lockers=' + (Get-Key $rmTreeQ.Out 'lockers'))
} finally {
    if ($rmTreeChild -gt 0) { Stop-Process -Id $rmTreeChild -Force -ErrorAction SilentlyContinue }
    if ($rmTreeParent -and -not $rmTreeParent.HasExited) { Stop-Process -Id $rmTreeParent.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 400
    if (Test-Path -LiteralPath $rmTree) { Remove-Item -LiteralPath $rmTree -Recurse -Force -ErrorAction SilentlyContinue }
}

# ---- 句柄表那条线索（rightmenu handles，只读）：用户问「火绒的解除占用是怎么做的」——
#      它遍历**全系统句柄表**，能看见 Restart Manager 看不见的东西：**共享打开**的文件
#      （记事本、IDE、看图这类程序用 FileShare.ReadWrite 打开，RM 一句"没人占"就完了），
#      以及"某个程序把文件夹当成了当前目录"这种目录句柄。这里只验"查得到"，不验"关得掉"
#      （关句柄是危险动作，命令行故意不提供入口，只能从界面点、还要过确认框 —— 见 GUI 套件 N09）。
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public class Mxx1HandleFixture{
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateFileW(string n,uint acc,uint share,IntPtr sa,uint disp,uint flags,IntPtr t);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool CloseHandle(IntPtr h);
 public static IntPtr HoldDir(string p,uint share){return CreateFileW(p,0x80000000,share,IntPtr.Zero,3,0x02000000,IntPtr.Zero);}
 public static void Release(IntPtr h){CloseHandle(h);}
 public static string ExclusiveProbe(string p){IntPtr h=CreateFileW(p,0x80000000,0,IntPtr.Zero,3,0x02000000,IntPtr.Zero);if(h==(IntPtr)(-1))return "err="+Marshal.GetLastWin32Error();CloseHandle(h);return "free";}
}
'@
$rmH = Join-Path $env:TEMP 'mxx1-handles'
if (Test-Path -LiteralPath $rmH) { Remove-Item -LiteralPath $rmH -Recurse -Force }
New-Item -ItemType Directory -Path $rmH | Out-Null
$rmHFile = Join-Path $rmH 'shared.txt'
Set-Content -LiteralPath $rmHFile -Value 'x' -Encoding UTF8
$rmHSub = Join-Path $rmH 'subdir'
New-Item -ItemType Directory -Path $rmHSub | Out-Null
$rmHShared = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-Command', ("`$fs=[System.IO.File]::Open('" + $rmHFile + "','Open','Read','ReadWrite'); Start-Sleep 90"))
$rmHDirHandle = [IntPtr]::Zero
Start-Sleep -Seconds 2
try {
    $rmHQ = Invoke-Exe ('rightmenu handles "' + $rmHFile + '"')
    Check 'M14g 句柄表查得到「共享打开」它的进程（Restart Manager 看不见的那种）' `
        (([int](Get-Key $rmHQ.Out 'hits') -ge 1) -and ($rmHQ.Out -match ('pid=' + $rmHShared.Id + '\b'))) `
        ('hits=' + (Get-Key $rmHQ.Out 'hits') + ' 期望 pid=' + $rmHShared.Id)

    # 目录句柄：用户右键的就是文件夹，而"某个程序把文件夹当当前目录"只有句柄表看得见
    $rmHDirHandle = [Mxx1HandleFixture]::HoldDir($rmHSub, 7)
    $rmHDQ = Invoke-Exe ('rightmenu handles "' + $rmHSub + '"')
    Check 'M14h 句柄表查得到「文件夹被人打开着」（目录句柄，RM 登记目录直接报错）' `
        ($rmHDQ.Out -match ('pid=' + $PID + '\b')) `
        ('hits=' + (Get-Key $rmHDQ.Out 'hits') + ' 期望 pid=' + $PID)
} finally {
    if ($rmHDirHandle -ne [IntPtr]::Zero) { [Mxx1HandleFixture]::Release($rmHDirHandle) }
    if ($rmHShared -and -not $rmHShared.HasExited) { Stop-Process -Id $rmHShared.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 400
    if (Test-Path -LiteralPath $rmH) { Remove-Item -LiteralPath $rmH -Recurse -Force -ErrorAction SilentlyContinue }
}

# ---- 装 / 卸：整段都在**隔离的根**里做（MXX1_RIGHTMENU_ROOT），绝不碰用户真实的右键菜单
$rmTestRoot = 'HKCU:\Software\mxx1-toolbox\rightmenu-test'
$rmRealShell = 'HKCU:\Software\Classes\*\shell'
$rmRecord = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\rightmenu-installed.tsv'
$rmRecordHad = Test-Path -LiteralPath $rmRecord
$rmRecordOld = ''
if ($rmRecordHad) { $rmRecordOld = [System.IO.File]::ReadAllText($rmRecord) }
$rmRealBefore = @(Get-ChildItem -LiteralPath $rmRealShell -ErrorAction SilentlyContinue | Select-Object -ExpandProperty PSChildName)
$rmRealValuesBefore = Get-RightMenuSnapshot
$rmRealIconDir = Join-Path (Split-Path -Parent $rmRecord) 'rightmenu-icons'
$rmTestIconDir = Join-Path (Split-Path -Parent $rmRecord) 'rightmenu-icons-test'
$rmRealIconsBefore = @(Get-ChildItem -LiteralPath $rmRealIconDir -File -ErrorAction SilentlyContinue).Count
$rmEnv = @{ MXX1_RIGHTMENU_ROOT = 'HKCU\Software\mxx1-toolbox\rightmenu-test' }
# 只有 M20c 要测"修补"这条路，它自己把开关打开（全局默认是关的，见文件开头）
$rmEnvSync = @{ MXX1_RIGHTMENU_ROOT = 'HKCU\Software\mxx1-toolbox\rightmenu-test'; MXX1_NO_RIGHTMENU_SYNC = '0' }
$rmRealAfter = $rmRealBefore
try {
    $rmIns = Invoke-Exe 'run rightmenu.unlock.on' 60 $Exe $rmEnv
    $rmOkLines = @($rmIns.Out -split "`r?`n" | Where-Object { $_ -match '√ 解除文件占用' })
    Check 'M15 在隔离根里装上「解除文件占用」：4 个位置都写了、读回核对过' `
        (($rmIns.Code -eq 0) -and ($rmOkLines.Count -eq 4)) ('exit=' + $rmIns.Code + ' √=' + $rmOkLines.Count)

    $rmVerb = Join-Path $rmTestRoot '*\shell\Mxx1Unlock'
    $rmProp = Get-ItemProperty -LiteralPath $rmVerb -ErrorAction SilentlyContinue
    $rmCmd = (Get-ItemProperty -LiteralPath (Join-Path $rmVerb 'command') -ErrorAction SilentlyContinue).'(default)'
    # 占位符必须分位置：文件 / 文件夹是 %1，**「文件夹里的空白处」和「桌面空白处」要 %V**
    # —— 那两个位置资源管理器不替换 %1，会把字面量 "%1" 当路径传给程序（2026-10-04 修的 bug）。
    $rmPlaces = @()
    foreach ($rmP in @(@('*\shell', '%1'), @('Directory\shell', '%1'), `
                       @('Directory\Background\shell', '%V'), @('DesktopBackground\Shell', '%V'))) {
        $rmPc = "$((Get-ItemProperty -LiteralPath (Join-Path $rmTestRoot ($rmP[0] + '\Mxx1Unlock\command')) -ErrorAction SilentlyContinue).'(default)')"
        if ($rmPc -match ('rightmenu unlock "' + [regex]::Escape($rmP[1]) + '"$')) { $rmPlaces += $rmP[1] }
    }
    Check 'M16 verb 写法：MUIVerb + 默认值留空 + MultiSelectModel=Player + 占位符按位置（背景用 %V）' `
        (($rmProp.MUIVerb -eq '解除文件占用') -and ($rmProp.MultiSelectModel -eq 'Player') -and `
         ("$($rmProp.'(default)')" -eq '') -and ($rmPlaces.Count -eq 4)) `
        ('MUIVerb=' + $rmProp.MUIVerb + ' cmd=' + $rmCmd + ' 占位符对的=' + ($rmPlaces -join ','))

    $rmIns2 = Invoke-Exe 'run rightmenu.common.on' 60 $Exe $rmEnv
    Check 'M17 装上「常用功能」：级联子菜单的子项写出来了' `
        (($rmIns2.Code -eq 0) -and ($rmIns2.Out -match '子菜单写了 \d+ 项')) ('exit=' + $rmIns2.Code)
    $rmParent = Get-ItemProperty -LiteralPath (Join-Path $rmTestRoot 'Directory\shell\Mxx1Common') -ErrorAction SilentlyContinue
    Check 'M18 父键指向共用的子项键（ExtendedSubCommandsKey=Mxx1Toolbox.Common，四个位置共用一份）' `
        ("$($rmParent.ExtendedSubCommandsKey)" -eq 'Mxx1Toolbox.Common') ('=' + $rmParent.ExtendedSubCommandsKey)

    $rmShared = @(Get-ChildItem -LiteralPath (Join-Path $rmTestRoot 'Mxx1Toolbox.Common\shell') -ErrorAction SilentlyContinue)
    Check 'M19 子项 = 置顶 + 最近用过 + 固定 3 项（至少 3 项，最多 30+3）' `
        (($rmShared.Count -ge 3) -and ($rmShared.Count -le 33)) ('数=' + $rmShared.Count)
    $rmFixed = @()
    foreach ($rmIt in $rmShared) {
        $rmV = Get-ItemProperty -LiteralPath $rmIt.PSPath -ErrorAction SilentlyContinue
        $rmC = (Get-ItemProperty -LiteralPath (Join-Path $rmIt.PSPath 'command') -ErrorAction SilentlyContinue).'(default)'
        if (@('打开工具箱', '运行日志', '设置') -contains "$($rmV.MUIVerb)") { $rmFixed += ("$($rmV.MUIVerb)=" + "$rmC") }
    }
    Check 'M20 固定三项：打开工具箱（不带参数）/ 运行日志（ui log）/ 设置（ui settings）' `
        (($rmFixed.Count -eq 3) -and (($rmFixed -join ' ') -match 'ui log') -and (($rmFixed -join ' ') -match 'ui settings')) `
        ($rmFixed -join ' | ')

    # ---- 图标：注册表的 Icon 只能指"带图标资源的 exe/dll"或者 .ico 文件，**指 .png 是无效的**。
    #      用户 2026-10-04 报「加进去的右键功能没有图标」：旧版 Icon 写的是 exe，而那个 exe 从来
    #      没有 /win32icon（assets\app.ico 不存在）→ 菜单里就是空白。现在装的时候把内嵌的按钮 PNG
    #      转成真正的 .ico 再指过去。
    $rmIconKeys = @()
    foreach ($rmR in @('*\shell', 'Directory\shell', 'Directory\Background\shell', 'DesktopBackground\Shell')) {
        foreach ($rmVerbName in @('Mxx1Unlock', 'Mxx1Common')) {
            $rmIconKeys += (Join-Path $rmTestRoot ($rmR + '\' + $rmVerbName))
        }
    }
    $rmIconOk = 0
    $rmIconBad = @()
    foreach ($rmK in $rmIconKeys) {
        $rmIcon = "$((Get-ItemProperty -LiteralPath $rmK -ErrorAction SilentlyContinue).Icon)"
        if (($rmIcon.Length -gt 0) -and (Test-Path -LiteralPath $rmIcon)) {
            $rmHead = Get-FirstBytes $rmIcon 4
            if (($rmHead.Length -eq 4) -and ($rmHead[0] -eq 0) -and ($rmHead[1] -eq 0) -and ($rmHead[2] -eq 1) -and ($rmHead[3] -eq 0)) {
                $rmIconOk++
            } else { $rmIconBad += $rmIcon }
        } else { $rmIconBad += ($rmK + ' -> ' + $rmIcon) }
    }
    Check 'M20a 两项的图标：Icon 指向真实存在的 .ico（不是没有图标资源的 exe，也不是 .png）' `
        (($rmIconOk -eq 8) -and ($rmIconBad.Count -eq 0)) ('ok=' + $rmIconOk + '/8 坏=' + ($rmIconBad -join ' '))

    $rmSubIcons = @($rmShared | Where-Object { "$((Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue).Icon)".Length -gt 0 })
    Check 'M20b 「常用功能」子菜单每一项也有图标（子项自己带 Icon）' `
        (($rmShared.Count -ge 3) -and ($rmSubIcons.Count -eq $rmShared.Count)) ('带图标=' + $rmSubIcons.Count + '/' + $rmShared.Count)

    # 子项命令必须带 --show：工具箱是 winexe **没有控制台**，"结果就是一段文字"的按钮
    # （激活状态 / 查看设置改动 / 导出系统日志）不弹窗口的话，用户从右键菜单点等于**没有效果**
    # （2026-10-04 用户报的正是「激活状态和查看设置改动这种没有效果」）。
    $rmSubNoShow = @()
    foreach ($k in $rmShared) {
        $cmd = "$((Get-ItemProperty -LiteralPath (Join-Path $k.PSPath 'command') -ErrorAction SilentlyContinue).'(default)')"
        if ($cmd -notmatch '\srun\s') { continue }          # 「打开工具箱」那种不带 run 的固定项不管
        if ($cmd -notmatch '--show') { $rmSubNoShow += $k.PSChildName }
    }
    Check 'M20b3 子菜单里 run 型的命令都带 --show（否则从右键点就是"没有效果"）' `
        ($rmSubNoShow.Count -eq 0) ('没带 --show 的=' + ($rmSubNoShow -join ' '))

    # 测试装的图标必须写在 rightmenu-icons-test 里 —— 否则撤测试项时会把用户真实那份图标目录
    # 一起删掉（2026-10-04 真踩，见 MenuIcons.Dir 的注释）
    $rmInTestDir = @($rmIconKeys | Where-Object { "$((Get-ItemProperty -LiteralPath $_ -ErrorAction SilentlyContinue).Icon)" -like ($rmTestIconDir + '\*') })
    Check 'M20b2 测试装的图标写在 rightmenu-icons-test 里（和用户真实那份分开）' `
        ($rmInTestDir.Count -eq 8) ('在 test 目录里的=' + $rmInTestDir.Count)

    # 状态里要能念出"图标在不在"（文件被清理软件删掉时，用户能从状态里看出来要点一次装上）
    $rmStat = Invoke-Exe 'rightmenu status' 60 $Exe $rmEnv
    Check 'M20d 状态里念得出菜单图标都在（8 个）' `
        (($rmStat.Code -eq 0) -and ($rmStat.Out -match '菜单图标\s*8 个都在')) `
        ('exit=' + $rmStat.Code + ' ' + (@($rmStat.Out -split "`r?`n" | Where-Object { $_ -match '菜单图标' }) -join ' '))

    # ---- 自动修补：旧版装出来的键（背景位置写 %1、Icon 指着一个没有图标资源的 exe）应该在
    #      "用一次工具箱"时就被修好，而不是等着用户去点「装上…」。这里把键写坏，然后走 pin 这条路
    #      （pin 之后会调 RightMenu.SyncIfInstalled），看它有没有修回来。
    $rmFixKey = Join-Path $rmTestRoot 'Directory\Background\shell\Mxx1Unlock'
    Set-ItemProperty -LiteralPath (Join-Path $rmFixKey 'command') -Name '(default)' `
        -Value ('"' + $Exe + '" rightmenu unlock "%1"')
    Remove-ItemProperty -LiteralPath $rmFixKey -Name 'Icon' -ErrorAction SilentlyContinue
    $rmPinFile = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\pinned.txt'
    $rmPinHad = Test-Path -LiteralPath $rmPinFile
    $rmPinOld = ''
    if ($rmPinHad) { $rmPinOld = [System.IO.File]::ReadAllText($rmPinFile, [System.Text.Encoding]::UTF8) }
    try {
        $rmPin = Invoke-Exe 'pin devmgmt' 60 $Exe $rmEnvSync
        $rmFixCmd = "$((Get-ItemProperty -LiteralPath (Join-Path $rmFixKey 'command') -ErrorAction SilentlyContinue).'(default)')"
        $rmFixIcon = "$((Get-ItemProperty -LiteralPath $rmFixKey -ErrorAction SilentlyContinue).Icon)"
        Check 'M20c 自动修补：用一次工具箱就把旧版写坏的占位符 / 丢掉的图标修回来' `
            (($rmPin.Code -eq 0) -and ($rmFixCmd -match 'rightmenu unlock "%V"$') -and `
             ($rmFixIcon.Length -gt 0) -and (Test-Path -LiteralPath $rmFixIcon)) `
            ('exit=' + $rmPin.Code + ' cmd=' + $rmFixCmd + ' icon=' + $rmFixIcon)
    } finally {
        if ($rmPinHad) { [System.IO.File]::WriteAllText($rmPinFile, $rmPinOld, (New-Object System.Text.UTF8Encoding($false))) }
        elseif (Test-Path -LiteralPath $rmPinFile) { Remove-Item -LiteralPath $rmPinFile -Force -ErrorAction SilentlyContinue }
    }

    $rmOff = Invoke-Exe 'run rightmenu.common.off' 60 $Exe $rmEnv
    $rmOff2 = Invoke-Exe 'run rightmenu.unlock.off' 60 $Exe $rmEnv
    Check 'M21 撤掉两项：自己写的键全删了（verb + 共用子项键）' `
        (((Test-Path -LiteralPath (Join-Path $rmTestRoot '*\shell\Mxx1Unlock')) -eq $false) -and `
         ((Test-Path -LiteralPath (Join-Path $rmTestRoot 'Mxx1Toolbox.Common')) -eq $false)) `
        ('off=' + $rmOff.Code + '/' + $rmOff2.Code)
    $rmRealAfter = @(Get-ChildItem -LiteralPath $rmRealShell -ErrorAction SilentlyContinue | Select-Object -ExpandProperty PSChildName)
    $rmRealMine = @($rmRealAfter | Where-Object { $_ -match 'Mxx1' })
    # 判据是"这次测试一个字都没改用户的真实菜单"，**不是**"用户的菜单里不许有我们的键"：
    # 用户自己点过「装上…」把菜单真装上了，那是正常状态（2026-10-04 就是这样——原断言把
    # 他自己的安装当成了失败）。只要求 before == after。
    $rmUserInstalled = @($rmRealBefore | Where-Object { $_ -match 'Mxx1' })
    Check 'M22 全程没碰用户真实的右键菜单（测试前后一个键都没变）' `
        ((($rmRealBefore -join ',') -eq ($rmRealAfter -join ','))) `
        ('before=' + ($rmRealBefore -join ',') + ' after=' + ($rmRealAfter -join ',') + `
         '（用户自己装的：' + $(if ($rmRealMine.Count -gt 0) { $rmRealMine -join ',' } else { '无' }) + '）')
    Check 'M22b 用户自己装过的话，测试认得出来那本来就在（不当成"测试装上去的"）' `
        ($rmUserInstalled.Count -eq $rmRealMine.Count) ('测试前就有=' + ($rmUserInstalled -join ','))

    # 只比键名不够：整个套件跑下来，用户真实菜单里那 8 个键的**值**（名字 / 图标 / 命令）也要
    # 一模一样 —— pin / unpin 那条路会调 SyncIfInstalled，2026-10-04 就是这样把用户真实的
    # Icon 和占位符悄悄改掉的（当时 M22 只比键名，全绿放过去了）。
    $rmRealValuesAfter = Get-RightMenuSnapshot
    if ($rmRealValuesBefore -eq $rmRealValuesAfter) {
        Check 'M22c 用户真实菜单的键值也没被动过（MUIVerb / Icon / 命令逐项一致）' $true ('键=' + $rmRealMine.Count + ' 个')
    } else {
        $diff = @()
        $b = @($rmRealValuesBefore -split "`n"); $a = @($rmRealValuesAfter -split "`n")
        for ($i = 0; $i -lt [Math]::Max($b.Count, $a.Count); $i++) {
            if ("$($b[$i])" -ne "$($a[$i])") { $diff += ("before=" + $b[$i] + ' → after=' + $a[$i]) }
        }
        Check 'M22c 用户真实菜单的键值也没被动过（MUIVerb / Icon / 命令逐项一致）' $false ($diff -join ' ; ')
    }
}
finally {
    Remove-Item -LiteralPath 'HKCU:\Software\mxx1-toolbox' -Recurse -Force -ErrorAction SilentlyContinue
    if ($rmRecordHad) { [System.IO.File]::WriteAllText($rmRecord, $rmRecordOld) }
    elseif (Test-Path -LiteralPath $rmRecord) { Remove-Item -LiteralPath $rmRecord -Force }
    if (Test-Path -LiteralPath $rmDir) { Remove-Item -LiteralPath $rmDir -Recurse -Force -ErrorAction SilentlyContinue }
}
Check 'M23 收尾干净：测试根删掉了、用户的原值记录按原样放回' `
    (((Test-Path -LiteralPath 'HKCU:\Software\mxx1-toolbox') -eq $false) -and `
     ((Test-Path -LiteralPath $rmRecord) -eq $rmRecordHad)) ('记录文件=' + (Test-Path -LiteralPath $rmRecord))
# 撤掉**测试根**里的两项时，不许动用户真实那份菜单在用的图标（2026-10-04 踩过：两边共用
# 一个图标目录，测试的卸载把整个目录删了 → 用户菜单里的图标当场变空白）。
$rmRealIconAfter = @(Get-ChildItem -LiteralPath $rmRealIconDir -File -ErrorAction SilentlyContinue).Count
Check 'M23b 撤掉测试项没动用户真实那份图标目录（文件数不变）' `
    ($rmRealIconAfter -eq $rmRealIconsBefore) ('before=' + $rmRealIconsBefore + ' after=' + $rmRealIconAfter)

# ---------------------------------------------------------------- S 组：使用条款与更新检查
Write-Host ''
Write-Host 'S 组 · 使用条款（免责声明 / 服务协议 / 首次运行确认门）与更新检查'
# 用户 2026-10-04 的要求：「缺少完整的检测更新功能/免责/服务协议，你看下 permanent-delete-menu 是怎么做的？」
# 正本 docs\DISCLAIMER.md 编译时内嵌进 exe（资源名 Disclaimer.md），窗口显示的就是它；同意记录写在
# settings.ini，记的是**正文指纹**而不是一句 true —— 条款一改，指纹对不上就重新要求确认。
# 界面那条确认门（弹窗 / 禁用按钮 / 不同意就退出）由 Test-Gui 的 I 组盯。

$dis = Invoke-Exe 'disclaimer'
Check 'S01 disclaimer 退出码 0' ($dis.Code -eq 0) ('exit=' + $dis.Code)
Check 'S02 正文不是空窗口（几 KB 的中文正文）' ($dis.Out.Length -gt 2000) ('字数=' + $dis.Out.Length)
Check 'S03 写清了许可证' ($dis.Out -match 'GPL-3\.0-or-later') ''
Check 'S04 写清了会写哪些注册表位置、本身不提权、提权走 UAC' `
    (($dis.Out -match 'HKEY_CURRENT_USER') -and ($dis.Out -match 'asInvoker') -and ($dis.Out -match 'UAC')) ''
Check 'S05 写清了唯一的联网动作与关掉它的开关' (($dis.Out -match 'api\.github\.com') -and ($dis.Out -match 'MXX1_NO_UPDATE=1')) ''
Check 'S06 写清了「解除文件占用」会结束进程 / 关句柄（后果不藏）' `
    (($dis.Out -match '结束那些进程') -and ($dis.Out -match '句柄')) ''
Check 'S07 指向仓库里的正本（窗口显示的与文档永远一致）' `
    (($dis.Out -match 'docs/DISCLAIMER\.md') -and ($dis.Out -match '(?m)^source=docs/DISCLAIMER')) ''
Check 'S08 命令里带上了正文指纹（同意门用的就是它）' ($dis.Out -match '(?m)^hash=[0-9a-f]{16}') (Get-Key $dis.Out 'hash')

# S01b：默认打的必须**是 GitHub 的接口地址**，不能是网页地址。
# 这一条是 2026-10-05 补的（真实环境里发现的 bug）：S10–S15 全程拿 MXX1_UPDATE_URL 指到本机假接口，
# 正好把默认值绕过去了 —— 而默认值当时被写成了网页地址
# （`https://github.com/<账号>/<仓库>/releases/latest`），GitHub 对请求里那个
# `Accept: application/vnd.github+json` 直接回 **406**，于是用户那边永远是「检查失败：http-406」。
# 这里只看 `api=` 那一行长什么样（**不要求网络通**），所以离线机器 / CI 上一样可靠。
$cuDefault = Invoke-Exe 'checkupdate' 60 $Exe @{ MXX1_UPDATE_TIMEOUT_MS = '1500' }
$apiDefault = Get-Key $cuDefault.Out 'api'
Check 'S01b 默认打的是 GitHub 接口地址（不是网页地址 —— 406 那次教训）' `
    (($apiDefault -match '^https://api\.github\.com/repos/') -and ($apiDefault -match '/mxx1-toolbox')) $apiDefault

$helpText = Invoke-Exe 'help'
Check 'S09 help 里能查到 checkupdate / disclaimer / consent 三个命令' `
    (($helpText.Out -match 'checkupdate') -and ($helpText.Out -match 'disclaimer') -and ($helpText.Out -match 'consent')) ''

# ---- 更新检查：只读版本号，不下载、不替换；三条路径都验（关掉 / 连不上 / 有新版）
$cuOff = Invoke-Exe 'checkupdate' 60 $Exe @{ MXX1_NO_UPDATE = '1' }
Check 'S10 MXX1_NO_UPDATE=1 时一个字节都不发（update=disabled，退出码 1 = 这次没结论）' `
    (($cuOff.Code -eq 1) -and ($cuOff.Out -match '(?m)^update=disabled\r?$')) (Get-Key $cuOff.Out 'update')
Check 'S11 关掉时也报版本号（脚本据此判断）' ((Get-Key $cuOff.Out 'version') -eq '1.5.3') (Get-Key $cuOff.Out 'version')

$cuBad = Invoke-Exe 'checkupdate' 60 $Exe @{
    MXX1_UPDATE_URL = 'http://127.0.0.1:9/releases'
    MXX1_UPDATE_TAGS_URL = 'http://127.0.0.1:9/tags'
    MXX1_UPDATE_TIMEOUT_MS = '1500'
}
Check 'S12 连不上时静默降级成 update=error（不抛异常、不弹窗）' `
    (($cuBad.Code -eq 1) -and ($cuBad.Out -match '(?m)^update=error\r?$')) (Get-Key $cuBad.Out 'detail')

# 本机假接口（用完就关，不碰外网）：有新版时命令行说得出来，而且只给出"发布页"这个地址
$cuListener = $null
try {
    $tcp2 = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $tcp2.Start()
    $cuPort = $tcp2.LocalEndpoint.Port
    $tcp2.Stop()
    $cuPrefix = 'http://127.0.0.1:' + $cuPort + '/'
    $cuBody = '{"tag_name":"v9.9.9","html_url":"' + $cuPrefix + 'fake-release"}'
    $cuBytes = [System.Text.Encoding]::UTF8.GetBytes($cuBody)
    $cuListener = New-Object System.Net.HttpListener
    $cuListener.Prefixes.Add($cuPrefix)
    $cuListener.Start()
    $cuCtx = $cuListener.GetContextAsync()

    $cuSi = New-Object System.Diagnostics.ProcessStartInfo
    $cuSi.FileName = $Exe
    $cuSi.Arguments = 'checkupdate'
    $cuSi.UseShellExecute = $false
    $cuSi.RedirectStandardOutput = $true
    $cuSi.RedirectStandardError = $true
    $cuSi.CreateNoWindow = $true
    $cuSi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $cuSi.EnvironmentVariables['MXX1_UPDATE_URL'] = ($cuPrefix + 'releases/latest')
    $cuSi.EnvironmentVariables['MXX1_UPDATE_TAGS_URL'] = ($cuPrefix + 'tags')
    $cuP = New-Object System.Diagnostics.Process
    $cuP.StartInfo = $cuSi
    [void]$cuP.Start()
    $cuTask = $cuP.StandardOutput.ReadToEndAsync()
    $cuErrTask = $cuP.StandardError.ReadToEndAsync()
    for ($ci = 0; $ci -lt 60 -and -not $cuP.HasExited; $ci++) {
        Start-Sleep -Milliseconds 150
        if ($cuCtx.IsCompleted) {
            try {
                $c = $cuCtx.Result
                $c.Response.StatusCode = 200
                $c.Response.ContentType = 'application/json'
                $c.Response.ContentLength64 = $cuBytes.Length
                $c.Response.OutputStream.Write($cuBytes, 0, $cuBytes.Length)
                $c.Response.OutputStream.Close()
            } catch { }
            $cuCtx = $cuListener.GetContextAsync()
        }
    }
    [void]$cuP.WaitForExit(20000)
    $cuOut = ''
    try { $cuOut = $cuTask.Result } catch { }
    Check 'S13 有新版时命令行说得出来（update=available + latest=9.9.9）' `
        (($cuP.ExitCode -eq 0) -and ($cuOut -match '(?m)^update=available\r?$') -and ((Get-Key $cuOut 'latest') -eq '9.9.9')) `
        ('exit=' + $cuP.ExitCode + ' ' + (Get-Key $cuOut 'latest'))
    Check 'S14 报的是"发布页"地址（只报告，不下载任何文件）' `
        ((Get-Key $cuOut 'url') -eq ($cuPrefix + 'fake-release')) (Get-Key $cuOut 'url')
    Check 'S15 界面文案里写清新版本和当前版本' ($cuOut -match '发现新版本 v9\.9\.9') (Get-Key $cuOut 'ui')
} finally {
    if ($cuListener -ne $null) { try { $cuListener.Stop(); $cuListener.Close() } catch { } }
}

# ---- 使用条款的同意状态（consent 命令 / status 字段 / settings.ini 里的记录）
$consentBefore = 'unknown'
$probeC = Invoke-Exe 'consent'
if ($probeC.Out -match '(?m)^consent=(\w+)') { $consentBefore = $Matches[1] }
$curHash = Get-Key $probeC.Out 'currentHash'
Check 'S16 当前条款正文的指纹是 16 位十六进制' ($curHash -match '^[0-9a-f]{16}$') $curHash

$cReset = Invoke-Exe 'consent --reset'
Check 'S17 --reset 之后状态是"需要确认"（退出码 1）' `
    (($cReset.Code -eq 1) -and ($cReset.Out -match '(?m)^consent=required\r?$')) (Get-Key $cReset.Out 'consent')
$cAccept = Invoke-Exe 'consent --accept'
Check 'S18 --accept 之后退出码 0 且 consent=agreed' `
    (($cAccept.Code -eq 0) -and ($cAccept.Out -match '(?m)^consent=agreed\r?$')) (Get-Key $cAccept.Out 'consent')
Check 'S19 记下来的是当前正文指纹（不是一句 true）' ((Get-Key $cAccept.Out 'consentHash') -eq $curHash) (Get-Key $cAccept.Out 'consentHash')

$iniPath = Get-Key $cAccept.Out 'settings'
$iniText = ''
if ($iniPath.Length -gt 0 -and (Test-Path -LiteralPath $iniPath)) {
    $iniText = [System.IO.File]::ReadAllText($iniPath, [System.Text.Encoding]::UTF8)
}
Check 'S20 同意记录真写进了 settings.ini（AgreedDisclaimer=<指纹>）' `
    ($iniText -match ('(?m)^AgreedDisclaimer=' + $curHash + '\s*$')) $iniPath
Check 'S21 记录里带同意时间（事后能追溯）' `
    ($iniText -match '(?m)^AgreedAt=\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\s*$') ''

$st3 = Invoke-Exe 'status'
Check 'S22 status 里能查到条款状态（脚本据此判断要不要先同意）' `
    (($st3.Out -match '(?m)^consent=agreed\r?$') -and ($st3.Out -match '(?m)^consentAgreed=yes\r?$')) (Get-Key $st3.Out 'consent')
Check 'S23 status 里能查到更新检查是开是关' ($st3.Out -match '(?m)^updateCheck=(enabled|disabled)\r?$') (Get-Key $st3.Out 'updateCheck')

$null = Invoke-Exe 'consent --reset'
$cliFree = Invoke-Exe 'list --tab mine'
Check 'S24 命令行不被条款拦（非交互场景：没同意也照常 list）' `
    (($cliFree.Code -eq 0) -and ($cliFree.Out -match '(?m)^shown=')) ''
$cliRun = Invoke-Exe 'run devmgmt --dry'
Check 'S25 命令行 run --dry 也一样不被拦（只解析、不启动）' (($cliRun.Code -eq 0) -and ((Get-Key $cliRun.Out 'dry') -eq 'yes')) (Get-Key $cliRun.Out 'id')

# 复原成测试之前的样子（用户下次打开界面该不该看到确认窗口，由他原来的状态决定）
if ($consentBefore -eq 'required') { $null = Invoke-Exe 'consent --reset' } else { $null = Invoke-Exe 'consent --accept' }
$cAfter = Invoke-Exe 'consent'
Check 'S26 条款状态已按测试前的样子复原' ($cAfter.Out -match ('(?m)^consent=' + $consentBefore)) `
    ('now=' + (Get-Key $cAfter.Out 'consent') + ' before=' + $consentBefore)

# ---------------------------------------------------------------- 汇总
Write-Host ''
Write-Host '----------------------------------------------------------'
# 把 MXX1_NO_RIGHTMENU_SYNC 恢复成测试之前的样子（别给同一个 shell 里后面的命令留副作用）
if ($script:SyncHad) { $env:MXX1_NO_RIGHTMENU_SYNC = $script:SyncOld }
else { Remove-Item Env:MXX1_NO_RIGHTMENU_SYNC -ErrorAction SilentlyContinue }
Write-Host (" 命令行回归: 通过 {0} 项, 失败 {1} 项" -f $script:Pass, $script:Fail)
if ($script:Skip -gt 0) { Write-Host (" （另有 {0} 项环境不满足，跳过 —— 不算失败，原因见上面 [SKIP] 那几行）" -f $script:Skip) }
Write-Host '----------------------------------------------------------'
if ($script:UserToolsHad -and (Test-Path -LiteralPath $UserToolsPaused)) {
    if (Test-Path -LiteralPath $UserToolsJson) { Remove-Item -LiteralPath $UserToolsJson -Force }
    Move-Item -LiteralPath $UserToolsPaused -Destination $UserToolsJson -Force
    Write-Host '（用户自己的 tools.json 已复原）'
}
if ($script:Fail -gt 0) { exit 1 }
exit 0
