// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「功能说明」窗口 —— 右键任意按钮 →「功能说明…」打开的那一页。
    ///
    /// 和它旁边那个「查看按钮定义」是**两件事**，别合并：
    /// · 「查看按钮定义」= 给排查问题看的（来源清单、类型、路径、启动命令、图标在哪）；
    /// · 「功能说明」= 给人看的（它是干什么的、怎么用、要注意什么），正文来自清单里的
    ///   `hint`（一句话）+ `about`（详情：使用方法 / 安全保障 / 什么时候适合用）。
    ///
    /// 为什么不用现成的 OutputForm 装这段正文：那个窗口是给**命令输出**用的（Consolas 等宽 +
    /// 不自动换行），中文整段说明会拉出一条横向滚动条，读起来很难受。这里用微软雅黑 + 自动换行。
    ///
    /// 界面硬规则照旧：按钮用 TableLayoutPanel 排（不手写坐标），标签不与按钮重叠，
    /// 窗口两个方框（最小化 / 最大化）都不开 —— 和另外十个窗口一致（只有主窗口有最小化）。</summary>
    internal sealed class HelpForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _body;
        private readonly Label _head;
        private readonly TableLayoutPanel _buttons;

        public HelpForm(ToolItem t, string body, string headline, bool canRun, Theme theme, EventHandler runClick)
        {
            _theme = theme;
            Text = t.Name + " · 功能说明";
            ClientSize = new Size(600, 460);
            MinimumSize = new Size(500, 340);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _head = new Label();
            _head.AutoSize = true;
            _head.Font = new Font("Microsoft YaHei", 10f, FontStyle.Bold, GraphicsUnit.Point);
            _head.Text = headline;
            _head.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(_head, 0, 0);

            _body = new TextBox();
            _body.Multiline = true;
            _body.ReadOnly = true;
            _body.WordWrap = true;
            _body.ScrollBars = ScrollBars.Vertical;
            _body.Dock = DockStyle.Fill;
            _body.Font = new Font("Microsoft YaHei", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            _body.Text = body;
            _body.Margin = new Padding(2, 0, 2, 8);
            _body.Select(0, 0);   // 打开时光标在开头，不要停在末尾一路滚到底
            root.Controls.Add(_body, 0, 1);

            _buttons = new TableLayoutPanel();
            _buttons.Dock = DockStyle.Fill;
            _buttons.AutoSize = true;
            _buttons.ColumnCount = 4;
            _buttons.RowCount = 1;
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.Controls.Add(new Label(), 0, 0);

            if (canRun && runClick != null)
            {
                Button run = MakeButton("运行这个功能");
                run.Click += runClick;
                _buttons.Controls.Add(run, 1, 0);
            }
            Button copy = MakeButton("复制说明");
            copy.Click += delegate { CopyBody(); };
            _buttons.Controls.Add(copy, canRun && runClick != null ? 2 : 1, 0);
            Button close = MakeButton("关闭");
            close.Click += delegate { Close(); };
            _buttons.Controls.Add(close, canRun && runClick != null ? 3 : 2, 0);
            root.Controls.Add(_buttons, 0, 2);

            Controls.Add(root);
            CancelButton = close;
            AcceptButton = close;
            ApplyTheme(theme);
        }

        private static Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(88, 28);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Margin = new Padding(6, 4, 0, 0);
            b.TabStop = false;
            return b;
        }

        public void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            _body.BackColor = theme.LogBack;
            _body.ForeColor = theme.LogText;
            _head.ForeColor = theme.TabActiveText;
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

        private void CopyBody()
        {
            try
            {
                Clipboard.SetText(_body.Text);
                _head.Text = _head.Text.EndsWith("（已复制）", StringComparison.Ordinal)
                    ? _head.Text
                    : _head.Text + "（已复制）";
            }
            catch { }
        }
    }
}
