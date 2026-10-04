#Requires -Version 5.1
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 mxx1.cn
<#
    Test-Gui.ps1 -- 萌新工具箱界面回归测试（需要交互式桌面）

    做法和 permanent-delete-menu 的界面回归一样：不看截图，用 Win32 探针
      * EnumChildWindows + GetWindowRect  判"按钮 / 标签有没有压在一起"
      * GetWindowLong(GWL_STYLE)          判标题栏样式位
      * PostMessage(BM_CLICK)             真点按钮
      * WM_GETTEXT                        跨进程读控件文字
    按钮清单不写死：从 `Mxx1Toolbox.exe list` 里读，两边必须一致。

    没有交互式桌面时返回退出码 3（跳过，不是失败）。

    用法: powershell -File tests\Test-Gui.ps1
    退出码: 0 = 全绿, 1 = 有失败, 3 = 环境不满足（跳过）
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Exe = Join-Path $root 'bin\Mxx1Toolbox.exe'
$SettingsIni = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\settings.ini'

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
Write-Host ' 萌新工具箱 · 界面回归测试'
Write-Host '=========================================================='
Write-Host (' exe : ' + $Exe)
Write-Host ''

if (-not [Environment]::UserInteractive) {
    Write-Host ' 环境不满足：当前会话不是交互式的（没有桌面），跳过界面测试。'
    exit 3
}

# ---------------------------------------------------------------- Win32 探针
# 下面这段 C# 一律英文注释：Add-Type 的源码是当字符串交给编译器的，中文会被按 ANSI 解坏。
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class TBGui
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hWnd, StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr hWnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);

    public static IntPtr[] TopLevel(uint pid)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid) { list.Add(h); }
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    public static IntPtr[] Children(IntPtr parent)
    {
        List<IntPtr> list = new List<IntPtr>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
        return list.ToArray();
    }

    // WM_GETTEXT (0x000D) with SMTO_ABORTIFHUNG: cross process GetWindowText is unreliable
    // for controls without a caption, WM_GETTEXT is the message the control actually answers.
    public static string Text(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(40000);
        IntPtr res;
        SendMessageTimeoutW(h, 0x000D, (IntPtr)sb.Capacity, sb, 0x0002, 10000, out res);
        string s = sb.ToString();
        if (s.Length > 0) { return s; }
        StringBuilder sb2 = new StringBuilder(1024);
        GetWindowTextW(h, sb2, sb2.Capacity);
        return sb2.ToString();
    }

    public static string Class(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(256);
        GetClassNameW(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static int[] Rect(IntPtr h)
    {
        RECT r;
        if (!GetWindowRect(h, out r)) { return new int[] { 0, 0, 0, 0 }; }
        return new int[] { r.Left, r.Top, r.Right, r.Bottom };
    }

    public static int Styles(IntPtr h) { return GetWindowLongW(h, -16); }
    public static bool Visible(IntPtr h) { return IsWindowVisible(h); }
    public static bool Enabled(IntPtr h) { return IsWindowEnabled(h); }
    public static bool Alive(IntPtr h) { return IsWindow(h); }
    public static bool Click(IntPtr h) { return PostMessageW(h, 0x00F5, IntPtr.Zero, IntPtr.Zero); }        // BM_CLICK
    public static bool CloseWindow(IntPtr h) { return PostMessageW(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }  // WM_CLOSE
}
'@

function Get-ChildControls {
    param([IntPtr]$RootHandle)
    $out = @()
    foreach ($h in [TBGui]::Children($RootHandle)) {
        $r = [TBGui]::Rect($h)
        $out += [pscustomobject]@{
            H       = $h
            Text    = [TBGui]::Text($h)
            Class   = [TBGui]::Class($h)
            Visible = [TBGui]::Visible($h)
            Enabled = [TBGui]::Enabled($h)
            Left    = $r[0]; Top = $r[1]; Right = $r[2]; Bottom = $r[3]
            Width   = $r[2] - $r[0]; Height = $r[3] - $r[1]
        }
    }
    return $out
}

function Get-TopWindows {
    param([int]$ProcessId)
    $out = @()
    foreach ($h in [TBGui]::TopLevel([uint32]$ProcessId)) {
        $out += [pscustomobject]@{ H = $h; Text = [TBGui]::Text($h); Class = [TBGui]::Class($h); Visible = [TBGui]::Visible($h) }
    }
    return $out
}

function Test-Overlap {
    param($A, $B)
    $w = [Math]::Min($A.Right, $B.Right) - [Math]::Max($A.Left, $B.Left)
    $h = [Math]::Min($A.Bottom, $B.Bottom) - [Math]::Max($A.Top, $B.Top)
    return ($w -gt 0 -and $h -gt 0)
}

function Test-ContainsRect {
    param($Outer, $Inner)
    return ($Inner.Left -ge $Outer.Left -and $Inner.Top -ge $Outer.Top -and
            $Inner.Right -le $Outer.Right -and $Inner.Bottom -le $Outer.Bottom -and
            ($Outer.Width -gt $Inner.Width -or $Outer.Height -gt $Inner.Height))
}

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
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $si
    [void]$p.Start()
    $tOut = $p.StandardOutput.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutSec * 1000)) { try { $p.Kill() } catch { } ; return '' }
    try { return $tOut.Result } catch { return '' }
}

