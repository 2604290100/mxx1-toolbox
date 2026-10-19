// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Globalization;

namespace Mxx1Toolbox
{
    /// <summary>「屏幕取色器」的换算部分：HEX / RGB / HSL 三种写法之间的互相转换。
    ///
    /// 窗口（PickForm）和命令行（`pick --decode=…`）**共用这一份** —— 和哈希校验同一条理由：
    /// 取色器把颜色写进剪贴板、命令行把同样的颜色念出来，两边的数字必须一模一样；要是两份代码，
    /// 改一处忘一处，用户拿它去对设计稿就会得出错误结论。
    ///
    /// `--decode` 接受这几种写法（大小写都不挑）：
    ///   #1A2B3C / 1a2b3c / #abc（三位简写）/ 0x1A2B3C
    ///   26,43,60 / 26, 43, 60 / rgb(26,43,60)
    /// 输出三行机器可读的 `hex=` / `rgb=` / `hsl=`，窗口上显示的和复制的是同一套数字。
    ///
    /// 取整口径：h 四舍五入到 0～359 度，s / l 四舍五入到 0～100（百分比）。
    /// 灰度色（r=g=b）的 h 记 0（HSL 里灰度没有色相，写 0 是惯例）。</summary>
    internal static class ColorPicker
    {
        /// <summary>三种复制格式（滚轮切换的顺序也是这个）。</summary>
        public static readonly string[] Modes = new string[] { "hex", "rgb", "hsl" };

        /// <summary>把用户输入的写法解成 r/g/b。失败时 error 里是一句人话。</summary>
        public static bool TryParse(string text, out int r, out int g, out int b, out string error)
        {
            r = g = b = 0;
            error = "";
            if (text == null || text.Trim().Length == 0)
            {
                error = "没有给颜色值";
                return false;
            }
            string s = text.Trim();

            // rgb(26,43,60)
            if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                int open = s.IndexOf('(');
                int close = s.LastIndexOf(')');
                if (open >= 0 && close > open)
                {
                    s = s.Substring(open + 1, close - open - 1);
                }
                else
                {
                    error = "rgb(…) 的括号没写全：" + text;
                    return false;
                }
            }

            if (s.IndexOf(',') >= 0)
            {
                string[] parts = s.Split(',');
                if (parts.Length != 3)
                {
                    error = "逗号分隔的写法要有三个数字（红,绿,蓝）：" + text;
                    return false;
                }
                int[] v = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i]))
                    {
                        error = "这个不是数字：" + parts[i].Trim();
                        return false;
                    }
                    if (v[i] < 0 || v[i] > 255)
                    {
                        error = "每个数字要在 0～255 之间：" + parts[i].Trim();
                        return false;
                    }
                }
                r = v[0];
                g = v[1];
                b = v[2];
                return true;
            }

            if (s.StartsWith("#", StringComparison.Ordinal)) { s = s.Substring(1); }
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { s = s.Substring(2); }
            s = s.Trim();
            if (s.Length == 3)
            {
                // #abc = #aabbcc（每一位重复一次）
                s = new string(new char[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            }
            if (s.Length != 6)
            {
                error = "十六进制颜色要是 3 位或者 6 位（例如 #1A2B3C）：" + text;
                return false;
            }
            int value;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                error = "里面有不是十六进制的字符（只能是 0-9 / A-F）：" + text;
                return false;
            }
            r = (value >> 16) & 0xFF;
            g = (value >> 8) & 0xFF;
            b = value & 0xFF;
            return true;
        }

        public static string ToHex(int r, int g, int b)
        {
            return "#" + r.ToString("X2", CultureInfo.InvariantCulture)
                + g.ToString("X2", CultureInfo.InvariantCulture)
                + b.ToString("X2", CultureInfo.InvariantCulture);
        }

        public static string ToRgb(int r, int g, int b)
        {
            return r.ToString(CultureInfo.InvariantCulture) + ","
                + g.ToString(CultureInfo.InvariantCulture) + ","
                + b.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>HSL 的机器可读写法：`210,40,17`（色相 0～359，饱和度 / 亮度是百分比）。</summary>
        public static string ToHsl(int r, int g, int b)
        {
            int h; int s; int l;
            ToHslParts(r, g, b, out h, out s, out l);
            return h.ToString(CultureInfo.InvariantCulture) + ","
                + s.ToString(CultureInfo.InvariantCulture) + ","
                + l.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>给人看的 HSL：`HSL(210, 40%, 17%)`。</summary>
        public static string ToHslText(int r, int g, int b)
        {
            int h; int s; int l;
            ToHslParts(r, g, b, out h, out s, out l);
            return "HSL(" + h.ToString(CultureInfo.InvariantCulture) + ", "
                + s.ToString(CultureInfo.InvariantCulture) + "%, "
                + l.ToString(CultureInfo.InvariantCulture) + "%)";
        }

        /// <summary>滚轮切换后的那一档：hex / rgb / hsl 换成剪贴板里真正要粘出去的样子
        /// （rgb 写成 `rgb(26, 43, 60)`、hsl 写成 `hsl(210, 40%, 17%)`，粘进 CSS 就能用）。</summary>
        public static string CopyText(int r, int g, int b, string mode)
        {
            string m = (mode == null) ? "hex" : mode.Trim().ToLowerInvariant();
            if (m == "rgb") { return "rgb(" + r.ToString(CultureInfo.InvariantCulture) + ", "
                + g.ToString(CultureInfo.InvariantCulture) + ", "
                + b.ToString(CultureInfo.InvariantCulture) + ")"; }
            if (m == "hsl")
            {
                int h; int s; int l;
                ToHslParts(r, g, b, out h, out s, out l);
                return "hsl(" + h.ToString(CultureInfo.InvariantCulture) + ", "
                    + s.ToString(CultureInfo.InvariantCulture) + "%, "
                    + l.ToString(CultureInfo.InvariantCulture) + "%)";
            }
            return ToHex(r, g, b);
        }

        private static void ToHslParts(int r, int g, int b, out int h, out int s, out int l)
        {
            double rn = r / 255.0;
            double gn = g / 255.0;
            double bn = b / 255.0;
            double max = Math.Max(rn, Math.Max(gn, bn));
            double min = Math.Min(rn, Math.Min(gn, bn));
            double delta = max - min;

            double ld = (max + min) / 2.0;
            double sd = 0.0;
            if (delta > 0.0000001)
            {
                sd = delta / (1.0 - Math.Abs(2.0 * ld - 1.0));
            }
            double hd = 0.0;
            if (delta > 0.0000001)
            {
                if (max == rn) { hd = ((gn - bn) / delta) % 6.0; }
                else if (max == gn) { hd = ((bn - rn) / delta) + 2.0; }
                else { hd = ((rn - gn) / delta) + 4.0; }
                hd *= 60.0;
                if (hd < 0) { hd += 360.0; }
            }

            h = (int)Math.Round(hd, MidpointRounding.AwayFromZero);
            if (h < 0) { h = 0; }
            if (h >= 360) { h = h % 360; }
            s = (int)Math.Round(sd * 100.0, MidpointRounding.AwayFromZero);
            l = (int)Math.Round(ld * 100.0, MidpointRounding.AwayFromZero);
            if (s < 0) { s = 0; }
            if (s > 100) { s = 100; }
            if (l < 0) { l = 0; }
            if (l > 100) { l = 100; }
        }
    }
}
