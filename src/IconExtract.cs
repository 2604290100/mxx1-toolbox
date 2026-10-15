// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Mxx1Toolbox
{
    /// <summary>「图标提取」—— 右键**任意文件**，把它的图标存成一个 .ico 或 .png，
    /// 放在源文件旁边（右键菜单里那一项，子菜单两行：「保存为 ICO」「保存为 PNG」）。
    ///
    /// 谁在用：`src\RightMenu.cs` 的 ExtractIconVerb，命令是
    /// `"&lt;工具箱 exe&gt;" extract-icon "%1" --as=ico`。
    ///
    /// 图标从哪儿来（四条路，按顺序试，**每条都说清楚是从哪儿来的**）：
    ///   ① `.ico` / `.cur` 自己 —— 直接读它目录里登记的那几档（`LoadImage` 按尺寸取，
    ///      所以拿到的就是它**真有**的尺寸）；
    ///   ② `.exe` / `.dll` / `.ocx` / `.cpl` 这类**资源载体** —— 枚举它自己的 `RT_GROUP_ICON`
    ///      图标组，按组里登记的每一档去取（**不放大**：组里没有 256 这一档就不会给你一张
    ///      放大糊出来的 256）；
    ///   ③ 别的文件（.txt / .pdf / .zip…）—— 先查这种文件在注册表里登记的**图标源**
    ///      （`HKCR\.pdf` → ProgId → `DefaultIcon`，像 `%SystemRoot%\system32\shell32.dll,-123`），
    ///      照②那条路抽那个源的真实多档图标；查不到就
    ///   ④ 用**资源管理器给这种文件配的那张**（`SHGetFileInfo`，只有 32×32 / 16×16 两档）——
    ///      拿到什么就说什么，卡片上写明是这一路来的。
    ///
    /// 两条底线：
    ///   · **只读**：源文件一个字节都不动，只新增一个图标文件；
    ///   · **不假装成功**：什么都没有就说"这个文件没有图标"，不拿一个空文件糊弄。
    ///
    /// 结果怎么告诉用户：鼠标旁边那张提示卡只说一句话的汇总（含**是从哪条路取的**），
    /// 明细进工具箱日志（用户 2026-10-15 拍板的口径）。</summary>
    internal static class IconExtract
    {
        public const string LogTag = "图标提取";
        /// <summary>输出文件名里加的那一段：`notepad.exe` → `notepad_icon.ico`。</summary>
        public const string Suffix = "_icon";
        public const string FormatIco = "ico";
        public const string FormatPng = "png";

        /// <summary>能装图标的资源载体（这几类才去枚举它自己的图标资源）。</summary>
        private static readonly string[] CarrierExts = new string[]
        {
            ".exe", ".dll", ".ocx", ".cpl", ".scr", ".mun", ".icl", ".bpl", ".ax", ".efi", ".sys", ".winmd",
        };

        public static string Normalize(string value)
        {
            if (value == null) { return ""; }
            string s = value.Trim().TrimStart('.').ToLowerInvariant();
            if (s == "icon") { return FormatIco; }
            return s;
        }

        public static bool SupportedFormat(string format)
        {
            return (format == FormatIco) || (format == FormatPng);
        }

        public static string Label(string format)
        {
            return (format == FormatPng) ? "PNG" : "ICO";
        }

        /// <summary>子菜单里那一行的标题（「保存为 ICO」）。</summary>
        public static string RowTitle(string format)
        {
            return "保存为 " + Label(format);
        }

        /// <summary>两行的标题连起来（`rightmenu items` 的 `iconrows=` 用它，回归测试也读它）。</summary>
        public static string RowsText()
        {
            return RowTitle(FormatIco) + " / " + RowTitle(FormatPng);
        }

        /// <summary>两行各一句话（给「右键增强说明」和命令行帮助用）。</summary>
        public static string Describe(string format)
        {
            if (format == FormatPng) { return "存成一张 PNG 图片（取最大的那一档，带透明）"; }
            return "存成 Windows 图标文件：有几档就装几档（16 / 32 / 48 / 64 / 128 / 256），只缩不放";
        }

        /// <summary>跑一次提取。
        ///
        /// 退出码：0 = 都存好了；1 = 有没存成的；2 = 用法错 / 格式不认识。</summary>
        public static int Run(string[] paths, string format, bool notify, int notifyMs)
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
            format = Normalize(format);

            if (files.Count == 0)
            {
                Console.Error.WriteLine("用法: extract-icon <文件…> [--as=ico|png]");
                return 2;
            }
            if (!SupportedFormat(format))
            {
                Console.Error.WriteLine("不支持的格式：" + format + "（这一版支持 ico / png）");
                return 2;
            }

            List<Item> results = new List<Item>();
            foreach (string f in files) { results.Add(ExtractOne(f, format)); }

            int ok = 0;
            int failed = 0;
            foreach (Item it in results) { if (it.Ok) { ok++; } else { failed++; } }

            Console.WriteLine("paths=" + files.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("format=" + format);
            foreach (Item it in results)
            {
                if (it.Ok)
                {
                    Console.WriteLine("file=" + it.Src + "\tresult=ok\tout=" + it.Out
                        + "\tsizes=" + it.Sizes + "\troute=" + it.Route
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
                if (it.Ok)
                {
                    Console.WriteLine("out=" + it.Out);
                    Console.WriteLine("sizes=" + it.Sizes);
                    Console.WriteLine("route=" + it.Route);
                    break;
                }
            }
            if (failed > 0)
            {
                foreach (Item it in results)
                {
                    if (!it.Ok) { Console.WriteLine("why=" + it.Why); break; }
                }
            }

            string summary = SummaryOf(results, format, ok, failed);
            Console.WriteLine("summary=" + summary);

            StringBuilder log = new StringBuilder();
            log.Append("存成 ").Append(Label(format)).Append("：成功 ").Append(ok.ToString(CultureInfo.InvariantCulture))
               .Append(" / 失败 ").Append(failed.ToString(CultureInfo.InvariantCulture));
            foreach (Item it in results)
            {
                log.AppendLine();
                if (it.Ok)
                {
                    log.Append("  √ ").Append(it.Src).Append("  →  ").Append(it.Out)
                       .Append("（").Append(it.Sizes).Append(" 档；").Append(it.Route).Append("）");
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
                    ? ("图标提取 · 已存成 " + Label(format))
                    : ((ok > 0) ? "图标提取 · 有没存成的" : "图标提取 · 没存成");
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
            public string Note = "";
            public string Sizes = "";       // 「256 / 48 / 32 / 16」（给 ICO 那一档和日志看）
            public int MaxSize;             // 最大那一档的边长（存 PNG 时就是它）
            public string Route = "";       // 「从这个文件自己的图标资源里抽的」
        }

        private static Item ExtractOne(string src, string format)
        {
            Item it = new Item();
            it.Src = src;
            try
            {
                if (Directory.Exists(src))
                {
                    it.Why = "这是文件夹 —— 文件夹的图标这一版不做（右键菜单里也只装在文件上）";
                    return it;
                }
                if (!File.Exists(src))
                {
                    it.Why = "文件不在了（被移走 / 改名 / 删掉了？）";
                    return it;
                }

                string route;
                string why;
                List<RawImage> images = Collect(src, out route, out why);
                if (images == null || images.Count == 0)
                {
                    it.Why = (why.Length > 0) ? why : "这个文件没有图标（资源里没有，系统也没给它登记图标）";
                    return it;
                }

                string ext = (format == FormatPng) ? "png" : "ico";
                string outPath = OutPaths.NextToSource(src, Suffix, ext);
                string error = "";
                try
                {
                    error = Write(outPath, format, images);
                }
                catch (UnauthorizedAccessException)
                {
                    outPath = OutPaths.OnDesktop(src, Suffix, ext);
                    it.Note = "（源文件夹写不进去，放到桌面上了）";
                    error = Write(outPath, format, images);
                }
                if (error.Length > 0) { it.Why = error; return it; }

                it.Out = outPath;
                it.Route = route;
                it.Sizes = SizesText(images);
                it.MaxSize = images[0].MaxSide;     // Collect 出来的都是**大到小**排好的
                it.Ok = true;
                return it;
            }
            catch (Exception ex)
            {
                it.Why = "提取出错：" + ex.Message;
                return it;
            }
        }

        private static string Write(string outPath, string format, List<RawImage> images)
        {
            if (format == FormatPng)
            {
                RawImage biggest = images[0];
                foreach (RawImage img in images)
                {
                    if (img.MaxSide > biggest.MaxSide) { biggest = img; }
                }
                // 这里要写全名：本类里有个自己的私有结构体也叫 Bitmap（Win32 的 BITMAP），会挡住它
                using (System.Drawing.Bitmap b = biggest.ToBitmap())
                {
                    b.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                return "";
            }
            IcoFile.Write(outPath, images);
            return "";
        }

        /// <summary>尺寸那串字（大到小，像 `256 / 48 / 32 / 16`）。</summary>
        private static string SizesText(List<RawImage> images)
        {
            List<string> parts = new List<string>();
            foreach (RawImage img in images)
            {
                string s = img.MaxSide.ToString(CultureInfo.InvariantCulture);
                if (!parts.Contains(s)) { parts.Add(s); }
            }
            return string.Join(" / ", parts.ToArray());
        }

        /// <summary>四条路依次试，返回的图**按大到小**排好。</summary>
        private static List<RawImage> Collect(string path, out string route, out string why)
        {
            route = "";
            why = "";
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();

            if (ext == ".ico" || ext == ".cur")
            {
                List<RawImage> own = FromIconFile(path, ext == ".cur");
                if (own.Count > 0)
                {
                    route = "从 .ico 文件自己那几档读的";
                    return Sort(own);
                }
                why = "这个 .ico 文件读不出图标（文件是不是坏了？）";
                return own;
            }

            if (IsCarrier(ext))
            {
                string resWhy;
                List<RawImage> res = FromResourceCarrier(path, -1, 0, out resWhy);
                if (res.Count > 0)
                {
                    route = "从这个文件自己的图标资源里抽的";
                    return Sort(res);
                }
                why = (resWhy.Length > 0) ? resWhy : "这个文件里没有图标资源";
                // 载体文件也可能登记了 DefaultIcon（少见），再往下试
            }

            List<RawImage> viaReg = FromRegisteredIconSource(path, out route);
            if (viaReg.Count > 0) { return Sort(viaReg); }

            List<RawImage> shell = FromShellIcon(path);
            if (shell.Count > 0)
            {
                route = "资源管理器给这种文件配的那张（只有 32 / 16 两档）";
                return Sort(shell);
            }

            if (why.Length == 0) { why = "这个文件没有图标（资源里没有，注册表里也没登记图标源）"; }
            return new List<RawImage>();
        }

        private static bool IsCarrier(string ext)
        {
            foreach (string e in CarrierExts) { if (e == ext) { return true; } }
            return false;
        }

        private static List<RawImage> Sort(List<RawImage> images)
        {
            List<RawImage> list = new List<RawImage>(images);
            list.Sort(delegate(RawImage a, RawImage b) { return b.MaxSide.CompareTo(a.MaxSide); });
            return list;
        }

        /// <summary>① .ico / .cur：目录里登记了哪几档就取哪几档。
        /// 用 `LoadImage`（而不是 GDI+）取 —— 它认 Vista 起那种"256 那一档用 PNG 压"的图标。</summary>
        private static List<RawImage> FromIconFile(string path, bool cursor)
        {
            List<RawImage> list = new List<RawImage>();
            List<int> sizes = IcoFile.SizesIn(path);
            if (sizes.Count == 0) { return list; }
            sizes.Sort();
            sizes.Reverse();
            uint type = cursor ? ImageCursor : ImageIcon;
            foreach (int size in sizes)
            {
                IntPtr h = LoadImageW(IntPtr.Zero, path, type, size, size, LrLoadFromFile | LrDefaultColor);
                if (h == IntPtr.Zero) { continue; }
                RawImage img = FromHIcon(h);
                if (img != null) { list.Add(img); }
            }
            return list;
        }

        /// <summary>② 资源载体：枚举 `RT_GROUP_ICON` 图标组，按组里登记的每一档取。
        ///
        /// `groupIndex` &gt;= 0 = 取第几个图标组（0 = 主图标，即资源 id 最小的那个组）；
        /// `wantIconId` &gt; 0 = 取"包含这个图标资源 id"的那个组（注册表 DefaultIcon 写负数时是这种）。</summary>
        private static List<RawImage> FromResourceCarrier(string path, int groupIndex, int wantIconId, out string why)
        {
            why = "";
            List<RawImage> list = new List<RawImage>();
            IntPtr hMod = LoadLibraryExW(path, IntPtr.Zero, LoadLibraryAsDataFile);
            if (hMod == IntPtr.Zero)
            {
                why = "这个文件打不开（" + path + "）";
                return list;
            }
            try
            {
                _groupIds.Clear();
                if (!EnumResourceNamesW(hMod, (IntPtr)RtGroupIcon, _enumProc, IntPtr.Zero) && _groupIds.Count == 0)
                {
                    why = "这个文件里没有图标组";
                    return list;
                }
                if (_groupIds.Count == 0)
                {
                    why = "这个文件里的图标组是字符串名（极罕见），这一版不认";
                    return list;
                }
                _groupIds.Sort();

                byte[] group = null;
                if (wantIconId > 0)
                {
                    foreach (int gid in _groupIds)
                    {
                        byte[] g = LoadResourceBytes(hMod, (IntPtr)RtGroupIcon, (IntPtr)gid);
                        List<int[]> entries = GroupEntries(g);
                        foreach (int[] e in entries)
                        {
                            if (e[1] == wantIconId) { group = g; break; }
                        }
                        if (group != null) { break; }
                    }
                    if (group == null) { why = "注册表指的那个图标不在这个文件里（编号变了？）"; return list; }
                }
                else
                {
                    int idx = (groupIndex < 0) ? 0 : groupIndex;
                    if (idx >= _groupIds.Count) { why = "这个文件没有第 " + (idx + 1).ToString(CultureInfo.InvariantCulture) + " 个图标组"; return list; }
                    group = LoadResourceBytes(hMod, (IntPtr)RtGroupIcon, (IntPtr)_groupIds[idx]);
                }

                List<int[]> items = GroupEntries(group);
                List<int> seen = new List<int>();
                foreach (int[] e in items)
                {
                    int side = e[0];
                    int id = e[1];
                    if (side <= 0 || side > 512 || id <= 0) { continue; }
                    if (seen.Contains(side)) { continue; }
                    byte[] blob = LoadResourceBytes(hMod, (IntPtr)RtIcon, (IntPtr)id);
                    if (blob == null || blob.Length == 0) { continue; }
                    IntPtr hIcon = CreateIconFromResourceEx(blob, (uint)blob.Length, true, 0x00030000, 0, 0, 0);
                    if (hIcon == IntPtr.Zero) { continue; }
                    RawImage img = FromHIcon(hIcon);
                    if (img == null) { continue; }
                    seen.Add(side);
                    list.Add(img);
                }
                if (list.Count == 0) { why = "图标组里没有能解出来的档"; }
                return list;
            }
            finally { FreeLibrary(hMod); }
        }

        /// <summary>③ 注册表登记的图标源：`HKCR\.pdf` → ProgId → `DefaultIcon`。</summary>
        private static List<RawImage> FromRegisteredIconSource(string path, out string route)
        {
            route = "";
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (ext.Length == 0) { return new List<RawImage>(); }

            string spec = ReadDefaultIcon(ext);
            if (spec.Length == 0)
            {
                string progId = ReadString(Registry.ClassesRoot, ext, "");
                if (progId.Length > 0) { spec = ReadDefaultIcon(progId); }
            }
            if (spec.Length == 0) { return new List<RawImage>(); }

            string file;
            int index;
            SplitIconSpec(spec, out file, out index);
            if (file.Length == 0 || !File.Exists(file)) { return new List<RawImage>(); }

            string lower = file.ToLowerInvariant();
            List<RawImage> list;
            if (lower.EndsWith(".ico", StringComparison.Ordinal) || lower.EndsWith(".cur", StringComparison.Ordinal))
            {
                list = FromIconFile(file, lower.EndsWith(".cur", StringComparison.Ordinal));
            }
            else
            {
                string why;
                list = FromResourceCarrier(file, (index >= 0) ? index : -1,
                    (index < 0) ? -index : 0, out why);
            }
            if (list.Count == 0) { return list; }
            route = "按这种文件登记的图标源抽的（" + Path.GetFileName(file) + "）";
            return list;
        }

        /// <summary>④ 资源管理器给这种文件配的那张（32 / 16 两档）。</summary>
        private static List<RawImage> FromShellIcon(string path)
        {
            List<RawImage> list = new List<RawImage>();
            uint size = (uint)Marshal.SizeOf(typeof(SHFileInfoW));
            SHFileInfoW info = new SHFileInfoW();
            if (SHGetFileInfoW(path, 0, ref info, size, ShgfiIcon | ShgfiLargeIcon) != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                RawImage big = FromHIcon(info.hIcon);
                if (big != null) { list.Add(big); }
            }
            info = new SHFileInfoW();
            if (SHGetFileInfoW(path, 0, ref info, size, ShgfiIcon | ShgfiSmallIcon) != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                RawImage small = FromHIcon(info.hIcon);
                if (small != null) { list.Add(small); }
            }
            return list;
        }

        // ------------------------------------------------------------------ HICON → 像素

        /// <summary>把 HICON 取成像素。**这个函数接管句柄的释放**（不管成功失败都 DestroyIcon）。
        ///
        /// 两个细节（不这么写就会出错）：
        ///   · 用 `GetDIBits` 取颜色位图（`hbmColor`）—— 不能拿 `Icon.ToBitmap()`，
        ///     它在 32 位带 alpha 的图标上会丢 alpha（老毛病）；
        ///   · **老图标（16 色 / 32 位但 alpha 全 0）的透明信息在掩码位图里**（`hbmMask`，
        ///     白色 = 透明）—— 颜色位图里一个透明像素都没有的时候要回头读掩码，
    ///     否则老图标会变成一块黑方块。</summary>
        private static RawImage FromHIcon(IntPtr hIcon)
        {
            if (hIcon == IntPtr.Zero) { return null; }
            IconInfo ii = new IconInfo();
            if (!GetIconInfo(hIcon, ref ii))
            {
                DestroyIcon(hIcon);
                return null;
            }
            try
            {
                if (ii.hbmColor == IntPtr.Zero) { return null; }     // 纯单色图标，这一版不处理
                int w;
                int h;
                if (!BitmapSizeOf(ii.hbmColor, out w, out h) || w <= 0 || h <= 0) { return null; }

                RawImage img = new RawImage(w, h);
                IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero) { return null; }
                try
                {
                    BitmapInfo bi = new BitmapInfo();
                    bi.bmiHeader.biSize = 40;
                    bi.bmiHeader.biWidth = w;
                    bi.bmiHeader.biHeight = -h;                      // 负 = 自上而下，省一次翻转
                    bi.bmiHeader.biPlanes = 1;
                    bi.bmiHeader.biBitCount = 32;
                    bi.bmiHeader.biCompression = 0;
                    bi.bmiColors = new RgbQuad[256];
                    if (GetDIBits(hdc, ii.hbmColor, 0, (uint)h, img.Pixels, ref bi, 0) == 0) { return null; }

                    bool anyAlpha = false;
                    for (int i = 3; i < img.Pixels.Length; i += 4)
                    {
                        if (img.Pixels[i] != 0) { anyAlpha = true; break; }
                    }
                    if (!anyAlpha) { ApplyMask(img, ii.hbmMask, hdc); }
                }
                finally { DeleteDC(hdc); }
                return img;
            }
            finally
            {
                if (ii.hbmColor != IntPtr.Zero) { DeleteObject(ii.hbmColor); }
                if (ii.hbmMask != IntPtr.Zero) { DeleteObject(ii.hbmMask); }
                DestroyIcon(hIcon);
            }
        }

        /// <summary>颜色位图上一个透明像素都没有 → 透明信息在掩码里：掩码位为 1 = 透明。</summary>
        private static void ApplyMask(RawImage img, IntPtr hbmMask, IntPtr hdc)
        {
            if (hbmMask == IntPtr.Zero) { return; }
            int w = img.Width;
            int h = img.Height;
            int stride = ((w + 31) / 32) * 4;
            byte[] mask = new byte[stride * h];

            int mw;
            int mh;
            if (!BitmapSizeOf(hbmMask, out mw, out mh))
            {
                for (int i = 3; i < img.Pixels.Length; i += 4) { img.Pixels[i] = 255; }
                return;
            }

            BitmapInfo bi = new BitmapInfo();
            bi.bmiHeader.biSize = 40;
            bi.bmiHeader.biWidth = w;
            bi.bmiHeader.biHeight = h;                  // 正 = 自下而上
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 1;
            bi.bmiHeader.biCompression = 0;
            bi.bmiColors = new RgbQuad[256];
            if (GetDIBits(hdc, hbmMask, 0, (uint)h, mask, ref bi, 0) == 0)
            {
                for (int i = 3; i < img.Pixels.Length; i += 4) { img.Pixels[i] = 255; }
                return;
            }

            for (int y = 0; y < h; y++)
            {
                int srcRow = h - 1 - y;                 // 掩码是自下而上的
                for (int x = 0; x < w; x++)
                {
                    int at = srcRow * stride + (x >> 3);
                    if (at >= mask.Length) { continue; }
                    int bit = (mask[at] >> (7 - (x & 7))) & 1;
                    img.Pixels[(y * w + x) * 4 + 3] = (byte)((bit == 1) ? 0 : 255);
                }
            }
        }

        private static bool BitmapSizeOf(IntPtr hBitmap, out int width, out int height)
        {
            width = 0;
            height = 0;
            int size = Marshal.SizeOf(typeof(Bitmap));
            IntPtr buf = Marshal.AllocCoTaskMem(size);
            try
            {
                if (GetObjectW(hBitmap, size, buf) == 0) { return false; }
                Bitmap bm = (Bitmap)Marshal.PtrToStructure(buf, typeof(Bitmap));
                width = bm.bmWidth;
                height = bm.bmHeight;
                return true;
            }
            finally { Marshal.FreeCoTaskMem(buf); }
        }

        // ------------------------------------------------------------------ 注册表 / 字符串

        private static string ReadDefaultIcon(string subKey)
        {
            string v = ReadString(Registry.ClassesRoot, subKey + "\\DefaultIcon", "");
            if (v.Length == 0) { v = ReadString(Registry.ClassesRoot, subKey, ""); }
            return v.Trim().Trim('"');
        }

        private static string ReadString(RegistryKey root, string subKey, string name)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(subKey, false))
                {
                    if (k == null) { return ""; }
                    object o = k.GetValue(name, null);
                    return (o == null) ? "" : o.ToString();
                }
            }
            catch { return ""; }
        }

        /// <summary>`"C:\...\app.exe",-101` → 文件 + 编号（负数 = 资源 id；正数 = 第几个图标）。
        /// 没有编号就是 0。`%SystemRoot%` 这类变量要展开。</summary>
        private static void SplitIconSpec(string spec, out string file, out int index)
        {
            file = "";
            index = 0;
            string s = spec.Trim();
            int comma = s.LastIndexOf(',');
            if (comma > 0)
            {
                string tail = s.Substring(comma + 1).Trim();
                int n;
                if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                {
                    index = n;
                    s = s.Substring(0, comma);
                }
            }
            s = s.Trim().Trim('"');
            try { s = Environment.ExpandEnvironmentVariables(s); }
            catch { }
            file = s;
        }

        // ------------------------------------------------------------------ 卡片那句汇总

        private static string SummaryOf(List<Item> results, string format, int ok, int failed)
        {
            bool desktop = false;
            foreach (Item it in results)
            {
                if (it.Note.IndexOf("桌面", StringComparison.Ordinal) >= 0) { desktop = true; break; }
            }
            if (failed == 0 && ok == 1)
            {
                Item it = results[0];
                string where = desktop
                    ? "（源文件夹写不进去，放到桌面上了）"
                    : "，和源文件放在一起";
                string sizeText = (format == FormatPng)
                    ? ("最大那一档 " + it.MaxSize.ToString(CultureInfo.InvariantCulture) + "×"
                        + it.MaxSize.ToString(CultureInfo.InvariantCulture))
                    : (it.Sizes + " 这些档");
                return "已存好：" + Path.GetFileName(it.Out) + "（" + sizeText + "）" + where + "。"
                    + it.Route + "。";
            }
            if (failed == 0)
            {
                return "存好 " + ok.ToString(CultureInfo.InvariantCulture) + " 个（" + Label(format) + "），"
                    + (desktop ? "源文件夹写不进去的那几个放到桌面上了。" : "都和源文件放在一起。");
            }
            string why = "";
            foreach (Item it in results) { if (!it.Ok) { why = it.Why; break; } }
            if (why.Length > 60) { why = why.Substring(0, 59) + "…"; }
            if (ok == 0) { return "没存成：" + why + "（细节在工具箱日志里）"; }
            return "存好 " + ok.ToString(CultureInfo.InvariantCulture) + " 个，"
                + failed.ToString(CultureInfo.InvariantCulture) + " 个没成功：" + why
                + "（细节在工具箱日志里）";
        }

        private static bool Contains(List<string> list, string s)
        {
            foreach (string x in list) { if (string.Equals(x, s, StringComparison.OrdinalIgnoreCase)) { return true; } }
            return false;
        }

        // ------------------------------------------------------------------ Win32

        private const int RtIcon = 3;
        private const int RtGroupIcon = 14;
        private const uint LoadLibraryAsDataFile = 0x00000002;
        private const uint ImageIcon = 1;
        private const uint ImageCursor = 2;
        private const uint LrLoadFromFile = 0x00000010;
        private const uint LrDefaultColor = 0x00000000;
        private const uint ShgfiIcon = 0x00000100;
        private const uint ShgfiLargeIcon = 0x00000000;
        private const uint ShgfiSmallIcon = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        private struct RgbQuad
        {
            public byte Blue;
            public byte Green;
            public byte Red;
            public byte Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader bmiHeader;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public RgbQuad[] bmiColors;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Bitmap
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public short bmPlanes;
            public short bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFileInfoW
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

        /// <summary>枚举回调攒下来的图标组 id。**委托要存成字段**，否则会被 GC 回收，
        /// 回调进到已释放的托管代码里就崩（经典的 P/Invoke 坑）。</summary>
        private static readonly List<int> _groupIds = new List<int>();
        private static readonly EnumResNameProc _enumProc = new EnumResNameProc(EnumGroupName);

        private static bool EnumGroupName(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam)
        {
            // 名字是整数 id 的才认（字符串名的图标组极罕见，这一版跳过）
            if ((lpszName.ToInt64() & 0xFFFF0000L) == 0) { _groupIds.Add((int)lpszName.ToInt64()); }
            return true;
        }

        /// <summary>读一个图标组（`RT_GROUP_ICON`）里的每一项：`[边长, 图标资源 id]`。</summary>
        private static List<int[]> GroupEntries(byte[] group)
        {
            List<int[]> list = new List<int[]>();
            if (group == null || group.Length < 6) { return list; }
            int count = group[4] | (group[5] << 8);
            for (int i = 0; i < count; i++)
            {
                int at = 6 + i * 14;
                if (at + 14 > group.Length) { break; }
                int w = (group[at] == 0) ? 256 : group[at];
                int h = (group[at + 1] == 0) ? 256 : group[at + 1];
                int side = (w < h) ? w : h;
                int id = group[at + 12] | (group[at + 13] << 8);
                list.Add(new int[] { side, id });
            }
            return list;
        }

        private static byte[] LoadResourceBytes(IntPtr hModule, IntPtr type, IntPtr name)
        {
            IntPtr info = FindResourceW(hModule, name, type);
            if (info == IntPtr.Zero) { return null; }
            uint size = SizeofResource(hModule, info);
            if (size == 0) { return null; }
            IntPtr data = LoadResource(hModule, info);
            if (data == IntPtr.Zero) { return null; }
            IntPtr ptr = LockResource(data);
            if (ptr == IntPtr.Zero) { return null; }
            byte[] buf = new byte[size];
            Marshal.Copy(ptr, buf, 0, (int)size);
            return buf;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumResourceNamesW(IntPtr hModule, IntPtr type, EnumResNameProc callback, IntPtr param);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindResourceW(IntPtr hModule, IntPtr name, IntPtr type);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr hModule, IntPtr resInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr resData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SizeofResource(IntPtr hModule, IntPtr resInfo);

        // ⚠️ GetObject 在 **gdi32** 里，不在 kernel32 —— 写错了不会编译报错，是运行时抛
        // 「无法在 DLL"kernel32.dll"中找到名为"GetObjectW"的入口点」（第一次就这么踩了）。
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetObjectW(IntPtr handle, int count, IntPtr buffer);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, bool isIcon, uint version,
            int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetIconInfo(IntPtr hIcon, ref IconInfo info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint load);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr handle);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hBitmap, uint start, uint lines,
            byte[] bits, ref BitmapInfo info, uint usage);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfoW(string path, uint fileAttributes, ref SHFileInfoW info,
            uint infoSize, uint flags);
    }
}
