// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「空文件夹清理」窗口：选一个文件夹，往下找出**真正空的**文件夹（有一个文件就不算空），
    /// 勾选后删到回收站。
    ///
    /// 列表按**由深到浅**排（最里面那层先列）：删除也必须按这个顺序，不然父目录先没了，
    /// 子目录那一条就变成"路径不存在"。判定口径见 `src\EmptyFolders.cs`：
    /// 系统目录 / junction / 没权限的目录都只列进"跳过"那一块，说清为什么没动它。
    ///
    /// 删除前过《使用条款》同意门 + 确认框；只进回收站，不永久删。</summary>
    internal sealed class EmptyFoldersForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _root = new TextBox();
        private readonly ListView _list = new ListView();
        private readonly TextBox _skipped = new TextBox();
        private readonly Label _status = new Label();
        private readonly Button _browse = new Button();
        private readonly Button _scan = new Button();
        private readonly Button _checkAll = new Button();
        private readonly Button _uncheck = new Button();
        private readonly Button _delete = new Button();

        private volatile bool _busy = false;
        private EmptyFolders.ScanResult _result;

        public EmptyFoldersForm(Theme theme)
        {
            _theme = theme;
            Text = "空文件夹清理（删到回收站）";
            ClientSize = new Size(900, 620);
            MinimumSize = new Size(760, 500);
            MinimizeBox = false;
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
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(850, 0);
            intro.Text = "从一个文件夹往下找**真正空的**文件夹（里面一个文件都没有；只有空子文件夹也算空，会一起列出来）。"
                + "隐藏文件也算文件 —— 有 desktop.ini 的文件夹不会出现在列表里。"
                + Environment.NewLine + "系统目录（Windows / Program Files / 回收站…）、junction / 符号链接、没权限读的目录都不会动，"
                + "单独列在下面「跳过」那一块。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel pathRow = new TableLayoutPanel();
            pathRow.Dock = DockStyle.Fill;
            pathRow.AutoSize = true;
            pathRow.ColumnCount = 4;
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label cap = new Label();
            cap.AutoSize = true;
            cap.Text = "要扫的文件夹：";
            cap.TextAlign = ContentAlignment.MiddleLeft;
            cap.Margin = new Padding(2, 7, 4, 0);
            pathRow.Controls.Add(cap, 0, 0);
            _root.Dock = DockStyle.Fill;
            _root.Margin = new Padding(2, 3, 6, 3);
            pathRow.Controls.Add(_root, 1, 0);
            _browse.Text = "浏览…";
            StyleButton(_browse, 84);
            _browse.Click += delegate { PickFolder(); };
            pathRow.Controls.Add(_browse, 2, 0);
            _scan.Text = "扫描";
            StyleButton(_scan, 84);
            _scan.Click += delegate { Scan(); };
            pathRow.Controls.Add(_scan, 3, 0);
            root.Controls.Add(pathRow, 0, 1);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.CheckBoxes = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("空文件夹（由深到浅）", 720);
            _list.Columns.Add("深度", 60);
            _list.ItemChecked += delegate { UpdateButtons(); };
            root.Controls.Add(_list, 0, 2);

            _skipped.Multiline = true;
            _skipped.ReadOnly = true;
            _skipped.ScrollBars = ScrollBars.Vertical;
            _skipped.WordWrap = false;
            _skipped.Dock = DockStyle.Fill;
            _skipped.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_skipped, 0, 3);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(850, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 4);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 6;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 5; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _checkAll.Text = "全选";
            StyleButton(_checkAll, 76);
            _checkAll.Click += delegate { CheckAll(true); };
            _uncheck.Text = "清除勾选";
            StyleButton(_uncheck, 92);
            _uncheck.Click += delegate { CheckAll(false); };
            _delete.Text = "删除选中的空文件夹（回收站）";
            StyleButton(_delete, 230);
            _delete.Click += delegate { DeleteChecked(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_checkAll, 1, 0);
            buttons.Controls.Add(_uncheck, 2, 0);
            buttons.Controls.Add(_delete, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 5);

            Controls.Add(root);
            CancelButton = close;
            ApplyTheme(theme);
            Load += delegate
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (desktop.Length > 0) { _root.Text = desktop; }
                _status.Text = "先选一个文件夹（比如下载目录），再点「扫描」。这一步只读，不会改任何东西。";
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
                    t.BackColor = t.ReadOnly ? theme.LogBack : theme.InputBack;
                    t.ForeColor = t.ReadOnly ? theme.LogText : theme.InputText;
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

        private void PickFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选一个要检查空文件夹的目录（只读扫描，不会改任何东西）";
                dlg.ShowNewFolderButton = false;
                if (_root.Text.Trim().Length > 0 && Directory.Exists(_root.Text.Trim()))
                {
                    dlg.SelectedPath = _root.Text.Trim();
                }
                if (dlg.ShowDialog(this) == DialogResult.OK) { _root.Text = dlg.SelectedPath; }
            }
        }

        private void Scan()
        {
            if (_busy) { return; }
            string dir = _root.Text.Trim();
            if (dir.Length == 0) { _status.Text = "先选一个文件夹。"; return; }
            if (!Directory.Exists(dir)) { _status.Text = "这个文件夹不存在：" + dir; return; }

            SetBusy(true, "正在往下找空文件夹…（目录很多时要一会儿）");
            _list.Items.Clear();
            _skipped.Text = "";
            _result = null;
            Thread t = new Thread(delegate ()
            {
                EmptyFolders.ScanResult res = EmptyFolders.Scan(dir, 32);
                try { BeginInvoke((MethodInvoker)delegate { Fill(res); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Fill(EmptyFolders.ScanResult res)
        {
            if (IsDisposed) { return; }
            _result = res;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (string p in res.Empty)
                {
                    ListViewItem row = new ListViewItem(p);
                    row.SubItems.Add(DepthOf(p, res.Root).ToString(CultureInfo.InvariantCulture));
                    row.Tag = p;
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            StringBuilder skip = new StringBuilder();
            if (res.Skipped.Count == 0) { skip.Append("没有跳过任何目录。"); }
            else
            {
                skip.Append("跳过 / 没动的 ").Append(res.Skipped.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个目录：");
                int shown = 0;
                for (int i = 0; i < res.Skipped.Count && shown < 200; i++, shown++)
                {
                    skip.Append("  · ").Append(res.Skipped[i]).Append("    —— ").AppendLine(res.SkippedWhy[i]);
                }
                if (res.Skipped.Count > shown) { skip.AppendLine("  … 还有 " + (res.Skipped.Count - shown).ToString(CultureInfo.InvariantCulture) + " 条，见运行日志"); }
            }
            _skipped.Text = skip.ToString();

            SetBusy(false, "");
            _status.Text = "共看了 " + res.Dirs.ToString(CultureInfo.InvariantCulture) + " 个目录，找到 "
                + res.Empty.Count.ToString(CultureInfo.InvariantCulture) + " 个空文件夹（由深到浅列出）。"
                + " 勾选之后点「删除选中的空文件夹」才真删（进回收站）。";
            Logger.Write("空文件夹清理", "扫描 " + res.Root + "：看了 " + res.Dirs.ToString(CultureInfo.InvariantCulture)
                + " 个目录，空文件夹 " + res.Empty.Count.ToString(CultureInfo.InvariantCulture)
                + " 个，跳过 " + res.Skipped.Count.ToString(CultureInfo.InvariantCulture) + " 个");
            UpdateButtons();
            if (res.Empty.Count == 0) { _status.Text += "（一个都没找到 —— 这个目录很干净。）"; }
        }

        private static int DepthOf(string path, string root)
        {
            try
            {
                string a = Path.GetFullPath(root).TrimEnd('\\');
                string b = Path.GetFullPath(path).TrimEnd('\\');
                if (b.Length <= a.Length) { return 0; }
                string rest = b.Substring(a.Length);
                int depth = 0;
                foreach (char c in rest) { if (c == '\\') { depth++; } }
                return depth;
            }
            catch { return 0; }
        }

        private void SetBusy(bool busy, string text)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _scan.Enabled = !busy;
            _browse.Enabled = !busy;
            _checkAll.Enabled = !busy;
            _uncheck.Enabled = !busy;
            if (text.Length > 0) { _status.Text = text; }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _delete.Enabled = !_busy && CheckedCount() > 0;
        }

        private int CheckedCount()
        {
            int n = 0;
            foreach (ListViewItem row in _list.Items) { if (row.Checked) { n++; } }
            return n;
        }

        private void CheckAll(bool value)
        {
            if (_busy) { return; }
            _list.BeginUpdate();
            try
            {
                foreach (ListViewItem row in _list.Items) { row.Checked = value; }
            }
            finally
            {
                _list.EndUpdate();
            }
            _status.Text = value ? "已全选 —— 点「删除选中的空文件夹」才会真删（进回收站）。" : "勾选已清除。";
            UpdateButtons();
        }

        private void DeleteChecked()
        {
            if (_busy) { return; }
            List<string> chosen = new List<string>();
            foreach (ListViewItem row in _list.Items)
            {
                if (row.Checked) { chosen.Add(row.Text); }
            }
            if (chosen.Count == 0) { return; }

            if (!Consent.EnsureAccepted(this, _theme, "要删除这些空文件夹"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次删除被拦下了。";
                return;
            }

            StringBuilder body = new StringBuilder();
            body.Append("会删掉下面 ").Append(chosen.Count.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" 个空文件夹（**只删文件夹本身，里面本来就没有文件**），全部进回收站，删错了可以还原：");
            body.AppendLine();
            int shown = 0;
            foreach (string p in chosen)
            {
                if (shown >= 12) { body.AppendLine("… 还有 " + (chosen.Count - shown).ToString(CultureInfo.InvariantCulture) + " 个"); break; }
                body.Append("  · ").AppendLine(p);
                shown++;
            }
            using (ConfirmForm f = new ConfirmForm("要删除这些空文件夹吗？", body.ToString(), "删除到回收站", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusy(true, "正在删（进回收站）…");
            Logger.Write("空文件夹清理", "删除 " + chosen.Count.ToString(CultureInfo.InvariantCulture) + " 个空文件夹（进回收站）");
            Thread t = new Thread(delegate ()
            {
                Recycle.DeleteResult dr = EmptyFolders.Delete(chosen);
                try { BeginInvoke((MethodInvoker)delegate { FinishDelete(dr); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void FinishDelete(Recycle.DeleteResult dr)
        {
            if (IsDisposed) { return; }
            SetBusy(false, "");
            if (dr.Failed.Count == 0)
            {
                _status.Text = "删好了：" + dr.Deleted.ToString(CultureInfo.InvariantCulture) + " 个空文件夹进了回收站。正在重新扫描…";
                Logger.Write("空文件夹清理", "删除完成：" + dr.Deleted.ToString(CultureInfo.InvariantCulture) + " 个");
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("删了 ").Append(dr.Deleted.ToString(CultureInfo.InvariantCulture)).Append(" 个，")
                    .Append(dr.Failed.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个没删成：");
                for (int i = 0; i < dr.Failed.Count && i < 5; i++)
                {
                    sb.Append(" ").Append(dr.Failed[i]).Append("（").Append(dr.ReasonOf(i)).Append("）");
                }
                _status.Text = sb.ToString();
                Logger.Write("空文件夹清理", "删除部分失败：" + sb.ToString());
            }
            Scan();
        }
    }
}
