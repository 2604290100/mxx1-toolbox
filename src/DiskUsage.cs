// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Mxx1Toolbox
{
    /// <summary>一个目录占了多少（只读统计的结果）。</summary>
    internal sealed class DirUsage
    {
        public string Name = "";
        public string Path = "";
        public long Bytes;
        public long Files;
        public long Dirs;
        public int Junctions;      // 跳过了几个 junction / 符号链接
        public int Denied;         // 多少个目录 / 文件读不进去（没权限）
        public bool IsRootFiles;   // "根目录下散着的文件"那一行（不是一个子目录）
    }

    /// <summary>「C 盘空间体检」的后端（GAP-04）—— **只读**统计某个目录下面每个直接子目录占多大。
    ///
    /// 为什么要自己走目录而不是 `Get-ChildItem -Recurse | Measure-Object`：
    /// ① PowerShell 那条路**会跟着 junction 走**（用户目录下 `All Users` 那种链接），要么算重、要么绕圈；
    ///    这里每个目录都先看 `FileAttributes.ReparsePoint`，是链接就只记一笔、不钻进去；
    /// ② 没权限的目录（System Volume Information / 别人的用户目录）不能让整次统计失败 ——
    ///    这里逐目录 try/catch，读不进去就记进 `Denied`，最后如实说"有 N 处没读进去"；
    /// ③ 只读：一个文件都不删、一个属性都不改（清理是别的按钮的事）。
    ///
    /// ⚠️ 统计要遍历整棵子树，慢是正常的（Windows 目录动辄十几万个文件）；
    ///    窗口里必须在**后台线程**跑，否则窗口会假死（PITFALLS 78/79 那类）。</summary>
    internal static class DiskUsage
    {
        /// <summary>最近一次失败的原因（窗口 / 命令行直接显示这句）。</summary>
        public static string LastError = "";

        /// <summary>统计 root 下面每个直接子目录的大小，按占用从大到小排好。
        /// `progress` 每统计完一个顶层目录回调一次（窗口用它刷新"正在统计 xxx"）。</summary>
        public static List<DirUsage> Scan(string root, Action<string> progress)
        {
            LastError = "";
            List<DirUsage> list = new List<DirUsage>();
            if (string.IsNullOrEmpty(root)) { LastError = "没给要统计的目录"; return list; }
            root = root.TrimEnd('\\');
            if (root.Length == 2 && root[1] == ':') { root += "\\"; }
            if (!Directory.Exists(root)) { LastError = "没有这个目录：" + root; return list; }

            // ① 根目录自己散着的文件（不属任何一个子目录，但也占地方）
            DirUsage loose = new DirUsage();
            loose.Name = "（根目录下的文件）";
            loose.Path = root;
            loose.IsRootFiles = true;
            try
            {
                foreach (string f in Directory.GetFiles(root))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        loose.Bytes += fi.Length;
                        loose.Files++;
                    }
                    catch { loose.Denied++; }
                }
            }
            catch { loose.Denied++; }
            if (loose.Files > 0) { list.Add(loose); }

            // ② 每个直接子目录
            string[] subs;
            try { subs = Directory.GetDirectories(root); }
            catch (Exception ex) { LastError = "读不了这个目录的子目录：" + ex.Message; return list; }

            Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
            foreach (string sub in subs)
            {
                DirUsage du = new DirUsage();
                du.Path = sub;
                du.Name = Path.GetFileName(sub);
                try
                {
                    FileAttributes a = File.GetAttributes(sub);
                    if ((a & FileAttributes.ReparsePoint) != 0)
                    {
                        // 本身就是一个链接（比如某些"文档"重定向）—— 只记一笔，不钻进去
                        du.Junctions = 1;
                        du.Name = du.Name + "（链接，没跟进去）";
                        list.Add(du);
                        if (progress != null) { progress(du.Name); }
                        continue;
                    }
                }
                catch
                {
                    du.Denied++;
                    list.Add(du);
                    if (progress != null) { progress(du.Name); }
                    continue;
                }

                Walk(sub, du);
                list.Add(du);
                if (progress != null) { progress(du.Name); }
            }

            list.Sort(delegate(DirUsage x, DirUsage y) { return y.Bytes.CompareTo(x.Bytes); });
            return list;
        }

        /// <summary>把一个目录整棵子树累加进 du（不跟 junction / 符号链接）。</summary>
        private static void Walk(string dir, DirUsage du)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(cur))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(f);
                            du.Bytes += fi.Length;
                            du.Files++;
                        }
                        catch { du.Denied++; }
                    }
                }
                catch { du.Denied++; }

                try
                {
                    foreach (string d in Directory.GetDirectories(cur))
                    {
                        try
                        {
                            FileAttributes a = File.GetAttributes(d);
                            if ((a & FileAttributes.ReparsePoint) != 0) { du.Junctions++; continue; }
                            du.Dirs++;
                            stack.Push(d);
                        }
                        catch { du.Denied++; }
                    }
                }
                catch { du.Denied++; }
            }
        }

        /// <summary>这个盘还剩多少 / 一共多大（读不到就给 -1）。</summary>
        public static void DriveSpace(string root, out long free, out long total)
        {
            free = -1;
            total = -1;
            try
            {
                string r = Path.GetPathRoot(Path.GetFullPath(root));
                if (string.IsNullOrEmpty(r)) { return; }
                DriveInfo di = new DriveInfo(r);
                if (!di.IsReady) { return; }
                free = di.AvailableFreeSpace;
                total = di.TotalSize;
            }
            catch { }
        }

        /// <summary>人话的"这一行要补一句什么"：读不进去 / 跳过链接都如实写出来。</summary>
        public static string Note(DirUsage du)
        {
            List<string> parts = new List<string>();
            if (du.Junctions > 0)
            {
                parts.Add(du.IsRootFiles ? "跳过 " + du.Junctions.ToString(CultureInfo.InvariantCulture) + " 个链接"
                    : "里面跳过 " + du.Junctions.ToString(CultureInfo.InvariantCulture) + " 个链接（junction / 符号链接，没跟进去，免得算重）");
            }
            if (du.Denied > 0)
            {
                parts.Add(du.Denied.ToString(CultureInfo.InvariantCulture) + " 处没权限读（数字可能偏小）");
            }
            return string.Join("；", parts.ToArray());
        }
    }
}
