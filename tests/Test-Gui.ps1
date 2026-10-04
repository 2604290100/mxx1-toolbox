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

# 量文字宽度要用同一套渲染器，才能判断"文字装不装得下"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$MeasureFont = New-Object System.Drawing.Font('Microsoft YaHei', 8.25)
function Measure-Width([string]$Text) {
    return [System.Windows.Forms.TextRenderer]::MeasureText($Text, $MeasureFont).Width
}

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
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hWnd);
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
    public static bool Click(IntPtr h) { return PostMessageW(h, 0x00F5, IntPtr.Zero, IntPtr.Zero); }

    // WM_SETTEXT (0x000C) across processes: used to type into the 新建按钮 window's fields.
    // ExactSpelling matters here: without it the runtime looks the entry point up as
    // "SendMessageTextWW" first (CharSet.Unicode appends a W to the *entry point*, not just the
    // method name) and a missing export throws EntryPointNotFoundException at the first call --
    // which is exactly how the 新建按钮 regression hung: the fields stayed empty, the modal
    // dialog never closed, and the disabled main window blocked every later check.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessageTextW(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);
    public static IntPtr SetText(IntPtr h, string text) { return SendMessageTextW(h, 0x000C, IntPtr.Zero, text); }
    public static bool CloseWindow(IntPtr h) { return PostMessageW(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }  // WM_CLOSE
    public static IntPtr Parent(IntPtr h) { return GetParent(h); }

    // PW_RENDERFULLCONTENT (2) asks the window to paint itself into the given DC, including its
    // children and the DWM composited title bar. The bitmap is created on the PowerShell side so
    // that this class never mentions System.Drawing -- that keeps Add-Type working on both
    // Windows PowerShell 5.1 and PowerShell 7 (where referencing System.Drawing needs extra work).
    public static bool Paint(IntPtr hWnd, IntPtr hdc) { return PrintWindow(hWnd, hdc, 2); }
}
'@

