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
    internal sealed class AboutForm : Form
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

        public AboutForm(Theme theme)
        {
            Text = "关于 " + ProductTitle;
            ClientSize = new Size(470, 300);
            MinimumSize = new Size(430, 280);
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

            Label note = new Label();
            note.AutoSize = true;
            note.MaximumSize = new Size(410, 0);
            note.Margin = new Padding(2, 10, 2, 8);
            note.Text = "主界面是多行多列的按钮墙：点一下按钮就启动一个已经做好的程序或功能。"
                + "「右键增强」页签里的按钮调用隔壁的「永久删除（不进回收站）」安装器，工具箱本身不改动它。"
                + "按钮全部由 tools\\*.json 定义，加按钮不需要重新编译。";
            _root.Controls.Add(note);
            _root.SetColumnSpan(note, 2);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 3;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Button folder = MakeButton("打开设置目录");
            folder.Click += delegate { OpenSettingsFolder(); };
            Button close = MakeButton("关闭");
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label());
            buttons.Controls.Add(folder);
            buttons.Controls.Add(close);
            _root.Controls.Add(buttons);
            _root.SetColumnSpan(buttons, 2);

            Controls.Add(_root);
            AcceptButton = close;
            CancelButton = close;
            ApplyTheme(theme);
        }

        private void AddRow(string label, string value)
        {
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
            try
            {
                AppPaths.EnsureBase();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", "\"" + AppPaths.BaseDir + "\"") { UseShellExecute = true });
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
