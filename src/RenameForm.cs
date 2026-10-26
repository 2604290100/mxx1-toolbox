// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「批量重命名」窗口：选一个文件夹 → 写规则 → 点「预览」看新旧对照 → 点「执行改名」。
    ///
    /// 最重要的一条：**预览和执行走的是同一份引擎**（`src\RenameRules.cs`）——
    /// 预览里看到什么，执行就改成什么；有冲突就整批拒绝（绝不覆盖、绝不先改一半）。
    /// 规则顺序（替换 → 序号 → 前缀 / 后缀 → 扩展名大小写）和占位符说明都写在那个文件里。
    ///
    /// 改名之前先把「从哪改到哪」写进 `%LOCALAPPDATA%\mxx1-toolbox\rename-<时间>.json`；
    /// 写不进记录就一个都不改（没有记录 = 不能撤销 = 不许动）。「撤销上一次」按那份记录倒着改回来。</summary>
    internal sealed class RenameForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _dir = new TextBox();
        private readonly TextBox _prefix = new TextBox();
        private readonly TextBox _suffix = new TextBox();
        private readonly TextBox _find = new TextBox();
        private readonly TextBox _replace = new TextBox();
        private readonly TextBox _filter = new TextBox();
        private readonly NumericUpDown _indexStart = new NumericUpDown();
        private readonly NumericUpDown _indexDigits = new NumericUpDown();
        private readonly ComboBox _position = new ComboBox();
        private readonly ComboBox _extCase = new ComboBox();
        private readonly CheckBox _regex = new CheckBox();
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly Button _browse = new Button();
        private readonly Button _preview = new Button();
        private readonly Button _apply = new Button();
        private readonly Button _undo = new Button();

        private RenameRules.PlanResult _plan;

        public RenameForm(Theme theme)
        {
            _theme = theme;
            Text = "批量重命名（先预览，再执行）";
            ClientSize = new Size(940, 640);
            MinimumSize = new Size(800, 520);
            MinimizeBox = true;   // 最小化按钮：点它把主窗口一起收进任务栏（2026-10-26 主副窗口关联）
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(890, 0);
            intro.Text = "规则按这个顺序叠：① 查找替换（或正则替换）→ ② 序号 → ③ 前缀 / 后缀 → ④ 扩展名大小写。"
                + "前缀和后缀里可以用两个占位符：{n} = 序号、{date} = 文件的修改日期（yyyyMMdd）。"
                + Environment.NewLine + "文件夹那一行可以直接把路径粘进去、回车就加载（「浏览…」选完也会马上列出文件）。"
                + "**有冲突就整批拒绝**（目标名字已存在 / 两个文件撞名 / 名字里有 Windows 不允许的字符），绝不覆盖。"
                + " 改名之前会先在 %LOCALAPPDATA%\\mxx1-toolbox 里留一份撤销记录，写不进记录就一个都不改。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel dirRow = new TableLayoutPanel();
            dirRow.Dock = DockStyle.Fill;
            dirRow.AutoSize = true;
            dirRow.ColumnCount = 3;
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label cap = new Label();
            cap.AutoSize = true;
            cap.Text = "要改名的文件夹：";
            cap.Margin = new Padding(2, 7, 4, 0);
            dirRow.Controls.Add(cap, 0, 0);
            _dir.Dock = DockStyle.Fill;
            _dir.Margin = new Padding(2, 3, 6, 3);
            _dir.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Preview();   // 路径粘进来、回车 = 直接读这个文件夹
                }
            };
            dirRow.Controls.Add(_dir, 1, 0);
            _browse.Text = "浏览…";
            StyleButton(_browse, 84);
            _browse.Click += delegate { PickFolder(); };
            dirRow.Controls.Add(_browse, 2, 0);
            root.Controls.Add(dirRow, 0, 1);

            TableLayoutPanel rules = new TableLayoutPanel();
            rules.Dock = DockStyle.Fill;
            rules.AutoSize = true;
            rules.ColumnCount = 4;
            rules.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            rules.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int i = 0; i < 5; i++) { rules.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            rules.Margin = new Padding(2, 0, 2, 6);

            rules.Controls.Add(RuleLabel("前缀："), 0, 0);
            _prefix.Dock = DockStyle.Fill;
            _prefix.Margin = new Padding(2, 3, 12, 3);
            rules.Controls.Add(_prefix, 1, 0);
            rules.Controls.Add(RuleLabel("后缀："), 2, 0);
            _suffix.Dock = DockStyle.Fill;
            _suffix.Margin = new Padding(2, 3, 2, 3);
            rules.Controls.Add(_suffix, 3, 0);

            rules.Controls.Add(RuleLabel("查找："), 0, 1);
            _find.Dock = DockStyle.Fill;
            _find.Margin = new Padding(2, 3, 12, 3);
            rules.Controls.Add(_find, 1, 1);
            rules.Controls.Add(RuleLabel("替换为："), 2, 1);
            _replace.Dock = DockStyle.Fill;
            _replace.Margin = new Padding(2, 3, 2, 3);
            rules.Controls.Add(_replace, 3, 1);

            rules.Controls.Add(RuleLabel("起始序号："), 0, 2);
            _indexStart.Minimum = 0;
            _indexStart.Maximum = 999999;
            _indexStart.Value = 1;
            _indexStart.Width = 90;
            _indexStart.Margin = new Padding(2, 3, 12, 3);
            rules.Controls.Add(_indexStart, 1, 2);
            rules.Controls.Add(RuleLabel("序号位数（0=不加）："), 2, 2);
            _indexDigits.Minimum = 0;
            _indexDigits.Maximum = 12;
            _indexDigits.Value = 0;
            _indexDigits.Width = 70;
            _indexDigits.Margin = new Padding(2, 3, 2, 3);
            rules.Controls.Add(_indexDigits, 3, 2);

            rules.Controls.Add(RuleLabel("序号位置："), 0, 3);
            Prepare(_position, 150);
            _position.Items.Add("名字后面");
            _position.Items.Add("名字前面");
            _position.SelectedIndex = 0;
            _position.Margin = new Padding(2, 3, 12, 3);
            rules.Controls.Add(_position, 1, 3);
            rules.Controls.Add(RuleLabel("扩展名："), 2, 3);
            Prepare(_extCase, 150);
            _extCase.Items.Add("不变");
            _extCase.Items.Add("全小写");
            _extCase.Items.Add("全大写");
            _extCase.SelectedIndex = 0;
            rules.Controls.Add(_extCase, 3, 3);

            rules.Controls.Add(RuleLabel("文件筛选："), 0, 4);
            _filter.Text = "*";
            _filter.Dock = DockStyle.Fill;
            _filter.Margin = new Padding(2, 3, 12, 3);
            rules.Controls.Add(_filter, 1, 4);
            _regex.Text = "查找用正则表达式（.NET 语法）";
            _regex.AutoSize = true;
            _regex.Margin = new Padding(2, 6, 2, 3);
            rules.Controls.Add(_regex, 2, 4);
            root.Controls.Add(rules, 0, 2);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("原名", 330);
            _list.Columns.Add("新名", 330);
            _list.Columns.Add("说明", 240);
            root.Controls.Add(_list, 0, 3);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(890, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 4);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _preview.Text = "预览";
            StyleButton(_preview, 92);
            _preview.Click += delegate { Preview(); };
            _apply.Text = "执行改名";
            StyleButton(_apply, 120);
            _apply.Click += delegate { ApplyNow(); };
            _undo.Text = "撤销上一次";
            StyleButton(_undo, 116);
            _undo.Click += delegate { UndoNow(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_preview, 1, 0);
            buttons.Controls.Add(_apply, 2, 0);
            buttons.Controls.Add(_undo, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 5);

            Controls.Add(root);
            CancelButton = close;
            ApplyTheme(theme);
            Load += delegate
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (desktop.Length > 0) { _dir.Text = desktop; }
                _status.Text = "先选文件夹、写规则，点「预览」看对照；确认没问题再点「执行改名」。";
                UpdateButtons();
            };
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

        private static Label RuleLabel(string text)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = text;
            l.Margin = new Padding(2, 7, 4, 0);
            return l;
        }

        private void Prepare(ComboBox box, int width)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Width = width;
            box.Font = Font;
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
                ComboBox cb = c as ComboBox;
                if (cb != null)
                {
                    cb.BackColor = theme.InputBack;
                    cb.ForeColor = theme.InputText;
                }
                NumericUpDown n = c as NumericUpDown;
                if (n != null)
                {
                    n.BackColor = theme.InputBack;
                    n.ForeColor = theme.InputText;
                }
                CheckBox ck = c as CheckBox;
                if (ck != null) { ck.ForeColor = theme.BarText; }
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

        private void PickFolder()
        {
            string picked = FolderPicker.Pick(this,
                "选一个要批量改名的文件夹（只改这一层，不进子文件夹；也可以把路径粘到上面的输入框、回车）",
                _dir.Text.Trim());
            if (picked == null) { return; }
            _dir.Text = picked;
            Preview();   // 选完马上把文件列出来（不用再点一次「预览」）
        }

        private RenameRules.Rule RuleFromUi()
        {
            RenameRules.Rule r = new RenameRules.Rule();
            r.Prefix = _prefix.Text;
            r.Suffix = _suffix.Text;
            r.Find = _find.Text;
            r.Replace = _replace.Text;
            r.UseRegex = _regex.Checked;
            r.IndexStart = (int)_indexStart.Value;
            r.IndexDigits = (int)_indexDigits.Value;
            r.IndexAfter = _position.SelectedIndex != 1;
            if (_extCase.SelectedIndex == 1) { r.ExtCase = "lower"; }
            if (_extCase.SelectedIndex == 2) { r.ExtCase = "upper"; }
            return r;
        }

        // ------------------------------------------------------------------ 预览

        private void Preview()
        {
            string dir = _dir.Text.Trim();
            if (dir.Length == 0) { _status.Text = "先选一个文件夹。"; return; }
            if (!Directory.Exists(dir)) { _status.Text = "这个文件夹不存在：" + dir; return; }

            _plan = RenameRules.Plan(dir, _filter.Text.Trim(), RuleFromUi(), null);
            if (_plan.Error.Length > 0)
            {
                _status.Text = _plan.Error;
                _status.ForeColor = _theme.Danger;
                _list.Items.Clear();
                UpdateButtons();
                return;
            }

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (RenameRules.PlanItem it in _plan.Items)
                {
                    ListViewItem row = new ListViewItem(it.OldName);
                    row.SubItems.Add(it.Conflict.Length > 0 ? "（不改）" : it.NewName);
                    row.SubItems.Add(it.Conflict.Length > 0 ? it.Conflict : (it.Changed ? "会改名" : "名字没变"));
                    if (it.Conflict.Length > 0) { row.ForeColor = _theme.Danger; }
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            _status.ForeColor = _theme.BarText;
            if (_plan.Conflicts > 0)
            {
                _status.Text = "有 " + _plan.Conflicts.ToString(CultureInfo.InvariantCulture)
                    + " 处冲突 —— 一个都不会改。把冲突处理掉再点「预览」。";
                _status.ForeColor = _theme.Danger;
            }
            else
            {
                _status.Text = "共 " + _plan.Items.Count.ToString(CultureInfo.InvariantCulture) + " 个文件，会改 "
                    + _plan.Changed.ToString(CultureInfo.InvariantCulture) + " 个，没有冲突。确认后点「执行改名」。";
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _undo.Enabled = RenameRules.LastUndoFile().Length > 0;
            _apply.Enabled = _plan != null && _plan.Error.Length == 0 && _plan.Conflicts == 0 && _plan.Changed > 0;
        }

        // ------------------------------------------------------------------ 执行

        private void ApplyNow()
        {
            if (_plan == null) { Preview(); }
            if (_plan == null || _plan.Error.Length > 0) { return; }
            if (_plan.Conflicts > 0)
            {
                _status.Text = "有冲突，一个都不会改。";
                _status.ForeColor = _theme.Danger;
                return;
            }
            if (_plan.Changed == 0) { _status.Text = "按当前规则，所有文件的名字都不变，没什么可改的。"; return; }

            if (!Consent.EnsureAccepted(this, _theme, "要给这些文件改名"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次改名被拦下了。";
                return;
            }

            StringBuilder body = new StringBuilder();
            body.Append("会把 ").Append(_plan.Changed.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" 个文件改名（只改名字，不移动、不改内容）：");
            body.AppendLine();
            int shown = 0;
            foreach (RenameRules.PlanItem it in _plan.Items)
            {
                if (!it.Changed) { continue; }
                if (shown >= 12) { body.AppendLine("… 还有 " + (_plan.Changed - shown).ToString(CultureInfo.InvariantCulture) + " 个"); break; }
                body.Append("  · ").AppendLine(it.OldName + "  →  " + it.NewName);
                shown++;
            }
            body.AppendLine();
            body.AppendLine("改之前会先写一份撤销记录；改完想反悔，点「撤销上一次改名」就行。");
            using (ConfirmForm f = new ConfirmForm("要执行这次批量改名吗？", body.ToString(), "执行改名", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            RenameRules.ApplyResult res = RenameRules.Apply(_plan);
            if (res.Error.Length > 0)
            {
                _status.Text = res.Error;
                _status.ForeColor = _theme.Danger;
                Logger.Write("批量重命名", "未执行：" + res.Error);
            }
            else
            {
                _status.Text = res.Report();
                _status.ForeColor = res.Failed > 0 ? _theme.Danger : _theme.BarText;
                Logger.Write("批量重命名", res.Report());
            }
            _plan = null;
            _list.Items.Clear();
            UpdateButtons();
            if (res.Error.Length == 0)
            {
                // 改完再预览一次：显示改后的名字（这时规则再套一遍会二次改名，所以只刷新状态）
                _status.Text = res.Report() + Environment.NewLine + "（列表已清空，想看别的规则重新点「预览」。）";
            }
        }

        private void UndoNow()
        {
            string file = RenameRules.LastUndoFile();
            if (file.Length == 0)
            {
                _status.Text = "还没有改名记录可以撤销。";
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要撤销上一次批量改名"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次撤销被拦下了。";
                return;
            }
            StringBuilder body = new StringBuilder();
            body.AppendLine("会按这份记录，把上一次改过的名字倒着改回去：");
            body.AppendLine();
            body.AppendLine("  " + file);
            body.AppendLine();
            body.AppendLine("记录里成过的那几条才撤；原来的名字又被别的文件占了、或者要撤的那份已经不在了，那一条会跳过并说明原因。");
            using (ConfirmForm f = new ConfirmForm("要撤销上一次改名吗？", body.ToString(), "撤销", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            RenameRules.UndoResult res = RenameRules.Undo(file);
            if (res.Error.Length > 0)
            {
                _status.Text = res.Error;
                _status.ForeColor = _theme.Danger;
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("撤销完成：改回来 ").Append(res.Done.ToString(CultureInfo.InvariantCulture)).Append(" 个");
            if (res.Failed > 0) { sb.Append("，失败 ").Append(res.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 个"); }
            sb.Append("。");
            foreach (string why in res.FailedWhy) { sb.AppendLine().Append("  × ").Append(why); }
            _status.Text = sb.ToString();
            _status.ForeColor = res.Failed > 0 ? _theme.Danger : _theme.BarText;
            Logger.Write("批量重命名", sb.ToString());
            _plan = null;
            _list.Items.Clear();
            UpdateButtons();
        }
    }
}
