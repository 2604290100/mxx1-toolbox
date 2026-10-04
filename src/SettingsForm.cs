// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>Settings window. Laid out row by row with TableLayoutPanel -- no manual
    /// coordinates, so nothing overlaps at other DPI settings or font sizes.</summary>
    internal sealed class SettingsForm : Mxx1Form
    {
        private readonly Settings _settings;
        private readonly TableLayoutPanel _root;

        private readonly ComboBox _themeBox;
        private readonly ComboBox _clickBox;
        private readonly CheckBox _confirmBox;
        private readonly CheckBox _hideConsoleBox;
        private readonly CheckBox _logPanelBox;
        private readonly NumericUpDown _keepDays;
        private readonly TextBox _permdelBox;
        private readonly CheckBox _autoSizeBox;
        private readonly CheckBox _rememberTabBox;

        public SettingsForm(Settings settings, Theme theme)
        {
            _settings = settings;

            Text = "设置";
            ClientSize = new Size(580, 430);
            MinimumSize = new Size(500, 380);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.Padding = new Padding(14, 12, 14, 12);
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _themeBox = new ComboBox();
            _themeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _themeBox.Dock = DockStyle.Fill;
            _themeBox.Items.AddRange(new object[] { "浅色", "深色", "跟随系统" });
            _themeBox.SelectedIndex = ThemeIndex(_settings.Theme);
            AddRow("主题", _themeBox);

            _clickBox = new ComboBox();
            _clickBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _clickBox.Dock = DockStyle.Fill;
            _clickBox.Items.AddRange(new object[] { "单击启动", "双击启动" });
            _clickBox.SelectedIndex = (_settings.ClickMode == "double") ? 1 : 0;
            AddRow("启动方式", _clickBox);

            _confirmBox = new CheckBox();
            _confirmBox.Text = "危险按钮执行前二次确认";
            _confirmBox.AutoSize = true;
            _confirmBox.Checked = _settings.ConfirmDangerous;
            AddRow("安全", _confirmBox);

            _hideConsoleBox = new CheckBox();
            _hideConsoleBox.Text = "隐藏子程序的黑窗口";
            _hideConsoleBox.AutoSize = true;
            _hideConsoleBox.Checked = _settings.HideConsole;
            AddRow("", _hideConsoleBox);

            _logPanelBox = new CheckBox();
            _logPanelBox.Text = "启动时展开运行日志面板";
            _logPanelBox.AutoSize = true;
            _logPanelBox.Checked = _settings.ShowLogPanel;
            AddRow("", _logPanelBox);

            _keepDays = new NumericUpDown();
            _keepDays.Minimum = 0;
            _keepDays.Maximum = 3650;
            _keepDays.Value = Math.Max(0, Math.Min(3650, _settings.LogKeepDays));
            _keepDays.Width = 80;
            AddRow("日志保留天数", _keepDays);

            TableLayoutPanel pathRow = new TableLayoutPanel();
            pathRow.Dock = DockStyle.Fill;
            pathRow.AutoSize = true;
            pathRow.ColumnCount = 3;
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _permdelBox = new TextBox();
            _permdelBox.Dock = DockStyle.Fill;
            _permdelBox.Text = _settings.PermanentDeleteExe;
            Button browse = MakeButton("浏览…");
            browse.Click += delegate { Browse(); };
            Button tools = MakeButton("打开工具目录");
            tools.Click += delegate { OpenToolFolder(); };
            pathRow.Controls.Add(_permdelBox);
            pathRow.Controls.Add(browse);
            pathRow.Controls.Add(tools);
            AddRow("永久删除安装器", pathRow);

            // 窗口：默认高度贴着当前页签的内容（「右键增强」只有 1 个按钮时不再撑一个空窗口）。
            // 用户自己拖过边框之后会自动取消勾选，这里可以把勾重新打上。
            _autoSizeBox = new CheckBox();
            _autoSizeBox.Text = "窗口高度跟随当前页签的内容（不勾 = 固定高度，拖过窗口会记住）";
            _autoSizeBox.AutoSize = true;
            _autoSizeBox.Checked = _settings.WindowAutoSize;
            _autoSizeBox.CheckedChanged += delegate
            {
                if (_autoSizeBox.Checked)
                {
                    _settings.WindowAutoSize = true;
                    _settings.WindowWidth = 0;
                    _settings.WindowHeight = 0;
                }
            };
            AddRow("窗口", _autoSizeBox);

            _rememberTabBox = new CheckBox();
            _rememberTabBox.Text = "记住上次停留的页签和窗口位置";
            _rememberTabBox.AutoSize = true;
            _rememberTabBox.Checked = _settings.LastTab.Length > 0 || _settings.WindowX >= 0;
            AddRow("", _rememberTabBox);

            Button clearRecent = MakeButton("清空最近使用（" + UserTools.LoadRecent().Count + " 个）");
            clearRecent.Click += delegate
            {
                string error = UserTools.ClearRecent();
                if (error.Length > 0)
                {
                    MessageBox.Show(this, "清空失败：" + error, "最近使用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                clearRecent.Text = "清空最近使用（0 个）";
            };
            AddRow("「常用」页", clearRecent);

            Label hint = new Label();
            hint.AutoSize = true;
            hint.MaximumSize = new Size(380, 0);
            hint.Text = "留空 = 自动查找（工具目录 bin-tools、环境变量 MXX1_PERMDEL_EXE、隔壁工程 bin 目录）。"
                + "其他外部工具（exe）也都放进 bin-tools 就行。";
            AddRow("", hint);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 3;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button save = MakeButton("保存");
            save.Click += delegate { Collect(); DialogResult = DialogResult.OK; Close(); };
            Button cancel = MakeButton("取消");
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(new Label());
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            AddRow("", buttons);

            Controls.Add(_root);
            AcceptButton = save;
            CancelButton = cancel;
            ApplyTheme(theme);
        }

        /// <summary>高度按内容定：设置项是一点点加上去的，写死的高度会把下面那排按钮切掉。</summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                int want = _root.PreferredSize.Height + 6;
                int max = Screen.FromControl(this).WorkingArea.Height - 80;
                if (want > max) { want = max; }
                if (want > ClientSize.Height) { ClientSize = new Size(ClientSize.Width, want); }
            }
            catch { }
        }

        /// <summary>One settings row: a label in column 0 and the field in column 1.</summary>
        private void AddRow(string label, Control field)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = label;
            l.Margin = new Padding(2, 6, 10, 6);
            field.Margin = new Padding(2, 4, 2, 4);
            _root.Controls.Add(l);
            _root.Controls.Add(field);
        }

        private static Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(84, 26);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Margin = new Padding(4, 4, 0, 0);
            return b;
        }

        private static int ThemeIndex(string mode)
        {
            if (mode == Settings.ThemeDark) { return 1; }
            if (mode == Settings.ThemeSystem) { return 2; }
            return 0;
        }

        private void Browse()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 PermanentDeleteSetup.exe";
                dlg.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) == DialogResult.OK) { _permdelBox.Text = dlg.FileName; }
            }
        }

        /// <summary>Opens bin-tools next to the exe (creates it first) -- that is where every
        /// external tool goes, so "add a tool" is just dropping the file in.</summary>
        private void OpenToolFolder()
        {
            try
            {
                string dir = AppPaths.EnsurePayloadDir();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        private void Collect()
        {
            _settings.Theme = (_themeBox.SelectedIndex == 1) ? Settings.ThemeDark
                : (_themeBox.SelectedIndex == 2) ? Settings.ThemeSystem : Settings.ThemeLight;
            _settings.ClickMode = (_clickBox.SelectedIndex == 1) ? "double" : "single";
            _settings.ConfirmDangerous = _confirmBox.Checked;
            _settings.HideConsole = _hideConsoleBox.Checked;
            _settings.ShowLogPanel = _logPanelBox.Checked;
            _settings.LogKeepDays = (int)_keepDays.Value;
            _settings.PermanentDeleteExe = _permdelBox.Text.Trim();
            _settings.WindowAutoSize = _autoSizeBox.Checked;
            if (!_rememberTabBox.Checked)
            {
                _settings.LastTab = "";
                _settings.WindowX = -1;
                _settings.WindowY = -1;
            }
            _settings.Save();
        }

        public void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            _root.BackColor = theme.FormBack;
            ApplyTo(_root, theme);
        }

        private static void ApplyTo(Control parent, Theme theme)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Label) { c.ForeColor = theme.BarText; c.BackColor = Color.Transparent; }
                else if (c is CheckBox) { c.ForeColor = theme.ButtonText; c.BackColor = theme.FormBack; }
                else if (c is TextBox || c is ComboBox || c is NumericUpDown)
                {
                    c.BackColor = theme.InputBack;
                    c.ForeColor = theme.InputText;
                }
                else if (c is Button)
                {
                    Button b = (Button)c;
                    b.BackColor = theme.ButtonBack;
                    b.ForeColor = theme.ButtonText;
                    b.FlatAppearance.BorderColor = theme.ButtonBorder;
                    b.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
                }
                if (c.Controls.Count > 0) { ApplyTo(c, theme); }
            }
        }
    }
}
