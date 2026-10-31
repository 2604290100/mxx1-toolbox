// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「程序和功能管理」窗口：把控制面板「程序和功能」/ 设置「应用」列表里的条目扫成一张
    /// **列表**（每行带它自己的小图标），选中一行可以**单个操作**（仿「端口与进程」/「此电脑多余
    /// 图标」，参考 HideUL 的隐藏 / 显示 / 锁定卸载三件套）。
    ///
    /// 治的是用户 2026-10-27 提的：列表里一堆不想看到 / 不想手滑卸掉的条目 —— 隐藏 = 写
    /// `SystemComponent=1`（列表立刻不显示，程序本体不删）；显示 = 按记录还原（工具箱藏的才还原）；
    /// 锁定卸载 = 写 `NoRemove=1`（卸载按钮变灰）；删除条目 = 先 .reg 备份再删列表登记（程序本体
    /// 不删、不跑卸载程序，「仅用做移除」的条目正合用）。
    ///
    /// 三条口径（和「此电脑多余图标」窗口一致）：
    /// ① 点开这个窗口**什么都不改**，只读扫三根 Uninstall 给你看，所以这里不弹确认框；
    /// ② 每个动作都过条款门 + 确认框；动 HKLM（64 位 / 32 位）的条目要管理员，HKCU 的不用；
    /// ③ 改之前先把原值记到磁盘（记不下来就一个字都不动），所以「还原成操作前」永远点得动。</summary>
    internal sealed class UninstallForm : Mxx1Form
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int ExtractIconEx(string lpszFile, int nIconIndex,
            out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly Theme _theme;
        private readonly TextBox _filter = new TextBox();
        private readonly ListView _list = new ListView();
        private readonly ImageList _icons = new ImageList();
        private readonly Label _status = new Label();
        private readonly Button _hide = new Button();
        private readonly Button _show = new Button();
        private readonly Button _lock = new Button();
        private readonly Button _unlock = new Button();
        private readonly Button _restore = new Button();
        private readonly Button _refresh = new Button();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _miHide = new ToolStripMenuItem("隐藏这个…");
        private readonly ToolStripMenuItem _miShow = new ToolStripMenuItem("显示这个…");
        private readonly ToolStripMenuItem _miLock = new ToolStripMenuItem("锁定卸载");
        private readonly ToolStripMenuItem _miUnlock = new ToolStripMenuItem("恢复卸载");
        private readonly ToolStripMenuItem _miDelete = new ToolStripMenuItem("删除条目…（先备份，可导回）");
        private readonly ToolStripMenuItem _miRestoreOne = new ToolStripMenuItem("还原更改前…");
        private readonly ToolStripMenuItem _miRegedit = new ToolStripMenuItem("打开注册表位置");
        private readonly ToolStripMenuItem _miKey = new ToolStripMenuItem("复制键路径");

        private volatile bool _busy = false;
        private List<UninstallItem> _all = new List<UninstallItem>();

        public UninstallForm(Theme theme)
        {
            _theme = theme;
            Text = "程序和功能管理（控制面板卸载列表）";
            ClientSize = new Size(1040, 600);
            MinimumSize = new Size(900, 480);
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
            intro.MaximumSize = new Size(990, 0);
            intro.Text = "控制面板「程序和功能」/ 设置「应用」列表 = 注册表三根 Uninstall 下的登记（一根一个程序）。"
                + "选中一行：点「隐藏这个…」把这条从列表藏起来（SystemComponent=1，程序本体不删、照常能用）；"
                + "「显示这个…」把工具箱藏过的放回列表；「锁定卸载」让卸载按钮变灰防手滑、"
                + "「恢复卸载」把工具箱锁过的解锁（NoRemove 删掉，卸载按钮恢复可点）；"
                + "右键「删除条目…」先从列表摘掉（先备份 .reg、可导回）—— 正适合「工商银行安全控件 (仅用做移除)」那种。"
                + "系统组件（○ 红色）只列出不动。";
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
            cap.Text = "过滤（名称 / 发布者 / 键路径）：";
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
            _list.Columns.Add("名称", 280);
            _list.Columns.Add("发布者", 160);
            _list.Columns.Add("安装时间", 90);
            _list.Columns.Add("登记位置", 66);
            _list.Columns.Add("状态", 74);
            _list.Columns.Add("键路径", 300);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.MouseDown += ListMouseDown;
            root.Controls.Add(_list, 0, 2);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(990, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 7;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 6; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _hide.Text = "隐藏这个…";
            StyleButton(_hide, 140);
            _hide.Click += delegate { DoHideOne(); };
            _show.Text = "显示这个…";
            StyleButton(_show, 140);
            _show.Click += delegate { DoShowOne(); };
            _lock.Text = "锁定卸载";
            StyleButton(_lock, 116);
            _lock.Click += delegate { DoLockOne(); };
            _unlock.Text = "恢复卸载";
            StyleButton(_unlock, 116);
            _unlock.Click += delegate { DoUnlockOne(); };
            _restore.Text = "还原成操作前";
            StyleButton(_restore, 132);
            _restore.Click += delegate { DoRestore(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_hide, 1, 0);
            buttons.Controls.Add(_show, 2, 0);
            buttons.Controls.Add(_lock, 3, 0);
            buttons.Controls.Add(_unlock, 4, 0);
            buttons.Controls.Add(_restore, 5, 0);
            buttons.Controls.Add(close, 6, 0);
            root.Controls.Add(buttons, 0, 4);

            _miHide.Click += delegate { DoHideOne(); };
            _miShow.Click += delegate { DoShowOne(); };
            _miLock.Click += delegate { DoLockOne(); };
            _miUnlock.Click += delegate { DoUnlockOne(); };
            _miDelete.Click += delegate { DoDeleteOne(); };
            _miRestoreOne.Click += delegate { DoRestoreOne(); };
            _miRegedit.Click += delegate { OpenRegedit(); };
            _miKey.Click += delegate { CopyKey(); };
            _menu.Items.Add(_miHide);
            _menu.Items.Add(_miShow);
            _menu.Items.Add(_miLock);
            _menu.Items.Add(_miUnlock);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miDelete);
            _menu.Items.Add(_miRestoreOne);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miRegedit);
            _menu.Items.Add(_miKey);
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
            _status.Text = "正在读三根 Uninstall…";
            Application.DoEvents();

            // 记住刚才选中的那一行（操作完成重扫列表后，还让它选中着 ——
            // 这样「锁定卸载」完立刻能点「恢复卸载」这一对，不用重新点行）
            UninstallItem prev = Selected();
            string prevKey = prev != null ? prev.KeyFull : "";

            List<UninstallItem> items;
            try
            {
                items = UninstallItems.Scan();
            }
            catch (Exception ex)
            {
                SetBusyUi(false);
                _status.ForeColor = _theme.Danger;
                _status.Text = "读不了「程序和功能」列表：" + ex.Message
                    + "（这一页读的是注册表：" + UninstallItems.RootText() + "；读不到多半是系统被精简 / 权限被限制。）";
                Logger.Write("程序和功能管理", "读失败：" + ex.Message);
                UpdateButtons();
                return;
            }

            // 可操作的排最上面（系统组件沉底），组内保持扫描顺序稳定
            items.Sort(delegate (UninstallItem a, UninstallItem b)
            {
                int ra = a.SystemItem ? 1 : 0;
                int rb = b.SystemItem ? 1 : 0;
                return ra.CompareTo(rb);
            });
            _all = items;
            Fill(items);
            if (prevKey.Length > 0)
            {
                foreach (ListViewItem row in _list.Items)
                {
                    UninstallItem it = row.Tag as UninstallItem;
                    if (it != null && string.Equals(it.KeyFull, prevKey, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Selected = true;
                        row.Focused = true;
                        break;
                    }
                }
            }

            int shown = 0, hidden = 0, sys = 0;
            foreach (UninstallItem it in items)
            {
                if (it.SystemItem) { sys++; } else if (it.OurHidden) { hidden++; } else { shown++; }
            }
            int pending = 0;
            try { pending = UninstallItems.PendingRecords(); } catch { }
            StringBuilder sb = new StringBuilder();
            if (resultPrefix.Length > 0) { sb.Append(resultPrefix).Append("　"); }
            sb.Append("共 ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append(" 条：显示中 ")
                .Append(shown.ToString(CultureInfo.InvariantCulture)).Append("、已隐藏 ")
                .Append(hidden.ToString(CultureInfo.InvariantCulture)).Append("、系统组件 ")
                .Append(sys.ToString(CultureInfo.InvariantCulture)).Append("（不动）。");
            if (shown > 0)
            {
                sb.Append("选中一条点「隐藏这个…」或右键。");
            }
            if (pending > 0)
            {
                sb.Append("有 ").Append(pending.ToString(CultureInfo.InvariantCulture)).Append(" 条记录可以点「还原成操作前」。");
            }
            SetBusyUi(false);
            _status.Text = sb.ToString();
        }

        private void Fill(List<UninstallItem> list)
        {
            string needle = _filter.Text.Trim();
            _icons.Images.Clear();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (UninstallItem it in list)
                {
                    if (needle.Length > 0)
                    {
                        string hay = it.Name + " " + it.Publisher + " " + it.KeyFull + " " + it.Root;
                        if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                    }
                    ListViewItem row = new ListViewItem(it.Name);
                    row.SubItems.Add(it.Publisher.Length > 0 ? it.Publisher : "—");
                    row.SubItems.Add(FormatDate(it.InstallDate));
                    row.SubItems.Add(it.Root == "HKLM64" ? "64位" : (it.Root == "HKLM32" ? "32位" : "用户"));
                    row.SubItems.Add(it.StatusText());
                    row.SubItems.Add(it.KeyFull);
                    row.Tag = it;
                    if (it.SystemItem) { row.ForeColor = _theme.Danger; }   // 系统组件：标红、不给动
                    int img = AddIcon(it.DisplayIcon);
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

        private static string FormatDate(string yyyymmdd)
        {
            if (yyyymmdd.Length != 8) { return yyyymmdd.Length > 0 ? yyyymmdd : "—"; }
            try
            {
                return yyyymmdd.Substring(0, 4) + "-" + yyyymmdd.Substring(4, 2) + "-" + yyyymmdd.Substring(6, 2);
            }
            catch { return yyyymmdd; }
        }

        /// <summary>把 DisplayIcon（某个 .exe/.ico 路径，或 `路径,索引`）解析成 16×16 小图标。</summary>
        private int AddIcon(string spec)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(spec)) { return -1; }
                string s = Environment.ExpandEnvironmentVariables(spec.Trim());
                string file = s;
                int index = 0;
                int comma = s.LastIndexOf(',');
                if (comma > 0)
                {
                    int i;
                    if (int.TryParse(s.Substring(comma + 1).Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out i))
                    {
                        file = s.Substring(0, comma).Trim();
                        index = i;
                    }
                }
                if (file.Length == 0 || !File.Exists(file)) { return -1; }
                Icon icon = null;
                if (index == 0)
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".ico")
                    {
                        icon = new Icon(file);
                    }
                    else
                    {
                        try { icon = Icon.ExtractAssociatedIcon(file); }
                        catch { icon = null; }
                    }
                }
                else
                {
                    IntPtr large = IntPtr.Zero;
                    IntPtr small = IntPtr.Zero;
                    if (ExtractIconEx(file, index, out large, out small, 1) <= 0) { return -1; }
                    try
                    {
                        if (small != IntPtr.Zero)
                        {
                            using (Icon tmp = Icon.FromHandle(small))
                            {
                                icon = (Icon)tmp.Clone();
                            }
                        }
                        else if (large != IntPtr.Zero)
                        {
                            using (Icon tmp = Icon.FromHandle(large))
                            {
                                icon = (Icon)tmp.Clone();
                            }
                        }
                    }
                    finally
                    {
                        if (small != IntPtr.Zero) { DestroyIcon(small); }
                        if (large != IntPtr.Zero) { DestroyIcon(large); }
                    }
                }
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
            UninstallItem it = Selected();
            bool shown = it != null && !it.SystemItem && !it.OurHidden;
            bool hidden = it != null && it.OurHidden;
            _hide.Enabled = !_busy && shown;
            _miHide.Enabled = _hide.Enabled;
            _show.Enabled = !_busy && hidden;
            _miShow.Enabled = _show.Enabled;
            _lock.Enabled = !_busy && shown && !it.LockedUninstall;
            _miLock.Enabled = _lock.Enabled;
            _unlock.Enabled = !_busy && it != null && it.OurLocked && !it.SystemItem;
            _miUnlock.Enabled = _unlock.Enabled;
            _miDelete.Enabled = !_busy && shown;
            _miRestoreOne.Enabled = !_busy && it != null && !it.SystemItem && (it.OurHidden || it.OurLocked);
            _miRegedit.Enabled = it != null;
            _miKey.Enabled = it != null;
            if (it != null && !it.SystemItem && !it.OurHidden && NeedsAdmin(it))
            {
                _hide.Text = "隐藏这个…（要管理员）";
                _lock.Text = "锁定卸载（要管理员）";
                _unlock.Text = "恢复卸载（要管理员）";
            }
            else
            {
                _hide.Text = "隐藏这个…";
                _lock.Text = "锁定卸载";
                _unlock.Text = "恢复卸载";
            }
            int pending = 0;
            try { pending = UninstallItems.PendingRecords(); } catch { }
            _restore.Enabled = !_busy && pending > 0;
        }

        private static bool NeedsAdmin(UninstallItem it)
        {
            if (it == null) { return false; }
            return it.Root == "HKLM64" || it.Root == "HKLM32";
        }

        private UninstallItem Selected()
        {
            if (_list.SelectedItems.Count == 0) { return null; }
            return _list.SelectedItems[0].Tag as UninstallItem;
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

        private void CopyKey()
        {
            UninstallItem it = Selected();
            if (it == null) { return; }
            try
            {
                Clipboard.SetDataObject(it.KeyFull, true);
                _status.ForeColor = _theme.BarText;
                _status.Text = "已复制键路径：" + it.KeyFull;
            }
            catch
            {
                Flash("剪贴板被别的程序占着，没复制成。键路径 = " + it.KeyFull);
            }
        }

        private void OpenRegedit()
        {
            UninstallItem it = Selected();
            if (it == null) { return; }
            try
            {
                Clipboard.SetDataObject(it.KeyFull, true);
                System.Diagnostics.Process.Start("regedit.exe");
                _status.ForeColor = _theme.BarText;
                _status.Text = "已打开注册表编辑器；键路径已复制到剪贴板（" + it.KeyFull + "），粘贴到地址栏回车即可定位。";
            }
            catch (Exception ex)
            {
                Flash("打不开注册表编辑器：" + ex.Message);
            }
        }

        // ------------------------------------------------------------------ 隐藏（单条）

        private void DoHideOne()
        {
            if (_busy) { return; }
            UninstallItem it = Selected();
            if (it == null) { Flash("先选中一行。"); return; }
            if (it.SystemItem) { Flash("这是系统组件 / 别的工具藏的，这一页不碰它。"); return; }
            if (!Consent.EnsureAccepted(this, _theme, "要把这条从「程序和功能」列表隐藏"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            string body = "要把「" + it.Name + "」从列表隐藏吗？" + Environment.NewLine + Environment.NewLine
                + "  登记位置：" + it.Root + Environment.NewLine
                + "  键路径：" + it.KeyFull + Environment.NewLine + Environment.NewLine
                + "隐藏 = 给这条写 SystemComponent=1 —— 控制面板 / 设置「应用」立刻不显示它；"
                + "**程序本体不删、照常能用**，卸载登记还在注册表里。想放回列表点「显示这个…」或「还原成操作前」。"
                + Environment.NewLine
                + "改之前会把原值记到：" + UninstallItems.RecordFile() + Environment.NewLine
                + "写完读回核对。";
            using (ConfirmForm f = new ConfirmForm("要把这条从列表隐藏吗？", body, "隐藏这个", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (NeedsAdmin(it) && !Launcher.IsAdmin())
            {
                OfferElevation("隐藏");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在隐藏「" + it.Name + "」…";
            Application.DoEvents();
            Logger.Write("程序和功能管理", "隐藏：" + it.KeyFull + "（" + it.Name + "）");
            UninstallReport rep = UninstallItems.Hide(new List<UninstallItem> { it });
            Finish(rep, "隐藏");
        }

        // ------------------------------------------------------------------ 显示（单条）

        private void DoShowOne()
        {
            if (_busy) { return; }
            UninstallItem it = Selected();
            if (it == null) { Flash("先选中一行。"); return; }
            if (!it.OurHidden) { Flash("这条不是工具箱隐藏的（是系统组件或别的工具藏的），这一页不碰它。"); return; }
            if (!Consent.EnsureAccepted(this, _theme, "要把这条放回「程序和功能」列表"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            string body = "要把「" + it.Name + "」放回列表吗？" + Environment.NewLine + Environment.NewLine
                + "  键路径：" + it.KeyFull + Environment.NewLine + Environment.NewLine
                + "按隐藏前的记录把 SystemComponent 删掉 / 写回原值，控制面板 / 设置「应用」里它就重新出现了。"
                + "程序本体没动过。" + Environment.NewLine
                + "改之前会把原值记到：" + UninstallItems.RecordFile() + Environment.NewLine
                + "写完读回核对。";
            using (ConfirmForm f = new ConfirmForm("要把这条放回列表吗？", body, "显示这个", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (NeedsAdmin(it) && !Launcher.IsAdmin())
            {
                OfferElevation("显示");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在显示「" + it.Name + "」…";
            Application.DoEvents();
            Logger.Write("程序和功能管理", "显示：" + it.KeyFull + "（" + it.Name + "）");
            UninstallReport rep = UninstallItems.Show(new List<UninstallItem> { it });
            Finish(rep, "显示");
        }

        // ------------------------------------------------------------------ 锁定卸载（单条）

        private void DoLockOne()
        {
            if (_busy) { return; }
            UninstallItem it = Selected();
            if (it == null) { Flash("先选中一行。"); return; }
            if (it.SystemItem) { Flash("这是系统组件，这一页不碰它。"); return; }
            if (it.LockedUninstall) { Flash("它已经是锁定状态了。"); return; }
            if (!Consent.EnsureAccepted(this, _theme, "要锁定这条的卸载按钮"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            string body = "要把「" + it.Name + "」的卸载按钮锁住吗？" + Environment.NewLine + Environment.NewLine
                + "  键路径：" + it.KeyFull + Environment.NewLine + Environment.NewLine
                + "锁定 = 写 NoRemove=1 —— 「程序和功能」里这条的「卸载」按钮变灰，防止手滑点到卸载。"
                + "程序本体没动，想解锁点「还原成操作前」。" + Environment.NewLine
                + "改之前会把原值记到：" + UninstallItems.RecordFile() + Environment.NewLine
                + "写完读回核对。";
            using (ConfirmForm f = new ConfirmForm("要锁定卸载按钮吗？", body, "锁定卸载", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (NeedsAdmin(it) && !Launcher.IsAdmin())
            {
                OfferElevation("锁定");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在锁定「" + it.Name + "」…";
            Application.DoEvents();
            Logger.Write("程序和功能管理", "锁定卸载：" + it.KeyFull + "（" + it.Name + "）");
            UninstallReport rep = UninstallItems.Lock(new List<UninstallItem> { it });
            Finish(rep, "锁定卸载");
        }

        // ------------------------------------------------------------------ 恢复卸载（单条）

        private void DoUnlockOne()
        {
            if (_busy) { return; }
            UninstallItem it = Selected();
            if (it == null) { Flash("先选中一行。"); return; }
            if (it.SystemItem) { Flash("这是系统组件，这一页不碰它。"); return; }
            if (!it.OurLocked) { Flash("这条不是工具箱锁的（NoRemove 不是工具箱写的），工具箱不动它。"); return; }
            if (!Consent.EnsureAccepted(this, _theme, "要恢复这条的卸载锁定"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            string body = "要把「" + it.Name + "」的卸载按钮恢复可点吗？" + Environment.NewLine + Environment.NewLine
                + "  键路径：" + it.KeyFull + Environment.NewLine + Environment.NewLine
                + "恢复卸载 = 按「锁定卸载」时记下的原值把 NoRemove 删掉 / 写回（正常情况下是删掉）——"
                + "「程序和功能」里这条的「卸载」按钮恢复可点。程序本体没动。"
                + Environment.NewLine + Environment.NewLine
                + "改之前会读一遍原值记录（" + UninstallItems.RecordFile() + "），写完读回核对、那行记录清掉。";
            using (ConfirmForm f = new ConfirmForm("要恢复卸载吗？", body, "恢复卸载", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (NeedsAdmin(it) && !Launcher.IsAdmin())
            {
                OfferElevation("恢复卸载");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在恢复「" + it.Name + "」的卸载…";
            Application.DoEvents();
            Logger.Write("程序和功能管理", "恢复卸载：" + it.KeyFull + "（" + it.Name + "）");
            UninstallReport rep = UninstallItems.Unlock(new List<UninstallItem> { it });
            Finish(rep, "恢复卸载");
        }

        // ------------------------------------------------------------------ 还原更改前（单条，右键）

        private void DoRestoreOne()
        {
            if (_busy) { return; }
            UninstallItem it = Selected();
            if (it == null) { Flash("先选中一行。"); return; }
            if (it.SystemItem) { Flash("这是系统组件，这一页不碰它。"); return; }
            if (!Consent.EnsureAccepted(this, _theme, "要还原这条在工具箱动过的设置"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            string body = "要把「" + it.Name + "」还原成工具箱动它之前的样子吗？" + Environment.NewLine + Environment.NewLine
                + "  键路径：" + it.KeyFull + Environment.NewLine + Environment.NewLine
                + "这条之前在工具箱里动过：隐藏（SystemComponent）/ 锁定卸载（NoRemove）都会按记录退回去；"
                + "如果这条之前被「删除条目…」删过，也会把备份 .reg 导回注册表。"
                + "程序本体没动。"
                + Environment.NewLine + Environment.NewLine
                + "改之前会读一遍原值记录（" + UninstallItems.RecordFile() + "），写完读回核对、这条的记录清掉（别条的不动）。";
            using (ConfirmForm f = new ConfirmForm("要还原这条的更改吗？", body, "还原", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (NeedsAdmin(it) && !Launcher.IsAdmin())
            {
                OfferElevation("还原更改前");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在还原「" + it.Name + "」…";
            Application.DoEvents();
            Logger.Write("程序和功能管理", "还原更改前：" + it.KeyFull + "（" + it.Name + "）");
            UninstallReport rep = UninstallItems.RestoreOne(it);
            Finish(rep, "还原更改前");
        }

        // ------------------------------------------------------------------ 删除条目（单条）

        private void DoDeleteOne()
        {
            if (_busy) { return; }
            UninstallItem it = Selected();
            if (it == null) { Flash("先选中一行。"); return; }
            if (it.SystemItem) { Flash("这是系统组件，这一页不碰它。"); return; }
            if (!Consent.EnsureAccepted(this, _theme, "要把这条从「程序和功能」列表删除"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            string body = "要把「" + it.Name + "」从「程序和功能」列表里删掉吗？" + Environment.NewLine + Environment.NewLine
                + "  键路径：" + it.KeyFull + Environment.NewLine + Environment.NewLine
                + "会先整条导出 .reg 备份，再删掉这条注册表登记 —— 列表里不再有它；"
                + "**程序本体不删、卸载程序也不跑**（「仅用做移除」的条目正合用）。"
                + Environment.NewLine + Environment.NewLine
                + "想找回来：点「还原成操作前」，工具箱按记录把 .reg 导回注册表。"
                + Environment.NewLine
                + "备份目录：" + UninstallItems.BackupDir();
            using (ConfirmForm f = new ConfirmForm("要把这条从列表删除吗？", body, "删除条目", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (NeedsAdmin(it) && !Launcher.IsAdmin())
            {
                OfferElevation("删除");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在删除「" + it.Name + "」的列表登记…";
            Application.DoEvents();
            Logger.Write("程序和功能管理", "删除条目：" + it.KeyFull + "（" + it.Name + "）");
            UninstallReport rep = UninstallItems.DeleteEntry(new List<UninstallItem> { it });
            Finish(rep, "删除条目");
        }

        // ------------------------------------------------------------------ 还原

        private void DoRestore()
        {
            if (_busy) { return; }
            if (!Consent.EnsureAccepted(this, _theme, "要把「程序和功能」列表还原成操作之前"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            int pending = 0;
            try { pending = UninstallItems.PendingRecords(); } catch { }
            if (pending == 0)
            {
                Flash("没有待还原的记录（这台机器还没用这个窗口动过「程序和功能」列表）。");
                return;
            }
            string body = "会把之前的所有改动按记录还原："
                + Environment.NewLine + Environment.NewLine
                + "  · 隐藏过的 → SystemComponent 删掉 / 写回原值（列表里重新出现）；" + Environment.NewLine
                + "  · 锁定过的 → NoRemove 删掉 / 写回原值（卸载按钮恢复）；" + Environment.NewLine
                + "  · 删除过的条目 → 把备份的 .reg 导回注册表（列表里重新出现）。" + Environment.NewLine + Environment.NewLine
                + "如果哪一条后来被别的程序改过，那一行会跳过并在报告里写出来（不会覆盖它）。"
                + Environment.NewLine + Environment.NewLine
                + "改动记录：" + UninstallItems.RecordFile() + Environment.NewLine
                + "删除记录：" + UninstallItems.DeletedRecordFile();
            using (ConfirmForm f = new ConfirmForm("要还原成操作前吗？", body, "还原", _theme))
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
            Logger.Write("程序和功能管理", "还原：" + pending + " 条记录");
            UninstallReport rep = UninstallItems.Restore();
            Finish(rep, "还原");
        }

        private void Finish(UninstallReport rep, string what)
        {
            if (IsDisposed) { return; }
            SetBusyUi(false);
            string line = what + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
            Logger.Write("程序和功能管理", what + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            RefreshState(line);
            if (rep.NeedAdmin && !Launcher.IsAdmin())
            {
                Flash(what + "时有几条因为权限没成 —— 点它们会弹 UAC，用管理员重开窗口之后再点一次。");
            }
        }

        // ------------------------------------------------------------------ 不是管理员

        private void OfferElevation(string what)
        {
            string body = "动「程序和功能」列表（写 HKLM 的 Uninstall）要用管理员权限才行。"
                + Environment.NewLine + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开。"
                + "因为只能由你在窗口里点出来，所以重开之后要再点一次「" + what + "」才会真的生效。";
            using (ConfirmForm f = new ConfirmForm("需要管理员权限", body, "确定", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath, "uninstall");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("程序和功能管理", "不是管理员：已请求以管理员身份重开窗口");
                Close();
            }
            catch (Exception ex)
            {
                Flash("请求管理员权限失败：" + ex.Message);
                Logger.Write("程序和功能管理", "请求管理员权限失败：" + ex.Message);
            }
        }
    }
}
