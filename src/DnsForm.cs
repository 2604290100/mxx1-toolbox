// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「一键换 DNS」窗口（GAP-09）：选一块网卡 → 三选一（114 / 阿里 / 改回自动）→ 应用。
    ///
    /// 三条口径（和后端 `src\DnsConfig.cs` 一一对应）：
    /// ① 只改 **DNS**，不改 IP / 网关 / 代理；每一块网卡都是独立的，选哪块改哪块；
    /// ② 三个选项里有 **「改回自动（DHCP）」** 兜底 —— 改完觉得更慢，点一下就回原样；
    /// ③ 点「应用」要过《使用条款》同意门 + 确认框，要管理员；写完**读回核对**，
    ///    核对不上就如实说失败（绝不把"命令发出去了"说成"改好了"）。</summary>
    internal sealed class DnsForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly RadioButton _r114 = new RadioButton();
        private readonly RadioButton _rAli = new RadioButton();
        private readonly RadioButton _rDhcp = new RadioButton();
        private readonly Button _apply = new Button();
        private readonly Button _refresh = new Button();

        private volatile bool _busy = false;
        private List<DnsAdapter> _adapters = new List<DnsAdapter>();

        public DnsForm(Theme theme)
        {
            _theme = theme;
            Text = "一键换 DNS（114 / 阿里 / 改回自动）";
            ClientSize = new Size(900, 560);
            MinimumSize = new Size(760, 460);
            MinimizeBox = true;   // 最小化按钮：点它把主窗口一起收进任务栏（2026-10-26 主副窗口关联）
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
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(850, 0);
            intro.Text = "网页打不开、但 QQ / 微信能用，多半是 DNS 的事。先在上面选一块正在用的网卡，再在下面三选一。"
                + Environment.NewLine
                + "只改 DNS，不动 IP / 网关 / 代理；改完觉得更慢，选「改回自动」就还原了。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("网卡", 240);
            _list.Columns.Add("状态", 80);
            _list.Columns.Add("现在的 DNS", 260);
            _list.Columns.Add("说明", 240);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            root.Controls.Add(_list, 0, 1);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(850, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 2);

            TableLayoutPanel choices = new TableLayoutPanel();
            choices.Dock = DockStyle.Fill;
            choices.AutoSize = true;
            choices.ColumnCount = 3;
            for (int i = 0; i < 3; i++) { choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f)); }
            _r114.Text = "114 DNS";
            _rAli.Text = "阿里 DNS（223.5.5.5）";
            _rDhcp.Text = "改回自动（还原）";
            _rDhcp.Checked = true;
            foreach (RadioButton r in new RadioButton[] { _r114, _rAli, _rDhcp })
            {
                r.AutoSize = true;
                r.Margin = new Padding(2, 2, 12, 6);
                r.CheckedChanged += delegate { UpdateButtons(); };
                choices.Controls.Add(r);
            }
            choices.Controls.Add(_r114, 0, 0);
            choices.Controls.Add(_rAli, 1, 0);
            choices.Controls.Add(_rDhcp, 2, 0);
            root.Controls.Add(choices, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 4;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 3; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _apply.Text = "应用这个 DNS";
            StyleButton(_apply, 130);
            _apply.Click += delegate { Apply(); };
            _refresh.Text = "刷新";
            StyleButton(_refresh, 84);
            _refresh.Click += delegate { Reload(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_refresh, 1, 0);
            buttons.Controls.Add(_apply, 2, 0);
            buttons.Controls.Add(close, 3, 0);
            root.Controls.Add(buttons, 0, 4);

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
                RadioButton r = c as RadioButton;
                if (r != null) { r.ForeColor = theme.BarText; }
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

        // ------------------------------------------------------------------ 读

        private void Reload()
        {
            if (_busy) { return; }
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                _adapters = DnsConfig.List();
                foreach (DnsAdapter a in _adapters)
                {
                    ListViewItem row = new ListViewItem(a.Name);
                    row.SubItems.Add(a.IsUp ? "已连接" : "没连接");
                    row.SubItems.Add(a.Dns.Count > 0 ? string.Join(" / ", a.Dns.ToArray()) : "（自动获取）");
                    row.SubItems.Add(a.Description);
                    row.Tag = a;
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            if (_list.Items.Count > 0) { _list.Items[0].Selected = true; }
            string err = DnsConfig.LastError;
            if (err.Length > 0)
            {
                _status.Text = err;
            }
            else if (_adapters.Count == 0)
            {
                _status.Text = "一块网卡都没读到（这台机器可能没有网卡，或者网络组件被精简了）。";
            }
            else
            {
                _status.Text = "读到 " + _adapters.Count.ToString(CultureInfo.InvariantCulture)
                    + " 块网卡。选一块，再点下面三个选项之一，然后按「应用这个 DNS」。";
            }
            UpdateButtons();
            Logger.Write("一键换 DNS", "读到 " + _adapters.Count.ToString(CultureInfo.InvariantCulture) + " 块网卡");
        }

        private void UpdateButtons()
        {
            _apply.Enabled = !_busy && (_list.SelectedItems.Count == 1);
        }

        // ------------------------------------------------------------------ 写

        private string SelectedChoice()
        {
            if (_r114.Checked) { return DnsConfig.Choice114; }
            if (_rAli.Checked) { return DnsConfig.ChoiceAli; }
            return DnsConfig.ChoiceDhcp;
        }

        private void Apply()
        {
            if (_busy) { return; }
            if (_list.SelectedItems.Count != 1) { _status.Text = "先在列表里选一块网卡。"; return; }
            DnsAdapter a = _list.SelectedItems[0].Tag as DnsAdapter;
            if (a == null) { return; }
            string choice = SelectedChoice();
            string servers = DnsConfig.TextOf(choice);

            if (!Launcher.IsAdmin())
            {
                OfferElevation("应用这个 DNS");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要把「" + a.Name + "」的 DNS 改成 " + servers))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次改动被拦下了，什么都没改。";
                return;
            }

            string body = "网卡：" + a.Name + Environment.NewLine
                + "现在：" + (a.Dns.Count > 0 ? string.Join(" / ", a.Dns.ToArray()) : "（自动获取）") + Environment.NewLine
                + "改成：" + servers + Environment.NewLine + Environment.NewLine
                + "只改 DNS，不动 IP / 网关 / 代理。改完不对，可以再选「改回自动（还原）」。";
            using (ConfirmForm f = new ConfirmForm("要改这块网卡的 DNS 吗？", body, "改 DNS", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusy(true, "正在改 " + a.Name + " 的 DNS …");
            Logger.Write("一键换 DNS", "改 " + a.Name + " → " + servers);
            string name = a.Name;
            Thread t = new Thread(delegate ()
            {
                string message;
                bool ok = DnsConfig.Apply(name, choice, out message);
                try { BeginInvoke((MethodInvoker)delegate { Finish(ok, message); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Finish(bool ok, string message)
        {
            if (IsDisposed) { return; }
            SetBusy(false, "");
            _status.Text = message;
            Logger.Write("一键换 DNS", (ok ? "成功：" : "失败：") + message + "（命令：" + DnsConfig.LastCommand + "）");
            Reload();
        }

        private void SetBusy(bool busy, string text)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _refresh.Enabled = !busy;
            _r114.Enabled = !busy;
            _rAli.Enabled = !busy;
            _rDhcp.Enabled = !busy;
            if (text.Length > 0) { _status.Text = text; }
            UpdateButtons();
        }

        /// <summary>不是管理员：问一句，然后带 UAC 重开这个窗口（重开之后要再点一次「应用」，绝不偷偷改）。</summary>
        private void OfferElevation(string what)
        {
            string body = "改 DNS 要用管理员权限才行。" + Environment.NewLine + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开。"
                + "因为改网络设置只能由你在窗口里点出来，所以重开之后要再点一次「" + what + "」才会真的生效。";
            using (ConfirmForm f = new ConfirmForm("需要管理员权限", body, "确定", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath, "dns");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("一键换 DNS", "不是管理员：已请求以管理员身份重开窗口");
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = "请求管理员权限失败：" + ex.Message;
                Logger.Write("一键换 DNS", "请求管理员权限失败：" + ex.Message);
            }
        }
    }
}
