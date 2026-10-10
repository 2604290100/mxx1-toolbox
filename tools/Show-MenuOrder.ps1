<#
    Show-MenuOrder.ps1 -- 把**真实右键菜单**按顺序打印出来（只读，一个字节都不写）。

    为什么要这个脚本：Windows 把同一个右键位置里的静态菜单项**按注册表键名（verb 名）的字母序**排，
    不看我们想让它排第几 —— 所以"菜单里谁在上面"这件事**没法靠读代码回答**，只能问 shell 自己。
    这个脚本走的就是资源管理器用来搭菜单的那套接口：
        SHParseDisplayName → SHBindToParent → IShellFolder::GetUIObjectOf / CreateViewObject
        → IContextMenu::QueryContextMenu → 遍历 HMENU（GetMenuStringW + GetMenuState + GetSubMenu）
    所以打出来的顺序就是你在资源管理器里看到的顺序。背景与结论见 docs\DESIGN.md §12.64。

    用法:
        powershell -NoProfile -ExecutionPolicy Bypass -File tools\Show-MenuOrder.ps1
        powershell ... -File tools\Show-MenuOrder.ps1 -Path D:\某个文件夹
        powershell ... -File tools\Show-MenuOrder.ps1 -SkipDesktop
        powershell ... -File tools\Show-MenuOrder.ps1 -Extended

    `-Extended`：连"按住 Shift 才显示"的项一起打出来（`Extended` 值 / `CMF_EXTENDEDVERBS`）。
    默认不带它 —— 那才是你**平时**右键看到的样子。2026-10-10 排查「Windows 保护的那几个键为什么
    管理员也写不动」时加的：`在此处打开命令窗口 / Powershell` 这类项都是 Extended，不带这个开关
    在菜单里根本看不到它们。

    带 ★ 的行 = 工具箱自己装的六项（复制文件名 / 复制文件路径 / 解除文件占用 / 一键解除占用 /
    常用功能 / 在此处打开终端）。**顺序就是从上往下的行序**；★ 之间谁在前谁在后 = 键名里的序号。

    两个已知的读不出来的地方（不是我们写错了）：
      · 级联子菜单的子项是**懒加载**的（鼠标真移上去才由 Windows 填），所以这里只能读到一行空文本
        —— 「发送到」「新建」也一样，属于同一件事；
      · 菜单里别家工具的项（TortoiseGit / 网盘 / 压缩软件）是 shell 扩展，跟静态项分组排，
        这里一样如实列出来（分组关系在 §12.64 ① 的注里）。
#>
param(
    [string]$Path = '',
    [switch]$SkipDesktop,
    [switch]$Extended
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Path)) { $Path = Split-Path -Parent $PSScriptRoot }
if (-not (Test-Path -LiteralPath $Path)) { Write-Host ('路径不存在：' + $Path); exit 2 }
$filePath = $Path
if (Test-Path -LiteralPath $Path -PathType Container) {
    # 文件夹那档用文件夹自己；再挑一个里面的文件来看"右键一个文件"长什么样
    $filePath = (Get-ChildItem -LiteralPath $Path -File -ErrorAction SilentlyContinue |
                 Select-Object -First 1 -ExpandProperty FullName)
}

