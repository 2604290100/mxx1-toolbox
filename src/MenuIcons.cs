// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>右键菜单项的图标（.ico 文件）。
    ///
    /// 为什么要有这个类：注册表里的 `Icon` 值**只能指向"带图标资源的 exe / dll"或者一个 .ico 文件**，
    /// 指向 .png 是没用的（Windows 不认），指向一个自己也没图标资源的 exe 同样是空白
    /// —— 2026-10-04 用户报「加进去的右键功能没有图标」，根因就是这个：`Icon` 写的是
    /// Mxx1Toolbox.exe，而那个 exe 从来没有 `/win32icon`（assets\app.ico 不存在）。
    ///
    /// 做法：把**已经内嵌在 exe 里的按钮 PNG**（build.ps1 的 icons.&lt;id&gt;.png）在装菜单的时候
    /// 转成真正的多尺寸 .ico，写到 %LOCALAPPDATA%\mxx1-toolbox\rightmenu-icons\，注册表指向它。
    /// 好处：① 图标跟着 exe 走，exe 搬到哪台机器都能重新生成；② 不用往仓库里塞 .ico 二进制；
    /// ③ 生成的是 16/20/24/32 四个尺寸，Windows 在 125% / 150% DPI 下也挑得到合适的那张。
    ///
    /// 文件名里带源图的指纹（&lt;id&gt;.&lt;hash&gt;.ico），所以以后图标改一版，装一次菜单就换新的，
    /// 不会留着旧图不放。</summary>
    internal static class MenuIcons
    {
        /// <summary>生成的 .ico 放哪。放在用户目录下：工具箱所在目录可能是只读的（Program Files / U 盘）。
        ///
        /// **按注册表根分开**（2026-10-04 实测踩到）：回归测试装到 `MXX1_RIGHTMENU_ROOT` 指的隔离根里，
        /// 撤掉时会调 `RemoveAll()` —— 如果两边共用同一个目录，**测试的卸载会把用户真实那份菜单还在
        /// 引用的 .ico 一起删掉**（菜单图标变空白）。所以测试根用 `rightmenu-icons-test`。</summary>
        public static string Dir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("MXX1_RIGHTMENU_ROOT");
                bool test = (env != null && env.Trim().Length > 0);
                return Path.Combine(AppPaths.BaseDir, test ? "rightmenu-icons-test" : "rightmenu-icons");
            }
        }

        /// <summary>生成（或复用）某个按钮的 .ico，返回完整路径；取不到源图就返回空
        /// —— 空的意思就是"这一项不写 Icon 值"，绝不写一个指不到东西的路径进去。</summary>
        public static string IcoFor(string id)
        {
            if (id == null || id.Trim().Length == 0) { return ""; }
            string safe = SafeName(id.Trim());
            if (safe.Length == 0) { return ""; }
            try
            {
                byte[] png = SourcePng(safe);
                if (png == null || png.Length == 0) { return ""; }

                string name = safe + "." + Fingerprint(png) + ".ico";
                Directory.CreateDirectory(Dir);
                string path = Path.Combine(Dir, name);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    File.WriteAllBytes(path, BuildIco(png, new int[] { 16, 20, 24, 32 }));
                }
                CleanOld(safe, name);
                return path;
            }
            catch { return ""; }
        }

        /// <summary>两个项都撤掉之后清理生成物（留着一个空目录也无所谓，删干净点）。</summary>
        public static void RemoveAll()
        {
            try
            {
                if (Directory.Exists(Dir)) { Directory.Delete(Dir, true); }
            }
            catch { }
        }

        /// <summary>同一个 id 只留最新的那一份（指纹变了的话旧的删掉）。</summary>
        private static void CleanOld(string safe, string keep)
        {
            try
            {
                foreach (string f in Directory.GetFiles(Dir, safe + ".*.ico"))
                {
                    if (string.Equals(Path.GetFileName(f), keep, StringComparison.OrdinalIgnoreCase)) { continue; }
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        /// <summary>按钮 id → 文件名（id 都是小写字母数字点，这里是兜底）。</summary>
        private static string SafeName(string id)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in id)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') { sb.Append(c); }
            }
            return sb.ToString();
        }

        /// <summary>源 PNG：先找内嵌资源（正式路径，exe 单文件自足），再找 exe 旁边的 assets\icons\（开发时）。</summary>
        private static byte[] SourcePng(string safe)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("icons." + safe + ".png"))
                {
                    if (s != null)
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            byte[] buf = new byte[8192];
                            int n;
                            while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); }
                            return ms.ToArray();
                        }
                    }
                }
            }
            catch { }
            try
            {
                string onDisk = Path.Combine(AppPaths.IconsDir, safe + ".png");
                if (File.Exists(onDisk)) { return File.ReadAllBytes(onDisk); }
            }
            catch { }
            return null;
        }

        /// <summary>源图的短指纹（FNV-1a 的 32 位，写成 8 位十六进制）。</summary>
        private static string Fingerprint(byte[] data)
        {
            uint h = 2166136261u;
            foreach (byte b in data)
            {
                h = h ^ b;
                h = h * 16777619u;
            }
            return h.ToString("x8", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ ICO 拼装

        /// <summary>一个 .ico = 头 + N 个目录项 + N 张图（这里是 32 位 BMP，不压缩、不依赖 PNG-in-ICO）。</summary>
        private static byte[] BuildIco(byte[] png, int[] sizes)
        {
            // 注意：GDI+ 要求 Image.FromStream 的那个流**在图片用完之后**才能关，
            // 所以这里不用 using 包住流再往外用图片（那会得到"参数无效"）。
            using (MemoryStream ms = new MemoryStream(png))
            using (Image src = Image.FromStream(ms))
            {
                List<byte[]> images = new List<byte[]>();
                List<int> used = new List<int>();
                foreach (int s in sizes)
                {
                    if (s <= 0 || s > 256) { continue; }
                    images.Add(BuildDib(src, s));
                    used.Add(s);
                }
                if (images.Count == 0) { return new byte[0]; }

                using (MemoryStream outMs = new MemoryStream())
                using (BinaryWriter w = new BinaryWriter(outMs))
                {
                    w.Write((short)0);                 // reserved
                    w.Write((short)1);                 // type = icon
                    w.Write((short)images.Count);
                    int offset = 6 + 16 * images.Count;
                    for (int i = 0; i < images.Count; i++)
                    {
                        int s = used[i];
                        w.Write((byte)((s >= 256) ? 0 : s));   // 宽（256 写 0）
                        w.Write((byte)((s >= 256) ? 0 : s));   // 高
                        w.Write((byte)0);                      // 调色板颜色数
                        w.Write((byte)0);                      // 保留
                        w.Write((short)1);                     // 平面数
                        w.Write((short)32);                    // 位深
                        w.Write(images[i].Length);
                        w.Write(offset);
                        offset += images[i].Length;
                    }
                    foreach (byte[] img in images) { w.Write(img); }
                    w.Flush();
                    return outMs.ToArray();
                }
            }
        }

        /// <summary>一张 32 位 DIB：BITMAPINFOHEADER + 自下而上的 BGRA + 全 0 的 AND 掩码
        /// （透明度由 alpha 通道表达，掩码只是格式要求）。</summary>
        private static byte[] BuildDib(Image src, int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, size, size));
                }

                int row = size * 4;
                byte[] xor = new byte[row * size];
                BitmapData d = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < size; y++)
                    {
                        // DIB 是自下而上存的：第 0 行写的是图的最下面一行。
                        IntPtr p = new IntPtr(d.Scan0.ToInt64() + (long)(size - 1 - y) * d.Stride);
                        Marshal.Copy(p, xor, y * row, row);
                    }
                }
                finally { bmp.UnlockBits(d); }

                int maskRow = ((size + 31) / 32) * 4;
                byte[] and = new byte[maskRow * size];

                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter w = new BinaryWriter(ms))
                {
                    w.Write(40);                  // biSize
                    w.Write(size);                // biWidth
                    w.Write(size * 2);            // biHeight = XOR + AND
                    w.Write((short)1);            // biPlanes
                    w.Write((short)32);           // biBitCount
                    w.Write(0);                   // biCompression = BI_RGB
                    w.Write(xor.Length + and.Length);
                    w.Write(0);                   // 横向 DPI
                    w.Write(0);                   // 纵向 DPI
                    w.Write(0);                   // 调色板
                    w.Write(0);
                    w.Write(xor);
                    w.Write(and);
                    w.Flush();
                    return ms.ToArray();
                }
            }
            finally { bmp.Dispose(); }
        }
    }
}
