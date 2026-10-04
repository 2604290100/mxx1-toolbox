// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「解除文件占用」的结果窗口。
    ///
    /// 从资源管理器右键点进来的（`Mxx1Toolbox.exe rightmenu unlock "&lt;路径&gt;"`），所以它自己就是
    /// 一个**独立的窗口进程**：不打开主界面，看完关掉就走。
    ///
    /// 界面上只做一件事：把"谁占着它"列清楚，让你勾要结束的程序。所以
    /// ① 系统关键进程列出来但**勾不动**（灰的 + 写明原因），explorer.exe 默认不勾；
    /// ② 结束前过一遍自家的确认窗口（右键菜单是误点高发区）；
    /// ③ 查不到就如实说查不到，并列出可能的原因，绝不谎报「已解除」。</summary>
    internal sealed class UnlockForm : Form
    {
        private readonly string[] _paths;
        private readonly Theme _theme;
        private readonly List<FileLocker> _lockers = new List<FileLocker>();

        private ListView _list;
        private Label _head;
        private Label _status;
        private Label _empty;
        private Label _hint;
        private Button _killBtn;
        private string _queryError = "";

        public UnlockForm(string[] paths)
        {
            _paths = (paths == null) ? new string[0] : paths;
            _theme = Theme.Resolve(Settings.Load().Theme);

            Text = "解除文件占用";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(620, 300);
            BackColor = _theme.FormBack;
            try { Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point); }
            catch { }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.Padding = new Padding(14, 12, 14, 12);
            root.BackColor = _theme.FormBack;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _head = new Label();
            _head.AutoSize = true;
            _head.MaximumSize = new Size(570, 0);
            _head.ForeColor = _theme.InputText;
            _head.Margin = new Padding(2, 0, 2, 8);
            _head.Text = PathsText();
            root.Controls.Add(_head, 0, 0);

            _status = new Label();
            _status.AutoSize = true;
            _status.MaximumSize = new Size(570, 0);
            _status.ForeColor = _theme.BarText;
            _status.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(_status, 0, 1);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.BackColor = _theme.InputBack;
            _list.ForeColor = _theme.InputText;
            _list.Margin = new Padding(2, 0, 2, 8);
            _list.Columns.Add("程序", 190, HorizontalAlignment.Left);
            _list.Columns.Add("PID", 70, HorizontalAlignment.Left);
            _list.Columns.Add("说明", 300, HorizontalAlignment.Left);
            _list.ItemCheck += OnItemCheck;
            root.Controls.Add(_list, 0, 2);

            _empty = new Label();
            _empty.Dock = DockStyle.Fill;
            _empty.AutoSize = false;
            _empty.ForeColor = _theme.BarText;
            _empty.Margin = new Padding(2, 0, 2, 8);
            _empty.Visible = false;
            root.Controls.Add(_empty, 0, 2);

            _hint = new Label();
            _hint.AutoSize = true;
            _hint.MaximumSize = new Size(570, 0);
            _hint.ForeColor = _theme.BarText;
            _hint.Margin = new Padding(2, 0, 2, 10);
            _hint.Text = "勾上要结束的程序，再点「结束选中的进程」。系统关键程序是灰的，勾不动。";
            root.Controls.Add(_hint, 0, 3);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.WrapContents = false;
            bar.Margin = new Padding(0);
            bar.BackColor = _theme.FormBack;
            _killBtn = MakeButton("结束选中的进程");
            _killBtn.Click += delegate { KillChecked(); };
            bar.Controls.Add(_killBtn);
            Button refresh = MakeButton("重新检查");
            refresh.Click += delegate { RefreshLockers(); };
            bar.Controls.Add(refresh);
            Button copy = MakeButton("复制路径");
            copy.Click += delegate { CopyPaths(); };
            bar.Controls.Add(copy);
            Button close = MakeButton("关闭");
            close.Click += delegate { Close(); };
            bar.Controls.Add(close);
            root.Controls.Add(bar, 0, 4);

            Controls.Add(root);
            // 故意不设 AcceptButton：这个窗口里按 Enter 应该是"什么都不做"，
            // 结束进程必须真的去点那个按钮（和 ConfirmForm 里"默认按钮是取消"同一个理由）。
            CancelButton = close;
            Native.ApplyDarkTitleBar(Handle, _theme.DarkMode);

            Shown += delegate
            {
                RefreshLockers();
                Activate();
            };
        }

        private Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(10, 4, 10, 4);
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.BackColor = _theme.ButtonBack;
            b.ForeColor = _theme.ButtonText;
            b.FlatAppearance.BorderColor = _theme.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = _theme.ButtonHover;
            b.FlatAppearance.MouseDownBackColor = _theme.ButtonPressed;
            b.Margin = new Padding(0, 0, 8, 0);
            return b;
        }

        private string PathsText()
        {
            if (_paths.Length == 0) { return "（没有给出路径）"; }
            if (_paths.Length == 1) { return _paths[0]; }
            StringBuilder sb = new StringBuilder();
            sb.Append(_paths.Length.ToString(CultureInfo.InvariantCulture)).Append(" 个路径：");
            int shown = 0;
            foreach (string p in _paths)
            {
                if (shown >= 4) { sb.Append(" 等").Append(_paths.Length.ToString(CultureInfo.InvariantCulture)).Append(" 个"); break; }
                if (shown > 0) { sb.Append("；"); }
                sb.Append(p);
                shown++;
            }
            return sb.ToString();
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            // 系统关键进程勾不动：把这次改动拨回去（ListView 允许在事件里改回去）。
            if (e.Index >= 0 && e.Index < _lockers.Count && _lockers[e.Index].Protected)
            {
                e.NewValue = e.CurrentValue;
            }
        }

        private void RefreshLockers()
        {
            _lockers.Clear();
            _queryError = "";
            List<string> errors = new List<string>();
            List<int> seen = new List<int>();
            foreach (string p in _paths)
            {
                string error;
                List<FileLocker> found = FileLock.WhoLocks(p, out error);
                if (error.Length > 0 && !errors.Contains(error)) { errors.Add(error); }
                foreach (FileLocker f in found)
                {
                    if (seen.Contains(f.Pid)) { continue; }
                    seen.Add(f.Pid);
                    _lockers.Add(f);
                }
            }
            if (errors.Count > 0) { _queryError = string.Join("；", errors.ToArray()); }

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (FileLocker f in _lockers)
            {
                ListViewItem it = new ListViewItem(f.Exe);
                it.SubItems.Add(f.Pid.ToString(CultureInfo.InvariantCulture));
                it.SubItems.Add(Explain(f));
                it.Checked = f.Checked && !f.Protected;
                if (f.Protected) { it.ForeColor = _theme.ButtonDisabledText; }
                _list.Items.Add(it);
            }
            _list.EndUpdate();

            bool any = _lockers.Count > 0;
            _list.Visible = any;
            _empty.Visible = !any;
            if (any)
            {
                _status.Text = "查到 " + _lockers.Count.ToString(CultureInfo.InvariantCulture)
                    + " 个程序正在占用它：";
                if (_queryError.Length > 0) { _status.Text += Environment.NewLine + "（注意：" + _queryError + "）"; }
            }
            else
            {
                _status.Text = "没查到占用它的程序。";
                _empty.Text = (_queryError.Length > 0)
                    ? ("这次查询本身没成功：" + _queryError + Environment.NewLine + Environment.NewLine + NotFoundText())
                    : NotFoundText();
            }
            _killBtn.Enabled = true;
            UpdateSize();
        }

        private static string Explain(FileLocker f)
        {
            // 系统给的是"友好显示名"（Windows PowerShell / Windows 资源管理器 / Windows Event Log），
            // 不是路径；进程名（pwsh.exe）是我们自己按 PID 反查的。两个都放，但别写成
            // 「资源管理器 · Windows 资源管理器」这种重复。
            List<string> parts = new List<string>();
            string bare = f.Exe;
            if (bare.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { bare = bare.Substring(0, bare.Length - 4); }
            bool dup = (f.Kind.Length > 0 && f.AppName.IndexOf(f.Kind, StringComparison.Ordinal) >= 0)
                || string.Equals(f.AppName, bare, StringComparison.OrdinalIgnoreCase);
            if (f.AppName.Length > 0 && !dup) { parts.Add(f.AppName); }
            if (f.Kind.Length > 0) { parts.Add(f.Kind); }
            if (f.Why.Length > 0) { parts.Add(f.Why); }
            return string.Join(" · ", parts.ToArray());
        }

        private static string NotFoundText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("可能的原因（按常见程度排）：");
            sb.AppendLine("  · 占用它的程序在别的用户或更高权限下运行 —— 用管理员身份再试一次（工具箱主界面里");
            sb.AppendLine("    按住 Shift 点按钮就是提权运行），没提权时系统不给看别人的进程；");
            sb.AppendLine("  · 文件其实没被占用：拦住你的是只读属性或者权限（ACL），不是「哪个程序开着它」；");
            sb.AppendLine("  · 占用来自内核态（杀毒软件的实时扫描、驱动），它不属于任何一个进程，所以查不到；");
            sb.AppendLine("  · 文件夹被占用：要么它被某个程序当成「当前目录」，要么里面某个文件正被打开。");
            return sb.ToString().TrimEnd();
        }

        private void UpdateSize()
        {
            int rows = _lockers.Count;
            if (rows > 8) { rows = 8; }
            int h = 210 + rows * 20;
            if (h > 470) { h = 470; }
            ClientSize = new Size(620, h);
        }

        private void CopyPaths()
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, _paths));
                _status.Text = "路径已经复制到剪贴板。";
            }
            catch (Exception ex)
            {
                _status.Text = "复制不了：" + ex.Message;
            }
        }

        private void KillChecked()
        {
            List<FileLocker> chosen = new List<FileLocker>();
            foreach (int i in _list.CheckedIndices)
            {
                if (i >= 0 && i < _lockers.Count) { chosen.Add(_lockers[i]); }
            }
            if (chosen.Count == 0)
            {
                _status.Text = "没勾选任何程序 —— 在上面的列表里勾一个再点这个按钮。";
                return;
            }

            StringBuilder names = new StringBuilder();
            foreach (FileLocker f in chosen)
            {
                if (names.Length > 0) { names.Append("、"); }
                names.Append(f.Exe).Append("（PID ").Append(f.Pid.ToString(CultureInfo.InvariantCulture)).Append("）");
            }

            ToolItem t = new ToolItem();
            t.Name = "结束选中的进程";
            t.Id = "rightmenu.unlock.kill";
            t.Danger = true;
            t.Hint = "结束 " + names.ToString() + " 之后，文件就不再被它们占着了。"
                + Environment.NewLine + "没保存的东西会丢 —— 结束之前先回那个程序里存一下。";
            DialogResult answer;
            using (ConfirmForm f = new ConfirmForm(t, "", false, false, _theme))
            {
                answer = f.ShowDialog(this);
            }
            if (answer != DialogResult.OK)
            {
                _status.Text = "已取消（没有结束任何程序）。";
                return;
            }

            string report = FileLock.Kill(chosen);
            Logger.Write("解除文件占用", "结束进程：" + names.ToString() + Environment.NewLine + report);
            RefreshLockers();
            _status.Text = "结束的结果：" + Environment.NewLine + report;
        }
    }
}