# ---------------------------------------------------------------- 渲染像素探针
# 用户报过的两个"看着像 bug"的问题只能从渲染结果判定，所以这里读真正的像素：
#   * 底栏按钮的文字是不是被下边缘裁掉（墨迹行数比按钮墙少 = 裁了）
#   * 按钮图标是不是在按钮里上下居中（图标是亮而饱和的色块，文字是暗墨迹；
#     ClearType 会给文字加彩色描边，所以判定图标必须加亮度下限）
function Get-WindowShot {
    param([IntPtr]$Handle)
    $r = [TBGui]::Rect($Handle)
    $w = $r[2] - $r[0]; $h = $r[3] - $r[1]
    if ($w -le 0 -or $h -le 0) { return $null }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $ok = [TBGui]::Paint($Handle, $hdc)
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    if (-not $ok) { $bmp.Dispose(); return $null }

    $area = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($area, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $stride = $data.Stride
    $bmp.UnlockBits($data)
    $bmp.Dispose()
    return [pscustomobject]@{
        Bytes = $bytes; Stride = $stride; Width = $w; Height = $h
        Left = $r[0]; Top = $r[1]
    }
}

# 一个矩形的墨迹行范围；返回 @{ LabelTop; LabelBottom; IconTop; IconBottom }（没找到 = -1）
function Get-InkRows {
    param($Shot, [int]$X, [int]$Y, [int]$W, [int]$H, [switch]$Icon)
    $bytes = $Shot.Bytes; $stride = $Shot.Stride

    # 背景色 = 内区里出现次数最多的颜色
    $counts = @{}
    for ($yy = 2; $yy -lt $H - 2; $yy++) {
        $row = ($Y + $yy) * $stride
        for ($xx = 2; $xx -lt $W - 2; $xx++) {
            $i = $row + (($X + $xx) * 4)
            $key = ([int]$bytes[$i + 2] -shl 16) -bor ([int]$bytes[$i + 1] -shl 8) -bor [int]$bytes[$i]
            if ($counts.ContainsKey($key)) { $counts[$key] = $counts[$key] + 1 } else { $counts[$key] = 1 }
        }
    }
    $bg = 0; $bestN = -1
    foreach ($k in $counts.Keys) { if ($counts[$k] -gt $bestN) { $bestN = $counts[$k]; $bg = $k } }
    $bgR = ($bg -shr 16) -band 0xFF; $bgG = ($bg -shr 8) -band 0xFF; $bgB = $bg -band 0xFF

    $labelTop = -1; $labelBottom = -1; $iconTop = -1; $iconBottom = -1
    $labelFrom = 1
    if ($Icon) {
        # 图标永远是最左边那块：先找第一段连续的"看得出是图标"的列。
        # 判据两种都认：① 亮而饱和（彩色图标，PNG 那套）② 明显比底色暗（灰色占位图标是灰的，
        # 一点饱和度都没有 —— 只认①的话灰图标会被当成背景，于是文字行数会把图标也算进去）。
        $bgMax = [Math]::Max($bgR, [Math]::Max($bgG, $bgB))
        $iconLeft = -1; $iconRight = -1; $run = 0; $gap = 0
        for ($xx = 1; $xx -lt $W - 1; $xx++) {
            $n = 0
            for ($yy = 1; $yy -lt $H - 1; $yy++) {
                $i = (($Y + $yy) * $stride) + (($X + $xx) * 4)
                $b = [int]$bytes[$i]; $g2 = [int]$bytes[$i + 1]; $r2 = [int]$bytes[$i + 2]
                $mx = [Math]::Max($r2, [Math]::Max($g2, $b)); $mn = [Math]::Min($r2, [Math]::Min($g2, $b))
                if ((($mx - $mn) -gt 60 -and $mx -gt 140) -or ($mx -lt ($bgMax - 45))) { $n++ }
            }
            if ($n -ge 3) {
                if ($iconLeft -lt 0) { $iconLeft = $xx }
                $iconRight = $xx; $run++; $gap = 0
            } elseif ($iconLeft -ge 0) {
                $gap++
                if ($gap -gt 1) { break }
            }
        }
        if ($run -lt 4) { $iconLeft = -1; $iconRight = -1 }
        if ($iconRight -ge 0) { $labelFrom = $iconRight + 1 }
        for ($yy = 1; $yy -lt $H - 1; $yy++) {
            $n = 0
            if ($iconLeft -ge 0) {
                for ($xx = $iconLeft; $xx -le $iconRight; $xx++) {
                    $i = (($Y + $yy) * $stride) + (($X + $xx) * 4)
                    $b = [int]$bytes[$i]; $g2 = [int]$bytes[$i + 1]; $r2 = [int]$bytes[$i + 2]
                    $mx = [Math]::Max($r2, [Math]::Max($g2, $b)); $mn = [Math]::Min($r2, [Math]::Min($g2, $b))
                    if ((($mx - $mn) -gt 60 -and $mx -gt 140) -or ($mx -lt ($bgMax - 45))) { $n++ }
                }
            }
            if ($n -ge 3) { if ($iconTop -lt 0) { $iconTop = $yy }; $iconBottom = $yy }
        }
    }

    for ($yy = 1; $yy -lt $H - 1; $yy++) {
        $n = 0
        for ($xx = $labelFrom; $xx -lt $W - 1; $xx++) {
            $i = (($Y + $yy) * $stride) + (($X + $xx) * 4)
            $b = [int]$bytes[$i]; $g2 = [int]$bytes[$i + 1]; $r2 = [int]$bytes[$i + 2]
            $mx = [Math]::Max($r2, [Math]::Max($g2, $b))
            # 暗墨迹、且与背景不同色（ClearType 的彩边也是暗的，所以只看亮度上限）
            if ($mx -lt 210 -and
                ([Math]::Abs($r2 - $bgR) -gt 40 -or [Math]::Abs($g2 - $bgG) -gt 40 -or [Math]::Abs($b - $bgB) -gt 40)) {
                $n++
            }
        }
        if ($n -ge 3) { if ($labelTop -lt 0) { $labelTop = $yy }; $labelBottom = $yy }
    }
    return [pscustomobject]@{ LabelTop = $labelTop; LabelBottom = $labelBottom; IconTop = $iconTop; IconBottom = $iconBottom }
}

# 一个矩形里"最暗的墨迹"有多暗（背景色不算）。用来判定按钮是不是灰的：
# 真功能按钮的文字是近黑（最暗约 26），灰色占位按钮的文字与图标都是灰的（最暗约 130+）。
# 返回 -1 = 这块地方什么都没有。
function Get-DarkestInk {
    param($Shot, [int]$X, [int]$Y, [int]$W, [int]$H)
    $bytes = $Shot.Bytes; $stride = $Shot.Stride

    $counts = @{}
    for ($yy = 2; $yy -lt $H - 2; $yy++) {
        $row = ($Y + $yy) * $stride
        for ($xx = 2; $xx -lt $W - 2; $xx++) {
            $i = $row + (($X + $xx) * 4)
            $key = ([int]$bytes[$i + 2] -shl 16) -bor ([int]$bytes[$i + 1] -shl 8) -bor [int]$bytes[$i]
            if ($counts.ContainsKey($key)) { $counts[$key] = $counts[$key] + 1 } else { $counts[$key] = 1 }
        }
    }
    $bg = 0; $bestN = -1
    foreach ($k in $counts.Keys) { if ($counts[$k] -gt $bestN) { $bestN = $counts[$k]; $bg = $k } }
    $bgR = ($bg -shr 16) -band 0xFF; $bgG = ($bg -shr 8) -band 0xFF; $bgB = $bg -band 0xFF

    $darkest = 255
    for ($yy = 2; $yy -lt $H - 2; $yy++) {
        for ($xx = 2; $xx -lt $W - 2; $xx++) {
            $i = (($Y + $yy) * $stride) + (($X + $xx) * 4)
            $b = [int]$bytes[$i]; $g2 = [int]$bytes[$i + 1]; $r2 = [int]$bytes[$i + 2]
            # 与背景同色 = 空处，跳过（留 40 的余量吃掉抗锯齿）
            if (([Math]::Abs($r2 - $bgR) -le 40) -and ([Math]::Abs($g2 - $bgG) -le 40) -and
                ([Math]::Abs($b - $bgB) -le 40)) { continue }
            $mx = [Math]::Max($r2, [Math]::Max($g2, $b))
            if ($mx -lt $darkest) { $darkest = $mx }
        }
    }
    if ($darkest -eq 255) { return -1 }
    return $darkest
}

function Get-ChildControls {    param([IntPtr]$RootHandle)
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
    $tErr = $p.StandardError.ReadToEndAsync()      # stderr 也要排空，否则写满管道子进程会卡住
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

# 关掉主窗口以外所有可见的顶层窗口（模态对话框 / 消息框），最多试 4 轮。
# 返回 $true = 主窗口没有被模态窗口压着（可以继续点界面）。模态窗口不关掉的话，
# 主窗口一直是禁用状态，后面每一组"点主窗口"的检查都会连带失败（2026-10-04 卡过一次）。
function Close-StrayDialogs {
    param([int]$ProcessId, [IntPtr]$Main)
    for ($round = 0; $round -lt 4; $round++) {
        $strays = @((Get-TopWindows -ProcessId $ProcessId) | Where-Object { $_.H -ne $Main -and $_.Visible })
        if ($strays.Count -eq 0) { break }
        foreach ($w in $strays) { try { [void][TBGui]::CloseWindow($w.H) } catch { } }
        Start-Sleep -Milliseconds 400
    }
    if (@((Get-TopWindows -ProcessId $ProcessId) | Where-Object { $_.H -ne $Main -and $_.Visible }).Count -gt 0) { return $false }
    return [TBGui]::Enabled($Main)
}

# ================================================================ 准备
$settingsBefore = $null
$settingsExisted = Test-Path -LiteralPath $SettingsIni
if ($settingsExisted) { $settingsBefore = [System.IO.File]::ReadAllText($SettingsIni, [System.Text.Encoding]::UTF8) }

# 设置也要能在"上一次跑测试被中断"之后自救：原样留一份备份在磁盘上，
# 下次开工时如果发现备份，就以备份为准（收尾时按它写回去，并删掉备份）。
$settingsBackup = $SettingsIni + '.before-test'
if (Test-Path -LiteralPath $settingsBackup) {
    $settingsBefore = [System.IO.File]::ReadAllText($settingsBackup, [System.Text.Encoding]::UTF8)
    $settingsExisted = $true
    Write-Host '（发现上一次测试留下的设置备份，收尾时按它复原）'
} elseif ($settingsExisted) {
    try { [System.IO.File]::WriteAllText($settingsBackup, $settingsBefore, (New-Object System.Text.UTF8Encoding($false))) } catch { }
}

# 先把设置写成一个已知状态再开界面：用户自己可能把日志面板开着（ShowLogPanel=1），
# 那样 B08「默认不显示日志面板」会莫名其妙地红 —— 测试不能依赖用户的个人设置。
# 跑完在最后按原样写回去（见文件末尾"现场复原"）。
try {
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $SettingsIni))
    [System.IO.File]::WriteAllText($SettingsIni,
        "Theme=light`r`nClickMode=single`r`nConfirmDangerous=1`r`nHideConsole=1`r`nShowLogPanel=0`r`nLogKeepDays=30`r`nPermanentDeleteExe=`r`n",
        (New-Object System.Text.UTF8Encoding($false)))
} catch { }

