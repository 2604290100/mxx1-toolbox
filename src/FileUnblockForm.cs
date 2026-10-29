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
    /// <summary>「解除附件管理器锁定」窗口（2026-10-28 用户提的第 1 件事）：
    /// 把下载文件夹（或你选的文件夹）里**被 Windows 锁住**的文件扫成一张列表，选中就解除。
    ///
    /// 治的是什么：从网上下载的文件 / 从 Outlook、微信、QQ 收到的附件，Windows 会写一条
    /// `文件名:Zone.Identifier` 备用数据流（`ZoneId=3`）。资源管理器看到它就当文件"来自 Internet" ——
    /// Office 文档变保护视图 / 只读、EXE 弹「Windows 已保护你的电脑」、解压出来的东西还带着它。
    ///
    /// 三条口径（和「此电脑多余图标」窗口一致）：
    /// ① 点开这个窗口**什么都不改**：只读每条流，所以这里不弹确认框；
    /// ② 「解除锁定」「还原锁定」每一次都过条款门 + 确认框；
    /// ③ 解除前先把那条流的**原文**记到磁盘（记不下来就一个文件都不动），所以「还原锁定」永远点得动。
    ///
    /// ⚠️ 只删那条备用数据流，**文件内容一个字节都不动**；FAT32 的 U 盘上没有 ADS，扫出来就是 0 个。</summary>
    internal sealed class FileUnblockForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _dir = new TextBox();
        private readonly CheckBox _recurse = new CheckBox();
        private readonly TextBox _filter = new TextBox();
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly Button _browse = new Button();
        private readonly Button _scan = new Button();
        private readonly Button _unblockOne = new Button();
        private readonly Button _unblockAll = new Button();
        private readonly Button _restore = new Button();

        private volatile bool _busy = false;
        private List<UnblockItem> _all = new List<UnblockItem>();
        private bool _truncated = false;

        public FileUnblockForm(Theme theme)
        {
            _theme = theme;
            Text = "解除附件管理器锁定（下载的文件 / 邮件附件）";
            ClientSize = new Size(1000, 600);
            MinimumSize = new Size(860, 480);
            MinimizeBox = true;
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
            intro.MaximumSize = new Size(950, 0);
            intro.Text = "从网上下载的文件、从邮件 / 微信 / QQ 收到的附件，会被 Windows 记上一条「来自 Internet」的标记"
                + "（就是文件属性里那个「解除锁定」勾选框）：Office 文档打开变保护视图 / 只读、EXE 弹"
                + "「Windows 已保护你的电脑」、压缩包解压出来的东西一层层都带着它。这一页把它们扫成一张列表，"
                + "选中就解除（只删那条标记，文件内容一个字节都不动；解除前记原值、随时能还原）。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel dirRow = new TableLayoutPanel();
            dirRow.Dock = DockStyle.Fill;
            dirRow.AutoSize = true;
            dirRow.ColumnCount = 5;
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label cap = new Label();
            cap.AutoSize = true;
            cap.Text = "要扫的文件夹：";
            cap.Margin = new Padding(2, 7, 4, 0);
            dirRow.Controls.Add(cap, 0, 0);
            _dir.Dock = DockStyle.Fill;
            _dir.Margin = new Padding(2, 3, 6, 3);
            _dir.Text = FileUnblock.DefaultRoot();
            _dir.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { ScanNow(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            dirRow.Controls.Add(_dir, 1, 0);
            _browse.Text = "浏览…";
            StyleButton(_browse, 84);
            _browse.Click += delegate { Browse(); };
            dirRow.Controls.Add(_browse, 2, 0);
            _recurse.Text = "包含子文件夹";
            _recurse.AutoSize = true;
            _recurse.Margin = new Padding(10, 6, 6, 0);
            _recurse.Checked = true;
            dirRow.Controls.Add(_recurse, 3, 0);
            _scan.Text = "扫描";
            StyleButton(_scan, 84);
            _scan.Click += delegate { ScanNow(); };
            dirRow.Controls.Add(_scan, 4, 0);
            root.Controls.Add(dirRow, 0, 1);

            TableLayoutPanel filterRow = new TableLayoutPanel();
            filterRow.Dock = DockStyle.Fill;
            filterRow.AutoSize = true;
            filterRow.ColumnCount = 2;
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Label fcap = new Label();
            fcap.AutoSize = true;
            fcap.Text = "过滤（文件名 / 来源 / 文件夹）：";
            fcap.Margin = new Padding(2, 7, 4, 0);
            filterRow.Controls.Add(fcap, 0, 0);
            _filter.Dock = DockStyle.Fill;
            _filter.Margin = new Padding(2, 3, 6, 3);
            _filter.TextChanged += delegate { Fill(); };
            filterRow.Controls.Add(_filter, 1, 0);
            root.Controls.Add(filterRow, 0, 2);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = true;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("文件名", 260);
            _list.Columns.Add("来自", 240);
            _list.Columns.Add("标记", 190);
            _list.Columns.Add("大小", 80, HorizontalAlignment.Right);
            _list.Columns.Add("所在文件夹", 220);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            root.Controls.Add(_list, 0, 3);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(950, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 4);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _unblockOne.Text = "解除选中…";
            StyleButton(_unblockOne, 110);
            _unblockOne.Click += delegate { DoUnblock(false); };
            _unblockAll.Text = "全部解除…";
            StyleButton(_unblockAll, 110);
            _unblockAll.Click += delegate { DoUnblock(true); };
            _restore.Text = "还原锁定";
            StyleButton(_restore, 100);
            _restore.Click += delegate { DoRestore(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_unblockOne, 1, 0);
            buttons.Controls.Add(_unblockAll, 2, 0);
            buttons.Controls.Add(_restore, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 5);

            Controls.Add(root);
            CancelButton = close;
            KeyPreview = true;
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { ScanNow(); e.Handled = true; }
            };

            ApplyTheme(theme);
            // ⚠️ 构造函数里**不许**先 SetBusyUi(true)：ScanNow() 开头就是 `if (_busy) return;`（坑 78/79）。
            Load += delegate { ScanNow(); };
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

        private void ApplyTheme(Theme theme)
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
                CheckBox cb = c as CheckBox;
                if (cb != null) { cb.ForeColor = theme.BarText; }
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

        private void Browse()
        {
            string picked = FolderPicker.Pick(this, "选一个要扫的文件夹", _dir.Text.Trim());
            if (!string.IsNullOrEmpty(picked))
            {
                _dir.Text = picked;
                ScanNow();
            }
        }

        // ------------------------------------------------------------------ 扫描

        private void ScanNow()
        {
            if (_busy) { return; }
            string root = _dir.Text.Trim().Trim('"');
            if (root.Length == 0) { Flash("先填一个文件夹（或者点「浏览…」）。"); return; }
            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在读这个文件夹里每个文件的「来自 Internet」标记…";
            Application.DoEvents();

            _truncated = false;
            List<UnblockItem> items;
            try
            {
                items = FileUnblock.Scan(root, _recurse.Checked, ref _truncated, 40000);
            }
            catch (Exception ex)
            {
                SetBusyUi(false);
                _status.ForeColor = _theme.Danger;
                _status.Text = "扫不了这个文件夹：" + ex.Message
                    + "（换个文件夹试试；FAT32 的 U 盘没有这个标记，扫出来就是 0 个）";
                Logger.Write("解除附件管理器锁定", "扫描失败：" + ex.Message);
                return;
            }
            _all = items;
            Fill();

            int blocked = 0;
            foreach (UnblockItem it in items) { if (it.Blocked) { blocked++; } }
            StringBuilder sb = new StringBuilder();
            sb.Append("共 ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个带标记的文件，其中真的被锁住 ")
                .Append(blocked.ToString(CultureInfo.InvariantCulture)).Append(" 个。");
            if (items.Count == 0)
            {
                sb.Append("（这个文件夹里没有被 Windows 锁过的文件 —— 也可能它根本不在 NTFS 盘上。）");
            }
            else
            {
                sb.Append("选中要解除的（可以多选）点「解除选中…」，或者「全部解除…」。");
            }
            int pending = 0;
            try { pending = FileUnblock.PendingRecords(); } catch { }
            if (pending > 0)
            {
                sb.Append("有 ").Append(pending.ToString(CultureInfo.InvariantCulture)).Append(" 条解除记录可以点「还原锁定」写回去。");
            }
            if (_truncated) { sb.Append(" ⚠️ 文件太多，只看了前面 4 万个。"); }
            SetBusyUi(false);
            _status.Text = sb.ToString();
        }

        private void Fill()
        {
            string needle = _filter.Text.Trim();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (UnblockItem it in _all)
                {
                    if (needle.Length > 0)
                    {
                        string hay = it.Path + " " + it.HostUrl + " " + it.ReferrerUrl + " " + it.ZoneText();
                        if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                    }
                    ListViewItem row = new ListViewItem(it.Name());
                    row.SubItems.Add(it.HostUrl.Length > 0 ? it.HostUrl
                        : (it.ReferrerUrl.Length > 0 ? it.ReferrerUrl : "（没记来源）"));
                    row.SubItems.Add(it.Blocked ? ("已锁定（" + it.ZoneText() + "）") : it.ZoneText());
                    row.SubItems.Add(SizeText(it.Size));
                    row.SubItems.Add(it.Dir());
                    row.Tag = it;
                    if (!it.Blocked) { row.ForeColor = _theme.Danger; }
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            UpdateButtons();
        }

        private static string SizeText(long bytes)
        {
            if (bytes < 1024) { return bytes.ToString(CultureInfo.InvariantCulture) + " B"; }
            if (bytes < 1024 * 1024) { return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB"; }
            if (bytes < 1024L * 1024 * 1024) { return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB"; }
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        private void SetBusyUi(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _scan.Enabled = !busy;
            _browse.Enabled = !busy;
            _recurse.Enabled = !busy;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            int blockedSel = 0;
            foreach (ListViewItem row in _list.SelectedItems)
            {
                UnblockItem it = row.Tag as UnblockItem;
                if (it != null && it.Blocked) { blockedSel++; }
            }
            int blockedAll = 0;
            foreach (UnblockItem it in _all) { if (it.Blocked) { blockedAll++; } }
            _unblockOne.Enabled = !_busy && blockedSel > 0;
            _unblockOne.Text = blockedSel > 0 ? ("解除选中 " + blockedSel.ToString(CultureInfo.InvariantCulture) + " 个…") : "解除选中…";
            _unblockAll.Enabled = !_busy && blockedAll > 0;
            int pending = 0;
            try { pending = FileUnblock.PendingRecords(); } catch { }
            _restore.Enabled = !_busy && pending > 0;
        }

        private List<UnblockItem> SelectedBlocked()
        {
            List<UnblockItem> list = new List<UnblockItem>();
            foreach (ListViewItem row in _list.SelectedItems)
            {
                UnblockItem it = row.Tag as UnblockItem;
                if (it != null && it.Blocked) { list.Add(it); }
            }
            return list;
        }

        private void Flash(string text)
        {
            _status.Text = text;
            _status.ForeColor = _theme.Danger;
        }

        // ------------------------------------------------------------------ 解除

        private void DoUnblock(bool all)
        {
            if (_busy) { return; }
            List<UnblockItem> targets = all ? BlockedOf(_all) : SelectedBlocked();
            if (targets.Count == 0)
            {
                Flash(all ? "这个列表里没有被锁住的文件。" : "先选中要解除的行（可以按住 Ctrl 多选）。");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要解除这些文件的「来自 Internet」锁定"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            StringBuilder body = new StringBuilder();
            body.Append("要解除 ").Append(targets.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个文件的锁定吗？");
            body.AppendLine();
            int shown = 0;
            foreach (UnblockItem it in targets)
            {
                if (shown >= 8) { body.Append("  … 还有 ").AppendLine((targets.Count - shown).ToString(CultureInfo.InvariantCulture) + " 个"); break; }
                body.Append("  · ").AppendLine(it.Path);
                shown++;
            }
            body.AppendLine();
            body.AppendLine("只删那条「来自 Internet」的标记（备用数据流 Zone.Identifier），文件内容一个字节都不动。");
            body.AppendLine("解除完每个文件都会读回来核对；原值记到：" + FileUnblock.RecordFile());
            body.AppendLine("想恢复原来的锁定，点「还原锁定」。");
            using (ConfirmForm f = new ConfirmForm("要解除这些文件的锁定吗？", body.ToString(), "解除", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在解除 " + targets.Count.ToString(CultureInfo.InvariantCulture) + " 个文件的锁定…";
            Application.DoEvents();
            Logger.Write("解除附件管理器锁定", "解除 " + targets.Count + " 个文件");
            UnblockReport rep = FileUnblock.Unblock(targets);
            SetBusyUi(false);
            Logger.Write("解除附件管理器锁定", "解除" + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            ScanNow();
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = "解除" + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
        }

        private static List<UnblockItem> BlockedOf(List<UnblockItem> items)
        {
            List<UnblockItem> list = new List<UnblockItem>();
            foreach (UnblockItem it in items) { if (it.Blocked) { list.Add(it); } }
            return list;
        }

        private void DoRestore()
        {
            if (_busy) { return; }
            int pending = 0;
            try { pending = FileUnblock.PendingRecords(); } catch { }
            if (pending == 0)
            {
                Flash("没有待还原的记录（这台机器还没用这个窗口解除过锁定）。");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要把这些文件恢复成解除之前的样子（写回锁定）"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            string body = "会把之前解除过的 " + pending.ToString(CultureInfo.InvariantCulture) + " 个文件的锁定按记录写回去："
                + Environment.NewLine + Environment.NewLine
                + "  · 那条标记现在不在了 → 按原样写回去；" + Environment.NewLine
                + "  · 已经被别的程序重新锁上了 → 跳过（本来就是它该有的样子）；" + Environment.NewLine
                + "  · 文件已经不在了 → 跳过，记录留着。" + Environment.NewLine + Environment.NewLine
                + "记录文件：" + FileUnblock.RecordFile();
            using (ConfirmForm f = new ConfirmForm("要把锁定恢复回去吗？", body, "还原", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在还原…";
            Application.DoEvents();
            Logger.Write("解除附件管理器锁定", "还原 " + pending + " 条记录");
            UnblockReport rep = FileUnblock.Restore();
            SetBusyUi(false);
            Logger.Write("解除附件管理器锁定", "还原" + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            ScanNow();
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = "还原" + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
        }
    }
}
