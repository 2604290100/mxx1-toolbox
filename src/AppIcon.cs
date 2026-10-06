// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>给窗口装上工具箱自己的图标（标题栏 + 任务栏 + Alt+Tab）。
    ///
    /// 为什么要有这个类（2026-10-04 用户报「编译好的 Mxx1Toolbox.exe 没有图标」）：
    /// `build.ps1` 的 `/win32icon:assets\app.ico` 只把图标写进 **exe 的资源**（资源管理器、
    /// 快捷方式、右键菜单那一档看的是它）；而 WinForms 的窗口如果没设 `Form.Icon`，
    /// 画的是 System.Windows.Forms.dll 里那个"空白窗体"图标 —— **标题栏和任务栏依旧是没图标的
    /// 老样子**。所以两处都得管：exe 资源（build.ps1）+ 每个窗口（这里）。
    ///
    /// 取法：csc 把 `/win32icon` 写成资源组 **RT_GROUP_ICON 32512**（实测就是这个号），
    /// 于是 `LoadImage(hInstance, "#32512", IMAGE_ICON, cx, cy, 0)` 能**按窗口要的尺寸**直接取那一档
    /// （16 / 20 / 24 随 DPI）—— 比 `Icon.ExtractAssociatedIcon` 拿一张 32×32 再往下缩清楚得多。
    /// 句柄由本类持有、进程活多久留多久（每个尺寸只取一次），不要 Dispose。</summary>
    internal static class AppIcon
    {
        private const int GroupId = 32512;         // csc /win32icon 写的就是这一组
        private const uint ImageIcon = 1;          // IMAGE_ICON
        private const uint WmSetIcon = 0x0080;
        private const int IconSmall = 0;
        private const int IconBig = 1;

        private static readonly object Gate = new object();
        private static Icon _small;
        private static Icon _big;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr LoadImage(IntPtr instance, IntPtr name, uint type, int cx, int cy, uint load);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam);

        /// <summary>把图标装到窗口上（窗口句柄没建好就直接返回）。
        ///
        /// 顺序有讲究（实测踩过）：`Icon` 属性给的是**大图（32×32）**，这样 WinForms 自己要发
        /// ICON_SMALL / ICON_BIG 的时候（改标题、换主题都会重发一次）两边都是 32×32 起的，
        /// 不会出现"任务栏拿一张 16×16 硬撑 32"；发完属性我们再把**小图（按 DPI 16/20/24）**
        /// 补一次 ICON_SMALL —— 标题栏那一格才清楚。</summary>
        public static void Apply(Form form)
        {
            if (form == null) { return; }
            try
            {
                if (!form.IsHandleCreated) { return; }

                Icon big = Big();
                if (big != null) { form.Icon = big; }

                IntPtr smallHandle = SmallHandle();
                if (smallHandle != IntPtr.Zero)
                {
                    SendMessage(form.Handle, WmSetIcon, new IntPtr(IconSmall), smallHandle);
                }
                if (big != null) { SendMessage(form.Handle, WmSetIcon, new IntPtr(IconBig), big.Handle); }
            }
            catch { }
        }

        private static IntPtr SmallHandle()
        {
            Icon small = Small();
            return (small == null) ? IntPtr.Zero : small.Handle;
        }

        /// <summary>标题栏那张：尺寸跟着 DPI 走（100% = 16、125% = 20、150% = 24）。</summary>
        private static Icon Small()
        {
            lock (Gate)
            {
                if (_small == null) { _small = FromModule(SystemInformation.SmallIconSize.Width); }
                return _small;
            }
        }

        /// <summary>任务栏 / Alt+Tab 那张（32×32）。</summary>
        private static Icon Big()
        {
            lock (Gate)
            {
                if (_big == null) { _big = FromModule(SystemInformation.IconSize.Width); }
                return _big;
            }
        }

        private static Icon FromModule(int size)
        {
            if (size < 16) { size = 16; }
            IntPtr h = LoadImage(GetModuleHandle(null), new IntPtr(GroupId), ImageIcon, size, size, 0);
            return (h == IntPtr.Zero) ? null : Icon.FromHandle(h);
        }

        /// <summary>提示卡（见 src\Balloon.cs）左上角那一张小图标。
        ///
        /// **故意不复用窗口那两张缓存**：卡片是**另一个进程**（`rightmenu unlock --auto`）
        /// 里画的，跟主窗口不在一个进程里，缓存也共享不到；现取一张更省事，而且
        /// `Icon.FromHandle` 出来的对象不持有句柄、Dispose 也不会毁掉 exe 资源。
        /// 用它的那条路（一键解除）稍后就退出了，多一个图标句柄无所谓；取不到返回 null，
        /// 卡片就只画字、不画图标。</summary>
        public static Icon ForNotice()
        {
            try { return FromModule(SystemInformation.SmallIconSize.Width); }
            catch { return null; }
        }
    }

    /// <summary>工具箱自己的窗口基类 —— 只做一件事：句柄建好时把图标装上。
    ///
    /// 全部窗口都从它派生，新加窗口自然也有图标，不会漏（`MainForm` / `UnlockForm` 这种
    /// 直接显示在任务栏上的窗口，有没有图标一眼就能看出来）。</summary>
    internal class Mxx1Form : Form
    {
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AppIcon.Apply(this);
        }

        /// <summary>显示之前再补一次：窗口建好之后一般还会改标题 / 字号（那就是一次 WinForms
        /// 自己的图标重发），补一次才不会把标题栏那张小图顶掉。</summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            AppIcon.Apply(this);
        }
    }
}