function Get-ToolNames {
    param([string]$Tab)
    $out = Invoke-Exe ('list --tab ' + $Tab)
    $names = @()
    foreach ($line in ($out -split "`r?`n")) {
        if ($line -match "`t") { $names += ($line -split "`t")[2] }
    }
    return $names
}

function Start-Gui {
    $si = New-Object System.Diagnostics.ProcessStartInfo
    $si.FileName = $Exe
    $si.UseShellExecute = $false
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $si
    [void]$p.Start()
    for ($i = 0; $i -lt 100; $i++) {
        Start-Sleep -Milliseconds 200
        $p.Refresh()
        if ($p.HasExited) { return $p }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    return $p
}

function Wait-Buttons {
    param([IntPtr]$Handle, [string[]]$Names, [int]$Tries = 40)
    for ($i = 0; $i -lt $Tries; $i++) {
        $controls = @(Get-ChildControls -RootHandle $Handle | Where-Object { $_.Class -like '*BUTTON*' })
        $texts = @($controls | ForEach-Object { $_.Text })
        $missing = @($Names | Where-Object { $texts -notcontains $_ })
        if ($missing.Count -eq 0) { return $true }
        Start-Sleep -Milliseconds 150
    }
    return $false
}

# ================================================================ 准备
$settingsBefore = $null
$settingsExisted = Test-Path -LiteralPath $SettingsIni
if ($settingsExisted) { $settingsBefore = [System.IO.File]::ReadAllText($SettingsIni, [System.Text.Encoding]::UTF8) }

$commonNames = Get-ToolNames 'common'
$rightNames = Get-ToolNames 'rightmenu'
$cleanNames = Get-ToolNames 'cleanup'
$sysNames = Get-ToolNames 'system'
$mineNames = Get-ToolNames 'mine'

$proc = Start-Gui
Check 'A01 界面能起来（主窗口出现）' (($proc -ne $null) -and (-not $proc.HasExited) -and ($proc.MainWindowHandle -ne [IntPtr]::Zero)) ''
if ($proc.HasExited -or $proc.MainWindowHandle -eq [IntPtr]::Zero) {
    Write-Host ' 启动失败，后面的检查没法做。'
    exit 1
}
$main = $proc.MainWindowHandle
Start-Sleep -Milliseconds 800

$top = @(Get-TopWindows -ProcessId $proc.Id)
$mainWin = @($top | Where-Object { $_.H -eq $main })
$title = ''
if ($mainWin.Count -gt 0) { $title = $mainWin[0].Text }
Check 'A02 标题栏写着「萌新工具箱 v1.0.0」' ($title -match '萌新工具箱' -and $title -match '1\.0\.0') $title

$style = [TBGui]::Styles($main)
Check 'A03 标题栏没有最小化方框' (($style -band 0x00020000) -eq 0) ('style=0x{0:X}' -f $style)
Check 'A04 标题栏没有最大化方框' (($style -band 0x00010000) -eq 0) ('style=0x{0:X}' -f $style)
Check 'A05 窗口可缩放（有 WS_THICKFRAME）' (($style -band 0x00040000) -ne 0) ('style=0x{0:X}' -f $style)

$rect = [TBGui]::Rect($main)
Check 'A06 窗口尺寸合理（宽 >= 460, 高 >= 500）' (($rect[2] - $rect[0]) -ge 460 -and ($rect[3] - $rect[1]) -ge 500) (('w={0} h={1}' -f ($rect[2] - $rect[0]), ($rect[3] - $rect[1])))

# ---------------------------------------------------------------- B 组：按钮墙
Write-Host ''
Write-Host 'B 组 · 按钮墙（对照 CLI 的按钮清单）'

$buttons = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' })

$commonOk = Wait-Buttons -Handle $main -Names $commonNames
Check ('B01 常用设置页签上 {0} 个按钮全在' -f $commonNames.Count) $commonOk ''

$buttons = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' })
$toolButtons = @($buttons | Where-Object { $commonNames -contains $_.Text })
$cols = @($toolButtons | ForEach-Object { $_.Left } | Sort-Object -Unique)
$rows = @($toolButtons | ForEach-Object { $_.Top } | Sort-Object -Unique)
Check 'B02 排成 4 列' ($cols.Count -eq 4) ('列数=' + $cols.Count)
Check 'B03 排成多行（常用设置 32 个按钮 = 8 行）' ($rows.Count -eq 8) ('行数=' + $rows.Count)

$rowCounts = @($toolButtons | Group-Object Top | ForEach-Object { $_.Count })
$fullRows = @($rowCounts | Where-Object { $_ -eq 4 })
Check 'B04 每行都是 4 个按钮' ($fullRows.Count -eq $rows.Count) ($rowCounts -join ',')

$sizes = @($toolButtons | ForEach-Object { '{0}x{1}' -f $_.Width, $_.Height } | Sort-Object -Unique)
Check 'B05 所有按钮尺寸完全一致' ($sizes.Count -eq 1) ($sizes -join ' ')
$first = $toolButtons[0]
Check 'B05b 按钮尺寸合理（宽 100~170, 高 24~40）' ($first.Width -ge 100 -and $first.Width -le 170 -and $first.Height -ge 24 -and $first.Height -le 40) ('{0}x{1}' -f $first.Width, $first.Height)

# 重叠检查：容器天然"包住"子控件，所以先排除"完整包住别人"的窗口
$all = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Visible -and $_.Width -gt 0 -and $_.Height -gt 0 })
$containers = @()
foreach ($c in $all) {
    $inner = @($all | Where-Object { $_.H -ne $c.H -and (Test-ContainsRect -Outer $c -Inner $_) })
    if ($inner.Count -gt 0) { $containers += $c.H }
}
$leaf = @($all | Where-Object { $containers -notcontains $_.H })
$overlaps = @()
for ($i = 0; $i -lt $leaf.Count; $i++) {
    for ($j = $i + 1; $j -lt $leaf.Count; $j++) {
        if (Test-Overlap -A $leaf[$i] -B $leaf[$j]) {
            $overlaps += ($leaf[$i].Text + ' <-> ' + $leaf[$j].Text)
        }
    }
}
Check 'B06 按钮/标签之间零重叠' ($overlaps.Count -eq 0) (($overlaps | Select-Object -First 4) -join ' | ')

