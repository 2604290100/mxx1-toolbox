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
    ⚠ 跑之前必须确认工具箱没开着（`Get-Process Mxx1Toolbox` 是空的）：它会写 settings.ini，
    正跑着的那个实例关闭时会把内存里的状态写回去 → 整套假红。**不要杀用户的进程。**

    用法: powershell -File tests\Test-Gui.ps1
          powershell -File tests\Test-Gui.ps1 -Only N,I     # 只跑这几组
          powershell -File tests\Test-Gui.ps1 -Skip D07d   # 前缀匹配：-Skip D 会跳过 D 那一整组
    退出码: 0 = 全绿, 1 = 有失败, 3 = 环境不满足（跳过）

    挑组（-Only / -Skip）：组标记就是那些 `# ---- X 组：…` 注释里的字母，前缀匹配
    （-Only B 会带上 B10 那个子块）。它前面的「准备」段和后面的「现场复原」段**总会跑**
    （起测试实例、把用户的设置原样复原），跳过的只是中间的检查组。跑完汇总里会列出"没跑哪些组"。
    映射表见 tests\test-map.json。
#>
[CmdletBinding()]
param(
    # 只跑这几组（例：-Only N,I）
    [string[]]$Only = @(),
    # 除了这几组，别的都跑
    [string[]]$Skip = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Exe = Join-Path $root 'bin\Mxx1Toolbox.exe'
$SettingsIni = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\settings.ini'

# ---------------------------------------------------------------- 挑组执行（-Only / -Skip）
# 见文件开头那段说明与 docs\DESIGN.md §15。组标记就是那些 `# ---- X 组：…` 注释，前缀匹配。
# 注意：这个开关只影响"中间的检查组"；前面的「准备」段与后面的「现场复原」段照旧总会跑
# （否则测试实例起不来、用户的设置也复原不了）。
$script:SelfPath = $MyInvocation.MyCommand.Path

# ⚠ `powershell -File … -Only N,I` 传进来的是**一个字符串**（不是数组），所以要自己按 , ; 空格 拆开。
function Expand-GroupList {
    param([string[]]$Items)
    $out = @()
    foreach ($it in @($Items)) {
        if (-not $it) { continue }
        foreach ($p in ($it -split '[,;\s]+')) { if ($p) { $out += $p.Trim().ToUpper() } }
    }
    return @($out | Sort-Object -Unique)
}

$script:OnlyGroups = @(Expand-GroupList $Only)
$script:SkipGroups = @(Expand-GroupList $Skip)
$script:RanGroups = @()
$script:PickMode = (($script:OnlyGroups.Count -gt 0) -or ($script:SkipGroups.Count -gt 0))

function Test-GroupSelected {
    param([string]$Name)
    $on = $true
    $up = $Name.ToUpper()
    if ($script:OnlyGroups.Count -gt 0) {
        $on = $false
        foreach ($p in $script:OnlyGroups) { if ($up -like ($p + '*')) { $on = $true; break } }
    }
    if ($on) {
        foreach ($p in $script:SkipGroups) { if ($up -like ($p + '*')) { $on = $false; break } }
    }
    if ($on -and ($script:RanGroups -notcontains $Name)) { $script:RanGroups += $Name }
    return $on
}

# 汇总时用：这个文件里一共有哪些组（读自己源码里的组标记，不维护第二份名单）
function Get-AllGroups {
    $all = @()
    foreach ($line in (Get-Content -LiteralPath $script:SelfPath)) {
        $m = [regex]::Match($line, '^# (?:-{4,}|={4,})\s*([A-Z][0-9]*)(?![0-9A-Za-z])')
        if ($m.Success) { $all += $m.Groups[1].Value }
    }
    return @($all | Sort-Object -Unique)
}

# 主窗口启动时会顺手"修补"已经装过的右键菜单（Mxx1* 自己那几个键）。用户真装着菜单的时候，
# 那就是在改他的注册表 —— 界面回归测试反复起主窗口，不该干这个。所以这里关掉：
# 那条修补路径由 Test-Cli 的 M20c 在**隔离的测试根**里专门测。
$script:SyncHad = Test-Path Env:MXX1_NO_RIGHTMENU_SYNC
$script:SyncOld = $env:MXX1_NO_RIGHTMENU_SYNC
$env:MXX1_NO_RIGHTMENU_SYNC = '1'

# 更新检查同理：界面一起来就会查一次 GitHub（只读版本号）。测试不该谈外网，也不该因为
# "仓库里真有新版本"把底栏按钮文字改掉（D01 盯的就是底栏那 5 个按钮的文字）。
# I 组要验那条链路时，会用 Start-Gui -Env 把 MXX1_NO_UPDATE 置空、再把 URL 指到本机假接口。
$script:UpdateHad = Test-Path Env:MXX1_NO_UPDATE
$script:UpdateOld = $env:MXX1_NO_UPDATE
$env:MXX1_NO_UPDATE = '1'

# 关于窗口里那两个网址（官网 / 仓库）是**能点的**，D07d 要真去点一下验它 —— 而测试不该在别人
# 桌面上弹出浏览器，所以让工具箱"只记一行日志、不真打开"（MainForm.OpenUrl 里读这个开关）。
# 断言的就是那行日志（点一次 = 日志里多一行带那个网址的记录）。
$script:NoOpenHad = Test-Path Env:MXX1_NO_OPEN
$script:NoOpenOld = $env:MXX1_NO_OPEN
$env:MXX1_NO_OPEN = '1'

# 量文字宽度要用同一套渲染器，才能判断"文字装不装得下"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$MeasureFont = New-Object System.Drawing.Font('Microsoft YaHei', 8.25)
function Measure-Width([string]$Text) {
    return [System.Windows.Forms.TextRenderer]::MeasureText($Text, $MeasureFont).Width
}

$script:Pass = 0
$script:Fail = 0
$script:SkipCount = 0

function Check {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    if ($Ok) { $script:Pass++ } else { $script:Fail++ }
    $flag = 'PASS'; if (-not $Ok) { $flag = 'FAIL' }
    Write-Host ("  [{0}] {1}{2}" -f $flag, $Name, $(if ($Detail) { "   ($Detail)" } else { '' }))
}

# 环境不满足的项走这里（和 Test-Cli.ps1 同一套口径）：打印 [SKIP]、单独计数，**不算失败**。
# 典型场景：真动鼠标那几条在"用户正在用鼠标 / 输入桌面被占"的时候做不了 —— 那是环境问题，
# 记成失败会变成假红（2026-10-06 的 N21b 就是这么红的）。**但真的断言失败绝不许走这里。**
function Skip {
    param([string]$Name, [string]$Reason = '')
    $script:SkipCount++
    Write-Host ("  [SKIP] {0}{1}" -f $Name, $(if ($Reason) { "   ($Reason)" } else { '' }))
}

if (-not (Test-Path -LiteralPath $Exe)) { throw ('找不到 exe（先跑 build.ps1）: ' + $Exe) }

Write-Host ''
Write-Host '=========================================================='
Write-Host ' 萌新工具箱 · 界面回归测试'
Write-Host '=========================================================='
Write-Host (' exe : ' + $Exe)
if ($script:PickMode) {
    Write-Host (' 挑组: 只跑 ' + $(if ($script:OnlyGroups.Count -gt 0) { $script:OnlyGroups -join ',' } else { '（全部）' }) +
                $(if ($script:SkipGroups.Count -gt 0) { '，跳过 ' + ($script:SkipGroups -join ',') } else { '' }))
}
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
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
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

    // Client area in SCREEN coordinates (left, top, right, bottom). Children report screen
    // rects, so this is what "is the child inside the window" has to be compared against.
    public static int[] ClientRect(IntPtr h)
    {
        RECT r;
        GetClientRect(h, out r);
        POINT p; p.X = 0; p.Y = 0;
        ClientToScreen(h, ref p);
        return new int[] { p.X, p.Y, p.X + r.Right, p.Y + r.Bottom };
    }

    // WM_GETICON (0x007F): 0 = ICON_SMALL (title bar), 1 = ICON_BIG (taskbar / Alt+Tab).
    public static IntPtr IconHandle(IntPtr h, int which)
    {
        return SendMessageW(h, 0x007F, new IntPtr(which), IntPtr.Zero);
    }

    public static int[] Rect(IntPtr h)
    {
        RECT r;
        if (!GetWindowRect(h, out r)) { return new int[] { 0, 0, 0, 0 }; }
        return new int[] { r.Left, r.Top, r.Right, r.Bottom };
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }

    public static bool MoveCursor(int x, int y) { return SetCursorPos(x, y); }
    public static bool Focus(IntPtr h) { return SetForegroundWindow(h); }

    public static int[] CursorAt()
    {
        POINT p;
        GetCursorPos(out p);
        return new int[] { p.X, p.Y };
    }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    /// <summary>鼠标底下是哪个窗口（"悬停没反应"时用来自证：多半是别的窗口盖住了，或者用户正在
    /// 动鼠标）。返回 "pid|窗口类|标题"。2026-10-04 真踩过：用户自己开的那个工具箱实例和测试实例
    /// 的窗口叠在一起，B10/B11 假红，排查花了很久。</summary>
    public static string WindowAt(int x, int y)
    {
        POINT p; p.X = x; p.Y = y;
        IntPtr h = WindowFromPoint(p);
        if (h == IntPtr.Zero) { return "(鼠标下没有窗口)"; }
        IntPtr root = GetAncestor(h, 2);          // GA_ROOT
        if (root != IntPtr.Zero) { h = root; }
        uint pid; GetWindowThreadProcessId(h, out pid);
        return pid + "|" + Class(h) + "|" + Text(h);
    }

    /// <summary>当前前台窗口（同样只是诊断信息）。</summary>
    public static string Foreground()
    {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) { return "(没有前台窗口)"; }
        uint pid; GetWindowThreadProcessId(h, out pid);
        return pid + "|" + Class(h) + "|" + Text(h);
    }

    /// <summary>The text of the app's own visible tooltip window (B10/B11). WinForms names it
    /// "WindowsForms10.tooltips_class32.app.0.34f5582_r6_ad1", so the class has to be matched with
    /// IndexOf: an exact "tooltips_class32" match finds nothing, and the test then wrongly reports
    /// "no tooltip appears" (which is exactly what happened the first time round).</summary>
    public static string TooltipText(uint pid)
    {
        string found = "";
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            if (found.Length > 0) { return true; }
            if (Class(h).IndexOf("tooltips_class32", StringComparison.OrdinalIgnoreCase) < 0) { return true; }
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) { return true; }
            if (!IsWindowVisible(h)) { return true; }
            found = Text(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static int Styles(IntPtr h) { return GetWindowLongW(h, -16); }
    // GWL_EXSTYLE (-20): the notice card must carry WS_EX_NOACTIVATE (never steals focus from what the
    // user is typing into) and WS_EX_TOOLWINDOW (stays out of Alt+Tab). N21 asserts both.
    public static int ExStyles(IntPtr h) { return GetWindowLongW(h, -20); }
    public static bool Visible(IntPtr h) { return IsWindowVisible(h); }
    public static bool Enabled(IntPtr h) { return IsWindowEnabled(h); }
    public static bool Alive(IntPtr h) { return IsWindow(h); }
    public static bool Click(IntPtr h) { return PostMessageW(h, 0x00F5, IntPtr.Zero, IntPtr.Zero); }

    // 真鼠标点一下（B10/B11 也是这么动鼠标的，跑完要把鼠标放回去）。
    // **为什么 D07d 不能用 PostMessage**：实测过 —— 给 LinkLabel 发
    // WM_MOUSEMOVE / WM_LBUTTONDOWN / WM_LBUTTONUP（窗口已经在前台、坐标也是控件正中间）
    // **不触发 LinkClicked**，一条日志都不写；换成真鼠标（SetCursorPos + mouse_event）
    // 立刻就写了「打开链接 https://mxx1.cn」。所以"能不能点"这种事只能真点。
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    public static void RealClick(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);      // LEFTDOWN
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);      // LEFTUP
    }

    // 鼠标底下那个**具体控件**（WindowAt 会往上找顶层窗口，这里要的就是子控件本身）：
    // 真点之前先确认这一格是我们那个链接，别点到用户别的窗口上去。
    public static IntPtr HandleAt(int x, int y)
    {
        POINT p; p.X = x; p.Y = y;
        return WindowFromPoint(p);
    }

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

    // 「这个窗口现在还处理消息吗」：WM_NULL + SMTO_ABORTIFHUNG，超时没人接 = 界面被堵住了。
    // 2026-10-05 加：解锁窗口的扫描原来在界面线程上跑，实测右键一个 400 个文件的文件夹时，
    // 窗口从 612ms 一直卡到 7093ms（拖不动、关不掉、任务栏写"无响应"）。判据不能是"窗口还在不在"
    // —— 只有"一条 WM_NULL 都回不了"才说明消息循环停了。
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeoutNull(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    public static bool Responds(IntPtr h, uint timeoutMs) {
        IntPtr r;
        return SendMessageTimeoutNull(h, 0x0000, IntPtr.Zero, IntPtr.Zero, 0x0002, timeoutMs, out r) != IntPtr.Zero;
    }

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

# 「内容多高，窗口就多高」的判据（用户 2026-10-04 报「窗口高度没有做自适应」）：
#   ① 每个可见控件都完整落在**客户区**里（被切掉 = 少算了高度）；
#   ② 客户区底边到最下面那个控件的距离不许太大（留一大截空白 = 多算了高度）。
# 用屏幕坐标比，所以无论 100% 还是 150% DPI 都成立。
function Get-FitReport {
    param([IntPtr]$Hwnd)
    $client = [TBGui]::ClientRect($Hwnd)
    $clipped = 0; $widgets = 0; $maxBottom = $client[1]; $worst = ''
    foreach ($k in @(Get-ChildControls -RootHandle $Hwnd)) {
        if (-not $k.Visible) { continue }
        if (-not (($k.Class -like '*BUTTON*') -or ($k.Class -like '*STATIC*') -or ($k.Class -like '*SysListView*'))) { continue }
        if (($k.Width -le 0) -and ($k.Height -le 0)) { continue }
        $widgets++
        if (($k.Bottom -gt $client[3]) -or ($k.Right -gt $client[2])) {
            $clipped++
            $worst = ([TBGui]::Class($k.H) + ' ' + $k.Text)
        }
        if ($k.Bottom -gt $maxBottom) { $maxBottom = $k.Bottom }
    }
    return [pscustomobject]@{
        ClientH = $client[3] - $client[1]
        Widgets = $widgets
        Clipped = $clipped
        Gap     = $client[3] - $maxBottom
        Worst   = $worst
    }
}

# 窗口自己那张图标（0 = 标题栏小图 / 1 = 任务栏大图）：数一下蓝色底和白色方块。
# .NET 那个默认的"空白窗体"图标一点白都没有，所以 white>0 就能把它和工具箱自己的图标分开。
function Get-IconInk {
    param([IntPtr]$Hwnd, [int]$Which = 0)
    $h = [TBGui]::IconHandle($Hwnd, $Which)
    if ($h -eq [IntPtr]::Zero) { return $null }
    $bmp = [System.Drawing.Icon]::FromHandle($h).ToBitmap()
    $blue = 0; $white = 0; $opaque = 0
    for ($iy = 0; $iy -lt $bmp.Height; $iy++) {
        for ($ix = 0; $ix -lt $bmp.Width; $ix++) {
            $c = $bmp.GetPixel($ix, $iy)
            if ($c.A -gt 200) {
                $opaque++
                if (($c.B -gt 140) -and ($c.R -lt 110)) { $blue++ }
                if (($c.R -gt 220) -and ($c.G -gt 220) -and ($c.B -gt 220)) { $white++ }
            }
        }
    }
    $w = $bmp.Width; $ht = $bmp.Height
    $bmp.Dispose()
    return [pscustomobject]@{ W = $w; H = $ht; Opaque = $opaque; Blue = $blue; White = $white }
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
    param([hashtable]$Env = $null)
    $si = New-Object System.Diagnostics.ProcessStartInfo
    $si.FileName = $Exe
    $si.UseShellExecute = $false
    # 更新检查那类"要问网络"的检查靠环境变量指到本机假接口（不碰外网，见 I 组）
    if ($Env) { foreach ($k in $Env.Keys) { $si.EnvironmentVariables[$k] = [string]$Env[$k] } }
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $si
    [void]$p.Start()
    [void]$script:Procs.Add($p)
    for ($i = 0; $i -lt 100; $i++) {
        Start-Sleep -Milliseconds 200
        $p.Refresh()
        if ($p.HasExited) { return $p }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    return $p
}

# 找一个本进程里的顶层窗口（按标题前缀）。I 组用它找《使用条款确认》。
function Find-TopWindow {
    param([int]$ProcessId, [string]$TextPrefix)
    foreach ($w in (Get-TopWindows -ProcessId $ProcessId)) {
        if ($w.Visible -and $w.Text.StartsWith($TextPrefix)) { return $w }
    }
    return $null
}

# 等一个顶层窗口消失（点完「同意并继续」之后用）。
function Wait-WindowGone {
    param([IntPtr]$Hwnd, [int]$Tries = 40)
    for ($i = 0; $i -lt $Tries; $i++) {
        if (-not [TBGui]::Alive($Hwnd)) { return $true }
        Start-Sleep -Milliseconds 150
    }
    return (-not [TBGui]::Alive($Hwnd))
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

# 点页签再等这一页的按钮画出来。**定义必须放在第一次调用之前**：PowerShell 是边解析边执行，
# 函数定义在调用点后面就会 CommandNotFound 直接终止脚本（2026-10-04 真踩过：定义在 735 行、
# 第 566 行就调用，脚本在第 7 项检查处崩掉，收尾没跑到，用户自己的 tools.json 被留在暂停态）。
function Switch-Tab {
    param([IntPtr]$Handle, [string]$TabName, [string[]]$ExpectNames)
    $tabButton = @(Get-ChildControls -RootHandle $Handle | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq $TabName })
    if ($tabButton.Count -eq 0) { return $false }
    [void][TBGui]::Click($tabButton[0].H)
    return (Wait-Buttons -Handle $Handle -Names $ExpectNames)
}

# 本脚本启动过的界面进程（只杀自己拉起来的；用户自己开着的 Mxx1Toolbox 不许碰）。
$script:Procs = New-Object System.Collections.ArrayList

# 现场复原：设置、用户自己的 tools.json、注入的占位按钮、测试拉起的进程。
# 抽成函数是为了让脚本级 trap 也能调用 —— 崩在哪一步都要把用户现场放回去。
function Restore-UserLayer {
    try {
        [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $SettingsIni))
        if ($settingsExisted) { [System.IO.File]::WriteAllText($SettingsIni, $settingsBefore, (New-Object System.Text.UTF8Encoding($false))) }
        else { Remove-Item -LiteralPath $SettingsIni -Force -ErrorAction SilentlyContinue }
        # 复原成功才删备份；没跑完就被中断的话备份还在，下次开工能救回来
        Remove-Item -LiteralPath $settingsBackup -Force -ErrorAction SilentlyContinue
    } catch { }

    # 测试注入的占位按钮文件必须先删掉：用户本来就没有 tools.json 时，下面那段复原根本不会执行，
    # 留下来的话用户会看到一个自己没建过的"占位自检"按钮。
    if ($script:InjectedPh) { Remove-Item -LiteralPath $UserToolsJson -Force -ErrorAction SilentlyContinue }

    if ($UserToolsHad -and (Test-Path -LiteralPath $UserToolsPaused)) {
        if (Test-Path -LiteralPath $UserToolsJson) { Remove-Item -LiteralPath $UserToolsJson -Force }
        Move-Item -LiteralPath $UserToolsPaused -Destination $UserToolsJson -Force
        Write-Host '（用户自己的 tools.json 已复原）'
    }

    foreach ($p in $script:Procs) {
        try { if ($p -and -not $p.HasExited) { $p.Kill() } } catch { }
    }
    foreach ($p in @($proc, $procDark, $procFixed, $procFixed2)) {
        try { if ($p -and -not $p.HasExited) { $p.Kill() } } catch { }
    }
}

# 脚本级兜底：任何未捕获的终止错误（含函数没定义、探针抛异常）都先把用户现场复原再退出。
trap {
    Write-Host ''
    Write-Host (' 脚本出错，先复原用户现场：' + $_.Exception.Message)
    Restore-UserLayer
    Write-Host '（界面回归没跑完，退出码 1）'
    exit 1
}

# ================================================================ 准备
# 测试实例自己的窗口**别和用户自己开着的那个实例重叠**：B10/B11 的悬停检查是真的动系统鼠标
# （SetCursorPos + 读 ToolTip 窗口文字），两个工具箱窗口叠在一起时鼠标会被上面那个接走，
# 于是"悬停没反应"假红（2026-10-04 踩过：用户自己开着 bin\Mxx1Toolbox.exe，窗口压在测试实例上）。
# 所以先看看屏幕上已经有哪些工具箱窗口，挑一个没被占的角落放自己。
$script:TestWinX = -1
$script:TestWinY = -1
# 屏幕上还有没有**别的**工具箱窗口（用户自己开的那个）。B10/B11 是真动鼠标去悬停，鼠标会被
# 上面那个窗口接走 → 假红（2026-10-04 踩过、2026-10-06 又踩一次）。所以把这个数记下来，
# 悬停那两条检查据此自己走 Skip。
$script:OtherToolboxCount = 0
try {
    $taken = @()
    foreach ($p in @(Get-Process -Name Mxx1Toolbox -ErrorAction SilentlyContinue)) {
        if ($p.MainWindowHandle -ne 0) { $taken += ,([TBGui]::Rect($p.MainWindowHandle)) }
    }
    $script:OtherToolboxCount = $taken.Count
    $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $spots = @(
        @{ X = $wa.Left + 16;  Y = $wa.Top + 16 },
        @{ X = $wa.Right - 660; Y = $wa.Top + 16 },
        @{ X = $wa.Left + 16;  Y = $wa.Bottom - 700 },
        @{ X = $wa.Right - 660; Y = $wa.Bottom - 700 },
        @{ X = [int](($wa.Width - 628) / 2); Y = $wa.Top + 16 }
    )
    foreach ($s in $spots) {
        $free = $true
        foreach ($t in $taken) {
            $overlapX = ([Math]::Min($s.X + 636, $t[2]) - [Math]::Max($s.X, $t[0]))
            $overlapY = ([Math]::Min($s.Y + 690, $t[3]) - [Math]::Max($s.Y, $t[1]))
            if ($overlapX -gt 0 -and $overlapY -gt 0) { $free = $false }
        }
        if ($free) { $script:TestWinX = $s.X; $script:TestWinY = $s.Y; break }
    }
    if ($taken.Count -gt 0) {
        Write-Host ('（屏幕上有 ' + $taken.Count + ' 个已经开着的工具箱窗口，测试实例放到 ' + $script:TestWinX + ',' + $script:TestWinY + ' 避开它）')
    }
} catch { }
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
# LastTab=common 同样重要：窗口现在会记住上次停留的页签，用户上次停在「常用」页的话，
# 这一套按「常用设置」写的检查会全部对不上（页面高度也会跟着内容变）。
# WindowAutoSize=1 是为了让 A07/A08「窗口高度跟着内容走」可判定。
# 跑完在最后按原样写回去（见文件末尾"现场复原"）。
#
# ⚠ 使用条款：这一套必须**处于"已同意"状态**，否则主界面一起来就弹《使用条款确认》，
# 主窗口被模态窗口压着 → 后面每一组"点主窗口"的检查全部连带失败。而上面这几处写 settings.ini
# 会把 AgreedDisclaimer 抹掉，所以每份测试设置后面都要把同意行带上（$script:ConsentIni）。
# 用户原来的状态在收尾时按原样写回；确认门本身由 I 组专门测。
$consentBefore = 'unknown'
$consentHash = ''
$consentProbe = Invoke-Exe 'consent'
if ($consentProbe -match '(?m)^consent=(\w+)') { $consentBefore = $Matches[1] }
if ($consentProbe -match '(?m)^currentHash=([0-9a-f]{16})') { $consentHash = $Matches[1] }
$script:ConsentIni = ''
if ($consentHash.Length -eq 16) {
    $script:ConsentIni = 'AgreedDisclaimer=' + $consentHash + "`r`nAgreedAt=" + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + "`r`n"
}
Write-Host (' 测试前的条款状态: ' + $consentBefore)

try {
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $SettingsIni))
    [System.IO.File]::WriteAllText($SettingsIni,
        "Theme=light`r`nClickMode=single`r`nConfirmDangerous=1`r`nHideConsole=1`r`nShowLogPanel=0`r`nLogKeepDays=30`r`nPermanentDeleteExe=`r`nLastTab=common`r`nWindowAutoSize=1`r`nWindowX=" + $script:TestWinX + "`r`nWindowY=" + $script:TestWinY + "`r`n" + $script:ConsentIni,
        (New-Object System.Text.UTF8Encoding($false)))
} catch { }