# 用户自己加的按钮会让"这一页有几个按钮"变得不确定：先请到一边，跑完在"现场复原"里放回去。
$UserToolsJson = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\tools.json'
$UserToolsPaused = $UserToolsJson + '.paused-by-gui-test'
# 自愈：上一次跑测试如果被中断（Ctrl+C / 卡在模态窗口上 / 被沙箱杀掉），用户自己的按钮清单
# 和小工具设置都会留在"暂停"状态回不来 —— 用户会以为"我建的按钮没了"（2026-10-04 真卡过一次）。
if ((Test-Path -LiteralPath $UserToolsPaused) -and (-not (Test-Path -LiteralPath $UserToolsJson))) {
    Move-Item -LiteralPath $UserToolsPaused -Destination $UserToolsJson -Force
    Write-Host '（上一次测试留下的暂停文件已自动放回 tools.json）'
}
$UserToolsHad = Test-Path -LiteralPath $UserToolsJson
if ($UserToolsHad) {
    if (Test-Path -LiteralPath $UserToolsPaused) { Remove-Item -LiteralPath $UserToolsPaused -Force }
    Move-Item -LiteralPath $UserToolsJson -Destination $UserToolsPaused -Force
}

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
Check 'A02 标题栏写着「萌新工具箱 v<版本号>」' ($title -match '萌新工具箱\s*v\d+\.\d+\.\d+') $title

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
$expectRows = [Math]::Ceiling($toolButtons.Count / 4.0)
Check ('B02 排成 4 列') ($cols.Count -eq 4) ('列数=' + $cols.Count)
Check ('B03 排成多行（{0} 个按钮 = {1} 行）' -f $toolButtons.Count, $expectRows) ($rows.Count -eq $expectRows) ('行数=' + $rows.Count)

# 除最后一行外每行必须满 4 个；最后一行 1..4 个（按钮总数不一定是 4 的倍数）
$rowCounts = @($toolButtons | Group-Object Top | Sort-Object Name | ForEach-Object { $_.Count })
$fullRows = @($rowCounts | Where-Object { $_ -eq 4 })
$lastCount = 0
if ($rowCounts.Count -gt 0) { $lastCount = $rowCounts[$rowCounts.Count - 1] }
Check 'B04 除最后一行外每行都是 4 个按钮（最后一行 1..4 个）' `
    (($fullRows.Count -eq ($rows.Count - 1)) -and ($lastCount -ge 1) -and ($lastCount -le 4)) ($rowCounts -join ',')

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

# 每个按钮的文字都必须装得下：装不下就会截断/挤开图标（用户报过"文字超长出现偏移"）
$overflow = @()
foreach ($b in $toolButtons) {
    $need = (Measure-Width $b.Text) + 22   # 22 = 16px 图标 + 图文间距 + 内边距
    if ($need -gt ($b.Width - 2)) { $overflow += ('{0}(需{1}>宽{2})' -f $b.Text, $need, $b.Width) }
}
Check 'B06b 每个按钮的文字都装得下（不溢出、不截断）' ($overflow.Count -eq 0) (($overflow | Select-Object -First 3) -join ' ')

$separators = @($all | Where-Object { $_.Height -le 2 -and $_.Width -gt 200 })
Check 'B07 段与段之间有分隔线' ($separators.Count -ge 1) ('分隔线=' + $separators.Count)

$visibleEdits = @($all | Where-Object { $_.Class -like '*EDIT*' -and $_.Height -gt 20 })
Check 'B08 默认不显示日志面板（没有大文本框）' ($visibleEdits.Count -eq 0) ('可见文本框=' + $visibleEdits.Count)

# 用户报过"按钮的图标没有上下居中"，还有"没做功能的按钮应该是灰的"，这两件事都只能从渲染结果判定。
# 常用设置页签上灰按钮和真按钮混在一起。**别假设左上角那个一定是灰的** ——
# 2026-10-04「任务栏从不合并」被接上真功能，B09/C01d 就假红了。灰按钮名单从 CLI 现取。
$greyNames = @()
$liveNames = @()
foreach ($line in ((Invoke-Exe 'list --tab common') -split "`r?`n")) {
    if ($line -notmatch "`t") { continue }
    $cells = $line -split "`t"
    if ($cells -contains 'placeholder') { $greyNames += $cells[2] } else { $liveNames += $cells[2] }
}

