// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>About window. This class is the ONLY place that defines the author name,
    /// the site, the repository and the licence -- nothing else may hard code them.</summary>
    internal sealed class AboutForm : Mxx1Form
    {
        public const string AuthorName = "mxx1";
        public const string AuthorSite = "mxx1.cn";
        public const string AuthorUrl = "https://mxx1.cn";
        public const string RepoUrl = "https://github.com/2604290100/mxx1-toolbox";
        public const string License = "GPL-3.0-or-later";
        public const string ProductTitle = "萌新工具箱";

        public static string VersionText
        {
            get
            {
                try
                {
                    Version v = Assembly.GetExecutingAssembly().GetName().Version;
                    return v.Major + "." + v.Minor + "." + v.Build;
                }
                catch { return "1.0.0"; }
            }
        }

        private readonly TableLayoutPanel _root;
        private readonly ToolTip _tips = new ToolTip();
        private readonly Theme _theme;
        private Label _updateValue;
        private Button _updateButton;
        private bool _checking;

        public AboutForm(Theme theme)
        {
            _theme = theme;
            Text = "关于 " + ProductTitle;
            // 里面除了署名还有两段说明 + 一节快捷键（用户报过"关于里面高度不够没有显示全面"），
            // 所以高度给足。
            ClientSize = new Size(470, 470);
            MinimumSize = new Size(430, 400);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            // Controls are added in reading order; TableLayoutPanel places them row by row,
            // so no row index is ever written by hand.
            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.Padding = new Padding(14, 12, 14, 12);
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76f));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            AddRow("名称", ProductTitle);
            AddRow("版本", "v" + VersionText);
            AddRow("作者", AuthorName + "　·　" + AuthorSite);
            AddRow("许可证", License);
            AddRow("主页", AuthorUrl);
            AddRow("仓库", RepoUrl);

            // 更新状态行：这是个**动的东西**，不是一个写死的字符串 —— 用户 2026-10-04 要求
            // "补上完整的检测更新功能"。值由 StartCheck / SetUpdateText 更新。
            Label updateTitle = new Label();
            updateTitle.AutoSize = true;
            updateTitle.Text = "更新";
            updateTitle.Margin = new Padding(2, 3, 12, 3);
            _updateValue = new Label();
            _updateValue.AutoSize = true;
            _updateValue.MaximumSize = new Size(330, 0);
            _updateValue.Margin = new Padding(2, 3, 2, 3);
            _updateValue.Text = UpdateCheck.Disabled ? "更新检查已关闭（MXX1_NO_UPDATE=1）" : "尚未检查";
            _root.Controls.Add(updateTitle);
            _root.Controls.Add(_updateValue);

            Label note = new Label();
            note.AutoSize = true;
            note.MaximumSize = new Size(410, 0);
            note.Margin = new Padding(2, 10, 2, 8);
            // 这段原来写着"灰色的按钮……点一下只会写日志，鼠标停上去会说明"——三处都是旧行为：
            // 灰色按钮现在是禁用的（点不动、也收不到鼠标消息所以没有悬停说明，说明挪到底栏）。
            // 一段会骗人的说明比没有说明更糟（2026-10-04 复核界面时发现）。
            note.Text = "主界面是多行多列的按钮墙：点一下按钮就启动一个已经做好的程序或功能。"
                + "灰色的按钮表示功能还没接入：它点不动，鼠标停上去也不会弹说明（禁用控件收不到鼠标消息），"
                + "这一页有几个灰色按钮写在最下面那条状态栏里。"
                + "按钮上点右键可以置顶（置顶的会汇总到「常用」页）、编辑、复制启动命令、查看定义。"
                + "「系统工具」调的是 Windows 自带的组件，只读查看、不改系统；"
                + "「常用设置」和「隐私设置」里写注册表的按钮会先把原值记下来、写完读回核对，随时能一键还原。"
                + "外部工具（exe）请放进本程序旁边的 bin-tools 文件夹，找不到工具时会有提示。"
                + "按钮全部由 tools\\*.json 定义，加按钮不需要重新编译。";
            _root.Controls.Add(note);
            _root.SetColumnSpan(note, 2);

            // 快捷键没有别的入口，全靠猜（用户 2026-10-04 提的"从用户体验看还缺什么"）。
            Label keys = new Label();
            keys.AutoSize = true;
            keys.MaximumSize = new Size(410, 0);
            keys.Margin = new Padding(2, 0, 2, 8);
            keys.Text = "快捷键：" + Environment.NewLine
                + "　Ctrl+F 搜索（搜全部页签的按钮名 / 说明 / id）　Ctrl+N 新建按钮　"
                + "Ctrl+L 日志面板　Ctrl+, 设置" + Environment.NewLine
                + "　Alt+1~9 运行这一页的前 9 个按钮　方向键在按钮之间移动　Enter 运行　"
                + "菜单键 / Shift+F10 右键菜单" + Environment.NewLine
                + "　F5 重新加载按钮清单　Esc 关掉搜索　"
                + "按住 Shift 点按钮 = 这一次以管理员身份运行";
            _root.Controls.Add(keys);
            _root.SetColumnSpan(keys, 2);

            // 按钮分两排：一排是"打开某个目录"这类，一排是"检查更新 / 免责声明 / 关闭"。
            // 挤成一排会把窗口撑宽（6 个按钮 × 94px > 470 可用宽度）。
            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 4;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Button tools = MakeButton("打开工具目录");
            tools.Click += delegate { OpenFolder(AppPaths.EnsurePayloadDir()); };
            _tips.SetToolTip(tools, "外部工具都放在这个文件夹里：" + AppPaths.PayloadDir);
            Button folder = MakeButton("打开设置目录");
            folder.Click += delegate { OpenSettingsFolder(); };
            Button terms = MakeButton("免责声明");
            terms.Click += delegate { OpenDisclaimer(); };
            _tips.SetToolTip(terms, "使用条款 / 免责声明的全文（就是程序里内嵌的那份正本）");
            buttons.Controls.Add(new Label());
            buttons.Controls.Add(tools);
            buttons.Controls.Add(folder);
            buttons.Controls.Add(terms);
            _root.Controls.Add(buttons);
            _root.SetColumnSpan(buttons, 2);

            TableLayoutPanel buttons2 = new TableLayoutPanel();
            buttons2.Dock = DockStyle.Fill;
            buttons2.AutoSize = true;
            buttons2.ColumnCount = 3;
            buttons2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _updateButton = MakeButton("检查更新");
            _updateButton.Click += delegate { StartCheck(true); };
            Button close = MakeButton("关闭");
            close.Click += delegate { Close(); };
            buttons2.Controls.Add(new Label());
            buttons2.Controls.Add(_updateButton);
            buttons2.Controls.Add(close);
            _root.Controls.Add(buttons2);
            _root.SetColumnSpan(buttons2, 2);

            Controls.Add(_root);
            AcceptButton = close;
            CancelButton = close;
            ApplyTheme(theme);
        }

        /// <summary>打开窗口时就查一次（后台线程，不卡界面）；手动点「检查更新」时 manual=true
        /// 会立刻把状态行改成"正在检查…"。</summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartCheck(false);
        }

        private void StartCheck(bool manual)
        {
            if (_checking) { return; }
            if (UpdateCheck.Disabled)
            {
                SetUpdateText("更新检查已关闭（MXX1_NO_UPDATE=1）");
                return;
            }
            UpdateResult cached = UpdateCheck.Last;
            if (!manual && cached.State != UpdateState.Unknown && cached.State != UpdateState.Checking)
            {
                SetUpdateText(cached.UiText);
                return;
            }
            _checking = true;
            SetUpdateText("正在检查…");
            if (_updateButton != null) { _updateButton.Enabled = false; }
            UpdateCheck.CheckAsync(delegate(UpdateResult r)
            {
                try { BeginInvoke((MethodInvoker)delegate { _checking = false; SetUpdateText(r.UiText); }); }
                catch { }
            });
        }

        private void SetUpdateText(string text)
        {
            try
            {
                if (_updateValue != null && !_updateValue.IsDisposed) { _updateValue.Text = text; }
                if (_updateButton != null && !_updateButton.IsDisposed) { _updateButton.Enabled = true; }
            }
            catch { }
        }

        private void OpenDisclaimer()
        {
            using (DisclaimerForm f = new DisclaimerForm(_theme))
            {
                f.ShowDialog(this);
            }
        }

        /// <summary>窗口高度按内容定：里面有两段说明 + 一节快捷键，字号 / DPI 一变写死的高度就
        /// 会把下面那排按钮切掉（用户 2026-10-04 报的"关于里面高度不够没有显示全面"）。</summary>
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

        private void AddRow(string label, string value)        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = label;
            l.Margin = new Padding(2, 3, 12, 3);
            Label v = new Label();
            v.AutoSize = true;
            v.Text = value;
            v.Margin = new Padding(2, 3, 2, 3);
            _root.Controls.Add(l);
            _root.Controls.Add(v);
        }

        private static Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(90, 26);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Margin = new Padding(4, 6, 0, 0);
            return b;
        }

        private void OpenSettingsFolder()
        {
            OpenFolder(AppPaths.BaseDir);
        }

        /// <summary>Opens a folder in Explorer, creating it first (the tool folder may not exist yet).</summary>
        private static void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) { return; }
                if (!System.IO.Directory.Exists(path)) { System.IO.Directory.CreateDirectory(path); }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch { }
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
                if (c is Label)
                {
                    c.ForeColor = theme.BarText;
                    c.BackColor = theme.FormBack;
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