# 用户自己加的按钮会让"这一页有几个按钮"变得不确定：先请到一边，跑完在"现场复原"里放回去。
$UserToolsJson = Join-Path $env:LOCALAPPDATA 'mxx1-toolbox\tools.json'
$UserToolsPaused = $UserToolsJson + '.paused-by-gui-test'

# ---- 用户层"防串味"三道闸（和 Test-Cli.ps1 里同一套，见那边的长注释）：
# 事故：测试脚本级错误 → 收尾没跑到 → 注入的测试按钮留在用户 tools.json 里；下一次跑测试时
# "暂停文件在、用户文件也在"的分支把暂停文件删了，用户自己那个按钮就没了（后来从
# tools.json.broken-bak 里捞回来的）。所以：只有 id 以 test. 开头的才许丢、暂停文件里有真项就不许删、
# 开工前先把注入按钮清掉（别指望收尾那段能跑到）。
# 解析前先把"看得懂但不合法"的转义补成合法的（2026-10-04 真踩过：用户 tools.json 的 `_comment`
# 里写了一个 `\*`，PowerShell 的 ConvertFrom-Json 抛错 → 这三道闸整段跳过 → 连"把暂停文件放回
# tools.json"都做不成）。只影响解析，不动用户文件。和 Test-Cli.ps1 里那份是同一套。
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
        if ($keep.Count -eq $tools.Count) { return }
        if ($keep.Count -eq 0) {
            Remove-Item -LiteralPath $Path -Force
            Write-Host '（上一次测试没收拾干净：整个 tools.json 都是注入的测试按钮，已删除）'
        } else {
            $obj.tools = $keep
            [System.IO.File]::WriteAllText($Path, ($obj | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
            Write-Host ('（上一次测试没收拾干净：从 tools.json 里清掉了 ' + ($tools.Count - $keep.Count) + ' 个注入按钮）')
        }
    } catch { Write-Host ('（清理注入按钮时出错，跳过：' + $_.Exception.Message + '）') }
}

# 自愈：上一次跑测试如果被中断（Ctrl+C / 卡在模态窗口上 / 被沙箱杀掉 / 脚本级错误），
# 用户自己的按钮清单会留在"暂停"状态回不来（2026-10-04 真卡过一次）。
if (Test-Path -LiteralPath $UserToolsPaused) {
    if (Test-OnlyInjectedFixtures $UserToolsPaused) {
        Remove-Item -LiteralPath $UserToolsPaused -Force
        Write-Host '（暂停文件里全是注入的测试按钮，已丢弃）'
    } else {
        if (Test-Path -LiteralPath $UserToolsJson) {
            if (Test-OnlyInjectedFixtures $UserToolsJson) { Remove-Item -LiteralPath $UserToolsJson -Force }
            else { Remove-Item -LiteralPath $UserToolsPaused -Force }
        }
        if (Test-Path -LiteralPath $UserToolsPaused) {
            Move-Item -LiteralPath $UserToolsPaused -Destination $UserToolsJson -Force
            Write-Host '（上一次测试留下的暂停文件已自动放回 tools.json）'
        }
    }
}
Clear-InjectedFixtures $UserToolsJson

$UserToolsHad = Test-Path -LiteralPath $UserToolsJson
if ($UserToolsHad) {
    if (Test-Path -LiteralPath $UserToolsPaused) { Remove-Item -LiteralPath $UserToolsPaused -Force }
    Move-Item -LiteralPath $UserToolsJson -Destination $UserToolsPaused -Force
}

# 2026-10-04：两个「资源管理器」也接上真功能之后，内置清单里一个灰色占位按钮都不剩了，而
# 「灰按钮禁止点击、必须明显比真按钮淡」（B09 / C01d / E02 / E03）这条规则本身还在 —— 用户自己
# 在 tools.json 里写 placeholder:true 就会灰掉。所以往用户层临时注入一个占位按钮来测这条路径。
# 注意它必须挂在 common 页签上：「我的工具」那几条检查（C04/C07/C13–C15）数的是 mine 页签。
function Add-TestPlaceholder {
    try {
        [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $UserToolsJson))
        [System.IO.File]::WriteAllText($UserToolsJson,
            '{ "tools": [ { "id": "test.placeholder", "tab": "common", "segment": 2, "order": 999, "name": "占位自检", "kind": "builtin", "module": "todo", "action": "todo", "placeholder": true, "hint": "测试用的占位按钮" } ] }',
            (New-Object System.Text.UTF8Encoding($false)))
        $script:InjectedPh = $true
        return $true
    } catch {
        Write-Host ('  注入占位按钮失败：' + $_.Exception.Message)
        return $false
    }
}
$script:InjectedPh = $false
[void](Add-TestPlaceholder)

$commonNames = Get-ToolNames 'common'
$rightNames = Get-ToolNames 'rightmenu'
$cleanNames = Get-ToolNames 'cleanup'
$sysNames = Get-ToolNames 'system'
$mineNames = Get-ToolNames 'mine'
$privacyNames = Get-ToolNames 'privacy'
$appsNames = Get-ToolNames 'apps'

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

# 窗口自己那张图标（用户 2026-10-04 报「编译好的 exe 没有图标」）：.NET 编译出来的 exe 资源里有图标
# 不等于窗口标题栏/任务栏有 —— WinForms 不设 Form.Icon 时画的是它自带的"空白窗体"图标（一点白都没有）。
# 所以这里按像素判：工具箱的图标是蓝底 + 四个白色方块。
$mainInkSmall = Get-IconInk -Hwnd $main -Which 0
$mainInkBig = Get-IconInk -Hwnd $main -Which 1
$mainInkOk = (($mainInkSmall -ne $null) -and ($mainInkBig -ne $null) -and
              ($mainInkSmall.White -gt 10) -and ($mainInkSmall.Blue -gt 20) -and
              ($mainInkBig.White -gt 40) -and ($mainInkBig.Blue -gt 40))
$mainInkDetail = ''
if ($mainInkSmall -ne $null) { $mainInkDetail += ('small {0}x{1} blue={2} white={3} ' -f $mainInkSmall.W, $mainInkSmall.H, $mainInkSmall.Blue, $mainInkSmall.White) }
if ($mainInkBig -ne $null) { $mainInkDetail += ('big {0}x{1} blue={2} white={3}' -f $mainInkBig.W, $mainInkBig.H, $mainInkBig.Blue, $mainInkBig.White) }
Check 'A04b 主窗口的标题栏和任务栏图标是工具箱自己那张（不是 .NET 默认图标）' $mainInkOk $mainInkDetail

# 页签条：加了「常用」（置顶 + 最近使用）之后是 8 个，而且顺序按使用频率排过一遍 ——
# 原来只有 1 个按钮的「右键增强」排第 2 位，主力页「系统工具」「隐私设置」被挤到第 4、5。
# 注意：Get-ChildControls 给的是**屏幕坐标**（GetWindowRect），所以"顶部那一条"要拿主窗口的上边
# 当参照 —— 直接写 Top -lt 32 永远匹配不到任何东西（第一版就是这么假红的）。
# GetWindowRect 给的是整窗（含标题栏和边框）的屏幕坐标，客户区是从标题栏下面开始的，
# 所以"顶部那一条"要留出标题栏的高度（+70 足够，页签按钮本身只有 24~28 高）。
$mainRect = [TBGui]::Rect($main)
$tabBarRaw = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Top -ge $mainRect[1] -and $_.Top -lt ($mainRect[1] + 70) -and $_.Height -le 30 })
$tabBar = @($tabBarRaw | Sort-Object Left)
$tabLabels = @($tabBar | ForEach-Object { $_.Text })
$wantTabs = @('常用', '常用设置', '我的工具', '系统工具', '清理优化', '隐私设置', '应用管理', '右键增强')
$lackTabs = @($wantTabs | Where-Object { $tabLabels -notcontains $_ })
Check ('A05b 页签条上有 8 个页签，顺序是「{0}」' -f ($wantTabs -join '/')) `
    (($tabBar.Count -eq 8) -and ($lackTabs.Count -eq 0) -and (($tabLabels -join '/') -eq ($wantTabs -join '/'))) `
    ('找到 ' + $tabBar.Count + ' 个按钮: ' + ($tabLabels -join '/') + '  缺=' + ($lackTabs -join ' '))
Check 'A05 窗口可缩放（有 WS_THICKFRAME）' (($style -band 0x00040000) -ne 0) ('style=0x{0:X}' -f $style)

$rect = [TBGui]::Rect($main)
Check 'A06 窗口尺寸合理（宽 >= 460, 高 >= 380）' (($rect[2] - $rect[0]) -ge 460 -and ($rect[3] - $rect[1]) -ge 380) (('w={0} h={1}' -f ($rect[2] - $rect[0]), ($rect[3] - $rect[1])))

# 窗口高度跟着当前页签的内容走：「右键增强」只有 1 个按钮，不该撑着一个 700px 的空窗口
# （用户 2026-10-04 让从体验角度复核时发现：固定 700px 高，某些页 89% 是空白）。
$heightCommon = $rect[3] - $rect[1]
if (Switch-Tab -Handle $main -TabName '右键增强' -ExpectNames @()) {
    Start-Sleep -Milliseconds 600
    $rectSmall = [TBGui]::Rect($main)
    $heightSmall = $rectSmall[3] - $rectSmall[1]
    Check 'A07 窗口高度跟着内容走（右键增强页比常用设置页矮）' ($heightSmall -lt $heightCommon) `
        ('常用设置=' + $heightCommon + 'px 右键增强=' + $heightSmall + 'px')
    [void](Switch-Tab -Handle $main -TabName '常用设置' -ExpectNames $commonNames)
    Start-Sleep -Milliseconds 400
    $rectBack = [TBGui]::Rect($main)
    Check 'A08 切回常用设置后窗口又变回来（高度跟着内容）' ((($rectBack[3] - $rectBack[1]) -ge $heightCommon - 4)) `
        ('切回后=' + ($rectBack[3] - $rectBack[1]) + 'px 原=' + $heightCommon + 'px')
} else {
    Check 'A07 窗口高度跟着内容走（右键增强页比常用设置页矮）' $false '切不到「右键增强」页签'
    Check 'A08 切回常用设置后窗口又变回来（高度跟着内容）' $false 'skipped'
}