$shot = Get-WindowShot -Handle $main
$gridProbe = @($toolButtons | Where-Object { $greyNames -contains $_.Text } | Sort-Object Top, Left | Select-Object -First 1)
$refInkH = 0
$greyDark = 0
if ($shot -eq $null -or $gridProbe.Count -eq 0) {
    Check 'B09 占位按钮是灰的（最暗墨迹 >= 60）' $false ('窗口截图失败或这一页没有灰按钮（灰=' + $greyNames.Count + '）')
    Check 'B09b 按钮文字完整（墨迹行数 >= 10）' $false '窗口截图失败'
} else {
    $probeRect = @{
        X = $gridProbe[0].Left - $shot.Left; Y = $gridProbe[0].Top - $shot.Top
        W = $gridProbe[0].Width; H = $gridProbe[0].Height
    }
    $ink = Get-InkRows -Shot $shot -Icon -X $probeRect.X -Y $probeRect.Y -W $probeRect.W -H $probeRect.H
    $refInkH = $ink.LabelBottom - $ink.LabelTop + 1
    Check 'B09b 按钮文字完整（墨迹行数 >= 10）' ($refInkH -ge 10) ('墨迹行=' + $ink.LabelTop + '..' + $ink.LabelBottom + ' 行数=' + $refInkH)

    $dark = Get-DarkestInk -Shot $shot -X $probeRect.X -Y $probeRect.Y -W $probeRect.W -H $probeRect.H
    $greyDark = $dark
    # 灰按钮现在是 Enabled=false 的：WinForms 画禁用控件的文字时会在下面描 1px 更深的"影子"
    # （DrawStringDisabled），所以最暗像素实测是 77，而不是我们设的纯灰 #8A8A8A（138）。
    # 阈值取 60；真正有说服力的对比在 C01d —— 灰按钮必须比真按钮明显淡一截。
    Check 'B09 占位按钮是灰的（最暗墨迹 >= 60）' ($dark -ge 60) ('最暗=' + $dark + ' 按钮=' + $gridProbe[0].Text)
}

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

