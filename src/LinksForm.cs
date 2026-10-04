// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「常用链接」：项目主页 / 仓库 / 几个 Windows 设置页的入口。
    ///
    /// The toolbox itself never goes online: every row is only handed to Windows when the user
    /// clicks 打开 (ShellExecute), so a web row opens the default browser and a ms-settings: row
    /// opens Settings. Author and repository strings come from AboutForm, the single source.</summary>
    internal sealed class LinksForm : Form
    {
        private sealed class Link
        {
            public string Name = "";
            public string Target = "";
            public string Kind = "";   // 网页 / 系统设置
        }

        private static readonly Link[] Links = new Link[]
        {
            Make("项目主页", AboutForm.AuthorUrl, "网页"),
            Make("工具箱仓库", AboutForm.RepoUrl, "网页"),
            Make("永久删除工具", "https://github.com/2604290100/permanent-delete-menu", "网页"),
            Make("Windows 更新", "ms-settings:windowsupdate", "系统设置"),
            Make("应用和功能", "ms-settings:appsfeatures", "系统设置"),
            Make("关于本机", "ms-settings:about", "系统设置")
        };

        private static Link Make(string name, string target, string kind)
        {
            Link l = new Link();
            l.Name = name; l.Target = target; l.Kind = kind;
            return l;
        }

        private readonly Theme _theme;
        private readonly TableLayoutPanel _root;
        private readonly ToolTip _tips = new ToolTip();

        public LinksForm(Theme theme)
        {
            _theme = theme;
            Text = "常用链接";
            ClientSize = new Size(560, 296);
            MinimumSize = new Size(480, 260);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 3;
            _root.Padding = new Padding(14, 12, 14, 12);
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            // One explicit row style per row, in the order the rows are filled below; the last
            // row (the buttons) gets the leftover height so the close button never gets squeezed.
            for (int i = 0; i < Links.Length; i++)
            {
                _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            }
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            foreach (Link l in Links)
            {
                Label name = new Label();
                name.AutoSize = true;
                name.Text = l.Name;
                name.Margin = new Padding(2, 7, 8, 3);
                _root.Controls.Add(name);

                Label target = new Label();
                target.AutoSize = true;
                target.Text = l.Target;
                target.Margin = new Padding(2, 7, 8, 3);
                target.Font = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
                _tips.SetToolTip(target, l.Kind + " · " + l.Target);
                _root.Controls.Add(target);

                Button open = MakeButton("打开");
                open.Tag = l.Target;
                open.Click += delegate(object sender, EventArgs e)
                {
                    Button b = sender as Button;
                    if (b != null) { OpenLink(Convert.ToString(b.Tag)); }
                };
                _root.Controls.Add(open);
            }

            Label note = new Label();
            note.AutoSize = true;
            note.MaximumSize = new Size(500, 0);
            note.Margin = new Padding(2, 10, 2, 6);
            note.Text = "工具箱本身不联网：网页链接交给默认浏览器打开，「系统设置」链接交给 Windows 设置。";
            _root.Controls.Add(note);
            _root.SetColumnSpan(note, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 2;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button close = MakeButton("关闭");
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label());
            buttons.Controls.Add(close);
            _root.Controls.Add(buttons);
            _root.SetColumnSpan(buttons, 3);

            Controls.Add(_root);
            AcceptButton = close;
            CancelButton = close;
            ApplyTheme(theme);
        }

        private Button MakeButton(string text)
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

        private void OpenLink(string target)
        {
            if (string.IsNullOrEmpty(target)) { return; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(target);
                psi.UseShellExecute = true;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                Logger.Write("常用链接", "打开 " + target);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打不开这个链接：" + ex.Message, "常用链接",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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