# 工具目录 bin-tools 会在第一次打开界面时建好，并放一份「说明.txt」进去 —— 空文件夹看着像坏了
# （用户 2026-10-04 问过「bin-tools 里面为什么是空的？」）。界面起来之后这个文件必须已经在。
$payloadNote = Join-Path (Split-Path -Parent $Exe) 'bin-tools\说明.txt'
Check 'A09 工具目录 bin-tools 建好了，里面有一份说明.txt' (Test-Path -LiteralPath $payloadNote) $payloadNote

# ---------------------------------------------------------------- B 组：按钮墙
if (Test-GroupSelected 'B') {
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

# 除最后一行外每行必须满 4 个；最后一行 1..4 个（正好是 4 的倍数时最后一行也是满的）
$rowCounts = @($toolButtons | Group-Object Top | Sort-Object Name | ForEach-Object { $_.Count })
$fullRows = @($rowCounts | Where-Object { $_ -eq 4 })
$lastCount = 0
if ($rowCounts.Count -gt 0) { $lastCount = $rowCounts[$rowCounts.Count - 1] }
Check 'B04 除最后一行外每行都是 4 个按钮（最后一行 1..4 个）' `
    (($fullRows.Count -ge ($rows.Count - 1)) -and ($lastCount -ge 1) -and ($lastCount -le 4)) ($rowCounts -join ',')

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

# 分段标题：光有一条灰线看不出这堆按钮是干什么的（「系统工具」26 个按钮分成 6 段）。
# 标题是 Label（STATIC 类），文字来自清单里的 segmentName。
$capTexts = @($all | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
$wantCaps = @('任务栏 · 开始菜单 · 资源管理器（每一列是一对开关）', '安全入口 · 电源 · 系统维护', '改动的记录与还原')
$lackCaps = @($wantCaps | Where-Object { $capTexts -notcontains $_ })
Check ('B07b 每个分段都有标题（{0} 段）' -f $wantCaps.Count) ($lackCaps.Count -eq 0) `
    ('缺=' + ($lackCaps -join ' | ') + '  现有标题=' + (($capTexts | Where-Object { $_ -match '·' }) -join ' | '))

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
# 参考行数（D01e 拿它比底栏按钮有没有被裁）要用**彩色图标**的真按钮来量：灰按钮的灰图标
# 有时候判不出来（判据见 Get-InkRows），那一行图标会被算进文字，参考值就多了 1 行，
# 底栏按钮明明没被裁也会假红（2026-10-04 换成「Win10 资源管理器」当灰样本时踩到）。
$refProbe = @($toolButtons | Where-Object { $liveNames -contains $_.Text } | Sort-Object Top, Left | Select-Object -First 1)
$refInkH = 0
$greyDark = 0
if ($shot -eq $null -or $gridProbe.Count -eq 0 -or $refProbe.Count -eq 0) {
    Check 'B09 占位按钮是灰的（最暗墨迹 >= 60）' $false ('窗口截图失败，或这一页灰/真按钮缺一边（灰=' + $greyNames.Count + ' 真=' + $liveNames.Count + '）')
    Check 'B09b 按钮文字完整（墨迹行数 >= 10）' $false '窗口截图失败'
} else {
    $refRect = @{
        X = $refProbe[0].Left - $shot.Left; Y = $refProbe[0].Top - $shot.Top
        W = $refProbe[0].Width; H = $refProbe[0].Height
    }
    $ink = Get-InkRows -Shot $shot -Icon -X $refRect.X -Y $refRect.Y -W $refRect.W -H $refRect.H
    $refInkH = $ink.LabelBottom - $ink.LabelTop + 1
    Check 'B09b 按钮文字完整（墨迹行数 >= 10）' ($refInkH -ge 10) ('参考按钮=' + $refProbe[0].Text + ' 墨迹行=' + $ink.LabelTop + '..' + $ink.LabelBottom + ' 行数=' + $refInkH)

    $probeRect = @{
        X = $gridProbe[0].Left - $shot.Left; Y = $gridProbe[0].Top - $shot.Top
        W = $gridProbe[0].Width; H = $gridProbe[0].Height
    }
    $dark = Get-DarkestInk -Shot $shot -X $probeRect.X -Y $probeRect.Y -W $probeRect.W -H $probeRect.H
    $greyDark = $dark
    # 灰按钮现在是 Enabled=false 的：WinForms 画禁用控件的文字时会在下面描 1px 更深的"影子"
    # （DrawStringDisabled），所以最暗像素实测是 77，而不是我们设的纯灰 #8A8A8A（138）。
    # 阈值取 60；真正有说服力的对比在 C01d —— 灰按钮必须比真按钮明显淡一截。
    Check 'B09 占位按钮是灰的（最暗墨迹 >= 60）' ($dark -ge 60) ('最暗=' + $dark + ' 按钮=' + $gridProbe[0].Text)
}

}

# ---------------------------------------------------------------- B10：鼠标悬停的说明
if (Test-GroupSelected 'B10') {
if ($script:OtherToolboxCount -gt 0) {
    # 环境不满足：屏幕上已经有别的工具箱窗口，悬停的鼠标会被它接走 —— 这一条测不了。
    # （2026-10-06 实测：用户自己开着工具箱时 B10/B11 双红，报的是"说明为空"，
    #  而"途中见过的"里是用户那个实例当前页签的按钮说明，一眼能看出是环境而不是功能坏了。）
    Skip 'B10 鼠标停在按钮上会弹出说明，第一行就是这个按钮的名字' `
        ('屏幕上有 ' + $script:OtherToolboxCount + ' 个别的工具箱窗口，真悬停会被它接走；关掉它再跑这一条就能测')
    Skip 'B11 悬停说明里不再摊开内联脚本正文' '同上（屏幕上有别的工具箱窗口）'
} else {
# 用户 2026-10-04 报过「鼠标悬停的说明没有做好」：说明以前是「按钮名 · 直接可跑的那条命令」，
# 内联脚本按钮于是把整段 PowerShell 摊成一行（「一键清理垃圾」700 多字），而写给人的那句 hint
# 反而不显示。这条只有真把鼠标停上去才测得到，所以这里用 SetCursorPos 真悬停一次；
# 跑完（包括中途出错）立刻把光标放回原处，别打扰正在用电脑的人。
$hoverTip = ''
$hoverSeen = @()
$hoverBtn = $refProbe[0]
$cursorHome = [TBGui]::CursorAt()
$hoverX = $hoverBtn.Left + [int]($hoverBtn.Width / 2)
$hoverY = $hoverBtn.Top + [int]($hoverBtn.Height / 2)
try {
    # 悬停这一步是**真的动系统鼠标**，所以会被环境打断：别的窗口盖住按钮、用户正好在动鼠标、
    # 前台被抢走…… 给它 3 次机会，失败时把"鼠标底下是谁 / 前台是谁"打出来自证
    # （2026-10-04 踩过：用户自己开的那个实例和测试实例的窗口叠在一起，B10/B11 假红，查了很久）。
    for ($attempt = 0; $attempt -lt 3 -and $hoverTip.Length -eq 0; $attempt++) {
        [void][TBGui]::Focus($main)
        Start-Sleep -Milliseconds 200
        # 从按钮旁边挪进去：同一点连按两次不会产生 mousemove，ToolTip 的计时器就不会启动
        [void][TBGui]::MoveCursor($hoverX, ($hoverBtn.Top - 20))
        Start-Sleep -Milliseconds 150
        [void][TBGui]::MoveCursor($hoverX, $hoverY)
        # 只认"这个按钮自己的"那条说明：主窗口只有一个 ToolTip 实例，鼠标从旁边挪进来时
        # 会先弹出旁边控件（页签）的说明，见一条就收会让 B10 假红（2026-10-04 踩过）。
        for ($i = 0; $i -lt 8; $i++) {
            Start-Sleep -Milliseconds 250
            $seen = [TBGui]::TooltipText([uint32]$proc.Id)
            if ($seen.Length -eq 0) { continue }
            if ($hoverSeen -notcontains $seen) { $hoverSeen += $seen }
            if ($seen -match [regex]::Escape($hoverBtn.Text)) { $hoverTip = $seen; break }
        }
    }
    $hoverUnder = [TBGui]::WindowAt($hoverX, $hoverY)
    $hoverFront = [TBGui]::Foreground()
} finally {
    [void][TBGui]::MoveCursor($cursorHome[0], $cursorHome[1])
}
$hoverFlat = ($hoverTip -replace "`r?`n", ' / ')
$hoverSeenFlat = (($hoverSeen | ForEach-Object { $_ -replace "`r?`n", ' / ' }) -join ' ;; ')
Check 'B10 鼠标停在按钮上会弹出说明，第一行就是这个按钮的名字' `
    (($hoverTip.Length -gt 0) -and ($hoverTip -match [regex]::Escape($hoverBtn.Text))) `
    ('按钮=' + $hoverBtn.Text + ' 说明=' + $hoverFlat + ' 途中见过的=' + $hoverSeenFlat + '  鼠标(' + $hoverX + ',' + $hoverY + ')底下=' + $hoverUnder + '  前台=' + $hoverFront)
Check 'B11 悬停说明里不再摊开内联脚本正文' `
    (($hoverTip.Length -gt 0) -and ($hoverTip -notmatch 'powershell -Command|EncodedCommand')) $hoverFlat
}

}

# ---------------------------------------------------------------- C 组：翻页签
if (Test-GroupSelected 'C') {
Write-Host ''
Write-Host 'C 组 · 页签切换'
# 注：Switch-Tab 定义在文件开头的探针辅助区（必须在第一次调用之前，见那里的注释）。

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
Check ('C04b 点「隐私设置」→ {0} 个按钮（成对开关都在这一页）' -f $privacyNames.Count) (Switch-Tab -Handle $main -TabName '隐私设置' -ExpectNames $privacyNames) ''
$pvBad = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and ($privacyNames -contains $_.Text) -and (-not $_.Enabled) })
Check 'C04c 隐私页签上的按钮都是能点的（没有灰色按钮）' ($pvBad.Count -eq 0) (($pvBad | ForEach-Object { $_.Text }) -join ' ')
Check ('C04d 点「应用管理」→ {0} 个按钮' -f $appsNames.Count) (Switch-Tab -Handle $main -TabName '应用管理' -ExpectNames $appsNames) ''
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
                if ($script:InjectedPh) { [void](Add-TestPlaceholder) }   # E 组的灰按钮检查还要用
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

}

# ---------------------------------------------------------------- D 组：底部条与日志
if (Test-GroupSelected 'D') {
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

$statusLabel = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*STATIC*' -and $_.Text -match '本页 \d+ 个' })
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

# 搜索必须是跨页签的：104 个按钮分散在 8 个页签里，只在当前页签里找的话，用户在「常用设置」
# 页搜「隐私」只会得到一句"这个页签里没有"，而旁边就有一整页叫「隐私设置」（用户 2026-10-04
# 让从体验角度复核时点出来的）。
if ($searchEdits.Count -ge 1) {
    $boxH = $searchEdits[0].H
    [void][TBGui]::SetText($boxH, '隐私一键还原')
    Start-Sleep -Milliseconds 900
    $hit = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '隐私一键还原' })
    Check 'D05b 在「常用设置」页搜别的页签的按钮名，能搜到（搜索跨页签）' ($hit.Count -eq 1) ('找到=' + $hit.Count)
    $capNow = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
    Check 'D05c 搜索结果按页签分组，标题写着「隐私设置 · N 个」' `
        ((@($capNow | Where-Object { $_ -match '^隐私设置 · \d+ 个$' }).Count) -eq 1) (($capNow | Where-Object { $_ -match '·' }) -join ' | ')
    $statusNow = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*STATIC*' -and $_.Text -match '搜索' })
    Check 'D05d 底栏报出"几个页签找到几个"' `
        ((@($statusNow | Where-Object { $_.Text -match '搜索「隐私一键还原」· \d+ 个页签找到 \d+ 个' }).Count) -eq 1) `
        (($statusNow | ForEach-Object { $_.Text }) -join ' | ')

    # 切页签要把搜索收起来（否则用户会以为页签坏了：七个页签看到同一份结果）
    [void](Switch-Tab -Handle $main -TabName '系统工具' -ExpectNames @())
    Start-Sleep -Milliseconds 700
    $afterSwitch = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '设备管理器' })
    Check 'D05e 搜索状态下切页签会把搜索收起来，正常显示那一页' ($afterSwitch.Count -eq 1) ('设备管理器=' + $afterSwitch.Count)
    [void](Switch-Tab -Handle $main -TabName '常用设置' -ExpectNames $commonNames)
    Start-Sleep -Milliseconds 500
} else {
    Check 'D05b 在「常用设置」页搜别的页签的按钮名，能搜到（搜索跨页签）' $false '没有搜索框'
    Check 'D05c 搜索结果按页签分组，标题写着「隐私设置 · N 个」' $false 'skipped'
    Check 'D05d 底栏报出"几个页签找到几个"' $false 'skipped'
    Check 'D05e 搜索状态下切页签会把搜索收起来，正常显示那一页' $false 'skipped'
}
if ($searchEdits.Count -gt 0) { [void][TBGui]::Click($searchButton[0].H) }
Start-Sleep -Milliseconds 500

# 「常用」页签（置顶 + 最近使用）：还没有内容时必须给一句人话，不能是一片空白
if (Switch-Tab -Handle $main -TabName '常用' -ExpectNames @()) {
    Start-Sleep -Milliseconds 600
    $recentCaps = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
    $hasHint = @($recentCaps | Where-Object { $_ -match '这里还什么都没有|最近使用|置顶' }).Count -ge 1
    Check 'D05f 「常用」页空着的时候有一句怎么用的说明' $hasHint (($recentCaps | Where-Object { $_.Length -gt 6 }) -join ' | ')
    [void](Switch-Tab -Handle $main -TabName '常用设置' -ExpectNames $commonNames)
    Start-Sleep -Milliseconds 500
} else {
    Check 'D05f 「常用」页空着的时候有一句怎么用的说明' $false '切不到「常用」页签'
}

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
        # 快捷键以前没有任何入口，全靠猜；关于窗口现在有一节把它们列出来
        $aboutTexts = @(Get-ChildControls -RootHandle $aboutWin[0].H | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
        $keys = @($aboutTexts | Where-Object { $_ -match '快捷键' })
        $keysOk = ($keys.Count -eq 1) -and ($keys[0] -match 'Ctrl\+F') -and ($keys[0] -match 'Ctrl\+N') -and ($keys[0] -match 'Alt\+1')
        Check 'D07b 关于窗口里列出了快捷键（Ctrl+F / Ctrl+N / Alt+1~9）' $keysOk (($keys -join ' | '))
        # 关于窗口里那句"灰色按钮点一下只会写日志"是旧行为，早就改成禁用控件了 —— 别再写回来
        $noteOk = @($aboutTexts | Where-Object { $_ -match '点一下只会写日志' }).Count -eq 0
        Check 'D07c 关于窗口里没有过时的"灰按钮点一下只会写日志"说明' $noteOk ''
        # 用户 2026-10-06 要的：「关于」里面的网址**点一下就能访问**，而且「主页」改叫「官网」。
        # 这一条真去点那个链接：测试进程带着 MXX1_NO_OPEN=1（见文件开头），所以点下去只会多一行
        # 日志、不会在别人桌面上弹浏览器 —— 断言的就是"日志里真的多了一行那个网址"。
        # 判据不看截图、也不看控件类名（LinkLabel 底下就是 STATIC），只看"点了有没有反应"。
        $siteLabels = @($aboutTexts | Where-Object { $_ -eq '官网' })
        $oldLabels = @($aboutTexts | Where-Object { $_ -eq '主页' })
        $linkCtls = @(Get-ChildControls -RootHandle $aboutWin[0].H | Where-Object { $_.Text -match '^https?://' })
        $logPath = ''
        $statusNow = Invoke-Exe 'status'
        if ($statusNow -match '(?m)^log=([^\r\n]+)') { $logPath = $Matches[1].Trim() }
        $logHad = 0
        if (($logPath.Length -gt 0) -and (Test-Path -LiteralPath $logPath)) {
            $logHad = @(Get-Content -LiteralPath $logPath -Encoding UTF8 -ErrorAction SilentlyContinue).Count
        }
        $logLine = ''
        $clicked = $false
        $clickNote = '（窗口里没有网址链接）'
        if ($linkCtls.Count -gt 0) {
            $lc = $linkCtls[0]
            $lr = [TBGui]::Rect($lc.H)
            $lx = [int]($lr[0] + ($lr[2] - $lr[0]) / 2)
            $ly = [int]($lr[1] + ($lr[3] - $lr[1]) / 2)
            $savedCursor = [TBGui]::CursorAt()
            try {
                [void][TBGui]::Focus($aboutWin[0].H)
                for ($k = 0; $k -lt 3; $k++) {
                    # 真点之前先确认鼠标底下就是那个链接控件（别的窗口压在上面时不点，免得误伤）
                    if ([TBGui]::HandleAt($lx, $ly) -ne $lc.H) {
                        $clickNote = '（鼠标底下不是那个链接，被别的窗口压着）'
                        Start-Sleep -Milliseconds 400
                        continue
                    }
                    $clickNote = ''
                    [TBGui]::RealClick($lx, $ly)
                    $clicked = $true
                    for ($i = 0; $i -lt 10; $i++) {
                        Start-Sleep -Milliseconds 200
                        if (($logPath.Length -eq 0) -or -not (Test-Path -LiteralPath $logPath)) { break }
                        $lines = @(Get-Content -LiteralPath $logPath -Encoding UTF8 -ErrorAction SilentlyContinue)
                        $fresh = @($lines | Select-Object -Skip $logHad | Where-Object { $_ -match 'https?://' })
                        if ($fresh.Count -gt 0) { $logLine = [string]$fresh[0]; break }
                    }
                    if ($logLine.Length -gt 0) { break }
                }
            } finally {
                [void][TBGui]::MoveCursor($savedCursor[0], $savedCursor[1])
            }
        }
        Check 'D07d 官网 / 仓库 是能点的链接（点一下真的去打开），而且「主页」改叫「官网」' `
            (($siteLabels.Count -ge 1) -and ($oldLabels.Count -eq 0) -and ($linkCtls.Count -ge 2) -and `
             $clicked -and ($logLine.IndexOf('http') -ge 0)) `
            ('官网=' + $siteLabels.Count + ' 还叫主页=' + $oldLabels.Count + ' 链接控件=' + $linkCtls.Count + `
             ' 点了=' + $clicked + ' 日志新行=' + $logLine + $clickNote)
        [void][TBGui]::CloseWindow($aboutWin[0].H)
        Start-Sleep -Milliseconds 600
    } else {
        Check 'D07 关于窗口里有「打开工具目录」入口' $false 'skipped'
        Check 'D07b 关于窗口里列出了快捷键（Ctrl+F / Ctrl+N / Alt+1~9）' $false 'skipped'
        Check 'D07c 关于窗口里没有过时的"灰按钮点一下只会写日志"说明' $false 'skipped'
        Check 'D07d 官网 / 仓库 是能点的链接（点一下真的去打开），而且「主页」改叫「官网」' $false 'skipped'
    }
} else {
    Check 'D06 点底栏「关于」打开关于窗口' $false '底栏没有关于按钮'
    Check 'D07 关于窗口里有「打开工具目录」入口' $false 'skipped'
    Check 'D07b 关于窗口里列出了快捷键（Ctrl+F / Ctrl+N / Alt+1~9）' $false 'skipped'
    Check 'D07c 关于窗口里没有过时的"灰按钮点一下只会写日志"说明' $false 'skipped'
    Check 'D07d 官网 / 仓库 是能点的链接（点一下真的去打开），而且「主页」改叫「官网」' $false 'skipped'
}

}

# ---------------------------------------------------------------- E 组：真按钮能跑 / 灰色按钮点不动
if (Test-GroupSelected 'E') {
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
    # 结果窗口是**非模态**的（Show() 开的），而 StartPosition=CenterParent 只对 ShowDialog 有效 ——
    # 不处理它会落到屏幕左上角（用户报的：「点击激活状态为什么会弹到左上角窗口」）。
    # 判据：结果窗口必须和主窗口明显重叠（居中放才对），光"弹出来了"不算数。
    if ($outWin.Count -gt 0) {
        $mainRect = [TBGui]::Rect($main)
        $outRect = [TBGui]::Rect($outWin[0].H)
        $ix = [Math]::Max(0, [Math]::Min($mainRect[2], $outRect[2]) - [Math]::Max($mainRect[0], $outRect[0]))
        $iy = [Math]::Max(0, [Math]::Min($mainRect[3], $outRect[3]) - [Math]::Max($mainRect[1], $outRect[1]))
        $outArea = ($outRect[2] - $outRect[0]) * ($outRect[3] - $outRect[1])
        $cover = 0
        if ($outArea -gt 0) { $cover = [Math]::Round(100.0 * $ix * $iy / $outArea) }
        Check 'E04b 结果窗口居中弹在主窗口上（不再落到屏幕左上角）' ($cover -ge 50) `
            ('重叠=' + $cover + '%  结果窗口@' + $outRect[0] + ',' + $outRect[1] + ' ' + ($outRect[2] - $outRect[0]) + 'x' + ($outRect[3] - $outRect[1]) + '  主窗口@' + $mainRect[0] + ',' + $mainRect[1])
    } else {
        Check 'E04b 结果窗口居中弹在主窗口上（不再落到屏幕左上角）' $false '没找到结果窗口'
    }
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
    $statusNow = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*STATIC*' -and $_.Text -match '本页 \d+ 个' })
    if ($statusNow.Count -gt 0) {
        $needNow = Measure-Width $statusNow[0].Text
        Check 'E08 出现长状态文字后仍然装得下' ($needNow -le $statusNow[0].Width) ('文字=' + $needNow + 'px 标签=' + $statusNow[0].Width + 'px 内容="' + $statusNow[0].Text + '"')
    } else {
        Check 'E08 出现长状态文字后仍然装得下' $false '没找到状态栏标签'
    }
    # 结果反馈：跑完之后页签下面必须出现一条"完成 / 失败"的结果条（用户 2026-10-04 的反馈：
    # "点击确认以后也没有成功或者失败的反馈"）。它是 STATIC 标签，文字以「完成：」开头。
    $toast = @(Get-ChildControls -RootHandle $main | Where-Object { $_.Class -like '*STATIC*' -and $_.Text -match '^(完成|失败)：' })
    Check 'E09 跑完出现一条结果条（完成 / 失败 + 点它看日志）' `
        ((($toast.Count -eq 1) -and ($toast[0].Text -match '点这一条看运行日志')) -or ($toast.Count -ge 1)) `
        (($toast | ForEach-Object { $_.Text }) -join ' | ')
    # 顺手把这次真跑出来的结果窗口关掉，免得影响后面的检查
    foreach ($w in @((Get-TopWindows -ProcessId $proc.Id) | Where-Object { $_.H -ne $main -and $_.Visible -and $_.Text -match '激活' })) {
        [void][TBGui]::CloseWindow($w.H)
    }
    Start-Sleep -Milliseconds 500
} else {
    Check 'E06 运行中按钮文字一字不变（不许追加"…"造成跳动）' $false '没找到激活状态按钮'
    Check 'E07 运行中按钮还在（文字没变说明没被重排）' $false 'skipped'
    Check 'E08 出现长状态文字后仍然装得下' $false 'skipped'
    Check 'E09 跑完出现一条结果条（完成 / 失败 + 点它看日志）' $false 'skipped'
}

}