# 真功能按钮长什么样：图标是彩色的，而且要上下居中；文字是近黑的（不是灰的）。
# 用户报过两条："图标没有上下居中"、"没做功能的按钮应该是灰的"，这两条只有渲染像素能判。
$rightProbe = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $rightNames -contains $_.Text })
Start-Sleep -Milliseconds 400     # 换页签后等它重画完再抓像素
$rightShot = Get-WindowShot -Handle $main
if ($rightProbe.Count -eq 0 -or $rightShot -eq $null) {
    Check 'C01b 真功能按钮不是灰的（最暗墨迹 <= 80）' $false '没找到按钮或截图失败'
    Check 'C01c 真功能按钮的图标上下居中（误差 <= 1px）' $false '没找到按钮或截图失败'
} else {
    $rr = @{ X = $rightProbe[0].Left - $rightShot.Left; Y = $rightProbe[0].Top - $rightShot.Top
             W = $rightProbe[0].Width; H = $rightProbe[0].Height }
    $rightDark = Get-DarkestInk -Shot $rightShot -X $rr.X -Y $rr.Y -W $rr.W -H $rr.H
    Check 'C01b 真功能按钮不是灰的（最暗墨迹 <= 80）' ($rightDark -ge 0 -and $rightDark -le 80) `
        ('最暗=' + $rightDark + ' 按钮=' + $rightProbe[0].Text)
    # 这条才是"灰色规则"真正想表达的东西：灰按钮必须明显比真按钮淡
    Check 'C01d 灰按钮比真按钮明显淡（至少差 30）' (($greyDark - $rightDark) -ge 30) `
        ('灰=' + $greyDark + ' 真=' + $rightDark + ' 差=' + ($greyDark - $rightDark))

    # 抓像素偶尔会抓到切页签前的那一帧（图标行会整块偏上），所以量到明显不合理的范围就重抓一次。
    $rightInk = Get-InkRows -Shot $rightShot -Icon -X $rr.X -Y $rr.Y -W $rr.W -H $rr.H
    for ($try = 0; $try -lt 3 -and ($rightInk.IconTop -lt 2 -or $rightInk.IconTop -gt $rr.H - 18); $try++) {
        Start-Sleep -Milliseconds 500
        $again = Get-WindowShot -Handle $main
        if ($again -eq $null) { break }
        $rr2 = @{ X = $rightProbe[0].Left - $again.Left; Y = $rightProbe[0].Top - $again.Top
                  W = $rightProbe[0].Width; H = $rightProbe[0].Height }
        $rightInk = Get-InkRows -Shot $again -Icon -X $rr2.X -Y $rr2.Y -W $rr2.W -H $rr2.H
    }
    if ($rightInk.IconTop -lt 0) {
        Check 'C01c 真功能按钮的图标上下居中（误差 <= 1px）' $false '没在按钮里找到彩色图标'
    } else {
        $iconCentre = ($rightInk.IconTop + $rightInk.IconBottom) / 2.0
        $btnCentre = ($rightProbe[0].Height - 1) / 2.0
        $offset = [Math]::Abs($iconCentre - $btnCentre)
        Check 'C01c 真功能按钮的图标上下居中（误差 <= 1px）' ($offset -le 1.0) `
            ('误差=' + $offset.ToString('0.0') + 'px 图标行=' + $rightInk.IconTop + '..' + $rightInk.IconBottom + ' 中心=' + $iconCentre + ' 按钮中心=' + $btnCentre)
    }
}

Check ('C02 点「清理优化」→ {0} 个按钮' -f $cleanNames.Count) (Switch-Tab -Handle $main -TabName '清理优化' -ExpectNames $cleanNames) ''
Check ('C03 点「系统工具」→ {0} 个按钮' -f $sysNames.Count) (Switch-Tab -Handle $main -TabName '系统工具' -ExpectNames $sysNames) ''
Check ('C04 点「我的工具」→ {0} 个按钮' -f $mineNames.Count) (Switch-Tab -Handle $main -TabName '我的工具' -ExpectNames $mineNames) ''
Check ('C05 回「常用设置」→ {0} 个按钮' -f $commonNames.Count) (Switch-Tab -Handle $main -TabName '常用设置' -ExpectNames $commonNames) ''

$gridAfter = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $commonNames -contains $_.Text })
Check 'C06 换回常用设置后按钮数没变' ($gridAfter.Count -eq $commonNames.Count) ('按钮=' + $gridAfter.Count)

# 「我的工具」：+ 新建按钮 现在是真的（彩色、可点），点开应该是图形化的新建窗口
Check ('C07 切到「我的工具」→ {0} 个按钮' -f $mineNames.Count) (Switch-Tab -Handle $main -TabName '我的工具' -ExpectNames $mineNames) ''
$newBtn = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '+ 新建按钮' })
Check 'C08 「+ 新建按钮」是真按钮（可点，不是灰的）' ($newBtn.Count -eq 1 -and $newBtn[0].Enabled) ''
if ($newBtn.Count -gt 0) {
    [void][TBGui]::Click($newBtn[0].H)
    $newWin = @()
    for ($i = 0; $i -lt 25; $i++) {
        Start-Sleep -Milliseconds 200
        $newWin = @((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible -and $_.Text -match '新建按钮' })
        if ($newWin.Count -gt 0) { break }
    }
    Check 'C09 点开了图形化「新建按钮」窗口' ($newWin.Count -gt 0) (@((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible } | ForEach-Object { $_.Text }) -join ' / ')
    if ($newWin.Count -gt 0) {
        $fields = @(Get-ChildControls -RootHandle $newWin[0].H | Where-Object { $_.Class -like '*EDIT*' -or $_.Class -like '*COMBOBOX*' })
        $hasName = @($fields | Where-Object { $_.Visible }).Count
        Check 'C10 新建窗口里有名称 / 类型 / 路径等输入框' ($hasName -ge 4) ('输入控件=' + $hasName)
        # 「新建按钮」窗口一次只能有一个：往窗口里拖文件应该填进这个窗口，而不是再开一个
        # （用户实测报过："拖入程序图标后会打开一个新的新建按钮弹出的窗口，应该只弹一个的"）。
        $dupWin = @((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible -and $_.Text -match '新建按钮' })
        Check 'C10b 「新建按钮」窗口只有一个' ($dupWin.Count -eq 1) ('找到=' + $dupWin.Count)
        [void][TBGui]::CloseWindow($newWin[0].H)      # 先取消一次，验证取消不写文件
        Start-Sleep -Milliseconds 600
        Check 'C11 取消后新建窗口关掉了（没有写进 tools.json）' (@((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible -and $_.Text -match '新建按钮' }).Count -eq 0) ''

        # 真的建一个按钮，再读回来 —— 这条专门盯"图形化写出来的 tools.json 能不能被读回"：
        # 曾经写出来是 `{ , "id": ...`（多一个逗号），本程序自己的解析器直接拒收，
        # 用户建的按钮就在界面上消失了（2026-10-04 由用户实测发现）。
        [void][TBGui]::Click($newBtn[0].H)
        $newWin2 = @()
        for ($i = 0; $i -lt 25; $i++) {
            Start-Sleep -Milliseconds 200
            $newWin2 = @((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible -and $_.Text -match '新建按钮' })
            if ($newWin2.Count -gt 0) { break }
        }
        if ($newWin2.Count -gt 0) {
            $edits = @(Get-ChildControls -RootHandle $newWin2[0].H | Where-Object { $_.Class -like '*EDIT*' -and $_.Visible } | Sort-Object Top)
            $typed = ''
            if ($edits.Count -ge 2) {
                [void][TBGui]::SetText($edits[0].H, '图形化测试按钮')          # 第一行 = 名称
                [void][TBGui]::SetText($edits[1].H, '%SystemRoot%\system32\notepad.exe')  # 第二行 = 程序路径
                Start-Sleep -Milliseconds 200
                # 回读一次：写不进去就当场判死，别去点"创建按钮"（点不动会留下模态窗口，
                # 主窗口一直是禁用的，后面每一组检查都会连带失败 —— 2026-10-04 卡过一次）。
                $typed = [TBGui]::Text($edits[0].H)
            }
            if ($edits.Count -ge 2 -and $typed -eq '图形化测试按钮') {
                $okBtn = @(Get-ChildControls -RootHandle $newWin2[0].H | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -match '创建按钮' })
                if ($okBtn.Count -gt 0) { [void][TBGui]::Click($okBtn[0].H) }
                Start-Sleep -Milliseconds 1200
                $listed = Invoke-Exe 'list --tab mine'
                Check 'C13 图形化建出来的按钮能被读回来（tools.json 格式自洽）' ($listed -match '图形化测试按钮') `
                    (($listed -split "`r?`n" | Where-Object { $_ -match "`t" }) -join ' / ')
                $inGrid = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '图形化测试按钮' })
                Check 'C14 新按钮立刻出现在「我的工具」上' ($inGrid.Count -eq 1) ('找到=' + $inGrid.Count)
                # 收尾：把测试建的那一条删掉（恢复原来的用户层文件，没有就删文件）
                if ($UserToolsHad) {
                    Copy-Item -LiteralPath $UserToolsPaused -Destination $UserToolsJson -Force
                } else {
                    Remove-Item -LiteralPath $UserToolsJson -Force -ErrorAction SilentlyContinue
                }
                $after = Invoke-Exe 'list --tab mine'
                Check 'C15 收尾后测试按钮已经不在了' (-not ($after -match '图形化测试按钮')) ''
            } else {
                if ($edits.Count -ge 2) {
                    Check 'C13 图形化建出来的按钮能被读回来（tools.json 格式自洽）' $false ('输入框没写进去（读回来是"' + $typed + '"）')
                } else {
                    Check 'C13 图形化建出来的按钮能被读回来（tools.json 格式自洽）' $false '没找到名称 / 路径输入框'
                }
                Check 'C14 新按钮立刻出现在「我的工具」上' $false 'skipped'
                Check 'C15 收尾后测试按钮已经不在了' $false 'skipped'
            }
            # 不管上面走哪条分支，这里都必须把模态窗口关掉（CancelButton = 取消，不写文件）
            [void](Close-StrayDialogs -ProcessId $proc.Id -Main $main)
        } else {
            Check 'C13 图形化建出来的按钮能被读回来（tools.json 格式自洽）' $false '第二次没打开新建窗口'
            Check 'C14 新按钮立刻出现在「我的工具」上' $false 'skipped'
            Check 'C15 收尾后测试按钮已经不在了' $false 'skipped'
        }
    } else {
        Check 'C10 新建窗口里有名称 / 类型 / 路径等输入框' $false 'skipped'
        Check 'C11 取消后新建窗口关掉了（没有写进 tools.json）' $false 'skipped'
        Check 'C13 图形化建出来的按钮能被读回来（tools.json 格式自洽）' $false 'skipped'
        Check 'C14 新按钮立刻出现在「我的工具」上' $false 'skipped'
        Check 'C15 收尾后测试按钮已经不在了' $false 'skipped'
    }
}
Check ('C12 回「常用设置」→ {0} 个按钮' -f $commonNames.Count) (Switch-Tab -Handle $main -TabName '常用设置' -ExpectNames $commonNames) ''

