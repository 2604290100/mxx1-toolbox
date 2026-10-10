// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「右键菜单管理」里的一行 = 一个菜单项。
    ///
    /// 为什么不用 `ListView`：这个窗口的定位是"和主窗口长一样"，而主窗口的内容区是
    /// **段标题（灰字 + 一条细线）+ 一行行带图标的小卡片**，不是表格。所以这里照 `ToolButton`
    /// 的套路自己搭一行：勾选框 + 图标 + 名称 +（灰色小字）点下去会跑什么 + 右侧状态。
    ///
    /// 三条硬要求（都是踩过的教训，别改）：
    /// ① **名称 / 副标题 / 状态必须是真 `Label`**，不许用 `OnPaint` 画字 —— 界面回归是靠
    ///    `WM_GETTEXT` **跨进程**读这些字的（`Get-ChildControls`），画上去的字读不到；那样
    ///    "窗口里到底列了什么"就只能信命令行，窗口显示错了也测不出来（本仓库为"能读到的字"
    ///    立过好几次规矩）。
    /// ② **勾选框用真 `CheckBox`**（同上：能被 `BM_CLICK` 点、能被读状态），只是配色跟 `Theme` 走 ——
    ///    和 `ConsentForm` 里那个「我已阅读并同意…」的勾选框同一种做法。
    /// ③ **点整行 = 勾上 / 取消勾选**（用户拍板的交互模型 A）：行本身和它那三个 `Label` 挂同一个
    ///    处理器 —— 因为 `Label` 会把鼠标吃掉，不接上就会"点字没反应、点空白才有反应"。
    ///
    /// 自绘的只有三样：行的底色（悬停 / 当前）/ 左侧那条选中亮线 / 底部分隔线。
    /// 行高固定（两行字），不随内容长 —— 一页十七行，行高跳动会让人找不着刚才看的那条。</summary>
    internal sealed class CtxRow : Panel
    {
        /// <summary>一行的固定高度（两行字 + 上下留白）。</summary>
        public const int RowHeight = 46;

        public readonly CtxEntry Entry;

        private readonly Theme _theme;
        private readonly CheckBox _check;
        private readonly PictureBox _icon;
        private readonly Label _name;
        private readonly Label _sub;
        private readonly Label _state;
        private bool _hover;
        private bool _current;

        public CtxRow(CtxEntry e, Theme theme, bool canCheck)
        {
            Entry = e;
            _theme = theme;
            Height = RowHeight;
            Dock = DockStyle.Fill;
            Margin = new Padding(0, 0, 0, 0);
            DoubleBuffered = true;
            BackColor = theme.InputBack;
            Cursor = canCheck ? Cursors.Hand : Cursors.Default;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = new Padding(0);
            root.ColumnCount = 4;
            root.RowCount = 1;
            root.BackColor = Color.Transparent;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30f));    // 勾选框
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22f));    // 图标（抽不到真图标就空着，列宽留着 → 各行文字仍对齐）
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));    // 名称 + 副标题
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));         // 状态
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _check = new CheckBox();
            _check.AutoSize = false;
            _check.Width = 16;
            _check.Height = 16;
            _check.Text = "";
            _check.Anchor = AnchorStyles.Left;
            _check.Margin = new Padding(7, 0, 0, 0);
            _check.TabStop = false;
            _check.Enabled = canCheck;
            _check.ForeColor = theme.InputText;
            _check.BackColor = Color.Transparent;
            _check.CheckedChanged += delegate { Raise(); };
            _check.Click += delegate { Mark(); };
            root.Controls.Add(_check, 0, 0);

            _icon = new PictureBox();
            _icon.Width = 16;
            _icon.Height = RowHeight;
            _icon.Dock = DockStyle.Fill;
            _icon.SizeMode = PictureBoxSizeMode.CenterImage;
            _icon.BackColor = Color.Transparent;
            _icon.Image = CtxMenu.RowIcon(e);
            _icon.Click += delegate { Toggle(); };
            root.Controls.Add(_icon, 1, 0);

            TableLayoutPanel text = new TableLayoutPanel();
            text.Dock = DockStyle.Fill;
            text.Margin = new Padding(0);
            text.ColumnCount = 1;
            text.RowCount = 2;
            text.BackColor = Color.Transparent;
            text.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            text.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            text.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _name = new Label();
            _name.AutoSize = false;
            _name.Dock = DockStyle.Fill;
            _name.TextAlign = ContentAlignment.MiddleLeft;
            _name.AutoEllipsis = true;
            _name.Margin = new Padding(0);
            _name.Text = e.Title;
            _name.ForeColor = (e.Actionable && !e.Disabled) ? theme.InputText : theme.BarText;
            _name.BackColor = Color.Transparent;
            _name.Cursor = Cursor;
            _name.Click += delegate { Toggle(); };
            text.Controls.Add(_name, 0, 0);

            _sub = new Label();
            _sub.AutoSize = false;
            _sub.Dock = DockStyle.Fill;
            _sub.TextAlign = ContentAlignment.TopLeft;
            _sub.AutoEllipsis = true;
            _sub.Margin = new Padding(0);
            _sub.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            _sub.Text = SubText(e);
            _sub.ForeColor = theme.BarText;
            _sub.BackColor = Color.Transparent;
            _sub.Cursor = Cursor;
            _sub.Click += delegate { Toggle(); };
            text.Controls.Add(_sub, 0, 1);

            root.Controls.Add(text, 2, 0);

            _state = new Label();
            _state.AutoSize = true;
            _state.Anchor = AnchorStyles.Right;
            _state.TextAlign = ContentAlignment.MiddleRight;
            _state.Margin = new Padding(8, 0, 10, 0);
            _state.Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            _state.Text = e.StateLabel + " · " + e.WhereLabel;
            _state.ForeColor = theme.BarText;
            _state.BackColor = Color.Transparent;
            _state.Cursor = Cursor;
            _state.Click += delegate { Toggle(); };
            root.Controls.Add(_state, 3, 0);

            Controls.Add(root);
            // 整行点哪儿都算：行本身（勾选框和图标之间的空隙、上下留白）
            Click += delegate { Toggle(); };
            // 鼠标停在那条上时高亮 —— 主窗口的按钮也是这个待遇（ButtonHover）
            foreach (Control c in new Control[] { this, root, text, _name, _sub, _state, _icon })
            {
                c.MouseEnter += delegate { SetHover(true); };
                c.MouseLeave += delegate { SetHover(false); };
            }
        }

        /// <summary>勾选状态（界面上的底栏按钮按它算"选中的几项"）。</summary>
        public bool Checked
        {
            get { return _check.Checked; }
            set { _check.Checked = value; }
        }

        /// <summary>这一行能不能勾（扩展项和"勾不动"的行是灰的）。</summary>
        public bool CanCheck { get { return _check.Enabled; } }

        /// <summary>勾选变了（底栏按钮上的数字要跟着变）。</summary>
        public event EventHandler Toggled;

        /// <summary>这一行被点到了（用来记"当前行"，键盘上下键从它开始走）。</summary>
        public event EventHandler Activated;

        /// <summary>是不是"当前行"（键盘焦点所在的那一条，画一条左侧亮线）。</summary>
        public bool Current
        {
            get { return _current; }
            set
            {
                if (_current == value) { return; }
                _current = value;
                Repaint();
            }
        }

        /// <summary>给测试 / 键盘用的：把这一行切一下（等于点它一下）。</summary>
        public void Toggle()
        {
            Mark();
            if (!_check.Enabled) { return; }
            _check.Checked = !_check.Checked;
        }

        private void Mark()
        {
            if (Activated != null) { Activated(this, EventArgs.Empty); }
        }

        private void Raise()
        {
            Repaint();
            if (Toggled != null) { Toggled(this, EventArgs.Empty); }
        }

        private void SetHover(bool on)
        {
            if (_hover == on) { return; }
            _hover = on;
            Repaint();
        }

        private void Repaint()
        {
            BackColor = CurrentBack();
            Invalidate();
        }

        private Color CurrentBack()
        {
            if (_check.Checked || _current) { return _theme.TabActiveBack; }
            if (_hover) { return _theme.ButtonHover; }
            return _theme.InputBack;
        }

        /// <summary>副标题：能改的项显示"点下去会跑什么"，不能改的（扩展项）显示它到底是什么。</summary>
        private static string SubText(CtxEntry e)
        {
            if (e.Command.Length > 0) { return e.Command; }
            return e.Note;
        }

        /// <summary>自绘两样：勾选 / 当前行左边那条亮线（和主窗口当前页签的底线同一个颜色），
        /// 以及底部的分隔线（把卡片分开，像主窗口段之间那条细线）。</summary>
        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);
            if (_check.Checked || _current)
            {
                using (SolidBrush b = new SolidBrush(_theme.TabActiveUnderline))
                {
                    pe.Graphics.FillRectangle(b, 0, 0, 3, Height);
                }
            }
            using (Pen p = new Pen(_theme.SegmentLine))
            {
                pe.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }
        }

        /// <summary>鼠标滚轮 / 悬停时背景要跟着主题重画（窗口只有浅深两套，构造时取一次就够）。</summary>
        public void ApplyTheme()
        {
            Repaint();
        }
    }
}
