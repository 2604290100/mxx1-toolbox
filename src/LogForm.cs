// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>In-app log viewer. Lines are shown NEWEST FIRST -- opening the newest entry in
    /// Notepad would mean scrolling to the very bottom, which is exactly what users complained
    /// about in the sibling project.</summary>
    internal sealed class LogForm : Form
    {
        private readonly Theme _theme;
        private readonly TableLayoutPanel _root;
        private readonly TextBox _box;
        private readonly Label _head;
        private readonly TableLayoutPanel _buttons;
        private readonly string _path;
        private readonly bool _newestFirst;

        public LogForm(string title, string path, string headline, Theme theme, bool newestFirst)
        {
            _theme = theme;
            _path = path;
            _newestFirst = newestFirst;

            Text = title;
            ClientSize = new Size(680, 480);
            MinimumSize = new Size(460, 320);
            StartPosition = FormStartPosition.CenterParent;
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
            _root.Controls.Add(_box, 0, 1);

            _buttons = new TableLayoutPanel();
            _buttons.Dock = DockStyle.Fill;
            _buttons.AutoSize = true;
            _buttons.ColumnCount = 5;
            _buttons.RowCount = 1;
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 1; i < 5; i++) { _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }

            Button refresh = MakeButton("刷新");
            refresh.Click += delegate { Reload(); };
            Button notepad = MakeButton("用记事本打开完整日志");
            notepad.Click += delegate { OpenInNotepad(); };
            Button folder = MakeButton("打开日志目录");
            folder.Click += delegate { OpenFolder(); };
            Button close = MakeButton("关闭");
            close.Click += delegate { Close(); };

            _buttons.Controls.Add(new Label(), 0, 0);
            _buttons.Controls.Add(folder, 1, 0);
            _buttons.Controls.Add(notepad, 2, 0);
            _buttons.Controls.Add(refresh, 3, 0);
            _buttons.Controls.Add(close, 4, 0);
            _root.Controls.Add(_buttons, 0, 2);

            Controls.Add(_root);
            CancelButton = close;
            ApplyTheme(theme);
            Reload();
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

        public void Reload()
        {
            string[] lines = _newestFirst
                ? Logger.TailNewestOf(_path, 800)
                : Logger.TailNewestOf(_path, 800);
            if (lines.Length == 0)
            {
                _box.Text = "（还没有内容）" + Environment.NewLine + _path;
            }
            else
            {
                _box.Text = string.Join(Environment.NewLine, lines);
            }
            _box.SelectionStart = 0;
            _box.SelectionLength = 0;
            _box.ScrollToCaret();
            _head.Text = (_newestFirst ? "最新的在最上面 · " : "") + _path;
        }

        private void OpenInNotepad()
        {
            try
            {
                if (File.Exists(_path))
                {
                    Process.Start(new ProcessStartInfo("notepad.exe", "\"" + _path + "\"") { UseShellExecute = true });
                }
            }
            catch { }
        }

        private void OpenFolder()
        {
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                }
            }
            catch { }
        }
    }
}
