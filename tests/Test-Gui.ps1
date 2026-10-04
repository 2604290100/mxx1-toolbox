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
    public static bool Click(IntPtr h) { return PostMessageW(h, 0x00F5, IntPtr.Zero, IntPtr.Zero); }        // BM_CLICK
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

# 先把设置写成一个已知状态再开界面：用户自己可能把日志面板开着（ShowLogPanel=1），
# 那样 B08「默认不显示日志面板」会莫名其妙地红 —— 测试不能依赖用户的个人设置。
# 跑完在最后按原样写回去（见文件末尾"现场复原"）。
try {
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $SettingsIni))
    [System.IO.File]::WriteAllText($SettingsIni,
        "Theme=light`r`nClickMode=single`r`nConfirmDangerous=1`r`nHideConsole=1`r`nShowLogPanel=0`r`nLogKeepDays=30`r`nPermanentDeleteExe=`r`n",
        (New-Object System.Text.UTF8Encoding($false)))
} catch { }

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
# 常用设置页签上的按钮**全是灰色占位按钮**，所以这里量两件事：
#   * 文字墨迹行数（完整的按钮应该有 >= 10 行，被裁就少）—— 同时给 D 组当底栏的参考值
#   * 最暗墨迹有多暗（灰按钮的最暗像素也是灰的，真按钮是近黑）—— 见下面的 B09
# 图标居中那条要彩色图标才量得准，挪到 C 组的「右键增强」页签（那个按钮是真功能，图标是彩色的）。
$shot = Get-WindowShot -Handle $main
$gridProbe = @($toolButtons | Sort-Object Top, Left | Select-Object -First 1)
$refInkH = 0
if ($shot -eq $null -or $gridProbe.Count -eq 0) {
    Check 'B09 占位按钮默认是灰的（最暗墨迹 >= 110）' $false '窗口截图失败'
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
    Check 'B09 占位按钮默认是灰的（最暗墨迹 >= 110，图标和文字都灰）' ($dark -ge 110) `
        ('最暗=' + $dark + ' 按钮=' + $gridProbe[0].Text)
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

    $rightInk = Get-InkRows -Shot $rightShot -Icon -X $rr.X -Y $rr.Y -W $rr.W -H $rr.H
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

# ---------------------------------------------------------------- D 组：底部条与日志
Write-Host ''
Write-Host 'D 组 · 底部条 · 搜索 · 日志'

$barButtons = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and @('搜索', '日志', '收起日志', '设置', '检查更新') -contains $_.Text })
Check 'D01 底部条上有搜索/日志/设置/检查更新' ($barButtons.Count -eq 4) (($barButtons | ForEach-Object { $_.Text }) -join ' ')

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

# 运行中不许改文字：旧版会追加 "…"，图标+文字整组重新居中 → 每点一次图标就跳一下
$probeName = 'hosts 修改'
$probeBtn = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $probeName })
if ($probeBtn.Count -gt 0) {
    [void][TBGui]::Click($probeBtn[0].H)
    Start-Sleep -Milliseconds 180
    $during = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -like 'hosts*' })
    $duringText = ''
    if ($during.Count -gt 0) { $duringText = $during[0].Text }
    Check 'E05 运行中按钮文字一字不变（不许追加"…"造成跳动）' ($duringText -eq $probeName) ('运行中="' + $duringText + '"')
    $stillThere = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $probeName })
    Check 'E06 运行中按钮仍然可用（文字没变说明没被重排）' ($stillThere.Count -eq 1) ''
    Start-Sleep -Milliseconds 900
    $statusNow = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*STATIC*' -and $_.Text -match '个按钮' })
    if ($statusNow.Count -gt 0) {
        $needNow = Measure-Width $statusNow[0].Text
        Check 'E07 出现长状态文字后仍然装得下' ($needNow -le $statusNow[0].Width) ('文字=' + $needNow + 'px 标签=' + $statusNow[0].Width + 'px 内容="' + $statusNow[0].Text + '"')
    } else {
        Check 'E07 出现长状态文字后仍然装得下' $false '没找到状态栏标签'
    }
} else {
    Check 'E05 运行中按钮文字一字不变（不许追加"…"造成跳动）' $false '没找到 hosts 修改 按钮'
    Check 'E06 运行中按钮仍然可用（文字没变说明没被重排）' $false 'skipped'
    Check 'E07 出现长状态文字后仍然装得下' $false 'skipped'
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

$danger = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '禁用 SmartScreen' })
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
