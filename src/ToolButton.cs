// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>One compact button of the wall: a 16x15 icon canvas (see IconFactory.Normalize,
    /// which exists so the icon lands on the label's optical centre) plus a plain text label.
    ///
    /// A button whose feature is not wired up yet (ToolItem.Placeholder) is drawn in grey --
    /// grey label, grey border, greyed-out icon -- but stays clickable, because clicking it is
    /// what tells the user that the feature is still missing.</summary>
    internal sealed class ToolButton : Button
    {
        public readonly ToolItem Tool;

        /// <summary>清单里的全名。窗口文字（Text）可能会为了塞进按钮而缩成「前几个字…」，
        /// 全名永远留在这里：工具提示、AccessibleName 用的都是它（用户 2026-10-19 报的
        /// 「桌面模式（合盖不睡）」被截成「桌面模式（合盖…」就是这一段在处理）。</summary>
        public readonly string FullName;

        private Theme _theme;
        private bool _busy;
        private Timer _flash;
        private int _fittedWidth = -1;

        public ToolButton(ToolItem tool)
        {
            Tool = tool;
            FullName = tool.Name;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            UseVisualStyleBackColor = false;
            TextAlign = ContentAlignment.MiddleCenter;
            TextImageRelation = TextImageRelation.ImageBeforeText;
            ImageAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(2, 0, 2, 0);
            Font = new Font("Microsoft YaHei", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            AutoEllipsis = true;
            Text = tool.Name;
            Image = tool.Placeholder ? IconFactory.GetMuted(tool) : IconFactory.Get(tool);
            TabStop = false;
            AccessibleName = tool.Name;
            AccessibleDescription = tool.Id;
            if (tool.Placeholder)
            {
                // A grey button is DISABLED (user's rule: "默认灰色按钮应该是禁止点击的"): it cannot
                // be clicked, focused or right-clicked. That also means no tooltip, so the status
                // bar carries the legend "灰色 = 还没接功能" while such a button is on the page.
                Enabled = false;
                AccessibleDescription = tool.Id + " (placeholder)";
            }
        }

        public bool Busy { get { return _busy; } }

        // ------------------------------------------------------------ 名字塞不下的时候

        /// <summary>按钮摆好（或窗口被拖动）之后量一次：名字塞得下就原样显示，塞不下就缩成
        /// 「前几个字…」。
        ///
        /// 为什么非要自己缩（用户 2026-10-19 报的「桌面模式（合盖不睡）」）：WinForms 的按钮在
        /// 「图标在文字左边」（ImageBeforeText）塞不下时，会**自己把图标翻到文字下面**、再把文字
        /// 截断 —— 画出来是文字在上、图标缩在左下角，看着像界面坏了（截图上就是这个问题）。
        /// 实测（144x30 按钮、8.25pt 微软雅黑）：117px 的「桌面模式（合盖不睡）」内容要 149px > 144，
        /// 于是翻了；把文字缩到 149 以内（≤112px）就不翻。判据用 PreferredSize —— 它就是适配器
        /// 算出来的"内容需要多宽"，比 Width 大就是塞不下；缩完再验一次，自己跟它对齐。
        ///
        /// 缩的只是画在按钮上的那串字；全名留在 FullName / 工具提示 / AccessibleName 里。</summary>
        public void FitLabel()
        {
            if (Width <= 0) { return; }
            if (_fittedWidth == Width) { return; }
            _fittedWidth = Width;
            Text = FullName;
            if (PreferredSize.Width <= Width) { return; }   // 塞得下：一个字都不动
            int lo = 0;
            int hi = FullName.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                Text = FullName.Substring(0, mid) + "…";
                if (PreferredSize.Width <= Width) { lo = mid; } else { hi = mid - 1; }
            }
            Text = FullName.Substring(0, lo) + "…";
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitLabel();   // Dock=Fill 摆好之后、以及用户拖动窗口时都会走到这里
        }

        public void ApplyTheme(Theme theme)
        {
            _theme = theme;
            if (Tool.Placeholder)
            {
                BackColor = theme.PlaceholderBack;
                FlatAppearance.BorderColor = theme.PlaceholderBorder;
                FlatAppearance.MouseOverBackColor = theme.PlaceholderHover;
                FlatAppearance.MouseDownBackColor = theme.PlaceholderPressed;
            }
            else
            {
                BackColor = theme.ButtonBack;
                FlatAppearance.BorderColor = theme.ButtonBorder;
                FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
            }
            ForeColor = CurrentTextColor();
            if (!Tool.Placeholder)
            {
                Image = IconFactory.Get(Tool);
            }
            Invalidate();
        }

        /// <summary>Normal label colour, danger colour for a dangerous button and the muted grey
        /// for a placeholder (or for any button while it is busy / flashing).</summary>
        private Color CurrentTextColor()
        {
            if (_theme == null) { return ForeColor; }
            if (_busy) { return _theme.ButtonDisabledText; }
            if (Tool.Placeholder) { return _theme.PlaceholderText; }
            return Tool.Danger ? _theme.Danger : _theme.ButtonText;
        }

        /// <summary>Busy = the button is running something. The icon is swapped for a spinner of
        /// exactly the same canvas size and the text is never touched. A grey placeholder button
        /// stays disabled forever (it has no feature to run).</summary>
        public void SetBusy(bool busy, int autoClearMs)
        {
            if (Tool.Placeholder) { return; }   // nothing behind it: never enable, never spin
            _busy = busy;
            Enabled = !busy;
            // Text is deliberately NOT touched. Appending "…" widened the image+text group, and
            // because the group is centred the icon jumped sideways on every click; on the widest
            // labels the text even overflowed the button. The busy state is shown by this icon
            // (same 16x15 size, see IconFactory.Normalize) plus the disabled colours.
            Image = busy ? IconFactory.Busy() : IconFactory.Get(Tool);
            ForeColor = CurrentTextColor();
            ArmFlash(busy ? autoClearMs : 0);
        }

        private void ArmFlash(int autoClearMs)
        {
            if (_flash != null) { _flash.Stop(); _flash.Dispose(); _flash = null; }
            if (autoClearMs <= 0) { return; }
            _flash = new Timer();
            _flash.Interval = autoClearMs;
            _flash.Tick += delegate(object s, EventArgs e)
            {
                Timer t = (Timer)s;
                t.Stop();
                SetBusy(false, 0);
            };
            _flash.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _flash != null) { _flash.Dispose(); _flash = null; }
            base.Dispose(disposing);
        }
    }
}