# 走到这里如果还压着模态窗口，后面所有"点主窗口"的检查都会连带失败 —— 先清场，出问题当场暴露。
if (-not (Close-StrayDialogs -ProcessId $proc.Id -Main $main)) {
    Check 'C16 C 组收尾时没有残留的模态窗口' $false '主窗口还被模态窗口压着'
} else {
    Check 'C16 C 组收尾时没有残留的模态窗口' $true ''
}

# ---------------------------------------------------------------- D 组：底部条与日志
Write-Host ''
Write-Host 'D 组 · 底部条 · 搜索 · 日志'

$barButtons = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and @('搜索', '日志', '收起日志', '设置', '关于', '检查更新') -contains $_.Text })
Check 'D01 底部条上有搜索/日志/设置/关于/检查更新' ($barButtons.Count -eq 5) (($barButtons | ForEach-Object { $_.Text }) -join ' ')

# 子控件必须完全落在父容器里 —— 被用户报过两次的"文字被裁"就是这条：
# 底栏按钮 30px 挤在 24px 的条里、以及按钮 20px 装不下 16px 的文字。
if ($barButtons.Count -gt 0) {
    $barHandle = [TBGui]::Parent($barButtons[0].H)
    $barRect = [TBGui]::Rect($barHandle)
    $outside = @()
    foreach ($b in $barButtons) {
        if ($b.Left -lt $barRect[0] -or $b.Top -lt $barRect[1] -or
            $b.Right -gt $barRect[2] -or $b.Bottom -gt $barRect[3]) {
            $outside += $b.Text
        }
    }
    Check 'D01b 底栏按钮完全在底栏范围内（没有被裁）' ($outside.Count -eq 0) (($outside -join ' ') + (' 底栏=' + ($barRect[3] - $barRect[1]) + 'px'))
    $tooShort = @($barButtons | Where-Object { $_.Height -lt 24 })
    Check 'D01c 底栏按钮高度 >= 24px（1px 边框 + 约 3px 内边距 + 16px 文字行）' ($tooShort.Count -eq 0) ((($barButtons | ForEach-Object { $_.Text + '=' + $_.Height }) -join ' '))
}

