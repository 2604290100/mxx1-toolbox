// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「端口与进程」窗口：只读列出正在监听的 TCP 端口和绑着的 UDP 端口，
    /// 以及每个端口属于哪个进程（PID / 名字 / 路径 / 有没有数字签名）。
    ///
    /// 这个窗口只有一个会改动系统的动作：**结束进程**。它过三道闸：
    /// ① 保护名单（系统关键进程 / pid ≤ 4 / explorer / 工具箱自己）直接不给点；
    /// ② 《使用条款》同意门；
    /// ③ 确认框（点名进程名 + PID，并提醒"没保存的东西会丢"）。
    ///
    /// 数据来源见 `src\PortTable.cs`：iphlpapi 一次拿整张表，不需要管理员、不解析 netstat 的本地化文本。
    /// 不是管理员时有些进程的路径 / 签名读不到 —— 那一列写「读不到」，不猜。</summary>
    internal sealed class PortsForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _filter = new TextBox();
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly Button _refresh = new Button();
        private readonly Button _kill = new Button();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _miPid = new ToolStripMenuItem("复制 PID");
        private readonly ToolStripMenuItem _miPath = new ToolStripMenuItem("复制程序路径");
        private readonly ToolStripMenuItem _miKill = new ToolStripMenuItem("结束这个进程…");

        private volatile bool _busy = false;
        private List<PortTable.Endpoint> _all = new List<PortTable.Endpoint>();

        public PortsForm(Theme theme)
        {
            _theme = theme;
            Text = "端口与进程（只读列出；结束进程要过确认框）";
            ClientSize = new Size(980, 580);
            MinimumSize = new Size(820, 460);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(930, 0);
            intro.Text = "列出**正在监听的 TCP 端口**和**绑着的 UDP 端口**（UDP 没有\"监听\"这回事），"
                + "以及每个端口属于哪个进程。数据来自 Windows 自己的端口表，不需要管理员、不联网。"
                + Environment.NewLine + "点一行选中；右键可以复制 PID / 路径；「结束这个进程」要过确认框（没保存的东西会丢）。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel filterRow = new TableLayoutPanel();
            filterRow.Dock = DockStyle.Fill;
            filterRow.AutoSize = true;
            filterRow.ColumnCount = 4;
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label cap = new Label();
            cap.AutoSize = true;
            cap.Text = "过滤（端口 / 进程名 / 路径）：";
            cap.Margin = new Padding(2, 7, 4, 0);
            filterRow.Controls.Add(cap, 0, 0);
            _filter.Dock = DockStyle.Fill;
            _filter.Margin = new Padding(2, 3, 6, 3);
            _filter.TextChanged += delegate { Fill(_all); };
            filterRow.Controls.Add(_filter, 1, 0);
            _refresh.Text = "刷新";
            StyleButton(_refresh, 84);
            _refresh.Click += delegate { Reload(); };
            filterRow.Controls.Add(_refresh, 2, 0);
            root.Controls.Add(filterRow, 0, 1);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("协议", 56);
            _list.Columns.Add("端口", 64);
            _list.Columns.Add("本地地址", 140);
            _list.Columns.Add("PID", 70);
            _list.Columns.Add("进程", 180);
            _list.Columns.Add("签名", 60);
            _list.Columns.Add("程序路径", 420);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.MouseDown += ListMouseDown;
            _list.DoubleClick += delegate { CopyPid(); };
            root.Controls.Add(_list, 0, 2);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(930, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 4;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 3; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _kill.Text = "结束这个进程…";
            StyleButton(_kill, 150);
            _kill.Click += delegate { KillSelected(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_kill, 1, 0);
            buttons.Controls.Add(close, 2, 0);
            root.Controls.Add(buttons, 0, 4);

            _miPid.Click += delegate { CopyPid(); };
            _miPath.Click += delegate { CopyPath(); };
            _miKill.Click += delegate { KillSelected(); };
            _menu.Items.Add(_miPid);
            _menu.Items.Add(_miPath);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miKill);
            _list.ContextMenuStrip = _menu;

            Controls.Add(root);
            CancelButton = close;
            ApplyTheme(theme);
            Load += delegate { Reload(); };
        }

        private void StyleButton(Button b, int width)
        {
            b.AutoSize = false;
            b.Height = 26;
            b.Width = width;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Margin = new Padding(6, 2, 0, 2);
            b.TabStop = false;
        }

        public void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            ApplyThemeRecursive(this, theme);
        }

        private static void ApplyThemeRecursive(Control parent, Theme theme)
        {
            foreach (Control c in parent.Controls)
            {
                c.BackColor = theme.FormBack;
                Label l = c as Label;
                if (l != null) { l.ForeColor = theme.BarText; }
                TextBox t = c as TextBox;
                if (t != null)
                {
                    t.BackColor = theme.InputBack;
                    t.ForeColor = theme.InputText;
                }
                ListView lv = c as ListView;
                if (lv != null)
                {
                    lv.BackColor = theme.LogBack;
                    lv.ForeColor = theme.LogText;
                }
                Button b = c as Button;
                if (b != null)
                {
                    b.BackColor = theme.ButtonBack;
                    b.ForeColor = theme.ButtonText;
                    b.FlatAppearance.BorderColor = theme.ButtonBorder;
                    b.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
                }
                if (c.Controls.Count > 0) { ApplyThemeRecursive(c, theme); }
            }
        }

        // ------------------------------------------------------------------ 读表

        private void Reload()
        {
            if (_busy) { return; }
            SetBusy(true, "正在读端口表…");
            Thread t = new Thread(delegate ()
            {
                string error;
                List<PortTable.Endpoint> list = PortTable.List(out error);
                try { BeginInvoke((MethodInvoker)delegate { AfterLoad(list, error); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void AfterLoad(List<PortTable.Endpoint> list, string error)
        {
            if (IsDisposed) { return; }
            _all = list;
            Fill(list);
            SetBusy(false, "");
            if (error.Length > 0)
            {
                _status.Text = error;
                _status.ForeColor = _theme.Danger;
                return;
            }
            int tcp = 0;
            int udp = 0;
            foreach (PortTable.Endpoint ep in list) { if (ep.Proto == "TCP") { tcp++; } else { udp++; } }
            _status.Text = "共 " + list.Count.ToString(CultureInfo.InvariantCulture) + " 个端点：TCP 监听 "
                + tcp.ToString(CultureInfo.InvariantCulture) + " 个，UDP 绑定 " + udp.ToString(CultureInfo.InvariantCulture)
                + " 个。选一行才能结束它（系统关键进程不给结束）。";
            Logger.Write("端口与进程", "读取完成：TCP " + tcp.ToString(CultureInfo.InvariantCulture)
                + " / UDP " + udp.ToString(CultureInfo.InvariantCulture));
        }

        private void Fill(List<PortTable.Endpoint> list)
        {
            string needle = _filter.Text.Trim();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                int shown = 0;
                foreach (PortTable.Endpoint ep in list)
                {
                    if (needle.Length > 0)
                    {
                        string hay = ep.Proto + " " + ep.Port.ToString(CultureInfo.InvariantCulture) + " "
                            + ep.LocalAddr + " " + ep.Pid.ToString(CultureInfo.InvariantCulture) + " "
                            + ep.ProcName + " " + ep.ProcPath;
                        if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                    }
                    ListViewItem row = new ListViewItem(ep.Proto);
                    row.SubItems.Add(ep.Port.ToString(CultureInfo.InvariantCulture));
                    row.SubItems.Add(ep.LocalAddr);
                    row.SubItems.Add(ep.Pid.ToString(CultureInfo.InvariantCulture));
                    row.SubItems.Add(ep.ProcName);
                    row.SubItems.Add(ep.SignatureText);
                    row.SubItems.Add(ep.ProcPath.Length > 0 ? ep.ProcPath : "（读不到路径，可能需要管理员）");
                    row.Tag = ep;
                    if (ep.Protected) { row.ForeColor = _theme.Danger; }
                    _list.Items.Add(row);
                    shown++;
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            UpdateButtons();
        }

        private void SetBusy(bool busy, string text)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _refresh.Enabled = !busy;
            if (text.Length > 0) { _status.Text = text; }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            PortTable.Endpoint ep = Selected();
            _kill.Enabled = !_busy && ep != null && !ep.Protected;
            _miKill.Enabled = _kill.Enabled;
            _miPid.Enabled = ep != null;
            _miPath.Enabled = ep != null;
            if (ep != null && ep.Protected) { _kill.Text = "系统关键进程（不给结束）"; }
            else { _kill.Text = "结束这个进程…"; }
        }

        private PortTable.Endpoint Selected()
        {
            if (_list.SelectedItems.Count == 0) { return null; }
            return _list.SelectedItems[0].Tag as PortTable.Endpoint;
        }

        private void ListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) { return; }
            ListViewItem row = _list.GetItemAt(e.X, e.Y);
            if (row != null) { row.Selected = true; }
        }

        // ------------------------------------------------------------------ 右键

        private void CopyPid()
        {
            PortTable.Endpoint ep = Selected();
            if (ep == null) { return; }
            string text = ep.Pid.ToString(CultureInfo.InvariantCulture);
            try
            {
                Clipboard.SetDataObject(text, true);
                _status.Text = "已复制 PID：" + text;
            }
            catch { _status.Text = "剪贴板被别的程序占着，没复制成。PID = " + text; }
        }

        private void CopyPath()
        {
            PortTable.Endpoint ep = Selected();
            if (ep == null) { return; }
            if (ep.ProcPath.Length == 0) { _status.Text = "这个进程的路径读不到（可能需要以管理员身份运行工具箱）。"; return; }
            try
            {
                Clipboard.SetDataObject(ep.ProcPath, true);
                _status.Text = "已复制路径：" + ep.ProcPath;
            }
            catch { _status.Text = "剪贴板被别的程序占着，没复制成：" + ep.ProcPath; }
        }

        // ------------------------------------------------------------------ 结束进程

        private void KillSelected()
        {
            if (_busy) { return; }
            PortTable.Endpoint ep = Selected();
            if (ep == null) { _status.Text = "先选一行。"; return; }
            if (ep.Protected)
            {
                _status.Text = "这个不能结束：" + ep.ProtectedWhy;
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要结束这个进程"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次结束被拦下了。";
                return;
            }

            StringBuilder body = new StringBuilder();
            body.Append("要结束这个进程吗？").AppendLine();
            body.AppendLine();
            body.Append("  进程：").AppendLine(ep.ProcName);
            body.Append("  PID ：").AppendLine(ep.Pid.ToString(CultureInfo.InvariantCulture));
            body.Append("  端口：").AppendLine(ep.Proto + " " + ep.Port.ToString(CultureInfo.InvariantCulture));
            if (ep.ProcPath.Length > 0) { body.Append("  路径：").AppendLine(ep.ProcPath); }
            body.AppendLine();
            body.AppendLine("⚠ 它开着的东西没保存就会丢，而且不会问你要不要保存 —— 先确认那个程序里没有没保存的东西。");
            using (ConfirmForm f = new ConfirmForm("要结束这个进程吗？", body.ToString(), "结束进程", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            string message;
            bool ok = PortTable.Kill(ep, out message);
            _status.Text = message;
            _status.ForeColor = ok ? _theme.BarText : _theme.Danger;
            Logger.Write("端口与进程", (ok ? "结束进程：" : "结束进程失败：") + ep.ProcName + " PID="
                + ep.Pid.ToString(CultureInfo.InvariantCulture) + " —— " + message);
            Reload();
        }
    }
}