$separators = @($all | Where-Object { $_.Height -le 2 -and $_.Width -gt 200 })
Check 'B07 段与段之间有分隔线' ($separators.Count -ge 1) ('分隔线=' + $separators.Count)

$visibleEdits = @($all | Where-Object { $_.Class -like '*EDIT*' -and $_.Height -gt 20 })
Check 'B08 默认不显示日志面板（没有大文本框）' ($visibleEdits.Count -eq 0) ('可见文本框=' + $visibleEdits.Count)

# ---------------------------------------------------------------- C 组：翻页签
Write-Host ''
Write-Host 'C 组 · 页签切换'

function Switch-Tab {
    param([IntPtr]$Handle, [string]$TabName, [string[]]$ExpectNames)
    $tabButton = @(Get-ChildControls -RootHandle $Handle | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $TabName })
    if ($tabButton.Count -eq 0) { return $false }
    [void][TBGui]::Click($tabButton[0].H)
    return (Wait-Buttons -Handle $Handle -Names $ExpectNames)
}

Check ('C01 点「右键增强」→ {0} 个按钮' -f $rightNames.Count) (Switch-Tab -Handle $main -TabName '右键增强' -ExpectNames $rightNames) ''
Check ('C02 点「清理优化」→ {0} 个按钮' -f $cleanNames.Count) (Switch-Tab -Handle $main -TabName '清理优化' -ExpectNames $cleanNames) ''
Check ('C03 点「系统工具」→ {0} 个按钮' -f $sysNames.Count) (Switch-Tab -Handle $main -TabName '系统工具' -ExpectNames $sysNames) ''
Check ('C04 点「我的工具」→ {0} 个按钮' -f $mineNames.Count) (Switch-Tab -Handle $main -TabName '我的工具' -ExpectNames $mineNames) ''
Check ('C05 回「常用设置」→ {0} 个按钮' -f $commonNames.Count) (Switch-Tab -Handle $main -TabName '常用设置' -ExpectNames $commonNames) ''

