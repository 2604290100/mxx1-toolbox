// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Mxx1Toolbox
{
    /// <summary>User settings, stored as a tiny key=value ini next to the logs.
    /// A missing or damaged file always falls back to sane defaults.</summary>
    internal sealed class Settings
    {
        public const string ThemeLight = "light";
        public const string ThemeDark = "dark";
        public const string ThemeSystem = "system";

        public string Theme = ThemeLight;
        public string ClickMode = "single";          // single | double
        public bool ConfirmDangerous = true;
        public bool HideConsole = true;
        public bool ShowLogPanel = false;
        public int LogKeepDays = 30;
        public string PermanentDeleteExe = "";       // empty = auto locate at run time

        /// <summary>窗口几何。X/Y = -1 表示没记过（第一次打开居中）；宽/高 = 0 表示没记过。
        /// **默认固定尺寸**（用户 2026-10-04 定的：固定比自适应好，宽度也不用跟着内容变）；
        /// 勾上 WindowAutoSize 之后高度才跟着当前页签的按钮走。</summary>
        public int WindowX = -1;
        public int WindowY = -1;
        public int WindowWidth = 0;
        public int WindowHeight = 0;
        public bool WindowAutoSize = false;

        /// <summary>上次停留的页签 id（"" = 没记过，按"常用页有没有东西"挑一个默认页）。</summary>
        public string LastTab = "";

        public static string ThemeDisplay(string mode)
        {
            switch (mode)
            {
                case ThemeDark: return "深色";
                case ThemeSystem: return "跟随系统";
                default: return "浅色";
            }
        }

        /// <summary>Turns the configured mode (which may be "system") into "light" or "dark".</summary>
        public static string ResolveTheme(string mode)
        {
            if (string.Equals(mode, ThemeDark, StringComparison.OrdinalIgnoreCase)) { return ThemeDark; }
            if (string.Equals(mode, ThemeSystem, StringComparison.OrdinalIgnoreCase))
            {
                return SystemUsesLightTheme() ? ThemeLight : ThemeDark;
            }
            return ThemeLight;
        }

        public bool IsDark
        {
            get { return ResolveTheme(Theme) == ThemeDark; }
        }

        public static bool SystemUsesLightTheme()
        {
            try
            {
                object v = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                if (v is int) { return ((int)v) != 0; }
            }
            catch { }
            return true;
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(AppPaths.SettingsIni)) { return s; }
                foreach (string raw in File.ReadAllLines(AppPaths.SettingsIni, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) { continue; }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) { continue; }
                    s.SetValue(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
                }
            }
            catch
            {
                // unreadable settings fall back to defaults
            }
            return s;
        }

        private void SetValue(string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "theme": Theme = Normalize(value); break;
                case "clickmode": ClickMode = (value.ToLowerInvariant() == "double") ? "double" : "single"; break;
                case "confirmdangerous": ConfirmDangerous = ToBool(value, true); break;
                case "hideconsole": HideConsole = ToBool(value, true); break;
                case "showlogpanel": ShowLogPanel = ToBool(value, false); break;
                case "logkeepdays":
                    int days;
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out days) && days >= 0) { LogKeepDays = days; }
                    break;
                case "permanentdeleteexe": PermanentDeleteExe = value; break;
                case "windowx": WindowX = ToInt(value, -1); break;
                case "windowy": WindowY = ToInt(value, -1); break;
                case "windowwidth": WindowWidth = ToInt(value, 0); break;
                case "windowheight": WindowHeight = ToInt(value, 0); break;
                case "windowautosize": WindowAutoSize = ToBool(value, true); break;
                case "lasttab": LastTab = value; break;
            }
        }

        private static int ToInt(string value, int fallback)
        {
            int n;
            if (int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { return n; }
            return fallback;
        }

        private static string Normalize(string value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            if (v == ThemeDark || v == ThemeSystem) { return v; }
            return ThemeLight;
        }

        private static bool ToBool(string value, bool fallback)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") { return true; }
            if (v == "0" || v == "false" || v == "no" || v == "off") { return false; }
            return fallback;
        }

        public void Save()
        {
            try
            {
                AppPaths.EnsureBase();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 萌新工具箱设置（UTF-8 无 BOM）");
                sb.AppendLine("Theme=" + Theme);
                sb.AppendLine("ClickMode=" + ClickMode);
                sb.AppendLine("ConfirmDangerous=" + (ConfirmDangerous ? "1" : "0"));
                sb.AppendLine("HideConsole=" + (HideConsole ? "1" : "0"));
                sb.AppendLine("ShowLogPanel=" + (ShowLogPanel ? "1" : "0"));
                sb.AppendLine("LogKeepDays=" + LogKeepDays.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("PermanentDeleteExe=" + PermanentDeleteExe);
                sb.AppendLine("WindowX=" + WindowX.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("WindowY=" + WindowY.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("WindowWidth=" + WindowWidth.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("WindowHeight=" + WindowHeight.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("WindowAutoSize=" + (WindowAutoSize ? "1" : "0"));
                sb.AppendLine("LastTab=" + LastTab);
                File.WriteAllText(AppPaths.SettingsIni, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // a read-only profile must not crash the app
            }
        }
    }
}
