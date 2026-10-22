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
    /// <summary>「U 盘安全弹出」窗口（GAP-03）：
    /// 上面列可移动磁盘（选一个）→ 「看谁占着」列出打开盘上文件的程序（系统关键进程禁止勾选）→
    /// 「结束勾选的」→ 「安全弹出」。
    ///
    /// 三条口径：
    /// ① **只弹盘，不删盘上任何东西**；结束进程那一步要过《使用条款》同意门 + 确认框；
    /// ② 占用的程序是从 **Restart Manager** 查的（和「解除文件占用」同一套）：别的账户 / 更高权限下跑的
    ///    进程查不到，内核态占用（杀软实时扫描）根本不属于某个进程 —— 这两种情况如实写在状态行；
    /// ③ 弹不出就把系统给的原因（含 veto 名字）原样念出来，绝不说"可以拔了"。</summary>
    internal sealed class UsbEjectForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly ListView _drives = new ListView();
        private readonly ListView _locks = new ListView();
        private readonly Label _status = new Label();
        private readonly Label _locksTitle = new Label();
        private readonly Button _scan = new Button();
        private readonly Button _kill = new Button();
        private readonly Button _eject = new Button();
        private readonly Button _refresh = new Button();
        private readonly List<RemovableDrive> _list = new List<RemovableDrive>();
        private readonly List<FileLocker> _lockers = new List<FileLocker>();
        private bool _scanning;

        public UsbEjectForm(Theme theme)
        {
            _theme = theme;
            Text = "U 盘安全弹出（先看谁占着，再弹出）";
            ClientSize = new Size(980, 620);
            MinimumSize = new Size(820, 520);
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(930, 0);
            intro.Text = "拔 U 盘前先在这里弹一下：系统会把缓存刷进盘里，拔了不丢数据、不坏文件系统。"
                + Environment.NewLine
                + "弹不出来多半是还有程序开着盘里的东西 —— 按「看谁占着」列出来，勾选以后结束它，再弹一次。";
            intro.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(intro, 0, 0);

            _drives.View = View.Details;
            _drives.FullRowSelect = true;
            _drives.MultiSelect = false;
            _drives.HideSelection = false;
            _drives.Dock = DockStyle.Fill;
            _drives.Margin = new Padding(2, 0, 2, 6);
            _drives.Columns.Add("盘符", 70);
            _drives.Columns.Add("卷标", 200);
            _drives.Columns.Add("类型", 100);
            _drives.Columns.Add("可用 / 总共", 160);
            _drives.Columns.Add("状态", 300);
            _drives.SelectedIndexChanged += delegate { OnDriveChanged(); };
            root.Controls.Add(_drives, 0, 1);

            _locksTitle.AutoSize = true;
            _locksTitle.Margin = new Padding(2, 0, 2, 4);
            _locksTitle.Text = "占用的程序（勾选 → 「结束勾选的」；灰色的系统关键进程不给动）：";
            root.Controls.Add(_locksTitle, 0, 2);

            _locks.View = View.Details;
            _locks.CheckBoxes = true;
            _locks.FullRowSelect = true;
            _locks.HideSelection = false;
            _locks.Dock = DockStyle.Fill;
            _locks.Margin = new Padding(2, 0, 2, 6);
            _locks.Columns.Add("程序", 220);
            _locks.Columns.Add("进程号", 80);
            _locks.Columns.Add("类型", 130);
            _locks.Columns.Add("占着的文件", 420);
            _locks.Columns.Add("能不能结束", 150);
            _locks.ItemCheck += delegate(object s, ItemCheckEventArgs e)
            {
                FileLocker f = _locks.Items[e.Index].Tag as FileLocker;
                if (f != null && f.Protected && e.NewValue == CheckState.Checked)
                {
                    e.NewValue = CheckState.Unchecked;
                    _status.Text = "「" + FileName(f) + "」是系统关键进程，不给结束（" + f.Why + "）。";
                    return;
                }
                BeginInvoke((MethodInvoker)delegate { UpdateButtons(); });
            };
            _locks.ItemChecked += delegate { UpdateButtons(); };
            root.Controls.Add(_locks, 0, 3);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(930, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 4);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 6;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 5; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _scan.Text = "看谁占着";
            StyleButton(_scan, 100);
            _scan.Click += delegate { ScanLocks(); };
            _kill.Text = "结束勾选的";
            StyleButton(_kill, 108);
            _kill.Click += delegate { KillChecked(); };
            _eject.Text = "安全弹出";
            StyleButton(_eject, 96);
            _eject.Click += delegate { EjectNow(); };
            _refresh.Text = "刷新";
            StyleButton(_refresh, 76);
            _refresh.Click += delegate { Reload(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_scan, 1, 0);
            buttons.Controls.Add(_kill, 2, 0);
            buttons.Controls.Add(_eject, 3, 0);
            buttons.Controls.Add(_refresh, 4, 0);
            buttons.Controls.Add(close, 5, 0);
            root.Controls.Add(buttons, 0, 5);

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

        // ------------------------------------------------------------------ 列盘

        private void Reload()
        {
            _drives.BeginUpdate();
            try
            {
                _drives.Items.Clear();
                _lockers.Clear();
                _locks.Items.Clear();
                _list.Clear();
                _list.AddRange(UsbEject.List());
                foreach (RemovableDrive d in _list)
                {
                    ListViewItem row = new ListViewItem(d.Letter);
                    row.SubItems.Add(d.Label);
                    row.SubItems.Add(d.Kind);
                    row.SubItems.Add(d.Ready ? (UsbEject.SizeText(d.FreeBytes) + " / " + UsbEject.SizeText(d.TotalBytes)) : "—");
                    row.SubItems.Add(d.Note.Length > 0 ? d.Note : (d.Ready ? "可以弹出" : "盘没插好 / 读不到（可能刚拔掉）"));
                    row.Tag = d;
                    if (!d.Ready) { row.ForeColor = _theme.Danger; }
                    _drives.Items.Add(row);
                }
                if (_drives.Items.Count > 0) { _drives.Items[0].Selected = true; }
            }
            finally
            {
                _drives.EndUpdate();
            }

            string err = UsbEject.LastError;
            if (err.Length > 0) { _status.Text = err; }
            else if (_list.Count == 0)
            {
                _status.Text = "没有可移动磁盘（U 盘 / 读卡器）—— 插上盘以后按「刷新」。"
                    + "移动硬盘常被系统当成「固定磁盘」，不会出现在这里，那种请用任务栏的「安全删除硬件」。";
            }
            else
            {
                _status.Text = "一共 " + _list.Count.ToString(CultureInfo.InvariantCulture)
                    + " 个可移动磁盘。选中一个 → 「看谁占着」→ 结束占用的程序 → 「安全弹出」。";
            }
            Logger.Write("U 盘弹出", "列到 " + _list.Count.ToString(CultureInfo.InvariantCulture) + " 个可移动磁盘");
            OnDriveChanged();
        }

        private RemovableDrive Current()
        {
            if (_drives.SelectedItems.Count == 0) { return null; }
            return _drives.SelectedItems[0].Tag as RemovableDrive;
        }

        private void OnDriveChanged()
        {
            _lockers.Clear();
            _locks.Items.Clear();
            _locksTitle.Text = "占用的程序（勾选 → 「结束勾选的」；灰色的系统关键进程不给动）："
                + (Current() == null ? "" : "　" + Current().Letter);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool has = Current() != null;
            _scan.Enabled = has && !_scanning;
            _eject.Enabled = has && !_scanning;
            int checkedCount = 0;
            foreach (ListViewItem row in _locks.Items)
            {
                FileLocker f = row.Tag as FileLocker;
                if (f != null && !f.Protected && row.Checked) { checkedCount++; }
            }
            _kill.Text = checkedCount > 0
                ? "结束勾选的 " + checkedCount.ToString(CultureInfo.InvariantCulture) + " 个"
                : "结束勾选的";
            _kill.Enabled = checkedCount > 0 && !_scanning;
        }

        private static string FileName(FileLocker f)
        {
            if (!string.IsNullOrEmpty(f.Exe)) { return f.Exe; }
            return string.IsNullOrEmpty(f.AppName) ? ("进程 " + f.Pid.ToString(CultureInfo.InvariantCulture)) : f.AppName;
        }

        // ------------------------------------------------------------------ 谁占着

        private void ScanLocks()
        {
            RemovableDrive d = Current();
            if (d == null) { _status.Text = "先在上面选中一个盘。"; return; }
            if (_scanning) { return; }
            _scanning = true;
            Cursor = Cursors.WaitCursor;
            _status.Text = "正在看 " + d.Letter + " 里有哪些文件被程序开着…（U 盘里文件多的时候要几秒）";
            UpdateButtons();

            string root = d.Root;
            ThreadPool.QueueUserWorkItem(delegate
            {
                LockReport report = null;
                string error = "";
                try { report = FileLock.Scan(new string[] { root }); }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate { ShowLocks(report, error, root); });
                }
                catch { }
            });
        }

        private void ShowLocks(LockReport report, string error, string root)
        {
            _scanning = false;
            Cursor = Cursors.Default;
            _lockers.Clear();
            _locks.BeginUpdate();
            try
            {
                _locks.Items.Clear();
                if (report != null)
                {
                    foreach (FileLocker f in report.AllLockers())
                    {
                        if (ContainsPid(_lockers, f.Pid)) { continue; }
                        _lockers.Add(f);
                    }
                }
                foreach (FileLocker f in _lockers)
                {
                    ListViewItem row = new ListViewItem(FileName(f));
                    row.SubItems.Add(f.Pid.ToString(CultureInfo.InvariantCulture));
                    row.SubItems.Add(string.IsNullOrEmpty(f.Kind) ? "（不祥）" : f.Kind);
                    row.SubItems.Add(OneLine(string.Join(" / ", report == null ? new string[0] : report.FilesOf(f.Pid).ToArray())));
                    row.SubItems.Add(f.Protected ? ("不给结束：" + f.Why) : "可以结束");
                    row.Tag = f;
                    row.Checked = !f.Protected && f.Checked;
                    if (f.Protected) { row.ForeColor = _theme.Danger; }
                    _locks.Items.Add(row);
                }
            }
            finally
            {
                _locks.EndUpdate();
            }

            if (error.Length > 0)
            {
                _status.Text = "查占用的过程出错了：" + error;
            }
            else if (_lockers.Count == 0)
            {
                _status.Text = "没查到哪个程序占着 " + root + " —— 可以直接按「安全弹出」。"
                    + "（别的账户 / 更高权限下跑的程序查不到；杀软实时扫描这种内核态占用也查不到，"
                    + "如果还是弹不出，先把盘里的文件夹关掉再试。）";
            }
            else
            {
                int prot = 0;
                foreach (FileLocker f in _lockers) { if (f.Protected) { prot++; } }
                _status.Text = "查到 " + _lockers.Count.ToString(CultureInfo.InvariantCulture) + " 个程序占着 "
                    + root + "（其中 " + prot.ToString(CultureInfo.InvariantCulture) + " 个是系统关键进程，不给结束）。"
                    + "勾选以后按「结束勾选的」，然后「安全弹出」。";
            }
            Logger.Write("U 盘弹出", "扫描 " + root + "：查到 " + _lockers.Count.ToString(CultureInfo.InvariantCulture) + " 个占用程序");
            UpdateButtons();
        }

        private static bool ContainsPid(List<FileLocker> list, int pid)
        {
            foreach (FileLocker f in list) { if (f.Pid == pid) { return true; } }
            return false;
        }

        private static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            return s.Replace("\r", " ").Replace("\n", " ");
        }

        // ------------------------------------------------------------------ 结束

        private void KillChecked()
        {
            List<FileLocker> chosen = new List<FileLocker>();
            foreach (ListViewItem row in _locks.Items)
            {
                FileLocker f = row.Tag as FileLocker;
                if (f != null && !f.Protected && row.Checked) { chosen.Add(f); }
            }
            if (chosen.Count == 0) { _status.Text = "先在上面的列表里勾选要结束的程序。"; return; }

            if (!Consent.EnsureAccepted(this, _theme, "要结束 " + chosen.Count.ToString(CultureInfo.InvariantCulture) + " 个占用 U 盘的程序"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次没有结束任何程序。";
                return;
            }

            System.Text.StringBuilder body = new System.Text.StringBuilder();
            body.AppendLine("要结束下面这些程序吗？（它们里面没保存的东西会丢）：");
            body.AppendLine();
            foreach (FileLocker f in chosen)
            {
                body.AppendLine("· " + FileName(f) + "（进程 " + f.Pid.ToString(CultureInfo.InvariantCulture) + "，" + f.Kind + "）");
            }
            body.AppendLine();
            body.AppendLine("结束后再按「安全弹出」。盘上的文件一个都不会被删。");
            using (ConfirmForm f2 = new ConfirmForm("要结束这些程序吗？", body.ToString(), "结束", _theme))
            {
                if (f2.ShowDialog(this) != DialogResult.OK) { return; }
            }

            Cursor = Cursors.WaitCursor;
            int killed, failed;
            FileLock.Kill(chosen, out killed, out failed);
            Cursor = Cursors.Default;
            Logger.Write("U 盘弹出", "结束占用程序：成功 " + killed.ToString(CultureInfo.InvariantCulture)
                + " 个，失败 " + failed.ToString(CultureInfo.InvariantCulture) + " 个");
            _status.Text = "结束了 " + killed.ToString(CultureInfo.InvariantCulture) + " 个程序"
                + (failed > 0 ? ("，" + failed.ToString(CultureInfo.InvariantCulture) + " 个没结束掉（权限不够 / 已经退出了）") : "")
                + "。现在可以按「安全弹出」。";
            ScanLocks();
        }

        // ------------------------------------------------------------------ 弹出

        private void EjectNow()
        {
            RemovableDrive d = Current();
            if (d == null) { _status.Text = "先在上面选中一个盘。"; return; }

            using (ConfirmForm f = new ConfirmForm("要把 " + d.Letter + " 安全弹出吗？",
                "弹出之后这个盘就从「我的电脑」里消失了，想再用要拔下来重新插。" + Environment.NewLine
                + Environment.NewLine + "盘上打开着的文件先保存好 —— 弹不出来的话会告诉你是什么挡着。",
                "弹出", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            Cursor = Cursors.WaitCursor;
            string msg;
            bool ok;
            try { ok = UsbEject.Eject(d, out msg); }
            finally { Cursor = Cursors.Default; }
            Logger.Write("U 盘弹出", (ok ? "弹出成功：" : "弹出失败：") + d.Letter + "（" + msg + "）");
            _status.Text = msg;
            if (ok) { Reload(); }
        }
    }
}