$gridAfter = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $commonNames -contains $_.Text })
Check 'C06 换回常用设置后按钮数没变' ($gridAfter.Count -eq $commonNames.Count) ('按钮=' + $gridAfter.Count)

# ---------------------------------------------------------------- D 组：底部条与日志
Write-Host ''
Write-Host 'D 组 · 底部条 · 搜索 · 日志'

$barButtons = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and @('搜索', '日志', '收起日志', '设置', '检查更新') -contains $_.Text })
Check 'D01 底部条上有搜索/日志/设置/检查更新' ($barButtons.Count -eq 4) (($barButtons | ForEach-Object { $_.Text }) -join ' ')

$logButton = @($barButtons | Where-Object { $_.Text -eq '日志' })
if ($logButton.Count -gt 0) { [void][TBGui]::Click($logButton[0].H) }
Start-Sleep -Milliseconds 900
$editsOpen = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*EDIT*' -and $_.Height -gt 20 -and $_.Visible })
Check 'D02 点「日志」展开运行日志面板' ($editsOpen.Count -ge 1) ('可见文本框=' + $editsOpen.Count)

$logText = ''
if ($editsOpen.Count -gt 0) { $logText = $editsOpen[0].Text }
Check 'D03 日志框里能看到"暂"字头提示或时间戳' (($logText -match '\d\d:\d\d:\d\d') -or ($logText -match '还没有运行记录')) (($logText -split "`r?`n" | Select-Object -First 1))

$hideButton = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '收起日志' })
if ($hideButton.Count -gt 0) { [void][TBGui]::Click($hideButton[0].H) }
Start-Sleep -Milliseconds 900
$editsClosed = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*EDIT*' -and $_.Height -gt 20 -and $_.Visible })
Check 'D04 再点一次收起日志面板' ($editsClosed.Count -eq 0) ('可见文本框=' + $editsClosed.Count)

$searchButton = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '搜索' })
if ($searchButton.Count -gt 0) { [void][TBGui]::Click($searchButton[0].H) }
Start-Sleep -Milliseconds 900
$searchEdits = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*EDIT*' -and $_.Height -gt 10 -and $_.Visible })
Check 'D05 点「搜索」出现搜索框' ($searchEdits.Count -ge 1) ('可见文本框=' + $searchEdits.Count)
if ($searchEdits.Count -gt 0) { [void][TBGui]::Click($searchButton[0].H) }

# ---------------------------------------------------------------- E 组：点一个占位按钮
Write-Host ''
Write-Host 'E 组 · 真点一个占位按钮（必须有反应：写日志，不弹窗）'

$logPath = ''
foreach ($line in ((Invoke-Exe 'status') -split "`r?`n")) {
    if ($line -match '^log=([^\r\n]+)') { $logPath = $Matches[1] }
}
$before = 0
if ($logPath -and (Test-Path -LiteralPath $logPath)) {
    $before = @(Get-Content -LiteralPath $logPath -Encoding UTF8).Count
}

$targetName = '刷新 DNS 缓存'
$target = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $targetName })
if ($target.Count -gt 0) { [void][TBGui]::Click($target[0].H) }

$found = $false
$newest = ''
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 200
    $lines = @(Get-Content -LiteralPath $logPath -Encoding UTF8 -ErrorAction SilentlyContinue)
    if ($lines.Count -gt $before) {
        $newest = $lines[$lines.Count - 1]
        if ($newest -match '功能待接入') { $found = $true; break }
    }
}
Check ('E01 点「{0}」写了一条运行日志' -f $targetName) ($found) $newest.Trim()
Check 'E02 占位按钮点了不弹窗（还是主窗口在最前）' (@((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.Visible -and $_.Class -like '*DIALOG*' }).Count -eq 0) ''
# 占位按钮点击后本来就会灰 600ms（表示"点到了"），所以这里要等它自己恢复
Start-Sleep -Milliseconds 1200
$disabled = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and -not $_.Enabled })
Check 'E03 占位按钮那 600ms 灰显结束后自己恢复' ($disabled.Count -eq 0) (($disabled | ForEach-Object { $_.Text }) -join ' ')
$back = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $targetName })
Check 'E04 被点的那个按钮文字恢复原样（没有卡在"…"）' ($back.Count -eq 1 -and $back[0].Text -eq $targetName) (($back | ForEach-Object { $_.Text }) -join ' ')