$src = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class MenuOrderProbe
{
    /// <summary>`true` = 问菜单时带上 CMF_EXTENDEDVERBS，连"按住 Shift 才显示"的项一起列出来。</summary>
    public static bool Extended = false;
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll", PreserveSig = false)]
    static extern void SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("shell32.dll", PreserveSig = false)]
    static extern void SHGetDesktopFolder(out IntPtr ppshf);

    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW")]
    static extern int GetMenuString(IntPtr hMenu, uint uIDItem, StringBuilder lpString, int cchMax, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);
    [DllImport("user32.dll")] static extern uint GetMenuItemID(IntPtr hMenu, int nPos);
    [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr hMenu, uint uId, uint uFlags);

    const uint MF_BYPOSITION = 0x400;
    const uint MF_SEPARATOR = 0x800;
    const uint MF_POPUP = 0x10;

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        void EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        void GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        void InvokeCommand(IntPtr pici);
        void GetCommandString(IntPtr idCmd, uint uFlags, IntPtr reserved, StringBuilder pszName, uint cchMax);
    }

    static readonly Guid IID_IShellFolder = new Guid("000214E6-0000-0000-C000-000000000046");
    static readonly Guid IID_IContextMenu = new Guid("000214E4-0000-0000-C000-000000000046");

    /// 某个路径的菜单（文件 / 文件夹）；background=true 时拿它所在文件夹的"空白处"菜单
    public static string Dump(string path, bool background)
    {
        IntPtr pidl;
        uint attrs;
        SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out attrs);
        Guid sfGuid = IID_IShellFolder;
        IntPtr sfPtr;
        IntPtr child;
        SHBindToParent(pidl, ref sfGuid, out sfPtr, out child);
        IShellFolder folder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(sfPtr, typeof(IShellFolder));
        return FromFolder(folder, background ? IntPtr.Zero : child, background);
    }

    /// 桌面本身的视图菜单（= DesktopBackground\Shell 那一档）
    public static string DumpDesktop()
    {
        IntPtr sfPtr;
        SHGetDesktopFolder(out sfPtr);
        IShellFolder folder = (IShellFolder)Marshal.GetTypedObjectForIUnknown(sfPtr, typeof(IShellFolder));
        return FromFolder(folder, IntPtr.Zero, true);
    }

    static string FromFolder(IShellFolder folder, IntPtr childPidl, bool background)
    {
        Guid cmGuid = IID_IContextMenu;
        IntPtr cmPtr;
        if (background)
        {
            folder.CreateViewObject(IntPtr.Zero, ref cmGuid, out cmPtr);
        }
        else
        {
            IntPtr arr = Marshal.AllocCoTaskMem(IntPtr.Size);
            Marshal.WriteIntPtr(arr, childPidl);
            folder.GetUIObjectOf(IntPtr.Zero, 1, arr, ref cmGuid, IntPtr.Zero, out cmPtr);
            Marshal.FreeCoTaskMem(arr);
        }
        IContextMenu cm = (IContextMenu)Marshal.GetTypedObjectForIUnknown(cmPtr, typeof(IContextMenu));
        IntPtr h = CreatePopupMenu();
        cm.QueryContextMenu(h, 0, 1, 0x7FFF, Extended ? 0x100u : 0u);   // 0x100 = CMF_EXTENDEDVERBS
        StringBuilder sb = new StringBuilder();
        Walk(sb, h, 0);
        DestroyMenu(h);
        return sb.ToString();
    }

    static void Walk(StringBuilder sb, IntPtr h, int depth)
    {
        int n = GetMenuItemCount(h);
        int shown = 0;
        for (int i = 0; i < n; i++)
        {
            uint state = GetMenuState(h, (uint)i, MF_BYPOSITION);
            string pad = new string(' ', depth * 4);
            if ((state & MF_SEPARATOR) != 0)
            {
                sb.Append(pad).Append("    ---------- 分隔线 ----------").AppendLine();
                continue;
            }
            StringBuilder text = new StringBuilder(512);
            GetMenuString(h, (uint)i, text, 512, MF_BYPOSITION);
            uint id = GetMenuItemID(h, i);
            bool popup = (id == 0xFFFFFFFF) || ((state & MF_POPUP) != 0);
            shown++;
            sb.Append(pad).Append(shown.ToString().PadLeft(3)).Append(".  ");
            sb.Append(text.Length == 0 ? "(空文本：子项是懒加载的，这里读不出来)" : text.ToString());
            if (popup) { sb.Append("   ▸"); }
            sb.AppendLine();
            if (popup)
            {
                IntPtr sub = GetSubMenu(h, i);
                if (sub != IntPtr.Zero) { Walk(sb, sub, depth + 1); }
            }
        }
    }
}
'@
Add-Type -TypeDefinition $src -Language CSharp | Out-Null
[MenuOrderProbe]::Extended = $Extended.IsPresent

# 工具箱那六项（标题就是菜单上显示的字）—— 命中的行打 ★
$mine = @('复制文件名', '复制文件路径', '解除文件占用', '一键解除占用', '常用功能', '在此处打开终端')

function Show-One([string]$title, [string]$text) {
    Write-Host ''
    Write-Host ('===== ' + $title + ' =====')
    if ([string]::IsNullOrWhiteSpace($text)) { Write-Host '  （什么都没有）'; return }
    foreach ($line in ($text -split "`r?`n")) {
        if ($line.Trim().Length -eq 0) { continue }
        $mark = '  '
        foreach ($m in $mine) { if ($line.IndexOf($m) -ge 0) { $mark = ' ★'; break } }
        Write-Host ($mark + $line.TrimEnd())
    }
}

Write-Host '真实右键菜单的顺序（只读探针：问的是资源管理器自己那套接口，没写任何东西）'
Write-Host ('★ = 工具箱装的六项；★ 之间从上到下的先后 = 键名里的序号（见 docs\DESIGN.md §12.64）')

if (Test-Path -LiteralPath $Path -PathType Container) {
    Show-One ('右键一个文件夹：' + $Path) ([MenuOrderProbe]::Dump($Path, $false))
    Show-One ('文件夹里的空白处：' + $Path) ([MenuOrderProbe]::Dump($Path, $true))
} elseif (Test-Path -LiteralPath $Path -PathType Leaf) {
    Show-One ('右键一个文件：' + $Path) ([MenuOrderProbe]::Dump($Path, $false))
    $parent = Split-Path -Parent $Path
    Show-One ('它所在文件夹的空白处：' + $parent) ([MenuOrderProbe]::Dump($parent, $true))
}
if (-not $SkipDesktop) {
    Show-One '桌面空白处' ([MenuOrderProbe]::DumpDesktop())
}
Write-Host ''