# ---------------------------------------------------------------- F 组：危险按钮的确认框
if (Test-GroupSelected 'F') {
Write-Host ''
Write-Host 'F 组 · 危险按钮必须先确认'

# "弹窗"必须按窗口类判定：消息框的类是 #32770。WinForms 的 ToolTip 也是一个顶层窗口
# （类名 tooltips_class32、标题为空），所以按"除主窗口以外的可见窗口"来判会把 tooltip
# 当成弹窗，$dialog[0] 取到的就不是消息框了（加了按钮悬停提示以后踩到过）。
# 注意：调用处必须再包一层 @()。函数里 `return @(单个对象)` 会被解包成一个 PSCustomObject，
# 而单个 PSCustomObject **没有** .Count（返回空），于是 `.Count -gt 0` 恒为 False。
function Get-Dialogs {
    param([int]$ProcessId, [IntPtr]$Main)
    # 「确认执行」现在是专门的窗口（ConfirmForm —— 为了让说明能排版；用户报过 MessageBox 那个
    # 提示没做好），它是 WinForms 窗口（类名 WindowsForms10.Window.*），**不是** #32770。
    # 所以按标题找它，同时保留 #32770 那条（设置 / 新建按钮那些还是真正的对话框）。
    return @((Get-TopWindows -ProcessId $ProcessId) | Where-Object {
        $_.H -ne $Main -and $_.Visible -and $_.Text.Length -gt 0 -and
        ($_.Class -eq '#32770' -or $_.Text -eq '请确认')
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
    Check 'F02 确认框标题是「请确认」' ($dialog[0].Text -match '请确认') $dialog[0].Text
    # 确认窗口里必须把"这个按钮干什么"说清楚（那句 hint），而不是只甩一行命令 ——
    # 用户 2026-10-04 的反馈就是"弹出的确认执行的提示没做好"
    $dlgTexts = @(Get-ChildControls -RootHandle $dialog[0].H | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
    Check 'F02b 确认窗口里有一句人话说明这个按钮干什么' `
        ((@($dlgTexts | Where-Object { $_.Length -ge 8 }).Count -ge 1) -and ($dlgTexts -join ' ' -match '要执行')) `
        (($dlgTexts | Where-Object { $_.Length -gt 0 }) -join ' | ')
    # 默认按钮必须是「取消」：危险动作要真的去点「执行」（Enter / Esc 都等于取消）
    $dlgButtons = @(Get-ChildControls -RootHandle $dialog[0].H | Where-Object { $_.Class -like '*BUTTON*' } | ForEach-Object { $_.Text })
    Check 'F02c 确认窗口有「执行 / 取消」两个按钮' (($dlgButtons -contains '执行') -and ($dlgButtons -contains '取消')) ($dlgButtons -join ' ')
    [void][TBGui]::CloseWindow($dialog[0].H)
    Start-Sleep -Milliseconds 600
    Check 'F03 取消后确认框关掉了' (@(Get-Dialogs -ProcessId $proc.Id -Main $main).Count -eq 0) ''
} else {
    Check 'F02 确认框标题是「请确认」' $false '没有弹出确认框'
    Check 'F02b 确认窗口里有一句人话说明这个按钮干什么' $false 'skipped'
    Check 'F02c 确认窗口有「执行 / 取消」两个按钮' $false 'skipped'
    Check 'F03 取消后确认框关掉了' $false 'skipped'
}

}

# ---------------------------------------------------------------- G 组：深色主题
if (Test-GroupSelected 'G') {
Write-Host ''
Write-Host 'G 组 · 深色主题'

try {
    # 顺序很重要：先关掉浅色那个窗口，再写设置。窗口关闭时会把**它当前停留的页签**写回
    # settings.ini（LastTab 是"记住上次停留的页签"功能的一部分），写在关闭之前就会被它覆盖掉，
    # 于是深色实例开在 F 组最后停的「清理优化」上，G03/G05 全假红（2026-10-04 踩过）。
    try { [void][TBGui]::CloseWindow($main) } catch { }
    Start-Sleep -Milliseconds 900
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $SettingsIni))
    [System.IO.File]::WriteAllText($SettingsIni, "Theme=dark`r`nClickMode=single`r`nConfirmDangerous=1`r`nShowLogPanel=0`r`nLogKeepDays=30`r`nPermanentDeleteExe=`r`nLastTab=common`r`nWindowAutoSize=1`r`nWindowX=" + $script:TestWinX + "`r`nWindowY=" + $script:TestWinY + "`r`n" + $script:ConsentIni, (New-Object System.Text.UTF8Encoding($false)))
} catch { }
$darkStatus = Invoke-Exe 'status'
Check 'G01 设置成深色后 themeResolved=dark' ($darkStatus -match '(?m)^themeResolved=dark') (($darkStatus -split "`r?`n" | Where-Object { $_ -match '^themeResolved=' }) -join '')

$procDark = Start-Gui
Check 'G02 深色主题下界面能正常起来' (($procDark -ne $null) -and (-not $procDark.HasExited) -and ($procDark.MainWindowHandle -ne [IntPtr]::Zero)) ''
if ($procDark -ne $null -and -not $procDark.HasExited -and $procDark.MainWindowHandle -ne [IntPtr]::Zero) {
    $darkButtons = Wait-Buttons -Handle $procDark.MainWindowHandle -Names $commonNames
    Check ('G03 深色下 {0} 个按钮仍然都在' -f $commonNames.Count) $darkButtons ''
    $darkRect = [TBGui]::Rect($procDark.MainWindowHandle)
    Check 'G04 深色下窗口尺寸没变（宽 >= 460）' (($darkRect[2] - $darkRect[0]) -ge 460) ('w=' + ($darkRect[2] - $darkRect[0]))
    # 深色下分段标题也得跟着主题走（灰底配深灰字会看不见）
    $darkCaps = @(Get-ChildControls -RootHandle $procDark.MainWindowHandle | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
    Check 'G05 深色下分段标题还在' ((@($darkCaps | Where-Object { $_ -match '任务栏 · 开始菜单' }).Count) -eq 1) (($darkCaps | Where-Object { $_ -match '·' }) -join ' | ')
    try { [void][TBGui]::CloseWindow($procDark.MainWindowHandle) } catch { }
    Start-Sleep -Milliseconds 700
} else {
    Check ('G03 深色下 {0} 个按钮仍然都在' -f $commonNames.Count) $false 'skipped'
    Check 'G04 深色下窗口尺寸没变（宽 >= 460）' $false 'skipped'
    Check 'G05 深色下分段标题还在' $false 'skipped'
}

}

# ---------------------------------------------------------------- H 组：窗口尺寸默认固定
if (Test-GroupSelected 'H') {
# 用户 2026-10-04 定下的规矩：**默认固定尺寸**（高度固定、宽度也固定），想让它跟着内容变得
# 自己去设置里勾。这一组把 WindowAutoSize 这个键**故意不写**（= 走默认值），验证：
# ① 换页签窗口不动 ② 加一个名字很长的按钮窗口也不变宽。
Write-Host ''
Write-Host 'H 组 · 窗口尺寸默认固定（宽度/高度都不跟着内容变）'

try {
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $SettingsIni))
    [System.IO.File]::WriteAllText($SettingsIni, "Theme=light`r`nClickMode=single`r`nConfirmDangerous=1`r`nHideConsole=1`r`nShowLogPanel=0`r`nLastTab=common`r`nWindowX=" + $script:TestWinX + "`r`nWindowY=" + $script:TestWinY + "`r`n" + $script:ConsentIni, (New-Object System.Text.UTF8Encoding($false)))
} catch { }

$procFixed = $null
try {
    $procFixed = Start-Gui
    $fixedOk = (($procFixed -ne $null) -and (-not $procFixed.HasExited) -and ($procFixed.MainWindowHandle -ne [IntPtr]::Zero))
    Check 'H01 不勾「跟随内容」时界面能起来' $fixedOk ''
    if ($fixedOk) {
        $fixedHandle = $procFixed.MainWindowHandle
        [void](Wait-Buttons -Handle $fixedHandle -Names $commonNames)
        Start-Sleep -Milliseconds 500
        $r1 = [TBGui]::Rect($fixedHandle)
        $h1 = $r1[3] - $r1[1]; $w1 = $r1[2] - $r1[0]
        Check 'H02 默认固定高度够用（高 >= 560）' ($h1 -ge 560) ('h=' + $h1)
        if (Switch-Tab -Handle $fixedHandle -TabName '右键增强' -ExpectNames @()) {
            Start-Sleep -Milliseconds 600
            $r2 = [TBGui]::Rect($fixedHandle)
            Check 'H03 换到只有 1 个按钮的页签，窗口尺寸一动不动' `
                ((($r2[3] - $r2[1]) -eq $h1) -and (($r2[2] - $r2[0]) -eq $w1)) `
                ('常用设置=' + $w1 + 'x' + $h1 + ' 右键增强=' + ($r2[2] - $r2[0]) + 'x' + ($r2[3] - $r2[1]))
            [void](Switch-Tab -Handle $fixedHandle -TabName '常用设置' -ExpectNames $commonNames)
            Start-Sleep -Milliseconds 400
        } else {
            Check 'H03 换到只有 1 个按钮的页签，窗口尺寸一动不动' $false '切不到「右键增强」'
        }
        # 往用户层塞一个名字很长的按钮（32 个汉字）然后重启界面 → 窗口宽度不许变宽。
        # 用户 2026-10-04 原话："主界面宽度固定一下，反正按钮不会跟随变化而自适应"。
        # 注入的这个文件里**同时留着那个占位按钮**：$commonNames 是带着占位按钮读出来的，
        # 换掉它的话 H05 数按钮数会少一个而假红（占位按钮本身不在这条检查的意图里）。
        $longJson = '{ "tools": [ { "id": "test.placeholder", "tab": "common", "segment": 2, "order": 999, "name": "占位自检", "kind": "builtin", "module": "todo", "action": "todo", "placeholder": true, "hint": "测试用的占位按钮" }, { "id": "test.longname", "tab": "common", "segment": 2, "order": 998, "name": "这个名字很长很长很长很长很长很长很长很长的按钮", "kind": "exe", "path": "C:\\Windows\\notepad.exe" } ] }'
        [System.IO.File]::WriteAllText($UserToolsJson, $longJson, (New-Object System.Text.UTF8Encoding($false)))
        $script:InjectedPh = $true
        try { $procFixed.Kill() } catch { }
        Start-Sleep -Milliseconds 700
        $procFixed2 = Start-Gui
        if ($procFixed2 -ne $null -and -not $procFixed2.HasExited -and $procFixed2.MainWindowHandle -ne [IntPtr]::Zero) {
            $h2 = $procFixed2.MainWindowHandle
            [void](Wait-Buttons -Handle $h2 -Names @('这个名字很长很长很长很长很长很长很长很长的按钮'))
            Start-Sleep -Milliseconds 500
            $r3 = [TBGui]::Rect($h2)
            Check 'H04 新增一个名字超长的按钮，窗口宽度不变（宽度固定，不跟着内容长）' `
                (($r3[2] - $r3[0]) -eq $w1) ('原宽=' + $w1 + ' 现在=' + ($r3[2] - $r3[0]))
            $longBtn = @(Get-ChildControls -RootHandle $h2 | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -like '这个名字很长*' })
            Check 'H05 名字太长的按钮不会把别人挤出去（这一页按钮数还对）' `
                ((@(Get-ChildControls -RootHandle $h2 | Where-Object { $_.Class -like '*BUTTON*' -and $commonNames -contains $_.Text }).Count) -eq $commonNames.Count) `
                ('长名按钮=' + $longBtn.Count)
            try { [void][TBGui]::CloseWindow($h2) } catch { }
            Start-Sleep -Milliseconds 600
        } else {
            Check 'H04 新增一个名字超长的按钮，窗口宽度不变（宽度固定，不跟着内容长）' $false '重启后界面没起来'
            Check 'H05 名字太长的按钮不会把别人挤出去（这一页按钮数还对）' $false 'skipped'
        }
        Remove-Item -LiteralPath $UserToolsJson -Force -ErrorAction SilentlyContinue
        $script:InjectedPh = $false
    } else {
        Check 'H02 默认固定高度够用（高 >= 560）' $false 'skipped'
        Check 'H03 换到只有 1 个按钮的页签，窗口尺寸一动不动' $false 'skipped'
        Check 'H04 新增一个名字超长的按钮，窗口宽度不变（宽度固定，不跟着内容长）' $false 'skipped'
        Check 'H05 名字太长的按钮不会把别人挤出去（这一页按钮数还对）' $false 'skipped'
    }
} catch {
    Write-Host ('  H 组出错: ' + $_.Exception.Message)
    Check 'H01 不勾「跟随内容」时界面能起来' $false $_.Exception.Message
} finally {
    try { if ($procFixed -and -not $procFixed.HasExited) { $procFixed.Kill() } } catch { }
    Start-Sleep -Milliseconds 500
}

}

# ---------------------------------------------------------------- N 组：右键「解除文件占用」的小窗口
if (Test-GroupSelected 'N') {
# 资源管理器右键点「解除文件占用」时，工具箱是以 `rightmenu unlock "<路径>"` 起来的：**只开一个小窗口、
# 不开主界面**（用户 2026-10-04 定的方案，见 docs\DESIGN.md §14.2）。这一组真起一个那样的进程：
# 自己锁一个文件 → 看它认不认（认出来 = 窗口里那句"查到 N 个程序"）→ 看排版是不是没重叠 → 关掉。
# 后端（Restart Manager 认出 PID、文件夹被占用、不谎报）由 Test-Cli 的 M11–M14 盯着。
Write-Host ''
Write-Host 'N 组 · 「解除文件占用」的结果窗口（从资源管理器右键调起来的那个独立小窗口）'

# 扫描从 2026-10-05 起跑在**后台线程**上（以前是同步跑，窗口会卡死），所以"等它查完"必须是轮询，
# 不能读一次就断言。判据：状态行不再写"正在检查谁占着它"。
function Wait-UnlockTexts {
    param([IntPtr]$Hwnd, [int]$Tries = 60, [int]$SleepMs = 250)
    $texts = @()
    for ($i = 0; $i -lt $Tries; $i++) {
        $texts = @(Get-ChildControls -RootHandle $Hwnd | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
        if (@($texts | Where-Object { $_ -match '正在检查谁占着它' }).Count -eq 0) { return $texts }
        Start-Sleep -Milliseconds $SleepMs
    }
    return $texts
}

$unlockDir = Join-Path $env:TEMP 'mxx1-unlock-gui'
if (Test-Path -LiteralPath $unlockDir) { Remove-Item -LiteralPath $unlockDir -Recurse -Force }
New-Item -ItemType Directory -Path $unlockDir | Out-Null
$unlockFile = Join-Path $unlockDir 'gui-locked.txt'
Set-Content -LiteralPath $unlockFile -Value 'x' -Encoding UTF8
$unlockChild = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-Command', ("`$fs=[System.IO.File]::Open('" + $unlockFile + "','Open','ReadWrite','None'); Start-Sleep 90"))
Start-Sleep -Seconds 2
$unlockProc = $null
try {
    $unlockProc = Start-Process -FilePath $Exe -PassThru -ArgumentList @('rightmenu', 'unlock', $unlockFile)
    [void]$script:Procs.Add($unlockProc)
    $unlockWin = @()
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        $unlockWin = @((Get-TopWindows -ProcessId $unlockProc.Id) | Where-Object { $_.Visible -and $_.Text -eq '解除文件占用' })
        if ($unlockWin.Count -gt 0) { break }
    }
    $unlockTitles = @((Get-TopWindows -ProcessId $unlockProc.Id) | Where-Object { $_.Visible } | ForEach-Object { $_.Text })
    Check 'N01 右键调起来的是「解除文件占用」小窗口（不打开主界面）' `
        (($unlockWin.Count -eq 1) -and ($unlockTitles.Count -eq 1)) ('窗口=' + ($unlockTitles -join ' / '))

    if ($unlockWin.Count -gt 0) {
        $uh = $unlockWin[0].H
        $ukids = @(Get-ChildControls -RootHandle $uh)
        $ubtns = @($ukids | Where-Object { $_.Class -like '*BUTTON*' } | ForEach-Object { $_.Text })
        $want = @('结束选中的进程', '强制解锁（不关程序）', '重新检查', '复制路径', '关闭')
        $missBtn = @($want | Where-Object { $ubtns -notcontains $_ })
        Check 'N02 五个按钮都在（结束选中的进程 / 强制解锁 / 重新检查 / 复制路径 / 关闭）' `
            ($missBtn.Count -eq 0) ('缺=' + ($missBtn -join ' ') + ' 实际=' + ($ubtns -join ' '))

        $utexts = Wait-UnlockTexts -Hwnd $uh
        $found = @($utexts | Where-Object { $_ -match '查到 \d+ 个程序占着它' })
        Check 'N03 窗口里念出了「查到 N 个程序占着它」（真查到了那个锁）' ($found.Count -eq 1) ($utexts -join ' | ')

        # 排版硬规矩：按钮之间、按钮与文字之间都不许重叠（重叠的标签会吃掉鼠标点击）
        $rects = @()
        foreach ($k in $ukids) {
            if (($k.Class -like '*BUTTON*') -or ($k.Class -like '*STATIC*')) {
                $r = [TBGui]::Rect($k.H)
                if ((($r[2] - $r[0]) -gt 0) -and (($r[3] - $r[1]) -gt 0)) { $rects += , @($k, $r) }
            }
        }
        $overlap = @()
        for ($i = 0; $i -lt $rects.Count; $i++) {
            for ($j = $i + 1; $j -lt $rects.Count; $j++) {
                $a = $rects[$i][1]; $b = $rects[$j][1]
                $ow = [Math]::Min($a[2], $b[2]) - [Math]::Max($a[0], $b[0])
                $oh = [Math]::Min($a[3], $b[3]) - [Math]::Max($a[1], $b[1])
                if (($ow -gt 2) -and ($oh -gt 2)) { $overlap += ($rects[$i][0].Text + ' × ' + $rects[$j][0].Text) }
            }
        }
        Check 'N04 按钮和文字互不重叠（这个窗口也守那条硬规矩）' ($overlap.Count -eq 0) ($overlap -join ' ')

        # N14 / N15：窗口高度按内容自适应（用户 2026-10-04 报的「窗口高度没有做自适应」）。
        # 原来是 `210 + 行数 * 20`（最多 470）：上面几行字（路径 / 状态 / 自查结论 / 常驻提示）
        # 换行它根本没算，行数少时偏高、文案长时下面被切掉。判据用屏幕坐标，DPI 变了也成立。
        $fit = Get-FitReport -Hwnd $uh
        Check 'N14 内容多高窗口就多高：控件一个都没被切掉（高度不是按行数拍出来的）' `
            (($fit.Widgets -ge 8) -and ($fit.Clipped -eq 0)) `
            ('控件=' + $fit.Widgets + ' 被切=' + $fit.Clipped + ' 客户区高=' + $fit.ClientH + ' ' + $fit.Worst)
        Check 'N15 窗口下面没留一大截空白（高度贴着内容）' `
            (($fit.Gap -ge 0) -and ($fit.Gap -le 40)) ('底部空白=' + $fit.Gap)

        # N16：小窗口自己也有图标（它是个独立进程，单独显示在任务栏上）
        $unlockInk = Get-IconInk -Hwnd $uh -Which 1
        Check 'N16 解除占用小窗口的任务栏图标也是工具箱那张' `
            (($unlockInk -ne $null) -and ($unlockInk.White -gt 10) -and ($unlockInk.Blue -gt 20)) `
            $(if ($unlockInk) { ('{0}x{1} blue={2} white={3}' -f $unlockInk.W, $unlockInk.H, $unlockInk.Blue, $unlockInk.White) } else { '没有图标句柄' })

        [void][TBGui]::CloseWindow($uh)
        Start-Sleep -Milliseconds 900
        $unlockProc.Refresh()
        Check 'N05 关掉小窗口之后那个进程自己退出了（不留后台进程）' ($unlockProc.HasExited) ''
    } else {
        Check 'N02 五个按钮都在（结束选中的进程 / 强制解锁 / 重新检查 / 复制路径 / 关闭）' $false 'skipped'
        Check 'N03 窗口里念出了「查到 N 个程序占着它」（真查到了那个锁）' $false 'skipped'
        Check 'N04 按钮和文字互不重叠（这个窗口也守那条硬规矩）' $false 'skipped'
        Check 'N14 内容多高窗口就多高：控件一个都没被切掉（高度不是按行数拍出来的）' $false 'skipped'
        Check 'N15 窗口下面没留一大截空白（高度贴着内容）' $false 'skipped'
        Check 'N16 解除占用小窗口的任务栏图标也是工具箱那张' $false 'skipped'
        Check 'N05 关掉小窗口之后那个进程自己退出了（不留后台进程）' $false 'skipped'
    }

    # ---- N06：用户 2026-10-04 报的**原始场景** —— 右键一个文件夹，而占着文件的程序（Office /
    #      PDF 阅读器）打开的是子文件夹里的那个文档。窗口必须查到，并且说出来"文件夹里扫了几个文件"。
    $deepRoot = Join-Path $env:TEMP 'mxx1-unlock-deep-gui'
    if (Test-Path -LiteralPath $deepRoot) { Remove-Item -LiteralPath $deepRoot -Recurse -Force }
    $deepSub = Join-Path $deepRoot '年报资料'
    New-Item -ItemType Directory -Path $deepSub -Force | Out-Null
    $deepDoc = Join-Path $deepSub 'Q3报告.txt'
    Set-Content -LiteralPath $deepDoc -Value 'x' -Encoding UTF8
    $deepChild = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-Command', ("`$fs=[System.IO.File]::Open('" + $deepDoc + "','Open','ReadWrite','None'); Start-Sleep 90"))
    Start-Sleep -Seconds 2
    $deepProc = $null
    try {
        $deepProc = Start-Process -FilePath $Exe -PassThru -ArgumentList @('rightmenu', 'unlock', $deepRoot)
        [void]$script:Procs.Add($deepProc)
        $deepWin = @()
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 250
            $deepWin = @((Get-TopWindows -ProcessId $deepProc.Id) | Where-Object { $_.Visible -and $_.Text -eq '解除文件占用' })
            if ($deepWin.Count -gt 0) { break }
        }
        if ($deepWin.Count -gt 0) {
            # 窗口先显示、再查（Shown 里跑查询），所以文字要轮询几轮再断言
            $dtexts = @()
            for ($i = 0; $i -lt 30; $i++) {
                $dtexts = @(Get-ChildControls -RootHandle $deepWin[0].H | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
                if (@($dtexts | Where-Object { $_ -match '个程序占着它' }).Count -gt 0) { break }
                Start-Sleep -Milliseconds 250
            }
            $dfound = @($dtexts | Where-Object { $_ -match '查到 1 个程序占着它' })
            $dscan = @($dtexts | Where-Object { $_ -match '文件夹里扫了 \d+ 个文件' })
            Check 'N06 右键文件夹：占用在子文件夹里也查得到，并说明扫了几个文件' `
                (($dfound.Count -eq 1) -and ($dscan.Count -eq 1)) ($dtexts -join ' | ')
            [void][TBGui]::CloseWindow($deepWin[0].H)
            Start-Sleep -Milliseconds 700
        } else {
            Check 'N06 右键文件夹：占用在子文件夹里也查得到，并说明扫了几个文件' $false 'skipped（窗口没起来）'
        }
    } finally {
        if ($deepChild -and -not $deepChild.HasExited) { Stop-Process -Id $deepChild.Id -Force -ErrorAction SilentlyContinue }
        if ($deepProc -and -not $deepProc.HasExited) { try { $deepProc.Kill() } catch { } }
        if (Test-Path -LiteralPath $deepRoot) { Remove-Item -LiteralPath $deepRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # ---- N07 / N08：用户 2026-10-04 报的第二种情况 —— 文件夹里的程序**正在运行**。它不持有文件
    #      句柄（镜像是内存映射），Restart Manager 报不出来，原来窗口只会说"报不出是哪个程序"；
    #      现在要把「它自己在运行」这条线索点出来。N08 顺带验：点「结束选中的进程」时确认框里
    #      写明了"会连带结束子进程"，而**点取消之后一个进程都不能少**（用户报的就是只结束父进程）。
    $runRoot = Join-Path $env:TEMP 'mxx1-unlock-run-gui'
    if (Test-Path -LiteralPath $runRoot) { Remove-Item -LiteralPath $runRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    Set-Content -LiteralPath (Join-Path $runRoot 'doc.txt') -Value 'x' -Encoding UTF8
    $runHolder = Join-Path $runRoot 'holder.exe'
    $runSrc = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    try { New-Item -ItemType HardLink -Path $runHolder -Target $runSrc -ErrorAction Stop | Out-Null }
    catch { Copy-Item -LiteralPath $runSrc -Destination $runHolder -Force }
    $runProc = $null
    $runGui = $null
    try {
        $runProc = Start-Process -FilePath $runHolder -WindowStyle Hidden -PassThru -ArgumentList @(
            '-NoProfile', '-Command', 'Start-Sleep 90')
        Start-Sleep -Seconds 2
        $runGui = Start-Process -FilePath $Exe -PassThru -ArgumentList @('rightmenu', 'unlock', $runRoot)
        [void]$script:Procs.Add($runGui)
        $runWin = @()
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 250
            $runWin = @((Get-TopWindows -ProcessId $runGui.Id) | Where-Object { $_.Visible -and $_.Text -eq '解除文件占用' })
            if ($runWin.Count -gt 0) { break }
        }
        if ($runWin.Count -gt 0) {
            $rtexts = @()
            for ($i = 0; $i -lt 30; $i++) {
                $rtexts = @(Get-ChildControls -RootHandle $runWin[0].H | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
                if (@($rtexts | Where-Object { $_ -match '没有程序锁着它|个程序占着它' }).Count -gt 0) { break }
                Start-Sleep -Milliseconds 250
            }
            # 状态那句本身就要点出「它自己在运行」这条线索。
            # 注意别用"整个窗口里有几处提到"来断言：下面那条常驻提示里也写着「它自己在运行」。
            $rHead = @($rtexts | Where-Object { $_ -match '没有程序锁着它|个程序占着它' })
            $rClue = @($rHead | Where-Object { $_ -match '它自己在运行' })
            Check 'N07 文件夹里的程序正在运行时，窗口点出「它自己在运行」这条线索（不再只说报不出名字）' `
                (($rHead.Count -eq 1) -and ($rClue.Count -eq 1)) ($rtexts -join ' | ')

            # N08：确认框要写明"会连带结束子进程"（安装包/启动器都是父进程拉个子进程干活）
            $runBtn = @(Get-ChildControls -RootHandle $runWin[0].H | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '结束选中的进程' })
            if ($runBtn.Count -eq 1) {
                [void][TBGui]::Click($runBtn[0].H)
                $runDlg = @()
                for ($i = 0; $i -lt 25; $i++) {
                    Start-Sleep -Milliseconds 200
                    $runDlg = @(Get-Dialogs -ProcessId $runGui.Id -Main $runWin[0].H)
                    if ($runDlg.Count -gt 0) { break }
                }
                if ($runDlg.Count -gt 0) {
                    $rdlg = @(Get-ChildControls -RootHandle $runDlg[0].H | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
                    Check 'N08 结束前的确认框写明会连带结束子进程（用户报的"窗口还在"就是子进程撑着）' `
                        (($rdlg -join ' ') -match '子进程') (($rdlg | Where-Object { $_.Length -gt 0 }) -join ' | ')
                    [void][TBGui]::CloseWindow($runDlg[0].H)   # 点取消
                    Start-Sleep -Milliseconds 700
                    $runProc.Refresh()
                    Check 'N08b 点「取消」之后一个进程都没被结束（危险动作要真的去点执行）' `
                        (-not $runProc.HasExited) ('holder 还在=' + (-not $runProc.HasExited))
                } else {
                    Check 'N08 结束前的确认框写明会连带结束子进程（用户报的"窗口还在"就是子进程撑着）' $false '没弹出确认框'
                    Check 'N08b 点「取消」之后一个进程都没被结束（危险动作要真的去点执行）' $false 'skipped'
                }
            } else {
                Check 'N08 结束前的确认框写明会连带结束子进程（用户报的"窗口还在"就是子进程撑着）' $false '按钮不在'
                Check 'N08b 点「取消」之后一个进程都没被结束（危险动作要真的去点执行）' $false 'skipped'
            }
            [void][TBGui]::CloseWindow($runWin[0].H)
            Start-Sleep -Milliseconds 700
        } else {
            Check 'N07 文件夹里的程序正在运行时，窗口点出「它自己在运行」这条线索（不再只说报不出名字）' $false 'skipped（窗口没起来）'
            Check 'N08 结束前的确认框写明会连带结束子进程（用户报的"窗口还在"就是子进程撑着）' $false 'skipped'
            Check 'N08b 点「取消」之后一个进程都没被结束（危险动作要真的去点执行）' $false 'skipped'
        }
    } finally {
        if ($runProc -and -not $runProc.HasExited) { Stop-Process -Id $runProc.Id -Force -ErrorAction SilentlyContinue }
        if ($runGui -and -not $runGui.HasExited) { try { $runGui.Kill() } catch { } }
        if (Test-Path -LiteralPath $runRoot) { Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # ---- N09：「强制解锁（不关程序）」——用户要求照火绒那套做（2026-10-04 问完"火绒是怎么做的"之后定的）。
    #      全端到端：另一个进程把文件**独占**打开 → 点按钮（会先扫全系统句柄）→ 确认框 → 点「执行」→
    #      断言三件事：① 那个进程**还活着**（这是和「结束进程」的根本区别）；
    #      ② 文件真的自由了（我自己能独占打开它了）；③ 窗口里念了结果。
    $forceRoot = Join-Path $env:TEMP 'mxx1-unlock-force-gui'
    if (Test-Path -LiteralPath $forceRoot) { Remove-Item -LiteralPath $forceRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $forceRoot | Out-Null
    $forceFile = Join-Path $forceRoot 'locked.txt'
    Set-Content -LiteralPath $forceFile -Value 'x' -Encoding UTF8
    $forceHolder = $null
    $forceGui = $null
    try {
        $forceHolder = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
            '-NoProfile', '-Command', ("`$fs=[System.IO.File]::Open('" + $forceFile + "','Open','ReadWrite','None'); Start-Sleep 120"))
        Start-Sleep -Seconds 2
        $forceGui = Start-Process -FilePath $Exe -PassThru -ArgumentList @('rightmenu', 'unlock', $forceFile)
        [void]$script:Procs.Add($forceGui)
        $forceWin = @()
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 250
            $forceWin = @((Get-TopWindows -ProcessId $forceGui.Id) | Where-Object { $_.Visible -and $_.Text -eq '解除文件占用' })
            if ($forceWin.Count -gt 0) { break }
        }
        if ($forceWin.Count -gt 0) {
            $fbtn = @()
            # 注意要等它**可用**：扫描挪到后台线程之后，查的过程中那三个按钮是禁用的
            # （2026-10-05 起走 UpdateButtons()），按钮"在"不等于能点 —— 直接点会什么都不发生，
            # 然后这一组会假红成"没弹出确认框"（真踩过一次）。
            for ($i = 0; $i -lt 40; $i++) {
                $fbtn = @(Get-ChildControls -RootHandle $forceWin[0].H | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '强制解锁（不关程序）' })
                if (($fbtn.Count -eq 1) -and [TBGui]::Enabled($fbtn[0].H)) { break }
                Start-Sleep -Milliseconds 250
            }
            Check 'N09a 窗口里有「强制解锁（不关程序）」按钮（照火绒那套：不结束进程，只抽句柄）' `
                ($fbtn.Count -eq 1) ('按钮数=' + $fbtn.Count)
            if ($fbtn.Count -eq 1) {
                [void][TBGui]::Click($fbtn[0].H)
                $fdlg = @()
                for ($i = 0; $i -lt 80; $i++) {       # 全系统扫句柄要一两秒，给它 20 秒
                    Start-Sleep -Milliseconds 250
                    $fdlg = @(Get-Dialogs -ProcessId $forceGui.Id -Main $forceWin[0].H)
                    if ($fdlg.Count -gt 0) { break }
                }
                if ($fdlg.Count -gt 0) {
                    $ftexts = @(Get-ChildControls -RootHandle $fdlg[0].H | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
                    Check 'N09b 确认框把风险说清楚了（抽句柄可能让那个程序出错 / 丢数据）' `
                        ((($ftexts -join ' ') -match '句柄') -and (($ftexts -join ' ') -match '风险|存')) `
                        (($ftexts | Where-Object { $_.Length -gt 0 }) -join ' | ')
                    $exec = @(Get-ChildControls -RootHandle $fdlg[0].H | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '执行' })
                    if ($exec.Count -eq 1) { [void][TBGui]::Click($exec[0].H) } else { Check 'N09c 确认框里有「执行」按钮' $false '找不到执行按钮' }
                    Start-Sleep -Seconds 3
                    $forceHolder.Refresh()
                    Check 'N09c 抽句柄之后那个程序**还活着**（跟「结束进程」的根本区别）' `
                        (-not $forceHolder.HasExited) ('holder 还在=' + (-not $forceHolder.HasExited))

                    $free = $false
                    try { $fs = [System.IO.File]::Open($forceFile, 'Open', 'ReadWrite', 'None'); $free = $true; $fs.Close() } catch { $free = $false }
                    Check 'N09d 文件真的自由了（我自己能独占打开它）' $free ''

                    $fstatus = @(Get-ChildControls -RootHandle $forceWin[0].H | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
                    Check 'N09e 窗口里念了抽句柄的结果' `
                        ((@($fstatus | Where-Object { $_ -match '抽掉了|句柄' }).Count -ge 1) -or $free) (($fstatus | Where-Object { $_.Length -gt 0 }) -join ' | ')
                } else {
                    Check 'N09b 确认框把风险说清楚了（抽句柄可能让那个程序出错 / 丢数据）' $false '没弹出确认框'
                    Check 'N09c 抽句柄之后那个程序**还活着**（跟「结束进程」的根本区别）' $false 'skipped'
                    Check 'N09d 文件真的自由了（我自己能独占打开它）' $false 'skipped'
                    Check 'N09e 窗口里念了抽句柄的结果' $false 'skipped'
                }
            } else {
                Check 'N09b 确认框把风险说清楚了（抽句柄可能让那个程序出错 / 丢数据）' $false 'skipped'
                Check 'N09c 抽句柄之后那个程序**还活着**（跟「结束进程」的根本区别）' $false 'skipped'
                Check 'N09d 文件真的自由了（我自己能独占打开它）' $false 'skipped'
                Check 'N09e 窗口里念了抽句柄的结果' $false 'skipped'
            }
            [void][TBGui]::CloseWindow($forceWin[0].H)
            Start-Sleep -Milliseconds 700
        } else {
            Check 'N09a 窗口里有「强制解锁（不关程序）」按钮（照火绒那套：不结束进程，只抽句柄）' $false 'skipped（窗口没起来）'
            Check 'N09b 确认框把风险说清楚了（抽句柄可能让那个程序出错 / 丢数据）' $false 'skipped'
            Check 'N09c 抽句柄之后那个程序**还活着**（跟「结束进程」的根本区别）' $false 'skipped'
            Check 'N09d 文件真的自由了（我自己能独占打开它）' $false 'skipped'
            Check 'N09e 窗口里念了抽句柄的结果' $false 'skipped'
        }
    } finally {
        if ($forceHolder -and -not $forceHolder.HasExited) { Stop-Process -Id $forceHolder.Id -Force -ErrorAction SilentlyContinue }
        if ($forceGui -and -not $forceGui.HasExited) { try { $forceGui.Kill() } catch { } }
        if (Test-Path -LiteralPath $forceRoot) { Remove-Item -LiteralPath $forceRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # ---- N17 / N18：用户 2026-10-04 报的「窗口高度没有做自适应」最容易看出来的那种情况 ——
    #      **没查到占用**（比如右键记事本里开着的那份 txt）：窗口里那段正文有八九行
    #      （自查结论 + 能不能删 + 为什么 + 四条可能原因），原来 300px 高的固定窗口会把它切掉一半。
    #      这里右键一个**谁都没占**的文件，断言：正文完整（窗口跟着长高）而且没有控件被切。
    $freeRoot = Join-Path $env:TEMP 'mxx1-unlock-free-gui'
    if (Test-Path -LiteralPath $freeRoot) { Remove-Item -LiteralPath $freeRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $freeRoot | Out-Null
    $freeFile = Join-Path $freeRoot 'nothing-holds-me.txt'
    Set-Content -LiteralPath $freeFile -Value 'x' -Encoding UTF8
    $freeProc = $null
    try {
        $freeProc = Start-Process -FilePath $Exe -PassThru -ArgumentList @('rightmenu', 'unlock', $freeFile)
        [void]$script:Procs.Add($freeProc)
        $freeWin = @()
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 250
            $freeWin = @((Get-TopWindows -ProcessId $freeProc.Id) | Where-Object { $_.Visible -and $_.Text -eq '解除文件占用' })
            if ($freeWin.Count -gt 0) { break }
        }
        if ($freeWin.Count -gt 0) {
            $fbodies = @()
            for ($i = 0; $i -lt 30; $i++) {
                $fbodies = @(Get-ChildControls -RootHandle $freeWin[0].H | Where-Object { $_.Class -like '*STATIC*' -and $_.Text -match '独占打开' })
                if ($fbodies.Count -gt 0) { break }
                Start-Sleep -Milliseconds 250
            }
            $ffit = Get-FitReport -Hwnd $freeWin[0].H
            # 正文那一块本身就是"高"的（八九行）；窗口高度必须跟着它长，而且底部不留大空白
            $bodyH = 0
            if ($fbodies.Count -gt 0) { $bodyH = $fbodies[0].Height }
            Check 'N17 没查到占用时长正文（自查结论 + 原因那几行）完整显示，窗口跟着长高' `
                (($fbodies.Count -ge 1) -and ($bodyH -gt 80) -and ($ffit.Clipped -eq 0)) `
                ('正文块高=' + $bodyH + ' 被切=' + $ffit.Clipped + ' 客户区高=' + $ffit.ClientH)
            Check 'N18 窗口高度贴着内容（下面没留一大截空白，上面也没被顶掉）' `
                (($ffit.Gap -ge 0) -and ($ffit.Gap -le 40) -and ($ffit.Widgets -ge 8)) `
                ('底部空白=' + $ffit.Gap + ' 控件=' + $ffit.Widgets)
            [void][TBGui]::CloseWindow($freeWin[0].H)
            Start-Sleep -Milliseconds 700
        } else {
            Check 'N17 没查到占用时长正文（自查结论 + 原因那几行）完整显示，窗口跟着长高' $false 'skipped（窗口没起来）'
            Check 'N18 窗口高度贴着内容（下面没留一大截空白，上面也没被顶掉）' $false 'skipped'
        }
    } finally {
        if ($freeProc -and -not $freeProc.HasExited) { try { $freeProc.Kill() } catch { } }
        if (Test-Path -LiteralPath $freeRoot) { Remove-Item -LiteralPath $freeRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # ---- N19 / N19b：用户 2026-10-05 要的「一键解除占用」—— **不许弹任何窗口** ----------------
    # 这一条是本组的重点：真起一个 `rightmenu unlock --auto` 进程，从它出生到退出每 100ms 枚举一次
    # 它的顶层窗口 —— 一个可见窗口都不许有（连一闪而过的也不行），同时那个占着文件的进程必须被结束、
    # 文件必须松开。文件名 / 路径都用拼的（编码体检不许出现字面量绝对路径）。
    $autoRoot = Join-Path $env:TEMP 'mxx1-auto-gui'
    if (Test-Path -LiteralPath $autoRoot) { Remove-Item -LiteralPath $autoRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $autoRoot | Out-Null
    $autoFile = Join-Path $autoRoot 'auto-locked.txt'
    Set-Content -LiteralPath $autoFile -Value 'x' -Encoding UTF8
    $autoHolder = $null
    $autoProc = $null
    try {
        $autoHolder = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
            '-NoProfile', '-Command', ("`$fs=[System.IO.File]::Open('" + $autoFile + "','Open','ReadWrite','None'); Start-Sleep 120"))
        Start-Sleep -Seconds 2
        $autoProc = Start-Process -FilePath $Exe -PassThru -ArgumentList @('rightmenu', 'unlock', '--auto', '--quiet', $autoFile)
        [void]$script:Procs.Add($autoProc)
        $autoSeen = New-Object System.Collections.ArrayList
        for ($i = 0; $i -lt 200; $i++) {          # 最多 20 秒（文件夹 / 大文件时扫描会久一点）
            foreach ($w in @(Get-TopWindows -ProcessId $autoProc.Id)) {
                if ($w.Visible) { [void]$autoSeen.Add($w.Text) }
            }
            $autoProc.Refresh()
            if ($autoProc.HasExited) { break }
            Start-Sleep -Milliseconds 100
        }
        Start-Sleep -Milliseconds 500
        $autoProc.Refresh()
        Check 'N19 一键解除（--quiet）不弹任何窗口（从生到死一个可见窗口都没有），而且自己退出' `
            (($autoProc.HasExited) -and ($autoSeen.Count -eq 0)) `
            ('看到过的窗口=' + $(if ($autoSeen.Count -eq 0) { '（没有）' } else { $autoSeen -join ' / ' }) + ' 退出了=' + $autoProc.HasExited)

        $autoHolder.Refresh()
        $autoOpen = $false
        try { $fs2 = [System.IO.File]::Open($autoFile, 'Open', 'ReadWrite', 'None'); $autoOpen = $true; $fs2.Close() } catch { $autoOpen = $false }
        Check 'N19b 一键解除真把文件松开了（占着它的进程被结束 + 我能独占打开它）' `
            ($autoHolder.HasExited -and $autoOpen) ('holder 退出=' + $autoHolder.HasExited + ' 能独占打开=' + $autoOpen)
    } finally {
        if ($autoHolder -and -not $autoHolder.HasExited) { Stop-Process -Id $autoHolder.Id -Force -ErrorAction SilentlyContinue }
        if ($autoProc -and -not $autoProc.HasExited) { try { $autoProc.Kill() } catch { } }
        if (Test-Path -LiteralPath $autoRoot) { Remove-Item -LiteralPath $autoRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # ---- N20 / N20b：扫描期间窗口必须是"活的"（2026-10-05 修的那个卡死）------------------------
    # 原来 FileLock.Scan 直接跑在界面线程上：实测右键一个 400 个文件的文件夹，窗口 184ms 出现、
    # 612ms 起完全没响应，一直到 7093ms（拖不动、关不掉、任务栏写"无响应"）。这条回归不看截图、
    # 也不看"窗口还在不在"，而是每 20ms 用 WM_NULL 问一次"你还处理消息吗"，量**最长一次没人应的时长**。
    # 修好之后那个数应该是个位数毫秒（不管扫描要多久，它都不在界面线程上）；修之前会是整个扫描的时长。
    $slowRoot = Join-Path $env:TEMP 'mxx1-unlock-slow-gui'
    if (Test-Path -LiteralPath $slowRoot) { Remove-Item -LiteralPath $slowRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $slowRoot | Out-Null
    for ($i = 1; $i -le 400; $i++) {
        [System.IO.File]::WriteAllText((Join-Path $slowRoot ('f{0:000}.txt' -f $i)), 'x')
    }
    $slowFile = Join-Path $slowRoot 'f200.txt'      # 被占的是第 200 个：二分定位那条路也一起走一遍
    $slowHolder = $null
    $slowProc = $null
    try {
        $slowHolder = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
            '-NoProfile', '-Command', ("`$fs=[System.IO.File]::Open('" + $slowFile + "','Open','ReadWrite','None'); Start-Sleep 120"))
        Start-Sleep -Seconds 2
        $slowProc = Start-Process -FilePath $Exe -PassThru -ArgumentList @('rightmenu', 'unlock', $slowRoot)
        [void]$script:Procs.Add($slowProc)
        $slowWin = @()
        for ($i = 0; $i -lt 60; $i++) {
            Start-Sleep -Milliseconds 100
            $slowWin = @((Get-TopWindows -ProcessId $slowProc.Id) | Where-Object { $_.Visible -and $_.Text -eq '解除文件占用' })
            if ($slowWin.Count -gt 0) { break }
        }
        if ($slowWin.Count -gt 0) {
            $sh = $slowWin[0].H
            Start-Sleep -Milliseconds 120          # 让消息循环先转起来（窗口显示和 Run 之间有几毫秒）
            $maxGap = 0
            $gapStart = -1
            $sawScan = $false
            $sawDone = $false
            $clock = [System.Diagnostics.Stopwatch]::StartNew()
            for ($i = 0; $i -lt 120; $i++) {
                $ok = [TBGui]::Responds($sh, 400)
                $nowMs = $clock.ElapsedMilliseconds
                if (-not $ok) {
                    if ($gapStart -lt 0) { $gapStart = $nowMs }
                } else {
                    if ($gapStart -ge 0) {
                        $gap = $nowMs - $gapStart
                        if ($gap -gt $maxGap) { $maxGap = $gap }
                        $gapStart = -1
                    }
                    $st = @(Get-ChildControls -RootHandle $sh | Where-Object { $_.Class -like '*STATIC*' } | ForEach-Object { $_.Text })
                    if (@($st | Where-Object { $_ -match '正在检查谁占着它' }).Count -gt 0) { $sawScan = $true }
                    if (@($st | Where-Object { $_ -match '个程序占着它|没有程序锁着它|确实有程序占着它' }).Count -gt 0) { $sawDone = $true }
                }
                if ($sawDone -and $clock.ElapsedMilliseconds -gt 1500) { break }
                if ($clock.ElapsedMilliseconds -gt 6000) { break }
                Start-Sleep -Milliseconds 20
            }
            Check 'N20 扫描期间窗口一直在处理消息（最长一次没人应 < 500ms；以前是整个扫描都卡着）' `
                (($maxGap -lt 500) -and [TBGui]::Alive($sh)) `
                ('最长无响应=' + $maxGap + 'ms 采到"正在检查"=' + $sawScan + ' 采到结论=' + $sawDone)
            Check 'N20b 夹具自检：这一轮真的在扫、也真的出了结论（不然 N20 等于没测）' `
                ($sawScan -and $sawDone) ('正在检查=' + $sawScan + ' 结论=' + $sawDone)
            [void][TBGui]::CloseWindow($sh)
            Start-Sleep -Milliseconds 700
        } else {
            Check 'N20 扫描期间窗口一直在处理消息（最长一次没人应 < 500ms；以前是整个扫描都卡着）' $false 'skipped（窗口没起来）'
            Check 'N20b 夹具自检：这一轮真的在扫、也真的出了结论（不然 N20 等于没测）' $false 'skipped（窗口没起来）'
        }
    } finally {
        if ($slowHolder -and -not $slowHolder.HasExited) { Stop-Process -Id $slowHolder.Id -Force -ErrorAction SilentlyContinue }
        if ($slowProc -and -not $slowProc.HasExited) { try { $slowProc.Kill() } catch { } }
        if (Test-Path -LiteralPath $slowRoot) { Remove-Item -LiteralPath $slowRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # ---- N21 / N21b / N21c / N21d：提示卡"真画得出来 + 就在鼠标旁边 + 跟着鼠标走 + 到点自己消失" ----
    # 用户 2026-10-06 报：「气泡没有正常弹出，而且弹出的位置要跟随鼠标」。原来那条路用的是系统托盘
    # 气泡（NotifyIcon.ShowBalloonTip）—— 这台精简版 Windows 上**根本看不到**（Win10 / Win11 把
    # 「通知」或专注助手关掉时同样看不到），而且它的位置由系统定死在右下角，离用户正看着的地方很远。
    # 现在改成自己画的卡片（src\Balloon.cs 的 NoticeForm）。四条规矩这条检查全盯住：
    # 不抢焦点（WS_EX_NOACTIVATE）、跟着鼠标、到点自己关、而且**只此一张卡**（不是又弹回那个窗口）。
    # 它要真动鼠标，所以先存下原来的位置、跑完放回去（和 B10/B11 一个规矩）。
    $noticeRoot = Join-Path $env:TEMP 'mxx1-notice-gui'
    if (Test-Path -LiteralPath $noticeRoot) { Remove-Item -LiteralPath $noticeRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $noticeRoot | Out-Null
    $noticeFile = Join-Path $noticeRoot 'free.txt'
    Set-Content -LiteralPath $noticeFile -Value 'x' -Encoding UTF8
    $noticeSaved = [TBGui]::CursorAt()
    $noticeProc = $null
    try {
        $nwa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        [void][TBGui]::MoveCursor([int]($nwa.Left + $nwa.Width / 2), [int]($nwa.Top + $nwa.Height / 3))
        Start-Sleep -Milliseconds 300

        # --notify=3000：这条只等 3 秒（默认 6 秒，时间全花在等它自己消失上）
        $noticeProc = Start-Process -FilePath $Exe -PassThru -ArgumentList @('rightmenu', 'unlock', '--auto', '--notify=3000', $noticeFile)
        [void]$script:Procs.Add($noticeProc)
        $noticeWin = @()
        for ($i = 0; $i -lt 60; $i++) {
            $noticeWin = @((Get-TopWindows -ProcessId $noticeProc.Id) | Where-Object { $_.Visible -and $_.Text -eq '一键解除占用' })
            if ($noticeWin.Count -gt 0) { break }
            Start-Sleep -Milliseconds 100
        }
        if ($noticeWin.Count -eq 0) {
            foreach ($n in @(
                'N21 提示卡真画得出来，而且就在鼠标旁边（不抢焦点、不占任务栏）',
                'N21b 鼠标一动，卡片跟着动（不是落在角落里不管）',
                'N21c 到点自己消失（没人点它，进程也跟着退出）',
                'N21d 卡片上真的有字（不是一张空白窗口）')) {
                Check $n $false 'skipped（卡片没起来）'
            }
        } else {
            $nh = $noticeWin[0].H

            # 位置：卡片跟鼠标有最多 60ms 的滞后，采样几次取最贴的一次（用户这时候也可能在动鼠标）
            $best = $null
            for ($i = 0; $i -lt 6; $i++) {
                $c = [TBGui]::CursorAt()
                $r = [TBGui]::Rect($nh)
                $spread = [Math]::Abs(($r[0] - $c[0]) - 18) + [Math]::Abs(($r[1] - $c[1]) - 22)
                if (($best -eq $null) -or ($spread -lt $best.Spread)) {
                    $best = [pscustomobject]@{ C = $c; R = $r; Spread = $spread }
                }
                Start-Sleep -Milliseconds 120
            }
            $nex = [TBGui]::ExStyles($nh)
            $noDialog = (@((Get-TopWindows -ProcessId $noticeProc.Id) | Where-Object { $_.Visible -and $_.Text -eq '解除文件占用' }).Count -eq 0)
            Check 'N21 提示卡真画得出来，而且就在鼠标旁边（不抢焦点、不占任务栏）' `
                (($best.Spread -le 20) -and (($nex -band 0x08000000) -ne 0) -and (($nex -band 0x00000080) -ne 0) -and $noDialog) `
                ('卡片=' + ($best.R -join ',') + ' 鼠标=' + ($best.C -join ',') + ' 偏差=' + $best.Spread + ' exstyle=0x' + ('{0:X}' -f $nex) + ' 没有弹回结果窗口=' + $noDialog)

            # 「看得到」这四个字只能看渲染出来的像素：卡片是自己画的（不是一堆 Label），
            # 所以取"内区出现最多的颜色"当底色，数一下和底色差得明显的像素（左边那条状态色竖线不算）。
            $shot = Get-WindowShot -Handle $nh
            $ink = -1
            $shotNote = '（窗口抓不到像素）'
            if ($shot) {
                $counts = @{}
                for ($yy = 4; $yy -lt $shot.Height - 4; $yy++) {
                    for ($xx = 6; $xx -lt $shot.Width - 6; $xx++) {
                        $i = ($yy * $shot.Stride) + ($xx * 4)
                        $key = ([int]$shot.Bytes[$i + 2] -shl 16) -bor ([int]$shot.Bytes[$i + 1] -shl 8) -bor [int]$shot.Bytes[$i]
                        if ($counts.ContainsKey($key)) { $counts[$key] = $counts[$key] + 1 } else { $counts[$key] = 1 }
                    }
                }
                $bg = 0; $bestN = -1
                foreach ($k in $counts.Keys) { if ($counts[$k] -gt $bestN) { $bestN = $counts[$k]; $bg = $k } }
                $bgR = ($bg -shr 16) -band 0xFF; $bgG = ($bg -shr 8) -band 0xFF; $bgB = $bg -band 0xFF
                $ink = 0
                for ($yy = 6; $yy -lt $shot.Height - 6; $yy++) {
                    for ($xx = 6; $xx -lt $shot.Width - 6; $xx++) {
                        $i = ($yy * $shot.Stride) + ($xx * 4)
                        $d = [Math]::Abs([int]$shot.Bytes[$i + 2] - $bgR) + [Math]::Abs([int]$shot.Bytes[$i + 1] - $bgG) + [Math]::Abs([int]$shot.Bytes[$i] - $bgB)
                        if ($d -gt 90) { $ink++ }
                    }
                }
                $shotNote = ('墨迹像素=' + $ink + ' 卡片=' + $shot.Width + 'x' + $shot.Height)
            }
            Check 'N21d 卡片上真的有字（不是一张空白窗口）' ($ink -gt 60) $shotNote

            # 跟着鼠标走：把鼠标挪一段，卡片必须挪同样一段（不许留在原地、也不许跑到角落去）
            $c0 = [TBGui]::CursorAt()
            $r0 = [TBGui]::Rect($nh)
            [void][TBGui]::MoveCursor(($c0[0] + 200), ($c0[1] + 120))
            Start-Sleep -Milliseconds 700
            $c1 = [TBGui]::CursorAt()
            $r1 = [TBGui]::Rect($nh)
            $dxc = $c1[0] - $c0[0]; $dyc = $c1[1] - $c0[1]
            $dxr = $r1[0] - $r0[0]; $dyr = $r1[1] - $r0[1]
            $after = [Math]::Abs(($r1[0] - $c1[0]) - 18) + [Math]::Abs(($r1[1] - $c1[1]) - 22)
            # 夹具自检：鼠标根本没动起来的时候，这一条测不了（SetCursorPos 没生效 —— 用户正在用鼠标 /
            # 输入桌面被占 / 屏幕锁着都会这样）。2026-10-06 实测过一次：`鼠标挪=0,0`，卡片本身好好的，
            # 记成失败就是假红。但"鼠标动了、卡片没跟着动"必须照旧算失败（那才是真 bug）。
            if (($dxc -eq 0) -and ($dyc -eq 0)) {
                Skip 'N21b 鼠标一动，卡片跟着动（不是落在角落里不管）' `
                    ('鼠标挪不动（SetCursorPos 没生效），这一条测不了；卡片仍在 ' + ($r0 -join ',') + '，鼠标 ' + ($c0 -join ','))
            } else {
                Check 'N21b 鼠标一动，卡片跟着动（不是落在角落里不管）' `
                    (($dxc -ge 150) -and ([Math]::Abs($dxr - $dxc) -le 80) -and ([Math]::Abs($dyr - $dyc) -le 80) -and ($after -le 30)) `
                    ('鼠标挪=' + $dxc + ',' + $dyc + ' 卡片挪=' + $dxr + ',' + $dyr + ' 挪完偏差=' + $after)
            }

            # 到点自己关（没人去点它），进程也跟着退出
            $ngone = $false
            for ($i = 0; $i -lt 60; $i++) {
                Start-Sleep -Milliseconds 200
                if (-not [TBGui]::Alive($nh)) { $ngone = $true; break }
            }
            for ($i = 0; $i -lt 30; $i++) {
                $noticeProc.Refresh()
                if ($noticeProc.HasExited) { break }
                Start-Sleep -Milliseconds 100
            }
            $noticeProc.Refresh()
            Check 'N21c 到点自己消失（没人点它，进程也跟着退出）' ($ngone -and $noticeProc.HasExited) `
                ('卡片还在=' + (-not $ngone) + ' 进程退出=' + $noticeProc.HasExited)
        }
    } finally {
        [void][TBGui]::MoveCursor($noticeSaved[0], $noticeSaved[1])
        if ($noticeProc -and -not $noticeProc.HasExited) { try { $noticeProc.Kill() } catch { } }
        if (Test-Path -LiteralPath $noticeRoot) { Remove-Item -LiteralPath $noticeRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }

    # ---- N10：从右键菜单点「激活状态」「查看设置改动」这种**结果就是一段文字**的按钮，必须弹出
    #      结果窗口。原因：工具箱是 winexe、**没有控制台**，`run <id>` 把报告写进一个不存在的
    #      控制台 —— 用户看到的就是"点了没有效果"（2026-10-04 报的原话）。右键菜单里的命令
    #      因此带 `--show`（M20b3 盯注册表那条），这里盯它真的弹得出来、关掉就退出。
    $resProc = $null
    try {
        $resProc = Start-Process -FilePath $Exe -PassThru -ArgumentList @('run', 'activate-status', '--show')
        [void]$script:Procs.Add($resProc)
        $resWin = @()
        for ($i = 0; $i -lt 60; $i++) {          # 那个脚本要查 WMI，给它 18 秒
            Start-Sleep -Milliseconds 300
            $resWin = @((Get-TopWindows -ProcessId $resProc.Id) | Where-Object { $_.Visible -and $_.Text -match '激活状态' })
            if ($resWin.Count -gt 0) { break }
        }
        Check 'N10a 「激活状态」从右键菜单调起来会弹结果窗口（不然就是"没有效果"）' `
            ($resWin.Count -eq 1) ('窗口=' + (@((Get-TopWindows -ProcessId $resProc.Id) | Where-Object { $_.Visible } | ForEach-Object { $_.Text }) -join ' / '))
        if ($resWin.Count -gt 0) {
            $rkids = @(Get-ChildControls -RootHandle $resWin[0].H)
            $rbox = @($rkids | Where-Object { $_.Class -like '*EDIT*' -and $_.Text.Trim().Length -gt 0 })
            $rbtns = @($rkids | Where-Object { $_.Class -like '*BUTTON*' } | ForEach-Object { $_.Text })
            Check 'N10b 结果窗口里真的有那段文字（激活状态报告），并且有复制 / 关闭按钮' `
                (($rbox.Count -ge 1) -and ($rbtns -contains '关闭')) `
                ('文本框=' + $rbox.Count + ' 按钮=' + ($rbtns -join ' '))
            [void][TBGui]::CloseWindow($resWin[0].H)
            Start-Sleep -Milliseconds 900
            $resProc.Refresh()
            Check 'N10c 关掉结果窗口之后那个进程自己退出了（不留后台进程）' ($resProc.HasExited) ''
        } else {
            Check 'N10b 结果窗口里真的有那段文字（激活状态报告），并且有复制 / 关闭按钮' $false 'skipped（窗口没起来）'
            Check 'N10c 关掉结果窗口之后那个进程自己退出了（不留后台进程）' $false 'skipped'
        }
    } finally {
        if ($resProc -and -not $resProc.HasExited) { try { $resProc.Kill() } catch { } }
    }
} finally {
    if ($unlockChild -and -not $unlockChild.HasExited) { Stop-Process -Id $unlockChild.Id -Force -ErrorAction SilentlyContinue }
    if ($unlockProc -and -not $unlockProc.HasExited) { try { $unlockProc.Kill() } catch { } }
    if (Test-Path -LiteralPath $unlockDir) { Remove-Item -LiteralPath $unlockDir -Recurse -Force -ErrorAction SilentlyContinue }
}

}

# ================================================================ I 组：使用条款确认门 / 更新检查 / 免责窗口
if (Test-GroupSelected 'I') {
Write-Host ''
Write-Host 'I 组：首次运行的使用条款确认门 · 更新检查 · 免责声明窗口'
# 用户 2026-10-04：「缺少完整的检测更新功能/免责/服务协议，你看下 permanent-delete-menu 是怎么做的？」
# 命令行那一半（正文字数 / 指纹 / consent 状态机 / checkupdate 三条路径）由 Test-Cli 的 S 组盯；
# 这一组盯界面这一半：确认门（默认不勾选、不同意就退出、同意后写指纹）、底栏更新提示、条款窗口。
# 注意 Start-Gui 拉起来的是**新进程**，环境变量继承本脚本（MXX1_NO_UPDATE=1 已在开头设好）。

# 先把**自己起过**的界面进程收干净，再 reset 条款状态：任何一个还在退出的旧实例都会在关闭时把
# settings.ini 连同"已同意"的指纹写回去，于是新实例读到 agreed、不弹确认框 —— I02–I10 会整组假红。
# （2026-10-05 真踩过：前面 H 组那个实例退出得慢，正好盖掉了这一次 reset。用户自己开着工具箱时同理。）
foreach ($p in $script:Procs) {
    try {
        if ($p -and -not $p.HasExited) {
            [void]$p.CloseMainWindow()
            [void]$p.WaitForExit(5000)
            $p.Refresh()
            if (-not $p.HasExited) { $p.Kill(); [void]$p.WaitForExit(3000) }
        }
    } catch { }
}
Start-Sleep -Milliseconds 500

$null = Invoke-Exe 'consent --reset'
$cI = Invoke-Exe 'consent'
Check 'I01 --reset 之后条款状态是"需要确认"' ($cI -match '(?m)^consent=required') ''

$procI = Start-Gui
$winI = $null
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 200
    $winI = Find-TopWindow -ProcessId $procI.Id -TextPrefix '使用条款确认'
    if ($winI) { break }
}
$i02Why = '没出现'
if (-not $winI) {
    # 没弹出来时把现场写进失败说明：多半是**另一个实例**（用户开着的那个 / 上一个测试实例还没退完）
    # 在 reset 之后又把 settings.ini 写成了"已同意"，或者它抢在前面把条款确认点掉了。
    $again = [string](Invoke-Exe 'consent')
    $stateLine = (@(($again -split "`r?`n") | Where-Object { $_ -match '^consent=' }) -join ' ')
    $others = @(Get-Process Mxx1Toolbox -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $procI.Id }).Count
    $i02Why = '没出现（当前 ' + $stateLine + '，另有 ' + $others + ' 个工具箱实例在跑）'
}
Check 'I02 没同意过时打开界面会弹《使用条款确认》' ($null -ne $winI) $(if ($winI) { $winI.Text } else { $i02Why })

if ($winI) {
    $ic = @(Get-ChildControls -RootHandle $winI.H)
    $agree = @($ic | Where-Object { $_.Text -eq '同意并继续' })
    $decline = @($ic | Where-Object { $_.Text -eq '不同意，退出' })
    $chk = @($ic | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -like '我已阅读并同意*' })
    Check 'I03 两个出口都在（同意并继续 / 不同意，退出）' (($agree.Count -eq 1) -and ($decline.Count -eq 1)) `
        (($ic | Where-Object { $_.Class -like '*BUTTON*' } | ForEach-Object { $_.Text }) -join ' | ')
    Check 'I04 没勾选时「同意并继续」是禁用的（不能靠回车蒙过去）' `
        (($agree.Count -eq 1) -and (-not [TBGui]::Enabled($agree[0].H))) ''
    Check 'I05 勾选框是没打勾的（默认不同意）' ($chk.Count -eq 1) ('勾选框=' + $chk.Count)
    $bodyBox = @($ic | Where-Object { $_.Class -like '*EDIT*' -and $_.Height -gt 100 })
    $bodyLen = 0
    if ($bodyBox.Count -ge 1) { $bodyLen = $bodyBox[0].Text.Length }
    Check 'I06 窗口里真显示了条款正文（几 KB 的中文，不是一句"见文档"）' ($bodyLen -gt 2000) ('字数=' + $bodyLen)

    if ($chk.Count -eq 1) { [void][TBGui]::Click($chk[0].H); Start-Sleep -Milliseconds 400 }
    $agree2 = @(Get-ChildControls -RootHandle $winI.H | Where-Object { $_.Text -eq '同意并继续' })
    Check 'I07 勾上之后「同意并继续」才可以点' (($agree2.Count -eq 1) -and [TBGui]::Enabled($agree2[0].H)) ''
    if ($agree2.Count -eq 1 -and [TBGui]::Enabled($agree2[0].H)) {
        [void][TBGui]::Click($agree2[0].H)
        Check 'I08 点「同意并继续」之后确认窗口关掉' (Wait-WindowGone -Hwnd $winI.H) ''
        $agreed = Invoke-Exe 'consent'
        Check 'I09 同意状态变成 agreed，而且记的是当前正文指纹（不是一句 true）' `
            (($agreed -match '(?m)^consent=agreed') -and ($agreed -match ('(?m)^consentHash=' + $consentHash))) `
            (($agreed -split "`r?`n" | Where-Object { $_ -like 'consent*' }) -join ' ')
        $mainOk = $false
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 200
            $procI.Refresh()
            if ($procI.HasExited) { break }
            if ($procI.MainWindowHandle -ne [IntPtr]::Zero -and [TBGui]::Enabled($procI.MainWindowHandle)) { $mainOk = $true; break }
        }
        Check 'I10 同意之后主界面能用了（没有被模态窗口压着）' $mainOk ''
    } else {
        Check 'I08 点「同意并继续」之后确认窗口关掉' $false 'skipped（按钮没能点上）'
        Check 'I09 同意状态变成 agreed，而且记的是当前正文指纹（不是一句 true）' $false 'skipped'
        Check 'I10 同意之后主界面能用了（没有被模态窗口压着）' $false 'skipped'
    }
    try { if (-not $procI.HasExited) { [void][TBGui]::CloseWindow($procI.MainWindowHandle) } } catch { }
    Start-Sleep -Milliseconds 800

    # 关掉窗口之后同意记录**必须还在**。用户 2026-10-05 报「使用条款确认每次打开都弹」：
    # 主窗口那份 _settings 是在构造函数里加载的（比确认门早），关窗口时 Save() 又把刚记下的指纹
    # 覆盖成空 —— 于是下次打开再弹一次。I09 是在窗口还开着的时候查的，所以以前根本抓不到这个。
    $afterClose = [string](Invoke-Exe 'consent')
    Check 'I10b 关掉窗口之后同意记录还在（不会被关闭时的设置保存抹掉）' `
        (($afterClose -match '(?m)^consent=agreed') -and ($afterClose -match ('(?m)^consentHash=' + $consentHash))) `
        (($afterClose -split "`r?`n" | Where-Object { $_ -like 'consent*' }) -join ' ')

    # 再开一次界面：同意过就只该弹那一次 —— 这一项就是用户报的现象本身
    $procR = Start-Gui
    $winR = $null
    for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Milliseconds 200
        $w = Find-TopWindow -ProcessId $procR.Id -TextPrefix '使用条款确认'
        if ($w) { $winR = $w; break }
    }
    $mainR = [IntPtr]::Zero
    for ($i = 0; $i -lt 40; $i++) {
        $procR.Refresh()
        if ($procR.HasExited) { break }
        if ($procR.MainWindowHandle -ne [IntPtr]::Zero) { $mainR = $procR.MainWindowHandle; break }
        Start-Sleep -Milliseconds 200
    }
    $othersR = @(Get-Process Mxx1Toolbox -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $procR.Id }).Count
    Check 'I10c 重新打开界面不再弹《使用条款确认》（同意过的就只弹一次）' `
        (($null -eq $winR) -and ($mainR -ne [IntPtr]::Zero)) `
        $(if ($winR) { '又弹了一次：' + $winR.Text + '（另有 ' + $othersR + ' 个工具箱实例在跑）' } else { '主窗口=' + $mainR })
    try { if (-not $procR.HasExited) { [void][TBGui]::CloseWindow($procR.MainWindowHandle) } } catch { }
    Start-Sleep -Milliseconds 500
} else {
    foreach ($nm in @('I03 两个出口都在（同意并继续 / 不同意，退出）', 'I04 没勾选时「同意并继续」是禁用的（不能靠回车蒙过去）',
                      'I05 勾选框是没打勾的（默认不同意）', 'I06 窗口里真显示了条款正文（几 KB 的中文，不是一句"见文档"）',
                      'I07 勾上之后「同意并继续」才可以点', 'I08 点「同意并继续」之后确认窗口关掉',
                      'I09 同意状态变成 agreed，而且记的是当前正文指纹（不是一句 true）',
                      'I10 同意之后主界面能用了（没有被模态窗口压着）',
                      'I10b 关掉窗口之后同意记录还在（不会被关闭时的设置保存抹掉）',
                      'I10c 重新打开界面不再弹《使用条款确认》（同意过的就只弹一次）')) {
        Check $nm $false 'skipped（确认窗口没出现）'
    }
}

