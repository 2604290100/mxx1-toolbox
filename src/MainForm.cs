// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>The button wall: tabs on top, a four column grid of compact buttons in the
    /// middle, a collapsible run log and a thin status bar at the bottom.
    /// Every control is placed by a TableLayoutPanel -- no coordinates are written by hand.</summary>
    internal sealed class MainForm : Form
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
        private const int Columns = 4;
        private const int LogPanelHeight = 170;

        private readonly Settings _settings;
        private Theme _theme;
        private List<ToolItem> _tools = new List<ToolItem>();
        private readonly List<string> _warnings = new List<string>();
        private readonly ToolTip _tips = new ToolTip();
        private string _currentTab = Tabs.Common;
        private string _filter = "";
        private string _statusText = "就绪";
        private int _running;

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
        private TableLayoutPanel _statusBar;
        private Label _statusLabel;
        private Button _btnSearch;
        private Button _btnLog;
        private Button _btnSettings;
        private Button _btnUpdate;

        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miRun;
        private ToolStripMenuItem _miRunAdmin;
        private ToolStripMenuItem _miReveal;
        private ToolStripMenuItem _miCopy;
        private ToolStripMenuItem _miDefine;
        private ToolButton _menuTarget;

        public MainForm()
        {
            _settings = Settings.Load();
            _theme = Theme.Resolve(_settings.Theme);

            Text = AboutForm.ProductTitle + " v" + AboutForm.VersionText;
            // The real size is set once the button names are known (see ComputeCellWidth).
            MinimumSize = new Size(460, 520);
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;
            KeyPreview = true;

            ReloadTools();
            _cellWidth = ComputeCellWidth();
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
            ClientSize = new Size(Math.Max(500, Columns * _cellWidth + 24), 700);
            BuildUi();
            BuildGrid();
            ApplyTheme();
            ApplyLogPanelVisibility();
            RefreshLogBox();
            UpdateStatusBar();

            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged; }
            catch { }
        }

        // ---------------------------------------------------------------- construction

        private void BuildUi()
        {
            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 5;
            _root.Margin = new Padding(0);
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, TabBarHeight));   // tabs
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));             // search row
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));            // button grid
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));             // log panel
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, _statusBarHeight));// status bar

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
            _root.Controls.Add(_searchRow, 0, 1);

            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.Margin = new Padding(0);
            _content.AutoScroll = true;
            _root.Controls.Add(_content, 0, 2);

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
            _root.Controls.Add(_logPanel, 0, 3);

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
            right.ColumnCount = 4;
            for (int i = 0; i < 4; i++) { right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _btnSearch = MakeBarButton("搜索", "搜索按钮（Ctrl+F）");
            _btnSearch.Click += delegate { ShowSearch(!_searchRow.Visible); };
            _btnLog = MakeBarButton("日志", "展开或收起运行日志（Ctrl+L）");
            _btnLog.Click += delegate { ToggleLogPanel(); };
            _btnSettings = MakeBarButton("设置", "主题、启动方式、永久删除安装器路径");
            _btnSettings.Click += delegate { OpenSettings(); };
            _btnUpdate = MakeBarButton("检查更新", "工具自身的更新检查（P2 接入）");
            _btnUpdate.Click += delegate { ShowUpdateNotice(); };
            right.Controls.Add(_btnSearch);
            right.Controls.Add(_btnLog);
            right.Controls.Add(_btnSettings);
            right.Controls.Add(_btnUpdate);

            _statusBar.Controls.Add(_statusLabel, 0, 0);
            _statusBar.Controls.Add(right, 1, 0);
            _root.Controls.Add(_statusBar, 0, 4);

            Controls.Add(_root);
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

            _menu.Items.Add(_miRun);
            _menu.Items.Add(_miRunAdmin);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miReveal);
            _menu.Items.Add(_miCopy);
            _menu.Items.Add(_miDefine);
            _menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem later = new ToolStripMenuItem("编辑 / 固定到常用 / 隐藏（P1 接入）");
            later.Enabled = false;
            _menu.Items.Add(later);
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

        private void BuildGrid()
        {
            if (_grid != null)
            {
                _content.Controls.Remove(_grid);
                _grid.Dispose();
                _grid = null;
            }
            _gridButtons.Clear();

            _grid = new TableLayoutPanel();
            _grid.Dock = DockStyle.Top;
            _grid.AutoSize = true;
            _grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
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

            List<ToolItem> visible = new List<ToolItem>();
            foreach (ToolItem t in _tools)
            {
                if (t.Hidden) { continue; }
                if (!string.Equals(t.Tab, _currentTab, StringComparison.OrdinalIgnoreCase)) { continue; }
                if (_filter.Length > 0 && !Matches(t, _filter)) { continue; }
                visible.Add(t);
            }

            int segment = int.MinValue;
            int row = -1;
            int col = Columns;
            foreach (ToolItem t in visible)
            {
                if (t.Segment != segment)
                {
                    if (segment != int.MinValue) { AddSeparatorRow(ref row); col = Columns; }
                    segment = t.Segment;
                    col = Columns;
                }
                if (col >= Columns)
                {
                    row++;
                    _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, ToolRowHeight));
                    _grid.RowCount = row + 1;
                    col = 0;
                }
                ToolButton b = new ToolButton(t);
                b.Dock = DockStyle.Fill;
                b.Margin = new Padding(3);
                b.Click += OnToolClick;
                b.MouseDoubleClick += OnToolDoubleClick;
                b.ContextMenuStrip = _menu;
                b.Enter += delegate { UpdateStatusBar(); };
                _grid.Controls.Add(b, col, row);
                _gridButtons.Add(b);
                col++;
            }

            if (visible.Count == 0)
            {
                row++;
                _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, ToolRowHeight));
                _grid.RowCount = row + 1;
                Label empty = new Label();
                empty.AutoSize = true;
                empty.Margin = new Padding(6, 6, 6, 6);
                empty.Text = _filter.Length > 0
                    ? "这个页签里没有匹配「" + _filter + "」的按钮。"
                    : "这个页签还没有按钮。把 exe / 脚本拖进来，或编辑 " + AppPaths.UserToolsJson;
                _grid.Controls.Add(empty, 0, row);
                _grid.SetColumnSpan(empty, Columns);
            }

            _content.Controls.Add(_grid);
            foreach (ToolButton b in _gridButtons) { b.ApplyTheme(_theme); }
            ColorSeparators();
        }

        private void AddSeparatorRow(ref int row)
        {
            row++;
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, SeparatorRowHeight));
            _grid.RowCount = row + 1;
            Panel sep = new Panel();
            sep.Dock = DockStyle.Fill;
            sep.Margin = new Padding(3, 6, 3, 6);
            sep.BackColor = _theme.SegmentLine;
            _grid.Controls.Add(sep, 0, row);
            _grid.SetColumnSpan(sep, Columns);
        }

        private void ColorSeparators()
        {
            if (_grid == null) { return; }
            foreach (Control c in _grid.Controls)
            {
                Panel p = c as Panel;
                if (p != null) { p.BackColor = _theme.SegmentLine; }
            }
        }

        private static bool Matches(ToolItem t, string needle)
        {
            string n = needle.ToLowerInvariant();
            return t.Name.ToLowerInvariant().IndexOf(n, StringComparison.Ordinal) >= 0
                || t.Id.ToLowerInvariant().IndexOf(n, StringComparison.Ordinal) >= 0
                || t.Hint.ToLowerInvariant().IndexOf(n, StringComparison.Ordinal) >= 0
                || Tabs.Display(t.Tab).ToLowerInvariant().IndexOf(n, StringComparison.Ordinal) >= 0;
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

            foreach (Button b in new Button[] { _btnSearch, _btnLog, _btnSettings, _btnUpdate })
            {
                StyleFlat(b);
            }
            UpdateTabColors();
            foreach (ToolButton b in _gridButtons) { b.ApplyTheme(_theme); }
            ColorSeparators();

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
        }

        private void RunTool(ToolButton b, bool forceAdmin)
        {
            if (b == null || b.Busy) { return; }
            ToolItem t = b.Tool;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            bool asAdmin = forceAdmin || shift || t.RunAsAdmin;

            if (_settings.ConfirmDangerous && (t.Danger || t.Confirm))
            {
                string message = "确定要执行「" + t.Name + "」吗？" + Environment.NewLine + Environment.NewLine
                    + (t.Danger ? "它会改动系统设置。" + Environment.NewLine : "")
                    + Launcher.DescribeCommand(t, _settings, asAdmin);
                DialogResult answer = MessageBox.Show(this, message, "确认执行",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.OK)
                {
                    SetStatus(t.Name + " · 已取消");
                    Logger.Write(t.Name, "已取消（用户在确认框里选了取消）");
                    return;
                }
            }

            if (t.Placeholder)
            {
                string hint = t.Hint.Length > 0 ? t.Hint : "P1";
                SetStatus(t.Name + " · 功能待接入（" + hint + "）");
                Logger.Write(t.Name, "功能待接入（" + hint + "）");
                b.SetBusy(true, 600);
                return;
            }

            if (t.Kind == "builtin" && t.Module == Launcher.ModuleApp)
            {
                HandleUiAction(t);
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
            SetStatus(t.Name + " · 正在运行…");
            UpdateStatusBar();
            Logger.Write(t.Name, "开始：" + Launcher.DescribeCommand(t, _settings, asAdmin));

            ToolButton button = b;
            ToolItem item = t;
            ThreadPool.QueueUserWorkItem(delegate
            {
                LaunchResult result;
                try { result = Launcher.Run(item, _settings, asAdmin); }
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

            Logger.Write(t.Name, (r.Ok ? "完成" : "失败") + " · " + r.Message);
            SetStatus(t.Name + " · " + (r.Ok ? "完成" : "失败")
                + (r.ExitCode != 0 ? "（退出码 " + r.ExitCode + "）" : ""));
            RefreshLogBox();
            UpdateStatusBar();

            if (r.Output != null && r.Output.Trim().Length > 0)
            {
                OutputForm f = new OutputForm(t.Name, t.Name + "　——　" + r.Message, r.Output, _theme);
                f.Show(this);
            }
            else if (!r.Ok)
            {
                MessageBox.Show(this, r.Message, t.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void HandleUiAction(ToolItem t)
        {
            switch (t.Action)
            {
                case "about":
                    {
                        AboutForm f = new AboutForm(_theme);
                        f.ShowDialog(this);
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
                case "newtool":
                    SetStatus("新建按钮 · P1 接入");
                    Logger.Write(t.Name, "新建按钮还没接入（P1）");
                    MessageBox.Show(this,
                        "现在加按钮有两个办法：" + Environment.NewLine + Environment.NewLine
                        + "1）把 exe / 脚本 / 网址快捷方式拖进窗口（P1 接入）" + Environment.NewLine
                        + "2）直接编辑 " + AppPaths.UserToolsJson + Environment.NewLine + Environment.NewLine
                        + "P1 会补上图形化的「新建按钮」窗口。",
                        t.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                default:
                    SetStatus("未实现的界面动作：" + t.Action);
                    Logger.Write(t.Name, "未实现的界面动作：" + t.Action);
                    break;
            }
        }

        private void ShowUpdateNotice()
        {
            SetStatus("检查更新 · P2 接入（当前 v" + AboutForm.VersionText + "）");
            Logger.Write("检查更新", "工具箱自身的更新检查还没接入（P2）");
            MessageBox.Show(this,
                "工具箱自身的更新检查将在 P2 接入。" + Environment.NewLine + Environment.NewLine
                + "当前版本 v" + AboutForm.VersionText + "。" + Environment.NewLine
                + "「永久删除」的更新检查在「右键增强」页签里（调它自己的 checkupdate）。",
                "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(_settings, _theme))
            {
                DialogResult r = f.ShowDialog(this);
                if (r == DialogResult.OK)
                {
                    ApplyTheme();
                    ApplyLogPanelVisibility();
                    SetStatus("设置已保存");
                    Logger.Write("设置", "主题=" + _settings.Theme + " 启动方式=" + _settings.ClickMode
                        + " 二次确认=" + (_settings.ConfirmDangerous ? "开" : "关"));
                }
            }
        }

        private void OpenEngineLog()
        {
            LogForm f = new LogForm("引擎日志（永久删除）", AppPaths.PermdelEngineLog,
                "最新的在最上面 —— " + AppPaths.PermdelEngineLog, _theme, true);
            f.Show(this);
        }

        private void OpenToolboxLog()
        {
            LogForm f = new LogForm("运行日志（工具箱）", Logger.CurrentFile(),
                "最新的在最上面 —— " + Logger.CurrentFile(), _theme, true);
            f.Show(this);
        }

        // ---------------------------------------------------------------- helper bits

        private string RevealPath(ToolItem t)
        {
            if (t.Kind == "exe" || t.Kind == "script")
            {
                string p = AppPaths.Expand(t.Path);
                return File.Exists(p) ? p : "";
            }
            if (t.Kind == "builtin" && t.Module == Launcher.ModulePermdel)
            {
                return Launcher.FindPermanentDeleteExe(_settings);
            }
            if (t.Kind == "open")
            {
                string p = AppPaths.Expand(t.Target);
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
            if (t.Kind == "exe" || t.Kind == "script") { sb.AppendLine("路径        : " + AppPaths.Expand(t.Path)); }
            if (t.Kind == "exe") { sb.AppendLine("参数        : " + t.Args); }
            if (t.Kind == "open") { sb.AppendLine("目标        : " + AppPaths.Expand(t.Target)); }
            sb.AppendLine("启动命令    : " + Launcher.DescribeCommand(t, _settings, false));
            sb.AppendLine("需要管理员  : " + (t.RunAsAdmin ? "是" : "否"));
            sb.AppendLine("危险按钮    : " + (t.Danger ? "是" : "否"));
            sb.AppendLine("待接入      : " + (t.Placeholder ? "是（" + (t.Hint.Length > 0 ? t.Hint : "P1") + "）" : "否"));
            sb.AppendLine("图标        : " + t.IconPath + (File.Exists(t.IconPath) ? "（已找到）" : "（没有 PNG，界面用画的占位图标）"));
            OutputForm f = new OutputForm(t.Name + " · 按钮定义", "按钮定义（只读）", sb.ToString(), _theme);
            f.Show(this);
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
            foreach (string w in _warnings) { Logger.Write("按钮定义", w); }
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
            foreach (ToolItem t in _tools)
            {
                if (t.Hidden) { continue; }
                total++;
                if (string.Equals(t.Tab, _currentTab, StringComparison.OrdinalIgnoreCase)) { here++; }
            }
            // Kept short on purpose: the label is a fixed width and a long line (the old format
            // plus a full "完成：xxx 退出码 0" message) overflowed it, so the tail was cut off.
            // The complete line is available as a tooltip and in the run log.
            string text = total + " 个按钮 · 本页 " + here;
            if (_warnings.Count > 0) { text += " · 定义有 " + _warnings.Count + " 处问题"; }
            if (_running > 0) { text += " · 运行中 " + _running; }
            text += " · " + _statusText;
            _statusLabel.Text = text;
            _tips.SetToolTip(_statusLabel, text);
            _btnLog.Text = _logPanel.Visible ? "收起日志" : "日志";
        }

        private void RefreshLogBox()
        {
            string[] lines = Logger.TailNewest(200);
            _logBox.Text = lines.Length == 0
                ? "（还没有运行记录 —— 点一个按钮试试）"
                : string.Join(Environment.NewLine, lines);
            _logBox.SelectionStart = 0;
            _logBox.SelectionLength = 0;
            _logBox.ScrollToCaret();
        }

        private void ShowSearch(bool show)
        {
            if (_searchRow.Visible == show) { return; }
            _searchRow.Visible = show;
            _root.RowStyles[1].Height = show ? 28f : 0f;
            if (show) { _searchBox.Focus(); }
            else
            {
                _searchBox.Text = "";
                _filter = "";
                BuildGrid();
            }
            UpdateStatusBar();
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
            _root.RowStyles[3].Height = show ? (float)LogPanelHeight : 0f;
            UpdateStatusBar();
        }

        // ---------------------------------------------------------------- keyboard

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
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
            base.OnFormClosed(e);
        }
    }
}
