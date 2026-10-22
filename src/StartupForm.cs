// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「启动项管理」窗口（GAP-05）：把开机自启的东西列出来，勾选 → 禁用；想开回来 → 恢复。
    ///
    /// 三条口径（和后端 `src\StartupItems.cs` 一一对应）：
    /// ① **只禁用、不删除**：注册表那条值会先把名字 / 类型 / 数据记进记录文件再删；
    ///    「启动」文件夹里的文件是**挪到备份目录**。两种都能一键还原；
    /// ② **系统自带组件标灰、不给勾**（命令落在 Windows\System32 里、或者名字是 Defender / SecurityHealth
    ///    那几个）—— 禁了可能没声音、没网、开不了机；
    /// ③ 禁用 / 还原都要过《使用条款》同意门 + 确认框；写完**读回核对**，核对不上就如实说失败。</summary>
    internal sealed class StartupForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly Button _disable = new Button();
        private readonly Button _restore = new Button();
        private readonly Button _refresh = new Button();
        private List<StartupItem> _items = new List<StartupItem>();

        public StartupForm(Theme theme)
        {
            _theme = theme;
            Text = "启动项管理（只禁用、不删除，能还原）";
            ClientSize = new Size(1000, 580);
            MinimumSize = new Size(820, 460);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(940, 0);
            intro.Text = "勾选上面「开着的」那些 → 按「禁用勾选的」：下次开机就不启动了，但一个字节都没有删。"
                + Environment.NewLine
                + "想开回来：选中「已禁用（可恢复）」那一行 → 按「恢复选中的」。灰色的系统自带组件不给动。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("名称", 220);
            _list.Columns.Add("状态", 130);
            _list.Columns.Add("位置", 190);
            _list.Columns.Add("命令 / 路径", 400);
            _list.ItemCheck += delegate(object s, ItemCheckEventArgs e) { BeginInvoke((MethodInvoker)delegate { UpdateButtons(); }); };
            _list.ItemChecked += delegate { UpdateButtons(); };
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            root.Controls.Add(_list, 0, 1);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(940, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 2);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _disable.Text = "禁用勾选的";
            StyleButton(_disable, 120);
            _disable.Click += delegate { DisableChecked(); };
            _restore.Text = "恢复选中的";
            StyleButton(_restore, 108);
            _restore.Click += delegate { RestoreSelected(); };
            _refresh.Text = "刷新";
            StyleButton(_refresh, 80);
            _refresh.Click += delegate { Reload(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_disable, 1, 0);
            buttons.Controls.Add(_restore, 2, 0);
            buttons.Controls.Add(_refresh, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 3);

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

        // ------------------------------------------------------------------ 列

        private void Reload()
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                _items = StartupItems.List();
                foreach (StartupItem it in _items)
                {
                    ListViewItem row = new ListViewItem(it.Name);
                    row.SubItems.Add(it.Enabled ? "开着" : "已禁用（可恢复）");
                    row.SubItems.Add(it.Source);
                    row.SubItems.Add(OneLine(it.Command));
                    row.Tag = it;
                    row.Checked = false;
                    if (!it.Enabled || it.Critical) { row.ForeColor = _theme.Danger; }
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            string err = StartupItems.LastError;
            if (err.Length > 0) { _status.Text = err; }
            else if (_items.Count == 0) { _status.Text = "没读到自启项（注册表 Run 键和「启动」文件夹都是空的）。"; }
            else
            {
                int disabled = 0;
                foreach (StartupItem it in _items) { if (!it.Enabled) { disabled++; } }
                _status.Text = "一共 " + _items.Count.ToString(CultureInfo.InvariantCulture) + " 项，其中 "
                    + disabled.ToString(CultureInfo.InvariantCulture) + " 项是本窗口禁用的（可恢复）。"
                    + "灰色的系统自带组件不给动 —— 禁了可能没声音 / 没网 / 开不了机。";
            }
            UpdateButtons();
            Logger.Write("启动项管理", "读到 " + _items.Count.ToString(CultureInfo.InvariantCulture) + " 项自启");
        }

        private static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            return s.Replace("\r", " ").Replace("\n", " ");
        }

        private void UpdateButtons()
        {
            int checkedCount = 0;
            foreach (ListViewItem row in _list.Items)
            {
                StartupItem it = row.Tag as StartupItem;
                if (it == null) { continue; }
                if (it.Enabled && !it.Critical && row.Checked) { checkedCount++; }
            }
            _disable.Text = checkedCount > 0
                ? "禁用勾选的 " + checkedCount.ToString(CultureInfo.InvariantCulture) + " 项"
                : "禁用勾选的";
            _disable.Enabled = checkedCount > 0;

            bool hasDisabled = false;
            foreach (ListViewItem row in _list.SelectedItems)
            {
                StartupItem it = row.Tag as StartupItem;
                if (it != null && !it.Enabled) { hasDisabled = true; }
            }
            _restore.Enabled = hasDisabled;
        }

        // ------------------------------------------------------------------ 禁用

        private void DisableChecked()
        {
            List<StartupItem> targets = new List<StartupItem>();
            foreach (ListViewItem row in _list.Items)
            {
                StartupItem it = row.Tag as StartupItem;
                if (it != null && it.Enabled && !it.Critical && row.Checked) { targets.Add(it); }
            }
            if (targets.Count == 0) { _status.Text = "先勾选上面那些「开着」的项。"; return; }

            if (!Consent.EnsureAccepted(this, _theme, "要禁用这 " + targets.Count.ToString(CultureInfo.InvariantCulture) + " 个开机自启项"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次没有被改，什么都没动。";
                return;
            }

            System.Text.StringBuilder body = new System.Text.StringBuilder();
            body.AppendLine("下面这些下次开机就不启动了（**只禁用，不删除**，随时可以还原）：");
            body.AppendLine();
            int shown = 0;
            foreach (StartupItem it in targets)
            {
                if (shown >= 12) { body.AppendLine("… 还有 " + (targets.Count - shown).ToString(CultureInfo.InvariantCulture) + " 项"); break; }
                body.AppendLine("· " + it.Name + "（" + it.Source + "）");
                shown++;
            }
            body.AppendLine();
            body.AppendLine("注册表项的原值会记进 startup-disabled.tsv；「启动」文件夹里的文件会挪到备份目录。"
                + "想开回来就在这个窗口里点「恢复选中的」。");
            using (ConfirmForm f = new ConfirmForm("要禁用这些开机自启项吗？", body.ToString(), "禁用", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            Cursor = Cursors.WaitCursor;
            int okCount = 0;
            List<string> fails = new List<string>();
            foreach (StartupItem it in targets)
            {
                string msg;
                bool ok = StartupItems.Disable(it, out msg);
                if (ok) { okCount++; }
                else { fails.Add(it.Name + "：" + msg); }
                Logger.Write("启动项管理", (ok ? "禁用成功：" : "禁用失败：") + it.Name + "（" + msg + "）");
            }
            Cursor = Cursors.Default;

            Reload();
            if (fails.Count == 0)
            {
                _status.Text = "已禁用 " + okCount.ToString(CultureInfo.InvariantCulture) + " 项。下次开机会生效；想开回来点「恢复选中的」。";
            }
            else
            {
                _status.Text = "禁用成功 " + okCount.ToString(CultureInfo.InvariantCulture) + " 项，失败 " + fails.Count.ToString(CultureInfo.InvariantCulture)
                    + " 项：" + string.Join("；", fails.ToArray());
            }
        }

        // ------------------------------------------------------------------ 还原

        private void RestoreSelected()
        {
            StartupItem target = null;
            foreach (ListViewItem row in _list.SelectedItems)
            {
                StartupItem it = row.Tag as StartupItem;
                if (it != null && !it.Enabled) { target = it; break; }
            }
            if (target == null) { _status.Text = "先在列表里选中一行「已禁用（可恢复）」的。"; return; }

            if (!Consent.EnsureAccepted(this, _theme, "要还原自启项「" + target.Name + "」"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次没有被改，什么都没动。";
                return;
            }
            string body = "要把「" + target.Name + "」还原成开机自启吗？" + Environment.NewLine + Environment.NewLine
                + "位置：" + target.Source + Environment.NewLine
                + "命令 / 路径：" + OneLine(target.Command);
            using (ConfirmForm f = new ConfirmForm("要还原这一项吗？", body, "还原", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            Cursor = Cursors.WaitCursor;
            string msg;
            bool ok = StartupItems.Restore(target, out msg);
            Cursor = Cursors.Default;
            Logger.Write("启动项管理", (ok ? "还原成功：" : "还原失败：") + target.Name + "（" + msg + "）");
            Reload();
            _status.Text = msg;
        }
    }
}
