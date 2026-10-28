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
    /// <summary>「开始菜单程序管理」窗口：把开始菜单「所有应用」列表里的 .lnk 扫成一张**列表**
    /// （每行带它自己的小图标），选中一行可以**单个操作**（仿「端口与进程」/「此电脑多余图标」）。
    ///
    /// 治的是用户 2026-10-27 提的：开始菜单列表里一堆没用的条目（7-Zip 帮助文档、各种「卸载」、
    /// 装软件自动塞的快捷方式）想收起来 —— 隐藏 = 把 .lnk 移进备份目录，开始菜单立刻不显示；
    /// 还原 = 按记录移回原位；删除 = 进回收站（程序本体不删）。系统级（ProgramData）的条目要
    /// 管理员权限，用户级的不用。
    ///
    /// 三条口径（和「此电脑多余图标」窗口一致）：
    /// ① 点开这个窗口**什么都不改**，只读扫一遍给你看，所以这里不弹确认框；
    /// ② 「隐藏这个…」/「删除这个…」/「还原成隐藏前」每一次都过条款门 + 确认框；动系统级
    ///    条目时不是管理员会弹 UAC 重开窗口，重开之后要再点一次；
    /// ③ 隐藏之前先把原值记到磁盘（记不下来就一个字都不动），所以「还原成隐藏前」永远点得动。</summary>
    internal sealed class StartMenuForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _filter = new TextBox();
        private readonly ListView _list = new ListView();
        private readonly ImageList _icons = new ImageList();
        private readonly Label _status = new Label();
        private readonly Button _hide = new Button();
        private readonly Button _show = new Button();
        private readonly Button _restore = new Button();
        private readonly Button _refresh = new Button();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _miHide = new ToolStripMenuItem("隐藏这个…");
        private readonly ToolStripMenuItem _miShow = new ToolStripMenuItem("显示这个…");
        private readonly ToolStripMenuItem _miDelete = new ToolStripMenuItem("删除这个…（回收站）");
        private readonly ToolStripMenuItem _miOpen = new ToolStripMenuItem("打开所在位置");
        private readonly ToolStripMenuItem _miCopy = new ToolStripMenuItem("复制完整路径");

        private volatile bool _busy = false;
        private List<StartMenuItem> _all = new List<StartMenuItem>();

        public StartMenuForm(Theme theme)
        {
            _theme = theme;
            Text = "开始菜单程序管理（所有应用列表里的快捷方式）";
            ClientSize = new Size(1020, 600);
            MinimumSize = new Size(880, 480);
            MinimizeBox = true;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized && Owner != null && !Owner.IsDisposed)
                {
                    Owner.WindowState = FormWindowState.Minimized;
                }
            };

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
            intro.MaximumSize = new Size(970, 0);
            intro.Text = "开始菜单「所有应用」里的每一项 = 系统级 + 用户级两个目录里的一个快捷方式（.lnk）。"
                + "这一页把它们扫成一张带图标的列表：装软件时自动塞进来的（帮助文档、各种「卸载」、"
                + "没用的入口）选中一行点「隐藏这个…」（或右键）把 .lnk 移进备份目录，开始菜单立刻不显示；"
                + "「还原成隐藏前」按记录移回原位；右键「删除这个…」删到回收站（程序本体不删、照常能用）。"
                + "系统级（所有用户都看得到）的条目要管理员权限。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel filterRow = new TableLayoutPanel();
            filterRow.Dock = DockStyle.Fill;
            filterRow.AutoSize = true;
            filterRow.ColumnCount = 3;
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label cap = new Label();
            cap.AutoSize = true;
            cap.Text = "过滤（名称 / 路径）：";
            cap.Margin = new Padding(2, 7, 4, 0);
            filterRow.Controls.Add(cap, 0, 0);
            _filter.Dock = DockStyle.Fill;
            _filter.Margin = new Padding(2, 3, 6, 3);
            _filter.TextChanged += delegate { Fill(_all); };
            filterRow.Controls.Add(_filter, 1, 0);
            _refresh.Text = "刷新";
            StyleButton(_refresh, 84);
            _refresh.Click += delegate { RefreshState(""); };
            filterRow.Controls.Add(_refresh, 2, 0);
            root.Controls.Add(filterRow, 0, 1);

            _icons.ColorDepth = ColorDepth.Depth32Bit;
            _icons.ImageSize = new Size(16, 16);
            _list.SmallImageList = _icons;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("名称（相对路径）", 300);
            _list.Columns.Add("来源", 56);
            _list.Columns.Add("类型", 72);
            _list.Columns.Add("状态", 64);
            _list.Columns.Add("完整路径", 380);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.MouseDown += ListMouseDown;
            root.Controls.Add(_list, 0, 2);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(970, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 6;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 5; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _hide.Text = "隐藏这个…";
            StyleButton(_hide, 150);
            _hide.Click += delegate { DoHideOne(); };
            _show.Text = "显示这个…";
            StyleButton(_show, 132);
            _show.Click += delegate { DoShowOne(); };
            _restore.Text = "还原成隐藏前";
            StyleButton(_restore, 132);
            _restore.Click += delegate { DoRestore(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_hide, 1, 0);
            buttons.Controls.Add(_show, 2, 0);
            buttons.Controls.Add(_restore, 3, 0);
            buttons.Controls.Add(_refresh, 4, 0);
            buttons.Controls.Add(close, 5, 0);
            root.Controls.Add(buttons, 0, 4);

            _miHide.Click += delegate { DoHideOne(); };
            _miShow.Click += delegate { DoShowOne(); };
            _miDelete.Click += delegate { DoDeleteOne(); };
            _miOpen.Click += delegate { OpenLocation(); };
            _miCopy.Click += delegate { CopyPath(); };
            _menu.Items.Add(_miHide);
            _menu.Items.Add(_miShow);
            _menu.Items.Add(_miDelete);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miOpen);
            _menu.Items.Add(_miCopy);
            _list.ContextMenuStrip = _menu;

            Controls.Add(root);
            CancelButton = close;
            KeyPreview = true;
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { RefreshState(""); e.Handled = true; }
            };

            ApplyTheme(theme);
            Load += delegate { RefreshState(""); };
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

        // ------------------------------------------------------------------ 读

        private void RefreshState(string resultPrefix)
        {
            if (_busy) { return; }
            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在读开始菜单的两个目录…";
            Application.DoEvents();

            List<StartMenuItem> items;
            try
            {
                items = StartMenu.Scan();
            }
            catch (Exception ex)
            {
                SetBusyUi(false);
                _status.ForeColor = _theme.Danger;
                _status.Text = "读不了开始菜单：" + ex.Message
                    + "（读的是：" + StartMenu.RootText() + "；读不到多半是目录被精简 / 权限被限制。）";
                Logger.Write("开始菜单程序管理", "读失败：" + ex.Message);
                UpdateButtons();
                return;
            }

            _all = items;
            Fill(items);

            int pending = 0;
            try { pending = StartMenu.PendingRecords(); } catch { }
            int hidden = 0;
            foreach (StartMenuItem it in items) { if (it.OurHidden) { hidden++; } }
            StringBuilder sb = new StringBuilder();
            if (resultPrefix.Length > 0) { sb.Append(resultPrefix).Append("　"); }
            sb.Append("共 ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个：显示中 ")
                .Append((items.Count - hidden).ToString(CultureInfo.InvariantCulture)).Append("、已隐藏 ")
                .Append(hidden.ToString(CultureInfo.InvariantCulture)).Append("（系统级 ")
                .Append(CountSource(items, "system")).Append(" 个、用户级 ")
                .Append(CountSource(items, "user")).Append(" 个）。");
            if (items.Count > 0)
            {
                sb.Append("选中一个点「隐藏这个…」或右键（系统级的要管理员权限）。");
            }
            if (hidden > 0)
            {
                sb.Append("隐藏过的还在列表里，选中点「显示这个…」随时放回去。");
            }
            if (pending > 0)
            {
                sb.Append("有 ").Append(pending.ToString(CultureInfo.InvariantCulture)).Append(" 个隐藏记录可以点「还原成隐藏前」放回去。");
            }
            SetBusyUi(false);
            _status.Text = sb.ToString();
        }

        private static int CountSource(List<StartMenuItem> items, string source)
        {
            int n = 0;
            foreach (StartMenuItem it in items) { if (it.Source == source) { n++; } }
            return n;
        }

        private void Fill(List<StartMenuItem> list)
        {
            string needle = _filter.Text.Trim();
            _icons.Images.Clear();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (StartMenuItem it in list)
                {
                    if (needle.Length > 0)
                    {
                        string hay = it.Rel + " " + it.Full + " " + it.Source;
                        if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                    }
                    ListViewItem row = new ListViewItem(it.Rel);
                    row.SubItems.Add(it.Source == "system" ? "系统级" : "用户级");
                    row.SubItems.Add(it.SystemItem ? "系统自带" : (it.OurHidden ? "已隐藏" : "普通"));
                    row.SubItems.Add(it.OurHidden ? "已隐藏" : "");
                    row.SubItems.Add(it.Full);
                    row.Tag = it;
                    if (it.OurHidden)
                    {
                        row.ForeColor = _theme.PlaceholderText; // 隐藏过的沉一点，状态列写着「已隐藏」
                    }
                    else if (it.SystemItem)
                    {
                        row.ForeColor = _theme.Danger;     // Windows 自带的标红提示
                    }
                    int img = AddIcon(it.OurHidden ? it.HiddenDest : it.Full);
                    if (img >= 0) { row.ImageIndex = img; }
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            UpdateButtons();
        }

        /// <summary>给 .lnk 提取它指向的程序的图标（Icon.ExtractAssociatedIcon 对快捷方式也认）。</summary>
        private int AddIcon(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { return -1; }
                Icon icon;
                try { icon = Icon.ExtractAssociatedIcon(path); }
                catch { icon = null; }
                if (icon == null) { return -1; }
                using (icon)
                {
                    _icons.Images.Add(icon);
                }
                return _icons.Images.Count - 1;
            }
            catch
            {
                return -1;
            }
        }

        private void SetBusyUi(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _refresh.Enabled = !busy;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            StartMenuItem it = Selected();
            bool hidden = (it != null) && it.OurHidden;
            _hide.Enabled = !_busy && it != null && !hidden;
            _miHide.Enabled = _hide.Enabled;
            _show.Enabled = !_busy && hidden;
            _miShow.Enabled = _show.Enabled;
            _miDelete.Enabled = it != null;
            _miOpen.Enabled = it != null;
            _miCopy.Enabled = it != null;
            if (it != null && IsSystemLevel(it) && !hidden) { _hide.Text = "隐藏这个…（要管理员）"; }
            else { _hide.Text = "隐藏这个…"; }
            int pending = 0;
            try { pending = StartMenu.PendingRecords(); } catch { }
            _restore.Enabled = !_busy && pending > 0;
        }

        private static bool IsSystemLevel(StartMenuItem it)
        {
            if (it == null) { return false; }
            // 权威字段是 Source（Scan 时按目录区分 system / user），不能拿路径字符串猜
            // （隔离测试目录不含 ProgramData，拿字符串判断会把系统级条目当用户级）。
            return string.Equals(it.Source, "system", StringComparison.OrdinalIgnoreCase);
        }

        private StartMenuItem Selected()
        {
            if (_list.SelectedItems.Count == 0) { return null; }
            return _list.SelectedItems[0].Tag as StartMenuItem;
        }

        private void ListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) { return; }
            ListViewItem row = _list.GetItemAt(e.X, e.Y);
            if (row != null) { row.Selected = true; }
        }

        private void Flash(string text)
        {
            _status.Text = text;
            _status.ForeColor = _theme.Danger;
        }

        private void CopyPath()
        {
            StartMenuItem it = Selected();
            if (it == null) { return; }
            try
            {
                Clipboard.SetDataObject(it.Full, true);
                _status.ForeColor = _theme.BarText;
                _status.Text = "已复制完整路径：" + it.Full;
            }
            catch
            {
                Flash("剪贴板被别的程序占着，没复制成。路径 = " + it.Full);
            }
        }

        private void OpenLocation()
        {
            StartMenuItem it = Selected();
            if (it == null) { return; }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("explorer.exe",
                    "/select,\"" + it.Full + "\"");
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                Flash("打不开所在位置：" + ex.Message);
            }
        }

        // ------------------------------------------------------------------ 隐藏（单条）

        private void DoHideOne()
        {
            if (_busy) { return; }
            StartMenuItem it = Selected();
            if (it == null) { Flash("先选中一行。"); return; }
            if (!Consent.EnsureAccepted(this, _theme, "要隐藏这条开始菜单快捷方式"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            string body = "要隐藏「" + it.Rel + "」吗？" + Environment.NewLine + Environment.NewLine
                + "  来源：" + (it.Source == "system" ? "系统级（所有用户看得到，要管理员）" : "用户级（只有你）") + Environment.NewLine
                + "  完整路径：" + it.Full + Environment.NewLine + Environment.NewLine
                + "隐藏 = 把这个 .lnk 移进工具箱的备份目录（" + StartMenu.HiddenRoot + "），开始菜单立刻不显示；"
                + "程序本体不删、照常能用。想放回去点「还原成隐藏前」。" + Environment.NewLine
                + "隐藏前会把原值记到：" + StartMenu.RecordFile() + Environment.NewLine
                + "移完会读回核对。";
            using (ConfirmForm f = new ConfirmForm("要隐藏这条开始菜单快捷方式吗？", body, "隐藏这个", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (IsSystemLevel(it) && !Launcher.IsAdmin())
            {
                OfferElevation("隐藏");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在隐藏「" + it.Rel + "」…";
            Application.DoEvents();
            Logger.Write("开始菜单程序管理", "隐藏：" + it.Rel);
            StartMenuReport rep = StartMenu.Hide(new List<StartMenuItem> { it });
            Finish(rep, "隐藏");
        }

        // ------------------------------------------------------------------ 显示（单条放回原位）

        private void DoShowOne()
        {
            if (_busy) { return; }
            StartMenuItem it = Selected();
            if (it == null) { Flash("先选中一行。"); return; }
            if (!it.OurHidden) { Flash("这一条还在原位，没有隐藏过，不需要显示。"); return; }
            if (!Consent.EnsureAccepted(this, _theme, "要把这条开始菜单快捷方式放回原位"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            string body = "要把「" + it.Rel + "」放回原位吗？" + Environment.NewLine + Environment.NewLine
                + "  来源：" + (it.Source == "system" ? "系统级（所有用户看得到，要管理员）" : "用户级（只有你）") + Environment.NewLine
                + "  完整路径：" + it.Full + Environment.NewLine + Environment.NewLine
                + "显示 = 把这个 .lnk 从工具箱的备份目录（" + StartMenu.HiddenRoot + "）移回原位，开始菜单重新显示它；"
                + "如果原位已经有一个 .lnk 了（软件重装加回来了），会跳过并在报告里写出来（不会覆盖它）。"
                + Environment.NewLine + "记录文件：" + StartMenu.RecordFile();
            using (ConfirmForm f = new ConfirmForm("要显示这条开始菜单快捷方式吗？", body, "显示这个", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (IsSystemLevel(it) && !Launcher.IsAdmin())
            {
                OfferElevation("显示");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在显示「" + it.Rel + "」…";
            Application.DoEvents();
            Logger.Write("开始菜单程序管理", "显示：" + it.Rel);
            StartMenuReport rep = StartMenu.Show(new List<StartMenuItem> { it });
            Finish(rep, "显示");
        }

        // ------------------------------------------------------------------ 删除（单条，回收站）

        private void DoDeleteOne()
        {
            if (_busy) { return; }
            StartMenuItem it = Selected();
            if (it == null) { Flash("先选中一行。"); return; }
            if (!Consent.EnsureAccepted(this, _theme, "要删除这条开始菜单快捷方式"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            string body = "要删除「" + it.Rel + "」吗？（**进回收站，删错了能捞回来**）" + Environment.NewLine + Environment.NewLine
                + "  来源：" + (it.Source == "system" ? "系统级（所有用户看得到，要管理员）" : "用户级（只有你）") + Environment.NewLine
                + "  完整路径：" + it.Full + Environment.NewLine + Environment.NewLine
                + "删除 = 把这个 .lnk 丢进回收站，开始菜单不再显示它；**程序本体不删**，照常能用。"
                + "想找回来：桌面回收站里还原。";
            using (ConfirmForm f = new ConfirmForm("要删除这条开始菜单快捷方式吗？", body, "删除这个", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            // 已隐藏的条目文件在工具箱的备份目录里（用户可写），不用过管理员那道门
            if (IsSystemLevel(it) && !it.OurHidden && !Launcher.IsAdmin())
            {
                OfferElevation("删除");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在删除「" + it.Rel + "」…";
            Application.DoEvents();
            Logger.Write("开始菜单程序管理", "删除：" + it.Rel);
            StartMenuReport rep = StartMenu.Delete(new List<StartMenuItem> { it });
            Finish(rep, "删除");
        }

        // ------------------------------------------------------------------ 还原

        private void DoRestore()
        {
            if (_busy) { return; }
            if (!Consent.EnsureAccepted(this, _theme, "要把开始菜单条目放回隐藏之前的样子"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            int pending = 0;
            try { pending = StartMenu.PendingRecords(); } catch { }
            if (pending == 0)
            {
                Flash("没有待还原的记录（这台机器还没用这个窗口隐藏过开始菜单条目）。");
                return;
            }
            string body = "会把之前隐藏的 " + pending.ToString(CultureInfo.InvariantCulture) + " 条快捷方式按记录移回原位："
                + Environment.NewLine + Environment.NewLine
                + "  · 系统级的（ProgramData）要管理员权限；" + Environment.NewLine
                + "  · 用户级的直接放回。" + Environment.NewLine + Environment.NewLine
                + "如果原位已经有一个 .lnk 了（软件重装加回来了），那一行会跳过并在报告里写出来（不会覆盖它）。"
                + Environment.NewLine + Environment.NewLine
                + "记录文件：" + StartMenu.RecordFile();
            using (ConfirmForm f = new ConfirmForm("要还原成隐藏前吗？", body, "还原", _theme))
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
            Logger.Write("开始菜单程序管理", "还原：" + pending + " 条记录");
            StartMenuReport rep = StartMenu.Restore();
            Finish(rep, "还原");
        }

        private void Finish(StartMenuReport rep, string what)
        {
            if (IsDisposed) { return; }
            SetBusyUi(false);
            string line = what + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
            Logger.Write("开始菜单程序管理", what + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            RefreshState(line);
            if (rep.NeedAdmin && !Launcher.IsAdmin())
            {
                Flash(what + "时有几条因为权限没成 —— 点它们会弹 UAC，用管理员重开窗口之后再点一次。");
            }
        }

        // ------------------------------------------------------------------ 不是管理员

        private void OfferElevation(string what)
        {
            string body = "动系统级（ProgramData）的开始菜单条目要用管理员权限才行。"
                + Environment.NewLine + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开。"
                + "因为只能由你在窗口里点出来，所以重开之后要再点一次「" + what + "」才会真的生效。";
            using (ConfirmForm f = new ConfirmForm("需要管理员权限", body, "确定", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath, "startmenu");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("开始菜单程序管理", "不是管理员：已请求以管理员身份重开窗口");
                Close();
            }
            catch (Exception ex)
            {
                Flash("请求管理员权限失败：" + ex.Message);
                Logger.Write("开始菜单程序管理", "请求管理员权限失败：" + ex.Message);
            }
        }
    }
}
