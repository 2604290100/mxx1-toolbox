// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>提示的轻重 —— 只决定卡片左边那条竖线的颜色。</summary>
    internal enum NoticeKind
    {
        Info = 0,
        Ok = 1,
        Warn = 2,
        Fail = 3
    }

    /// <summary>「一键解除占用」的结果提示：**鼠标旁边一张小卡片**。
    ///
    /// 为什么要有它：「一键解除占用」是**不弹窗口**的（用户 2026-10-05 要的），可是"点了什么都
    /// 不发生"同样不行 —— 用户 2026-10-04 就报过「点击确认以后也没有成功或者失败的反馈」。
    ///
    /// 为什么不用系统托盘气泡（v1.5.3 那版就是它，`NotifyIcon.ShowBalloonTip`）：用户 2026-10-06
    /// 实测**根本看不到**（这台是精简版 Windows，通知平台被裁过；Win10 / Win11 的专注助手、
    /// 通知设置关掉时同样看不到），而且气泡落在右下角、**离用户正看着的地方很远**。
    /// 用户原话：「气泡没有正常弹出，而且弹出的位置要跟随鼠标」—— 于是改成**自己画一张卡片**：
    /// 一定画得出来、位置就在鼠标旁边、鼠标动它跟着动、点一下就关掉。
    ///
    /// 四条规矩（别改，改了就成了"抢焦点 / 挡路"）：
    ///   ① **不许抢焦点**：`ShowWithoutActivation` + `WS_EX_NOACTIVATE` —— 正在打字时它弹出来，
    ///      字照样打进原来那个窗口，也不会把资源管理器挤到后面；
    ///   ② **几秒后自己消失**，点一下立刻消失（点它不会把点击透传给底下的窗口）；
    ///   ③ **不占任务栏、不进 Alt+Tab**（`ShowInTaskbar=false` + `WS_EX_TOOLWINDOW`）；
    ///   ④ 位置永远夹在**鼠标所在那块屏幕的工作区**里（多显示器 / 鼠标贴边也不会跑到屏幕外）。
    ///
    /// 前置条件：STA + `Application.EnableVisualStyles()`（Program.Main 开头已经做了）。
    /// 关掉它的办法：`--quiet` / `--notify=0` / 环境变量 `MXX1_NO_NOTIFY=1`（回归测试用它 ——
    /// 测试不该在别人桌面上弹东西）。</summary>
    internal static class Balloon
    {
        /// <summary>弹一张提示卡（信息级），等它自己消失（或者被点掉）再返回。</summary>
        public static void Show(string title, string text, int ms)
        {
            Show(title, text, ms, NoticeKind.Info);
        }

        public static void Show(string title, string text, int ms, NoticeKind kind)
        {
            try
            {
                Theme theme = Theme.Resolve(Settings.Load().Theme);
                using (NoticeForm card = new NoticeForm(title, text, ms, kind, theme))
                {
                    card.Show();
                    Application.Run(card);   // 要有消息循环：卡片才跟得上鼠标、才到点自己关
                }
            }
            catch (Exception ex)
            {
                Logger.Write("提示卡片", "没弹出来（不影响已经做完的事）：" + ex.Message);
            }
        }
    }

    /// <summary>那张卡片本身（为什么是它、四条规矩见上面 `Balloon` 的说明）。
    ///
    /// 画面全部自己画（`OnPaint`）：卡片是有边框的窗口里最挑尺寸的一种 —— 用 Label 拼的话
    /// 每加一行都要重算高度（「解除文件占用」那个窗口就在这上面栽过，见 DESIGN §12.4x），
    /// 自己画就是"先量文字再定窗口大小"，一次算准。</summary>
    internal sealed class NoticeForm : Form
    {
        private const int OffsetX = 18;         // 挪到鼠标右下多远
        private const int OffsetY = 22;
        private const int PadX = 12;
        private const int PadY = 10;
        private const int IconGap = 8;
        private const int TitleGap = 4;
        private const int MaxBodyWidth = 300;   // 正文最宽（再长就换行）
        private const int MinBodyWidth = 170;
        private const int FollowMs = 60;        // 跟鼠标的节拍
        private const int FollowSlack = 8;      // 鼠标没挪这么多像素就不动它
        private const int MinLifeMs = 1200;

        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;

        private readonly string _title;
        private readonly string _body;
        private readonly int _lifeMs;
        private readonly Font _titleFont;
        private readonly Font _bodyFont;
        private readonly Color _titleColor;
        private readonly Color _bodyColor;
        private readonly Color _border;
        private readonly Color _accent;
        private readonly Icon _icon;
        private readonly int _iconW;
        private readonly int _iconH;
        private readonly int _titleHeight;

        private readonly Timer _follow = new Timer();
        private readonly Timer _life = new Timer();
        private Point _anchor;
        private bool _flipX;
        private bool _flipY;

        public NoticeForm(string title, string text, int ms, NoticeKind kind, Theme theme)
        {
            _title = (title == null) ? "" : title;
            _body = Clip(text, 300);
            _lifeMs = (ms < MinLifeMs) ? MinLifeMs : ms;

            _titleFont = new Font("Microsoft YaHei", 9f, FontStyle.Bold, GraphicsUnit.Point);
            _bodyFont = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _titleColor = theme.InputText;
            _bodyColor = theme.DarkMode
                ? Color.FromArgb(0xC8, 0xC8, 0xC8)
                : Color.FromArgb(0x33, 0x33, 0x33);
            _border = theme.InputBorder;
            _accent = AccentOf(kind, theme.DarkMode);

            _icon = AppIcon.ForNotice();
            _iconW = (_icon == null) ? 0 : _icon.Width;
            _iconH = (_icon == null) ? 0 : _icon.Height;

            // 先量文字，再定窗口大小 —— 量出来的高度就是画的时候用的高度，不会切字
            Size titleSize = TextRenderer.MeasureText(_title, _titleFont, new Size(MaxBodyWidth, 1000),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            Size bodySize = TextRenderer.MeasureText(_body, _bodyFont, new Size(MaxBodyWidth, 1000),
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            _titleHeight = Math.Max(titleSize.Height, _iconH);

            int contentW = bodySize.Width;
            int titleW = titleSize.Width + ((_iconW > 0) ? (_iconW + IconGap) : 0);
            if (titleW > contentW) { contentW = titleW; }
            if (contentW < MinBodyWidth) { contentW = MinBodyWidth; }
            if (contentW > MaxBodyWidth) { contentW = MaxBodyWidth; }

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            Font = _bodyFont;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            Text = _title;                       // 回归测试按标题找这个窗口（Get-TopWindows 读的就是它）
            BackColor = theme.InputBack;
            ClientSize = new Size(contentW + PadX * 2, PadY * 2 + _titleHeight + TitleGap + bodySize.Height);
        }

        /// <summary>只显示、不抢焦点：WinForms 会照这个用 SW_SHOWNOACTIVATE 显示窗口。</summary>
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WsExNoActivate;    // 点它也不会激活
                cp.ExStyle |= WsExToolWindow;    // 不进 Alt+Tab
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _anchor = Cursor.Position;
            PlaceAt(_anchor);

            _follow.Interval = FollowMs;
            _follow.Tick += delegate { Follow(); };
            _follow.Start();

            _life.Interval = _lifeMs;
            _life.Tick += delegate { _life.Stop(); Close(); };
            _life.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _follow.Stop();
            _life.Stop();
            _follow.Dispose();
            _life.Dispose();
            base.OnFormClosed(e);
        }

        /// <summary>鼠标动了就跟着挪（没动够 `FollowSlack` 像素就不动，免得抖）。</summary>
        private void Follow()
        {
            Point p = Cursor.Position;
            if (Math.Abs(p.X - _anchor.X) < FollowSlack && Math.Abs(p.Y - _anchor.Y) < FollowSlack) { return; }
            _anchor = p;
            PlaceAt(p);
        }

        /// <summary>放到鼠标旁边：默认右下，放不下就翻到另一侧（翻过一次就记住，免得鼠标压在
        /// 临界线上时来回跳），最后无论如何都夹进那块屏幕的工作区。</summary>
        private void PlaceAt(Point cursor)
        {
            Rectangle wa = Screen.FromPoint(cursor).WorkingArea;

            int x = _flipX ? (cursor.X - OffsetX - Width) : (cursor.X + OffsetX);
            if (x < wa.Left || x + Width > wa.Right)
            {
                _flipX = !_flipX;
                x = _flipX ? (cursor.X - OffsetX - Width) : (cursor.X + OffsetX);
            }

            int y = _flipY ? (cursor.Y - OffsetY - Height) : (cursor.Y + OffsetY);
            if (y < wa.Top || y + Height > wa.Bottom)
            {
                _flipY = !_flipY;
                y = _flipY ? (cursor.Y - OffsetY - Height) : (cursor.Y + OffsetY);
            }

            if (x < wa.Left) { x = wa.Left; }
            if (y < wa.Top) { y = wa.Top; }
            if (x + Width > wa.Right) { x = wa.Right - Width; }
            if (y + Height > wa.Bottom) { y = wa.Bottom - Height; }

            Location = new Point(x, y);
        }

        /// <summary>点一下就关（不然它得挂在那儿等自己到点）。</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            using (SolidBrush back = new SolidBrush(BackColor)) { g.FillRectangle(back, 0, 0, w, h); }
            using (SolidBrush accent = new SolidBrush(_accent)) { g.FillRectangle(accent, 0, 0, 3, h); }
            using (Pen edge = new Pen(_border)) { g.DrawRectangle(edge, 0, 0, w - 1, h - 1); }

            int x = PadX;
            if (_icon != null)
            {
                g.DrawIcon(_icon, new Rectangle(x, PadY + (_titleHeight - _iconH) / 2, _iconW, _iconH));
                x += _iconW + IconGap;
            }

            TextRenderer.DrawText(g, _title, _titleFont,
                new Rectangle(x, PadY, w - x - PadX, _titleHeight), _titleColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            int bodyTop = PadY + _titleHeight + TitleGap;
            TextRenderer.DrawText(g, _body, _bodyFont,
                new Rectangle(PadX, bodyTop, w - PadX * 2, h - bodyTop - PadY), _bodyColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        }

        private static Color AccentOf(NoticeKind kind, bool dark)
        {
            switch (kind)
            {
                case NoticeKind.Ok:
                    return dark ? Color.FromArgb(0x6B, 0xD0, 0x8A) : Color.FromArgb(0x1E, 0x8E, 0x3E);
                case NoticeKind.Warn:
                    return dark ? Color.FromArgb(0xFF, 0xB3, 0x4D) : Color.FromArgb(0xC7, 0x77, 0x00);
                case NoticeKind.Fail:
                    return dark ? Color.FromArgb(0xFF, 0x7A, 0x7A) : Color.FromArgb(0xB0, 0x00, 0x20);
                default:
                    return dark ? Color.FromArgb(0x4C, 0xA0, 0xE8) : Color.FromArgb(0x00, 0x78, 0xD7);
            }
        }

        /// <summary>卡片上放不下的长文（日志里是完整的）。</summary>
        private static string Clip(string s, int max)
        {
            if (s == null) { return ""; }
            s = s.Replace("\r\n", " ").Replace("\n", " ").Trim();
            return (s.Length <= max) ? s : (s.Substring(0, max - 1) + "…");
        }
    }
}