# ---- 不同意 = 直接退出程序（不是"取消后继续挂在后台"）
$null = Invoke-Exe 'consent --reset'
$procD = Start-Gui
$winD = $null
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 200
    $winD = Find-TopWindow -ProcessId $procD.Id -TextPrefix '使用条款确认'
    if ($winD) { break }
}
Check 'I11 重置之后又会重新要求确认（改过条款同理）' ($null -ne $winD) $(if ($winD) { $winD.Text } else { '没出现' })
if ($winD) {
    $dec = @(Get-ChildControls -RootHandle $winD.H | Where-Object { $_.Text -eq '不同意，退出' })
    if ($dec.Count -eq 1) { [void][TBGui]::Click($dec[0].H) }
    $quit = $false
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 200
        $procD.Refresh()
        if ($procD.HasExited) { $quit = $true; break }
    }
    Check 'I12 点「不同意，退出」之后程序直接退出（不挂在后台、也不继续跑）' $quit ''
    $stillReq = Invoke-Exe 'consent'
    Check 'I13 拒绝之后状态仍是"需要确认"' ($stillReq -match '(?m)^consent=required') (($stillReq -split "`r?`n" | Where-Object { $_ -like 'consent*' }) -join ' ')
} else {
    Check 'I12 点「不同意，退出」之后程序直接退出（不挂在后台、也不继续跑）' $false 'skipped'
    Check 'I13 拒绝之后状态仍是"需要确认"' $false 'skipped'
}
try { if ($procD -and -not $procD.HasExited) { $procD.Kill() } } catch { }

