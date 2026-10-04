// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>
    /// 首次运行的使用条款确认（强制）：勾选同意才能继续用界面。
    ///
    /// 设计取舍（与隔壁 permanent-delete-menu 同一套，用户 2026-10-04 要求"补上免责 / 服务协议"）：
    ///   * **默认不勾选**、且「同意并继续」在勾选前是禁用的 —— 不能靠一路回车蒙过去；
    ///   * 条款正文用的是和「免责声明 / 服务协议」窗口同一份文本（内嵌的 docs/DISCLAIMER.md），
    ///     不存在"看的是一份、同意的是另一份"；
    ///   * 不同意就**直接退出程序**，不写注册表、不做任何改动（不是把按钮灰掉继续挂着）；
    ///   * 只拦界面：命令行属于非交互场景，行为不变（见 docs/DISCLAIMER.md）。
    /// </summary>
    internal sealed class ConsentForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TableLayoutPanel _root;
        private readonly CheckBox _chkAgree;
        private readonly Button _btnAccept;
        private readonly Button _btnDecline;

        public ConsentForm(Theme theme)
        {
            _theme = theme;
            Text = "使用条款确认 · 首次运行";
            ClientSize = new Size(700, 580);
            MinimumSize = new Size(520, 420);
            // 两个都必须 false：Min=true/Max=false 时 Win10 会在标题栏画一个灰掉的最大化方框
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            // 行高由 TableLayoutPanel 按内容排，谁也不压谁（界面硬规则：标签压住按钮会吃掉鼠标点击）
            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 5;
            _root.Padding = new Padding(14, 12, 14, 12);
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 标题
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 说明
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 正文
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 勾选框
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 按钮

            Label title = new Label();
            title.AutoSize = true;
            title.Text = "使用前请阅读并同意下面的条款";
            title.Font = new Font("Microsoft YaHei", 12f, FontStyle.Bold, GraphicsUnit.Point);
            title.Margin = new Padding(2, 0, 2, 6);
            _root.Controls.Add(title, 0, 0);

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(640, 0);
            intro.Margin = new Padding(2, 0, 2, 8);
            intro.Text = "本工具是一面按钮墙：按钮会启动别的程序；「隐私设置」「常用设置」会写 HKCU 下的注册表值"
                + "（写入前记原值，随时能一键还原）；「右键增强」会往你的资源管理器右键菜单里加两项；"
                + "「解除文件占用」在你确认后可以结束你勾选的进程、或者只关掉它们持有这个文件的句柄"
                + "（那个程序可能会报错或存不上盘）。\r\n"
                + "请读完下面的《免责声明与服务条款》并勾选同意，才能使用。";
            _root.Controls.Add(intro, 0, 1);

            TextBox body = new TextBox();
            body.Multiline = true;
            body.ReadOnly = true;
            body.ScrollBars = ScrollBars.Vertical;
            body.WordWrap = true;
            body.Dock = DockStyle.Fill;
            body.Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            body.Text = DisclaimerForm.LoadText().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
            body.SelectionStart = 0;
            body.SelectionLength = 0;
            body.Margin = new Padding(2, 0, 2, 8);
            _root.Controls.Add(body, 0, 2);

            _chkAgree = new CheckBox();
            _chkAgree.AutoSize = true;
            _chkAgree.Text = "我已阅读并同意《免责声明与服务条款》，并知悉本工具会改动上面写明的那些设置";
            _chkAgree.Margin = new Padding(2, 4, 2, 6);
            _chkAgree.CheckedChanged += delegate { _btnAccept.Enabled = _chkAgree.Checked; };
            _root.Controls.Add(_chkAgree, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 3;
            buttons.RowCount = 1;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _btnAccept = new Button();
            _btnAccept.Text = "同意并继续";
            _btnAccept.AutoSize = true;
            _btnAccept.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _btnAccept.MinimumSize = new Size(120, 30);
            _btnAccept.Enabled = false;                 // 勾选后才可用
            _btnAccept.DialogResult = DialogResult.OK;
            _btnAccept.Margin = new Padding(4, 6, 0, 0);

            _btnDecline = new Button();
            _btnDecline.Text = "不同意，退出";
            _btnDecline.AutoSize = true;
            _btnDecline.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _btnDecline.MinimumSize = new Size(120, 30);
            _btnDecline.DialogResult = DialogResult.Abort;
            _btnDecline.Margin = new Padding(8, 6, 0, 0);

            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_btnAccept, 1, 0);
            buttons.Controls.Add(_btnDecline, 2, 0);
            _root.Controls.Add(buttons, 0, 4);

            Controls.Add(_root);
            // 注意：AcceptButton 不能设成 _btnAccept —— 回车会把没勾选也当成同意。
            // 也不设 CancelButton：Esc 走 DialogResult.Cancel，调用方按"不同意"处理。
            AcceptButton = null;
            CancelButton = null;
            ApplyTheme(theme);
        }

        public void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            _root.BackColor = theme.FormBack;
            foreach (Control c in _root.Controls)
            {
                if (c is Label) { c.ForeColor = theme.BarText; c.BackColor = theme.FormBack; }
                if (c is CheckBox)
                {
                    c.ForeColor = theme.BarText;
                    c.BackColor = theme.FormBack;
                }
                TableLayoutPanel p = c as TableLayoutPanel;
                if (p != null)
                {
                    p.BackColor = theme.FormBack;
                    foreach (Control b in p.Controls)
                    {
                        Button btn = b as Button;
                        if (btn == null) { continue; }
                        btn.BackColor = theme.ButtonBack;
                        btn.ForeColor = theme.ButtonText;
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.FlatAppearance.BorderSize = 1;
                        btn.FlatAppearance.BorderColor = theme.ButtonBorder;
                        btn.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                        btn.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
                        btn.UseVisualStyleBackColor = false;
                    }
                }
            }
        }
    }
}
