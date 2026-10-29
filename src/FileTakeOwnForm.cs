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
    /// <summary>「文件 / 文件夹取得所有权」窗口（2026-10-28 用户提的第 4 件事）：
    /// 删不掉、改不了、复制不进去的文件，在这里把它「拿到手」。
    ///
    /// 和「右键菜单管理」里那个注册表版同一个道理（`src\RegAcl.cs`），只是对象换成文件 / 文件夹：
    /// 所有者改成自己 + 追加一条「完全控制」；**只追加、不删原有 ACE**，随时能按记录还原。
    ///
    /// 三条口径（和「此电脑多余图标」窗口一致）：
    /// ① 加进来只读一遍所有者，什么都不改（所以不弹确认框）；
    /// ② 「取得所有权」「还原」每一次都过条款门 + 确认框；不是管理员时弹一次 UAC 带管理员重开窗口；
    /// ③ 动手前把原样的 SDDL 记进 TSV（记不下来就一个都不动），写完逐个读回来核对。
    ///
    /// ⚠️ 这一页**不替你删任何文件**：拿到权限之后要怎么删、怎么改，是你自己的事。</summary>
    internal sealed class FileTakeOwnForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _filter = new TextBox();
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly CheckBox _recurse = new CheckBox();
        private readonly Button _take = new Button();
        private readonly Button _restore = new Button();

        private volatile bool _busy = false;
        private List<string> _paths = new List<string>();
        private List<FileOwnItem> _all = new List<FileOwnItem>();

        public FileTakeOwnForm(Theme theme)
        {
            _theme = theme;
            Text = "文件 / 文件夹取得所有权（删不掉、改不了）";
            ClientSize = new Size(1040, 580);
            MinimumSize = new Size(880, 460);
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
            intro.MaximumSize = new Size(990, 0);
            intro.Text = "文件 / 文件夹删不掉、改不了、复制不进去，右键看权限又是灰的 —— 多半是「所有者不是你」，"
                + "你连改权限的资格都没有（重装系统留下的老文件夹、别的电脑拷来的移动硬盘最常见）。"
                + "把它们加进这个列表，选中点「取得所有权…」：所有者改成你、再给你一条「完全控制」。"
                + "只追加、不删原有权限，随时能「还原成取得前」。这一页不替你删任何文件。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel row1 = new TableLayoutPanel();
            row1.Dock = DockStyle.Fill;
            row1.AutoSize = true;
            row1.ColumnCount = 6;
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button addFile = new Button();
            addFile.Text = "添加文件…";
            StyleButton(addFile, 116);
            addFile.Click += delegate { AddFiles(); };
            Button addDir = new Button();
            addDir.Text = "添加文件夹…";
            StyleButton(addDir, 128);
            addDir.Click += delegate { AddFolder(); };
            _recurse.Text = "包含子文件夹 / 子文件（递归，勾了就整棵树一起）";
            _recurse.AutoSize = true;
            _recurse.Margin = new Padding(10, 6, 6, 0);
            Button removeSel = new Button();
            removeSel.Text = "从列表去掉";
            StyleButton(removeSel, 116);
            removeSel.Click += delegate { RemoveSelected(); };
            Button refresh = new Button();
            refresh.Text = "刷新";
            StyleButton(refresh, 84);
            refresh.Click += delegate { Rescan(); };
            row1.Controls.Add(addFile, 0, 0);
            row1.Controls.Add(addDir, 1, 0);
            row1.Controls.Add(_recurse, 2, 0);
            row1.Controls.Add(new Label(), 3, 0);
            row1.Controls.Add(removeSel, 4, 0);
            row1.Controls.Add(refresh, 5, 0);
            root.Controls.Add(row1, 0, 1);

            TableLayoutPanel filterRow = new TableLayoutPanel();
            filterRow.Dock = DockStyle.Fill;
            filterRow.AutoSize = true;
            filterRow.ColumnCount = 2;
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Label cap = new Label();
            cap.AutoSize = true;
            cap.Text = "过滤（路径 / 所有者）：";
            cap.Margin = new Padding(2, 7, 4, 0);
            filterRow.Controls.Add(cap, 0, 0);
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
            _list.Columns.Add("名称", 250);
            _list.Columns.Add("类型", 66);
            _list.Columns.Add("所有者", 260);
            _list.Columns.Add("归你管吗", 80);
            _list.Columns.Add("完整路径", 360);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            root.Controls.Add(_list, 0, 3);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(990, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 4);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 4;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 3; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _take.Text = "取得所有权（选中）…";
            StyleButton(_take, 170);
            _take.Click += delegate { DoTake(); };
            _restore.Text = "还原成取得前";
            StyleButton(_restore, 132);
            _restore.Click += delegate { DoRestore(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_take, 1, 0);
            buttons.Controls.Add(_restore, 2, 0);
            buttons.Controls.Add(close, 3, 0);
            root.Controls.Add(buttons, 0, 5);

            Controls.Add(root);
            CancelButton = close;
            KeyPreview = true;
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { Rescan(); e.Handled = true; }
            };

            ApplyTheme(theme);
            // ⚠️ 构造函数里**不许**先 SetBusyUi(true)（坑 78/79）。
            Load += delegate { Rescan(); };
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

        // ------------------------------------------------------------------ 加 / 读

        private void AddFiles()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选要取得所有权的文件（可以多选）";
                dlg.Filter = "所有文件 (*.*)|*.*";
                dlg.Multiselect = true;
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                int n = 0;
                foreach (string f in dlg.FileNames) { if (AddPath(f)) { n++; } }
                Rescan();
                if (n == 0) { Flash("这些路径已经在列表里了。"); }
            }
        }

        private void AddFolder()
        {
            string picked = FolderPicker.Pick(this, "选要取得所有权的文件夹", "");
            if (string.IsNullOrEmpty(picked)) { return; }
            if (!AddPath(picked)) { Flash("这个文件夹已经在列表里了。"); return; }
            Rescan();
        }

        private bool AddPath(string path)
        {
            foreach (string p in _paths)
            {
                if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) { return false; }
            }
            _paths.Add(path);
            return true;
        }

        private void RemoveSelected()
        {
            if (_list.SelectedItems.Count == 0) { Flash("先选中要从列表里去掉的行（注册表 / 权限一个字节都没动）。"); return; }
            List<string> drop = new List<string>();
            foreach (ListViewItem row in _list.SelectedItems)
            {
                FileOwnItem it = row.Tag as FileOwnItem;
                if (it != null) { drop.Add(it.Path); }
            }
            foreach (string p in drop)
            {
                for (int i = _paths.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_paths[i], p, StringComparison.OrdinalIgnoreCase)) { _paths.RemoveAt(i); }
                }
            }
            Rescan();
        }

        private void Rescan()
        {
            if (_busy) { return; }
            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在读所有者…";
            Application.DoEvents();
            List<FileOwnItem> items = new List<FileOwnItem>();
            foreach (string p in _paths) { items.Add(FileTakeOwn.Probe(p)); }
            _all = items;
            Fill();
            int self = 0;
            foreach (FileOwnItem it in items) { if (it.IsSelf) { self++; } }
            StringBuilder sb = new StringBuilder();
            sb.Append("列表里 ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append(" 项，其中 ")
                .Append(self.ToString(CultureInfo.InvariantCulture)).Append(" 项的所有者已经归你管。");
            if (items.Count == 0)
            {
                sb.Append("先点「添加文件…」或「添加文件夹…」把目标加进来。");
            }
            else
            {
                sb.Append("选中要处理的点「取得所有权（选中）…」；勾上「包含子文件夹」会把整棵树一起处理。");
            }
            int pending = 0;
            try { pending = FileTakeOwn.PendingRecords(); } catch { }
            if (pending > 0)
            {
                sb.Append("有 ").Append(pending.ToString(CultureInfo.InvariantCulture)).Append(" 条改动可以点「还原成取得前」。");
            }
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
                foreach (FileOwnItem it in _all)
                {
                    if (needle.Length > 0)
                    {
                        string hay = it.Path + " " + it.Owner;
                        if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                    }
                    string name;
                    try { name = Path.GetFileName(it.Path); } catch { name = it.Path; }
                    if (name.Length == 0) { name = it.Path; }
                    ListViewItem row = new ListViewItem(name);
                    row.SubItems.Add(it.KindText());
                    row.SubItems.Add(it.Owner.Length > 0 ? it.Owner : "（读不到）");
                    row.SubItems.Add(it.IsSelf ? "是" : "不是");
                    row.SubItems.Add(it.Path);
                    row.Tag = it;
                    if (!it.IsSelf) { row.ForeColor = _theme.Danger; }
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            UpdateButtons();
        }

        private void SetBusyUi(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _take.Enabled = !_busy && _list.SelectedItems.Count > 0;
            _take.Text = _list.SelectedItems.Count > 0
                ? ("取得所有权 " + _list.SelectedItems.Count.ToString(CultureInfo.InvariantCulture) + " 项…")
                : "取得所有权（选中）…";
            int pending = 0;
            try { pending = FileTakeOwn.PendingRecords(); } catch { }
            _restore.Enabled = !_busy && pending > 0;
        }

        private List<FileOwnItem> SelectedItems()
        {
            List<FileOwnItem> list = new List<FileOwnItem>();
            foreach (ListViewItem row in _list.SelectedItems)
            {
                FileOwnItem it = row.Tag as FileOwnItem;
                if (it != null) { list.Add(it); }
            }
            return list;
        }

        private void Flash(string text)
        {
            _status.Text = text;
            _status.ForeColor = _theme.Danger;
        }

        // ------------------------------------------------------------------ 取得所有权

        private void DoTake()
        {
            if (_busy) { return; }
            List<FileOwnItem> sel = SelectedItems();
            if (sel.Count == 0) { Flash("先选中要处理的行（可以按住 Ctrl 多选）。"); return; }
            bool recursive = _recurse.Checked;
            if (!Consent.EnsureAccepted(this, _theme, "要对这些文件 / 文件夹取得所有权"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            StringBuilder body = new StringBuilder();
            body.Append("要对 ").Append(sel.Count.ToString(CultureInfo.InvariantCulture)).Append(" 项取得所有权吗？");
            if (recursive) { body.Append("（勾了「包含子文件夹」，文件夹会整棵树一起处理）"); }
            body.AppendLine();
            body.AppendLine();
            int shown = 0;
            foreach (FileOwnItem it in sel)
            {
                if (shown >= 8) { body.Append("  … 还有 ").AppendLine((sel.Count - shown).ToString(CultureInfo.InvariantCulture) + " 项"); break; }
                body.Append("  · ").Append(it.Path).Append("（所有者：").Append(it.Owner.Length > 0 ? it.Owner : "读不到").AppendLine("）");
                shown++;
            }
            body.AppendLine();
            body.AppendLine("做的事：把所有者改成你（" + FileTakeOwn.CurrentUserName() + "），再追加一条「完全控制」；");
            body.AppendLine("**只追加、不删原有权限**，所以就算还原失败，原来的权限也还在。");
            body.AppendLine("改前会把原样的权限（SDDL）记到：" + FileTakeOwn.RecordFile());
            body.AppendLine("改完每一项都读回来核对；想退回去点「还原成取得前」。");
            body.AppendLine("⚠️ 这一页不替你删任何文件 —— 拿到权限之后怎么删、怎么改是你自己的事。");
            using (ConfirmForm f = new ConfirmForm("要对这些文件 / 文件夹取得所有权吗？", body.ToString(), "取得所有权", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (!Launcher.IsAdmin())
            {
                OfferElevation("取得所有权");
                return;
            }

            List<string> paths = new List<string>();
            foreach (FileOwnItem it in sel) { paths.Add(it.Path); }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在取得所有权（可能要一会儿，文件夹会整棵树走一遍）…";
            Application.DoEvents();
            Logger.Write("文件取得所有权", "取得所有权：" + string.Join(" | ", paths.ToArray()) + (recursive ? "（递归）" : ""));
            TakeOwnReport rep = FileTakeOwn.TakeOwn(paths, recursive);
            SetBusyUi(false);
            Logger.Write("文件取得所有权", "取得所有权" + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            Rescan();
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = "取得所有权" + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
        }

        private void DoRestore()
        {
            if (_busy) { return; }
            int pending = 0;
            try { pending = FileTakeOwn.PendingRecords(); } catch { }
            if (pending == 0)
            {
                Flash("没有待还原的记录（这台机器还没用这个窗口取得过所有权）。");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要把所有者和权限恢复成取得之前的样子"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            string body = "会把之前改过的 " + pending.ToString(CultureInfo.InvariantCulture) + " 项按记录写回去（所有者和整串权限）："
                + Environment.NewLine + Environment.NewLine
                + "  · 原来是谁的就是谁的；" + Environment.NewLine
                + "  · 原来没有的那条「完全控制」也跟着没了。" + Environment.NewLine + Environment.NewLine
                + "⚠️ 还原之后你可能又删不动它了 —— 这是设计如此。" + Environment.NewLine
                + "文件已经不在的项会跳过、记录留着。" + Environment.NewLine + Environment.NewLine
                + "记录文件：" + FileTakeOwn.RecordFile();
            using (ConfirmForm f = new ConfirmForm("要还原成取得前吗？", body, "还原", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (!Launcher.IsAdmin())
            {
                OfferElevation("还原");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在还原…";
            Application.DoEvents();
            Logger.Write("文件取得所有权", "还原：" + pending + " 条记录");
            TakeOwnReport rep = FileTakeOwn.Restore();
            SetBusyUi(false);
            Logger.Write("文件取得所有权", "还原" + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            Rescan();
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = "还原" + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
        }

        // ------------------------------------------------------------------ 不是管理员

        private void OfferElevation(string what)
        {
            string body = "取得文件 / 文件夹的所有权要用管理员权限（SeTakeOwnershipPrivilege 只有管理员有）。"
                + Environment.NewLine + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开。"
                + "因为改权限只能由你在窗口里点出来，所以重开之后要把路径重新加进来、再点一次「" + what + "」。";
            using (ConfirmForm f = new ConfirmForm("需要管理员权限", body, "确定", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath, "takeown");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("文件取得所有权", "不是管理员：已请求以管理员身份重开窗口");
                Close();
            }
            catch (Exception ex)
            {
                Flash("请求管理员权限失败：" + ex.Message);
                Logger.Write("文件取得所有权", "请求管理员权限失败：" + ex.Message);
            }
        }
    }
}