# 用户报过两次的"底栏按钮差一点才显示全文字"：22px 的按钮只给文字留 14px，最后一行字形被下边缘
# 裁掉。这里不看高度公式，直接数渲染出来的墨迹行数，和按钮墙上同一个字号的按钮对比。
$barShot = Get-WindowShot -Handle $main
if ($barShot -eq $null -or $refInkH -le 0 -or $barButtons.Count -eq 0) {
    Check 'D01e 底栏按钮文字没有被裁（墨迹行数和按钮墙一致）' $false ('截图失败或参考行数=' + $refInkH + ' 底栏按钮=' + $barButtons.Count)
} else {
    $clipped = @()
    $detail = @()
    foreach ($b in $barButtons) {
        $ink = Get-InkRows -Shot $barShot -X ($b.Left - $barShot.Left) -Y ($b.Top - $barShot.Top) -W $b.Width -H $b.Height
        $rows = $ink.LabelBottom - $ink.LabelTop + 1
        $detail += ('{0}={1}行({2}..{3})' -f $b.Text, $rows, $ink.LabelTop, $ink.LabelBottom)
        if ($rows -lt $refInkH) { $clipped += $b.Text }
    }
    $note = ''
    if ($clipped.Count -gt 0) { $note = ' 被裁=' + ($clipped -join ',') }
    Check 'D01e 底栏按钮文字没有被裁（墨迹行数和按钮墙一致）' ($clipped.Count -eq 0) `
        (($detail -join ' ') + ' 参考=' + $refInkH + '行' + $note)
}

$statusLabel = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*STATIC*' -and $_.Text -match '个按钮' })
if ($statusLabel.Count -gt 0) {
    $need = Measure-Width $statusLabel[0].Text
    Check 'D01d 状态栏文字装得下（不会被截）' ($need -le $statusLabel[0].Width) ('文字=' + $need + 'px 标签=' + $statusLabel[0].Width + 'px')
} else {
    Check 'D01d 状态栏文字装得下（不会被截）' $false '没找到状态栏标签'
}

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

# 关于窗口：右键增强收敛成一个按钮以后，它本来没了入口，所以底栏加了「关于」。
# 顺便验证「打开工具目录」这个入口在（工具目录 = bin-tools，外部工具丢进去就能用）。
$aboutButton = @($barButtons | Where-Object { $_.Text -eq '关于' })
if ($aboutButton.Count -gt 0) {
    [void][TBGui]::Click($aboutButton[0].H)
    $aboutWin = @()
    for ($i = 0; $i -lt 25; $i++) {
        Start-Sleep -Milliseconds 200
        $aboutWin = @((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible -and $_.Text -match '关于' })
        if ($aboutWin.Count -gt 0) { break }
    }
    Check 'D06 点底栏「关于」打开关于窗口' ($aboutWin.Count -gt 0) (@((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible } | ForEach-Object { $_.Text }) -join ' / ')
    if ($aboutWin.Count -gt 0) {
        $aboutButtons = @(Get-ChildControls -RootHandle $aboutWin[0].H | Where-Object { $_.Class -like '*BUTTON*' } | ForEach-Object { $_.Text })
        Check 'D07 关于窗口里有「打开工具目录」入口' (($aboutButtons -contains '打开工具目录') -and ($aboutButtons -contains '打开设置目录')) ($aboutButtons -join ' ')
        [void][TBGui]::CloseWindow($aboutWin[0].H)
        Start-Sleep -Milliseconds 600
    } else {
        Check 'D07 关于窗口里有「打开工具目录」入口' $false 'skipped'
    }
} else {
    Check 'D06 点底栏「关于」打开关于窗口' $false '底栏没有关于按钮'
    Check 'D07 关于窗口里有「打开工具目录」入口' $false 'skipped'
}

# ---------------------------------------------------------------- E 组：真按钮能跑 / 灰色按钮点不动
Write-Host ''
Write-Host 'E 组 · 真按钮真的在跑，灰色按钮禁止点击'

# 常见设置页签上哪些按钮是灰的（= placeholder）：直接从 CLI 读，别写死名单
$greyNames = @()
$liveNames = @()
foreach ($line in ((Invoke-Exe 'list --tab common') -split "`r?`n")) {
    if ($line -notmatch "`t") { continue }
    $cells = $line -split "`t"
    if ($cells -contains 'placeholder') { $greyNames += $cells[2] } else { $liveNames += $cells[2] }
}
Check 'E01 CLI 报出这一页的灰按钮和真按钮' (($greyNames.Count + $liveNames.Count) -eq $commonNames.Count) `
    ('灰=' + $greyNames.Count + ' 真=' + $liveNames.Count)

$gridNow = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $commonNames -contains $_.Text })
$greyBad = @()
$liveBad = @()
foreach ($b in $gridNow) {
    if ($greyNames -contains $b.Text) { if ($b.Enabled) { $greyBad += $b.Text } }
    elseif ($liveNames -contains $b.Text) { if (-not $b.Enabled) { $liveBad += $b.Text } }
}
Check ('E02 灰色按钮全部禁止点击（{0} 个 Enabled=false）' -f $greyNames.Count) ($greyBad.Count -eq 0) (($greyBad -join ' ') + ' 灰=' + $greyNames.Count)
Check ('E03 真功能按钮都可以点（{0} 个 Enabled=true）' -f $liveNames.Count) ($liveBad.Count -eq 0) ($liveBad -join ' ')

# 真按钮真跑一次：挑一个只读的（激活状态 = 查授权信息），跑完会弹结果窗口
$probeReal = '激活状态'
$realBtn = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $probeReal })
if ($realBtn.Count -gt 0) {
    $before = @((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main })
    [void][TBGui]::Click($realBtn[0].H)
    $outWin = @()
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 300
        $outWin = @((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible -and $_.Text -match '激活状态' })
        if ($outWin.Count -gt 0) { break }
    }
    Check ('E04 点真按钮「{0}」弹出结果窗口' -f $probeReal) ($outWin.Count -gt 0) `
        ('现有窗口=' + (@((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible } | ForEach-Object { $_.Text }) -join ' / '))
    if ($outWin.Count -gt 0) { [void][TBGui]::CloseWindow($outWin[0].H) ; Start-Sleep -Milliseconds 500 }
    Start-Sleep -Milliseconds 800
    $after = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $probeReal })
    Check 'E05 真按钮跑完自己恢复成可点（没有卡在禁用）' ($after.Count -eq 1 -and $after[0].Enabled) ''
} else {
    Check ('E04 点真按钮「{0}」弹出结果窗口' -f $probeReal) $false '没找到这个按钮'
    Check 'E05 真按钮跑完自己恢复成可点（没有卡在禁用）' $false 'skipped'
}