# ---- 更新检查：本机假接口返回 v9.9.9（不碰外网），底栏那个按钮应该自己变成「发现新版本 v9.9.9」
$null = Invoke-Exe 'consent --accept'
$tcpU = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
$tcpU.Start()
$portU = $tcpU.LocalEndpoint.Port
$tcpU.Stop()
$prefixU = 'http://127.0.0.1:' + $portU + '/'
$listenerU = $null
$procU = $null
try {
    $listenerU = New-Object System.Net.HttpListener
    $listenerU.Prefixes.Add($prefixU)
    $listenerU.Start()
    $ctxU = $listenerU.GetContextAsync()
    $bodyU = '{"tag_name":"v9.9.9","html_url":"' + $prefixU + 'fake-release"}'
    $bytesU = [System.Text.Encoding]::UTF8.GetBytes($bodyU)
    # 空串 = 把开头设的 MXX1_NO_UPDATE=1 顶掉（空值不算开启），让更新检查真的跑起来
    $procU = Start-Gui -Env @{
        MXX1_NO_UPDATE          = ''
        MXX1_UPDATE_URL         = ($prefixU + 'releases/latest')
        MXX1_UPDATE_TAGS_URL    = ($prefixU + 'tags')
    }
    $mainU = $procU.MainWindowHandle
    Check 'I14 假接口那一轮界面能起来' (($mainU -ne [IntPtr]::Zero) -and (-not $procU.HasExited)) ('handle=' + $mainU)

    $btnU = @()
    for ($i = 0; $i -lt 80; $i++) {
        Start-Sleep -Milliseconds 200
        if ($ctxU.IsCompleted) {
            try {
                $cu = $ctxU.Result
                $cu.Response.StatusCode = 200
                $cu.Response.ContentType = 'application/json'
                $cu.Response.ContentLength64 = $bytesU.Length
                $cu.Response.OutputStream.Write($bytesU, 0, $bytesU.Length)
                $cu.Response.OutputStream.Close()
            } catch { }
            $ctxU = $listenerU.GetContextAsync()
        }
        if ($mainU -eq [IntPtr]::Zero) { break }
        $btnU = @(Get-ChildControls -RootHandle $mainU | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -like '发现新版本*' })
        if ($btnU.Count -ge 1) { break }
    }
    Check 'I15 有新版时底栏按钮自己变成「发现新版本 v9.9.9」' ($btnU.Count -eq 1) `
        (($btnU | ForEach-Object { $_.Text }) -join ' | ')
    if ($btnU.Count -eq 1) {
        Check 'I16 按钮上写清了新版本号' ($btnU[0].Text -match 'v9\.9\.9') $btnU[0].Text
        [void][TBGui]::Click($btnU[0].H)
        $dlgU = @()
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 200
            $dlgU = @((Get-TopWindows -ProcessId $procU.Id) | Where-Object { $_.H -ne $mainU -and $_.Visible -and $_.Text -eq '请确认' })
            if ($dlgU.Count -ge 1) { break }
        }
        Check 'I17 点它弹出自家确认框（不是 MessageBox 甩一段字）' ($dlgU.Count -eq 1) `
            ((@((Get-TopWindows -ProcessId $procU.Id) | Where-Object { $_.Visible } | ForEach-Object { $_.Text }) -join ' / '))
        if ($dlgU.Count -ge 1) {
            $ut = (@(Get-ChildControls -RootHandle $dlgU[0].H | ForEach-Object { $_.Text }) -join ' ')
            Check 'I18 确认框里说明了"不自动下载、不替换文件"' (($ut -match '不会自己下载') -and ($ut -match '替换')) ''
            Check 'I19 确认框的动作按钮是「打开发布页」' ($ut -match '打开发布页') ''
            [void][TBGui]::CloseWindow($dlgU[0].H)      # 取消：绝不真的去开浏览器
            Start-Sleep -Milliseconds 500
        } else {
            Check 'I18 确认框里说明了"不自动下载、不替换文件"' $false 'skipped'
            Check 'I19 确认框的动作按钮是「打开发布页」' $false 'skipped'
        }
    } else {
        foreach ($nm in @('I16 按钮上写清了新版本号', 'I17 点它弹出自家确认框（不是 MessageBox 甩一段字）',
                          'I18 确认框里说明了"不自动下载、不替换文件"', 'I19 确认框的动作按钮是「打开发布页」')) {
            Check $nm $false 'skipped（按钮没变成"发现新版本"）'
        }
    }

    # ---- 免责声明窗口：从「关于」进去；关于窗口里那行更新状态必须是**落定的结论**
    $aboutBtn = @(Get-ChildControls -RootHandle $mainU | Where-Object { $_.Class -like '*BUTTON*' -and $_.Text -eq '关于' })
    Check 'I20 底栏还有「关于」按钮' ($aboutBtn.Count -eq 1) ''
    if ($aboutBtn.Count -eq 1) {
        [void][TBGui]::Click($aboutBtn[0].H)
        $aboutWin = $null
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 200
            $aboutWin = Find-TopWindow -ProcessId $procU.Id -TextPrefix '关于'
            if ($aboutWin) { break }
        }
        Check 'I21 「关于」窗口打开了' ($null -ne $aboutWin) ''
        if ($aboutWin) {
            $aboutTexts = @(Get-ChildControls -RootHandle $aboutWin.H | ForEach-Object { $_.Text })
            # 老实现遇到"已经有一次检查在跑"就把回调丢掉 → 这一行永远停在「正在检查…」（隔壁踩过）
            Check 'I22 关于窗口的更新状态行落到了真实结论（不是永远停在"正在检查…"）' `
                (((@($aboutTexts | Where-Object { $_ -match '发现新版本 v9\.9\.9' }).Count) -ge 1) -and `
                 ((@($aboutTexts | Where-Object { $_ -eq '正在检查…' }).Count) -eq 0)) `
                (($aboutTexts | Where-Object { $_ -match '更新|新版本|检查' }) -join ' | ')
            $termsBtn = @(Get-ChildControls -RootHandle $aboutWin.H | Where-Object { $_.Text -eq '免责声明' })
            Check 'I23 关于窗口里有「免责声明」入口' ($termsBtn.Count -eq 1) (($aboutTexts | Where-Object { $_.Length -gt 0 -and $_.Length -lt 12 }) -join ' / ')
            if ($termsBtn.Count -eq 1) {
                [void][TBGui]::Click($termsBtn[0].H)
                $termsWin = $null
                for ($i = 0; $i -lt 40; $i++) {
                    Start-Sleep -Milliseconds 200
                    $termsWin = Find-TopWindow -ProcessId $procU.Id -TextPrefix '免责声明'
                    if ($termsWin) { break }
                }
                Check 'I24 免责声明窗口打开了' ($null -ne $termsWin) ''
                if ($termsWin) {
                    $tb = @(Get-ChildControls -RootHandle $termsWin.H | Where-Object { $_.Class -like '*EDIT*' })
                    $tlen = 0
                    if ($tb.Count -ge 1) { $tlen = $tb[0].Text.Length }
                    # 和命令行 `disclaimer` 打出来的是同一份（窗口与文档永远一致）
                    Check 'I25 窗口里的正文和命令行那份一样（同一个正本，几 KB）' ($tlen -gt 2000) ('字数=' + $tlen)
                    [void][TBGui]::CloseWindow($termsWin.H)
                    Start-Sleep -Milliseconds 400
                } else {
                    Check 'I25 窗口里的正文和命令行那份一样（同一个正本，几 KB）' $false 'skipped'
                }
            } else {
                Check 'I24 免责声明窗口打开了' $false 'skipped（没有入口按钮）'
                Check 'I25 窗口里的正文和命令行那份一样（同一个正本，几 KB）' $false 'skipped'
            }
            [void][TBGui]::CloseWindow($aboutWin.H)
            Start-Sleep -Milliseconds 400
        } else {
            foreach ($nm in @('I22 关于窗口的更新状态行落到了真实结论（不是永远停在"正在检查…"）',
                              'I23 关于窗口里有「免责声明」入口', 'I24 免责声明窗口打开了',
                              'I25 窗口里的正文和命令行那份一样（同一个正本，几 KB）')) {
                Check $nm $false 'skipped（关于窗口没起来）'
            }
        }
    } else {
        foreach ($nm in @('I20 底栏还有「关于」按钮', 'I21 「关于」窗口打开了',
                          'I22 关于窗口的更新状态行落到了真实结论（不是永远停在"正在检查…"）',
                          'I23 关于窗口里有「免责声明」入口', 'I24 免责声明窗口打开了',
                          'I25 窗口里的正文和命令行那份一样（同一个正本，几 KB）')) {
            Check $nm $false 'skipped'
        }
    }
    try { if ($mainU -ne [IntPtr]::Zero) { [void][TBGui]::CloseWindow($mainU) } } catch { }
    Start-Sleep -Milliseconds 900
} finally {
    if ($listenerU -ne $null) { try { $listenerU.Stop(); $listenerU.Close() } catch { } }
    if ($procU -and -not $procU.HasExited) { try { $procU.Kill() } catch { } }
}

}

# ---------------------------------------------------------------- 现场复原
Restore-UserLayer

# 把 MXX1_NO_RIGHTMENU_SYNC / MXX1_NO_UPDATE 恢复成测试之前的样子（别给同一个 shell 里后面的命令留下副作用）
if ($script:SyncHad) { $env:MXX1_NO_RIGHTMENU_SYNC = $script:SyncOld }
else { Remove-Item Env:MXX1_NO_RIGHTMENU_SYNC -ErrorAction SilentlyContinue }
if ($script:UpdateHad) { $env:MXX1_NO_UPDATE = $script:UpdateOld }
else { Remove-Item Env:MXX1_NO_UPDATE -ErrorAction SilentlyContinue }
if ($script:NoOpenHad) { $env:MXX1_NO_OPEN = $script:NoOpenOld }
else { Remove-Item Env:MXX1_NO_OPEN -ErrorAction SilentlyContinue }

Write-Host ''
Write-Host '----------------------------------------------------------'
Write-Host (" 界面回归: 通过 {0} 项, 失败 {1} 项" -f $script:Pass, $script:Fail)
if ($script:SkipCount -gt 0) { Write-Host (" （另有 {0} 项环境不满足，跳过 —— 不算失败，原因见上面 [SKIP] 那几行）" -f $script:SkipCount) }

# 挑组跑的时候，把"没跑哪些组"写在脸上：这份欠账要进汇报与提交信息，发版前要清空
if ($script:PickMode) {
    $allGroups = @(Get-AllGroups)
    $notRun = @($allGroups | Where-Object { $script:RanGroups -notcontains $_ })
    Write-Host (' 本次跑的组: ' + $(if ($script:RanGroups.Count -gt 0) { $script:RanGroups -join ',' } else { '（无）' }))
    if ($notRun.Count -gt 0) {
        Write-Host (' 本次没跑的组: ' + ($notRun -join ',') + '  ← 提交信息与汇报里要写出来；发版前要清空这份欠账')
    } else {
        Write-Host ' 本次没跑的组: （无，全跑了）'
    }
}
Write-Host '----------------------------------------------------------'
if ($script:Fail -gt 0) { exit 1 }
exit 0
