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
    /// 默认按钮是「取消」：Enter 和 Esc 都等于取消，要点「执行」必须真的去点它。
    ///
    /// 另一个入口是「一段话 + 一个动作」这种提示（例如更新检查发现新版本：要不要前往更新），
    /// 用的就是第二个构造函数 —— 它不显示命令、也不提权，但仍然守同一条规矩：默认是取消。
    ///
    /// ## 窗口高度跟着正文走（2026-10-10 用户报的：「**它窗口没有自动适应文字高度，按钮完全遮挡了**」）
    ///
    /// 说的是「取得所有权」那个重确认框（`CtxMenuForm.DoTakeOwn` 的正文最长）。当时窗口写死
    /// **460x260**，而那段正文在 420px 宽下要 **238px 高**，加上标题行和按钮行一共 **334px** ——
    /// 实测（`PrintWindow` 拍实图 + 量每个控件的矩形，两边对上）：
    ///
    /// | 控件 | 位置 | 当时的结果 |
    /// | --- | --- | --- |
    /// | 正文标签 | y=39 高 238 | 最后一行被客户区（高 260）切掉 |
    /// | 按钮行 | **y=290**（客户区只有 260 高） | **整个落在窗口外面：看不到、点不到** |
    ///
    /// 也就是用户想点「取得所有权并禁用」**根本没有那个按钮可点**，等于这个功能用不了。
    /// 现在两条规矩，缺一条就会再犯：
    ///
    /// ① **显示之前自己量一遍内容，把客户区高度撑够**（`FitToContent`，在 `OnLoad` 里调，
    ///    那时字体和控件都真实了、窗口还没画出来）；屏幕装不下就按工作区封顶；
    /// ② **按钮行不跟正文抢高度**：正文放进 `AutoScroll` 面板（第 0 行 `Percent`），按钮单独一行
    ///    `Absolute`（按钮建好之后才量得出这一行多高）。窗口小到撑不开时压缩的是正文区（出滚动条），
    ///    **按钮永远在客户区里**。写死高度 + 让 TableLayoutPanel 自己挤，就是这次踩的坑：它不挤行高，
    ///    它把最后一行整个推到容器外面去。</summary>
    internal sealed class ConfirmForm : Mxx1Form
    {
        /// <summary>命令超过这个长度就不在这里给全文了（脚本正文属于「查看按钮定义」）。</summary>
        private const int CommandLimit = 240;

        /// <summary>正文那一摞的最窄可用宽度（比这还窄就没法读，宁可让窗口宽一点）。</summary>
        private const int MinContentWidth = 240;

        private readonly TableLayoutPanel _root;
        private readonly TableLayoutPanel _content;
        private readonly TableLayoutPanel _buttons;

        private ConfirmForm(string titleText, string bodyText, string marks, string commandLabel,
            string command, bool danger, string okText, Theme theme)
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

            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 2;
            _root.Padding = new Padding(14, 12, 14, 12);
            _root.BackColor = theme.FormBack;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 0 正文（放不下就自己滚，见类说明 ②）
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));   // 1 按钮行（建好按钮后重设成真实高度）

            // 正文：Dock=Top + AutoSize 的一摞标签，套在 AutoScroll 面板里 —— "放不下就滚动"的标准搭法。
            // **按钮行在它外面**，所以正文再长也挤不掉按钮。
            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.Margin = new Padding(0);
            host.AutoScroll = true;
            host.BackColor = theme.FormBack;

            _content = new TableLayoutPanel();
            _content.Dock = DockStyle.Top;
            _content.AutoSize = true;
            _content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _content.ColumnCount = 1;
            _content.Margin = new Padding(0);
            _content.BackColor = theme.FormBack;
            _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Label title = new Label();
            title.AutoSize = true;
            title.MaximumSize = new Size(420, 0);
            title.Font = new Font("Microsoft YaHei", 10f, FontStyle.Bold, GraphicsUnit.Point);
            title.Text = titleText;
            title.Margin = new Padding(2, 0, 2, 8);
            _content.Controls.Add(title);

            // ① 这件事是什么（按重要性排序：先把人话说完，再谈风险，最后才是命令）
            Label what = new Label();
            what.AutoSize = true;
            what.MaximumSize = new Size(420, 0);
            what.Text = bodyText;
            what.Margin = new Padding(2, 0, 2, 10);
            _content.Controls.Add(what);

            // ② 风险 / 权限 / 能不能反悔
            if (marks.Length > 0)
            {
                Label warn = new Label();
                warn.AutoSize = true;
                warn.MaximumSize = new Size(420, 0);
                warn.Text = marks.TrimEnd();
                warn.Margin = new Padding(2, 0, 2, 10);
                _content.Controls.Add(warn);
            }

            // ③ 命令本身：短就给，长了就指路
            if (command.Length > 0 && command.Length <= CommandLimit)
            {
                Label cmdLabel = new Label();
                cmdLabel.AutoSize = true;
                cmdLabel.Text = commandLabel;
                cmdLabel.Margin = new Padding(2, 0, 2, 2);
                _content.Controls.Add(cmdLabel);

                TextBox cmd = new TextBox();
                cmd.ReadOnly = true;
                cmd.Multiline = true;
                cmd.WordWrap = true;
                cmd.ScrollBars = ScrollBars.Vertical;
                cmd.Dock = DockStyle.Top;
                cmd.Height = 56;
                cmd.Text = command;
                cmd.Margin = new Padding(2, 0, 2, 10);
                _content.Controls.Add(cmd);
            }
            else if (command.Length > CommandLimit)
            {
                Label more = new Label();
                more.AutoSize = true;
                more.MaximumSize = new Size(420, 0);
                more.Text = "这个按钮是一段比较长的脚本（" + command.Length + " 个字符），就不摊在这里了："
                    + "在按钮上点右键 →「查看按钮定义」可以看全文。";
                more.Margin = new Padding(2, 0, 2, 10);
                _content.Controls.Add(more);
            }

            host.Controls.Add(_content);

            _buttons = new TableLayoutPanel();
            _buttons.Dock = DockStyle.Fill;
            _buttons.AutoSize = true;
            _buttons.Margin = new Padding(0);
            _buttons.ColumnCount = 3;
            _buttons.BackColor = theme.FormBack;
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button cancel = MakeButton("取消");
            cancel.DialogResult = DialogResult.Cancel;
            Button ok = MakeButton(okText);
            ok.DialogResult = DialogResult.OK;
            Label spacer = new Label();
            spacer.Margin = new Padding(0);
            _buttons.Controls.Add(spacer);
            _buttons.Controls.Add(cancel);
            _buttons.Controls.Add(ok);

            _root.Controls.Add(host, 0, 0);
            _root.Controls.Add(_buttons, 0, 1);

            // 按钮行的高度**从按钮本身量出来**（别写死数字：字号 / DPI 一变就不对了）
            int inner = ClientSize.Width - _root.Padding.Horizontal;
            if (inner < MinContentWidth) { inner = MinContentWidth; }
            int buttonsHeight = _buttons.GetPreferredSize(new Size(inner, 0)).Height;
            if (buttonsHeight < 24) { buttonsHeight = 24; }
            _root.RowStyles[1] = new RowStyle(SizeType.Absolute, buttonsHeight);

            Controls.Add(_root);
            CancelButton = cancel;
            AcceptButton = cancel;   // Enter = 取消：危险动作要真的去点「执行」

            BackColor = theme.FormBack;
            _root.BackColor = theme.FormBack;
            ApplyTextTheme(_content, theme);
            title.ForeColor = theme.TabActiveText;
            foreach (Button b in new Button[] { cancel, ok })
            {
                b.BackColor = theme.ButtonBack;
                b.ForeColor = theme.ButtonText;
                b.FlatAppearance.BorderColor = theme.ButtonBorder;
                b.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                b.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
            }
            ok.ForeColor = danger ? theme.Danger : theme.ButtonText;
        }

        /// <summary>正文里那一摞的文字颜色（标签 / 只读文本框）—— 递归走，因为现在正文是
        /// 「面板 → 一摞标签」两层，不再是 `Controls` 的直接子项（2026-10-10 改的）。</summary>
        private static void ApplyTextTheme(Control parent, Theme theme)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Label) { c.ForeColor = theme.BarText; c.BackColor = Color.Transparent; }
                else if (c is TextBox) { c.ForeColor = theme.InputText; c.BackColor = theme.InputBack; }
                ApplyTextTheme(c, theme);
            }
        }

        /// <summary>窗口显示之前把客户区高度撑到"正文真正需要的高度"（见类说明 ①）。
        ///
        /// 宽度不动：正文标签的换行宽度是 `MaximumSize` 定的（420），窗口宽度跟它已经对上了，
        /// 动宽度只会让换行跟着变、越算越乱。屏幕装不下时按工作区封顶 —— 那时候滚动条出现在正文区，
        /// 按钮行依然在客户区里（第 1 行是 `Absolute`）。</summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitToContent();
        }

        private void FitToContent()
        {
            int inner = ClientSize.Width - _root.Padding.Horizontal;
            if (inner < MinContentWidth) { inner = MinContentWidth; }
            // 不要在这儿扣掉滚动条宽度：正文装得下就不会有滚动条，扣了就白白多出一条空行的高度
            int wanted = _root.Padding.Vertical
                + _content.GetPreferredSize(new Size(inner, 0)).Height
                + _buttons.GetPreferredSize(new Size(inner, 0)).Height;

            int cap = 0;
            try { cap = Screen.FromControl(this).WorkingArea.Height - 80; }
            catch { }
            if (cap < 240) { cap = 240; }
            if (wanted > cap) { wanted = cap; }

            // 不是"只撑大"，是"按内容定"：正文短的框（更新提示那种）也不该留一大块空白。
            // 太小的由 `MinimumSize` 兜底 —— WinForms 会把窗口尺寸自己夹上去。
            if (wanted != ClientSize.Height) { ClientSize = new Size(ClientSize.Width, wanted); }
        }

        public ConfirmForm(ToolItem t, string command, bool asAdmin, bool asElevatedChild, Theme theme)
            : this(BuildTitle(t), BuildBody(t), BuildMarks(t, asAdmin, asElevatedChild), "要执行的东西：",
                   command, t.Danger, "执行", theme)
        {
        }

        /// <summary>「一段话 + 一个动作」的确认框（更新提示这类）。没有命令、没有提权说明。</summary>
        public ConfirmForm(string titleText, string bodyText, string okText, Theme theme)
            : this(titleText, bodyText, "", "", "", false, okText, theme)
        {
        }

        private static string BuildTitle(ToolItem t)
        {
            return "要执行「" + t.Name + "」";
        }

        private static string BuildBody(ToolItem t)
        {
            return (t.Hint.Length > 0) ? t.Hint : "（这个按钮没有写说明）";
        }

        private static string BuildMarks(ToolItem t, bool asAdmin, bool asElevatedChild)
        {
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
            return marks;
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
