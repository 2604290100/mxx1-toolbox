// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「确认执行」窗口。
    ///
    /// 原来用的是 MessageBox，正文是「确定要执行「X」吗？」+ Launcher.DescribeCommand —— 对脚本类
    /// 按钮那就是把 700 多个字符的 PowerShell 正文摊在一个不能选、不能滚的框里（用户 2026-10-04
    /// 的反馈："点击按钮后弹出的确认执行的提示没做好"）。现在：先说这个按钮是干什么的（按钮自己
    /// 那句 hint），再说这次会以什么身份跑、会不会改系统、能不能还原，最后才是命令本身，而且命令
    /// 只在短到能读的时候才给（长脚本让用户走右键菜单的「查看按钮定义」）。
    ///
    /// 默认按钮是「取消」：Enter 和 Esc 都等于取消，要点「执行」必须真的去点它。</summary>
    internal sealed class ConfirmForm : Form
    {
        /// <summary>命令超过这个长度就不在这里给全文了（脚本正文属于「查看按钮定义」）。</summary>
        private const int CommandLimit = 240;

        public ConfirmForm(ToolItem t, string command, bool asAdmin, bool asElevatedChild, Theme theme)
        {
            Text = "请确认";
            ClientSize = new Size(460, 260);
            MinimumSize = new Size(420, 220);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.Padding = new Padding(14, 12, 14, 12);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Label title = new Label();
            title.AutoSize = true;
            title.MaximumSize = new Size(420, 0);
            title.Font = new Font("Microsoft YaHei", 10f, FontStyle.Bold, GraphicsUnit.Point);
            title.Text = "要执行「" + t.Name + "」";
            title.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(title);

            // ① 这个按钮干什么（按重要性排序：先把人话说完，再谈风险，最后才是命令）
            Label what = new Label();
            what.AutoSize = true;
            what.MaximumSize = new Size(420, 0);
            what.Text = (t.Hint.Length > 0) ? t.Hint : "（这个按钮没有写说明）";
            what.Margin = new Padding(2, 0, 2, 10);
            root.Controls.Add(what);

            // ② 风险 / 权限 / 能不能反悔
            string marks = "";
            if (t.Danger) { marks += "· 它会改动系统设置，而且是这一类里后果比较重的一个。" + Environment.NewLine; }
            else if (t.Confirm) { marks += "· 它会改动系统设置。" + Environment.NewLine; }
            bool registryPair = (t.Kind == "builtin")
                && (t.Module == Launcher.ModulePrivacy || t.Module == Launcher.ModuleSysreg);
            if (registryPair)
            {
                marks += "· 改之前会把原值记下来，之后可以一键还原。" + Environment.NewLine;
            }
            if (asAdmin && !asElevatedChild)
            {
                marks += "· 需要管理员权限：接下来会弹一个 UAC 窗口，请点「是」。" + Environment.NewLine;
            }
            else if (asElevatedChild)
            {
                marks += "· 会以管理员身份运行（已经提权）。" + Environment.NewLine;
            }
            if (marks.Length > 0)
            {
                Label warn = new Label();
                warn.AutoSize = true;
                warn.MaximumSize = new Size(420, 0);
                warn.Text = marks.TrimEnd();
                warn.Margin = new Padding(2, 0, 2, 10);
                root.Controls.Add(warn);
            }

            // ③ 命令本身：短就给，长了就指路
            if (command.Length > 0 && command.Length <= CommandLimit)
            {
                Label cmdLabel = new Label();
                cmdLabel.AutoSize = true;
                cmdLabel.Text = "要执行的东西：";
                cmdLabel.Margin = new Padding(2, 0, 2, 2);
                root.Controls.Add(cmdLabel);

                TextBox cmd = new TextBox();
                cmd.ReadOnly = true;
                cmd.Multiline = true;
                cmd.WordWrap = true;
                cmd.ScrollBars = ScrollBars.Vertical;
                cmd.Dock = DockStyle.Fill;
                cmd.Height = 56;
                cmd.Text = command;
                cmd.Margin = new Padding(2, 0, 2, 10);
                root.Controls.Add(cmd);
            }
            else if (command.Length > CommandLimit)
            {
                Label more = new Label();
                more.AutoSize = true;
                more.MaximumSize = new Size(420, 0);
                more.Text = "这个按钮是一段比较长的脚本（" + command.Length + " 个字符），就不摊在这里了："
                    + "在按钮上点右键 →「查看按钮定义」可以看全文。";
                more.Margin = new Padding(2, 0, 2, 10);
                root.Controls.Add(more);
            }

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 3;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button cancel = MakeButton("取消");
            cancel.DialogResult = DialogResult.Cancel;
            Button ok = MakeButton("执行");
            ok.DialogResult = DialogResult.OK;
            buttons.Controls.Add(new Label());
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            root.Controls.Add(buttons);

            Controls.Add(root);
            CancelButton = cancel;
            AcceptButton = cancel;   // Enter = 取消：危险动作要真的去点「执行」

            BackColor = theme.FormBack;
            root.BackColor = theme.FormBack;
            foreach (Control c in root.Controls)
            {
                c.ForeColor = theme.ButtonText;
                if (c is Label || c is TextBox) { c.BackColor = (c is TextBox) ? theme.InputBack : Color.Transparent; }
                if (c is Label) { c.ForeColor = theme.BarText; }
                if (c is TextBox) { c.ForeColor = theme.InputText; }
            }
            title.ForeColor = theme.TabActiveText;
            foreach (Button b in new Button[] { cancel, ok })
            {
                b.BackColor = theme.ButtonBack;
                b.ForeColor = theme.ButtonText;
                b.FlatAppearance.BorderColor = theme.ButtonBorder;
                b.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                b.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
            }
            ok.ForeColor = t.Danger ? theme.Danger : theme.ButtonText;
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
            b.Margin = new Padding(6, 0, 0, 0);
            return b;
        }
    }
}
