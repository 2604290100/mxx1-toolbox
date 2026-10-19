// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Mxx1Toolbox
{
    /// <summary>「重复文件查找」的后端：三档比较 —— **大小 → 前 4 KB 的 SHA256 → 整个文件的 SHA256**。
    /// 窗口（DupForm）和命令行（`dup`）共用这一份。
    ///
    /// 为什么是这三档：大小不同的一定不是重复；同大小的文件里绝大多数只看前 4 KB 就能分开；
    /// 只有前 4 KB 一样的才值得读整个文件（几百 MB 的镜像读一遍很贵，能省就省）。
    /// `--full` 那条开关是"跳过前 4 KB 预筛、直接整个文件比对"——给对结果有疑问时交叉验证用，
    /// 两种模式的分组结果**必须完全一致**（回归测试就是这么比的）。
    ///
    /// 三条硬口径：
    /// ① **硬链接不是重复**：两个路径指向同一份数据（同一个卷 + 同一个文件索引），删掉一个不省空间。
    ///    这一类的成员标 `Hardlink = true`，窗口里不默认勾选、命令行 `--delete` 也不碰；
    /// ② **不进 junction / 符号链接目录**（可能成环，也不是"另一份文件"）；
    /// ③ `--min-size` 默认 1 MB：几 KB 的小文件重复了也省不出空间，列出来只会把真正的重复埋掉。
    ///
    /// 扫描可取消：每一块读写之间都看一次 cancel()，窗口的「取消」按钮和关窗口都靠它。</summary>
    internal static class DupFinder
    {
        public sealed class Entry
        {
            public string Path = "";
            public long Size = 0;
            public DateTime Modified = DateTime.MinValue;
            public string Identity = "";      // 卷序列号:文件索引高:文件索引低（读不到就是空）
            public bool Hardlink = false;     // 和组里另一个成员是同一份数据
        }

        public sealed class Group
        {
            public long Size = 0;
            public string Hash = "";
            public readonly List<Entry> Files = new List<Entry>();
        }

        public sealed class Options
        {
            public long MinSize = 1024L * 1024L;   // 1 MB
            public bool Full = false;              // 不做 4 KB 预筛
        }

        public sealed class Progress
        {
            public int Files = 0;              // 收进来的候选文件数
            public int Hashed = 0;             // 已经读过的文件数
            public long HashedBytes = 0;
            public string Current = "";
        }

        public sealed class Result
        {
            public readonly List<Group> Groups = new List<Group>();
            public readonly List<string> Errors = new List<string>();
            public int Scanned = 0;            // 看到的文件总数（含太小被跳过的）
            public int Candidates = 0;         // 达到最小体积、参与比较的
            public bool Canceled = false;
        }

        // ------------------------------------------------------------ 扫描

        public static Result Find(string[] roots, Options opt, Func<bool> cancel, Action<Progress> report)
        {
            Result res = new Result();
            if (opt == null) { opt = new Options(); }
            Progress prog = new Progress();
            if (report != null) { report(prog); }

            List<Entry> files = new List<Entry>();
            foreach (string root in roots)
            {
                if (root == null || root.Trim().Length == 0) { continue; }
                try
                {
                    if (Directory.Exists(root)) { Walk(root, 0, opt, res, files, prog, cancel); }
                    else if (File.Exists(root))
                    {
                        Entry one = MakeEntry(root, opt, res);
                        if (one != null) { files.Add(one); }
                    }
                    else { res.Errors.Add("这个路径不存在：" + root); }
                }
                catch (Exception ex) { res.Errors.Add("扫不了 " + root + "：" + ex.Message); }
                if (cancel != null && cancel()) { res.Canceled = true; break; }
            }
            res.Candidates = files.Count;

            // ① 按大小分组（只有一个的组直接没有重复）
            Dictionary<long, List<Entry>> bySize = new Dictionary<long, List<Entry>>();
            foreach (Entry e in files)
            {
                List<Entry> list;
                if (!bySize.TryGetValue(e.Size, out list)) { list = new List<Entry>(); bySize[e.Size] = list; }
                list.Add(e);
            }

            foreach (long size in SortedKeys(bySize))
            {
                if (cancel != null && cancel()) { res.Canceled = true; break; }
                List<Entry> sameSize = bySize[size];
                if (sameSize.Count < 2) { continue; }

                // ② 前 4 KB（--full 时跳过，直接用整个文件的哈希）
                Dictionary<string, List<Entry>> byPrefix = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
                if (!opt.Full)
                {
                    foreach (Entry e in sameSize)
                    {
                        if (cancel != null && cancel()) { res.Canceled = true; break; }
                        string h = HashOf(e.Path, 4096, cancel, prog, res);
                        if (h == null)
                        {
                            if (cancel != null && cancel()) { res.Canceled = true; break; }
                            continue;   // 读不了的已经记进 Errors
                        }
                        List<Entry> bucket;
                        if (!byPrefix.TryGetValue(h, out bucket)) { bucket = new List<Entry>(); byPrefix[h] = bucket; }
                        bucket.Add(e);
                    }
                    if (res.Canceled) { break; }
                }

                List<List<Entry>> buckets;
                if (opt.Full) { buckets = new List<List<Entry>>(); buckets.Add(sameSize); }
                else
                {
                    buckets = new List<List<Entry>>();
                    foreach (string h in SortedKeys(byPrefix))
                    {
                        if (byPrefix[h].Count >= 2) { buckets.Add(byPrefix[h]); }
                    }
                }

                // ③ 整个文件的 SHA256 定案
                foreach (List<Entry> bucket in buckets)
                {
                    if (cancel != null && cancel()) { res.Canceled = true; break; }
                    Dictionary<string, List<Entry>> byFull = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
                    foreach (Entry e in bucket)
                    {
                        if (cancel != null && cancel()) { res.Canceled = true; break; }
                        string h = HashOf(e.Path, 0, cancel, prog, res);
                        if (h == null) { continue; }
                        List<Entry> list;
                        if (!byFull.TryGetValue(h, out list)) { list = new List<Entry>(); byFull[h] = list; }
                        list.Add(e);
                    }
                    if (res.Canceled) { break; }
                    foreach (string h in SortedKeys(byFull))
                    {
                        List<Entry> same = byFull[h];
                        if (same.Count < 2) { continue; }
                        Group g = new Group();
                        g.Size = size;
                        g.Hash = h;
                        g.Files.AddRange(same);
                        g.Files.Sort(delegate(Entry a, Entry b)
                        {
                            return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                        });
                        res.Groups.Add(g);
                    }
                }
                if (res.Canceled) { break; }
            }

            MarkHardlinks(res);
            res.Groups.Sort(delegate(Group a, Group b)
            {
                int bySizeCmp = b.Size.CompareTo(a.Size);
                if (bySizeCmp != 0) { return bySizeCmp; }
                return string.Compare(a.Hash, b.Hash, StringComparison.Ordinal);
            });
            if (report != null) { report(prog); }
            return res;
        }

        private static List<long> SortedKeys(Dictionary<long, List<Entry>> map)
        {
            List<long> keys = new List<long>(map.Keys);
            keys.Sort();
            return keys;
        }

        private static List<string> SortedKeys(Dictionary<string, List<Entry>> map)
        {
            List<string> keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        private static void Walk(string dir, int depth, Options opt, Result res, List<Entry> files,
            Progress prog, Func<bool> cancel)
        {
            if (depth > 48) { return; }
            if (cancel != null && cancel()) { return; }
            string[] list = null;
            try { list = Directory.GetFiles(dir); }
            catch (Exception ex) { res.Errors.Add("读不了这个目录（跳过）：" + dir + " —— " + ex.Message); }
            if (list != null)
            {
                foreach (string f in list)
                {
                    if (cancel != null && cancel()) { return; }
                    res.Scanned++;
                    Entry e = MakeEntry(f, opt, res);
                    if (e != null) { files.Add(e); prog.Files = files.Count; }
                }
            }
            string[] subs = null;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception ex) { res.Errors.Add("读不了这个目录的子目录（跳过）：" + dir + " —— " + ex.Message); }
            if (subs == null) { return; }
            foreach (string sub in subs)
            {
                try
                {
                    FileAttributes at = File.GetAttributes(sub);
                    if ((at & FileAttributes.ReparsePoint) != 0) { continue; }
                }
                catch { continue; }
                Walk(sub, depth + 1, opt, res, files, prog, cancel);
                if (cancel != null && cancel()) { return; }
            }
        }

        private static Entry MakeEntry(string path, Options opt, Result res)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists) { return null; }
                if (fi.Length <= 0) { return null; }              // 0 字节：全是"重复"，没有意义
                if (fi.Length < opt.MinSize) { return null; }
                Entry e = new Entry();
                e.Path = fi.FullName;
                e.Size = fi.Length;
                e.Modified = fi.LastWriteTime;
                e.Identity = IdentityOf(e.Path);
                return e;
            }
            catch (Exception ex)
            {
                if (res.Errors.Count < 200) { res.Errors.Add("读不了这个文件（跳过）：" + path + " —— " + ex.Message); }
                return null;
            }
        }

        // ------------------------------------------------------------ 哈希

        /// <summary>算一个文件的 SHA256。limit &gt; 0 = 只读前 limit 个字节（前 4 KB 那一档）。
        /// 读不了返回 null（原因进 Errors）；被取消也返回 null（调用方自己看 cancel()）。</summary>
        private static string HashOf(string path, int limit, Func<bool> cancel, Progress prog, Result res)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] buffer = new byte[1024 * 1024];
                    long total = 0;
                    long cap = (limit > 0) ? limit : long.MaxValue;
                    int n;
                    while (total < cap && (n = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, cap - total))) > 0)
                    {
                        if (cancel != null && cancel()) { return null; }
                        sha.TransformBlock(buffer, 0, n, null, 0);
                        total += n;
                        if (limit <= 0) { prog.HashedBytes += n; }
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    prog.Hashed++;
                    return ToHex(sha.Hash);
                }
            }
            catch (Exception ex)
            {
                if (res.Errors.Count < 200) { res.Errors.Add("读不了这个文件（跳过）：" + path + " —— " + ex.Message); }
                return null;
            }
        }

        private static string ToHex(byte[] data)
        {
            if (data == null) { return ""; }
            System.Text.StringBuilder sb = new System.Text.StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++) { sb.Append(data[i].ToString("x2", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        // ------------------------------------------------------------ 硬链接

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out BY_HANDLE_FILE_INFORMATION info);

        /// <summary>卷序列号 + 文件索引 = 这份数据在磁盘上的唯一身份。两个路径同身份 = 硬链接。
        /// 读不到返回空串（那就没法判硬链接，退化成普通重复 —— 保守方向是不删，见 CLI）。</summary>
        private static string IdentityOf(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    BY_HANDLE_FILE_INFORMATION info;
                    if (!GetFileInformationByHandle(fs.SafeFileHandle, out info)) { return ""; }
                    return info.VolumeSerialNumber.ToString("x8", CultureInfo.InvariantCulture) + ":"
                        + info.FileIndexHigh.ToString("x8", CultureInfo.InvariantCulture) + ":"
                        + info.FileIndexLow.ToString("x8", CultureInfo.InvariantCulture);
                }
            }
            catch { return ""; }
        }

        private static void MarkHardlinks(Result res)
        {
            Dictionary<string, Entry> seen = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (Group g in res.Groups)
            {
                foreach (Entry e in g.Files)
                {
                    if (e.Identity.Length == 0) { continue; }
                    Entry first;
                    if (seen.TryGetValue(e.Identity, out first))
                    {
                        first.Hardlink = true;
                        e.Hardlink = true;
                    }
                    else
                    {
                        seen[e.Identity] = e;
                    }
                }
            }
        }

        // ------------------------------------------------------------ 保留哪一个 / 能省多少

        /// <summary>规则：oldest（最早修改的）/ shortest（路径最短的）/ first（按路径字母序第一个）。
        /// 认不出来的规则按 oldest 处理。</summary>
        public static Entry Suggest(Group g, string rule)
        {
            if (g == null || g.Files.Count == 0) { return null; }
            string r = (rule == null) ? "oldest" : rule.Trim().ToLowerInvariant();
            Entry best = g.Files[0];
            foreach (Entry e in g.Files)
            {
                if (r == "shortest")
                {
                    if (e.Path.Length < best.Path.Length) { best = e; }
                }
                else if (r == "first")
                {
                    if (string.Compare(e.Path, best.Path, StringComparison.OrdinalIgnoreCase) < 0) { best = e; }
                }
                else
                {
                    if (e.Modified < best.Modified) { best = e; }
                }
            }
            return best;
        }

        /// <summary>按当前规则和硬链接口径，删掉"除了保留那一个以外"的能省多少字节。
        /// 硬链接不算（删了不省空间）。</summary>
        public static long FreeableBytes(List<Group> groups, string rule)
        {
            long total = 0;
            foreach (Group g in groups)
            {
                Entry keep = Suggest(g, rule);
                foreach (Entry e in g.Files)
                {
                    if (e == keep || e.Hardlink) { continue; }
                    total += g.Size;
                }
            }
            return total;
        }

        /// <summary>按规则把"除了保留那一个、而且不是硬链接"的成员收集起来（给 --delete 用）。</summary>
        public static List<string> DeletablePaths(List<Group> groups, string rule)
        {
            List<string> paths = new List<string>();
            foreach (Group g in groups)
            {
                Entry keep = Suggest(g, rule);
                foreach (Entry e in g.Files)
                {
                    if (e == keep || e.Hardlink) { continue; }
                    paths.Add(e.Path);
                }
            }
            return paths;
        }

        /// <summary>解析 `--min-size=` 的几种写法：纯字节数 / 1KB / 2.5MB / 1GB（大小写不限）。
        /// 认不出来返回 -1（调用方报用法错误，别猜）。</summary>
        public static long ParseSize(string text)
        {
            if (text == null) { return -1; }
            string s = text.Trim();
            if (s.Length == 0) { return -1; }
            long unit = 1;
            string suffix = "";
            if (s.EndsWith("kb", StringComparison.OrdinalIgnoreCase)) { unit = 1024; suffix = "kb"; }
            else if (s.EndsWith("mb", StringComparison.OrdinalIgnoreCase)) { unit = 1024 * 1024; suffix = "mb"; }
            else if (s.EndsWith("gb", StringComparison.OrdinalIgnoreCase)) { unit = 1024L * 1024L * 1024L; suffix = "gb"; }
            else if (s.EndsWith("b", StringComparison.OrdinalIgnoreCase)) { unit = 1; suffix = "b"; }
            if (suffix.Length > 0) { s = s.Substring(0, s.Length - suffix.Length); }
            double number;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) { return -1; }
            if (number < 0) { return -1; }
            double bytes = number * unit;
            if (bytes > 9.0e15) { return -1; }
            return (long)Math.Round(bytes);
        }

        /// <summary>给人看的大小（和哈希校验那边同一套写法）。</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) { return bytes.ToString(CultureInfo.InvariantCulture) + " 字节"; }
            double kb = bytes / 1024.0;
            if (kb < 1024) { return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB"; }
            double mb = kb / 1024.0;
            if (mb < 1024) { return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB"; }
            return (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
