// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>Shows captured command output (status / verify / disclaimer / engine log).
    /// The text keeps the order the command produced it in.</summary>
    internal sealed class OutputForm : Form
    {
        private readonly Theme _theme;
        private readonly TableLayoutPanel _root;
        private readonly TextBox _box;
        private readonly Label _head;
        private readonly TableLayoutPanel _buttons;

        public OutputForm(string title, string headline, string body, Theme theme)
        {
            _theme = theme;
            Text = title;
            ClientSize = new Size(620, 460);
            MinimumSize = new Size(420, 300);
            // 位置不在这里定：StartPosition=CenterParent 只对 ShowDialog 打开的模态窗口有效，
            // 这三个窗口都是用 Show() 开的非模态窗口 —— 位置交给 WindowPlacement.ShowCentered（那里有说明）。
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.Padding = new Padding(10, 8, 10, 8);
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _head = new Label();
            _head.AutoSize = true;
            _head.Text = headline;
            _head.Margin = new Padding(2, 2, 2, 6);
            _root.Controls.Add(_head, 0, 0);

            _box = new TextBox();
            _box.Multiline = true;
            _box.ReadOnly = true;
            _box.ScrollBars = ScrollBars.Both;
            _box.WordWrap = false;
            _box.Dock = DockStyle.Fill;
            _box.Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _box.Text = body;
            _root.Controls.Add(_box, 0, 1);

            _buttons = new TableLayoutPanel();
            _buttons.Dock = DockStyle.Fill;
            _buttons.AutoSize = true;
            _buttons.ColumnCount = 4;
            _buttons.RowCount = 1;
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button copy = MakeButton("复制全文");
            copy.Click += delegate { CopyAll(); };
            Button open = MakeButton("用记事本打开");
            open.Click += delegate { OpenInNotepad(); };
            Button close = MakeButton("关闭");
            close.Click += delegate { Close(); };
            _buttons.Controls.Add(new Label(), 0, 0);
            _buttons.Controls.Add(open, 1, 0);
            _buttons.Controls.Add(copy, 2, 0);
            _buttons.Controls.Add(close, 3, 0);
            _root.Controls.Add(_buttons, 0, 2);

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
            b.Margin = new Padding(4, 6, 0, 0);
            return b;
        }

        public void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            _root.BackColor = theme.FormBack;
            _head.BackColor = theme.FormBack;
            _head.ForeColor = theme.BarText;
            _box.BackColor = theme.LogBack;
            _box.ForeColor = theme.LogText;
            foreach (Control c in _buttons.Controls)
            {
                Button b = c as Button;
                if (b == null) { continue; }
                b.BackColor = theme.ButtonBack;
                b.ForeColor = theme.ButtonText;
                b.FlatAppearance.BorderColor = theme.ButtonBorder;
                b.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                b.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
            }
        }

        private void CopyAll()
        {
            try { Clipboard.SetText(_box.Text); }
            catch { }
        }

        private const string NotepadTempFileName = "mxx1-toolbox-output.txt";

        private void OpenInNotepad()
        {
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), NotepadTempFileName);
                System.IO.File.WriteAllText(path, _box.Text, new System.Text.UTF8Encoding(true));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