# 运行中不许改文字：旧版会追加 "…"，图标+文字整组重新居中 → 每点一次图标就跳一下。
# 探针用「激活状态」（只读、要跑一两秒，正好能观察到运行中那一瞬间）。
$probeName = '激活状态'
$probeBtn = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $probeName })
if ($probeBtn.Count -gt 0) {
    [void][TBGui]::Click($probeBtn[0].H)
    Start-Sleep -Milliseconds 180
    $during = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -like '激活*' })
    $duringText = ''
    if ($during.Count -gt 0) { $duringText = $during[0].Text }
    Check 'E06 运行中按钮文字一字不变（不许追加"…"造成跳动）' ($duringText -eq $probeName) ('运行中="' + $duringText + '"')
    $stillThere = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $probeName })
    Check 'E07 运行中按钮还在（文字没变说明没被重排）' ($stillThere.Count -eq 1) ''
    Start-Sleep -Milliseconds 2500
    $statusNow = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*STATIC*' -and $_.Text -match '个按钮' })
    if ($statusNow.Count -gt 0) {
        $needNow = Measure-Width $statusNow[0].Text
        Check 'E08 出现长状态文字后仍然装得下' ($needNow -le $statusNow[0].Width) ('文字=' + $needNow + 'px 标签=' + $statusNow[0].Width + 'px 内容="' + $statusNow[0].Text + '"')
    } else {
        Check 'E08 出现长状态文字后仍然装得下' $false '没找到状态栏标签'
    }
    # 顺手把这次真跑出来的结果窗口关掉，免得影响后面的检查
    foreach ($w in @((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible -and $_.Text -match '激活' })) {
        [void][TBGui]::CloseWindow($w.H)
    }
    Start-Sleep -Milliseconds 500
} else {
    Check 'E06 运行中按钮文字一字不变（不许追加"…"造成跳动）' $false '没找到激活状态按钮'
    Check 'E07 运行中按钮还在（文字没变说明没被重排）' $false 'skipped'
    Check 'E08 出现长状态文字后仍然装得下' $false 'skipped'
}

# ---------------------------------------------------------------- F 组：危险按钮的确认框
Write-Host ''
Write-Host 'F 组 · 危险按钮必须先确认'

# "弹窗"必须按窗口类判定：消息框的类是 #32770。WinForms 的 ToolTip 也是一个顶层窗口
# （类名 tooltips_class32、标题为空），所以按"除主窗口以外的可见窗口"来判会把 tooltip
# 当成弹窗，$dialog[0] 取到的就不是消息框了（加了按钮悬停提示以后踩到过）。
# 注意：调用处必须再包一层 @()。函数里 `return @(单个对象)` 会被解包成一个 PSCustomObject，
# 而单个 PSCustomObject **没有** .Count（返回空），于是 `.Count -gt 0` 恒为 False。
function Get-Dialogs {
    param([int]$ProcessId, [IntPtr]$Main)
    return @((Get-TopWindows -ProcessId $ProcessId) | Where-Object {
        $_.H -ne $Main -and $_.Visible -and $_.Text.Length -gt 0 -and $_.Class -eq '#32770'
    })
}

# 危险按钮现在只有「清理优化」里那两个是真的（清空回收站 / 一键清理垃圾），
# 常用设置里那几个危险按钮还是灰的（点不动）。这里切到清理优化，点「清空回收站」再取消 ——
# 只验证"弹出确认框 + 能取消"，绝不真的清空。
Check ('F00 切到「清理优化」→ {0} 个按钮' -f $cleanNames.Count) (Switch-Tab -Handle $main -TabName '清理优化' -ExpectNames $cleanNames) ''

$danger = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '清空回收站' })
if ($danger.Count -gt 0) { [void][TBGui]::Click($danger[0].H) }
$dialog = @()
for ($i = 0; $i -lt 25; $i++) {
    Start-Sleep -Milliseconds 200
    $dialog = @(Get-Dialogs -ProcessId $proc.Id -Main $main)
    if ($dialog.Count -gt 0) { break }
}
Check 'F01 点危险按钮弹出确认框' ($dialog.Count -gt 0) (($dialog | ForEach-Object { $_.Text }) -join ' ')
if ($dialog.Count -gt 0) {
    Check 'F02 确认框标题是「确认执行」' ($dialog[0].Text -match '确认执行') $dialog[0].Text
    [void][TBGui]::CloseWindow($dialog[0].H)
    Start-Sleep -Milliseconds 600
    Check 'F03 取消后确认框关掉了' (@(Get-Dialogs -ProcessId $proc.Id -Main $main).Count -eq 0) ''
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
    # 复原成功才删备份；没跑完就被中断的话备份还在，下次开工能救回来
    Remove-Item -LiteralPath $settingsBackup -Force -ErrorAction SilentlyContinue
} catch { }

if ($UserToolsHad -and (Test-Path -LiteralPath $UserToolsPaused)) {
    if (Test-Path -LiteralPath $UserToolsJson) { Remove-Item -LiteralPath $UserToolsJson -Force }
    Move-Item -LiteralPath $UserToolsPaused -Destination $UserToolsJson -Force
    Write-Host '（用户自己的 tools.json 已复原）'
}

foreach ($p in @($proc, $procDark)) {
    try { if ($p -and -not $p.HasExited) { $p.Kill() } } catch { }
}

Write-Host ''
Write-Host '----------------------------------------------------------'
Write-Host (" 界面回归: 通过 {0} 项, 失败 {1} 项" -f $script:Pass, $script:Fail)
Write-Host '----------------------------------------------------------'
if ($script:Fail -gt 0) { exit 1 }
exit 0
