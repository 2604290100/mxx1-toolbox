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
    /// <summary>「重复文件查找」窗口：选一个目录，三档比较（大小 → 前 4 KB → 整个文件）找出内容相同的文件。
    ///
    /// 界面口径（和后端 `src\DupFinder.cs` 一一对应）：
    /// ① 每组自动留一个（按「保留规则」下拉框：最早修改 / 路径最短 / 字母序第一个），其余标「重复」；
    /// ② **硬链接标出来、永不删**（删了不省空间，还可能是用户自己做的布局）；
    /// ③ 删除走回收站；删之前过《使用条款》同意门 + 确认框，确认框里写清"省多少空间"。
    ///
    /// 扫描可取消（几百 GB 的盘读起来很慢）；取消后已经找到的结果照样显示。</summary>
    internal sealed class DupForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _root = new TextBox();
        private readonly ComboBox _minSize = new ComboBox();
        private readonly ComboBox _keep = new ComboBox();
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly Button _browse = new Button();
        private readonly Button _scan = new Button();
        private readonly Button _cancel = new Button();
        private readonly Button _delete = new Button();

        private volatile bool _busy = false;
        private volatile bool _canceled = false;
        private DupFinder.Result _result;
        private string _keepRule = "oldest";

        public DupForm(Theme theme)
        {
            _theme = theme;
            Text = "重复文件查找（三档比较，删到回收站）";
            ClientSize = new Size(1020, 620);
            MinimumSize = new Size(860, 500);
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
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(970, 0);
            intro.Text = "三档比较：**大小不同的一定不是重复** → 同大小的先比前 4 KB → 只有前 4 KB 也一样的才读整个文件。"
                + "比较的是内容，和文件名无关；改过名、拷过好几份的都会被找出来。"
                + Environment.NewLine + "每组自动留一个（见「保留规则」），其余标「重复」；硬链接标出来但不会删（删了不省空间）。"
                + "删除走回收站，勾选之后点右下角那个按钮才会真删。";
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
            cap.Margin = new Padding(2, 7, 4, 0);
            pathRow.Controls.Add(cap, 0, 0);
            _root.Dock = DockStyle.Fill;
            _root.Margin = new Padding(2, 3, 6, 3);
            pathRow.Controls.Add(_root, 1, 0);
            _browse.Text = "浏览…";
            StyleButton(_browse, 84);
            _browse.Click += delegate { PickFolder(); };
            pathRow.Controls.Add(_browse, 2, 0);
            _scan.Text = "开始扫描";
            StyleButton(_scan, 96);
            _scan.Click += delegate { Scan(); };
            pathRow.Controls.Add(_scan, 3, 0);
            root.Controls.Add(pathRow, 0, 1);

            TableLayoutPanel opts = new TableLayoutPanel();
            opts.Dock = DockStyle.Fill;
            opts.AutoSize = true;
            opts.ColumnCount = 6;
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            opts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Label cap2 = new Label();
            cap2.AutoSize = true;
            cap2.Text = "只看大于：";
            cap2.Margin = new Padding(2, 7, 4, 0);
            opts.Controls.Add(cap2, 0, 0);
            Prepare(_minSize, 150);
            _minSize.Items.Add("1 MB（默认）");
            _minSize.Items.Add("100 KB");
            _minSize.Items.Add("10 KB");
            _minSize.Items.Add("0（所有非空文件）");
            _minSize.SelectedIndex = 0;
            opts.Controls.Add(_minSize, 1, 0);
            Label cap3 = new Label();
            cap3.AutoSize = true;
            cap3.Text = "保留规则：";
            cap3.Margin = new Padding(16, 7, 4, 0);
            opts.Controls.Add(cap3, 2, 0);
            Prepare(_keep, 170);
            _keep.Items.Add("最早修改的（默认）");
            _keep.Items.Add("路径最短的");
            _keep.Items.Add("按字母序第一个");
            _keep.SelectedIndex = 0;
            _keep.SelectedIndexChanged += delegate
            {
                _keepRule = KeepRule();
                if (_result != null) { Fill(_result); }
            };
            opts.Controls.Add(_keep, 3, 0);
            _cancel.Text = "取消扫描";
            StyleButton(_cancel, 96);
            _cancel.Click += delegate { _canceled = true; _status.Text = "正在停…（已经读进去的结果会保留）"; };
            opts.Controls.Add(_cancel, 4, 0);
            opts.Controls.Add(new Label(), 5, 0);
            root.Controls.Add(opts, 0, 2);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("组", 50);
            _list.Columns.Add("大小", 92);
            _list.Columns.Add("处理", 120);
            _list.Columns.Add("文件路径", 760);
            root.Controls.Add(_list, 0, 3);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(970, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 4);
            root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.SetRow(_status, 4);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 4;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 3; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _delete.Text = "删除重复文件（保留每组一个，进回收站）";
            StyleButton(_delete, 330);
            _delete.Click += delegate { DeleteDuplicates(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_delete, 1, 0);
            buttons.Controls.Add(close, 2, 0);
            root.Controls.Add(buttons, 0, 5);

            Controls.Add(root);
            CancelButton = close;
            ApplyTheme(theme);
            Load += delegate
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string dl = Path.Combine(home, "Downloads");
                if (Directory.Exists(dl)) { _root.Text = dl; }
                else { _root.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
                _status.Text = "选一个文件夹，点「开始扫描」。扫描是只读的；默认跳过 1 MB 以下的小文件（省不出空间）。";
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

        private void Prepare(ComboBox box, int width)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Width = width;
            box.Font = Font;
            box.Margin = new Padding(2, 3, 2, 3);
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

        private string KeepRule()
        {
            int i = _keep.SelectedIndex;
            if (i == 1) { return "shortest"; }
            if (i == 2) { return "first"; }
            return "oldest";
        }

        private long MinSizeValue()
        {
            int i = _minSize.SelectedIndex;
            if (i == 1) { return 100 * 1024; }
            if (i == 2) { return 10 * 1024; }
            if (i == 3) { return 0; }
            return 1024L * 1024L;
        }

        private void PickFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选一个要查重复文件的目录（只读扫描）";
                dlg.ShowNewFolderButton = false;
                if (_root.Text.Trim().Length > 0 && Directory.Exists(_root.Text.Trim()))
                {
                    dlg.SelectedPath = _root.Text.Trim();
                }
                if (dlg.ShowDialog(this) == DialogResult.OK) { _root.Text = dlg.SelectedPath; }
            }
        }

        // ------------------------------------------------------------------ 扫描

        private void Scan()
        {
            if (_busy) { return; }
            string dir = _root.Text.Trim();
            if (dir.Length == 0) { _status.Text = "先选一个文件夹。"; return; }
            if (!Directory.Exists(dir)) { _status.Text = "这个文件夹不存在：" + dir; return; }

            _keepRule = KeepRule();
            DupFinder.Options opt = new DupFinder.Options();
            opt.MinSize = MinSizeValue();
            _canceled = false;
            _result = null;
            _list.Items.Clear();
            SetBusy(true, "正在扫：" + dir + " …");

            string rootPath = dir;
            DateTime last = DateTime.MinValue;
            DupFinder.Options options = opt;
            Thread t = new Thread(delegate ()
            {
                DupFinder.Result res = DupFinder.Find(new string[] { rootPath }, options,
                    delegate { return _canceled; },
                    delegate (DupFinder.Progress p)
                    {
                        DateTime now = DateTime.Now;
                        if ((now - last).TotalMilliseconds < 200) { return; }
                        last = now;
                        string text = "正在扫：候选 " + p.Files.ToString(CultureInfo.InvariantCulture) + " 个，"
                            + "已读 " + DupFinder.FormatSize(p.HashedBytes);
                        try { BeginInvoke((MethodInvoker)delegate { if (_busy) { _status.Text = text; } }); }
                        catch { }
                    });
                try { BeginInvoke((MethodInvoker)delegate { Fill(res); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Fill(DupFinder.Result res)
        {
            if (IsDisposed) { return; }
            _result = res;
            _keepRule = KeepRule();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                for (int gi = 0; gi < res.Groups.Count; gi++)
                {
                    DupFinder.Group g = res.Groups[gi];
                    DupFinder.Entry keep = DupFinder.Suggest(g, _keepRule);
                    foreach (DupFinder.Entry e in g.Files)
                    {
                        string status = (e == keep) ? "保留" : (e.Hardlink ? "硬链接（不删）" : "重复，可删");
                        ListViewItem row = new ListViewItem((gi + 1).ToString(CultureInfo.InvariantCulture));
                        row.SubItems.Add(DupFinder.FormatSize(g.Size));
                        row.SubItems.Add(status);
                        row.SubItems.Add(e.Path);
                        if (e == keep) { row.ForeColor = _theme.DarkMode ? Color.LightGreen : Color.DarkGreen; }
                        else if (e.Hardlink) { row.ForeColor = _theme.DarkMode ? Color.Goldenrod : Color.DarkGoldenrod; }
                        _list.Items.Add(row);
                    }
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            SetBusy(false, "");
            long freeable = DupFinder.FreeableBytes(res.Groups, _keepRule);
            string line = "找到 " + res.Groups.Count.ToString(CultureInfo.InvariantCulture) + " 组重复，共 "
                + CountMembers(res).ToString(CultureInfo.InvariantCulture) + " 个文件；按当前规则删掉多余的能省 "
                + DupFinder.FormatSize(freeable) + "。";
            if (res.Canceled) { line = "扫描被取消了 —— " + line; }
            if (res.Errors.Count > 0) { line += " 有 " + res.Errors.Count.ToString(CultureInfo.InvariantCulture) + " 个文件 / 目录读不了（见日志）。"; }
            _status.Text = line;
            Logger.Write("重复文件查找", "扫描 " + (res.Canceled ? "（取消）" : "") + "：组 " + res.Groups.Count.ToString(CultureInfo.InvariantCulture)
                + "，文件 " + CountMembers(res).ToString(CultureInfo.InvariantCulture) + "，可省 " + freeable.ToString(CultureInfo.InvariantCulture) + " 字节");
            foreach (string e in res.Errors) { Logger.Write("重复文件查找", "扫描提示：" + e); }
            UpdateButtons();
        }

        private static int CountMembers(DupFinder.Result res)
        {
            int n = 0;
            foreach (DupFinder.Group g in res.Groups) { n += g.Files.Count; }
            return n;
        }

        private void SetBusy(bool busy, string text)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _scan.Enabled = !busy;
            _browse.Enabled = !busy;
            _cancel.Enabled = busy;
            _minSize.Enabled = !busy;
            _keep.Enabled = !busy;
            if (text.Length > 0) { _status.Text = text; }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _delete.Enabled = !_busy && _result != null && DupFinder.DeletablePaths(_result.Groups, _keepRule).Count > 0;
        }

        // ------------------------------------------------------------------ 删除

        private void DeleteDuplicates()
        {
            if (_busy || _result == null) { return; }
            List<string> doomed = DupFinder.DeletablePaths(_result.Groups, _keepRule);
            if (doomed.Count == 0) { _status.Text = "没有可删的：每组只剩一个（或者全是硬链接）。"; return; }
            long bytes = DupFinder.FreeableBytes(_result.Groups, _keepRule);

            if (!Consent.EnsureAccepted(this, _theme, "要删除这些重复文件"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次删除被拦下了。";
                return;
            }

            StringBuilder body = new StringBuilder();
            body.Append("会删掉 ").Append(doomed.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" 个文件（每组留一个），预计省出 ").Append(DupFinder.FormatSize(bytes))
                .AppendLine("，全部进回收站，删错了可以还原：");
            body.AppendLine();
            body.AppendLine("保留规则：" + _keep.Items[_keep.SelectedIndex].ToString());
            body.AppendLine();
            int shown = 0;
            foreach (string p in doomed)
            {
                if (shown >= 12) { body.AppendLine("… 还有 " + (doomed.Count - shown).ToString(CultureInfo.InvariantCulture) + " 个"); break; }
                body.Append("  · ").AppendLine(p);
                shown++;
            }
            using (ConfirmForm f = new ConfirmForm("要删除这些重复文件吗？", body.ToString(), "删除到回收站", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusy(true, "正在删（进回收站）…");
            Logger.Write("重复文件查找", "删除 " + doomed.Count.ToString(CultureInfo.InvariantCulture) + " 个重复文件（进回收站）");
            Thread t = new Thread(delegate ()
            {
                Recycle.DeleteResult dr = Recycle.DeleteToRecycleBin(doomed);
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
                _status.Text = "删好了：" + dr.Deleted.ToString(CultureInfo.InvariantCulture) + " 个文件进了回收站。正在重新扫描…";
                Logger.Write("重复文件查找", "删除完成：" + dr.Deleted.ToString(CultureInfo.InvariantCulture) + " 个");
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
                Logger.Write("重复文件查找", "删除部分失败：" + sb.ToString());
            }
            Scan();
        }
    }
}
