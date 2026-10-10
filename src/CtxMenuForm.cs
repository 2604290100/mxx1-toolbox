// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「右键菜单管理」窗口：按位置（文件 / 文件夹 / 文件夹空白处 / 桌面 / 磁盘）列出右键菜单里
    /// 真实存在的项，勾上几项就能 **禁用 / 恢复 / 删除**。
    ///
    /// ## 这个窗口长什么样，是 2026-10-09 用户挑出来的（别自己改回去）
    ///
    /// 用户原话：「**右键菜单管理 的界面是不是可以优化一下，给我几个方案看看。最好就是和主窗口
    /// 【萌新工具箱】界面统一风格**」。给了他三个方案（只换外壳 / 宽行卡片 / 左右分栏），他选
    /// **宽行卡片**；交互也定了：**勾选多选 + 底栏动作**、**禁用和恢复不弹确认框**（完全可逆）、
    /// **动作记录里每一条都能撤销**。所以内容区**不用 `ListView`**（那是"表格脸"，和主窗口不是
    /// 一种气质），照主窗口那套搭：
    ///
    /// ```
    /// 页签行（同款：TabBack / 当前页 TabActiveBack + 蓝边；**高 30，写死的**；每个页签带条数）
    /// 搜索行：[ 输入名字过滤… ] [全部][未禁用][程序装的][已禁用][扩展项]   改完立刻生效，不用重启资源管理器
    /// 内容区：段标题（灰色小字 + 一条细线）＋ 一行行卡片（勾选框 / 图标 / 名称 / 灰色副标题 / 右侧状态）
    /// 动作记录（可收起，Ctrl+L）：每条后面挂「恢复 / 再禁一次 / 还原来」
    /// 底栏（同款：BarBack + 右边一排小按钮）：已勾选 N 项 · 共 M 条（…）  [禁用选中的 N 项][恢复…][删除…][刷新][关闭]
    /// ```
    ///
    /// ## 三条"照主窗口"的高度规矩（2026-10-09 看实图定的，别写死数字）
    /// ① **页签行 `Absolute(TabBarHeight = 30)`**：这一行我第一版写成 `AutoSize`，`AutoSize` 行 +
    ///    `Dock=Fill` 的页签按钮互相喂高度，5 个页签被撑到 **94px 高** —— 用户原话
    ///    「上面5个大按钮…差点意思」（用 `PrintWindow` 拍实图量的，主窗口那边一直是写死 30）；
    /// ② **小按钮高度按字体算**：`max(24, 量「国」字高 + 8)`（底栏 / 段标题的展开收起 / 记录里那个
    ///    撤销按钮全用它，底栏高 = 按钮高 + 4）。第一版写死 20 → 用户一眼看出「字被切了」；
    ///    主窗口早就为这一条踩过坑（22px 时底栏每个标签的最后一行墨迹被切掉，回归 D01e）；
    /// ③ 段标题行高 = 小按钮高度 + 10，跟着它走。
    ///
    /// ## 其余不能松的口径
    /// ① **禁用 / 恢复不弹确认框**（用户拍板）：完全可逆，动作记录里能一键撤销；**删除仍然弹**
    ///    （列清单 + 说清备份在哪）；
    /// ② **切页 / 改筛选 / 改搜索 = 清空勾选** —— 否则会出现"勾了 3 项、其中 2 项被筛掉了，
    ///    点「删除选中的 3 项」到底删几条"这种没人说得清的状态；
    /// ③ **底栏按钮上的数字跟着勾选实时变**，没勾任何一项时是灰的（绝不猜你要动哪一条）；
    /// ④ `--focus=&lt;位置&gt;|&lt;键名&gt;[,&lt;键名&gt;…]` 只负责"打开窗口 + 把这几项勾上"（提权重开时把用户
    ///    刚才的勾选带过来）；**命令行里不存在"带动作"的参数**，写注册表只能由用户点出来；
    /// ⑤ 要管理员的项：弹一次 UAC，带管理员权限重开窗口（勾选会跟过来），再点一次才生效。</summary>
    internal sealed class CtxMenuForm : Mxx1Form
    {
        /// <summary>页签行高度 —— 和主窗口同一个数（`MainForm.TabBarHeight`）。**必须写死**，
        /// 别改回 `AutoSize`（理由见类说明 ①）。</summary>
        private const int TabBarHeight = 30;
        private const int ActionPanelHeight = 170;
        private const int MaxActionLines = 20;

        /// <summary>小按钮的统一高度（底栏 / 段标题的展开收起 / 动作记录里的撤销按钮）。
        /// **照主窗口的算法算、别写死**（理由见类说明 ②）。</summary>
        private readonly int _barHeight;
        private readonly int _statusBarHeight;
        private readonly int _captionRowHeight;

        private readonly Theme _theme;
        private readonly bool _confirmDangerous;
        private string _zoneId = "files";
        private string _filter = "all";
        private string _search = "";
        private bool _busy;

        private List<CtxEntry> _all = new List<CtxEntry>();
        private readonly List<CtxRow> _rows = new List<CtxRow>();
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _zoneCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private TableLayoutPanel _root;
        private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> _filterButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private Panel _searchRow;
        private TextBox _searchBox;
        private Label _hint;
        private Panel _content;
        private TableLayoutPanel _list;
        private Panel _actionPanel;
        private TableLayoutPanel _actionList;
        private Label _status;
        private Button _logBtn, _disableBtn, _restoreBtn, _deleteBtn, _refreshBtn, _closeBtn;
        private ToolTip _tips;

        /// <summary>`focus` = `--focus=&lt;位置id&gt;|&lt;键名&gt;[,&lt;键名&gt;…]`（空 = 打开第一页）。</summary>
        public CtxMenuForm(string focus)
        {
            _theme = Theme.Resolve(Settings.Load().Theme);
            _confirmDangerous = Settings.Load().ConfirmDangerous;
            _tips = new ToolTip();
            _tips.AutoPopDelay = 20000;

            // 小按钮的高度从字体算出来（和主窗口同一套算法，见类说明 ②）
            using (Font barFont = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point))
            {
                _barHeight = TextRenderer.MeasureText("国", barFont).Height + 8;
                if (_barHeight < 24) { _barHeight = 24; }
            }
            _statusBarHeight = _barHeight + 4;      // 上下各 2px 留白
            _captionRowHeight = _barHeight + 10;    // 段标题行：一个按钮 + 上下留白

            string focusVerbs = "";
            if (focus != null && focus.Length > 0)
            {
                int bar = focus.IndexOf('|');
                if (bar > 0)
                {
                    _zoneId = focus.Substring(0, bar).Trim().Trim('"');
                    focusVerbs = focus.Substring(bar + 1).Trim().Trim('"');
                }
                else { _zoneId = focus.Trim().Trim('"'); }
            }
            _zoneId = CtxMenu.ZoneOf(_zoneId).Id;
            foreach (string id in CtxMenu.GroupIds) { _expanded[id] = !CtxMenu.GroupCollapsedByDefault(id); }

            Text = "右键菜单管理";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1000, 620);
            MinimumSize = new Size(860, 460);
            BackColor = _theme.FormBack;
            try { Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point); }
            catch { }

            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.Padding = new Padding(10, 8, 10, 0);
            _root.BackColor = _theme.FormBack;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, TabBarHeight));      // 0 页签（写死，别用 AutoSize）
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                    // 1 搜索 + 快选 + 常驻说明
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));               // 2 内容区
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));                // 3 动作记录（默认收起）
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, _statusBarHeight));  // 4 底栏

            BuildTabs();
            BuildSearchRow();
            BuildContent();
            BuildActionPanel();
            BuildStatusBar();

            Controls.Add(_root);
            KeyPreview = true;
            Native.ApplyDarkTitleBar(Handle, _theme.DarkMode);

            LoadRows();
            if (focusVerbs.Length > 0) { CheckVerbs(focusVerbs, CtxMenu.IsAdmin()); }
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
                b.Text = z.Label;                 // 真正的文本（带条数）由 RefreshTabTexts 填
                b.Tag = z.Id;
                b.Dock = DockStyle.Fill;
                b.Margin = new Padding(1, 2, 1, 0);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.UseVisualStyleBackColor = false;
                b.Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
                string id = z.Id;
                b.Click += delegate { SelectZone(id); };
                _tabButtons[id] = b;
                bar.Controls.Add(b);
            }
            _root.Controls.Add(bar, 0, 0);
        }

        private void BuildSearchRow()
        {
            _searchRow = new Panel();
            _searchRow.Dock = DockStyle.Fill;
            _searchRow.Height = _barHeight + 8;
            _searchRow.Margin = new Padding(0, 0, 0, 4);
            _searchRow.BackColor = _theme.FormBack;

            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.ColumnCount = 4;
            row.RowCount = 1;
            row.Margin = new Padding(0);
            row.BackColor = _theme.FormBack;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220f));    // 搜索框
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // 快选
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));     // 空
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // 常驻说明
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _searchBox = new TextBox();
            _searchBox.Dock = DockStyle.Fill;
            _searchBox.BorderStyle = BorderStyle.FixedSingle;
            _searchBox.Margin = new Padding(2, 4, 8, 4);
            _searchBox.BackColor = _theme.InputBack;
            _searchBox.ForeColor = _theme.InputText;
            _searchBox.TextChanged += delegate
            {
                _search = _searchBox.Text.Trim();
                if (!_busy) { ClearChecks(); BuildRows(); }
            };
            row.Controls.Add(_searchBox, 0, 0);

            FlowLayoutPanel filters = new FlowLayoutPanel();
            filters.Dock = DockStyle.Fill;
            filters.AutoSize = true;
            filters.FlowDirection = FlowDirection.LeftToRight;
            filters.WrapContents = false;
            filters.Margin = new Padding(0);
            filters.BackColor = _theme.FormBack;
            AddFilter(filters, "all", "全部");
            AddFilter(filters, "active", "未禁用");
            AddFilter(filters, "apps", "程序装的");
            AddFilter(filters, "disabled", "已禁用");
            AddFilter(filters, "shellex", "扩展项");
            row.Controls.Add(filters, 1, 0);

            // 常驻那句：回答"是不是要重启资源管理器才生效"（不然用户会怀疑没生效）
            _hint = new Label();
            _hint.AutoSize = true;
            _hint.Anchor = AnchorStyles.Right;
            _hint.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            _hint.ForeColor = _theme.BarText;
            _hint.Text = "改完立刻生效，不用重启资源管理器";
            _hint.Margin = new Padding(8, 0, 4, 0);
            row.Controls.Add(_hint, 3, 0);

            _searchRow.Controls.Add(row);
            _root.Controls.Add(_searchRow, 0, 1);
            _tips.SetToolTip(_hint, "静态菜单项是每次右键现场拼的 —— 2026-10-09 用只读探针在五个位置实测过。");
        }

        private void AddFilter(FlowLayoutPanel host, string id, string text)
        {
            Button b = new Button();
            b.Text = text;
            b.Tag = id;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(6, 2, 6, 2);
            b.Margin = new Padding(0, 2, 6, 2);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            b.Click += delegate { SelectFilter(id); };
            _filterButtons[id] = b;
            host.Controls.Add(b);
        }

        private void BuildContent()
        {
            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.Margin = new Padding(0);
            _content.AutoScroll = true;
            _content.BackColor = _theme.FormBack;

            _list = new TableLayoutPanel();
            _list.Dock = DockStyle.Top;
            _list.AutoSize = true;
            _list.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _list.ColumnCount = 1;
            _list.Margin = new Padding(0);
            _list.BackColor = _theme.FormBack;
            _list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _content.Controls.Add(_list);
            _root.Controls.Add(_content, 0, 2);
        }

        private void BuildActionPanel()
        {
            _actionPanel = new Panel();
            _actionPanel.Dock = DockStyle.Fill;
            _actionPanel.Margin = new Padding(0, 4, 0, 4);
            _actionPanel.AutoScroll = true;
            _actionPanel.BackColor = _theme.FormBack;
            _actionPanel.Visible = false;

            _actionList = new TableLayoutPanel();
            _actionList.Dock = DockStyle.Top;
            _actionList.AutoSize = true;
            _actionList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _actionList.ColumnCount = 1;
            _actionList.Margin = new Padding(0);
            _actionList.BackColor = _theme.FormBack;
            _actionList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _actionPanel.Controls.Add(_actionList);
            _root.Controls.Add(_actionPanel, 0, 3);
        }

        private void BuildStatusBar()
        {
            TableLayoutPanel bar = new TableLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.Margin = new Padding(0);
            bar.ColumnCount = 2;
            bar.RowCount = 1;
            bar.Padding = new Padding(6, 0, 4, 0);
            bar.BackColor = _theme.BarBack;
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _status = new Label();
            _status.AutoSize = false;
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.AutoEllipsis = true;
            _status.Margin = new Padding(0);
            _status.BackColor = _theme.BarBack;
            _status.ForeColor = _theme.BarText;
            bar.Controls.Add(_status, 0, 0);

            FlowLayoutPanel right = new FlowLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.AutoSize = true;
            right.FlowDirection = FlowDirection.LeftToRight;
            right.WrapContents = false;
            right.Margin = new Padding(0);
            right.BackColor = _theme.BarBack;

            _logBtn = MakeBarButton("动作记录", "展开或收起动作记录（Ctrl+L）—— 每条后面能直接撤销");
            _logBtn.Click += delegate { ToggleActionPanel(); };
            right.Controls.Add(_logBtn);

            _disableBtn = MakeBarButton("禁用选中的 0 项", "给选中的项加一个隐藏开关：键和标题原样留着，随时能「恢复」");
            _disableBtn.Click += delegate { DoAction("禁用"); };
            right.Controls.Add(_disableBtn);

            _restoreBtn = MakeBarButton("恢复选中的 0 项", "把隐藏开关删掉，选中的项回到右键菜单里");
            _restoreBtn.Click += delegate { DoAction("恢复"); };
            right.Controls.Add(_restoreBtn);

            _deleteBtn = MakeBarButton("删除选中的 0 项", "先给每一个键各备份一份 .reg（备份没成功就不删），然后才删");
            _deleteBtn.Click += delegate { DoAction("删除"); };
            right.Controls.Add(_deleteBtn);

            _refreshBtn = MakeBarButton("刷新", "重新读一遍注册表（别的软件刚装完 / 刚卸完时用）");
            _refreshBtn.Click += delegate { LoadRows(); };
            right.Controls.Add(_refreshBtn);

            _closeBtn = MakeBarButton("关闭", "关掉这个窗口（不改任何东西）");
            _closeBtn.Click += delegate { Close(); };
            right.Controls.Add(_closeBtn);

            bar.Controls.Add(right, 1, 0);
            _root.Controls.Add(bar, 0, 4);
            CancelButton = _closeBtn;
        }

        private Button MakeBarButton(string text, string tip)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = false;
            b.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            b.Height = _barHeight;
            b.Width = TextRenderer.MeasureText(text, b.Font).Width + 12;
            b.Margin = new Padding(4, 2, 0, 2);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.BackColor = _theme.ButtonBack;
            b.ForeColor = _theme.ButtonText;
            b.FlatAppearance.BorderColor = _theme.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = _theme.ButtonHover;
            b.FlatAppearance.MouseDownBackColor = _theme.ButtonPressed;
            b.TabStop = false;
            _tips.SetToolTip(b, tip);
            return b;
        }

        private static void SetButtonText(Button b, string text)
        {
            if (b.Text == text) { return; }
            b.Text = text;
            b.Width = TextRenderer.MeasureText(text, b.Font).Width + 12;
        }

        // ------------------------------------------------------------------ 数据 → 界面

        private void SelectZone(string zoneId)
        {
            if (string.Equals(_zoneId, zoneId, StringComparison.OrdinalIgnoreCase)) { return; }
            _zoneId = zoneId;
            LoadRows();          // 切页会清空勾选（见类说明 ②）
        }

        private void SelectFilter(string id)
        {
            if (string.Equals(_filter, id, StringComparison.OrdinalIgnoreCase)) { return; }
            _filter = id;
            ClearChecks();
            BuildRows();
        }

        private void LoadRows()
        {
            ClearChecks();
            RefreshZoneCounts();
            RefreshTabTexts();
            BuildRows();
        }

        /// <summary>五个位置各自的条数（页签上写「文件 17」这种）。用户 2026-10-09 看过实图之后要的：
        /// 一眼知道哪一页有东西、哪一页是空的，省得一个个点过去看。
        /// 顺手把当前页那一份结果留下来当 `_all`，所以这里不会白白多读一遍。</summary>
        private void RefreshZoneCounts()
        {
            foreach (CtxZone z in CtxMenu.Zones)
            {
                List<CtxEntry> one = CtxMenu.List(z.Id);
                _zoneCounts[z.Id] = one.Count;
                if (string.Equals(z.Id, _zoneId, StringComparison.OrdinalIgnoreCase)) { _all = one; }
            }
        }

        private void RefreshTabTexts()
        {
            foreach (CtxZone z in CtxMenu.Zones)
            {
                Button b;
                if (!_tabButtons.TryGetValue(z.Id, out b) || b == null) { continue; }
                int n = 0;
                _zoneCounts.TryGetValue(z.Id, out n);
                b.Text = z.Label + " " + n.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void ClearChecks()
        {
            foreach (CtxRow r in _rows) { r.Checked = false; }
        }

        private bool VisibleEntry(CtxEntry e)
        {
            if (_filter == "active") { if (e.Disabled || e.Shellex) { return false; } }
            else if (_filter == "apps") { if (e.GroupId != CtxMenu.GroupApps) { return false; } }
            else if (_filter == "disabled") { if (!e.Disabled) { return false; } }
            else if (_filter == "shellex") { if (!e.Shellex) { return false; } }
            if (_search.Length == 0) { return true; }
            string needle = _search.ToLowerInvariant();
            return (e.Title.ToLowerInvariant().IndexOf(needle) >= 0)
                || (e.Verb.ToLowerInvariant().IndexOf(needle) >= 0)
                || (e.Command.ToLowerInvariant().IndexOf(needle) >= 0)
                || (e.Note.ToLowerInvariant().IndexOf(needle) >= 0);
        }

        /// <summary>把（筛选后的）项按四段摆出来：段标题 + 行卡片。</summary>
        private void BuildRows()
        {
            _rows.Clear();
            _list.SuspendLayout();
            try
            {
                foreach (Control c in GetControls(_list)) { c.Dispose(); }
                _list.Controls.Clear();
                _list.RowStyles.Clear();
                _list.RowCount = 0;

                int shown = 0;
                foreach (string groupId in CtxMenu.GroupIds)
                {
                    List<CtxEntry> mine = new List<CtxEntry>();
                    foreach (CtxEntry e in _all)
                    {
                        if (e.GroupId == groupId && VisibleEntry(e)) { mine.Add(e); }
                    }
                    if (mine.Count == 0) { continue; }
                    AddCaption(groupId, mine.Count);
                    if (!_expanded[groupId]) { continue; }
                    foreach (CtxEntry e in mine)
                    {
                        CtxRow row = new CtxRow(e, _theme, e.Actionable);
                        row.Toggled += delegate { UpdateBar(); };
                        row.Activated += delegate(object s, EventArgs a) { SetCurrent(s as CtxRow); };
                        row.Margin = new Padding(0, 0, 0, 1);
                        _list.RowStyles.Add(new RowStyle(SizeType.Absolute, CtxRow.RowHeight + 1));
                        _list.Controls.Add(row);
                        _rows.Add(row);
                        shown++;
                    }
                }
                if (shown == 0)
                {
                    Label none = new Label();
                    none.AutoSize = true;
                    none.Margin = new Padding(4, 10, 4, 10);
                    none.ForeColor = _theme.BarText;
                    none.Text = (_all.Count == 0)
                        ? "这个位置里什么都没有（右键菜单里没有静态项）。"
                        : "没有符合条件的项 —— 换个筛选或把搜索框清空试试。";
                    _list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    _list.Controls.Add(none);
                }
            }
            finally { _list.ResumeLayout(true); }

            UpdateTabColors();
            UpdateFilterColors();
            UpdateBar();
            RefreshActionList();
        }

        private static List<Control> GetControls(Control parent)
        {
            List<Control> list = new List<Control>();
            foreach (Control c in parent.Controls) { list.Add(c); }
            return list;
        }

        private void AddCaption(string groupId, int count)
        {
            TableLayoutPanel line = new TableLayoutPanel();
            line.Dock = DockStyle.Fill;
            line.Height = _captionRowHeight;
            line.Margin = new Padding(2, 6, 2, 4);
            line.ColumnCount = 3;
            line.RowCount = 1;
            line.BackColor = _theme.FormBack;
            line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            line.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Label cap = new Label();
            cap.AutoSize = true;
            cap.Anchor = AnchorStyles.Left;
            cap.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            cap.ForeColor = _theme.BarText;
            cap.BackColor = Color.Transparent;
            cap.TextAlign = ContentAlignment.MiddleLeft;
            cap.Margin = new Padding(0, 0, 6, 0);
            cap.Text = CtxMenu.GroupLabel(groupId) + "（" + count.ToString(CultureInfo.InvariantCulture) + "）"
                + " · " + CtxMenu.GroupNote(groupId);
            // 段标题本身也能点：不像按钮那么显眼，但点到它就展开 / 收起
            cap.Cursor = Cursors.Hand;
            cap.Click += delegate { ToggleGroup(groupId); };
            line.Controls.Add(cap, 0, 0);

            Button toggle = new Button();
            toggle.Text = _expanded[groupId] ? "收起" : "展开";
            toggle.Tag = groupId;
            toggle.AutoSize = false;
            toggle.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            toggle.Height = _barHeight;
            toggle.Width = TextRenderer.MeasureText(toggle.Text, toggle.Font).Width + 12;
            toggle.Margin = new Padding(0, 0, 8, 0);
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.FlatAppearance.BorderSize = 1;
            toggle.UseVisualStyleBackColor = false;
            toggle.BackColor = _theme.ButtonBack;
            toggle.ForeColor = _theme.ButtonText;
            toggle.FlatAppearance.BorderColor = _theme.ButtonBorder;
            toggle.FlatAppearance.MouseOverBackColor = _theme.ButtonHover;
            toggle.FlatAppearance.MouseDownBackColor = _theme.ButtonPressed;
            toggle.TabStop = false;
            toggle.Click += delegate { ToggleGroup(groupId); };
            line.Controls.Add(toggle, 1, 0);

            Panel rule = new Panel();
            rule.Dock = DockStyle.Fill;
            rule.Margin = new Padding(0, (_captionRowHeight / 2), 0, (_captionRowHeight / 2) - 1);
            rule.BackColor = _theme.SegmentLine;
            line.Controls.Add(rule, 2, 0);

            _list.RowStyles.Add(new RowStyle(SizeType.Absolute, _captionRowHeight));
            _list.Controls.Add(line);
        }

        private void ToggleGroup(string groupId)
        {
            _expanded[groupId] = !_expanded[groupId];
            BuildRows();
        }

        private void SetCurrent(CtxRow row)
        {
            foreach (CtxRow r in _rows) { r.Current = (r == row); }
        }

        private void UpdateTabColors()
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

        private void UpdateFilterColors()
        {
            foreach (KeyValuePair<string, Button> kv in _filterButtons)
            {
                bool active = string.Equals(kv.Key, _filter, StringComparison.OrdinalIgnoreCase);
                kv.Value.BackColor = active ? _theme.TabActiveBack : _theme.ButtonBack;
                kv.Value.ForeColor = active ? _theme.TabActiveText : _theme.ButtonText;
                kv.Value.FlatAppearance.BorderColor = active ? _theme.TabActiveUnderline : _theme.ButtonBorder;
                kv.Value.FlatAppearance.MouseOverBackColor = active ? _theme.TabActiveBack : _theme.ButtonHover;
                kv.Value.FlatAppearance.MouseDownBackColor = active ? _theme.TabActiveBack : _theme.ButtonPressed;
            }
        }

        /// <summary>底栏：左边那句小结 + 右边按钮上的数字（都跟着勾选实时变）。</summary>
        private void UpdateBar()
        {
            List<CtxRow> checkedRows = CheckedRows();
            int canDisable = 0, canRestore = 0;
            foreach (CtxRow r in checkedRows)
            {
                if (r.Entry.Disabled) { canRestore++; } else { canDisable++; }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("已勾选 ").Append(checkedRows.Count.ToString(CultureInfo.InvariantCulture)).Append(" 项");
            sb.Append(" · ").Append(CtxMenu.Summary(_all));
            CtxZone z = CtxMenu.ZoneOf(_zoneId);
            sb.Append(" · ").Append(z.Label);
            if (!string.Equals(_filter, "all", StringComparison.OrdinalIgnoreCase) && _filterButtons.ContainsKey(_filter))
            {
                sb.Append("（筛选中：").Append(_filterButtons[_filter].Text).Append("）");
            }
            if (_busy) { sb.Append(" · 正在处理…"); }
            _status.Text = sb.ToString();

            SetButtonText(_disableBtn, "禁用选中的 " + canDisable.ToString(CultureInfo.InvariantCulture) + " 项");
            SetButtonText(_restoreBtn, "恢复选中的 " + canRestore.ToString(CultureInfo.InvariantCulture) + " 项");
            SetButtonText(_deleteBtn, "删除选中的 " + checkedRows.Count.ToString(CultureInfo.InvariantCulture) + " 项");
            _disableBtn.Enabled = (!_busy && canDisable > 0);
            _restoreBtn.Enabled = (!_busy && canRestore > 0);
            _deleteBtn.Enabled = (!_busy && checkedRows.Count > 0);
            _refreshBtn.Enabled = !_busy;
        }

        private List<CtxRow> CheckedRows()
        {
            List<CtxRow> list = new List<CtxRow>();
            foreach (CtxRow r in _rows) { if (r.Checked && r.CanCheck) { list.Add(r); } }
            return list;
        }

        private void CheckVerbs(string verbList, bool elevated)
        {
            string[] wanted = verbList.Split(',');
            int hit = 0;
            foreach (CtxRow r in _rows)
            {
                foreach (string v in wanted)
                {
                    if (string.Equals(r.Entry.Verb, v.Trim(), StringComparison.OrdinalIgnoreCase) && r.CanCheck)
                    {
                        r.Checked = true;
                        if (hit == 0) { SetCurrent(r); }
                        hit++;
                        break;
                    }
                }
            }
            UpdateBar();
            if (hit > 0 && elevated)
            {
                Flash("带你回到了刚才那几项（已经勾好）。现在有管理员权限了，再点一次刚才那个按钮，这一次会真的生效。");
            }
            else if (hit > 0)
            {
                Flash("已勾选 " + hit.ToString(CultureInfo.InvariantCulture) + " 项。");
            }
        }

        /// <summary>底栏左边那句临时换成一句提示（几秒后回到正常的小结）。</summary>
        private void Flash(string text)
        {
            _status.Text = text + "（" + CtxMenu.Summary(_all) + "）";
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 4000;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                UpdateBar();
            };
            t.Start();
        }

        // ------------------------------------------------------------------ 动作记录

        private void ToggleActionPanel()
        {
            bool show = !_actionPanel.Visible;
            _actionPanel.Visible = show;
            _root.RowStyles[3] = new RowStyle(SizeType.Absolute, show ? ActionPanelHeight : 0f);
            _logBtn.Text = show ? "收起记录" : "动作记录";
            _logBtn.Width = TextRenderer.MeasureText(_logBtn.Text, _logBtn.Font).Width + 12;
            if (show) { RefreshActionList(); }
        }

        private void RefreshActionList()
        {
            if (_actionPanel == null) { return; }
            _actionList.SuspendLayout();
            try
            {
                foreach (Control c in GetControls(_actionList)) { c.Dispose(); }
                _actionList.Controls.Clear();
                _actionList.RowStyles.Clear();
                _actionList.RowCount = 0;

                List<CtxAction> list = CtxMenu.LoadActions(MaxActionLines);
                Label head = new Label();
                head.AutoSize = true;
                head.Margin = new Padding(2, 2, 2, 4);
                head.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
                head.ForeColor = _theme.BarText;
                head.Text = (list.Count == 0)
                    ? "动作记录：还没有动过任何一项（这里会记下每一次禁用 / 恢复 / 删除，每条后面能直接撤销）。"
                    : "动作记录（最新的在最上面；点右边的按钮就能撤销那一步）：";
                _actionList.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _actionList.Controls.Add(head);

                foreach (CtxAction a in list)
                {
                    TableLayoutPanel row = new TableLayoutPanel();
                    row.Dock = DockStyle.Fill;
                    row.AutoSize = true;
                    row.ColumnCount = 2;
                    row.RowCount = 1;
                    row.Margin = new Padding(0, 0, 0, 2);
                    row.BackColor = _theme.FormBack;
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                    row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                    Label line = new Label();
                    line.AutoSize = true;
                    line.Margin = new Padding(2, 4, 8, 0);
                    line.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
                    line.ForeColor = _theme.InputText;
                    line.Text = a.Line;
                    CtxAction captured = a;
                    _tips.SetToolTip(line, a.Legacy
                        ? "旧格式的记录（那时候只记了键名、没记完整键路径），撤不了。"
                        : ("键：" + a.Key + (a.Backup.Length > 0 ? Environment.NewLine + "备份：" + a.Backup : "")));
                    row.Controls.Add(line, 0, 0);

                    Button undo = MakeBarButton(a.CanUndo ? a.UndoLabel : "撤不了", "把这一步反着做一遍");
                    undo.Enabled = a.CanUndo;
                    undo.Click += delegate { UndoAction(captured); };
                    row.Controls.Add(undo, 1, 0);

                    _actionList.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    _actionList.Controls.Add(row);
                }
            }
            finally { _actionList.ResumeLayout(true); }
        }

        private void UndoAction(CtxAction a)
        {
            if (_busy) { return; }
            if (!Consent.EnsureAccepted(this, _theme, "右键菜单管理：撤销「" + a.Title + "」"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            _busy = true;
            UpdateBar();
            bool ok;
            string report;
            try { report = CtxMenu.Undo(a, out ok); }
            catch (Exception ex) { report = "撤销失败：" + ex.Message; ok = false; }
            _busy = false;
            Logger.Write("右键菜单管理", report);
            LoadRows();
            Flash(ok ? ("已撤销：" + a.Action + "「" + a.Title + "」") : "撤销没成功 —— 详情在工具箱日志里");
        }

        // ------------------------------------------------------------------ 三个动作（批量）

        private void DoAction(string kind)
        {
            if (_busy) { return; }
            List<CtxRow> checkedRows = CheckedRows();
            if (checkedRows.Count == 0) { return; }

            List<CtxEntry> items = new List<CtxEntry>();
            foreach (CtxRow r in checkedRows)
            {
                if (kind == "禁用" && r.Entry.Disabled) { continue; }
                if (kind == "恢复" && !r.Entry.Disabled) { continue; }
                items.Add(r.Entry);
            }
            if (items.Count == 0) { return; }

            if (!Consent.EnsureAccepted(this, _theme, "右键菜单管理：" + kind + " " + items.Count + " 项"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            // 需要管理员：弹一次 UAC、带管理员权限重开窗口（勾选跟过来），让用户再点一次
            bool needAdmin = false;
            foreach (CtxEntry e in items) { if (e.NeedsAdmin) { needAdmin = true; break; } }
            if (needAdmin && !CtxMenu.IsAdmin())
            {
                OfferElevation(kind, items, checkedRows);
                return;
            }

            // 删除有确认框（列清单、说清备份）；禁用 / 恢复不弹 —— 完全可逆，记录里能撤销
            if (kind == "删除" && !ConfirmDelete(items)) { return; }

            _busy = true;
            UpdateBar();
            int done;
            bool ok;
            string report;
            try
            {
                if (kind == "禁用") { report = CtxMenu.DisableMany(items, out done, out ok); }
                else if (kind == "恢复") { report = CtxMenu.RestoreMany(items, out done, out ok); }
                else { report = CtxMenu.DeleteMany(items, out done, out ok); }
            }
            catch (Exception ex)
            {
                report = kind + "失败：" + ex.Message;
                done = 0;
                ok = false;
            }
            _busy = false;
            Logger.Write("右键菜单管理", report);
            LoadRows();
            Flash(kind + "：成功 " + done.ToString(CultureInfo.InvariantCulture)
                + " / 共 " + items.Count.ToString(CultureInfo.InvariantCulture) + " 项"
                + (ok ? "" : "（有没成功的，逐条原因见工具箱日志）"));
            // 动完之后，"刚才到底动了哪几个键"的答案就在动作记录里 —— 收起着就替用户展开一次
            if (_actionPanel != null && !_actionPanel.Visible) { ToggleActionPanel(); }
        }

        private bool ConfirmDelete(List<CtxEntry> items)
        {
            if (!_confirmDangerous) { return true; }
            StringBuilder body = new StringBuilder();
            body.Append("要删除选中的 ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 项吗？");
            body.AppendLine();
            int shown = 0;
            foreach (CtxEntry e in items)
            {
                if (shown >= 8) { body.AppendLine("…等 " + items.Count.ToString(CultureInfo.InvariantCulture) + " 项"); break; }
                body.Append("· ").Append(e.Title).Append("（").Append(e.WhereLabel).Append("）").AppendLine();
                shown++;
            }
            body.AppendLine();
            body.Append("会先给这 ").Append(items.Count.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" 个键各备份一份 .reg，都备份成功了才开始删；任何一份备份失败，那一条就不删。")
                .AppendLine("后悔了：动作记录里点那一条的「还原来」，或者双击备份文件夹里的文件。")
                .AppendLine("只删选中的这些键，别的键一个都不动。");
            using (ConfirmForm f = new ConfirmForm(
                "要删除这 " + items.Count.ToString(CultureInfo.InvariantCulture) + " 项吗？", body.ToString(), "删除", _theme))
            {
                return f.ShowDialog(this) == DialogResult.OK;
            }
        }

        private bool Confirm(string title, string body, string okText)
        {
            if (!_confirmDangerous) { return true; }
            using (ConfirmForm f = new ConfirmForm(title, body, okText, _theme))
            {
                return f.ShowDialog(this) == DialogResult.OK;
            }
        }

        /// <summary>需要管理员：说清楚，然后提权重开这个窗口，**把刚才勾的那几项一起带过去**
        /// （`--focus=&lt;位置&gt;|&lt;键名,…&gt;` 只负责"勾上"，不带任何动作 —— 命令行里不存在能写
        /// 注册表的参数，写只能由用户点出来）。</summary>
        private void OfferElevation(string kind, List<CtxEntry> items, List<CtxRow> checkedRows)
        {
            string names = "";
            int shown = 0;
            foreach (CtxEntry e in items)
            {
                if (shown >= 6) { names += " 等 " + items.Count.ToString(CultureInfo.InvariantCulture) + " 项"; break; }
                if (shown > 0) { names += "、"; }
                names += e.Title;
                shown++;
            }
            bool go = Confirm("选中的项里有装在系统区的，需要管理员权限",
                "选中的 " + items.Count.ToString(CultureInfo.InvariantCulture) + " 项里有装在系统区的："
                + names + Environment.NewLine + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开，"
                + "刚才勾的那几项会自动帮你勾好。" + Environment.NewLine
                + "因为写注册表只能由你在界面上点出来（工具箱的命令行故意没有能写注册表的参数），"
                + "所以重开之后要再点一次「" + kind + "」那一下才会真的生效。", "确定");
            if (!go) { return; }
            try
            {
                StringBuilder verbs = new StringBuilder();
                foreach (CtxRow r in checkedRows)
                {
                    if (verbs.Length > 0) { verbs.Append(','); }
                    verbs.Append(r.Entry.Verb);
                }
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                    AppPaths.ExePath,
                    "rightmenu manage --focus=\"" + _zoneId + "|" + verbs.ToString() + "\"");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("右键菜单管理", "需要管理员：已请求以管理员身份重开窗口（" + items.Count + " 项，勾选带过去了）");
                Close();
            }
            catch (Exception ex)
            {
                Flash("请求管理员权限失败：" + ex.Message);
                Logger.Write("右键菜单管理", "请求管理员权限失败：" + ex.Message);
            }
        }

        // ------------------------------------------------------------------ 搜索框的灰字提示

        /// <summary>搜索框的灰字提示。`TextBox` 自己没有占位符，这里用系统的 `EM_SETCUEBANNER`
        /// （Vista 起支持，工具箱最低支持 Win7，够用）—— 比自己画一段灰字、输入时再抹掉省事，
        /// 也不会在取值时把提示文字当成用户输入。
        /// ⚠️ 这个 API 的名字**没有 W 后缀**，所以要 `EntryPoint` + `ExactSpelling`（同 PITFALLS 坑 28
        /// 那个 `FindWindowW`）。</summary>
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW", ExactSpelling = true)]
        private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SendMessageW(_searchBox.Handle, EM_SETCUEBANNER, (IntPtr)1, "输入名字过滤…"); }
            catch { }
        }

        // ------------------------------------------------------------------ 键盘（和主窗口对齐：Ctrl+F / Ctrl+L）

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                _searchBox.Focus();
                return true;
            }
            if (keyData == (Keys.Control | Keys.L)) { ToggleActionPanel(); return true; }
            if (keyData == Keys.Up || keyData == Keys.Down)
            {
                MoveCurrent(keyData == Keys.Down ? 1 : -1);
                return true;
            }
            if (keyData == Keys.Space)
            {
                CtxRow cur = CurrentRow();
                if (cur != null && cur.CanCheck) { cur.Toggle(); return true; }
            }
            if (keyData == Keys.Delete)
            {
                if (_deleteBtn.Enabled) { DoAction("删除"); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private CtxRow CurrentRow()
        {
            foreach (CtxRow r in _rows) { if (r.Current) { return r; } }
            return (_rows.Count > 0) ? _rows[0] : null;
        }

        private void MoveCurrent(int delta)
        {
            if (_rows.Count == 0) { return; }
            int index = 0;
            for (int i = 0; i < _rows.Count; i++) { if (_rows[i].Current) { index = i; break; } }
            int want = index + delta;
            if (want < 0) { want = 0; }
            if (want >= _rows.Count) { want = _rows.Count - 1; }
            SetCurrent(_rows[want]);
            try { _content.ScrollControlIntoView(_rows[want]); }
            catch { }
        }
    }
}
