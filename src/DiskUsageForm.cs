// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「C 盘空间体检」窗口（GAP-04）：**只读**统计某个盘 / 目录下面每个直接子目录占多大，
    /// 按占用从大到小排，顺便报出这个盘一共多大、还剩多少。
    ///
    /// 口径（和后端 `src\DiskUsage.cs` 一一对应）：
    /// ① **一个字节都不删、不改**：这一页只回答"地方被谁占了"，清理是「清理优化」那一页的事；
    /// ② 读不进去的目录（System Volume Information、别人的用户目录）不会让整次统计失败 ——
    ///    那一行会写"N 处没权限读（数字可能偏小）"，**绝不假装统计完整**；
    /// ③ junction / 符号链接只记一笔、不跟进去（跟了会算重，用户目录下 `All Users` 那种还会绕圈）；
    /// ④ 统计在**后台线程**跑：Windows 目录十几万个文件，同步跑窗口会假死（PITFALLS 78/79 那类）。</summary>
    internal sealed class DiskUsageForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly ListView _list = new ListView();
        private readonly Label _rootLabel = new Label();
        private readonly Label _status = new Label();
        private readonly Button _rescan = new Button();
        private readonly Button _pick = new Button();
        private readonly Button _open = new Button();

        private volatile bool _busy = false;
        private string _root = "";
        private long _totalBytes = 0;

        public DiskUsageForm(Theme theme)
        {
            _theme = theme;
            Text = "C 盘空间体检（只读）";
            ClientSize = new Size(940, 580);
            MinimumSize = new Size(800, 460);
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
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(890, 0);
            intro.Text = "看看这个盘的空间都被谁占了：下面每一行是一个直接子目录，按占用从大到小排。"
                + Environment.NewLine
                + "只读：不删文件、不清缓存、不动任何设置。要清理请去「清理优化」那一页。";
            intro.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(intro, 0, 0);

            _rootLabel.AutoSize = true;
            _rootLabel.MaximumSize = new Size(890, 0);
            _rootLabel.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_rootLabel, 0, 1);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("目录", 200);
            _list.Columns.Add("占用", 100);
            _list.Columns.Add("文件数", 90);
            _list.Columns.Add("子目录数", 90);
            _list.Columns.Add("说明", 380);
            _list.DoubleClick += delegate { OpenSelected(); };
            root.Controls.Add(_list, 0, 2);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(890, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _rescan.Text = "重新统计";
            StyleButton(_rescan, 100);
            _rescan.Click += delegate { Scan(); };
            _pick.Text = "换个目录";
            StyleButton(_pick, 94);
            _pick.Click += delegate { PickRoot(); };
            _open.Text = "在资源管理器里打开";
            StyleButton(_open, 160);
            _open.Click += delegate { OpenSelected(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_rescan, 1, 0);
            buttons.Controls.Add(_pick, 2, 0);
            buttons.Controls.Add(_open, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 4);

            Controls.Add(root);
            CancelButton = close;
            ApplyTheme(theme);
            Load += delegate { Scan(); };
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

        // ------------------------------------------------------------------ 统计

        private void Scan()
        {
            if (_busy) { return; }
            if (_root.Length == 0) { _root = DefaultRoot(); }
            SetBusy(true, "正在统计 " + _root + " …（文件多的盘要一会儿，请等它跑完）");
            string scanRoot = _root;
            Thread t = new Thread(delegate ()
            {
                List<DirUsage> rows = DiskUsage.Scan(scanRoot, null);
                string error = DiskUsage.LastError;
                long free, total;
                DiskUsage.DriveSpace(scanRoot, out free, out total);
                try { BeginInvoke((MethodInvoker)delegate { Fill(rows, error, free, total); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>默认统计 C:（没有 C: 就退到系统盘所在的盘）。
        /// `MXX1_DISKUSAGE_ROOT` 是**测试用的隔离根**（界面回归拿一个小夹具跑完就关，
        /// 不去扫整台机器的 C:\ —— 那会拖慢整套回归）；平时没人设这个变量。</summary>
        private static string DefaultRoot()
        {
            try
            {
                string envRoot = Environment.GetEnvironmentVariable("MXX1_DISKUSAGE_ROOT");
                if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot)) { return envRoot; }
            }
            catch { }
            try
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string r = Path.GetPathRoot(sys);
                if (!string.IsNullOrEmpty(r)) { return r; }
            }
            catch { }
            return "C:\\";
        }

        private void Fill(List<DirUsage> rows, string error, long free, long total)
        {
            if (IsDisposed) { return; }
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                long sum = 0;
                foreach (DirUsage du in rows)
                {
                    ListViewItem row = new ListViewItem(du.Name);
                    row.SubItems.Add(DupFinder.FormatSize(du.Bytes));
                    row.SubItems.Add(du.Files.ToString(CultureInfo.InvariantCulture));
                    row.SubItems.Add(du.Dirs.ToString(CultureInfo.InvariantCulture));
                    string note = DiskUsage.Note(du);
                    row.SubItems.Add(note.Length > 0 ? note : "-");
                    row.Tag = du;
                    if (note.Length > 0) { row.ForeColor = _theme.Danger; }
                    _list.Items.Add(row);
                    sum += du.Bytes;
                }
                _totalBytes = sum;
            }
            finally
            {
                _list.EndUpdate();
            }
            SetBusy(false, "");

            _rootLabel.Text = "统计的目录：" + _root;
            string line = "一共 " + DupFinder.FormatSize(_totalBytes);
            if (total > 0)
            {
                line += "（这个盘一共 " + DupFinder.FormatSize(total) + "，还剩 " + DupFinder.FormatSize(free) + "）";
            }
            if (error.Length > 0) { line += "，" + error; }
            line += "。上面只统计了各子目录的文件大小，不含系统还原点、休眠文件这类「不在目录里」的占用。";
            _status.Text = line;
            Logger.Write("C 盘空间体检", "统计 " + _root + " 完成：" + _list.Items.Count.ToString(CultureInfo.InvariantCulture)
                + " 行，合计 " + DupFinder.FormatSize(_totalBytes));
        }

        private void SetBusy(bool busy, string text)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _rescan.Enabled = !busy;
            _pick.Enabled = !busy;
            _open.Enabled = !busy;
            if (text.Length > 0) { _status.Text = text; }
        }

        private void PickRoot()
        {
            if (_busy) { return; }
            string picked;
            try { picked = FolderPicker.Pick(this, "要统计哪个目录（默认是 C 盘根目录）", _root); }
            catch { return; }
            if (picked == null) { return; }
            _root = picked;
            Scan();
        }

        private void OpenSelected()
        {
            if (_busy) { return; }
            string path = _root;
            if (_list.SelectedItems.Count > 0)
            {
                DirUsage du = _list.SelectedItems[0].Tag as DirUsage;
                if (du != null && !du.IsRootFiles && du.Path.Length > 0) { path = du.Path; }
            }
            try
            {
                if (Directory.Exists(path)) { System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\""); }
                else { _status.Text = "这个目录已经不在了：" + path; }
            }
            catch (Exception ex)
            {
                _status.Text = "打不开这个目录：" + ex.Message;
            }
        }
    }
}
