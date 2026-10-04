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
            Image = tool.Placeholder ? IconFactory.GetMuted(tool) : IconFactory.Get(tool);
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
        /// exactly the same canvas size and the text is never touched.</summary>
        public void SetBusy(bool busy, int autoClearMs)
        {
            _busy = busy;
            Enabled = !busy;
            // Text is deliberately NOT touched. Appending "…" widened the image+text group, and
            // because the group is centred the icon jumped sideways on every click; on the widest
            // labels the text even overflowed the button. The busy state is shown by this icon
            // (same 16x15 size, see IconFactory.Normalize) plus the disabled colours.
            if (busy) { Image = IconFactory.Busy(); }
            else if (Tool.Placeholder) { Image = IconFactory.GetMuted(Tool); }
            else { Image = IconFactory.Get(Tool); }
            ForeColor = CurrentTextColor();
            ArmFlash(busy ? autoClearMs : 0);
        }

        /// <summary>Click feedback for a button that has no feature behind it: briefly disabled
        /// (and therefore grey), but the icon is NOT swapped for the spinner -- nothing is running,
        /// so showing a spinner would be a lie.</summary>
        public void Flash(int ms)
        {
            if (_busy) { return; }
            _busy = true;
            Enabled = false;
            ForeColor = CurrentTextColor();
            ArmFlash(ms);
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
                if (Tool.Placeholder) { SetIdle(); } else { SetBusy(false, 0); }
            };
            _flash.Start();
        }

        /// <summary>Back to the normal (not busy, clickable) look.</summary>
        private void SetIdle()
        {
            _busy = false;
            Enabled = true;
            ForeColor = CurrentTextColor();
            if (_flash != null) { _flash.Stop(); _flash.Dispose(); _flash = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _flash != null) { _flash.Dispose(); _flash = null; }
            base.Dispose(disposing);
        }
    }
}
