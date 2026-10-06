// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>The button wall: tabs on top, a four column grid of compact buttons in the
    /// middle, a collapsible run log and a thin status bar at the bottom.
    /// Every control is placed by a TableLayoutPanel -- no coordinates are written by hand.</summary>
    internal sealed class MainForm : Mxx1Form
    {
        private const int TabBarHeight = 30;
        // The status bar height is computed from the font (see the constructor): with a fixed
        // 24px bar and 20px buttons the 8.25pt labels lost their bottom halves, because one
        // line of text already needs 16px plus the button border.
        private int _statusBarHeight = 26;
        private int _barButtonHeight = 22;
        // Column width is measured from the longest button name at the real font size
        // (see ComputeCellWidth) so nothing is ever truncated and other DPI settings work.
        private int _cellWidth = 114;
        private const int ToolRowHeight = 36;
        private const int SeparatorRowHeight = 13;
        // 分段标题行：原来那条灰线旁边多一句人话（「修复与诊断 ————」）。26 个按钮分成 6 段时，
        // 光看灰线不知道这堆是干什么的（见 docs/DESIGN.md §4.8）。
        private const int CaptionRowHeight = 24;
        private const int Columns = 4;
        private const int LogPanelHeight = 170;
        private const int SearchRowHeight = 28;
        private const int ToastRowHeight = 26;
        /// <summary>没记过用户尺寸时的固定高度（内容最高的页签也基本装得下）。</summary>
        private const int DefaultFixedHeight = 620;

        private readonly Settings _settings;
        private Theme _theme;

        /// <summary>`ui log` / `ui settings`：右键「常用功能」子菜单里那几个固定入口用 —— 打开界面之后
        /// 替用户点一下那个界面动作（否则从资源管理器点出来会"什么都不发生"）。</summary>
        public static string StartupAction = "";
        private List<ToolItem> _tools = new List<ToolItem>();
        private readonly List<string> _warnings = new List<string>();
        private readonly ToolTip _tips = new ToolTip();
        private string _currentTab = Tabs.Common;
        private string _filter = "";
        private string _statusText = "就绪";
        private int _running;
        // 等"提升权限后另起的那个进程"把结果写进交接文件（它没有控制台，见 Launcher.RunPrivacy）
        private System.Windows.Forms.Timer _elevatedWatch;
        private DateTime _elevatedSince;
        private int _elevatedTries;

        // 运行计时：DISM / SFC 这类要跑几分钟，只转个圈看不出是活着还是卡死了（底栏那一格会走秒）
        private System.Windows.Forms.Timer _runTimer;
        private DateTime _runStarted = DateTime.MinValue;
        private DateTime _runFinished = DateTime.MinValue;
        private string _lastElapsed = "";

        // 窗口几何：_fitting 期间改尺寸的是我们自己，别当成"用户拖过窗口"（那会把自动高度关掉）
        private bool _fitting;
        private int _contentRows;
        private int _contentSeps;
        private readonly List<Label> _sepLabels = new List<Label>();

        private TableLayoutPanel _root;
        private TableLayoutPanel _tabBar;
        private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private Panel _searchRow;
        private TextBox _searchBox;
        private Panel _content;
        private TableLayoutPanel _grid;
        private readonly List<ToolButton> _gridButtons = new List<ToolButton>();
        private Panel _logPanel;
        private TextBox _logBox;
        private Panel _toastRow;
        private Label _toastLabel;
        private bool _toastOk = true;
        private System.Windows.Forms.Timer _toastTimer;
        /// <summary>宽度在启动时定一次就不再动：用户 2026-10-04 明确说"主界面宽度固定一下，
        /// 反正按钮不会跟随变化而自适应"。</summary>
        private int _fixedWidth;
        private TableLayoutPanel _statusBar;
        private Label _statusLabel;
        private Button _btnSearch;
        private Button _btnLog;
        private Button _btnSettings;
        private Button _btnAbout;
        private Button _btnUpdate;

        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miRun;
        private ToolStripMenuItem _miRunAdmin;
        private ToolStripMenuItem _miReveal;
        private ToolStripMenuItem _miCopy;
        private ToolStripMenuItem _miDefine;
        private ToolStripMenuItem _miHelp;
        private ToolStripMenuItem _miEdit;
        private ToolStripMenuItem _miDelete;
        private ToolStripMenuItem _miPin;
        private ToolButton _menuTarget;   // the button the context menu was opened on

        /// <summary>True while the 新建按钮 / 编辑按钮 window is up. A drop that arrives on the grid
        /// during that time must not open a second one (the user hit exactly that).</summary>
        private bool _userDialogOpen;

        public MainForm()
        {
            _settings = Settings.Load();
            _theme = Theme.Resolve(_settings.Theme);

            Text = AboutForm.ProductTitle + " v" + AboutForm.VersionText;
            // The real size is set once the button names are known (see ComputeCellWidth).
            // 最小尺寸只要容得下 8 个页签和一行按钮就行：窗口高度默认贴着当前页签的内容走
            // （见 FitToContent），所以「右键增强」那种只有 1 个按钮的页码再撑个 700px 空窗口。
            MinimumSize = new Size(520, 240);
            StartPosition = FormStartPosition.CenterScreen;
            // 最小化要能用（用户 2026-10-06：「给工具箱右上角添加一个最小化，目前很影响体验，
            // 只有关闭的情况下」）。只开最小化、**不开最大化**：Windows 会给"只给最小化"的窗口
            // 画一个灰掉的最大化方框（实测 TITLEBARINFOEX 的 state=0x1 = unavailable），
            // 那是系统对这种窗口的标准画法，用户拍板留着那个灰方块。
            // **别为了藏它把 MaximizeBox 改成 true**：按钮墙是固定 4 列、列宽也不随窗口变，
            // 最大化之后墙挤在左上角、右边和下方一大片空白，而且 RememberGeometry / WndProc
            // 会把铺满屏幕的尺寸写进 settings.ini，把用户记住的窗口大小冲掉。
            MinimizeBox = true;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;
            KeyPreview = true;
            // 悬停提示：默认 5 秒就消失（长句子根本读不完），而且主窗口一失焦就再也不弹了
            // （ShowAlways=false 时，点过别的窗口再回来悬停会没反应）—— 两样都调过来。
            _tips.AutoPopDelay = 20000;
            _tips.InitialDelay = 400;
            _tips.ReshowDelay = 200;
            _tips.ShowAlways = true;

            ReloadTools();
            // 宽度是**固定**的（用户 2026-10-04 的规矩：「主界面宽度固定一下，反正按钮不会跟随变化
            // 而自适应」）。设置里记过就用记着的那一个（拖过窗口也会更新它），没记过就在下面按清单里
            // 的名字量一次，然后在 FitToContent 里立刻记进设置 —— 之后再加多长的名字都不改窗口宽度，
            // 改的只是那个按钮上的省略号（悬停提示里永远是全名）。
            if (_settings.WindowWidth > 0) { _fixedWidth = _settings.WindowWidth; }
            _cellWidth = ComputeCellWidth();
            ClampCellWidthToWindow();
            using (Font barFont = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point))
            {
                // One line of 8.25pt text measures 16px, but a flat button also reserves its 1px
                // border plus about 3px of internal padding on each side: the label is drawn in
                // height - 8. So a 22px button gives the glyph 14px and the bottom row of every
                // bar label was cut off -- measured on the rendered window, "检查更新" came out
                // with 9 ink rows instead of the 10 a grid button shows (tests\Test-Gui.ps1 D01e).
                _barButtonHeight = TextRenderer.MeasureText("国", barFont).Height + 8;
                if (_barButtonHeight < 24) { _barButtonHeight = 24; }
            }
            _statusBarHeight = _barButtonHeight + 4;   // 2px margin above and below
            // 上次停在哪个页签就还停在哪个（第一次打开：常用页有东西就用它，否则常用设置）
            _currentTab = PickInitialTab();
            // 真实高度在 BuildGrid → FitToContent 里按内容定；这里先给个能用的初值
            ClientSize = new Size(Math.Max(500, Columns * _cellWidth + 24), 320);
            BuildUi();
            BuildGrid();
            ApplyTheme();
            ApplyLogPanelVisibility();
            RefreshLogBox();
            UpdateStatusBar();

            // 运行计时（DISM / SFC 这类要跑几分钟，底栏得能看出还活着）
            _runTimer = new System.Windows.Forms.Timer();
            _runTimer.Interval = 1000;
            _runTimer.Tick += delegate
            {
                if (_running > 0) { UpdateStatusBar(); }
                else { _runTimer.Stop(); }
            };

            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged; }
            catch { }
        }

        /// <summary>启动时停在哪个页签。上次的页签还存在就用它；没有记录时，如果「常用」页里有
        /// 置顶或最近使用的按钮就用「常用」，否则回到「常用设置」。</summary>
        private string PickInitialTab()
        {
            string last = (_settings.LastTab ?? "").Trim();
            if (last.Length > 0 && Tabs.Index(last) < Tabs.Ids.Length) { return last; }
            List<ToolItem> pinned;
            List<ToolItem> recent;
            SplitFavorites(out pinned, out recent);
            if (pinned.Count + recent.Count > 0) { return Tabs.Recent; }
            return Tabs.Common;
        }

        // ---------------------------------------------------------------- construction

        private void BuildUi()
        {
            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 6;
            _root.Margin = new Padding(0);
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, TabBarHeight));   // 0 页签
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));             // 1 结果条（跑完才有）
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));             // 2 搜索行
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));            // 3 按钮墙
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));             // 4 日志面板
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, _statusBarHeight));// 5 状态栏

            // tabs: one flat button per page, equal width
            _tabBar = new TableLayoutPanel();
            _tabBar.Dock = DockStyle.Fill;
            _tabBar.Margin = new Padding(0);
            _tabBar.ColumnCount = Tabs.Ids.Length;
            _tabBar.RowCount = 1;
            for (int i = 0; i < Tabs.Ids.Length; i++)
            {
                _tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Tabs.Ids.Length));
            }
            foreach (string id in Tabs.Ids)
            {
                Button b = new Button();
                b.Text = Tabs.Display(id);
                b.Tag = id;
                b.Dock = DockStyle.Fill;
                b.Margin = new Padding(1, 2, 1, 0);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.UseVisualStyleBackColor = false;
                b.TabStop = true;
                b.Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
                b.Click += OnTabClick;
                _tabButtons[id] = b;
                _tabBar.Controls.Add(b);
            }
            _root.Controls.Add(_tabBar, 0, 0);

            // 结果条：跑完一个按钮之后在页签下面出现一行有颜色的横幅（成功 / 失败 + 用时），
            // 8 秒后自己消失，点它展开运行日志。用户 2026-10-04 的反馈："点击确认以后也没有
            // 成功或者失败的反馈" —— 以前只有底栏一闪而过的一行小字。
            _toastRow = new Panel();
            _toastRow.Dock = DockStyle.Fill;
            _toastRow.Margin = new Padding(0);
            _toastRow.Padding = new Padding(10, 2, 10, 2);
            _toastRow.Visible = false;
            _toastLabel = new Label();
            _toastLabel.Dock = DockStyle.Fill;
            _toastLabel.AutoSize = false;
            _toastLabel.TextAlign = ContentAlignment.MiddleLeft;
            _toastLabel.AutoEllipsis = true;
            _toastLabel.Cursor = Cursors.Hand;
            _toastLabel.Click += delegate { if (!_logPanel.Visible) { ToggleLogPanel(); } };
            _tips.SetToolTip(_toastLabel, "点这里展开运行日志（这条提示 8 秒后自己消失）");
            _toastRow.Controls.Add(_toastLabel);
            _root.Controls.Add(_toastRow, 0, 1);

            // search row (hidden until the user asks for it)
            _searchRow = new Panel();
            _searchRow.Dock = DockStyle.Fill;
            _searchRow.Margin = new Padding(0);
            _searchRow.Padding = new Padding(8, 3, 8, 3);
            _searchRow.Visible = false;
            _searchBox = new TextBox();
            _searchBox.Dock = DockStyle.Fill;
            _searchBox.BorderStyle = BorderStyle.FixedSingle;
            _searchBox.TextChanged += delegate
            {
                _filter = _searchBox.Text.Trim();
                BuildGrid();
                UpdateStatusBar();
            };
            _searchRow.Controls.Add(_searchBox);
            _root.Controls.Add(_searchRow, 0, 2);

            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.Margin = new Padding(0);
            _content.AutoScroll = true;
            // Dropping an exe / script / folder anywhere on the wall creates a button for it.
            _content.AllowDrop = true;
            _content.DragEnter += OnDragEnter;
            _content.DragDrop += OnDragDrop;
            _root.Controls.Add(_content, 0, 3);

            _logPanel = new Panel();
            _logPanel.Dock = DockStyle.Fill;
            _logPanel.Margin = new Padding(0);
            _logPanel.Padding = new Padding(8, 4, 8, 4);
            _logPanel.Visible = false;
            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.WordWrap = false;
            _logBox.Dock = DockStyle.Fill;
            _logBox.Font = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            _logPanel.Controls.Add(_logBox);
            _root.Controls.Add(_logPanel, 0, 4);

            _statusBar = new TableLayoutPanel();
            _statusBar.Dock = DockStyle.Fill;
            _statusBar.Margin = new Padding(0);
            _statusBar.ColumnCount = 2;
            _statusBar.RowCount = 1;
            // Without an explicit row style the single row is auto sized, so the buttons grew to
            // 30px inside a 23px bar and lost their bottom edge (interface regression B06).
            _statusBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _statusBar.Padding = new Padding(8, 0, 6, 0);
            _statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _statusLabel = new Label();
            _statusLabel.AutoSize = false;
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Margin = new Padding(0);

            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.AutoSize = true;
            right.Margin = new Padding(0);
            right.RowCount = 1;
            right.ColumnCount = 5;
            for (int i = 0; i < 5; i++) { right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _btnSearch = MakeBarButton("搜索", "搜索按钮（Ctrl+F）");
            _btnSearch.Click += delegate { ShowSearch(!_searchRow.Visible); };
            _btnLog = MakeBarButton("日志", "展开或收起运行日志（Ctrl+L）");
            _btnLog.Click += delegate { ToggleLogPanel(); };
            _btnSettings = MakeBarButton("设置", "主题、启动方式、工具目录、永久删除安装器路径");
            _btnSettings.Click += delegate { OpenSettings(); };
            // 关于窗口（署名/许可证/工具目录入口）本来是「右键增强」页签里的一个按钮，
            // 那个页签收敛成一个按钮以后它就没了入口，所以放到这一排。
            _btnAbout = MakeBarButton("关于", "版本、作者、许可证、工具目录");
            _btnAbout.Click += delegate { OpenAbout(); };
            _btnUpdate = MakeBarButton("检查更新", "工具自身的更新检查（P2 接入）");
            _btnUpdate.Click += delegate { ShowUpdateNotice(); };
            right.Controls.Add(_btnSearch);
            right.Controls.Add(_btnLog);
            right.Controls.Add(_btnSettings);
            right.Controls.Add(_btnAbout);
            right.Controls.Add(_btnUpdate);

            _statusBar.Controls.Add(_statusLabel, 0, 0);
            _statusBar.Controls.Add(right, 1, 0);
            _root.Controls.Add(_statusBar, 0, 5);

            Controls.Add(_root);
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            BuildContextMenu();
        }

        private Button MakeBarButton(string text, string tip)
        {
            Button b = new Button();
            b.Text = text;
            // Fixed height on purpose: an AutoSize button grows to ~28px, which sticks out of the
            // 24px status bar and gets clipped (interface regression B06). The width is measured
            // from the label so the AutoSize column gives it exactly that much room.
            b.AutoSize = false;
            b.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            b.Height = _barButtonHeight;
            b.Width = TextRenderer.MeasureText(text, b.Font).Width + 12;
            b.Margin = new Padding(4, 2, 0, 2);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.TabStop = false;
            b.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            _tips.SetToolTip(b, tip);
            return b;
        }

        private void BuildContextMenu()
        {
            _menu = new ContextMenuStrip();
            _miRun = new ToolStripMenuItem("运行");
            _miRun.Click += delegate { if (_menuTarget != null) { RunTool(_menuTarget, false); } };
            _miRunAdmin = new ToolStripMenuItem("以管理员身份运行");
            _miRunAdmin.Click += delegate { if (_menuTarget != null) { RunTool(_menuTarget, true); } };
            _miReveal = new ToolStripMenuItem("打开所在文件夹");
            _miReveal.Click += delegate { RevealTarget(); };
            _miCopy = new ToolStripMenuItem("复制启动命令");
            _miCopy.Click += delegate { CopyCommand(); };
            _miDefine = new ToolStripMenuItem("查看按钮定义");
            _miDefine.Click += delegate { ShowDefinition(); };
            // 「功能说明」和「查看按钮定义」是两件事：前者给人看（干什么用 / 怎么用），
            // 后者给排查问题看（来源清单 / 路径 / 启动命令）。放在「运行」旁边 —— 用户想点之前
            // 顺手先看一眼「这东西到底是干嘛的」。
            _miHelp = new ToolStripMenuItem("功能说明…");
            _miHelp.Click += delegate { ShowHelp(); };
            _miEdit = new ToolStripMenuItem("编辑按钮…");
            _miEdit.Click += delegate { EditUserButton(); };
            _miDelete = new ToolStripMenuItem("删除按钮");
            _miDelete.Click += delegate { DeleteUserButton(); };

            _miPin = new ToolStripMenuItem("置顶 / 取消置顶（排在这一页最前）");
            _miPin.Click += delegate { TogglePin(); };

            _menu.Items.Add(_miRun);
            _menu.Items.Add(_miRunAdmin);
            _menu.Items.Add(_miHelp);
            _menu.Items.Add(_miPin);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miEdit);
            _menu.Items.Add(_miDelete);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miReveal);
            _menu.Items.Add(_miCopy);
            _menu.Items.Add(_miDefine);
            _menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem add = new ToolStripMenuItem("新建按钮…（也可以把 exe / 脚本拖进窗口）");
            add.Click += delegate { NewUserButton(null, null); };
            _menu.Items.Add(add);
            _menu.Opening += OnMenuOpening;
        }

        // ---------------------------------------------------------------- grid

        /// <summary>Measures the widest button label at the real font size: 16px icon + padding
        /// + gutter. Clamped, so a rogue 40 character button name cannot blow the window up.</summary>
        private int ComputeCellWidth()
        {
            int widest = 0;
            using (Font probe = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point))
            {
                foreach (ToolItem t in _tools)
                {
                    if (t.Hidden) { continue; }
                    int w = TextRenderer.MeasureText(t.Name, probe).Width;
                    if (w > widest) { widest = w; }
                }
            }
            // 36 = 16px icon + the gap WinForms leaves between image and text + button padding,
            // plus a safety margin: with only 22 the longest labels ("关闭实时防护与篡改") came out
            // as "关闭实时防护与…" in the rendered window.
            int cell = widest + 36 + 8;
            if (cell < 104) { cell = 104; }
            if (cell > 170) { cell = 170; }
            return cell;
        }

        /// <summary>网格里的一块按钮：一句小标题 + 它下面那几个按钮。普通页签里一块 = 一个
        /// segment（标题来自清单的 segmentName）；搜索时一块 = 一个页签；「常用」页里一块 =
        /// 置顶 / 最近使用。</summary>
        private sealed class GridBlock
        {
            public string Caption = "";
            public List<ToolItem> Items = new List<ToolItem>();
        }

        private void BuildGrid()
        {
            if (_grid != null)
            {
                _content.Controls.Remove(_grid);
                _grid.Dispose();
                _grid = null;
            }
            _gridButtons.Clear();
            _sepLabels.Clear();

            _grid = new TableLayoutPanel();
            _grid.Dock = DockStyle.Top;
            _grid.AutoSize = true;
            _grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grid.AllowDrop = true;
            _grid.DragEnter += OnDragEnter;
            _grid.DragDrop += OnDragDrop;
            _grid.ColumnCount = Columns + 1;
            _grid.RowCount = 0;
            _grid.Margin = new Padding(0);
            _grid.Padding = new Padding(8, 6, 8, 6);
            for (int c = 0; c < Columns; c++)
            {
                _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _cellWidth));
            }
            // A trailing percent column swallows the leftover width. Without it a Dock=Top grid
            // hands the surplus to the LAST button column, so every fourth button came out
            // wider than its neighbours (interface regression B05).
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            int row = -1;
            int col = Columns;
            _contentRows = 0;
            _contentSeps = 0;
            int shown = 0;
            bool firstBlock = true;
            foreach (GridBlock block in CollectBlocks())
            {
                if (block.Items.Count == 0) { continue; }
                if (block.Caption.Length > 0 || !firstBlock) { AddSeparatorRow(ref row, block.Caption); }
                firstBlock = false;
                col = Columns;
                foreach (ToolItem t in block.Items)
                {
                    if (col >= Columns)
                    {
                        row++;
                        _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, ToolRowHeight));
                        _grid.RowCount = row + 1;
                        _contentRows++;
                        col = 0;
                    }
                    ToolButton b = new ToolButton(t);
                    b.Dock = DockStyle.Fill;
                    b.Margin = new Padding(3);
                    b.Click += OnToolClick;
                    b.MouseDoubleClick += OnToolDoubleClick;
                    b.ContextMenuStrip = _menu;
                    b.Enter += delegate { UpdateStatusBar(); };
                    _tips.SetToolTip(b, TipFor(t, _settings));
                    _grid.Controls.Add(b, col, row);
                    _gridButtons.Add(b);
                    col++;
                    shown++;
                }
            }

            if (shown == 0)
            {
                row++;
                _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, ToolRowHeight));
                _grid.RowCount = row + 1;
                _contentRows++;
                Label empty = new Label();
                empty.AutoSize = true;
                empty.Margin = new Padding(6, 6, 6, 6);
                empty.Text = EmptyMessage();
                _grid.Controls.Add(empty, 0, row);
                _grid.SetColumnSpan(empty, Columns);
            }

            _content.Controls.Add(_grid);
            foreach (ToolButton b in _gridButtons) { b.ApplyTheme(_theme); }
            ColorSeparators();
            FitToContent();
        }

        /// <summary>这一页（或这次搜索）该显示哪些块。</summary>
        private List<GridBlock> CollectBlocks()
        {
            List<GridBlock> blocks = new List<GridBlock>();

            if (_filter.Length > 0)
            {
                // 搜索是跨页签的：104 个按钮分散在 8 个页签里，只在当前页签里找，用户会得到
                // 一句"这个页签里没有"然后永远找不到（在「常用设置」页搜「隐私」就是这种情况）。
                foreach (string tabId in Tabs.Ids)
                {
                    GridBlock blk = new GridBlock();
                    foreach (ToolItem t in _tools)
                    {
                        if (t.Hidden) { continue; }
                        if (!string.Equals(t.Tab, tabId, StringComparison.OrdinalIgnoreCase)) { continue; }
                        if (!Matches(t, _filter)) { continue; }
                        blk.Items.Add(t);
                    }
                    if (blk.Items.Count == 0) { continue; }
                    blk.Caption = Tabs.Display(tabId) + " · " + blk.Items.Count + " 个";
                    blocks.Add(blk);
                }
                return blocks;
            }

            if (string.Equals(_currentTab, Tabs.Recent, StringComparison.OrdinalIgnoreCase))
            {
                List<ToolItem> pinned;
                List<ToolItem> recent;
                SplitFavorites(out pinned, out recent);
                if (pinned.Count > 0)
                {
                    GridBlock p = new GridBlock();
                    p.Caption = "置顶 · " + pinned.Count + " 个（在按钮上点右键可以取消置顶）";
                    p.Items = pinned;
                    blocks.Add(p);
                }
                if (recent.Count > 0)
                {
                    GridBlock r = new GridBlock();
                    r.Caption = "最近使用 · " + recent.Count + " 个（最多 " + UserTools.RecentLimit + " 个）";
                    r.Items = recent;
                    blocks.Add(r);
                }
                return blocks;
            }

            // 普通页签：按 segment 分组，标题来自清单里的 segmentName（没写就只画一条细线）
            int segment = int.MinValue;
            GridBlock current = null;
            foreach (ToolItem t in _tools)
            {
                if (t.Hidden) { continue; }
                if (!string.Equals(t.Tab, _currentTab, StringComparison.OrdinalIgnoreCase)) { continue; }
                if (current == null || t.Segment != segment)
                {
                    segment = t.Segment;
                    current = new GridBlock();
                    blocks.Add(current);
                }
                if (current.Caption.Length == 0 && t.SegmentName.Length > 0) { current.Caption = t.SegmentName; }
                current.Items.Add(t);
            }
            return blocks;
        }

        /// <summary>「常用」页的两块内容：置顶的（跨页签汇总）+ 最近点过的。
        /// 置顶以前只是在本页内往前排，跨页签的常用按钮还是得挨个页签去找。</summary>
        private void SplitFavorites(out List<ToolItem> pinned, out List<ToolItem> recent)
        {
            pinned = new List<ToolItem>();
            recent = new List<ToolItem>();
            List<string> pinIds = UserTools.LoadPinned();
            foreach (ToolItem t in _tools)
            {
                if (t.Hidden || t.Placeholder || !t.Pinned && !UserTools.IsPinned(t.Id, pinIds)) { continue; }
                pinned.Add(t);
            }
            foreach (string id in UserTools.LoadRecent())
            {
                ToolItem found = null;
                foreach (ToolItem t in _tools)
                {
                    if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) { found = t; break; }
                }
                if (found == null || found.Hidden || found.Placeholder) { continue; }
                if (found.Pinned || UserTools.IsPinned(found.Id, pinIds)) { continue; }   // 置顶那块已经显示了
                recent.Add(found);
            }
        }

        private string EmptyMessage()
        {
            if (_filter.Length > 0)
            {
                return "8 个页签里都没有匹配「" + _filter + "」的按钮。搜的是按钮名 / 说明 / id，试短一点的关键字。";
            }
            if (string.Equals(_currentTab, Tabs.Recent, StringComparison.OrdinalIgnoreCase))
            {
                return "这里还什么都没有：在任意按钮上点右键 →「置顶」，或者随便点几个按钮，最近用过的就会自动出现在「常用」页。";
            }
            return "这个页签还没有按钮。把 exe / 脚本拖进来，或编辑 " + AppPaths.UserToolsJson;
        }

        private void AddSeparatorRow(ref int row, string caption)
        {
            row++;
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute,
                caption.Length > 0 ? CaptionRowHeight : SeparatorRowHeight));
            _grid.RowCount = row + 1;
            _contentSeps++;

            if (caption.Length == 0)
            {
                Panel sep = new Panel();
                sep.Dock = DockStyle.Fill;
                sep.Margin = new Padding(3, 6, 3, 6);
                sep.BackColor = _theme.SegmentLine;
                _grid.Controls.Add(sep, 0, row);
                _grid.SetColumnSpan(sep, Columns);
                return;
            }

            // 标题行：左边一句人话，右边一条细线（细线的高度 = 行高减去上下外边距）
            TableLayoutPanel line = new TableLayoutPanel();
            line.Dock = DockStyle.Fill;
            line.Margin = new Padding(8, 0, 8, 0);
            line.ColumnCount = 2;
            line.RowCount = 1;
            line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            line.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Label cap = new Label();
            cap.AutoSize = true;
            // 只锚左边 = 垂直居中（上下都不锚定的时候 WinForms 会把它摆在中间）
            cap.Anchor = AnchorStyles.Left;
            cap.Text = caption;
            cap.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            cap.ForeColor = _theme.BarText;
            cap.BackColor = Color.Transparent;
            cap.TextAlign = ContentAlignment.MiddleLeft;
            cap.Margin = new Padding(0, 0, 8, 0);

            Panel rule = new Panel();
            rule.Dock = DockStyle.Fill;
            rule.Margin = new Padding(0, (CaptionRowHeight / 2), 0, (CaptionRowHeight / 2) - 1);
            rule.BackColor = _theme.SegmentLine;

            line.Controls.Add(cap, 0, 0);
            line.Controls.Add(rule, 1, 0);
            _grid.Controls.Add(line, 0, row);
            _grid.SetColumnSpan(line, Columns);
            _sepLabels.Add(cap);
        }

        private void ColorSeparators()
        {
            if (_grid == null) { return; }
            foreach (Control c in _grid.Controls)
            {
                Panel p = c as Panel;
                if (p != null) { p.BackColor = _theme.SegmentLine; }
            }
            foreach (Label l in _sepLabels) { l.ForeColor = _theme.BarText; }
        }

        // ---------------------------------------------------------------- 窗口尺寸

        /// <summary>窗口宽度和高度。
        ///
        /// 用户 2026-10-04 定的规矩：**默认固定尺寸**（宽度固定、高度固定），想让它跟着内容变
        /// 得自己去设置里勾「窗口高度跟随当前页签的内容」。宽度永远不会跟着内容变 —— 按钮是固定
        /// 宽度的 4 列网格，加一个名字很长的按钮不该把整个窗口撑宽（那会让窗口一直在跳）。
        ///
        /// 勾了跟随内容之后：高度 = 页签条 + 当前页按钮墙 + 状态栏（+ 搜索行 + 日志面板），
        /// 上限是屏幕工作区的九成，超过就在按钮墙里滚动。</summary>
        private void FitToContent()
        {
            if (_fixedWidth <= 0) { _fixedWidth = FixedWidth(); }
            ClampCellWidthToWindow();
            // 第一次量出来的宽度立刻记进设置：下次开机还是这个宽度。不这么写的话，用户加了一个名字
            // 很长的按钮、下次开机窗口就宽一圈 —— 那就不是"固定宽度"了（Test-Gui 的 H04 盯着这条）。
            // 用户自己拖过窗口的话，RememberGeometry 会把拖出来的尺寸写成新的固定宽度。
            if (_settings.WindowWidth != _fixedWidth)
            {
                _settings.WindowWidth = _fixedWidth;
                _settings.Save();
            }
            int width = _fixedWidth;

            int height;
            if (_settings.WindowAutoSize)
            {
                height = TabBarHeight + ContentHeight() + _statusBarHeight;
                if (_toastRow != null && _toastRow.Visible) { height += ToastRowHeight; }
                if (_searchRow != null && _searchRow.Visible) { height += SearchRowHeight; }
                if (_logPanel != null && _logPanel.Visible) { height += LogPanelHeight; }
                int max = ScreenHeight() - 80;
                if (height > max) { height = max; }
                if (height < 240) { height = 240; }
            }
            else
            {
                height = (_settings.WindowHeight > 0) ? _settings.WindowHeight : DefaultFixedHeight;
            }

            if (ClientSize.Width == width && ClientSize.Height == height) { return; }
            _fitting = true;
            try { ClientSize = new Size(width, height); }
            finally { _fitting = false; }
        }

        /// <summary>固定的宽度：设置里记过就用记着的那一个，否则四列按钮 + 网格内边距
        /// （启动时算一次，之后不再跟着内容变）。</summary>
        private int FixedWidth()
        {
            if (_settings.WindowWidth > 0) { return _settings.WindowWidth; }
            int w = Columns * _cellWidth + 24;
            if (w < 520) { w = 520; }
            return w;
        }

        /// <summary>列宽不许超过窗口宽度允许的上限。宽度是固定的：名字超长的按钮改用省略号
        /// （ToolButton.AutoEllipsis，悬停提示里是全名），既不能把窗口撑宽，也不能把同排别的
        /// 按钮挤出去 / 挤出窗口（Test-Gui 的 H04 / H05 盯着这两条）。</summary>
        private void ClampCellWidthToWindow()
        {
            if (_fixedWidth <= 0) { return; }
            int max = (_fixedWidth - 24) / Columns;
            if (max < 104) { max = 104; }
            if (_cellWidth > max) { _cellWidth = max; }
        }

        private int ContentHeight()
        {
            // 按钮行 + 分隔/标题行 + 网格自己的上下 padding（BuildGrid 里的 Padding(8,6,8,6)）
            return _contentRows * ToolRowHeight + _contentSeps * CaptionRowHeight + 12;
        }

        private int ScreenHeight()
        {
            try
            {
                Screen s = IsHandleCreated ? Screen.FromControl(this) : Screen.PrimaryScreen;
                if (s != null) { return s.WorkingArea.Height; }
            }
            catch { }
            return 900;
        }

        /// <summary>记住窗口现在在哪、多大。拖过窗口之后不再自动改高度（设置里可以改回「跟随内容」）。</summary>
        private void RememberGeometry()
        {
            if (WindowState != FormWindowState.Normal) { return; }
            _settings.WindowX = Location.X;
            _settings.WindowY = Location.Y;
            _settings.WindowWidth = ClientSize.Width;
            _settings.WindowHeight = ClientSize.Height;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // 《免责声明与服务条款》的首次运行确认门：没勾选同意就不写注册表、不做任何改动，
            // 点「不同意，退出」直接关掉程序（口径与窗口原文见 docs/DISCLAIMER.md 5.1）。
            // 命令行（list / run / status…）不拦 —— 那是脚本场景，可以用 consent --accept 先记录同意。
            if (!Consent.IsAccepted())
            {
                Logger.Write("启动", "还没有同意当前这版条款，先弹确认窗口");
                if (!Consent.EnsureAccepted(this, _theme, "首次运行界面"))
                {
                    // 窗口还没显示出来就退：先让消息循环转起来再关，别在 OnLoad 里硬关。
                    BeginInvoke((MethodInvoker)delegate { Close(); });
                    return;
                }
                // 同意记录是 Consent 直接写盘的。这份 _settings 是**构造函数里**加载的（比确认门早），
                // 不刷新的话，关闭窗口时 Save() 会把刚记下的同意覆盖成空 —— 那就是用户 2026-10-05
                // 报的「每次打开都弹」（Save() 里另有一道合并兜底，见 src\Settings.cs）。
                _settings.AgreedDisclaimer = Consent.StoredHash();
                _settings.AgreedAt = Consent.AgreedAt();
            }
            if (_settings.WindowX >= 0 && _settings.WindowY >= 0 && IsOnScreen(_settings.WindowX, _settings.WindowY))
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(_settings.WindowX, _settings.WindowY);
            }
            FitToContent();
            if (StartupAction.Length > 0)
            {
                string action = StartupAction;
                StartupAction = "";   // 只执行一次
                BeginInvoke((MethodInvoker)delegate { RunStartupAction(action); });
            }
            // 已经装过右键菜单的话，顺手把**工具箱自己写的**那几个键修补到当前版本：旧版把
            // 「文件夹里的空白处」和「桌面空白处」的命令写成了 %1（资源管理器在那两个位置不替换 %1），
            // 图标也指向了一个自己没有图标资源的 exe（菜单里是空白）—— 2026-10-04 用户报的两个问题。
            // 没装过就什么都不做：不许因为"打开一下工具箱"就往注册表里写东西。
            // 界面回归测试用 MXX1_NO_RIGHTMENU_SYNC=1 关掉（测试不该碰用户的真实菜单）。
            if (!RightMenu.SyncDisabled)
            {
                BeginInvoke((MethodInvoker)delegate { RightMenu.SyncIfInstalled(); });
            }
            // 界面起来之后查一次更新（只读版本号；MXX1_NO_UPDATE=1 时一个字节都不发；失败静默）。
            BeginInvoke((MethodInvoker)delegate { StartUpdateCheck(); });
        }

        /// <summary>这个按钮会不会改动系统（写注册表、装右键菜单）？「隐私设置」「常用设置」
        /// 「右键增强」这三页都会 —— 所以它们按下去之前要再过一次条款同意状态（见 RunTool）。</summary>
        private static bool WritesSystem(ToolItem t)
        {
            if (t.Kind != "builtin") { return false; }
            return t.Module == Launcher.ModulePrivacy
                || t.Module == Launcher.ModuleSysreg
                || t.Module == Launcher.ModuleRightMenu;
        }

        /// <summary>`ui &lt;动作&gt;` 进来的：窗口已经显示出来了，再做那件事
        /// （右键「常用功能」子菜单里的「运行日志」「设置」就是这条路）。</summary>
        private void RunStartupAction(string action)
        {
            if (action == "log") { OpenToolboxLog(); }
            else if (action == "settings") { OpenSettings(); }
            else if (action == "about") { OpenAbout(); }
            else
            {
                SetStatus("不认识的界面动作：" + action);
                Logger.Write("启动", "不认识的界面动作：" + action);
            }
        }

        /// <summary>上次的位置还在屏幕里吗（换显示器、拔掉外接屏之后，记下来的坐标可能已经在屏幕外）。</summary>
        private bool IsOnScreen(int x, int y)
        {
            try
            {
                foreach (Screen s in Screen.AllScreens)
                {
                    Rectangle r = s.WorkingArea;
                    if (x >= r.Left - 20 && x < r.Right - 80 && y >= r.Top - 20 && y < r.Bottom - 60) { return true; }
                }
            }
            catch { }
            return false;
        }

        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private Size _sizeBeforeDrag;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ENTERSIZEMOVE) { _sizeBeforeDrag = ClientSize; }
            base.WndProc(ref m);
            // _fitting 期间是我们自己在按内容改尺寸，那不是"用户拖过窗口"
            if (m.Msg != WM_EXITSIZEMOVE || _fitting) { return; }
            bool resized = (_sizeBeforeDrag != ClientSize);
            if (resized && _settings.WindowAutoSize)
            {
                // 用户自己拖了边框：之后按这个尺寸显示（设置里能改回「跟随内容」）
                _settings.WindowAutoSize = false;
                SetStatus("窗口大小已记住 · 在「设置」里可以改回跟随内容");
                Logger.Write("窗口", "用户手动调整了窗口大小 " + ClientSize.Width + "x" + ClientSize.Height + "，之后按这个尺寸显示");
            }
            // 拖过的尺寸就是新的固定尺寸（宽度也一样：不勾跟随内容时窗口尺寸只由用户决定）
            _fixedWidth = ClientSize.Width;
            RememberGeometry();
            _settings.Save();
        }

        private static bool Matches(ToolItem t, string needle)
        {
            string n = needle.ToLowerInvariant();
            return t.Name.ToLowerInvariant().IndexOf(n, StringComparison.Ordinal) >= 0
                || t.Id.ToLowerInvariant().IndexOf(n, StringComparison.Ordinal) >= 0
                || t.Hint.ToLowerInvariant().IndexOf(n, StringComparison.Ordinal) >= 0
                || Tabs.Display(t.Tab).ToLowerInvariant().IndexOf(n, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Hover text. It says what the button DOES in plain words first -- the button's own
        /// hint -- and never dumps a wall of script: the old version appended the ready-to-run command
        /// line, so hovering 「一键清理垃圾」 showed a 700-character PowerShell body on one line, and
        /// the hint sentence (the only part written for a human) was not shown at all. A live
        /// complaint from the user, 2026-10-04: 「鼠标悬停的说明没有做好」.
        ///
        /// The command is appended only when a person can actually read it: a path, a URI, a short
        /// switch line. Longer than ~100 characters means it is an inline script body -- that belongs
        /// in 「查看按钮定义」 (right-click menu), not in a tooltip.
        ///
        /// Public so the CLI can print the exact same string (Mxx1Toolbox.exe tip) and the test suite
        /// can assert on it without moving the real mouse pointer.</summary>
        public static string TipFor(ToolItem t, Settings settings)
        {
            string s = t.Name;
            if (t.Hint.Length > 0) { s += Environment.NewLine + t.Hint; }
            if (t.Placeholder)
            {
                return s + Environment.NewLine + "功能还没接入：这个按钮是灰的，点不动";
            }
            if (t.Danger) { s += Environment.NewLine + "会改动系统：点下去先弹确认框"; }
            if (t.RunAsAdmin) { s += Environment.NewLine + "需要管理员权限：会弹 UAC 窗口"; }
            string cmd = Launcher.DescribeCommand(t, settings, false);
            if (cmd.Length > 0 && cmd.Length <= 100) { s += Environment.NewLine + cmd; }
            return s;
        }

        /// <summary>「功能说明」窗口里的整段正文。**界面和命令行共用这一个函数**
        /// （`Mxx1Toolbox.exe tip <id> --full` 打的就是它），所以测试可以在不弹窗口的前提下
        /// 断言"说明窗口里到底写了什么"。
        ///
        /// 正文来源只有清单：`hint`（一句话）+ `about`（详情整段）。程序里**不另抄一份文案** ——
        /// 抄一份就会出现"说明窗口和悬停提示说两套话"。</summary>
        public static string HelpText(ToolItem t, Settings settings)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("【它是干什么的】");
            sb.AppendLine(t.Hint.Length > 0 ? t.Hint : "（这个按钮还没有写一句话说明）");
            sb.AppendLine();
            sb.AppendLine("【怎么用】");
            sb.AppendLine(t.About.Trim().Length > 0
                ? t.About.Trim()
                : "（这个按钮还没有写详细说明，上面那句就是它目前的全部说明。）");
            string marks = HelpMarks(t);
            if (marks.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("【要注意什么】");
                sb.AppendLine(marks.TrimEnd());
            }
            sb.AppendLine();
            sb.AppendLine("【点下去会执行什么】");
            string cmd = Launcher.DescribeCommand(t, settings, false);
            if (t.Placeholder)
            {
                sb.AppendLine("这个按钮现在是灰的、点不动：功能还没接进来"
                    + (t.Hint.Length > 0 ? "（" + t.Hint + "）" : "") + "。");
            }
            else if (cmd.Length > 200)
            {
                sb.AppendLine("这是一段比较长的脚本（" + cmd.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " 个字符），全文在按钮右键菜单的「查看按钮定义」里。");
            }
            else
            {
                sb.AppendLine(cmd);
            }
            return sb.ToString();
        }

        /// <summary>说明窗口里那一段"要注意什么"：危险 / 会弹确认框 / 要管理员权限 / 还没接功能。
        /// 悬停提示里也有同一批话（TipFor），但那边一行只能放一句，这里可以展开。</summary>
        private static string HelpMarks(ToolItem t)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (t.Danger) { sb.AppendLine("· 它会改动系统设置，而且是这一类里后果比较重的一个；点下去会先弹确认框。"); }
            else if (t.Confirm) { sb.AppendLine("· 它会改动系统设置，点下去会先弹一个确认框，确认之后才真的执行。"); }
            if (t.RunAsAdmin) { sb.AppendLine("· 需要管理员权限：会弹一个 UAC 窗口，要点「是」。"); }
            if (t.UserLayer) { sb.AppendLine("· 这是你自己加的按钮（在我的工具里），可以右键「编辑按钮」改它。"); }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- theme

        public void ApplyTheme()
        {
            _theme = Theme.Resolve(_settings.Theme);
            BackColor = _theme.FormBack;
            _root.BackColor = _theme.FormBack;
            _tabBar.BackColor = _theme.FormBack;
            _searchRow.BackColor = _theme.FormBack;
            _searchBox.BackColor = _theme.InputBack;
            _searchBox.ForeColor = _theme.InputText;
            _content.BackColor = _theme.FormBack;
            _logPanel.BackColor = _theme.FormBack;
            _logBox.BackColor = _theme.LogBack;
            _logBox.ForeColor = _theme.LogText;
            _statusBar.BackColor = _theme.BarBack;
            _statusLabel.BackColor = _theme.BarBack;
            _statusLabel.ForeColor = _theme.BarText;

            foreach (Button b in new Button[] { _btnSearch, _btnLog, _btnSettings, _btnAbout, _btnUpdate })
            {
                StyleFlat(b);
            }
            UpdateTabColors();
            foreach (ToolButton b in _gridButtons) { b.ApplyTheme(_theme); }
            ColorSeparators();
            if (_toastRow != null && _toastRow.Visible)
            {
                _toastRow.BackColor = _toastOk ? _theme.ToastOkBack : _theme.ToastFailBack;
                _toastLabel.BackColor = _toastRow.BackColor;
                _toastLabel.ForeColor = _toastOk ? _theme.ToastOkText : _theme.ToastFailText;
            }

            Native.ApplyDarkTitleBar(Handle, _theme.DarkMode);
            Native.ApplyDarkControl(_logBox.Handle, _theme.DarkMode);
            Invalidate(true);
        }

        private void StyleFlat(Button b)
        {
            b.BackColor = _theme.ButtonBack;
            b.ForeColor = _theme.ButtonText;
            b.FlatAppearance.BorderColor = _theme.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = _theme.ButtonHover;
            b.FlatAppearance.MouseDownBackColor = _theme.ButtonPressed;
        }

        private void UpdateTabColors()
        {
            foreach (KeyValuePair<string, Button> kv in _tabButtons)
            {
                bool active = string.Equals(kv.Key, _currentTab, StringComparison.OrdinalIgnoreCase);
                kv.Value.BackColor = active ? _theme.TabActiveBack : _theme.TabBack;
                kv.Value.ForeColor = active ? _theme.TabActiveText : _theme.TabText;
                kv.Value.FlatAppearance.BorderColor = active ? _theme.TabActiveUnderline : _theme.ButtonBorder;
                kv.Value.FlatAppearance.MouseOverBackColor = active ? _theme.TabActiveBack : _theme.ButtonHover;
                kv.Value.FlatAppearance.MouseDownBackColor = active ? _theme.TabActiveBack : _theme.ButtonPressed;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.ApplyDarkTitleBar(Handle, _theme.DarkMode);
        }

        private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (!string.Equals(_settings.Theme, Settings.ThemeSystem, StringComparison.OrdinalIgnoreCase)) { return; }
            try
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)delegate { ApplyTheme(); });
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------- actions

        private void OnTabClick(object sender, EventArgs e)
        {
            Button b = sender as Button;
            if (b == null || b.Tag == null) { return; }
            string tab = Convert.ToString(b.Tag);
            if (string.Equals(tab, _currentTab, StringComparison.OrdinalIgnoreCase)) { return; }
            _currentTab = tab;
            _settings.LastTab = tab;
            // 搜索是跨页签的（结果里已经标了页签名），所以切页签时把搜索收起来，
            // 不然用户会一个页签一个页签地看到同一份结果，以为页签坏了。
            if (_filter.Length > 0)
            {
                _searchBox.Text = "";
                _filter = "";
                ShowSearch(false);
                BuildGrid();
            }
            BuildGrid();
            UpdateTabColors();
            SetStatus("就绪");
        }

        private void OnToolClick(object sender, EventArgs e)
        {
            ToolButton b = sender as ToolButton;
            if (b == null || b.Busy) { return; }
            if (_settings.ClickMode == "double") { return; }   // wait for the double click
            RunTool(b, false);
        }

        private void OnToolDoubleClick(object sender, MouseEventArgs e)
        {
            ToolButton b = sender as ToolButton;
            if (b == null || b.Busy) { return; }
            if (_settings.ClickMode != "double") { return; }   // already handled by Click
            RunTool(b, false);
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _menuTarget = _menu.SourceControl as ToolButton;
            if (_menuTarget == null) { e.Cancel = true; return; }
            ToolItem t = _menuTarget.Tool;
            _miRun.Enabled = !_menuTarget.Busy;
            _miRunAdmin.Enabled = !_menuTarget.Busy;
            _miReveal.Enabled = RevealPath(t).Length > 0;
            _miCopy.Enabled = true;
            _miDefine.Enabled = true;
            // 说明窗口对每个按钮都能开：没写 about 的按钮也至少能说清"它是什么、会执行什么"
            _miHelp.Enabled = true;
            // Editing and deleting only makes sense for the buttons this program wrote itself
            // (the user layer); the built in ones live inside the exe.
            _miEdit.Enabled = t.UserLayer;
            _miDelete.Enabled = t.UserLayer;
        }

        // ---------------------------------------------------------------- user layer (我的工具)

        /// <summary>Graphical 新建按钮 / 编辑按钮. Saves into the user layer and rebuilds the wall.</summary>
        private void NewUserButton(ToolItem prefill, string droppedPath)
        {
            // Only one of these windows at a time. A file dropped while the window is already open
            // used to reach the grid's drop handler and open a SECOND 新建按钮 window (the user
            // reported exactly that: "拖入程序图标后会打开一个新的新建按钮弹出的窗口，应该只弹一个的").
            if (_userDialogOpen)
            {
                SetStatus("「新建按钮」窗口已经开着了 · 拖进来的文件会填进那个窗口");
                return;
            }
            _userDialogOpen = true;
            try
            {
                using (NewToolForm f = new NewToolForm(null, prefill, _theme))
                {
                    if (f.ShowDialog(this) != DialogResult.OK || f.Result == null) { return; }
                    List<ToolItem> mine = UserTools.Collect(_tools);
                    f.Result.Id = UserTools.NextId(f.Result.Name, mine, _tools);
                    f.Result.Order = UserTools.NextOrder(mine, f.Result.Tab);
                    mine.Add(f.Result);
                    string error = UserTools.Save(mine);
                    if (error.Length > 0)
                    {
                        MessageBox.Show(this, "保存失败：" + error, "新建按钮", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    Logger.Write(f.Result.Name, "新建按钮 " + f.Result.Id + (droppedPath == null ? "" : "（拖进来的 " + droppedPath + "）"));
                    ReloadAfterUserEdit(f.Result.Tab);
                    SetStatus("已添加按钮「" + f.Result.Name + "」");
                }
            }
            finally { _userDialogOpen = false; }
        }

        private void EditUserButton()
        {
            if (_menuTarget == null || _userDialogOpen) { return; }
            ToolItem target = _menuTarget.Tool;
            if (!target.UserLayer) { return; }
            _userDialogOpen = true;
            try
            {
                using (NewToolForm f = new NewToolForm(target, null, _theme))
                {
                    if (f.ShowDialog(this) != DialogResult.OK || f.Result == null) { return; }
                    List<ToolItem> mine = UserTools.Collect(_tools);
                    bool replaced = false;
                    for (int i = 0; i < mine.Count; i++)
                    {
                        if (!string.Equals(mine[i].Id, target.Id, StringComparison.OrdinalIgnoreCase)) { continue; }
                        f.Result.Id = target.Id;          // id / position stay put, only the rest changes
                        f.Result.Segment = target.Segment;
                        f.Result.Order = target.Order;
                        mine[i] = f.Result;
                        replaced = true;
                        break;
                    }
                    if (!replaced) { mine.Add(f.Result); }
                    string error = UserTools.Save(mine);
                    if (error.Length > 0)
                    {
                        MessageBox.Show(this, "保存失败：" + error, "编辑按钮", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    Logger.Write(f.Result.Name, "编辑按钮 " + f.Result.Id);
                    ReloadAfterUserEdit(f.Result.Tab);
                    SetStatus("已保存按钮「" + f.Result.Name + "」");
                }
            }
            finally { _userDialogOpen = false; }
        }

        private void DeleteUserButton()
        {
            if (_menuTarget == null) { return; }
            ToolItem target = _menuTarget.Tool;
            if (!target.UserLayer) { return; }
            DialogResult answer = MessageBox.Show(this,
                "删除按钮「" + target.Name + "」？" + Environment.NewLine + Environment.NewLine
                + "只删掉 " + AppPaths.UserToolsJson + " 里的这一条，内置按钮不受影响。",
                "删除按钮", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                SetStatus("已取消删除");
                return;
            }
            List<ToolItem> mine = UserTools.Collect(_tools);
            for (int i = mine.Count - 1; i >= 0; i--)
            {
                if (string.Equals(mine[i].Id, target.Id, StringComparison.OrdinalIgnoreCase)) { mine.RemoveAt(i); }
            }
            string error = UserTools.Save(mine);
            if (error.Length > 0)
            {
                MessageBox.Show(this, "删除失败：" + error, "删除按钮", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Logger.Write(target.Name, "删除按钮 " + target.Id);
            ReloadAfterUserEdit(_currentTab);
            SetStatus("已删除按钮「" + target.Name + "」");
        }

        /// <summary>Reloads the manifests (the user layer changed), re-measures the columns and
        /// rebuilds the wall. A longer button name widens the window -- never narrows it.</summary>
        /// <summary>把「我的工具」导出成一个文件（换机器 / 重装之前备份）。</summary>
        private void ExportUserTools()
        {
            List<ToolItem> mine = UserTools.Collect(_tools);
            if (mine.Count == 0)
            {
                MessageBox.Show(this, "还没有自己建的按钮，没什么可导出的。", "导出我的按钮",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "把「我的工具」导出成一个文件";
                dlg.Filter = "按钮清单 (*.json)|*.json|所有文件 (*.*)|*.*";
                dlg.FileName = "萌新工具箱-我的按钮.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                string error = UserTools.Export(dlg.FileName, mine);
                if (error.Length > 0)
                {
                    MessageBox.Show(this, "导出失败：" + error, "导出我的按钮", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SetStatus("已导出 " + mine.Count + " 个按钮 → " + dlg.FileName);
                MessageBox.Show(this, "已导出 " + mine.Count + " 个按钮：" + Environment.NewLine + dlg.FileName,
                    "导出我的按钮", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>把一个导出文件里的按钮并进来（同 id 覆盖）。</summary>
        private void ImportUserTools()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选一个导出文件（*.json）";
                dlg.Filter = "按钮清单 (*.json)|*.json|所有文件 (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                List<ToolItem> mine = UserTools.Collect(_tools);
                int added;
                int replaced;
                string error = UserTools.Import(dlg.FileName, mine, out added, out replaced);
                if (error.Length > 0)
                {
                    MessageBox.Show(this, "导入失败：" + error, "导入按钮", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string saveError = UserTools.Save(mine);
                if (saveError.Length > 0)
                {
                    MessageBox.Show(this, "保存失败：" + saveError, "导入按钮", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ReloadAfterUserEdit(Tabs.Mine);
                SetStatus("已导入：新增 " + added + " 个，覆盖 " + replaced + " 个");
            }
        }

        private void ReloadAfterUserEdit(string tab)
        {
            ReloadTools();
            // 重算列宽（只加宽不缩窄），但绝不越过窗口宽度允许的上限：宽度固定的规矩优先，
            // 名字超长的按钮改用省略号（悬停提示里是全名），不会把窗口撑宽。
            int cell = ComputeCellWidth();
            if (cell > _cellWidth) { _cellWidth = cell; }
            ClampCellWidthToWindow();
            if (!string.IsNullOrEmpty(tab)) { _currentTab = tab; UpdateTabColors(); }
            BuildGrid();
            UpdateStatusBar();
        }

        /// <summary>Builds a draft button from a dropped file or folder (see DroppedFile).</summary>
        private void OnDragEnter(object sender, DragEventArgs e)
        {
            bool files = (e.Data != null) && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = files ? DragDropEffects.Copy : DragDropEffects.None;
        }

        /// <summary>One dropped file opens the 新建按钮 window prefilled (so it can be renamed);
        /// several at once are created straight away with their file names.</summary>
        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] files = null;
            try { files = e.Data.GetData(DataFormats.FileDrop) as string[]; }
            catch { }
            if (files == null || files.Length == 0) { return; }

            if (files.Length == 1)
            {
                ToolItem draft = DroppedFile.Draft(files[0]);
                if (draft == null) { SetStatus("拖进来的东西不支持"); return; }
                NewUserButton(draft, files[0]);
                return;
            }

            List<ToolItem> mine = UserTools.Collect(_tools);
            List<string> added = new List<string>();
            foreach (string file in files)
            {
                ToolItem t = DroppedFile.Draft(file);
                if (t == null) { continue; }
                t.Id = UserTools.NextId(t.Name, mine, _tools);
                t.Order = UserTools.NextOrder(mine, t.Tab);
                mine.Add(t);
                added.Add(t.Name);
            }
            if (added.Count == 0) { SetStatus("拖进来的东西不支持"); return; }
            string error = UserTools.Save(mine);
            if (error.Length > 0)
            {
                MessageBox.Show(this, "保存失败：" + error, "拖进来的文件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Logger.Write("拖进来", "一次加了 " + added.Count + " 个按钮：" + string.Join("、", added.ToArray()));
            ReloadAfterUserEdit(Tabs.Mine);
            SetStatus("已添加 " + added.Count + " 个按钮：" + string.Join("、", added.ToArray()));
        }

        private void RunTool(ToolButton b, bool forceAdmin)
        {
            if (b == null || b.Busy) { return; }
            ToolItem t = b.Tool;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            bool asAdmin = forceAdmin || shift || t.RunAsAdmin;

            // 会改动系统的入口（写注册表的隐私设置 / 常用设置、装或卸右键菜单）再检查一次条款同意状态：
            // 首次运行的确认门在 OnLoad 里，这里只是"万一"的兜底 —— 用户拒绝就拦下这一个动作，
            // 不退出程序（口径见 docs/DISCLAIMER.md 5.1：命令行不拦，界面里的写动作要拦）。
            if (WritesSystem(t) && !Consent.IsAccepted())
            {
                if (!Consent.EnsureAccepted(this, _theme, "要改动系统的按钮「" + t.Name + "」"))
                {
                    SetStatus(t.Name + " · 没同意《免责声明与服务条款》，已拦下");
                    Logger.Write(t.Name, "用户没同意条款，动作被拦下");
                    return;
                }
            }

            if (_settings.ConfirmDangerous && (t.Danger || t.Confirm))
            {
                // 专用确认窗口，不是 MessageBox：MessageBox 里只能塞一段不能换行排版、不能滚动的
                // 纯文本，脚本类按钮等于把 700 字符的正文摊给用户看（用户 2026-10-04 的反馈）。
                bool elevatedChild = Launcher.IsAdmin() && asAdmin;
                DialogResult answer;
                using (ConfirmForm f = new ConfirmForm(t, Launcher.DescribeCommand(t, _settings, asAdmin), asAdmin, elevatedChild, _theme))
                {
                    answer = f.ShowDialog(this);
                }
                if (answer != DialogResult.OK)
                {
                    SetStatus(t.Name + " · 已取消");
                    Logger.Write(t.Name, "已取消（用户在确认框里选了取消）");
                    return;
                }
            }

            if (t.Placeholder)
            {
                // Grey buttons are disabled, so a mouse click never reaches this branch; it stays
                // as the safety net for the CLI path / a stale grid: it must still say the truth.
                string hint = t.Hint.Length > 0 ? t.Hint : "P1";
                SetStatus(t.Name + " · 功能待接入（" + hint + "）");
                Logger.Write(t.Name, "功能待接入（" + hint + "）· 这是灰色按钮，不能点");
                return;
            }

            if (t.Kind == "builtin" && t.Module == Launcher.ModuleApp)
            {
                HandleUiAction(t);
                return;
            }
            if (t.Kind == "builtin" && t.Module == Launcher.ModuleSystem && t.Action == "links")
            {
                OpenLinks();
                SetStatus(t.Name + " · 已打开常用链接");
                Logger.Write(t.Name, "打开常用链接窗口");
                return;
            }
            if (t.Kind == "builtin" && t.Module == Launcher.ModulePermdel && t.Action == "enginelog")
            {
                OpenEngineLog();
                SetStatus(t.Name + " · 已打开引擎日志");
                Logger.Write(t.Name, "打开引擎日志 " + AppPaths.PermdelEngineLog);
                return;
            }

            b.SetBusy(true, 0);
            _running++;
            if (_running == 1)
            {
                _runStarted = DateTime.Now;
                _lastElapsed = "";
                if (_runTimer != null) { _runTimer.Start(); }
            }
            // 「常用」页的「最近使用」：点过的按钮自动排到最前面（写进 recent.txt）
            UserTools.PushRecent(t.Id);
            // 装了右键「常用功能」子菜单的话，它列的就是这一份最近使用 —— 跟着重建一次
            // （没装就什么都不做，不许因为用户点了个按钮就悄悄改注册表）。
            RightMenu.SyncIfInstalled();
            SetStatus(t.Name + " · 正在运行…");
            UpdateStatusBar();
            Logger.Write(t.Name, "开始：" + Launcher.DescribeCommand(t, _settings, asAdmin));

            ToolButton button = b;
            ToolItem item = t;
            ThreadPool.QueueUserWorkItem(delegate
            {
                LaunchResult result;
                try { result = Launcher.Run(item, _settings, asAdmin, false); }
                catch (Exception ex)
                {
                    result = new LaunchResult();
                    result.Ok = false;
                    result.Message = ex.Message;
                }
                try { BeginInvoke((MethodInvoker)delegate { FinishRun(button, item, result); }); }
                catch { }
            });
        }

        private void FinishRun(ToolButton b, ToolItem t, LaunchResult r)
        {
            if (_running > 0) { _running--; }
            if (b != null && !b.IsDisposed) { b.SetBusy(false, 0); }
            // 跑完把用时说出来：脚本类按钮（DISM / SFC / 清理）以前只有一句"完成"，
            // 用户不知道刚才那几分钟到底干了什么、是不是白等了。
            string took = ElapsedText(DateTime.Now - _runStarted);
            if (_running == 0)
            {
                _runFinished = DateTime.Now;
                _lastElapsed = took;
                if (_runTimer != null) { _runTimer.Stop(); }
            }

            Logger.Write(t.Name, (r.Ok ? "完成" : "失败") + " · 用时 " + took + " · " + r.Message);
            SetStatus(t.Name + " · " + (r.Ok ? "完成" : "失败") + "（" + took + "）"
                + (r.ExitCode != 0 ? "（退出码 " + r.ExitCode + "）" : ""));
            RefreshLogBox();
            UpdateStatusBar();

            if (r.Deferred)
            {
                // 真正干活的是"提升权限后另起的那个进程"：它没有控制台，结果写在交接文件里。
                // 不盯着它的话，用户就只看到一句"已请求以管理员身份运行"、没有下文（这类
                // "点了没反应"的体验正是用户一直在报的那一类）。
                _elevatedSince = DateTime.Now.AddSeconds(-1);
                _elevatedTries = 0;
                if (_elevatedWatch == null)
                {
                    _elevatedWatch = new System.Windows.Forms.Timer();
                    _elevatedWatch.Interval = 1000;
                    _elevatedWatch.Tick += delegate { PickUpElevatedResult(); };
                }
                _elevatedWatch.Start();
                SetStatus(t.Name + " · 等管理员窗口确认……（结果会自动弹出来）");
                ShowToast(true, t.Name + " 已请求管理员权限 · 请在 UAC 窗口点「是」，结果随后自动弹出来");
                return;
            }

            // 结果反馈（用户 2026-10-04 的反馈："点击确认以后也没有成功或者失败的反馈"）：
            // ① 页签下面一条有颜色的结果条（8 秒后自己消失，点它看日志）
            // ② 有输出的还是照旧弹结果窗口（脚本类按钮就靠它看正文）
            // ③ 失败且没有输出 → 仍然弹一个提示框，别让失败悄悄过去
            string summary = (r.Message != null && r.Message.Length > 0) ? r.Message : (r.Ok ? "执行完成" : "执行失败");
            ShowToast(r.Ok, t.Name + " · " + took + " · " + summary);

            if (r.Output != null && r.Output.Trim().Length > 0)
            {
                string head = t.Name + "　——　" + (r.Ok ? "成功" : "失败") + "（用时 " + took + "）· " + r.Message;
                OutputForm f = new OutputForm(t.Name, head, r.Output, _theme);
                WindowPlacement.ShowCentered(f, this);
            }
            else if (!r.Ok)
            {
                MessageBox.Show(this, r.Message, t.Name + "（失败）", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>读取"提升权限的那个自己"留下的报告，用平时那个结果窗口显示出来。
        /// 没有这一步，用户点了需要管理员的按钮就只看到一句"已请求以管理员身份运行"，
        /// 真正的结果消失在那个没有控制台的进程里（用户报过的"点了没反应"就是这一类）。</summary>
        private void PickUpElevatedResult()
        {
            _elevatedTries++;
            try
            {
                string file = AppPaths.ElevatedResultFile;
                if (File.Exists(file) && File.GetLastWriteTime(file) >= _elevatedSince)
                {
                    string text = File.ReadAllText(file);
                    try { File.Delete(file); } catch { }
                    if (_elevatedWatch != null) { _elevatedWatch.Stop(); }
                    if (text.Trim().Length > 0)
                    {
                        Logger.Write("管理员动作", "结果已从提升权限的进程取回");
                        RefreshLogBox();
                        OutputForm form = new OutputForm("管理员动作的结果", "已用管理员身份执行完", text, _theme);
                        WindowPlacement.ShowCentered(form, this);
                        SetStatus("管理员动作已完成（结果见窗口和日志）");
                    }
                    return;
                }
            }
            catch { }
            if (_elevatedTries >= 25)   // 25 秒还没等到就别再轮询（用户可能把 UAC 取消了）
            {
                if (_elevatedWatch != null) { _elevatedWatch.Stop(); }
                SetStatus("没有等到管理员动作的结果（UAC 窗口被取消了吗？）");
            }
        }

        private void HandleUiAction(ToolItem t)
        {
            switch (t.Action)
            {
                case "about":
                    {
                        OpenAbout();
                        Logger.Write(t.Name, "打开关于窗口");
                        break;
                    }
                case "log":
                    OpenToolboxLog();
                    break;
                case "settings":
                    OpenSettings();
                    break;
                case "checkupdate":
                    ShowUpdateNotice();
                    break;
                case "exporttools":
                    {
                        ExportUserTools();
                        break;
                    }
                case "importtools":
                    {
                        ImportUserTools();
                        break;
                    }
                case "newtool":
                    NewUserButton(null, null);
                    break;
                case "hash":
                    OpenHashTool();
                    break;
                default:
                    SetStatus("未实现的界面动作：" + t.Action);
                    Logger.Write(t.Name, "未实现的界面动作：" + t.Action);
                    break;
            }
        }

        private void ShowUpdateNotice()
        {
            if (UpdateCheck.Disabled)
            {
                SetStatus("检查更新 · 已关闭（MXX1_NO_UPDATE=1）");
                Logger.Write("检查更新", "已关闭（MXX1_NO_UPDATE），没有联网");
                MessageBox.Show(this,
                    "更新检查已经关掉了（环境变量 MXX1_NO_UPDATE=1）。" + Environment.NewLine + Environment.NewLine
                    + "当前版本 v" + AboutForm.VersionText + "。" + Environment.NewLine
                    + "想恢复检查就把那个环境变量删掉或改成 0，再打开工具箱。",
                    "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 已经有结论（启动时查过）就直接用它，不再打第二次网络请求。
            UpdateResult r = UpdateCheck.Last;
            if (r.State != UpdateState.Unknown && r.State != UpdateState.Checking)
            {
                ReportUpdate(r);
                return;
            }

            SetStatus("检查更新 · 正在查…");
            Logger.Write("检查更新", "手动检查一次（只读版本号，不下载）");
            _btnUpdate.Enabled = false;
            UpdateCheck.CheckAsync(delegate(UpdateResult result)
            {
                try { BeginInvoke((MethodInvoker)delegate { _btnUpdate.Enabled = true; ReportUpdate(result); }); }
                catch { }
            });
        }

        /// <summary>把检查结果讲清楚：有新版本就把下载页打开（不自动下载、不替换文件）。</summary>
        private void ReportUpdate(UpdateResult r)
        {
            SetStatus("检查更新 · " + r.UiText);
            Logger.Write("检查更新", r.UiText + "（detail=" + r.Detail + "）");
            if (r.State == UpdateState.Available)
            {
                using (ConfirmForm f = new ConfirmForm(
                    "发现新版本", r.UiText + Environment.NewLine + Environment.NewLine
                    + "本工具不会自己下载、也不会替换文件 —— 要看这一版就打开发布页，"
                    + "下载和替换都由你自己决定。",
                    "打开发布页", _theme))
                {
                    if (f.ShowDialog(this) == DialogResult.OK) { OpenUrl(r.Url); }
                }
                MarkUpdateAvailable(r.Latest);
                return;
            }
            MessageBox.Show(this, r.UiText + Environment.NewLine + Environment.NewLine
                + "当前版本 v" + AboutForm.VersionText + "（只读版本号，不下载、不替换任何文件）。",
                "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>底栏那个按钮改成「发现新版本 vX」：一直挂在那儿，用户不用再点一次。
        /// 宽度按新文字重新量（底栏那一列是 AutoSize，标签列是 100%，所以只会挤标签、不会撑窗口）。</summary>
        private void MarkUpdateAvailable(string latest)
        {
            try
            {
                if (_btnUpdate == null || _btnUpdate.IsDisposed) { return; }
                _btnUpdate.Text = "发现新版本 v" + latest;
                _btnUpdate.Width = TextRenderer.MeasureText(_btnUpdate.Text, _btnUpdate.Font).Width + 12;
                _tips.SetToolTip(_btnUpdate, "有新版本 v" + latest + "（当前 v" + AboutForm.VersionText
                    + "）· 点一下打开发布页；本工具不自动下载、不替换文件");
            }
            catch { }
        }

        /// <summary>界面起来之后查一次（后台线程，失败静默；MXX1_NO_UPDATE=1 时一个字节都不发）。</summary>
        private void StartUpdateCheck()
        {
            if (UpdateCheck.Disabled) { return; }
            UpdateResult cached = UpdateCheck.Last;
            if (cached.State != UpdateState.Unknown && cached.State != UpdateState.Checking)
            {
                if (cached.State == UpdateState.Available) { MarkUpdateAvailable(cached.Latest); }
                return;
            }
            UpdateCheck.CheckAsync(delegate(UpdateResult r)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (r.State == UpdateState.Available) { MarkUpdateAvailable(r.Latest); }
                        SetStatus("就绪 · " + r.UiText);
                    });
                }
                catch { }
            });
        }

        /// <summary>在浏览器里打开一个网址。**全工具唯一的入口**（关于窗口的官网 / 仓库、
        /// 更新检查的「打开发布页」都走它），所以"点一下就能访问"只有这一处要保证。
        ///
        /// 回归测试要验"点了真的会去打开"：那种检查不能让测试机真弹出浏览器，所以
        /// `MXX1_NO_OPEN=1` 时**只写一行日志、不真打开** —— 测试点完去读日志，两边都干净。
        /// 日志照写（不管是哪种模式）：用户点了哪个网址，查日志时看得到。</summary>
        internal static void OpenUrl(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) { return; }
                Logger.Write("打开链接", url);

                string off = Environment.GetEnvironmentVariable("MXX1_NO_OPEN");
                if (off != null && off.Trim() == "1") { return; }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }

        /// <summary>About window: version, author, licence and the way into the tool folder.</summary>
        private void OpenAbout()
        {
            using (AboutForm f = new AboutForm(_theme))
            {
                f.ShowDialog(this);
            }
        }

        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(_settings, _theme))
            {
                DialogResult r = f.ShowDialog(this);
                if (r == DialogResult.OK)
                {
                    // 在设置里取消「跟随内容」时，把现在的窗口尺寸当成用户选定的尺寸记下来
                    if (!_settings.WindowAutoSize && (_settings.WindowWidth == 0 || _settings.WindowHeight == 0))
                    {
                        _settings.WindowWidth = ClientSize.Width;
                        _settings.WindowHeight = ClientSize.Height;
                    }
                    _settings.Save();
                    ApplyTheme();
                    ApplyLogPanelVisibility();
                    FitToContent();
                    SetStatus("设置已保存");
                    Logger.Write("设置", "主题=" + _settings.Theme + " 启动方式=" + _settings.ClickMode
                        + " 二次确认=" + (_settings.ConfirmDangerous ? "开" : "关")
                        + " 窗口跟随内容=" + (_settings.WindowAutoSize ? "开" : "关"));
                }
            }
        }

        private void OpenEngineLog()
        {
            LogForm f = new LogForm("引擎日志（永久删除）", AppPaths.PermdelEngineLog,
                "最新的在最上面 —— " + AppPaths.PermdelEngineLog, _theme, true);
            WindowPlacement.ShowCentered(f, this);
        }

        private void OpenLinks()
        {
            LinksForm f = new LinksForm(_theme);
            WindowPlacement.ShowCentered(f, this);
        }

        /// <summary>「文件哈希校验」窗口（系统工具页那个按钮）。这是程序**自己**的窗口，
        /// 不走 Launcher 起进程 —— 所以它是 module=app 的一条界面动作（和「新建按钮」同一类）。</summary>
        private void OpenHashTool()
        {
            HashForm f = new HashForm(_theme);
            WindowPlacement.ShowCentered(f, this);
            SetStatus("文件哈希校验 · 选一个文件（也可以直接拖进来）就能算出 MD5 / SHA256");
            Logger.Write("文件哈希校验", "打开窗口");
        }

        private void OpenToolboxLog()
        {
            LogForm f = new LogForm("运行日志（工具箱）", Logger.CurrentFile(),
                "最新的在最上面 —— " + Logger.CurrentFile(), _theme, true);
            WindowPlacement.ShowCentered(f, this);
        }

        // ---------------------------------------------------------------- helper bits

        private string RevealPath(ToolItem t)
        {
            if (t.Kind == "exe" || t.Kind == "script")
            {
                string p = AppPaths.Resolve(t.Path);
                return File.Exists(p) ? p : "";
            }
            if (t.Kind == "builtin" && t.Module == Launcher.ModulePermdel)
            {
                return Launcher.FindPermanentDeleteExe(_settings);
            }
            if (t.Kind == "open")
            {
                string p = AppPaths.Resolve(t.Target);
                return (File.Exists(p) || Directory.Exists(p)) ? p : "";
            }
            return "";
        }

        private void RevealTarget()
        {
            if (_menuTarget == null) { return; }
            string path = RevealPath(_menuTarget.Tool);
            if (path.Length == 0) { SetStatus("这个按钮没有对应的本地文件"); return; }
            Launcher.RevealInExplorer(path);
            SetStatus("已在资源管理器中打开 " + Path.GetFileName(path));
        }

        private void CopyCommand()
        {
            if (_menuTarget == null) { return; }
            string command = Launcher.DescribeCommand(_menuTarget.Tool, _settings, false);
            try { Clipboard.SetText(command); }
            catch { }
            SetStatus("已复制启动命令：" + command);
            Logger.Write(_menuTarget.Tool.Name, "复制启动命令：" + command);
        }

        private void ShowDefinition()
        {
            if (_menuTarget == null) { return; }
            ToolItem t = _menuTarget.Tool;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(t.Describe());
            sb.AppendLine();
            sb.AppendLine("来源        : " + t.Source);
            sb.AppendLine("页签 / 段   : " + Tabs.Display(t.Tab) + " / 第 " + t.Segment + " 段");
            sb.AppendLine("类型        : " + t.Kind + (t.Kind == "builtin" ? "（" + t.Module + "/" + t.Action + "）" : ""));
            if (t.Kind == "exe" || t.Kind == "script") { sb.AppendLine("路径        : " + AppPaths.Resolve(t.Path)); }
            if (t.Kind == "exe") { sb.AppendLine("参数        : " + t.Args); }
            if (t.Kind == "open") { sb.AppendLine("目标        : " + AppPaths.Resolve(t.Target)); }
            sb.AppendLine("启动命令    : " + Launcher.DescribeCommand(t, _settings, false));
            sb.AppendLine("需要管理员  : " + (t.RunAsAdmin ? "是" : "否"));
            sb.AppendLine("危险按钮    : " + (t.Danger ? "是" : "否"));
            sb.AppendLine("待接入      : " + (t.Placeholder ? "是（" + (t.Hint.Length > 0 ? t.Hint : "P1") + "）" : "否"));
            sb.AppendLine("图标        : " + t.IconPath + (File.Exists(t.IconPath) ? "（已找到）" : "（没有 PNG，界面用画的占位图标）"));
            OutputForm f = new OutputForm(t.Name + " · 按钮定义", "按钮定义（只读）", sb.ToString(), _theme);
            WindowPlacement.ShowCentered(f, this);
        }

        /// <summary>右键 →「功能说明…」：给人看的那一页（干什么用 / 怎么用 / 要注意什么），
        /// 正文由 HelpText 从清单的 hint + about 拼出来。不弹就不要它改任何东西 —— 纯只读窗口。</summary>
        private void ShowHelp()
        {
            if (_menuTarget == null) { return; }
            ShowHelpFor(_menuTarget);
        }

        /// <summary>打开某个按钮的「功能说明」。两个入口共用它：右键菜单 →「功能说明…」，
        /// 以及选中按钮后按 F1（F1 = 帮助是通用习惯，也顺手让这个窗口能用键盘开出来）。</summary>
        private void ShowHelpFor(ToolButton target)
        {
            if (target == null) { return; }
            ToolItem t = target.Tool;
            EventHandler run = delegate(object sender, EventArgs e)
            {
                // 说明窗口里那个「运行这个功能」：先把这个窗口关掉，再走和点按钮**完全同一条**路
                // （RunTool：确认框 / 提权 / 条款同意门一个都不少），不然就成了绕开安全门的后门。
                Control c = sender as Control;
                Form owner = (c != null) ? c.FindForm() : null;
                if (owner != null) { owner.Close(); }
                RunTool(target, false);
            };
            HelpForm f = new HelpForm(t, HelpText(t, _settings), t.Name, !t.Placeholder, _theme, run);
            WindowPlacement.ShowCentered(f, this);
        }

        private void ReloadTools()
        {
            _warnings.Clear();
            try { _tools = ToolRegistry.LoadAll(_warnings); }
            catch (Exception ex)
            {
                _tools = new List<ToolItem>();
                _warnings.Add(ex.Message);
            }
            // 置顶的排在本页最前（右键菜单 → 置顶 / 取消置顶；id 记在 pinned.txt 里）。
            // 清单里自带 pinned 的（比如「+ 新建按钮」）也算置顶。
            List<string> pinned = UserTools.LoadPinned();
            _tools.Sort(delegate(ToolItem a, ToolItem b)
            {
                int pa = (a.Pinned || UserTools.IsPinned(a.Id, pinned)) ? 0 : 1;
                int pb = (b.Pinned || UserTools.IsPinned(b.Id, pinned)) ? 0 : 1;
                if (pa != pb) { return pa - pb; }
                return ToolRegistry.Compare(a, b);
            });
            foreach (string w in _warnings) { Logger.Write("按钮定义", w); }
            // bin-tools 里的工具会自动长出按钮（v1.5.3）：说清楚是谁加的，用户才知道按钮为什么冒出来。
            int auto = 0;
            foreach (ToolItem t in _tools) { if (t.AutoLayer) { auto++; } }
            if (auto > 0)
            {
                Logger.Write("工具目录", AppPaths.PayloadDirName + " 里自动加载了 " + auto + " 个按钮（文件夹里的 "
                    + ToolFolders.ManifestName + " 或单个 exe）");
            }
        }

        /// <summary>右键菜单里的「置顶 / 取消置顶」：置顶的按钮排在这一页最前面。</summary>
        private void TogglePin()
        {
            ToolButton b = _menuTarget;
            if (b == null) { return; }
            List<string> pinned = UserTools.LoadPinned();
            bool byFile = UserTools.IsPinned(b.Tool.Id, pinned);
            if (b.Tool.Pinned && !byFile)
            {
                SetStatus("「" + b.Tool.Name + "」在按钮清单里就写着置顶，改不了");
                return;
            }
            string error = UserTools.SetPinned(b.Tool.Id, !byFile);
            if (error.Length > 0)
            {
                MessageBox.Show(this, "置顶失败：" + error, "置顶", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Logger.Write(b.Tool.Name, byFile ? "已取消置顶（命令行/右键）" : "已置顶：排在这一页最前面");
            RightMenu.SyncIfInstalled();   // 置顶的按钮就是右键「常用功能」里最上面那一段
            ReloadAfterUserEdit(b.Tool.Tab);
            SetStatus("「" + b.Tool.Name + "」" + (byFile ? "已取消置顶" : "已置顶：以后排在这一页最前面"));
        }

        private void SetStatus(string text)
        {
            _statusText = text;
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            int total = 0;
            int here = 0;
            int greyHere = 0;
            foreach (ToolItem t in _tools)
            {
                if (t.Hidden) { continue; }
                total++;
                if (string.Equals(t.Tab, _currentTab, StringComparison.OrdinalIgnoreCase))
                {
                    here++;
                    if (t.Placeholder) { greyHere++; }
                }
            }
            // Kept short on purpose: the label is a fixed width and a long line (the old format
            // plus a full "完成：xxx 退出码 0" message) overflowed it, so the tail was cut off.
            // The complete line is available as a tooltip and in the run log.
            string text;
            if (_filter.Length > 0)
            {
                // 搜索是跨页签的，底栏要说清查了几个页签、找到几个，别只报"本页"。
                int hits = 0;
                int tabsHit = 0;
                foreach (string id in Tabs.Ids)
                {
                    int n = 0;
                    foreach (ToolItem t in _tools)
                    {
                        if (t.Hidden || t.Tab != id || !Matches(t, _filter)) { continue; }
                        n++;
                    }
                    hits += n;
                    if (n > 0) { tabsHit++; }
                }
                text = "搜索「" + _filter + "」· " + tabsHit + " 个页签找到 " + hits + " 个 · " + _statusText;
            }
            else
            {
                // 长任务（DISM / SFC 要跑几分钟）没有计时的话，转个圈看不出是活着还是卡死了
                string head = (_running > 0)
                    ? "运行中 " + _running + "（已 " + ElapsedText(DateTime.Now - _runStarted) + "）"
                    : _statusText;
                text = head + " · 本页 " + here + " 个（共 " + total + "）";
            }
            // 底栏这一格宽度固定、长了会被裁，所以要紧的话放前面（"刚才那一下怎么了"必须看得见），
            // 提醒类的挂在后面。完整的一行仍然在悬停说明里。
            // 没提权时说一句：好几十个按钮要管理员权限，点了才弹 UAC 会让人以为是坏了。
            if (!Launcher.IsAdmin()) { text += " · 未提权"; }
            // A disabled control shows no tooltip, so the explanation for the grey buttons lives
            // here, and only while such a button is actually on this page.
            if (greyHere > 0) { text += " · 灰色 " + greyHere + " 个没接功能"; }
            if (_warnings.Count > 0) { text += " · 定义有 " + _warnings.Count + " 处问题"; }
            _statusLabel.Text = text;
            _tips.SetToolTip(_statusLabel, text + Environment.NewLine
                + "（灰色按钮点不动：禁用控件收不到鼠标消息，它的说明只能写在这一行）");
            _btnLog.Text = _logPanel.Visible ? "收起日志" : "日志";

            // 页签按钮的悬停说明：这一页有几个按钮。放在这里（而不是建按钮的时候）是因为
            // 「我的工具」里的按钮随时会被增删，建的时候算出来的数会过期。
            foreach (string id in Tabs.Ids)
            {
                Button tabButton;
                if (!_tabButtons.TryGetValue(id, out tabButton)) { continue; }
                int onTab = 0;
                if (string.Equals(id, Tabs.Recent, StringComparison.OrdinalIgnoreCase))
                {
                    List<ToolItem> pinnedForTip;
                    List<ToolItem> recentForTip;
                    SplitFavorites(out pinnedForTip, out recentForTip);
                    onTab = pinnedForTip.Count + recentForTip.Count;
                    _tips.SetToolTip(tabButton, "常用 · 置顶 " + pinnedForTip.Count + " 个 + 最近用过 "
                        + recentForTip.Count + " 个（点这里切换；右键任意按钮可以置顶）");
                    continue;
                }
                foreach (ToolItem t in _tools) { if (t.Tab == id && !t.Hidden) { onTab++; } }
                _tips.SetToolTip(tabButton, Tabs.Display(id) + " · " + onTab + " 个按钮（点这里切换）");
            }
        }

        /// <summary>跑完一个按钮的结果条：一句人话（成功 / 失败 + 用时 + 结果摘要），
        /// 底色跟着成败变，8 秒后自己消失；点它展开运行日志看细节。</summary>
        private void ShowToast(bool ok, string text)
        {
            if (_toastRow == null || _toastLabel == null) { return; }
            _toastLabel.Text = (ok ? "完成：" : "失败：") + text + "　（点这一条看运行日志）";
            _toastOk = ok;
            _toastRow.BackColor = ok ? _theme.ToastOkBack : _theme.ToastFailBack;
            _toastLabel.BackColor = _toastRow.BackColor;
            _toastLabel.ForeColor = ok ? _theme.ToastOkText : _theme.ToastFailText;
            _toastRow.Visible = true;
            _root.RowStyles[1].Height = (float)ToastRowHeight;
            if (_toastTimer == null)
            {
                _toastTimer = new System.Windows.Forms.Timer();
                _toastTimer.Interval = 8000;
                _toastTimer.Tick += delegate
                {
                    _toastTimer.Stop();
                    HideToast();
                };
            }
            _toastTimer.Stop();
            _toastTimer.Start();
            FitToContent();
        }

        private void HideToast()
        {
            if (_toastRow == null || !_toastRow.Visible) { return; }
            _toastRow.Visible = false;
            if (_root != null) { _root.RowStyles[1].Height = 0f; }
            FitToContent();
        }

        /// <summary>把一段时长写成人话：90 秒以内报秒，再长就报分。</summary>
        private static string ElapsedText(TimeSpan span)        {
            double seconds = span.TotalSeconds;
            if (seconds < 0) { seconds = 0; }
            if (seconds < 90) { return seconds.ToString("0.0", CultureInfo.InvariantCulture) + " 秒"; }
            return Math.Floor(span.TotalMinutes).ToString("0", CultureInfo.InvariantCulture)
                + " 分 " + span.Seconds.ToString("0", CultureInfo.InvariantCulture) + " 秒";
        }

        private void RefreshLogBox()
        {
            string[] lines = Logger.TailNewest(200);
            _logBox.Text = lines.Length == 0
                ? "（还没有运行记录 —— 点一个按钮试试；灰色的按钮表示功能还没接入）"
                : string.Join(Environment.NewLine, lines);
            _logBox.SelectionStart = 0;
            _logBox.SelectionLength = 0;
            _logBox.ScrollToCaret();
        }

        private void ShowSearch(bool show)
        {
            if (_searchRow.Visible == show) { return; }
            _searchRow.Visible = show;
            _root.RowStyles[2].Height = show ? (float)SearchRowHeight : 0f;
            if (show)
            {
                _searchBox.Focus();
                SetStatus("输入关键字 · 搜的是全部 " + Tabs.Ids.Length + " 个页签（Esc 关掉）");
            }
            else
            {
                _searchBox.Text = "";
                _filter = "";
                BuildGrid();
            }
            UpdateStatusBar();
            FitToContent();
        }

        private void ToggleLogPanel()
        {
            bool show = !_logPanel.Visible;
            _settings.ShowLogPanel = show;
            ApplyLogPanelVisibility();
            if (show) { RefreshLogBox(); }
            UpdateStatusBar();
        }

        private void ApplyLogPanelVisibility()
        {
            bool show = _settings.ShowLogPanel;
            _logPanel.Visible = show;
            _root.RowStyles[4].Height = show ? (float)LogPanelHeight : 0f;
            UpdateStatusBar();
            FitToContent();
        }

        // ---------------------------------------------------------------- keyboard

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N)) { NewUserButton(null, null); return true; }
            if (keyData == (Keys.Control | Keys.F)) { ShowSearch(true); return true; }
            if (keyData == (Keys.Control | Keys.L)) { ToggleLogPanel(); return true; }
            if (keyData == (Keys.Control | Keys.Oemcomma)) { OpenSettings(); return true; }
            if (keyData == Keys.F5)
            {
                ReloadTools();
                BuildGrid();
                SetStatus("已刷新按钮列表");
                return true;
            }
            if (keyData == Keys.Escape && _searchRow.Visible) { ShowSearch(false); return true; }
            if (keyData == Keys.F1 && ActiveControl is ToolButton)
            {
                // F1 = 当前选中按钮的「功能说明」（和右键菜单那一项是同一个窗口）
                ShowHelpFor((ToolButton)ActiveControl);
                return true;
            }
            if (keyData == Keys.Enter && ActiveControl is ToolButton)
            {
                RunTool((ToolButton)ActiveControl, false);
                return true;
            }
            if (keyData == Keys.Left) { MoveFocus(-1, 0); return true; }
            if (keyData == Keys.Right) { MoveFocus(1, 0); return true; }
            if (keyData == Keys.Up) { MoveFocus(0, -1); return true; }
            if (keyData == Keys.Down) { MoveFocus(0, 1); return true; }
            if (keyData == Keys.Apps || keyData == (Keys.Shift | Keys.F10))
            {
                ToolButton b = ActiveControl as ToolButton;
                if (b != null) { _menu.Show(b, new Point(6, 6)); return true; }
            }
            Keys digit = keyData & Keys.KeyCode;
            if ((keyData & Keys.Alt) == Keys.Alt && digit >= Keys.D1 && digit <= Keys.D9)
            {
                int index = digit - Keys.D1;
                if (index < _gridButtons.Count) { RunTool(_gridButtons[index], false); }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void MoveFocus(int dx, int dy)
        {
            if (_gridButtons.Count == 0) { return; }
            ToolButton current = ActiveControl as ToolButton;
            int index = (current == null) ? -1 : _gridButtons.IndexOf(current);
            if (index < 0) { _gridButtons[0].Focus(); return; }
            int target = index + dx + dy * Columns;
            if (target < 0 || target >= _gridButtons.Count) { return; }
            _gridButtons[target].Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; }
            catch { }
            // 记住窗口在哪、多大、停在哪个页签（下次打开就回到原样）
            try
            {
                _settings.LastTab = _currentTab;
                RememberGeometry();
                _settings.Save();
            }
            catch { }
            base.OnFormClosed(e);
        }
    }
}
