// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System.Drawing;

namespace Mxx1Toolbox
{
    /// <summary>Colour sets for the two themes. Dark mode needs no owner-drawn control:
    /// FlatStyle.Flat plus FlatAppearance is enough for WinForms buttons.</summary>
    internal sealed class Theme
    {
        public bool DarkMode;

        public Color FormBack;
        public Color SegmentLine;
        public Color TabBack;
        public Color TabText;
        public Color TabActiveBack;
        public Color TabActiveText;
        public Color TabActiveUnderline;

        public Color ButtonBack;
        public Color ButtonBorder;
        public Color ButtonText;
        public Color ButtonHover;
        public Color ButtonHoverBorder;
        public Color ButtonPressed;
        public Color ButtonDisabledText;
        public Color Danger;          // text colour of a dangerous button

        // A button whose feature is not wired up yet is drawn muted (grey label, grey label
        // colour, no coloured icon). It is still clickable: clicking explains what is missing.
        public Color PlaceholderBack;
        public Color PlaceholderBorder;
        public Color PlaceholderText;
        public Color PlaceholderHover;
        public Color PlaceholderPressed;

        public Color BarBack;
        public Color BarText;
        public Color InputBack;
        public Color InputText;
        public Color InputBorder;
        public Color LogBack;
        public Color LogText;

        public static Theme Light()
        {
            Theme t = new Theme();
            t.DarkMode = false;
            t.FormBack = Color.FromArgb(0xF0, 0xF0, 0xF0);
            t.SegmentLine = Color.FromArgb(0xD0, 0xD0, 0xD0);
            t.TabBack = Color.FromArgb(0xE4, 0xE4, 0xE4);
            t.TabText = Color.FromArgb(0x33, 0x33, 0x33);
            t.TabActiveBack = Color.White;
            t.TabActiveText = Color.FromArgb(0x11, 0x11, 0x11);
            t.TabActiveUnderline = Color.FromArgb(0x00, 0x78, 0xD7);

            t.ButtonBack = Color.FromArgb(0xF5, 0xF5, 0xF5);
            t.ButtonBorder = Color.FromArgb(0xAD, 0xAD, 0xAD);
            t.ButtonText = Color.FromArgb(0x1A, 0x1A, 0x1A);
            t.ButtonHover = Color.FromArgb(0xE5, 0xF1, 0xFB);
            t.ButtonHoverBorder = Color.FromArgb(0x00, 0x78, 0xD7);
            t.ButtonPressed = Color.FromArgb(0xCC, 0xE4, 0xF7);
            t.ButtonDisabledText = Color.FromArgb(0x9A, 0x9A, 0x9A);
            t.Danger = Color.FromArgb(0xB0, 0x00, 0x20);

            t.PlaceholderBack = Color.FromArgb(0xEA, 0xEA, 0xEA);
            t.PlaceholderBorder = Color.FromArgb(0xC8, 0xC8, 0xC8);
            t.PlaceholderText = Color.FromArgb(0x8A, 0x8A, 0x8A);
            t.PlaceholderHover = Color.FromArgb(0xE0, 0xE0, 0xE0);
            t.PlaceholderPressed = Color.FromArgb(0xD6, 0xD6, 0xD6);

            t.BarBack = Color.FromArgb(0xE8, 0xE8, 0xE8);
            t.BarText = Color.FromArgb(0x33, 0x33, 0x33);
            t.InputBack = Color.White;
            t.InputText = Color.FromArgb(0x1A, 0x1A, 0x1A);
            t.InputBorder = Color.FromArgb(0xAD, 0xAD, 0xAD);
            t.LogBack = Color.White;
            t.LogText = Color.FromArgb(0x22, 0x22, 0x22);
            return t;
        }

        public static Theme Dark()
        {
            Theme t = new Theme();
            t.DarkMode = true;
            t.FormBack = Color.FromArgb(0x20, 0x20, 0x20);
            t.SegmentLine = Color.FromArgb(0x3F, 0x3F, 0x46);
            t.TabBack = Color.FromArgb(0x2B, 0x2B, 0x2B);
            t.TabText = Color.FromArgb(0xC8, 0xC8, 0xC8);
            t.TabActiveBack = Color.FromArgb(0x37, 0x37, 0x3D);
            t.TabActiveText = Color.White;
            t.TabActiveUnderline = Color.FromArgb(0x4C, 0xA0, 0xE8);

            t.ButtonBack = Color.FromArgb(0x2D, 0x2D, 0x30);
            t.ButtonBorder = Color.FromArgb(0x3F, 0x3F, 0x46);
            t.ButtonText = Color.FromArgb(0xF0, 0xF0, 0xF0);
            t.ButtonHover = Color.FromArgb(0x09, 0x47, 0x71);
            t.ButtonHoverBorder = Color.FromArgb(0x00, 0x78, 0xD7);
            t.ButtonPressed = Color.FromArgb(0x0E, 0x5A, 0x94);
            t.ButtonDisabledText = Color.FromArgb(0x76, 0x76, 0x76);
            t.Danger = Color.FromArgb(0xFF, 0x6B, 0x6B);

            t.PlaceholderBack = Color.FromArgb(0x26, 0x26, 0x29);
            t.PlaceholderBorder = Color.FromArgb(0x35, 0x35, 0x3A);
            t.PlaceholderText = Color.FromArgb(0x7C, 0x7C, 0x82);
            t.PlaceholderHover = Color.FromArgb(0x2E, 0x2E, 0x33);
            t.PlaceholderPressed = Color.FromArgb(0x35, 0x35, 0x3B);

            t.BarBack = Color.FromArgb(0x1B, 0x1B, 0x1B);
            t.BarText = Color.FromArgb(0xD0, 0xD0, 0xD0);
            t.InputBack = Color.FromArgb(0x2D, 0x2D, 0x30);
            t.InputText = Color.FromArgb(0xF0, 0xF0, 0xF0);
            t.InputBorder = Color.FromArgb(0x5A, 0x5A, 0x5A);
            t.LogBack = Color.FromArgb(0x18, 0x18, 0x18);
            t.LogText = Color.FromArgb(0xD8, 0xD8, 0xD8);
            return t;
        }

        public static Theme Resolve(string mode)
        {
            return Settings.ResolveTheme(mode) == Settings.ThemeDark ? Dark() : Light();
        }
    }
}
