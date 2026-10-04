// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>One compact button of the wall: 16x16 PNG icon plus a plain text label.</summary>
    internal sealed class ToolButton : Button
    {
        public readonly ToolItem Tool;

        private Theme _theme;
        private bool _busy;
        private Timer _flash;

        public ToolButton(ToolItem tool)
        {
            Tool = tool;
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
            Image = IconFactory.Get(tool);
            TabStop = false;
            AccessibleName = tool.Name;
            AccessibleDescription = tool.Id;
            if (tool.Placeholder)
            {
                AccessibleDescription = tool.Id + " (placeholder)";
            }
        }

        public bool Busy { get { return _busy; } }

        public void ApplyTheme(Theme theme)
        {
            _theme = theme;
            BackColor = theme.ButtonBack;
            ForeColor = Tool.Danger ? theme.Danger : theme.ButtonText;
            FlatAppearance.BorderColor = theme.ButtonBorder;
            FlatAppearance.MouseOverBackColor = theme.ButtonHover;
            FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
            if (!Enabled) { ForeColor = theme.ButtonDisabledText; }
            Invalidate();
        }

        /// <summary>Busy = the button is running something. A short flash is used for the
        /// "feature not wired up yet" feedback so a click is never silent.</summary>
        public void SetBusy(bool busy, int autoClearMs)
        {
            _busy = busy;
            Enabled = !busy;
            Text = busy ? Tool.Name + "…" : Tool.Name;
            if (_theme != null && !busy) { ForeColor = Tool.Danger ? _theme.Danger : _theme.ButtonText; }
            if (_theme != null && busy) { ForeColor = _theme.ButtonDisabledText; }

            if (_flash != null) { _flash.Stop(); _flash.Dispose(); _flash = null; }
            if (busy && autoClearMs > 0)
            {
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _flash != null) { _flash.Dispose(); _flash = null; }
            base.Dispose(disposing);
        }
    }
}
