// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>把**非模态**窗口（结果窗口 / 日志窗口 / 常用链接）摆到该在的地方。
    ///
    /// 为什么要有这个文件：`StartPosition = FormStartPosition.CenterParent` **只对用
    /// `ShowDialog()` 打开的模态窗口有效**。用 `Show(owner)` 打开的非模态窗口完全不认它，
    /// Windows 会按默认位置放 —— 实际上就是屏幕左上角。用户报的正是这个：
    /// 「点击激活状态为什么会弹到左上角窗口」。所以这里手工居中：先对父窗口居中，
    /// 再夹进那块屏幕的工作区（别顶到任务栏下面或屏幕外），同时开着好几个窗口时往下错开一点。</summary>
    internal static class WindowPlacement
    {
        /// <summary>多个窗口同时开着时，每多一个往下（往右）错这么多像素。</summary>
        private const int CascadeStep = 28;

        /// <summary>居中显示一个非模态窗口。用它替换 `f.Show(this)`。</summary>
        public static void ShowCentered(Form f, IWin32Window owner)
        {
            f.StartPosition = FormStartPosition.Manual;
            IWin32Window parent = owner;
            // 放到 Load 里算：那时候窗口句柄已经建好，f.Width / f.Height 才是含边框的真实尺寸
            f.Load += delegate { f.Location = Centered(f, parent); };
            f.Show(owner);
        }

        /// <summary>算"居中在 owner 上"的左上角坐标（已按工作区夹过）。</summary>
        public static Point Centered(Form f, IWin32Window owner)
        {
            Rectangle target;
            Rectangle area;
            Control c = owner as Control;
            if (c != null && !c.IsDisposed)
            {
                target = c.RectangleToScreen(c.ClientRectangle);
                area = Screen.FromControl(c).WorkingArea;
            }
            else
            {
                target = Screen.PrimaryScreen.WorkingArea;
                area = target;
            }

            int x = target.Left + (target.Width - f.Width) / 2;
            int y = target.Top + (target.Height - f.Height) / 2;

            // 连着点几个按钮会开好几个结果窗口：完全叠在一起的话用户会以为只有一个
            int open = 0;
            foreach (Form other in Application.OpenForms)
            {
                if (other != f && other.Visible && other.Owner == owner) { open++; }
            }
            x += open * CascadeStep;
            y += open * CascadeStep;

            if (x + f.Width > area.Right) { x = area.Right - f.Width; }
            if (y + f.Height > area.Bottom) { y = area.Bottom - f.Height; }
            if (x < area.Left) { x = area.Left; }
            if (y < area.Top) { y = area.Top; }
            return new Point(x, y);
        }
    }
}
