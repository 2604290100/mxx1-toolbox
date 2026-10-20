// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>托盘图标（右下角那个小图标）。用户 2026-10-20：「点击关闭后进入托盘图标」。
    ///
    /// 三条口径：
    /// ① **只在真的要缩进去那一刻才建**（`MainForm.HideToTray` 里第一次调用）——
    ///    窗口开着的时候桌面上不会多一个图标，关了才出现，和"关闭 = 进托盘"的直觉一致。
    /// ② 菜单只有两项：**打开主界面** / **退出工具箱**。左键单击、双击也都是"打开主界面"；
    ///    要真退出只能走菜单 —— 点 X 是"缩起来"，别让人找不到程序又关不掉。
    /// ③ 图标用的是主窗口自己那张（`AppIcon` 装上去的），没有的话退回提示卡那张小图；
    ///    实在取不到图标就不显示（返回 false），由调用方退到"任务栏最小化"，
    ///    **绝不留下一个看不见也点不到的窗口**。</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;

        /// <summary>点了「打开主界面」/ 左键 / 双击。参数是那个 NotifyIcon（没用，留着给习惯的写法）。</summary>
        public event EventHandler Open;

        /// <summary>点了「退出工具箱」—— 调用方负责真的退出（这里只管菜单）。</summary>
        public event EventHandler Exit;

        public TrayIcon(Icon icon, string tip, Theme theme)
        {
            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;
            _menu.Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _menu.BackColor = theme.FormBack;
            _menu.ForeColor = theme.ButtonText;

            ToolStripMenuItem open = new ToolStripMenuItem("打开主界面");
            open.Font = new Font(_menu.Font, FontStyle.Bold);
            open.Click += delegate { Raise(Open); };
            ToolStripMenuItem exit = new ToolStripMenuItem("退出工具箱");
            exit.Click += delegate { Raise(Exit); };
            _menu.Items.Add(open);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exit);
            foreach (ToolStripItem it in _menu.Items)
            {
                it.BackColor = theme.FormBack;
                it.ForeColor = theme.ButtonText;
            }

            _icon = new NotifyIcon();
            _icon.Icon = icon;
            _icon.Text = (tip == null) ? AboutForm.ProductTitle : tip;
            _icon.ContextMenuStrip = _menu;
            _icon.DoubleClick += delegate { Raise(Open); };
            _icon.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { Raise(Open); }
            };
        }

        /// <summary>图标真的建得出来吗（没有图标就没法显示 —— 调用方要退到"最小化"）。</summary>
        public bool Usable
        {
            get { return (_icon != null) && (_icon.Icon != null); }
        }

        public void Show()
        {
            try { _icon.Visible = true; }
            catch (Exception ex) { Logger.Write("托盘", "图标没能显示出来：" + ex.Message); }
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception ex) { Logger.Write("托盘", "菜单动作出错：" + ex.Message); }
            }
        }

        public void Dispose()
        {
            try { _icon.Visible = false; } catch { }
            try { _icon.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
        }
    }
}
