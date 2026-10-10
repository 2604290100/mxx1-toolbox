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
    /// <summary>「右键菜单管理」窗口：按位置（文件 / 文件夹 / 文件夹空白处 / 桌面 / 磁盘）列出右键菜单里
    /// 真实存在的项，选中一行可以 **禁用 / 恢复 / 删除**。
    ///
    /// 几个口径（对应用户 2026-10-09 的拍板）：
    /// ① **5 页**：他要的「文件 / 文件夹 / 桌面」三页，加上「文件夹空白处」和「磁盘」——
    ///    「在此处打开终端」「Git Bash Here」这类就装在空白处，少了它看着像漏项；
    /// ② **扩展项只显示不给动**（火绒 / 网盘 / 压缩软件那一类 DLL 扩展）：那一套要多写一份系统级
    ///    黑名单、必须重启资源管理器、而且禁了是所有位置一起没了，不在这一版；列表里照样列出来，
    ///    让用户知道"为什么它在菜单里而这里点不动"；
    /// ③ **要管理员的项走 UAC**：选了「提权重开窗口，让我再点一次」，而不是"自动做完" ——
    ///    "自动做完"需要给命令行加一个**带动作**的参数，那会破掉"命令行只有只读入口"这条底线
    ///    （回归 L07 钉着它），所以重开时只带"打开窗口 + 聚焦哪一行"，动作仍然只能由用户点出来；
    /// ④ 三个动作都会过自家的确认框（和主界面里"会改动系统的按钮"同一条规矩），
    ///    只有"设置里关掉了确认"时才不问。
    ///
    /// 高度不随内容长（列表占满中间、动作报告进下面那个只读框）—— 这个窗口是"看列表"的，
    /// 不适合像解锁窗口那样按内容自适应。</summary>
    internal sealed class CtxMenuForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly bool _confirmDangerous;  // 设置里关掉了确认就不弹（和主界面同一个开关）
        private string _zoneId = "files";
        private List<CtxEntry> _rows = new List<CtxEntry>();
        private bool _busy;

        private TableLayoutPanel _root;
        private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private Label _head;
        private Label _detail;
        private ListView _list;
        private TextBox _report;
        private Label _rule;
        private Button _disableBtn, _restoreBtn, _deleteBtn, _refreshBtn, _backupBtn;

        private const int TextWidth = 900;

        /// <summary>`focus` 是 `--focus=&lt;位置id&gt;|&lt;键名&gt;`（提权重开时用：自动帮你选中原来那一行）。
        /// 空 = 打开第一页。</summary>
        public CtxMenuForm(string focus)
        {
            _theme = Theme.Resolve(Settings.Load().Theme);
            _confirmDangerous = Settings.Load().ConfirmDangerous;

            string focusVerb = "";
            if (focus != null && focus.Length > 0)
            {
                int bar = focus.IndexOf('|');
                if (bar > 0)
                {
                    _zoneId = focus.Substring(0, bar).Trim().Trim('"');
                    focusVerb = focus.Substring(bar + 1).Trim().Trim('"');
                }
                else
                {
                    _zoneId = focus.Trim().Trim('"');
                }
            }
            _zoneId = CtxMenu.ZoneOf(_zoneId).Id;

            Text = "右键菜单管理";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(980, 620);
            MinimumSize = new Size(820, 520);
            BackColor = _theme.FormBack;
            try { Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point); }
            catch { }

            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.Padding = new Padding(14, 12, 14, 12);
            _root.BackColor = _theme.FormBack;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 页签
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 标题 + 小结
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 列表
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 选中项详情
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f)); // 动作结果（只读框）
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 按钮行
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 底部规则

            BuildTabs();
            BuildHead();
            BuildList();
            BuildDetail();
            BuildReportBox();
            BuildBar();
            BuildRule();

            Controls.Add(_root);
            Native.ApplyDarkTitleBar(Handle, _theme.DarkMode);
            Native.ApplyDarkControl(_report.Handle, _theme.DarkMode);

            // 提权重开时：那一行自动选好，并且把"再点一次"写在报告框里
            if (focusVerb.Length > 0)
            {
                _report.Text = "带你回到了刚才那一项。" + Environment.NewLine
                    + (CtxMenu.IsAdmin()
                        ? "现在有管理员权限了，再点一次刚才那个按钮（这一次会真的生效）。"
                        : "（没有拿到管理员权限。）");
            }
            else if (CtxMenu.IsAdmin())
            {
                _report.Text = "当前窗口有管理员权限：用户区和系统区的项都能改。" + Environment.NewLine;
            }

            SelectZone(_zoneId, focusVerb);
        }

        // ------------------------------------------------------------------ 搭界面

        private void BuildTabs()
        {
            TableLayoutPanel bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.Margin = new Padding(0);
            bar.RowCount = 1;
            bar.ColumnCount = CtxMenu.Zones.Length;
            bar.BackColor = _theme.FormBack;
            for (int i = 0; i < CtxMenu.Zones.Length; i++)
            {
                bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / CtxMenu.Zones.Length));
            }
            foreach (CtxZone z in CtxMenu.Zones)
            {
                Button b = new Button();
                b.Text = z.Label;
                b.Tag = z.Id;
                b.Dock = DockStyle.Fill;
                b.Margin = new Padding(1, 2, 1, 6);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.UseVisualStyleBackColor = false;
                b.Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
                string id = z.Id;
                b.Click += delegate { SelectZone(id, ""); };
                _tabButtons[id] = b;
                bar.Controls.Add(b);
            }
            _root.Controls.Add(bar, 0, 0);
        }

        private void BuildHead()
        {
            _head = new Label();
            _head.AutoSize = true;
            _head.MaximumSize = new Size(TextWidth, 0);
            _head.ForeColor = _theme.InputText;
            _head.Margin = new Padding(2, 0, 2, 8);
            _root.Controls.Add(_head, 0, 1);
        }

        private void BuildList()
        {
            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.BackColor = _theme.InputBack;
            _list.ForeColor = _theme.InputText;
            _list.Margin = new Padding(2, 0, 2, 8);
            _list.Columns.Add("菜单上显示的字", 320, HorizontalAlignment.Left);
            _list.Columns.Add("状态", 100, HorizontalAlignment.Left);
            _list.Columns.Add("在哪儿", 165, HorizontalAlignment.Left);
            _list.Columns.Add("点下去会跑什么", 355, HorizontalAlignment.Left);
            _list.SelectedIndexChanged += delegate { UpdateDetail(); UpdateButtons(); };
            _root.Controls.Add(_list, 0, 2);
        }

        private void BuildDetail()
        {
            _detail = new Label();
            _detail.AutoSize = true;
            _detail.MaximumSize = new Size(TextWidth, 0);
            _detail.ForeColor = _theme.BarText;
            _detail.Margin = new Padding(2, 0, 2, 8);
            _root.Controls.Add(_detail, 0, 3);
        }

        private void BuildReportBox()
        {
            _report = new TextBox();
            _report.Dock = DockStyle.Fill;
            _report.Multiline = true;
            _report.ReadOnly = true;
            _report.ScrollBars = ScrollBars.Vertical;
            _report.WordWrap = true;
            _report.BackColor = _theme.LogBack;
            _report.ForeColor = _theme.LogText;
            _report.BorderStyle = BorderStyle.FixedSingle;
            _report.Margin = new Padding(2, 0, 2, 8);
            _report.Text = "这里会说清楚每一步到底写了哪个键。" + Environment.NewLine
                + "（「禁用」＝给那一项加一个隐藏开关，键本身不动；「删除」＝先备份成 .reg 再删。）";
            _root.Controls.Add(_report, 0, 4);
        }

        private void BuildBar()
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.WrapContents = false;
            bar.Margin = new Padding(0);
            bar.BackColor = _theme.FormBack;

            _disableBtn = MakeButton("禁用");
            _disableBtn.Click += delegate { DoDisable(); };
            bar.Controls.Add(_disableBtn);

            _restoreBtn = MakeButton("恢复");
            _restoreBtn.Click += delegate { DoRestore(); };
            bar.Controls.Add(_restoreBtn);

            _deleteBtn = MakeButton("删除（先备份）");
            _deleteBtn.Click += delegate { DoDelete(); };
            bar.Controls.Add(_deleteBtn);

            _refreshBtn = MakeButton("刷新");
            _refreshBtn.Click += delegate { Reload(); };
            bar.Controls.Add(_refreshBtn);

            _backupBtn = MakeButton("打开备份文件夹");
            _backupBtn.Click += delegate { OpenBackupDir(); };
            bar.Controls.Add(_backupBtn);

            Button close = MakeButton("关闭");
            close.Click += delegate { Close(); };
            bar.Controls.Add(close);
            CancelButton = close;

            _root.Controls.Add(bar, 0, 5);
        }

        private void BuildRule()
        {
            _rule = new Label();
            _rule.AutoSize = true;
            _rule.MaximumSize = new Size(TextWidth, 0);
            _rule.ForeColor = _theme.BarText;
            _rule.Margin = new Padding(2, 0, 2, 0);
            _rule.Text =
                "· 「禁用」只是给那一项加一个隐藏开关：键和标题原样留着，随时能「恢复」。"
                + Environment.NewLine
                + "· 「删除」会先把那个键导出成一份 .reg（在备份文件夹里），备份没成功就绝不删。"
                + Environment.NewLine
                + "· 改完立刻生效，不用重启资源管理器 —— 静态菜单项是每次右键现场拼的（2026-10-09 实测）。"
                + Environment.NewLine
                + "· 扩展项（DLL，比如压缩软件 / 网盘那一类）这一版只显示、不给动：那是另一套机制，"
                + "要多写一份系统级黑名单、必须重启资源管理器，而且禁了是所有位置一起没了。";
            _root.Controls.Add(_rule, 0, 6);
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

        // ------------------------------------------------------------------ 读列表

        private void SelectZone(string zoneId, string focusVerb)
        {
            _zoneId = zoneId;
            Reload();
            if (focusVerb != null && focusVerb.Length > 0) { SelectVerb(focusVerb); }
        }

        private void Reload()
        {
            string keep = "";
            CtxEntry sel = Selected();
            if (sel != null) { keep = sel.Verb; }

            _rows = CtxMenu.List(_zoneId);
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (CtxEntry e in _rows)
                {
                    ListViewItem it = new ListViewItem(new string[]
                    {
                        e.Title, e.StateLabel, e.WhereLabel, CommandText(e)
                    });
                    it.Tag = e;
                    // 灰字 = 这一行点不动（已禁用 / 扩展项），和"能操作的项"一眼分得开
                    if (!e.Actionable || e.Disabled) { it.ForeColor = _theme.BarText; }
                    _list.Items.Add(it);
                }
            }
            finally { _list.EndUpdate(); }

            RefreshTabs();
            CtxZone z = CtxMenu.ZoneOf(_zoneId);
            StringBuilder sb = new StringBuilder();
            sb.Append('[').Append(z.Label).Append("] ").Append(z.Note).Append(" —— ");
            sb.Append(CtxMenu.Summary(_rows));
            if (CtxMenu.IsTestRoot)
            {
                sb.Append(Environment.NewLine).Append("（测试根：这次读写的不是真实的右键菜单）");
            }
            _head.Text = sb.ToString();

            if (keep.Length > 0) { SelectVerb(keep); }
            UpdateDetail();
            UpdateButtons();
        }

        private void RefreshTabs()
        {
            foreach (KeyValuePair<string, Button> kv in _tabButtons)
            {
                bool active = string.Equals(kv.Key, _zoneId, StringComparison.OrdinalIgnoreCase);
                kv.Value.BackColor = active ? _theme.TabActiveBack : _theme.TabBack;
                kv.Value.ForeColor = active ? _theme.TabActiveText : _theme.TabText;
                kv.Value.FlatAppearance.BorderColor = active ? _theme.TabActiveUnderline : _theme.ButtonBorder;
                kv.Value.FlatAppearance.MouseOverBackColor = active ? _theme.TabActiveBack : _theme.ButtonHover;
                kv.Value.FlatAppearance.MouseDownBackColor = active ? _theme.TabActiveBack : _theme.ButtonPressed;
            }
        }

        private string CommandText(CtxEntry e)
        {
            string s = (e.Command.Length > 0) ? e.Command : e.Note;
            if (s.Length == 0) { return ""; }
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length > 120) { s = s.Substring(0, 120) + "…"; }
            return s;
        }

        private CtxEntry Selected()
        {
            if (_list.SelectedItems.Count == 0) { return null; }
            return _list.SelectedItems[0].Tag as CtxEntry;
        }

        private void SelectVerb(string verb)
        {
            foreach (ListViewItem it in _list.Items)
            {
                CtxEntry e = it.Tag as CtxEntry;
                if (e == null) { continue; }
                if (string.Equals(e.Verb, verb, StringComparison.OrdinalIgnoreCase))
                {
                    it.Selected = true;
                    it.Focused = true;
                    it.EnsureVisible();
                    return;
                }
            }
        }

        private void UpdateDetail()
        {
            CtxEntry e = Selected();
            if (e == null)
            {
                _detail.Text = "选中一行看它的注册表键和完整命令。";
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("键：").Append(e.InUser ? e.UserKey : e.MachineKey);
            if (e.InMachine && e.InUser) { sb.Append("　（系统区还有一份：" ).Append(e.MachineKey).Append('）'); }
            if (e.Note.Length > 0) { sb.Append(Environment.NewLine).Append(e.Note); }
            _detail.Text = sb.ToString();
        }

        private void UpdateButtons()
        {
            CtxEntry e = Selected();
            bool can = (e != null) && e.Actionable && !_busy;
            _disableBtn.Enabled = can && !e.Disabled;
            _restoreBtn.Enabled = can && e.Disabled;
            _deleteBtn.Enabled = can;
            _refreshBtn.Enabled = !_busy;
        }

        // ------------------------------------------------------------------ 三个动作

        private void DoDisable()
        {
            CtxEntry e = Selected();
            if (e == null || _busy) { return; }
            if (!BeforeWrite("禁用", e)) { return; }
            if (!Confirm("要禁用「" + e.Title + "」吗？",
                "位置：" + e.ZoneLabel + Environment.NewLine
                + "它现在的状态：" + e.StateLabel + Environment.NewLine + Environment.NewLine
                + "会怎么做：给这个菜单项加一个隐藏开关 —— 注册表里的键和标题一个字都不动，"
                + "只是右键菜单里不再出现它。" + Environment.NewLine
                + "后悔了怎么办：回来选中这一行点「恢复」，它会原样回来。" + Environment.NewLine
                + "生效时间：立刻（不用重启资源管理器）。", "禁用"))
            {
                return;
            }
            Run("禁用", delegate(CtxEntry x) { bool ok; return CtxMenu.Disable(x, out ok); });
        }

        private void DoRestore()
        {
            CtxEntry e = Selected();
            if (e == null || _busy) { return; }
            if (!BeforeWrite("恢复", e)) { return; }
            if (!Confirm("要恢复「" + e.Title + "」吗？",
                "位置：" + e.ZoneLabel + Environment.NewLine + Environment.NewLine
                + "会怎么做：把那个隐藏开关删掉 —— 右键菜单里就又能看到它了。" + Environment.NewLine
                + "生效时间：立刻（不用重启资源管理器）。", "恢复"))
            {
                return;
            }
            Run("恢复", delegate(CtxEntry x) { bool ok; return CtxMenu.Restore(x, out ok); });
        }

        private void DoDelete()
        {
            CtxEntry e = Selected();
            if (e == null || _busy) { return; }
            if (!BeforeWrite("删除", e)) { return; }
            StringBuilder body = new StringBuilder();
            body.Append("位置：").Append(e.ZoneLabel).Append(Environment.NewLine);
            if (e.Command.Length > 0) { body.Append("命令：").Append(e.Command).Append(Environment.NewLine); }
            body.Append(Environment.NewLine);
            body.Append("会怎么做：先把注册表里这一个键导出成一份 .reg 放到备份文件夹，"
                + "备份没成功就绝不删；然后再删掉那个键。").Append(Environment.NewLine);
            body.Append("后悔了怎么办：双击备份文件夹里那个 .reg，菜单项就回来了"
                + "（也可以在窗口里点「打开备份文件夹」）。").Append(Environment.NewLine);
            if (e.Ours)
            {
                body.Append("⚠ 这是工具箱自己装的项：正规做法是回「右键增强」页点对应的「撤掉…」按钮。")
                    .Append(Environment.NewLine);
            }
            if (e.Systemish)
            {
                body.Append("⚠ 这一项看着是 Windows 自带的（键名以 Windows. 开头）：删了可能影响系统功能，"
                    + "建议改用「禁用」。").Append(Environment.NewLine);
            }
            body.Append("只删这一个键，别的一个都不动。");
            if (!Confirm("要删除「" + e.Title + "」吗？", body.ToString(), "删除"))
            {
                return;
            }
            Run("删除", delegate(CtxEntry x)
            {
                bool ok; string backup; return CtxMenu.Delete(x, out backup, out ok);
            });
        }

        /// <summary>写之前的两道门：① 条款同意（界面里"会改动系统"的动作都要过）；
        /// ② 这一项在系统区、而现在不是管理员 —— 走提权那条路（提权重开窗口，让用户再点一次）。</summary>
        private bool BeforeWrite(string action, CtxEntry e)
        {
            if (!Consent.EnsureAccepted(this, _theme, "右键菜单管理：" + action + "「" + e.Title + "」"))
            {
                SetReport("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return false;
            }
            if (e.NeedsAdmin && !CtxMenu.IsAdmin())
            {
                return OfferElevation(action, e);
            }
            return true;
        }

        /// <summary>需要管理员：说清楚，然后提权重开这个窗口（只带"打开窗口 + 聚焦哪一行"，
        /// **不带动作** —— 命令行里不存在"能写注册表的参数"，底线不破）。
        /// 返回值永远是 false：本窗口要么关掉（提权成功），要么什么都不做。</summary>
        private bool OfferElevation(string action, CtxEntry e)
        {
            bool go = Confirm("这一项在系统区，需要管理员权限",
                "「" + e.Title + "」装在系统区（" + e.ZoneLabel + "），改它要管理员权限。"
                + Environment.NewLine + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，"
                + "然后带管理员权限重新打开，并自动帮你选中刚才那一行。" + Environment.NewLine
                + "因为写注册表只能由你在界面上点出来（工具箱的命令行故意没有能写注册表的参数），"
                + "所以**重开之后要再点一次**「" + action + "」，那一次才会真的生效。",
                "确定")
                ;
            if (!go) { return false; }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                    AppPaths.ExePath,
                    "rightmenu manage --focus=\"" + _zoneId + "|" + e.Verb + "\"");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("右键菜单管理", "需要管理员：已请求以管理员身份重开窗口（" + e.Title + "）");
                Close();
            }
            catch (Exception ex)
            {
                SetReport("请求管理员权限失败：" + ex.Message);
                Logger.Write("右键菜单管理", "请求管理员权限失败：" + ex.Message);
            }
            return false;
        }

        private void Run(string action, Func<CtxEntry, string> work)
        {
            CtxEntry e = Selected();
            if (e == null) { return; }
            _busy = true;
            UpdateButtons();
            string report;
            try { report = work(e); }
            catch (Exception ex) { report = action + "失败：" + ex.Message; }
            _busy = false;
            Logger.Write("右键菜单管理", report);
            string keep = e.Verb;
            Reload();
            SelectVerb(keep);
            _report.Text = report + Environment.NewLine + Environment.NewLine + CtxMenu.LastAction();
            _report.SelectionStart = 0;
            _report.SelectionLength = 0;
        }

        private bool Confirm(string title, string body, string okText)
        {
            if (!_confirmDangerous) { return true; }
            using (ConfirmForm f = new ConfirmForm(title, body, okText, _theme))
            {
                return f.ShowDialog(this) == DialogResult.OK;
            }
        }

        private void SetReport(string text)
        {
            _report.Text = text;
            _report.SelectionStart = 0;
            _report.SelectionLength = 0;
        }

        private void OpenBackupDir()
        {
            try
            {
                System.IO.Directory.CreateDirectory(CtxMenu.BackupDir);
                System.Diagnostics.ProcessStartInfo psi =
                    new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + CtxMenu.BackupDir + "\"");
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                SetReport("打开备份文件夹失败：" + ex.Message + Environment.NewLine + CtxMenu.BackupDir);
            }
        }
    }
}
