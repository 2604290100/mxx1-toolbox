// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Runtime.InteropServices;
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
    ///
    /// 用户 2026-10-06 晚**改口**（原话：「**哪个弹框弹出以后不要跟随鼠标和 3 秒自动消失**」）——
    /// v1.5.4 那张卡是"跟着鼠标走 + 到点自己消失"，他自己用起来发现那两条恰恰是毛病：
    ///   ① **跟着鼠标跑 = 点不到它**：他要去点掉卡片的时候，鼠标一动卡片先跑了
    ///      （"跟随鼠标"和"点一下关"天生打架）；
    ///   ② **几秒不够读**：卡片上写的是「已解锁：结束了 3 个程序（a.exe、b.exe、c.exe 等 5 个）」，
    ///      还得看清都是谁，一闪而过等于没看清。
    /// 所以现在：**位置钉在弹出来的地方，之后不再动**；**不写 `--notify` 就一直留着**，点一下才关
    /// （常驻时卡片右下角写着「点一下关闭」）。`--notify=<毫秒>` 照旧 —— 测试和"想让它自己走"的
    /// 场合用它（`--notify=0` / `--quiet` / `MXX1_NO_NOTIFY=1` = 干脆不显示）。
    ///
    /// 三条规矩（别改，改了就成了"抢焦点 / 挡路"）：
    ///   ① **不许抢焦点**：`ShowWithoutActivation` + `WS_EX_NOACTIVATE` —— 正在打字时它弹出来，
    ///      字照样打进原来那个窗口，也不会把资源管理器挤到后面；
    ///   ② **不占任务栏、不进 Alt+Tab**（`ShowInTaskbar=false` + `WS_EX_TOOLWINDOW`）；
    ///   ③ 位置永远夹在**鼠标所在那块屏幕的工作区**里（多显示器 / 鼠标贴边也不会跑到屏幕外）。
    ///
    /// 另外**同时只留一张卡**：窗口标题固定是「一键解除占用」（`NoticeForm.WindowTitle`），
    /// 新卡起来之前先把上一张关掉 —— 卡片变成常驻之后，"点几次攒一摞关不掉的卡片"才是真麻烦。
    ///
    /// 前置条件：STA + `Application.EnableVisualStyles()`（Program.Main 开头已经做了）。</summary>
    internal static class Balloon
    {
        private const uint WmClose = 0x0010;

        /// <summary>把上一张卡关掉。它是**别的进程**开的（每次点右键都是一次新的 exe 进程），
        /// 所以只能按窗口标题找 —— 标题是固定的常量，不是动态拼的。</summary>
        private static void ClosePrevious()
        {
            try
            {
                IntPtr h = FindWindowW(null, NoticeForm.WindowTitle);
                if (h != IntPtr.Zero) { PostMessageW(h, WmClose, IntPtr.Zero, IntPtr.Zero); }
            }
            catch (Exception ex)
            {
                Logger.Write("提示卡片", "没关掉上一张卡（不影响这次的提示）：" + ex.Message);
            }
        }

        /// <summary>弹一张提示卡（信息级）。`ms > 0` 到点自己消失，`ms <= 0` **一直留着**（点一下才关）。
        /// 等它关掉（或者被点掉）才返回 —— 常驻那一档会一直等下去，这是有意的。</summary>
        public static void Show(string title, string text, int ms)
        {
            Show(title, text, ms, NoticeKind.Info);
        }

        public static void Show(string title, string text, int ms, NoticeKind kind)
        {
            try
            {
                Theme theme = Theme.Resolve(Settings.Load().Theme);
                ClosePrevious();
                using (NoticeForm card = new NoticeForm(title, text, ms, kind, theme))
                {
                    card.Show();
                    Application.Run(card);   // 要有消息循环：卡片才画得出来、点一下才关得掉
                }
            }
            catch (Exception ex)
            {
                Logger.Write("提示卡片", "没弹出来（不影响已经做完的事）：" + ex.Message);
            }
        }

        // ExactSpelling 这一条照 `tests\Test-Gui.ps1` 里 SendMessageTextW 那个坑写：不写它，运行时
        // 会先去找带 W 后缀的名字（FindWindowWW）→ 找不到就抛 EntryPointNotFoundException，
        // 而它正好被上面的 catch 吞掉，表现成"上一张卡关不掉"（查起来会以为是标题对不上）。
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW", ExactSpelling = true)]
        private static extern IntPtr FindWindowW(string className, string windowName);
        [DllImport("user32.dll")]
        private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

    /// <summary>那张卡片本身（为什么是它、三条规矩见上面 `Balloon` 的说明）。
    ///
    /// 画面全部自己画（`OnPaint`）：卡片是有边框的窗口里最挑尺寸的一种 —— 用 Label 拼的话
    /// 每加一行都要重算高度（「解除文件占用」那个窗口就在这上面栽过，见 DESIGN §12.4x），
    /// 自己画就是"先量文字再定窗口大小"，一次算准。</summary>
    internal sealed class NoticeForm : Form
    {
        /// <summary>窗口标题 —— **固定值**。两个用途：新卡片靠它找到上一张关掉
        /// （`Balloon.ClosePrevious`）、回归 N21 靠它找这张卡。状态那句（"已处理" / "没执行"）
        /// 画在卡片上，**不写进窗口标题**。</summary>
        public const string WindowTitle = "一键解除占用";

        private const int OffsetX = 18;         // 挪到鼠标右下多远（只算这一次，之后不动）
        private const int OffsetY = 22;
        private const int PadX = 12;
        private const int PadY = 10;
        private const int IconGap = 8;
        private const int TitleGap = 4;
        private const int HintGap = 6;          // 正文和「点一下关闭」之间
        private const int MaxBodyWidth = 300;   // 正文最宽（再长就换行）
        private const int MinBodyWidth = 170;
        private const int MinLifeMs = 1200;     // 给了 --notify 时最短显示多久（再短看不见）
        private const string DismissHint = "点一下关闭";

        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;

        private readonly string _title;
        private readonly string _body;
        private readonly int _lifeMs;           // <= 0 = 常驻（点一下才关）
        private readonly bool _stays;
        private readonly Font _titleFont;
        private readonly Font _bodyFont;
        private readonly Color _titleColor;
        private readonly Color _bodyColor;
        private readonly Color _hintColor;
        private readonly Color _border;
        private readonly Color _accent;
        private readonly Icon _icon;
        private readonly int _iconW;
        private readonly int _iconH;
        private readonly int _titleHeight;
        private readonly int _hintTop;          // 常驻那行提示的 y（不常驻 = 0）
        private readonly int _hintHeight;

        private readonly Timer _life = new Timer();
        private Point _anchor;
        private bool _flipX;
        private bool _flipY;

        public NoticeForm(string title, string text, int ms, NoticeKind kind, Theme theme)
        {
            _title = (title == null) ? "" : title;
            _body = Clip(text, 300);
            _stays = (ms <= 0);
            _lifeMs = _stays ? 0 : ((ms < MinLifeMs) ? MinLifeMs : ms);

            _titleFont = new Font("Microsoft YaHei", 9f, FontStyle.Bold, GraphicsUnit.Point);
            _bodyFont = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _titleColor = theme.InputText;
            _bodyColor = theme.DarkMode
                ? Color.FromArgb(0xC8, 0xC8, 0xC8)
                : Color.FromArgb(0x33, 0x33, 0x33);
            _hintColor = theme.DarkMode
                ? Color.FromArgb(0x8A, 0x8A, 0x8A)
                : Color.FromArgb(0x88, 0x88, 0x88);
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
            Size hintSize = _stays
                ? TextRenderer.MeasureText(DismissHint, _bodyFont, new Size(MaxBodyWidth, 1000),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine)
                : Size.Empty;
            _titleHeight = Math.Max(titleSize.Height, _iconH);
            _hintTop = PadY + _titleHeight + TitleGap + bodySize.Height + HintGap;
            _hintHeight = _stays ? hintSize.Height : 0;

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
            Text = WindowTitle;                  // 固定标题：上一张卡靠它被找到（回归 N21 也按它找）
            BackColor = theme.InputBack;
            ClientSize = new Size(contentW + PadX * 2,
                PadY * 2 + _titleHeight + TitleGap + bodySize.Height + (_stays ? (HintGap + _hintHeight) : 0));
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

            // 位置**只算这一次** —— 之后不再跟着鼠标挪（用户 2026-10-06 晚：卡片跟着鼠标跑，
            // 他就点不到它了；这条原来是"鼠标动它跟着动"，已经按用户改口反转，见类说明）
            _anchor = Cursor.Position;
            PlaceAt(_anchor);

            // 常驻那一档**不起计时器**：一直留着，直到用户点一下（或者被下一张卡关掉）
            if (_lifeMs > 0)
            {
                _life.Interval = _lifeMs;
                _life.Tick += delegate { _life.Stop(); Close(); };
                _life.Start();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _life.Stop();
            _life.Dispose();
            base.OnFormClosed(e);
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

        /// <summary>点一下就关（常驻那一档就靠它，不然卡片得挂在那儿谁也赶不走）。</summary>
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
                new Rectangle(PadX, bodyTop, w - PadX * 2, h - bodyTop - PadY - (_stays ? (HintGap + _hintHeight) : 0)),
                _bodyColor, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);

            // 常驻时把"怎么关掉"写在卡片上 —— 不然用户会以为它关不掉（用户 2026-10-06 晚要的就是
            // "别自己消失"，那"怎么让它消失"就必须看得见）
            if (_stays)
            {
                TextRenderer.DrawText(g, DismissHint, _bodyFont,
                    new Rectangle(PadX, _hintTop, w - PadX * 2, _hintHeight), _hintColor,
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Right);
            }
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
