// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Mxx1Toolbox
{
    /// <summary>一张 32 位 BGRA 的位图 —— **像素拿在自己手里**（不依赖 GDI+ 的句柄）。
    ///
    /// 为什么要它：GDI+ 能写出来的格式只有 BMP / JPG / PNG / GIF / TIFF 五种
    /// （2026-10-15 在真机上用 `ImageCodecInfo.GetImageEncoders()` 数过，见 DESIGN §12.70），
    /// 而「图片转换」还要 ICO / TGA / DDS 三种 —— 那三种是**自己按格式规范写文件**的，
    /// 先在这张统一的中间图里把像素理清楚，写的时候各自按格式要求翻转 / 打乱即可。
    ///
    /// 约定（写每种格式时都靠它）：
    ///   · **自上而下**（第 0 行是最上面那行）；
    ///   · 每行 `Width * 4` 字节，顺序 **B, G, R, A** —— 和 Windows 的 `Format32bppArgb`
    ///     在内存里的排列一模一样（`LockBits` 拿到什么就是什么，不做任何搬运）；
    ///   · **非预乘**（straight alpha）：`Format32bppPArgb` 那种预乘过的要转，
    ///     否则半透明像素写出去颜色会发黑。</summary>
    internal sealed class RawImage
    {
        public int Width;
        public int Height;
        public byte[] Pixels;

        public RawImage(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public int MaxSide { get { return (Width > Height) ? Width : Height; } }
        public int MinSide { get { return (Width < Height) ? Width : Height; } }

        /// <summary>有没有半透明 / 全透明的像素（决定"要不要压白底"和卡片上那句实话）。</summary>
        public bool HasTransparency
        {
            get
            {
                for (int i = 3; i < Pixels.Length; i += 4)
                {
                    if (Pixels[i] != 255) { return true; }
                }
                return false;
            }
        }

        /// <summary>把 GDI+ 的位图搬进来。**统一走一遍"画到 32bppArgb 的新图上"**：
        /// 索引色（GIF）、24 位、预乘这些格式的差异就都在这一步被抹平了。</summary>
        public static RawImage FromBitmap(Bitmap source)
        {
            if (source == null) { throw new ArgumentNullException("source"); }
            int w = source.Width;
            int h = source.Height;
            using (Bitmap copy = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(copy))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.Clear(Color.Transparent);
                    g.DrawImage(source, new Rectangle(0, 0, w, h));
                }
                return FromArgbBitmap(copy);
            }
        }

        /// <summary>从一张**已经是 32bppArgb** 的位图上取像素（不做转换，直接 LockBits）。</summary>
        public static RawImage FromArgbBitmap(Bitmap argb)
        {
            RawImage r = new RawImage(argb.Width, argb.Height);
            BitmapData d = argb.LockBits(new Rectangle(0, 0, argb.Width, argb.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < argb.Height; y++)
                {
                    IntPtr row = new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride);
                    Marshal.Copy(row, r.Pixels, y * argb.Width * 4, argb.Width * 4);
                }
            }
            finally { argb.UnlockBits(d); }
            return r;
        }

        /// <summary>变回 GDI+ 的位图（存 PNG / JPG 那几种用 GDI+ 编码器时走这里）。</summary>
        public Bitmap ToBitmap()
        {
            Bitmap b = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            BitmapData d = b.LockBits(new Rectangle(0, 0, Width, Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < Height; y++)
                {
                    IntPtr row = new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride);
                    Marshal.Copy(Pixels, y * Width * 4, row, Width * 4);
                }
            }
            finally { b.UnlockBits(d); }
            return b;
        }

        /// <summary>缩放到指定大小（高质量插值）。**半透明像素的 alpha 也一起缩** ——
        /// 这是拿 GDI+ 画一遍做的，比自己算采样准。</summary>
        public RawImage ScaledTo(int width, int height)
        {
            using (Bitmap src = ToBitmap())
            using (Bitmap dst = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(dst))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(src, new Rectangle(0, 0, width, height));
                }
                return FromArgbBitmap(dst);
            }
        }

        /// <summary>缩成一张 size×size 的方图（等比、居中、四周补透明）—— 写 ICO 每一档用它。
        ///
        /// **只缩不放**：源的最大边比 size 还小就返回 null，上层跳过这一档 ——
        /// 「把 48×48 放大成 256×256 填进去」比"没有这一档"更糟
        /// （实测过：ImageMagick 的 `-define icon:auto-resize=256,...` 就是这么干的，
        /// 出来的 256 帧是拿 48 的放大糊出来的，见 DESIGN §12.70）。
        /// 非正方形的源走"等比缩小 + 居中留透明边"，不裁切、不拉变形。</summary>
        public RawImage SquareAt(int size)
        {
            if (size < 1) { return null; }
            if (MaxSide < size) { return null; }
            double k = (double)size / (double)MaxSide;
            int w = (int)Math.Round(Width * k);
            int h = (int)Math.Round(Height * k);
            if (w < 1) { w = 1; }
            if (h < 1) { h = 1; }
            RawImage scaled = ((w == Width) && (h == Height)) ? this : ScaledTo(w, h);
            return scaled.Place(size, size);
        }

        /// <summary>把这张图原样放到 width×height 的画布上（居中，四周透明）。
        /// 已经正好那么大就返回自己。</summary>
        public RawImage Place(int width, int height)
        {
            if (Width == width && Height == height) { return this; }
            RawImage outImg = new RawImage(width, height);
            int dx = (width - Width) / 2;
            int dy = (height - Height) / 2;
            for (int y = 0; y < Height; y++)
            {
                int ty = y + dy;
                if (ty < 0 || ty >= height) { continue; }
                for (int x = 0; x < Width; x++)
                {
                    int tx = x + dx;
                    if (tx < 0 || tx >= width) { continue; }
                    int s = (y * Width + x) * 4;
                    int t = (ty * width + tx) * 4;
                    outImg.Pixels[t] = Pixels[s];
                    outImg.Pixels[t + 1] = Pixels[s + 1];
                    outImg.Pixels[t + 2] = Pixels[s + 2];
                    outImg.Pixels[t + 3] = Pixels[s + 3];
                }
            }
            return outImg;
        }
    }

    /// <summary>手写 **ICO**（也叫 Windows 图标文件）。
    ///
    /// 为什么要自己写：本机 GDI+ 的编码器里**没有 ICO**（只有 BMP / JPG / PNG / GIF / TIFF），
    /// 所以「转成 ICO」和「提取图标」两条路都得自己按格式拼字节。格式本身很简单：
    ///   ICONDIR(6 字节) + ICONDIRENTRY×n(每个 16 字节) + 每一张图的图像数据。
    ///
    /// 两个约定（都是量过 / 查过规范才这么写的）：
    ///   · **一档一张图**：16 / 32 / 48 / 64 / 128 / 256 各一份，Windows 按显示尺寸自己挑
    ///     （只写一张 256 的话，任务栏 / 小图标视图会拿它硬缩，糊得很明显）；
    ///   · **≤128 的档写成 BMP 写法、256 那一档写成 PNG 写法**：
    ///     PNG 写法是 Vista 起才允许的，好处是 256×256 那张不会臭成 256KB
    ///     （实测：ImageMagick 用 `icon:auto-resize` 出一张 256 的 ICO 是 370,070 字节，
    ///     其中一大半就是那张没压缩的 256×256 + 掩码）。老工具读不了 PNG 那一条，
    ///     但它们也只关心小尺寸，所以这样最兼容也最省地方。</summary>
    internal static class IcoFile
    {
        public const int MaxSize = 256;

        /// <summary>写一个 ICO。`images` 里的顺序无所谓（写的时候按大到小排）。</summary>
        public static void Write(string path, List<RawImage> images)
        {
            if (images == null || images.Count == 0) { throw new ArgumentException("没有图可以写"); }
            List<RawImage> sorted = new List<RawImage>(images);
            sorted.Sort(delegate(RawImage a, RawImage b) { return (b.MaxSide).CompareTo(a.MaxSide); });

            List<byte[]> blobs = new List<byte[]>();
            foreach (RawImage img in sorted)
            {
                blobs.Add((img.MaxSide >= MaxSize) ? PngBlob(img) : DibBlob(img));
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);                     // reserved
                w.Write((ushort)1);                     // 1 = 图标（2 = 光标，我们不做）
                w.Write((ushort)sorted.Count);
                int offset = 6 + 16 * sorted.Count;
                for (int i = 0; i < sorted.Count; i++)
                {
                    RawImage img = sorted[i];
                    w.Write((byte)((img.Width >= MaxSize) ? 0 : img.Width));    // 256 用 0 表示
                    w.Write((byte)((img.Height >= MaxSize) ? 0 : img.Height));
                    w.Write((byte)0);                   // 调色板颜色数（真彩色填 0）
                    w.Write((byte)0);                   // reserved
                    w.Write((ushort)1);                 // 色彩平面
                    w.Write((ushort)32);                // 每像素位数
                    w.Write((uint)blobs[i].Length);
                    w.Write((uint)offset);
                    offset += blobs[i].Length;
                }
                foreach (byte[] blob in blobs) { w.Write(blob); }
            }
        }

        /// <summary>264 字节以内的小图标用 BMP 写法：BITMAPINFOHEADER（高度写两倍）
        /// + 自下而上的 32 位 BGRA + AND 掩码（32 位图带 alpha 时这一位全 0 就行，
        /// Windows 认 alpha 通道；写全 0 是通行做法）。</summary>
        private static byte[] DibBlob(RawImage img)
        {
            int w = img.Width;
            int h = img.Height;
            int maskStride = ((w + 31) / 32) * 4;       // 1bpp 的行按 4 字节对齐
            int xorSize = w * h * 4;
            int andSize = maskStride * h;
            byte[] outBytes = new byte[40 + xorSize + andSize];

            WriteInt(outBytes, 0, 40);                  // biSize
            WriteInt(outBytes, 4, w);                   // biWidth
            WriteInt(outBytes, 8, h * 2);               // biHeight = XOR + AND 两张叠起来
            WriteShort(outBytes, 12, 1);                // biPlanes
            WriteShort(outBytes, 14, 32);               // biBitCount
            WriteInt(outBytes, 16, 0);                  // biCompression = BI_RGB
            WriteInt(outBytes, 20, xorSize);            // biSizeImage
            // 12..35 的其余字段（分辨率 / 调色板）留 0

            for (int y = 0; y < h; y++)
            {
                int srcRow = y * w * 4;
                int dstRow = 40 + (h - 1 - y) * w * 4;  // 自下而上
                Buffer.BlockCopy(img.Pixels, srcRow, outBytes, dstRow, w * 4);
            }
            // AND 掩码：32 位 + alpha 的图标这一块全 0（= 全部不透明，透明度交给 alpha 通道）
            return outBytes;
        }

        /// <summary>256×256 那一档用 PNG 写法（Vista 起支持）。</summary>
        private static byte[] PngBlob(RawImage img)
        {
            using (Bitmap b = img.ToBitmap())
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        /// <summary>读一个 ICO / CUR 文件里有哪些尺寸（只读目录，不解码像素）。
        /// 「图标提取」右键的是 .ico 时用它 —— 目录里的尺寸才是**这个文件真有的尺寸**。</summary>
        public static List<int> SizesIn(string path)
        {
            List<int> list = new List<int>();
            try
            {
                byte[] head = new byte[6];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Read(head, 0, 6) != 6) { return list; }
                    int count = head[4] | (head[5] << 8);
                    if (count <= 0 || count > 64) { return list; }
                    byte[] entry = new byte[16];
                    for (int i = 0; i < count; i++)
                    {
                        if (fs.Read(entry, 0, 16) != 16) { break; }
                        int w = (entry[0] == 0) ? 256 : entry[0];
                        int h = (entry[1] == 0) ? 256 : entry[1];
                        int side = (w < h) ? w : h;
                        if (side >= 1 && side <= 256 && !list.Contains(side)) { list.Add(side); }
                    }
                }
            }
            catch { }
            return list;
        }

        private static void WriteInt(byte[] b, int at, int value)
        {
            b[at] = (byte)(value & 0xFF);
            b[at + 1] = (byte)((value >> 8) & 0xFF);
            b[at + 2] = (byte)((value >> 16) & 0xFF);
            b[at + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteShort(byte[] b, int at, int value)
        {
            b[at] = (byte)(value & 0xFF);
            b[at + 1] = (byte)((value >> 8) & 0xFF);
        }
    }

    /// <summary>手写 **TGA**（Truevision Targa）。
    ///
    /// 写的是最保守的那一种：**不压缩的真彩色（imageType=2）+ 32 位 BGRA + 左上角原点**
    /// （imageDescriptor 的 bit5 = 1）—— 老软件（PS / GIMP / 各种贴图工具）全都认。
    /// RLE（imageType=10）本来也能写，但第一版不做：多一条压缩路径就多一套回归，收益很小。
    /// 18 字节文件头 + 像素数据，没有文件尾（TGA 2.0 的 footer 是可选的）。</summary>
    internal static class TgaFile
    {
        public static void Write(string path, RawImage img)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((byte)0);                       // 图像 ID 长度
                w.Write((byte)0);                       // 没有调色板
                w.Write((byte)2);                       // 2 = 不压缩的真彩色
                w.Write((ushort)0); w.Write((ushort)0); // 调色板起点 / 长度
                w.Write((byte)0);                       // 调色板每项位数
                w.Write((ushort)0); w.Write((ushort)0); // x / y 原点
                w.Write((ushort)img.Width);
                w.Write((ushort)img.Height);
                w.Write((byte)32);                      // 每像素位数
                w.Write((byte)0x28);                    // bit5 = 左上角原点；低 4 位 = alpha 位数 8
                // TGA 里的 32 位顺序就是 B,G,R,A —— 和 RawImage 的内存顺序一致，直接写
                w.Write(img.Pixels);
            }
        }
    }

    /// <summary>手写 **DDS**（DirectDraw Surface）。
    ///
    /// 写的是**不压缩**那一种（`DDPF_RGB | DDPF_ALPHAPIXELS` + BGRA8 掩码，没有 FourCC）：
    /// 128 字节文件头（`DDS ` 魔数 + 124 字节 DDS_HEADER）+ 像素数据。
    /// **不做 DXT / BC 压缩** —— 那几种压缩纹理是真算法（块压缩 + 端点插值），
    /// 不在"零依赖"这条线里；不压缩的 DDS 贴图工具照样能开，只是文件大。
    /// 像素顺序 BGRA、自上而下（不压缩 DDS 的通行写法，用 WIC 的 DDS 解码器读回来验过，
    /// 见 DESIGN §12.70）。</summary>
    internal static class DdsFile
    {
        public static void Write(string path, RawImage img)
        {
            byte[] head = new byte[128];
            head[0] = (byte)'D'; head[1] = (byte)'D'; head[2] = (byte)'S'; head[3] = (byte)' ';
            PutInt(head, 4, 124);                       // dwSize
            // DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_PITCH
            PutInt(head, 8, 0x1 | 0x2 | 0x4 | 0x1000 | 0x8);
            PutInt(head, 12, img.Height);
            PutInt(head, 16, img.Width);
            PutInt(head, 20, img.Width * 4);            // dwPitchOrLinearSize
            PutInt(head, 24, 0);                        // dwDepth
            PutInt(head, 28, 0);                        // dwMipMapCount = 0（没有 mip 链）
            // 32..75 = dwReserved1[11]，留 0
            PutInt(head, 76, 32);                       // ddspf.dwSize
            PutInt(head, 80, 0x40 | 0x1);               // DDPF_RGB | DDPF_ALPHAPIXELS
            PutInt(head, 84, 0);                        // dwFourCC = 0
            PutInt(head, 88, 32);                       // dwRGBBitCount
            PutInt(head, 92, 0x00FF0000);               // R
            PutInt(head, 96, 0x0000FF00);               // G
            PutInt(head, 100, 0x000000FF);              // B
            PutInt(head, 104, unchecked((int)0xFF000000));  // A
            PutInt(head, 108, 0x1000);                  // DDSCAPS_TEXTURE
            // 112..127 = caps2 / caps3 / caps4 / dwReserved2，留 0

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                fs.Write(head, 0, head.Length);
                fs.Write(img.Pixels, 0, img.Pixels.Length);
            }
        }

        private static void PutInt(byte[] b, int at, int value)
        {
            b[at] = (byte)(value & 0xFF);
            b[at + 1] = (byte)((value >> 8) & 0xFF);
            b[at + 2] = (byte)((value >> 16) & 0xFF);
            b[at + 3] = (byte)((value >> 24) & 0xFF);
        }
    }

    /// <summary>输出路径的唯一出处（「图片转换」和「图标提取」共用）。
    ///
    /// 三条规矩（用户 2026-10-15 拍板）：
    ///   ① **写在源文件旁边**（不弹目录选择框、不问任何问题）；
    ///   ② **绝不覆盖**：同名已经存在就 `-2`、`-3`……（源文件一个字节都不动）；
    ///   ③ 源文件夹**写不进去**（系统目录、Program Files 这种）就退到**桌面**，
    ///      并在提示卡上说清楚"写到桌面了" —— 让用户自己去找，而不是悄悄失败。</summary>
    internal static class OutPaths
    {
        /// <summary>源文件旁边那个不撞名的路径（`报告.png` + `_转换` + `jpg` → `报告_转换.jpg`）。</summary>
        public static string NextToSource(string sourcePath, string suffix, string ext)
        {
            string dir = Path.GetDirectoryName(sourcePath);
            if (dir == null || dir.Length == 0) { dir = Directory.GetCurrentDirectory(); }
            return Numbered(dir, Path.GetFileNameWithoutExtension(sourcePath) + suffix, ext);
        }

        /// <summary>桌面上的不撞名路径（源文件夹写不进去时用）。</summary>
        public static string OnDesktop(string sourcePath, string suffix, string ext)
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (dir == null || dir.Length == 0) { dir = Path.GetTempPath(); }
            return Numbered(dir, Path.GetFileNameWithoutExtension(sourcePath) + suffix, ext);
        }

        /// <summary>在 dir 里挑一个还不存在的名字：`名字.ext` → `名字-2.ext` → `名字-3.ext`……</summary>
        public static string Numbered(string dir, string stem, string ext)
        {
            string safe = Sanitize(stem);
            string first = Path.Combine(dir, safe + "." + ext);
            if (!File.Exists(first)) { return first; }
            for (int n = 2; n < 10000; n++)
            {
                string candidate = Path.Combine(dir, safe + "-" + n.ToString(CultureInfo.InvariantCulture) + "." + ext);
                if (!File.Exists(candidate)) { return candidate; }
            }
            return Path.Combine(dir, safe + "-" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture) + "." + ext);
        }

        /// <summary>文件名里不能出现的字符换掉（源文件名理论上是合法的，但外来的路径不一定）。</summary>
        private static string Sanitize(string name)
        {
            if (name == null || name.Length == 0) { return "output"; }
            char[] bad = Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool hit = false;
                foreach (char b in bad) { if (c == b) { hit = true; break; } }
                sb.Append(hit ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
