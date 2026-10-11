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
    /// ⑤ 要管理员的项：弹一次 UAC，带管理员权限重开窗口（勾选会跟过来），再点一次才生效；
    /// ⑥ **"当前行"变了就必须刷底栏**（`SetCurrent` 里那句 `UpdateBar()`，2026-10-11 补的）：
    ///    底栏的「取得所有权」和那句"怎么办"都是按**当前行**算的，而点一行只会走到 `SetCurrent` ——
    ///    别指望勾选框那条路帮你刷新（受保护行的勾选框是禁用的，它永远不触发）。</summary>
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
        private Button _logBtn, _disableBtn, _restoreBtn, _deleteBtn, _takeBtn, _dllBtn, _clsidBtn, _restartBtn, _refreshBtn, _closeBtn;
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

            // 常驻那句：回答"是不是要重启资源管理器才生效"（不然用户会怀疑没生效）。
            // 2026-10-11 起两种东西**生效方式不一样**了，所以这句必须分开说：
            // 静态菜单项是每次右键现场拼的（改完立刻生效，实测过五个位置）；DLL 扩展项是
            // explorer 启动时加载进去的 COM 组件，改黑名单**必须重启资源管理器**。
            _hint = new Label();
            _hint.AutoSize = true;
            _hint.Anchor = AnchorStyles.Right;
            _hint.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            _hint.ForeColor = _theme.BarText;
            _hint.Text = "静态项不用重启资源管理器；扩展项要重启才生效";
            _hint.Margin = new Padding(8, 0, 4, 0);
            row.Controls.Add(_hint, 3, 0);

            _searchRow.Controls.Add(row);
            _root.Controls.Add(_searchRow, 0, 1);
            _tips.SetToolTip(_hint, "静态菜单项是每次右键现场拼的（2026-10-09 用只读探针在五个位置实测过），"
                + "改完再点一次右键就是新的；DLL 扩展项是资源管理器启动时加载进去的，"
                + "改完必须重启资源管理器才看得到。");
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

            // 「取得所有权」：只给"被 Windows 保护"的项用（所有者 TrustedInstaller、管理员只给读）。
            // 它和前面三个动作**不是一回事** —— 那个是改系统安全描述符，所以单独一个按钮 + 重确认，
            // 而且只作用在**当前选中的那一项**上（一次一项，绝不混进批量里）。
            _takeBtn = MakeBarButton("取得所有权", "给被 Windows 保护的项用：把那个键的所有者改成管理员并追加完全控制，"
                + "然后才写得动。会先存下原样的权限（可还原）；这一步要管理员。");
            _takeBtn.Click += delegate { DoTakeOwn(); };
            right.Controls.Add(_takeBtn);

            // 「打开 DLL 目录」/「复制 CLSID」：只给**当前选中的那一行**用（一次一项）——
            // 扩展项那一行背后的东西（DLL 在哪、CLSID 是多少）光看名字看不出来，
            // 要排查"这是谁装的"就得能拿到这两样。都不改任何东西。
            _dllBtn = MakeBarButton("打开 DLL 目录", "在资源管理器里定位当前选中那一条扩展的 DLL（只打开文件夹，不改任何东西）");
            _dllBtn.Click += delegate { DoRevealDll(); };
            right.Controls.Add(_dllBtn);

            _clsidBtn = MakeBarButton("复制 CLSID", "把当前选中那一条扩展的 CLSID 复制到剪贴板（搜它是谁装的、写反馈都用得上）");
            _clsidBtn.Click += delegate { DoCopyClsid(); };
            right.Controls.Add(_clsidBtn);

            // 「重启资源管理器」：扩展项的停用 / 恢复**必须重启资源管理器才生效**（DLL 是 explorer
            // 启动时加载进去的），所以这件事得有个一步就能点的地方 —— 不然用户改完看不到变化，
            // 只会以为"这功能没用"。命令走工具箱自己那颗按钮（见 DoRestartExplorer 的说明）。
            _restartBtn = MakeBarButton("重启资源管理器", "让扩展项的停用 / 恢复立刻生效：会关掉所有已打开的文件夹窗口、"
                + "任务栏闪一下（有确认框）；跑的就是「常用设置」页那颗按钮");
            _restartBtn.Click += delegate { DoRestartExplorer(); };
            right.Controls.Add(_restartBtn);

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
                        // 扩展项 2026-10-11 起**也能勾了**（停用 / 恢复走系统黑名单那条路），
                        // 但三种情形是灰的（黑名单写在系统区 / CLSID 认不出来 / 登记键读不动）——
                        // 判据全在 `CtxEntry.Checkable` 里，这里只负责取。
                        // 「被 Windows 保护」的项勾不动：它要单独走「取得所有权」，不能混进批量里。
                        CtxRow row = new CtxRow(e, _theme, e.Checkable && !e.Protected);
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

        /// <summary>换"当前行"（键盘上下键 / 鼠标点某一行走这里）。
        ///
        /// ⚠️ **这里必须顺手 `UpdateBar()`** —— 底栏那个「取得所有权」是按"当前行是不是被 Windows 保护"
        /// 亮灭的（`_takeBtn.Enabled = curProtected`），而"当前行"就是这里改的。
        /// 2026-10-11 用户问「为什么【取得所有权】按钮一直都是灰色的」：真鼠标点受保护那一行时，
        /// 底栏**一次都不刷新** —— 唯一会刷新的那条路是勾选框的 `CheckedChanged → Toggled → UpdateBar`，
        /// 而受保护那一行的勾选框是**禁用的**，永远不触发。于是：蓝线高亮了、底栏那句提示没有、
        /// 按钮永远灰着（只有 `--focus` 那条提权重开的路径会亮，回归 C19w 走的正好是它，
        /// 所以自动测试一直没发现）。实测三行：点普通行 → 「已勾选 1 项」；点受保护行 → 一个字都不变。</summary>
        private void SetCurrent(CtxRow row)
        {
            foreach (CtxRow r in _rows) { r.Current = (r == row); }
            UpdateBar();
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

        /// <summary>底栏：左边那句小结 + 右边按钮上的数字（都跟着勾选实时变）。
        /// ⚠️ 勾选的项**分两类**（2026-10-11 起）：
        ///  · 静态菜单项 → 「禁用 / 恢复」（往那个键写/删隐藏开关，改完立刻生效）；
        ///  · DLL 扩展项（含黑名单残留）→ 「停用 / 恢复扩展」（往用户区系统黑名单写/删一条，
        ///    **要重启资源管理器才生效**）。所以按钮上的字会跟着勾选换（勾到扩展就写「停用」）。</summary>
        private void UpdateBar()
        {
            List<CtxRow> checkedRows = CheckedRows();
            int canDisable = 0, canRestore = 0, extDisable = 0, extRestore = 0;
            foreach (CtxRow r in checkedRows)
            {
                if (r.Entry.Shellex)
                {
                    // 扩展项（含黑名单残留）：黑名单里有它 → 这一步是"恢复"，没有 → 是"停用"
                    if (r.Entry.BlockedUser) { canRestore++; extRestore++; } else { canDisable++; extDisable++; }
                    continue;
                }
                if (r.Entry.Disabled) { canRestore++; } else { canDisable++; }
            }
            bool anyExt = (extDisable + extRestore) > 0;

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
            // 选中的那一项被 Windows 保护时，把"怎么办"直接写在底栏（以前是点了才知道没反应）
            CtxRow cur = CurrentRow();
            bool curProtected = (cur != null) && cur.Entry.Protected;
            if (curProtected)
            {
                sb.Append(" · 选中这一项被 Windows 保护（所有者是 TrustedInstaller，连管理员也写不动）→ 点右边「取得所有权」");
            }
            else if (cur != null && cur.Entry.Shellex && cur.Entry.Blocked)
            {
                // 扩展项：状态是从系统黑名单读的，这里顺便把黑名单里那个"人看的名字"念出来 ——
                // 那个名字往往比注册表里的英文名好认（比如「打开方式(H)」）。
                sb.Append(" · 选中这一项已被系统停用");
                if (cur.Entry.BlockedName.Length > 0)
                {
                    sb.Append("（黑名单里写的名字是「").Append(cur.Entry.BlockedName).Append("」）");
                }
                sb.Append(cur.Entry.BlockedUser ? " → 点「恢复」能让它回到菜单里" : "，那份写在系统区，本工具箱不动它");
            }
            else if (cur != null && cur.Entry.Shellex && !cur.Entry.Zombie)
            {
                sb.Append(" · 选中这一项是 DLL 扩展（不在黑名单里 = 在用；停用它要重启资源管理器）");
            }
            _status.Text = sb.ToString();

            // ⚠️ 勾到扩展项时这个按钮改叫「停用」（它干的事是写系统黑名单，而且要重启资源管理器才生效）；
            // **恢复那一个不改字** —— "恢复"对两类都说得通（清黑名单 / 删隐藏开关）。
            SetButtonText(_disableBtn, (anyExt ? "停用选中的 " : "禁用选中的 ") + canDisable.ToString(CultureInfo.InvariantCulture) + " 项");
            SetButtonText(_restoreBtn, "恢复选中的 " + canRestore.ToString(CultureInfo.InvariantCulture) + " 项");
            SetButtonText(_deleteBtn, "删除选中的 " + checkedRows.Count.ToString(CultureInfo.InvariantCulture) + " 项");
            _disableBtn.Enabled = (!_busy && canDisable > 0);
            _restoreBtn.Enabled = (!_busy && canRestore > 0);
            _deleteBtn.Enabled = (!_busy && checkedRows.Count > 0);
            _takeBtn.Enabled = (!_busy && curProtected);
            // 「打开 DLL 目录」/「复制 CLSID」作用于**当前选中的那一行**（没选 / 选中的不是扩展就是灰的）
            _dllBtn.Enabled = (!_busy && cur != null && cur.Entry.DllPath.Length > 0);
            _clsidBtn.Enabled = (!_busy && cur != null && cur.Entry.Clsid.Length > 0);
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
            int hit = 0;      // 勾上的
            int picked = 0;   // 选中的（含勾不动的：受保护 / 扩展项）
            foreach (CtxRow r in _rows)
            {
                foreach (string v in wanted)
                {
                    if (!string.Equals(r.Entry.Verb, v.Trim(), StringComparison.OrdinalIgnoreCase)) { continue; }
                    // ⚠️ **勾不动也要选中**：被 Windows 保护的项 checkbox 是灰的，但它正是要靠
                    // 「取得所有权」处理的那一项 —— 提权重开之后得把它选中，底栏那个按钮才会亮
                    // （2026-10-10 实测踩过：只勾不选，重开之后按钮还是灰的）。
                    if (picked == 0) { SetCurrent(r); }
                    picked++;
                    if (r.CanCheck) { r.Checked = true; hit++; }
                    break;
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
            else if (picked > 0)
            {
                Flash("已选中那一项（它被 Windows 保护、勾不动）—— 想禁用它就点底栏的「取得所有权」。");
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
                    : "动作记录（最新的在最上面；失败的那几条是红字；点右边的按钮就能撤销那一步）：";
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
                    // 失败的整行画成危险色 —— 用户要的是"哪条没成、为什么"直接看得见，不用去翻日志
                    line.ForeColor = a.Failed ? _theme.Danger : _theme.InputText;
                    line.Text = a.Line;
                    CtxAction captured = a;
                    _tips.SetToolTip(line, a.Legacy
                        ? "旧格式的记录（那时候只记了键名、没记完整键路径），撤不了。"
                        : ("结果：" + (a.Result.Length > 0 ? a.Result : "（旧记录没记结果）") + Environment.NewLine
                           + "键：" + a.Key
                           + (a.Backup.Length > 0 ? Environment.NewLine + "备份：" + a.Backup : "")));
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
            Flash(ok ? ("已撤销：" + a.Action + "「" + a.Title + "」") : "撤销没成功 —— 原因记在动作记录里（红字那行）");
        }

        // ------------------------------------------------------------------ 三个动作（批量）

        /// <summary>批量动作。⚠️ 2026-10-11 起勾选的项**要分成两拨**（两套完全不同的机制）：
        ///  · 静态菜单项 → `CtxMenu.DisableMany / RestoreMany / DeleteMany`（改那个登记键，立刻生效）；
        ///  · DLL 扩展项（含黑名单残留）→ `CtxMenu.BlockMany / UnblockMany`（写用户区系统黑名单，
        ///    **要重启资源管理器**才生效；「删除」对它们没有意义 —— 登记的键不是我们该删的）。
        /// 两拨各自跑完，报告拼在一起写进日志；动作记录是逐条记的，所以撤销也是逐条的。</summary>
        private void DoAction(string kind)
        {
            if (_busy) { return; }
            List<CtxRow> checkedRows = CheckedRows();
            if (checkedRows.Count == 0) { return; }

            List<CtxEntry> items = new List<CtxEntry>();
            List<CtxEntry> exts = new List<CtxEntry>();
            foreach (CtxRow r in checkedRows)
            {
                if (r.Entry.Shellex)
                {
                    if (kind == "删除") { continue; }        // 扩展项不走"删键"（那是别的东西）
                    if (kind == "禁用" && r.Entry.BlockedUser) { continue; }
                    if (kind == "恢复" && !r.Entry.BlockedUser) { continue; }
                    exts.Add(r.Entry);
                    continue;
                }
                if (kind == "禁用" && r.Entry.Disabled) { continue; }
                if (kind == "恢复" && !r.Entry.Disabled) { continue; }
                items.Add(r.Entry);
            }
            if (items.Count == 0 && exts.Count == 0) { return; }

            if (!Consent.EnsureAccepted(this, _theme, "右键菜单管理：" + kind + " "
                + (items.Count + exts.Count).ToString(CultureInfo.InvariantCulture) + " 项"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            // 需要管理员：弹一次 UAC、带管理员权限重开窗口（勾选跟过来），让用户再点一次。
            // ⚠️ 扩展项恒为"不用管理员"（黑名单只写用户区那份，见 CtxEntry.NeedsAdmin）。
            bool needAdmin = false;
            foreach (CtxEntry e in items) { if (e.NeedsAdmin) { needAdmin = true; break; } }
            if (needAdmin && !CtxMenu.IsAdmin())
            {
                OfferElevation(kind, items, checkedRows);
                return;
            }

            // 删除有确认框（列清单、说清备份）；禁用 / 恢复不弹 —— 完全可逆，记录里能撤销
            if (kind == "删除" && !ConfirmDelete(items)) { return; }
            // 「停用扩展」要重启资源管理器才生效，而且**一次影响所有位置** —— 这件事必须在点之前说清楚。
            // 但它本身完全可逆（黑名单里删一条就回来了），所以**不弹确认框**（和禁用同一条口径：
            // 可逆的动作不拿确认框挡人），改写进底栏那句 + 动作报告 + 记录里。
            _busy = true;
            UpdateBar();
            int done;
            bool ok;
            StringBuilder report = new StringBuilder();
            try
            {
                int one = 0;
                bool sub;
                if (items.Count > 0)
                {
                    if (kind == "禁用") { report.Append(CtxMenu.DisableMany(items, out one, out sub)); }
                    else if (kind == "恢复") { report.Append(CtxMenu.RestoreMany(items, out one, out sub)); }
                    else { report.Append(CtxMenu.DeleteMany(items, out one, out sub)); }
                    done = one;
                    ok = sub;
                }
                else { done = 0; ok = true; }
                if (exts.Count > 0)
                {
                    int two;
                    bool sub2;
                    if (kind == "禁用") { report.Append(CtxMenu.BlockMany(exts, out two, out sub2)); }
                    else { report.Append(CtxMenu.UnblockMany(exts, out two, out sub2)); }
                    done += two;
                    ok = ok && sub2;
                }
            }
            catch (Exception ex)
            {
                report.Append(kind).Append("失败：").Append(ex.Message);
                done = 0;
                ok = false;
            }
            _busy = false;
            Logger.Write("右键菜单管理", report.ToString());
            LoadRows();
            string tail = (exts.Count > 0) ? "（扩展项要重启资源管理器才生效 —— 点底栏「重启资源管理器」）" : "";
            Flash(kind + "：成功 " + done.ToString(CultureInfo.InvariantCulture)
                + " / 共 " + (items.Count + exts.Count).ToString(CultureInfo.InvariantCulture) + " 项"
                + (ok ? "" : "（没成功的那几条在动作记录里，红字那行写着原因）") + tail);
            // 动完之后，"刚才到底动了哪几个键"的答案就在动作记录里 —— 收起着就替用户展开一次
            if (_actionPanel != null && !_actionPanel.Visible) { ToggleActionPanel(); }
        }

        /// <summary>「取得所有权」：只对**当前选中**的那一项（一次一项）。
        /// ① 不是管理员 → 走提权重开那条路（和别的动作一样）；
        /// ② 重确认：把"改所有者 + 追加完全控制 + 可还原"说清楚，再动手。</summary>
        private void DoTakeOwn()
        {
            if (_busy) { return; }
            CtxRow cur = CurrentRow();
            if (cur == null || !cur.Entry.Protected) { return; }
            CtxEntry e = cur.Entry;

            if (!Consent.EnsureAccepted(this, _theme, "右键菜单管理：取得所有权「" + e.Title + "」"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            if (e.NeedsAdmin && !CtxMenu.IsAdmin())
            {
                List<CtxEntry> one = new List<CtxEntry>();
                one.Add(e);
                List<CtxRow> rows = new List<CtxRow>();
                rows.Add(cur);
                OfferElevation("取得所有权", one, rows);
                return;
            }

            StringBuilder body = new StringBuilder();
            body.Append("这一项被 Windows 保护（所有者是 NT SERVICE\\TrustedInstaller，管理员只有读权限），")
                .AppendLine("所以连管理员也写不动 —— 想禁用它，必须先把这个注册表键的所有权拿过来。")
                .AppendLine();
            body.AppendLine("会做三件事：");
            body.AppendLine("· 先把原样的所有者和权限存成一份文件（备份文件夹里，扩展名 .sddl）；");
            body.AppendLine("· 把所有者改成 Administrators，并追加一条「管理员：完全控制」（原有的权限一条都不删）；");
            body.AppendLine("· 然后写隐藏开关，让那一项从右键里消失。");
            body.AppendLine();
            body.AppendLine("后悔了：动作记录里点那一条的「还原权限」—— 隐藏开关删掉、原样的所有者和权限写回去。");
            // 用户 2026-10-10 问过「取得所有权是不是只需要执行一次下次打开就不需要了？」——
            // 答案是"是、永久生效"，那就把这句话写在他会看到的地方（别让他下次再问一遍）。
            body.AppendLine("这一个键抢过一次就一直是你的：以后再禁用它（或恢复）都不用再点这个按钮，"
                + "只有点「还原权限」才会把权限还回去。别的项、别的位置是另外的键，要各自处理。");
            body.Append("注意：这是改系统的安全设置（比写一个值重得多）；Windows 以后更新时也可能把它再收回去。");

            bool go;
            using (ConfirmForm f = new ConfirmForm("要取得「" + e.Title + "」的所有权吗？", body.ToString(), "取得所有权并禁用", _theme))
            {
                go = (f.ShowDialog(this) == DialogResult.OK);
            }
            if (!go) { return; }

            _busy = true;
            UpdateBar();
            bool ok;
            string report;
            try { report = CtxMenu.TakeOwnAndDisable(e, out ok); }
            catch (Exception ex) { report = "取得所有权失败：" + ex.Message; ok = false; }
            _busy = false;
            Logger.Write("右键菜单管理", report);
            LoadRows();
            Flash(ok ? ("已取得所有权并禁用「" + e.Title + "」（想还原就点记录里的「还原权限」）")
                     : "没成功 —— 原因在动作记录里（红字那行）");
            if (_actionPanel != null && !_actionPanel.Visible) { ToggleActionPanel(); }
        }

        /// <summary>底栏「打开 DLL 目录」：在资源管理器里定位**当前选中**那一条扩展的 DLL。
        /// 只打开文件夹、不改任何东西。`MXX1_NO_OPEN=1` 时**只写一行日志、不真打开** ——
        /// 和主窗口「打开网址」用同一个开关（见 `MainForm.OpenUrl`）：测试点完去读日志，两边都干净。</summary>
        private void DoRevealDll()
        {
            CtxRow cur = CurrentRow();
            if (cur == null || cur.Entry.DllPath.Length == 0) { return; }
            string path = cur.Entry.DllPath;
            string off = Environment.GetEnvironmentVariable("MXX1_NO_OPEN");
            bool noOpen = (off != null) && (off.Trim() == "1");
            Logger.Write("右键菜单管理", (noOpen ? "（测试模式，没有真打开）" : "") + "打开扩展 DLL 目录 · "
                + cur.Entry.Title + " · " + path);
            if (noOpen)
            {
                Flash("测试模式：没有真打开，只写进了日志 · " + path);
                return;
            }
            Launcher.RevealInExplorer(path);
            Flash("已经在资源管理器里定位到那个 DLL：" + System.IO.Path.GetFileName(path));
        }

        /// <summary>底栏「复制 CLSID」：把**当前选中**那一条扩展的 CLSID 复制到剪贴板
        /// （拿去搜"这是谁装的"、写给作者看都用得上）。不改任何东西。</summary>
        private void DoCopyClsid()
        {
            CtxRow cur = CurrentRow();
            if (cur == null || cur.Entry.Clsid.Length == 0) { return; }
            string text = cur.Entry.Clsid;
            string err = WriteClipboard(text);
            Logger.Write("右键菜单管理", "复制 CLSID · " + cur.Entry.Title + " · " + text
                + (err.Length > 0 ? "（失败：" + err + "）" : ""));
            Flash(err.Length > 0 ? ("复制失败：" + err) : ("已复制到剪贴板：" + text));
        }

        /// <summary>写剪贴板。**必须 copy=true**：窗口关掉之后剪贴板里还得留着
        /// （和 `CopyPath.WriteClipboard` 同一条教训）。失败重试 3 次 —— 剪贴板是全局资源，
        /// 别的程序正开着它很正常。</summary>
        private static string WriteClipboard(string text)
        {
            string last = "";
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return "";
                }
                catch (Exception ex)
                {
                    last = ex.Message;
                    System.Threading.Thread.Sleep(150);
                }
            }
            return (last.Length > 0) ? last : "剪贴板打不开";
        }

        /// <summary>底栏「重启资源管理器」：让扩展项的停用 / 恢复**立刻生效**
        /// （DLL 是 explorer 启动时加载进去的，不重启看不到变化）。
        ///
        /// 三条（2026-10-11 定的）：
        /// ① 命令**只有一个来源** —— 工具箱「常用设置」页那颗「重启资源管理器」按钮
        ///    （`tools\common.json` 里的 `restart-explorer`）。这里照 id 把那个 `ToolItem` 找出来、
        ///    走 `Launcher.Run` 跑它，**绝不在这里重抄一遍 `taskkill /f /im explorer.exe`**
        ///    （抄一份就有两处要维护，以后改一处忘一处）。
        /// ② 会**关掉所有已经打开的文件夹窗口**、任务栏闪一下，所以先过一次确认框
        ///    （那颗按钮自己也是 `confirm: true`）；确认框里把命令原样写出来给用户看。
        /// ③ `MXX1_NO_EXPLORER_RESTART=1` 时**只写日志、不真做** —— 回归测试点它的时候绝不许
        ///    把用户（或测试机）的 explorer 杀掉。</summary>
        private void DoRestartExplorer()
        {
            if (_busy) { return; }
            ToolItem t = FindToolById("restart-explorer");
            if (t == null)
            {
                Flash("工具清单里找不到「重启资源管理器」那颗按钮（tools\\common.json 被改过？）");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "右键菜单管理：重启资源管理器"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            Settings settings = Settings.Load();
            string cmd = Launcher.DescribeCommand(t, settings, false);
            StringBuilder body = new StringBuilder();
            body.Append("扩展项的「停用 / 恢复」要重启资源管理器才生效")
                .AppendLine("（DLL 是在 explorer 启动时加载进去的，改完当场看不到变化）。").AppendLine();
            body.AppendLine("现在会做这件事：");
            body.AppendLine("· 结束 explorer.exe，然后立刻把它拉起来；");
            body.AppendLine("· 任务栏和桌面会闪一下 —— 所有已经打开的文件夹窗口会一起关掉（没保存的先存一下）；");
            body.AppendLine("· 托盘图标、任务栏上固定的按钮会重新出现。");
            body.AppendLine();
            body.AppendLine("跑的就是工具箱「常用设置」页那颗按钮，命令原样是：");
            body.Append("  ").AppendLine(cmd);
            bool go;
            using (ConfirmForm f = new ConfirmForm("要重启资源管理器吗？", body.ToString(), "重启资源管理器", _theme))
            {
                go = (f.ShowDialog(this) == DialogResult.OK);
            }
            if (!go) { return; }

            string off = Environment.GetEnvironmentVariable("MXX1_NO_EXPLORER_RESTART");
            if (off != null && off.Trim() == "1")
            {
                Logger.Write("右键菜单管理", "（测试模式，没有真重启）重启资源管理器：" + cmd);
                Flash("测试模式：没有真重启，只写进了日志");
                return;
            }
            LaunchResult r = Launcher.Run(t, settings, false, false);
            Logger.Write("右键菜单管理", "重启资源管理器：" + cmd
                + ((r == null) ? " · （没有结果）" : (" · " + r.Message)));
            Flash("已经重启资源管理器 —— 现在再点一次右键看看（扩展项的停用 / 恢复这下才生效）");
        }

        /// <summary>按 id 从工具清单里找那一颗按钮（找不到返回 null）。</summary>
        private static ToolItem FindToolById(string id)
        {
            try
            {
                List<string> warnings = new List<string>();
                foreach (ToolItem t in ToolRegistry.LoadAll(warnings))
                {
                    if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) { return t; }
                }
            }
            catch { }
            return null;
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

        /// <summary>"当前行"（键盘上下键 / 鼠标点出来的那一条），**没选就是 null**。
        ///
        /// ⚠️ 这里**故意不兜底取 `_rows[0]`**（2026-10-11 用户拍板改掉的）。原来是
        /// `return (_rows.Count > 0) ? _rows[0] : null;`，后果是"按钮亮着"并不等于"你选中了东西"：
        /// 某一页的第一条碰巧是被 Windows 保护的项，一打开窗口按钮就是亮的，而底栏那句
        /// 「选中这一项被 Windows 保护 → 点右边…」也不会出现 —— 用户没法把因果关系看清楚。
        /// 现在：**按钮亮 = 你真的点中了那一行**。顺带修掉另一个同源的小毛病：什么都没选中时
        /// 按空格会去勾第 1 行（`ProcessCmdKey` 拿 `CurrentRow()` 用的就是它）。</summary>
        private CtxRow CurrentRow()
        {
            foreach (CtxRow r in _rows) { if (r.Current) { return r; } }
            return null;
        }

        /// <summary>键盘上下键换当前行。**从"什么都没选"起步时，↓ 落到第 1 行、↑ 也落到第 1 行**
        /// （`index = -1` 起步就是为这个：原来兜底取第 1 行，从那儿再往下会跳过第 1 行）。</summary>
        private void MoveCurrent(int delta)
        {
            if (_rows.Count == 0) { return; }
            int index = -1;
            for (int i = 0; i < _rows.Count; i++) { if (_rows[i].Current) { index = i; break; } }
            int want = (index < 0) ? 0 : index + delta;
            if (want < 0) { want = 0; }
            if (want >= _rows.Count) { want = _rows.Count - 1; }
            SetCurrent(_rows[want]);
            try { _content.ScrollControlIntoView(_rows[want]); }
            catch { }
        }
    }
}
