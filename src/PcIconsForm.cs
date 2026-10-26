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
    /// <summary>「此电脑多余图标」窗口：把「此电脑 → 设备和驱动器」里的挂载点图标扫成一张**列表**
    /// （每行带它自己的小图标），选中一行可以**单个操作**（仿「端口与进程」窗口）：
    ///
    /// 治的是用户 2026-10-26 报的那个：装完百度网盘，「此电脑 → 设备和驱动器」里多了一颗
    /// 「双击运行百度网盘」的图标 —— 真因是软件往 `MyComputer\NameSpace` 写了挂载点（HKCU +
    /// HKLM 两处都有同名 GUID）。移除 = 只删挂载子键、绝不碰 CLSID / 软件本身；系统自带的挂载点
    /// 只列出、一颗都不会动（选中了也只显示「系统自带（不给移除）」）；移除前记原值、随时能还原。
    ///
    /// 三条口径（和「文件关联体检」「端口与进程」窗口一致）：
    /// ① 点开这个窗口**什么都不改**，只读一遍两个挂载点给你看，所以这里不弹确认框；
    /// ② 「移除这颗…」/「还原成移除前」每一次都过条款门 + 确认框；不是管理员时弹一次 UAC 带
    ///    管理员重开窗口，重开之后要再点一次；
    /// ③ 移除之前先把原值记到磁盘（记不下来就一个字都不改），所以「还原成移除前」永远点得动。</summary>
    internal sealed class PcIconsForm : Mxx1Form
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
        private readonly Button _remove = new Button();
        private readonly Button _restore = new Button();
        private readonly Button _refresh = new Button();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _miRemove = new ToolStripMenuItem("移除这颗…");
        private readonly ToolStripMenuItem _miGuid = new ToolStripMenuItem("复制 GUID");

        private volatile bool _busy = false;
        private int _removable = 0;
        private List<PcIconItem> _all = new List<PcIconItem>();

        public PcIconsForm(Theme theme)
        {
            _theme = theme;
            Text = "此电脑多余图标（设备和驱动器里被软件加的）";
            ClientSize = new Size(980, 580);
            MinimumSize = new Size(840, 460);
            MinimizeBox = true;                  // 最小化按钮：点它会把主窗口一起收进任务栏（样板窗口，2026-10-26）
            MaximizeBox = false;
            ShowInTaskbar = false;               // 不占任务栏：跟主窗口共用一个按钮（见 Resize 联动）
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;
            // ⚠️ 联动：这个窗口是挂在主窗口名下的从属窗口。它最小化时（收进任务栏 = 没有自己的
            // 按钮），把主窗口也一起最小化 —— 任务栏只剩主窗口一个按钮，点它两个窗口一起回来。
            // 反过来主窗口最小化 / 还原时，从属窗口是 WinForms 自己跟着动的，不用这里管。
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
            intro.MaximumSize = new Size(930, 0);
            intro.Text = "装软件时被塞进「此电脑 → 设备和驱动器」的图标（百度网盘、迅雷、微信这类）"
                + "就来自注册表里的两个挂载点 —— 这一页把它们扫成一张列表（每行带它自己的小图标）："
                + "显示名 / 来源 / 图标 / 双击干什么。选中一颗第三方图标点「移除这颗…」（或右键），"
                + "只把挂载点清掉：软件本身不删、照常能用，图标马上从「此电脑」消失。"
                + "系统自带的条目只列出、一颗都不会动；移除前会把原值记下来，所以随时能点「还原成移除前」放回去。";
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
            cap.Text = "过滤（名称 / GUID / 图标 / 命令）：";
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
            _list.Columns.Add("名称", 150);
            _list.Columns.Add("来源", 54);
            _list.Columns.Add("类型", 66);
            _list.Columns.Add("GUID", 236);
            _list.Columns.Add("图标文件", 230);
            _list.Columns.Add("双击命令", 220);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.MouseDown += ListMouseDown;
            root.Controls.Add(_list, 0, 2);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(930, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _remove.Text = "移除这颗…";
            StyleButton(_remove, 150);
            _remove.Click += delegate { DoRemoveOne(); };
            _restore.Text = "还原成移除前";
            StyleButton(_restore, 132);
            _restore.Click += delegate { DoRestore(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_remove, 1, 0);
            buttons.Controls.Add(_restore, 2, 0);
            buttons.Controls.Add(_refresh, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 4);

            _miRemove.Click += delegate { DoRemoveOne(); };
            _miGuid.Click += delegate { CopyGuid(); };
            _menu.Items.Add(_miRemove);
            _menu.Items.Add(_miGuid);
            _list.ContextMenuStrip = _menu;

            Controls.Add(root);
            CancelButton = close;
            KeyPreview = true;
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { RefreshState(""); e.Handled = true; }
            };

            ApplyTheme(theme);
            // ⚠️ 构造函数里**不许**先 SetBusyUi(true)：RefreshState() 开头就是 `if (_busy) return;`，
            // 会把自己挡掉、窗口定格在"忙"上（坑 78/79 那次「鼠标移上去一直转圈」就是这么来的）。
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
            _status.Text = "正在读「此电脑」的两个挂载点…";
            Application.DoEvents();

            List<PcIconItem> items;
            try
            {
                items = PcIcons.Scan();
            }
            catch (Exception ex)
            {
                SetBusyUi(false);
                _status.ForeColor = _theme.Danger;
                _status.Text = "读不了这台机器的「此电脑」挂载点：" + ex.Message
                    + "（这一页读的是注册表：" + PcIcons.RootText() + "；读不到多半是系统被精简 / 权限被限制。）";
                Logger.Write("此电脑多余图标", "读失败：" + ex.Message);
                UpdateButtons();
                return;
            }

            // 可移除的排最上面（最常操作的在眼前），组内保持扫描顺序稳定
            items.Sort(delegate (PcIconItem a, PcIconItem b)
            {
                int ra = a.Removable ? 0 : 1;
                int rb = b.Removable ? 0 : 1;
                return ra.CompareTo(rb);
            });
            _all = items;
            Fill(items);

            _removable = 0;
            foreach (PcIconItem it in items)
            {
                if (it.Removable) { _removable++; }
            }
            int pending = 0;
            try { pending = PcIcons.PendingRecords(); } catch { }
            StringBuilder sb = new StringBuilder();
            if (resultPrefix.Length > 0) { sb.Append(resultPrefix).Append("　"); }
            sb.Append("共 ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append(" 颗：系统 ")
                .Append((items.Count - _removable).ToString(CultureInfo.InvariantCulture)).Append(" 颗（不动）、第三方 / 来历不明 ")
                .Append(_removable.ToString(CultureInfo.InvariantCulture)).Append(" 颗（可移除）。");
            if (_removable > 0)
            {
                sb.Append("选中一颗点「移除这颗…」或右键（系统自带的不给移除）。");
            }
            if (pending > 0)
            {
                sb.Append("有 ").Append(pending.ToString(CultureInfo.InvariantCulture)).Append(" 条移除记录可以点「还原成移除前」放回去。");
            }
            SetBusyUi(false);
            _status.Text = sb.ToString();
        }

        private void Fill(List<PcIconItem> list)
        {
            string needle = _filter.Text.Trim();
            _icons.Images.Clear();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (PcIconItem it in list)
                {
                    if (needle.Length > 0)
                    {
                        string hay = it.Name + " " + it.Guid + " " + it.Icon + " " + it.Command
                            + " " + it.SourceText() + " " + it.Kind;
                        if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                    }
                    string typeText = it.Kind == "system" ? "系统自带"
                        : (it.Kind == "third" ? "第三方" : "来历不明");
                    ListViewItem row = new ListViewItem(it.Name.Length > 0 ? it.Name : "（来历不明）");
                    row.SubItems.Add(it.SourceText());
                    row.SubItems.Add(typeText);
                    row.SubItems.Add(it.Guid);
                    row.SubItems.Add(it.Icon.Length > 0 ? it.Icon : "（没有图标信息）");
                    row.SubItems.Add(it.Command.Length > 0 ? it.Command : "（没有双击命令）");
                    row.Tag = it;
                    if (!it.Removable) { row.ForeColor = _theme.Danger; }   // 系统自带：标红、不给移除
                    int img = AddIcon(it.Icon);
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

        /// <summary>把 CLSID 的 DefaultIcon 文本（某个 `.ico` 文件路径或 `%SystemRoot%\…\imageres.dll,-184`）
        /// 解析成一张 16×16 小图标。.ico 文件直接读；dll / exe 的 `,-索引` 走 ExtractIconEx。
        /// 解析不出来就返回 -1（那行不带图标，不假装）。</summary>
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
                    _icons.Images.Add(icon);   // ImageList 会复制一份，用完即可释放
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
            PcIconItem it = Selected();
            bool removable = it != null && it.Removable;
            _remove.Enabled = !_busy && removable;
            _miRemove.Enabled = _remove.Enabled;
            _miGuid.Enabled = it != null;
            if (it != null && !it.Removable) { _remove.Text = "系统自带（不给移除）"; }
            else { _remove.Text = "移除这颗…"; }
            int pending = 0;
            try { pending = PcIcons.PendingRecords(); } catch { }
            _restore.Enabled = !_busy && pending > 0;
        }

        private PcIconItem Selected()
        {
            if (_list.SelectedItems.Count == 0) { return null; }
            return _list.SelectedItems[0].Tag as PcIconItem;
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

        private void CopyGuid()
        {
            PcIconItem it = Selected();
            if (it == null) { return; }
            try
            {
                Clipboard.SetDataObject(it.Guid, true);
                _status.ForeColor = _theme.BarText;
                _status.Text = "已复制 GUID：" + it.Guid;
            }
            catch
            {
                Flash("剪贴板被别的程序占着，没复制成。GUID = " + it.Guid);
            }
        }

        // ------------------------------------------------------------------ 移除（单颗）

        private void DoRemoveOne()
        {
            if (_busy) { return; }
            PcIconItem it = Selected();
            if (it == null) { Flash("先选中一行。"); return; }
            if (!it.Removable)
            {
                Flash("这颗是系统自带的挂载点，这一页不会动它。");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要移除这颗「此电脑」图标"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            string name = it.Name.Length > 0 ? it.Name : "（来历不明）";
            StringBuilder body = new StringBuilder();
            body.Append("要移除这颗「此电脑」图标吗？").AppendLine();
            body.AppendLine();
            body.Append("  名称：").AppendLine(name);
            body.Append("  GUID：").AppendLine(it.Guid);
            body.Append("  挂在：").AppendLine(it.SourceText() + (it.InHkcu && it.InHklm ? "（HKCU + HKLM 两层同名，会一起删）" : ""));
            if (it.Icon.Length > 0) { body.Append("  图标：").AppendLine(it.Icon); }
            if (it.Command.Length > 0) { body.Append("  双击：").AppendLine(it.Command); }
            body.AppendLine();
            body.AppendLine("只删挂载点：软件本身不删、照常能用，图标马上从「此电脑」消失（不用重启资源管理器）。");
            body.AppendLine("系统自带的条目一颗都不会动。移除前会把原值记到：" + PcIcons.RecordFile());
            body.AppendLine("删完会一处一处读回来核对；想放回去，点「还原成移除前」。");
            body.AppendLine();
            body.AppendLine("要动的是注册表（HKLM 那一层），需要管理员权限。软件重启 / 重装可能把图标写回来 —— 那要进软件设置里关。");

            using (ConfirmForm f = new ConfirmForm("要移除这颗「此电脑」图标吗？", body.ToString(), "移除这颗", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (!Launcher.IsAdmin())
            {
                OfferElevation("移除");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在移除「" + name + "」…";
            Application.DoEvents();
            Logger.Write("此电脑多余图标", "移除：" + it.Guid + "（" + name + "）");
            PcIconsReport rep = PcIcons.Remove(new List<PcIconItem> { it });
            Finish(rep, "移除");
        }

        private void DoRestore()
        {
            if (_busy) { return; }
            if (!Consent.EnsureAccepted(this, _theme, "要把「此电脑」图标放回移除之前的样子"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            int pending = 0;
            try { pending = PcIcons.PendingRecords(); } catch { }
            if (pending == 0)
            {
                Flash("没有待还原的记录（这台机器还没用这个窗口移除过「此电脑」图标）。");
                return;
            }
            string body = "会把之前移除的 " + pending.ToString(CultureInfo.InvariantCulture) + " 颗挂载点按记录放回去："
                + Environment.NewLine + Environment.NewLine
                + "  · 原来挂在用户层（HKCU）的 → 在用户层重建；" + Environment.NewLine
                + "  · 原来挂在系统层（HKLM）的 → 在系统层重建。" + Environment.NewLine + Environment.NewLine
                + "如果有哪一颗后来被软件自己加回来了，那一颗会跳过并在报告里写出来（不会覆盖它）。"
                + Environment.NewLine + Environment.NewLine
                + "记录文件：" + PcIcons.RecordFile();
            using (ConfirmForm f = new ConfirmForm("要还原成移除前吗？", body, "还原", _theme))
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
            Logger.Write("此电脑多余图标", "还原：" + pending + " 条记录");
            PcIconsReport rep = PcIcons.Restore();
            Finish(rep, "还原");
        }

        private void Finish(PcIconsReport rep, string what)
        {
            if (IsDisposed) { return; }
            SetBusyUi(false);
            string line = what + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
            Logger.Write("此电脑多余图标", what + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            RefreshState(line);
            if (rep.NeedAdmin && !Launcher.IsAdmin())
            {
                Flash(what + "时有几颗因为权限没删成 —— 点它们会弹 UAC，用管理员重开窗口之后再点一次。");
            }
        }

        // ------------------------------------------------------------------ 不是管理员

        /// <summary>不是管理员时的两条路（同「文件关联体检」「端口与进程」）：说清楚 → 弹一次 UAC
        /// 带管理员重开这个窗口 → 重开之后**要再点一次**。命令行那条 `pcicons` 只开窗口，
        /// 不带任何"要改什么"。</summary>
        private void OfferElevation(string what)
        {
            string body = "移除 / 还原「此电脑」图标（写 " + PcIcons.RootText() + "）要用管理员权限才行。"
                + Environment.NewLine + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开。"
                + "因为写注册表只能由你在窗口里点出来，所以重开之后要再点一次「" + what + "」才会真的生效。";
            using (ConfirmForm f = new ConfirmForm("需要管理员权限", body, "确定", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath, "pcicons");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("此电脑多余图标", "不是管理员：已请求以管理员身份重开窗口");
                Close();
            }
            catch (Exception ex)
            {
                Flash("请求管理员权限失败：" + ex.Message);
                Logger.Write("此电脑多余图标", "请求管理员权限失败：" + ex.Message);
            }
        }
    }
}
