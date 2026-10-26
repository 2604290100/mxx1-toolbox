// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>「去掉小箭头」用的那张空白图标（2026-10-26 起的做法）。
    ///
    /// 为什么不再借 shell32.dll,-50：那是 Windows 自带的"全透明空图标"，本机实测它的像素
    /// 确实全是透明的（ExtractIconEx 出来 32×32、不透明像素 0 个），可一旦被写进
    /// `Shell Icons\29`，Shell 画叠加图标时画出来的是一块**黑方块** —— 用户 2026-10-24 报的
    /// 「点了去掉小箭头，桌面图标全变黑块」就是它。所以改成工具箱自己生成一张真正空白的 ICO：
    ///
    ///   **1 位色（1bpp）+ AND 掩码全 0xFF**
    ///
    /// 透明是这样说出来的：图标里的 AND 掩码位 = 1 表示"这块保持屏幕原样"，整张掩码全是 1
    /// 就一个像素都不会画；XOR 位全是 0，所以那条"先盖黑再打掩码"的老路也只盖黑 + 全透明。
    /// 它**不依赖 alpha 通道** —— 黑块那件事恰恰是"像素 alpha 全 0、但掩码是全不透明"撞上
    /// 一条不看 alpha 的渲染路径造成的（坑 101）。
    ///
    /// 三个尺寸（16 / 32 / 48）：Shell 按当前视图挑它要的那个。
    /// 文件位置见 AppPaths.BlankIconFile（默认 %LOCALAPPDATA%\mxx1-toolbox\blank-arrow.ico，
    /// `MXX1_BLANK_ICON` 可以换掉，回归测试指向临时目录）。
    /// ⚠️ 注册表里存的是**这个文件的完整路径**，所以别随手删它：删了"隐藏小箭头"就失效
    /// （桌面会回到系统默认的小箭头，不影响别的任何东西）。</summary>
    internal static class BlankIcon
    {
        /// <summary>这张图放在哪（写进注册表的就是这个路径）。</summary>
        public static string Path { get { return AppPaths.BlankIconFile; } }

        /// <summary>ICO 里带的三个尺寸。</summary>
        private static readonly int[] Sizes = new int[] { 16, 32, 48 };

        /// <summary>确保硬盘上有那张空白图标，并且它的字节就是我们认得的那份。
        /// 已经有、而且字节一模一样 → 不动它（不白白改文件时间）。失败返回 false + 原因，
        /// 调用方**一个字节的注册表都不要写**（写一个指向不存在文件的路径，桌面只会更难看）。</summary>
        public static bool Ensure(out string path, out string error)
        {
            path = Path;
            error = "";
            byte[] want = Build();
            try
            {
                if (File.Exists(path))
                {
                    byte[] have = null;
                    try { have = File.ReadAllBytes(path); }
                    catch { have = null; }
                    if (have != null && Same(have, want)) { return true; }
                }
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }
                File.WriteAllBytes(path, want);
                byte[] back = File.ReadAllBytes(path);
                if (!Same(back, want))
                {
                    error = "写完之后读回来对不上（" + back.Length.ToString() + " 字节，应该是 "
                        + want.Length.ToString() + " 字节）";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>这个注册表值指向的是不是我们这张空白图标（吃 `路径` 和 `路径,0` 两种写法，
        /// 并且会展开 %VAR%）。「查看设置改动」靠它认出"小箭头是隐藏状态"。</summary>
        public static bool IsOurs(string value)
        {
            if (value == null) { return false; }
            string v = value.Trim().Trim('"');
            if (v.Length == 0) { return false; }
            string mine = Path;
            if (SamePath(v, mine)) { return true; }
            try { v = Environment.ExpandEnvironmentVariables(v); }
            catch { }
            if (SamePath(v, mine)) { return true; }
            int comma = v.LastIndexOf(',');
            if (comma > 0 && comma < v.Length - 1)
            {
                string head = v.Substring(0, comma).Trim();
                if (SamePath(head, mine)) { return true; }
            }
            return false;
        }

        /// <summary>一张合格的空图标长什么样：`sizes=16/32/48`、`bpp=1`、掩码全 1、像素全 0。</summary>
        public static bool Validate(byte[] data, out string sizes, out string error)
        {
            sizes = "";
            error = "";
            if (data == null || data.Length < 6 + 16) { error = "文件太短，连 ICO 头都不完整"; return false; }
            if (Get16(data, 0) != 0) { error = "ICO 头第一个字段应该是 0"; return false; }
            if (Get16(data, 2) != 1) { error = "这不是 ICO（类型字段应该是 1）"; return false; }
            int count = Get16(data, 4);
            if (count <= 0) { error = "ICO 里一张图都没有"; return false; }
            if (data.Length < 6 + 16 * count) { error = "ICO 目录被截断了"; return false; }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                int e = 6 + 16 * i;
                int w = data[e];
                int h = data[e + 1];
                if (w == 0 || h == 0) { error = "第 " + (i + 1).ToString() + " 张图标写的是 256，这里不该出现"; return false; }
                if (w != h) { error = "第 " + (i + 1).ToString() + " 张图标不是正方形"; return false; }
                int planes = Get16(data, e + 4);
                int bpp = Get16(data, e + 6);
                if (planes != 1) { error = "第 " + (i + 1).ToString() + " 张图标的 planes 不是 1"; return false; }
                if (bpp != 1) { error = "第 " + (i + 1).ToString() + " 张图标不是 1 位色（bpp=" + bpp.ToString() + "）"; return false; }
                long size = Get32(data, e + 8);
                long off = Get32(data, e + 12);
                if (off < 0 || size <= 0 || off + size > data.Length)
                {
                    error = "第 " + (i + 1).ToString() + " 张图标的偏移 / 长度越界";
                    return false;
                }
                int o = (int)off;
                if (Get32(data, o) != 40) { error = "第 " + (i + 1).ToString() + " 张图标的 BITMAPINFOHEADER 不是 40 字节"; return false; }
                if (Get32(data, o + 4) != w) { error = "第 " + (i + 1).ToString() + " 张图标头里的宽度和目录里写的不一致"; return false; }
                if (Get32(data, o + 8) != (long)h * 2) { error = "第 " + (i + 1).ToString() + " 张图标头的高度应该是目录里的两倍（XOR + AND）"; return false; }
                if (Get16(data, o + 12) != 1) { error = "第 " + (i + 1).ToString() + " 张图标头的 planes 不是 1"; return false; }
                if (Get16(data, o + 14) != 1) { error = "第 " + (i + 1).ToString() + " 张图标头的位深不是 1"; return false; }
                if (Get32(data, o + 16) != 0) { error = "第 " + (i + 1).ToString() + " 张图标头写了压缩方式（应该是 0）"; return false; }
                long clrUsed = Get32(data, o + 32);
                if (clrUsed > 2) { error = "第 " + (i + 1).ToString() + " 张图标用了超过 2 个调色板项"; return false; }

                int stride = ((w + 31) / 32) * 4;      // 1 位色：每行按 32 位对齐
                int xorLen = stride * h;
                int andStart = o + 40 + 8 + xorLen;     // 头部 + 两个调色板项
                int end = o + (int)size;
                if (andStart + xorLen > end) { error = "第 " + (i + 1).ToString() + " 张图标不够长（放不下 XOR + AND）"; return false; }
                for (int k = 0; k < xorLen; k++)
                {
                    if (data[o + 48 + k] != 0) { error = "第 " + (i + 1).ToString() + " 张图标的像素不是全 0"; return false; }
                }
                for (int k = 0; k < xorLen; k++)
                {
                    if (data[andStart + k] != 0xFF) { error = "第 " + (i + 1).ToString() + " 张图标的掩码不是全 1（会有像素被画出来）"; return false; }
                }
                if (sb.Length > 0) { sb.Append('/'); }
                sb.Append(w.ToString());
            }
            sizes = sb.ToString();
            return true;
        }

        /// <summary>生成那张 ICO 的字节（3 个尺寸，每个都是 1bpp + 掩码全 0xFF）。</summary>
        public static byte[] Build()
        {
            List<byte[]> images = new List<byte[]>();
            int total = 6 + 16 * Sizes.Length;
            foreach (int s in Sizes)
            {
                byte[] img = BuildImage(s);
                images.Add(img);
                total += img.Length;
            }
            byte[] data = new byte[total];
            int p = 0;
            Put16(data, ref p, 0);                 // reserved
            Put16(data, ref p, 1);                 // type = icon
            Put16(data, ref p, Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                data[p++] = (byte)Sizes[i];        // 宽（0 才表示 256，这里不会）
                data[p++] = (byte)Sizes[i];        // 高
                data[p++] = 2;                     // 调色板颜色数（1 位色 = 2）
                data[p++] = 0;                     // reserved
                Put16(data, ref p, 1);             // planes
                Put16(data, ref p, 1);             // bit count
                Put32(data, ref p, (uint)images[i].Length);
                Put32(data, ref p, (uint)offset);
                offset += images[i].Length;
            }
            foreach (byte[] img in images)
            {
                Buffer.BlockCopy(img, 0, data, p, img.Length);
                p += img.Length;
            }
            return data;
        }

        private static byte[] BuildImage(int size)
        {
            int stride = ((size + 31) / 32) * 4;
            int xorLen = stride * size;
            int andLen = stride * size;
            byte[] img = new byte[40 + 8 + xorLen + andLen];
            int p = 0;
            Put32(img, ref p, 40);                       // biSize
            Put32(img, ref p, (uint)size);               // biWidth
            Put32(img, ref p, (uint)(size * 2));         // biHeight = XOR + AND 两半
            Put16(img, ref p, 1);                        // biPlanes
            Put16(img, ref p, 1);                        // biBitCount = 1
            Put32(img, ref p, 0);                        // biCompression = BI_RGB
            Put32(img, ref p, (uint)(xorLen + andLen));  // biSizeImage
            Put32(img, ref p, 0);                        // biXPelsPerMeter
            Put32(img, ref p, 0);                        // biYPelsPerMeter
            Put32(img, ref p, 2);                        // biClrUsed
            Put32(img, ref p, 0);                        // biClrImportant
            // 两个调色板项（黑 / 白）：XOR 位全是 0，所以实际上画不到它们，写了只是为了让文件合法。
            for (int i = 0; i < 8; i++) { img[p++] = 0; }
            // XOR 位图：全 0（数组本来就是 0）。
            // AND 掩码：全 1 = 这块保持屏幕原样（= 全透明）。
            int andStart = 40 + 8 + xorLen;
            for (int i = 0; i < andLen; i++) { img[andStart + i] = 0xFF; }
            return img;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null) { return false; }
            if (a.Length != b.Length) { return false; }
            for (int i = 0; i < a.Length; i++) { if (a[i] != b[i]) { return false; } }
            return true;
        }

        private static bool SamePath(string a, string b)
        {
            if (a == null || b == null) { return false; }
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static void Put16(byte[] b, ref int p, int v)
        {
            b[p++] = (byte)(v & 0xFF);
            b[p++] = (byte)((v >> 8) & 0xFF);
        }

        private static void Put32(byte[] b, ref int p, uint v)
        {
            b[p++] = (byte)(v & 0xFF);
            b[p++] = (byte)((v >> 8) & 0xFF);
            b[p++] = (byte)((v >> 16) & 0xFF);
            b[p++] = (byte)((v >> 24) & 0xFF);
        }

        private static int Get16(byte[] b, int p)
        {
            return b[p] | (b[p + 1] << 8);
        }

        private static long Get32(byte[] b, int p)
        {
            return (long)((uint)b[p] | ((uint)b[p + 1] << 8) | ((uint)b[p + 2] << 16) | ((uint)b[p + 3] << 24));
        }
    }
}
