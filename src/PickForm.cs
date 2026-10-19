// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「屏幕取色器」：全屏半透明遮罩 + 一个跟着鼠标走的放大镜。
    ///
    /// 操作（放大镜上也写着，不用记）：
    ///   · 鼠标移动 = 看颜色；放大镜里是光标周围 17×17 像素放大 8 倍，中间那格就是当前像素
    ///   · **左键** = 取色并复制进剪贴板（复制成当前格式）
    ///   · **滚轮** = 切换复制格式：HEX → RGB → HSL
    ///   · **Esc / 右键** = 退出（什么都不复制）
    ///
    /// 为什么做成两块窗口：遮罩要半透明（`Opacity`）才能看见底下的屏幕，但半透明会把放大镜、
    /// 文字也一起变暗。所以放大镜是**另一个不透明的顶层小窗口**，遮罩只负责盖住屏幕和收鼠标事件。
    ///
    /// 换算（HEX / RGB / HSL）走 `src\ColorPicker.cs` —— 和命令行 `pick --decode=` 共用一份；
    /// 这里不自己算，免得"界面上复制的"和"命令行报的"对不上。
    ///
    /// ⚠️ 遮罩窗口故意**不进任务栏、不抢焦点**：取色时用户往往正开着设计稿 / 浏览器，
    /// 抢焦点会把人家正在输入的地方打断。</summary>
    internal sealed class PickForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly MagnifierForm _mag;
        private int _mode = 0;
        private DateTime _copiedAt = DateTime.MinValue;
        private string _copiedText = "";
        private readonly System.Windows.Forms.Timer _copyTimer = new System.Windows.Forms.Timer();

        public PickForm(Theme theme)
        {
            _theme = theme;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Opacity = 0.16;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;

            _mag = new MagnifierForm(this, theme);
            _mag.Show(this);

            _copyTimer.Interval = 1500;
            _copyTimer.Tick += delegate { _copyTimer.Stop(); _copiedText = ""; _mag.Refresh(); };

            MouseMove += delegate { UpdateSample(Cursor.Position); };
            MouseDown += delegate (object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) { Close(); return; }
                if (e.Button == MouseButtons.Left) { PickCurrent(Cursor.Position); }
            };
            MouseWheel += delegate
            {
                _mode = (_mode + 1) % ColorPicker.Modes.Length;
                _copiedText = "";
                _copiedAt = DateTime.MinValue;
                _mag.Refresh();
            };
            KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Close(); }
            };
            Shown += delegate { MoveMagnifier(Cursor.Position); UpdateSample(Cursor.Position); };
            FormClosed += delegate { try { _mag.Close(); } catch { } };
        }

        private void UpdateSample(Point pt)
        {
            Color c = SampleAt(pt);
            _mag.SetSample(pt, c);
            MoveMagnifier(pt);
        }

        /// <summary>取一个屏幕像素。`CopyFromScreen` 拿的是**屏幕上真实显示的东西**（和截图一样），
        /// 不是某个窗口自己画的样子。</summary>
        private static Color SampleAt(Point pt)
        {
            try
            {
                using (Bitmap one = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(one))
                    {
                        g.CopyFromScreen(pt.X, pt.Y, 0, 0, new Size(1, 1), CopyPixelOperation.SourceCopy);
                    }
                    return one.GetPixel(0, 0);
                }
            }
            catch { return Color.Black; }
        }

        private void PickCurrent(Point pt)
        {
            Color c = SampleAt(pt);
            string text = ColorPicker.CopyText(c.R, c.G, c.B, ColorPicker.Modes[_mode]);
            bool ok = false;
            for (int attempt = 0; attempt < 2 && !ok; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    ok = true;
                }
                catch
                {
                    Thread.Sleep(40);
                }
            }
            _copiedText = ok ? text : ("没复制成（剪贴板被别的程序占着）：" + text);
            _copiedAt = DateTime.Now;
            _copyTimer.Stop();
            _copyTimer.Start();
            _mag.SetSample(pt, c);
            _mag.Refresh();
            Logger.Write("屏幕取色器", "取色 " + ColorPicker.ToHex(c.R, c.G, c.B) + " → 复制 " + text
                + (ok ? "" : "（剪贴板没写进去）"));
        }

        private void MoveMagnifier(Point pt)
        {
            Rectangle area;
            try { area = Screen.FromPoint(pt).WorkingArea; }
            catch { area = SystemInformation.VirtualScreen; }
            int x = pt.X + 24;
            int y = pt.Y + 24;
            Size size = _mag.Size;
            if (x + size.Width > area.Right) { x = pt.X - size.Width - 24; }
            if (y + size.Height > area.Bottom) { y = pt.Y - size.Height - 24; }
            if (x < area.Left) { x = area.Left; }
            if (y < area.Top) { y = area.Top; }
            _mag.Location = new Point(x, y);
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { _mag.Close(); } catch { }
        }

        /// <summary>跟着鼠标走的放大镜 + 信息条。不透明、不进任务栏、不被激活（不抢焦点）。</summary>
        internal sealed class MagnifierForm : Mxx1Form
        {
            private const int Zoom = 8;
            private const int Cells = 17;
            private const int Pad = 10;
            private const int TextWidth = 214;

            private readonly PickForm _owner;
            private readonly Theme _theme;
            private readonly Bitmap _sample = new Bitmap(Cells, Cells, PixelFormat.Format32bppArgb);
            private Point _point;
            private Color _center = Color.Black;
            private int _sampleLeft;
            private int _sampleTop;

            public MagnifierForm(PickForm owner, Theme theme)
            {
                _owner = owner;
                _theme = theme;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                ShowInTaskbar = false;
                BackColor = theme.DarkMode ? Color.FromArgb(28, 28, 28) : Color.FromArgb(250, 250, 250);
                ClientSize = new Size(Pad * 2 + Cells * Zoom + 8 + TextWidth, Pad * 2 + Cells * Zoom);
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE：永远不抢焦点
                    cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW：不进任务栏 / Alt-Tab
                    return cp;
                }
            }

            public void SetSample(Point pt, Color center)
            {
                _point = pt;
                _center = center;
                int left = SystemInformation.VirtualScreen.Left;
                int top = SystemInformation.VirtualScreen.Top;
                int width = SystemInformation.VirtualScreen.Width;
                int height = SystemInformation.VirtualScreen.Height;
                int sx = pt.X - Cells / 2;
                int sy = pt.Y - Cells / 2;
                if (sx < left) { sx = left; }
                if (sy < top) { sy = top; }
                if (sx > left + width - Cells) { sx = left + width - Cells; }
                if (sy > top + height - Cells) { sy = top + height - Cells; }
                _sampleLeft = sx;
                _sampleTop = sy;
                try
                {
                    using (Graphics g = Graphics.FromImage(_sample))
                    {
                        g.CopyFromScreen(sx, sy, 0, 0, new Size(Cells, Cells), CopyPixelOperation.SourceCopy);
                    }
                }
                catch { }
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                Rectangle zoomRect = new Rectangle(Pad, Pad, Cells * Zoom, Cells * Zoom);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_sample, zoomRect);
                g.PixelOffsetMode = PixelOffsetMode.Default;

                using (Pen grid = new Pen(Color.FromArgb(56, 255, 255, 255)))
                {
                    for (int i = 1; i < Cells; i++)
                    {
                        g.DrawLine(grid, zoomRect.Left + i * Zoom, zoomRect.Top,
                            zoomRect.Left + i * Zoom, zoomRect.Bottom);
                        g.DrawLine(grid, zoomRect.Left, zoomRect.Top + i * Zoom,
                            zoomRect.Right, zoomRect.Top + i * Zoom);
                    }
                }

                int cellX = _point.X - _sampleLeft;
                int cellY = _point.Y - _sampleTop;
                cellX = Math.Max(0, Math.Min(Cells - 1, cellX));
                cellY = Math.Max(0, Math.Min(Cells - 1, cellY));
                using (Pen hot = new Pen(Color.White, 2f))
                {
                    g.DrawRectangle(hot, zoomRect.Left + cellX * Zoom + 1, zoomRect.Top + cellY * Zoom + 1, Zoom - 2, Zoom - 2);
                }
                using (Pen rim = new Pen(Color.FromArgb(180, 0, 0, 0)))
                {
                    g.DrawRectangle(rim, zoomRect.Left + cellX * Zoom, zoomRect.Top + cellY * Zoom, Zoom, Zoom);
                }

                g.DrawRectangle(Pens.Gray, new Rectangle(0, 0, Width - 1, Height - 1));

                int tx = zoomRect.Right + 8;
                int ty = Pad;
                Color textColor = _theme.DarkMode ? Color.White : Color.Black;
                using (Font bold = new Font("Microsoft YaHei", 10f, FontStyle.Bold, GraphicsUnit.Point))
                using (Font small = new Font("Microsoft YaHei", 8.5f, FontStyle.Regular, GraphicsUnit.Point))
                using (SolidBrush brush = new SolidBrush(textColor))
                using (SolidBrush dim = new SolidBrush(_theme.DarkMode ? Color.FromArgb(200, 210, 210, 210) : Color.FromArgb(215, 70, 70, 70)))
                using (SolidBrush good = new SolidBrush(_theme.DarkMode ? Color.LightGreen : Color.DarkGreen))
                {
                    g.DrawString(ColorPicker.ToHex(_center.R, _center.G, _center.B), bold, brush, tx, ty);
                    ty += 26;
                    g.DrawString("RGB  " + ColorPicker.ToRgb(_center.R, _center.G, _center.B), small, brush, tx, ty);
                    ty += 20;
                    g.DrawString(ColorPicker.ToHslText(_center.R, _center.G, _center.B), small, brush, tx, ty);
                    ty += 24;
                    g.DrawString("坐标  " + _point.X.ToString(CultureInfo.InvariantCulture) + ", "
                        + _point.Y.ToString(CultureInfo.InvariantCulture), small, dim, tx, ty);
                    ty += 22;
                    g.DrawString("复制格式（滚轮切换）：" + ColorPicker.Modes[_owner.ModeIndex].ToUpperInvariant(), small, brush, tx, ty);
                    ty += 18;
                    g.DrawString("左键复制 · 右键 / Esc 退出", small, dim, tx, ty);
                    string copied = _owner.CopiedText;
                    if (copied.Length > 0)
                    {
                        ty += 20;
                        g.DrawString(copied, small, good, tx, ty);
                    }
                }
            }
        }

        /// <summary>当前复制格式（0=hex / 1=rgb / 2=hsl）。放大镜上要显示它。</summary>
        public int ModeIndex
        {
            get { return _mode; }
        }

        /// <summary>刚复制成功的提示（1.5 秒后自己消失）。放大镜上要显示它。</summary>
        public string CopiedText
        {
            get
            {
                if (_copiedText.Length == 0) { return ""; }
                if ((DateTime.Now - _copiedAt).TotalMilliseconds > 1500) { return ""; }
                return "已复制：" + _copiedText;
            }
        }
    }
}

