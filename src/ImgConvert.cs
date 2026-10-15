// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>「图片转换」—— 右键一个图片文件，把它换一种格式存到**源文件旁边**
    /// （右键菜单里那一项，子菜单八行：JPG / PNG / GIF / BMP / TIFF / ICO / TGA / DDS）。
    ///
    /// 谁在用：`src\RightMenu.cs` 的 ConvertVerb，命令是
    /// `"&lt;工具箱 exe&gt;" convert-image "%1" --to=jpg`（八行各带一个 `--to=`）。
    ///
    /// 八种输出分成两拨（这条线是**量出来的**，不是拍脑袋 —— 实测数据在 DESIGN §12.70）：
    ///   · **GDI+ 自己就能写**：JPG / PNG / GIF / BMP / TIFF
    ///     （2026-10-15 在真机上 `ImageCodecInfo.GetImageEncoders()` 数出来只有这五种；
    ///      没有 WebP、没有 ICO、没有 TGA / DDS）；
    ///   · **自己按规范拼字节**：ICO（`IcoFile`）/ TGA（`TgaFile`）/ DDS（`DdsFile`）。
    ///
    /// 三条底线：
    ///   ① **源文件一个字节都不动**（只读打开、只新增一个文件，不移动、不改名、不删除）；
    ///   ② **绝不覆盖**：要写的名字已经存在就 `-2` / `-3`（见 `OutPaths`）；
    ///   ③ **不假装成功**：本机解不开的输入（WebP / HEIC / AVIF 这些）如实说
    ///      「本机解不开这个格式」，不生成一个半成品文件（ImageMagick 那条路踩过这个坑：
    ///      它转 HEIC 时退出码 0、文件也生成了，可头 8 个字节还是 `89 50 4E 47` = 改了名的 PNG）。
    ///
    /// 结果怎么告诉用户：鼠标旁边那张提示卡（`src\Balloon.cs`，5 秒自动关闭）
    /// 只说**一句话的汇总**，每个文件的细节写进工具箱日志
    /// （用户 2026-10-15 拍板：「批量只给汇总一句话 + 明细进日志」）。</summary>
    internal static class ImgConvert
    {
        public const string LogTag = "图片转换";
        /// <summary>输出文件名里加的那一段：`报告.png` → `报告_转换.jpg`。</summary>
        public const string Suffix = "_转换";

        /// <summary>这一版支持的输出（顺序 = 右键子菜单里的顺序）。
        /// 前八种是"每台机器都有"的；最后那个 **JXR** 要走 Windows 自带的 WIC 编码器
        /// （`JxrWriter` 探测通过才在菜单里出现，见那里的类说明）。</summary>
        public static readonly string[] Targets = new string[]
        {
            "jpg", "png", "gif", "bmp", "tiff", "ico", "tga", "dds", "jxr",
        };

        /// <summary>写 ICO 时依次尝试的档位（大到小）。**只缩不放**：源比这一档小就跳过。</summary>
        private static readonly int[] IcoSizes = new int[] { 256, 128, 64, 48, 32, 16 };

        /// <summary>这台机器上**真能用**的那几种（少了 JXR 就是八种）—— 写子菜单、报状态都按它来，
        /// **不给用户摆一个点了必然失败的菜单项**。</summary>
        public static string[] AvailableTargets()
        {
            List<string> list = new List<string>();
            foreach (string t in Targets)
            {
                if ((t == JxrWriter.Target) && !JxrWriter.Available()) { continue; }
                list.Add(t);
            }
            return list.ToArray();
        }

        /// <summary>`jpeg` / `tif` / `.PNG` 这些写法都归一化到 `Targets` 里的那个词。</summary>
        public static string Normalize(string value)
        {
            if (value == null) { return ""; }
            string s = value.Trim().TrimStart('.').ToLowerInvariant();
            if (s == "jpeg" || s == "jpe" || s == "jfif") { return "jpg"; }
            if (s == "tif") { return "tiff"; }
            if (s == "icon") { return "ico"; }
            if (s == "wdp" || s == "hdp") { return "jxr"; }     // HD Photo 的老名字，同一个编码器
            return s;
        }

        public static bool Supported(string target)
        {
            foreach (string t in Targets) { if (t == target) { return true; } }
            return false;
        }

        /// <summary>给人看的名字（菜单行、报告、提示卡都用它）。</summary>
        public static string Label(string target)
        {
            if (target == "jpg") { return "JPG"; }
            if (target == "png") { return "PNG"; }
            if (target == "gif") { return "GIF"; }
            if (target == "bmp") { return "BMP"; }
            if (target == "tiff") { return "TIFF"; }
            if (target == "ico") { return "ICO"; }
            if (target == "tga") { return "TGA"; }
            if (target == "dds") { return "DDS"; }
            if (target == "jxr") { return "JXR"; }
            return target.ToUpperInvariant();
        }

        /// <summary>子菜单里那一行的标题（「转成 JPG」）。</summary>
        public static string RowTitle(string target)
        {
            return "转成 " + Label(target);
        }

        /// <summary>子菜单里那几行的标题连起来（`rightmenu items` 的 `convertrows=` 用它，回归测试也读它）——
        /// 行标题的唯一出处就是 `RowTitle`，别在别处再抄一份。
        /// ⚠️ 用 `AvailableTargets()`：念出来的必须是**菜单里真有**的那几行（本机没有 JXR 编码器时就是八行）。</summary>
        public static string RowsText()
        {
            List<string> list = new List<string>();
            foreach (string t in AvailableTargets()) { list.Add(RowTitle(t)); }
            return string.Join(" / ", list.ToArray());
        }

        public static string ExtOf(string target)
        {
            if (target == "tiff") { return "tif"; }
            return target;
        }

        /// <summary>每种各一句话（给「右键增强说明」和命令行帮助用，一处写、别处别抄）。</summary>
        public static string Describe(string target)
        {
            if (target == "jpg") { return "最常见的照片格式，体积小；**有损**，透明的地方补成白底"; }
            if (target == "png") { return "无损、支持透明（截图 / 图标首选）"; }
            if (target == "gif") { return "256 色、单帧（老网页 / 表情包那种）；存不了半透明"; }
            if (target == "bmp") { return "24 位不压缩的位图，什么程序都认；体积大"; }
            if (target == "tiff") { return "无损 + LZW 压缩，支持透明（印刷 / 存档常用）"; }
            if (target == "ico") { return "Windows 图标：一档一张，16 / 32 / 48 / 64 / 128 / 256（只缩不放）"; }
            if (target == "tga") { return "不压缩的 32 位 TGA（贴图 / 三维软件常用，带透明）"; }
            if (target == "dds") { return "不压缩的 32 位 DDS（游戏贴图常用；这一版不做 DXT 压缩）"; }
            if (target == "jxr") { return "JPEG XR：Windows 自带，压缩率比 JPG 好一截、也能无损（Win7 上是它的前身 HD Photo）"; }
            return target;
        }

        /// <summary>跑一次转换。`paths` 是右键选中的那些文件（多选时资源管理器一次全传进来），
        /// `target` 是 `Targets` 里的一个词。
        ///
        /// 退出码：0 = 全部转好了；1 = 有没转成的；2 = 用法错 / 格式不认识。
        /// 命令行**只输出 key=value**（回归测试读它），给人看的话在提示卡与日志里。</summary>
        public static int Run(string[] paths, string target, bool notify, int notifyMs)
        {
            List<string> files = new List<string>();
            if (paths != null)
            {
                foreach (string p in paths)
                {
                    if (p == null || p.Trim().Length == 0) { continue; }
                    if (!Contains(files, p.Trim())) { files.Add(p.Trim()); }
                }
            }
            target = Normalize(target);

            if (files.Count == 0)
            {
                Console.Error.WriteLine("用法: convert-image <文件…> --to=" + string.Join("|", Targets));
                return 2;
            }
            if (!Supported(target))
            {
                Console.Error.WriteLine("不支持的格式：" + target + "（这一版支持 "
                    + string.Join(" / ", Targets) + "）");
                return 2;
            }

            List<Item> results = new List<Item>();
            foreach (string f in files) { results.Add(ConvertOne(f, target)); }

            int ok = 0;
            int failed = 0;
            foreach (Item it in results) { if (it.Ok) { ok++; } else { failed++; } }

            Console.WriteLine("paths=" + files.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("target=" + target);
            Console.WriteLine("label=" + Label(target));
            foreach (Item it in results)
            {
                if (it.Ok)
                {
                    Console.WriteLine("file=" + it.Src + "\tresult=ok\tout=" + it.Out
                        + (it.Detail.Length > 0 ? ("\tdetail=" + it.Detail) : "")
                        + (it.Note.Length > 0 ? ("\tnote=" + it.Note) : ""));
                }
                else
                {
                    Console.WriteLine("file=" + it.Src + "\tresult=fail\twhy=" + it.Why);
                }
            }
            Console.WriteLine("ok=" + ok.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("failed=" + failed.ToString(CultureInfo.InvariantCulture));
            foreach (Item it in results)
            {
                if (it.Ok) { Console.WriteLine("out=" + it.Out); break; }
            }
            if (failed > 0)
            {
                foreach (Item it in results)
                {
                    if (!it.Ok) { Console.WriteLine("why=" + it.Why); break; }
                }
            }

            string summary = SummaryOf(results, target, ok, failed);
            Console.WriteLine("summary=" + summary);

            StringBuilder log = new StringBuilder();
            log.Append("转成 ").Append(Label(target)).Append("：成功 ").Append(ok.ToString(CultureInfo.InvariantCulture))
               .Append(" / 失败 ").Append(failed.ToString(CultureInfo.InvariantCulture));
            foreach (Item it in results)
            {
                log.AppendLine();
                if (it.Ok)
                {
                    log.Append("  √ ").Append(it.Src).Append("  →  ").Append(it.Out);
                    if (it.Detail.Length > 0) { log.Append("（").Append(it.Detail).Append("）"); }
                    if (it.Note.Length > 0) { log.Append("  ").Append(it.Note); }
                }
                else
                {
                    log.Append("  × ").Append(it.Src).Append("  ").Append(it.Why);
                }
            }
            Logger.Write(LogTag, log.ToString());

            if (notify)
            {
                string title = (failed == 0)
                    ? ("图片转换 · 已转成 " + Label(target))
                    : ((ok > 0) ? "图片转换 · 有没转成的" : "图片转换 · 没转成");
                NoticeKind kind = (failed == 0) ? NoticeKind.Ok : ((ok > 0) ? NoticeKind.Warn : NoticeKind.Fail);
                Balloon.Show(title, summary, notifyMs, kind);
            }
            return (failed == 0) ? 0 : 1;
        }

        // ------------------------------------------------------------------ 一个文件

        private sealed class Item
        {
            public string Src = "";
            public string Out = "";
            public bool Ok;
            public string Why = "";
            public string Note = "";        // 「源文件夹写不进去，放到桌面了」/「透明的地方补成白底」
            public string Detail = "";      // 「档位 256/128/64/48/32/16」
        }

        private static Item ConvertOne(string src, string target)
        {
            Item it = new Item();
            it.Src = src;
            try
            {
                if (Directory.Exists(src))
                {
                    it.Why = "这是文件夹，得选一个图片文件";
                    return it;
                }
                if (!File.Exists(src))
                {
                    it.Why = "文件不在了（被移走 / 改名 / 删掉了？）";
                    return it;
                }

                string loadWhy;
                Bitmap loaded = Load(src, out loadWhy);
                if (loaded == null)
                {
                    it.Why = loadWhy;
                    return it;
                }

                using (loaded)
                {
                    RawImage raw = RawImage.FromBitmap(loaded);
                    bool transparent = raw.HasTransparency;

                    string outPath = OutPaths.NextToSource(src, Suffix, ExtOf(target));
                    string detail = "";
                    string error = "";
                    try
                    {
                        error = WriteTarget(outPath, target, raw, out detail);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // 源文件夹写不进去（系统目录、Program Files 这种）→ 退到桌面
                        outPath = OutPaths.OnDesktop(src, Suffix, ExtOf(target));
                        it.Note = "（源文件夹写不进去，放到桌面上了）";
                        error = WriteTarget(outPath, target, raw, out detail);
                    }
                    if (error.Length > 0)
                    {
                        it.Why = error;
                        return it;
                    }

                    it.Out = outPath;
                    it.Detail = detail;
                    if (transparent && FlattensTransparency(target) && it.Note.Length == 0)
                    {
                        it.Note = "（透明的地方补成白底）";
                    }
                    it.Ok = true;
                    return it;
                }
            }
            catch (Exception ex)
            {
                it.Why = "转换出错：" + ex.Message;
                return it;
            }
        }

        /// <summary>这几种格式**存不了透明**，写之前要把透明的地方压成白底
        /// （实测：透明 PNG 直接存 JPG 会变**黑** —— 左边那块量出来是 `R0 G0 B0`，
        /// 加了白底才是 `255,255,255`，见 DESIGN §12.70）。</summary>
        private static bool FlattensTransparency(string target)
        {
            return (target == "jpg") || (target == "gif") || (target == "bmp");
        }

        /// <summary>读进来。GDI+ 解不开的（WebP / HEIC / AVIF / JXL…）如实说解不开，
        /// 不做任何"转换后才发现是空文件"的事。</summary>
        private static Bitmap Load(string src, out string why)
        {
            why = "";
            try
            {
                return new Bitmap(src);
            }
            catch (Exception ex)
            {
                // .ico 走 GDI+ 的解码器有时挑到的不是想要那一档，退回 Icon 那条路
                try
                {
                    using (Icon ic = new Icon(src))
                    {
                        return ic.ToBitmap();
                    }
                }
                catch { }
                why = "本机解不开这个格式（" + ex.GetType().Name + "）—— GDI+ 只认得 "
                    + "BMP / JPG / PNG / GIF / TIFF / ICO 这几种，HEIC / WebP / AVIF 要装系统的解码扩展，这一版不做";
                return null;
            }
        }

        /// <summary>按目标格式写文件。返回空串 = 成功，否则是一句人话。</summary>
        private static string WriteTarget(string outPath, string target, RawImage raw, out string detail)
        {
            detail = "";
            using (Bitmap argb = raw.ToBitmap())
            {
                switch (target)
                {
                    case "jpg":
                        {
                            using (Bitmap flat = FlattenWhite(argb)) { SaveJpeg(flat, outPath, 90); }
                            detail = "质量 90";
                            return "";
                        }

                    case "png":
                        argb.Save(outPath, ImageFormat.Png);
                        detail = "无损、保留透明";
                        return "";

                    case "gif":
                        {
                            using (Bitmap flat = FlattenWhite(argb)) { flat.Save(outPath, ImageFormat.Gif); }
                            detail = "256 色、单帧";
                            return "";
                        }

                    case "bmp":
                        {
                            using (Bitmap flat = FlattenWhite(argb)) { flat.Save(outPath, ImageFormat.Bmp); }
                            detail = "24 位不压缩";
                            return "";
                        }

                    case "tiff":
                        SaveTiffLzw(argb, outPath);
                        detail = "LZW 压缩";
                        return "";

                    case "ico":
                        {
                            List<RawImage> frames = MakeIcoFrames(raw, out detail);
                            IcoFile.Write(outPath, frames);
                            return "";
                        }

                    case "tga":
                        TgaFile.Write(outPath, raw);
                        detail = "不压缩 32 位";
                        return "";

                    case "dds":
                        DdsFile.Write(outPath, raw);
                        detail = "不压缩 32 位";
                        return "";

                    case "jxr":
                        {
                            // 借 Windows 自带的 WIC 编码器（WPF 那层）—— 本机没有就**不假装成功**
                            string jxrWhy = JxrWriter.Write(outPath, raw);
                            if (jxrWhy.Length > 0) { return jxrWhy; }
                            detail = "质量 0.9、保留透明";
                            return "";
                        }
                }
            }
            return "不支持的格式：" + target;
        }

        /// <summary>ICO 的每一档（方图、只缩不放）。源比 16 还小的时候就按它自己的大小写一档。</summary>
        private static List<RawImage> MakeIcoFrames(RawImage raw, out string detail)
        {
            List<RawImage> list = new List<RawImage>();
            List<string> names = new List<string>();
            foreach (int size in IcoSizes)
            {
                RawImage frame = raw.SquareAt(size);
                if (frame == null) { continue; }
                list.Add(frame);
                names.Add(size.ToString(CultureInfo.InvariantCulture));
            }
            if (list.Count == 0)
            {
                int side = raw.MaxSide;
                if (side < 1) { side = 1; }
                list.Add(raw.Place(side, side));
                names.Add(side.ToString(CultureInfo.InvariantCulture));
            }
            detail = "档位 " + string.Join("/", names.ToArray());
            return list;
        }

        /// <summary>透明压白底（JPG / GIF / BMP 用）。</summary>
        private static Bitmap FlattenWhite(Bitmap src)
        {
            Bitmap b = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.CompositingMode = CompositingMode.SourceOver;
                g.Clear(Color.White);
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            }
            return b;
        }

        private static void SaveJpeg(Bitmap b, string path, int quality)
        {
            ImageCodecInfo codec = CodecOf(ImageFormat.Jpeg);
            if (codec == null) { b.Save(path, ImageFormat.Jpeg); return; }
            using (EncoderParameters p = new EncoderParameters(1))
            {
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                b.Save(path, codec, p);
            }
        }

        private static void SaveTiffLzw(Bitmap b, string path)
        {
            ImageCodecInfo codec = CodecOf(ImageFormat.Tiff);
            if (codec == null) { b.Save(path, ImageFormat.Tiff); return; }
            using (EncoderParameters p = new EncoderParameters(1))
            {
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Compression,
                    (long)EncoderValue.CompressionLZW);
                b.Save(path, codec, p);
            }
        }

        private static ImageCodecInfo CodecOf(ImageFormat fmt)
        {
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
            {
                if (c.FormatID == fmt.Guid) { return c; }
            }
            return null;
        }

        /// <summary>卡片上那一句话（正文有 300 字上限，所以这里只给汇总）。</summary>
        private static string SummaryOf(List<Item> results, string target, int ok, int failed)
        {
            string label = Label(target);
            bool desktop = false;
            foreach (Item it in results) { if (it.Note.IndexOf("桌面", StringComparison.Ordinal) >= 0) { desktop = true; break; } }

            if (failed == 0 && ok == 1)
            {
                string text = "已转好：" + Path.GetFileName(results[0].Out) + "，和源文件放在一起。";
                if (desktop) { text = "已转好：" + Path.GetFileName(results[0].Out) + "（源文件夹写不进去，放到桌面上了）。"; }
                return text;
            }
            if (failed == 0)
            {
                return "转好 " + ok.ToString(CultureInfo.InvariantCulture) + " 张（" + label + "），"
                    + (desktop ? "源文件夹写不进去的那几张放到桌面上了。" : "都和源文件放在一起。");
            }
            string why = "";
            foreach (Item it in results) { if (!it.Ok) { why = it.Why; break; } }
            if (why.Length > 60) { why = why.Substring(0, 59) + "…"; }
            if (ok == 0)
            {
                return "没转成：" + why + "（细节在工具箱日志里）";
            }
            return "转好 " + ok.ToString(CultureInfo.InvariantCulture) + " 张，"
                + failed.ToString(CultureInfo.InvariantCulture) + " 张没成功：" + why
                + "（细节在工具箱日志里）";
        }

        private static bool Contains(List<string> list, string s)
        {
            foreach (string x in list) { if (string.Equals(x, s, StringComparison.OrdinalIgnoreCase)) { return true; } }
            return false;
        }
    }
}