# ---------------------------------------------------------------- F 组：危险按钮的确认框
Write-Host ''
Write-Host 'F 组 · 危险按钮必须先确认'

$danger = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '禁用 SmartScreen' })
if ($danger.Count -gt 0) { [void][TBGui]::Click($danger[0].H) }
$dialog = $null
for ($i = 0; $i -lt 25; $i++) {
    Start-Sleep -Milliseconds 200
    $dialog = @((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible })
    if ($dialog.Count -gt 0) { break }
}
Check 'F01 点危险按钮弹出确认框' ($dialog -ne $null -and $dialog.Count -gt 0) (($dialog | ForEach-Object { $_.Text }) -join ' ')
if ($dialog -ne $null -and $dialog.Count -gt 0) {
    Check 'F02 确认框标题是「确认执行」' ($dialog[0].Text -match '确认') $dialog[0].Text
    [void][TBGui]::CloseWindow($dialog[0].H)
    Start-Sleep -Milliseconds 600
    Check 'F03 取消后确认框关掉了' (@((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible }).Count -eq 0) ''
} else {
    Check 'F02 确认框标题是「确认执行」' $false '没有弹出确认框'
    Check 'F03 取消后确认框关掉了' $false 'skipped'
}

# ---------------------------------------------------------------- G 组：深色主题
Write-Host ''
Write-Host 'G 组 · 深色主题'

try {
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $SettingsIni))
    [System.IO.File]::WriteAllText($SettingsIni, "Theme=dark`r`nClickMode=single`r`nConfirmDangerous=1`r`nShowLogPanel=0`r`nLogKeepDays=30`r`nPermanentDeleteExe=`r`n", (New-Object System.Text.UTF8Encoding($false)))
} catch { }
$darkStatus = Invoke-Exe 'status'
Check 'G01 设置成深色后 themeResolved=dark' ($darkStatus -match '(?m)^themeResolved=dark') (($darkStatus -split "`r?`n" | Where-Object { $_ -match '^themeResolved=' }) -join '')

try { [void][TBGui]::CloseWindow($main) } catch { }
Start-Sleep -Milliseconds 900
$procDark = Start-Gui
Check 'G02 深色主题下界面能正常起来' (($procDark -ne $null) -and (-not $procDark.HasExited) -and ($procDark.MainWindowHandle -ne [IntPtr]::Zero)) ''
if ($procDark -ne $null -and -not $procDark.HasExited -and $procDark.MainWindowHandle -ne [IntPtr]::Zero) {
    $darkButtons = Wait-Buttons -Handle $procDark.MainWindowHandle -Names $commonNames
    Check ('G03 深色下 32 个按钮仍然都在') $darkButtons ''
    $darkRect = [TBGui]::Rect($procDark.MainWindowHandle)
    Check 'G04 深色下窗口尺寸没变' (($darkRect[2] - $darkRect[0]) -ge 460) ('w=' + ($darkRect[2] - $darkRect[0]))
    try { [void][TBGui]::CloseWindow($procDark.MainWindowHandle) } catch { }
    Start-Sleep -Milliseconds 700
} else {
    Check 'G03 深色下 32 个按钮仍然都在' $false 'skipped'
    Check 'G04 深色下窗口尺寸没变' $false 'skipped'
}

# ---------------------------------------------------------------- 现场复原
try {
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $SettingsIni))
    if ($settingsExisted) { [System.IO.File]::WriteAllText($SettingsIni, $settingsBefore, (New-Object System.Text.UTF8Encoding($false))) }
    else { Remove-Item -LiteralPath $SettingsIni -Force -ErrorAction SilentlyContinue }
} catch { }

foreach ($p in @($proc, $procDark)) {
    try { if ($p -and -not $p.HasExited) { $p.Kill() } } catch { }
}

Write-Host ''
Write-Host '----------------------------------------------------------'
Write-Host (" 界面回归: 通过 {0} 项, 失败 {1} 项" -f $script:Pass, $script:Fail)
Write-Host '----------------------------------------------------------'
if ($script:Fail -gt 0) { exit 1 }
exit 0
