// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.IO;

namespace Mxx1Toolbox
{
    /// <summary>「空文件夹清理」的后端：从一个根目录往下找**真正空的**文件夹
    /// （里面一个文件都没有；只有空的子文件夹也算空 —— 它们会一起列出来）。
    ///
    /// 判定口径（宁可漏报，不可误删）：
    /// ① **有一个文件就不算空** —— 隐藏文件、系统文件（desktop.ini 那种）也算文件。
    ///    用户看到"这个文件夹是空的"但资源管理器里其实藏着 desktop.ini，删了会丢缩略图/自定义图标；
    /// ② junction / 符号链接**不钻进去**（也可能形成环），把它当成"有东西"→ 整个目录不算空；
    /// ③ 没权限读的目录**不当空**，单独列进"跳过"并说清原因；
    /// ④ 扫描根自己**永远不进列表、永远不会被删**（用户选 C:\ 也不会把 C:\ 删了）；
    /// ⑤ 根下面第一层的系统目录（$Recycle.Bin / System Volume Information / Windows /
    ///    Program Files / Program Files (x86)）不扫 —— 就算它们看起来是空的，也不是给这个工具删的；
    /// ⑥ 比 `--depth=` 更深的目录不判定为空（默认 32 层，正常目录结构到不了，防环也防误判）。
    ///
    /// 列表顺序是**由深到浅**（最里面那层先列）—— 删除也必须按这个顺序，不然父目录先没了，
    /// 子目录那一条就变成"路径不存在"，看起来像失败。</summary>
    internal static class EmptyFolders
    {
        public sealed class ScanResult
        {
            public string Root = "";
            public int Dirs = 0;                       // 一共看了多少个目录
            public readonly List<string> Empty = new List<string>();       // 由深到浅
            public readonly List<string> Skipped = new List<string>();     // 跳过的目录
            public readonly List<string> SkippedWhy = new List<string>();

            public void Skip(string dir, string why)
            {
                Skipped.Add(dir);
                SkippedWhy.Add(why);
            }
        }

        /// <summary>根下面第一层里"不碰"的系统目录（名字比较，忽略大小写）。</summary>
        private static readonly string[] SystemDirNames = new string[]
        {
            "$Recycle.Bin", "System Volume Information", "Windows",
            "Program Files", "Program Files (x86)"
        };

        public static ScanResult Scan(string root, int maxDepth)
        {
            ScanResult res = new ScanResult();
            res.Root = root;
            if (root == null || root.Trim().Length == 0)
            {
                res.Skip("", "没有指定要扫描的目录");
                return res;
            }
            try
            {
                if (!Directory.Exists(root))
                {
                    res.Skip(root, "这个目录不存在（或者不是目录）");
                    return res;
                }
            }
            catch (Exception ex)
            {
                res.Skip(root, "读不了这个目录：" + ex.Message);
                return res;
            }
            if (maxDepth <= 0) { maxDepth = 32; }
            try { Walk(Normalize(root), 0, 1, maxDepth, res); }
            catch (Exception ex) { res.Skip(root, "扫描中止：" + ex.Message); }
            return res;
        }

        private static string Normalize(string dir)
        {
            string full = Path.GetFullPath(dir);
            while (full.Length > 3 && (full.EndsWith("\\", StringComparison.Ordinal) || full.EndsWith("/", StringComparison.Ordinal)))
            {
                full = full.Substring(0, full.Length - 1);
            }
            return full;
        }

        /// <summary>返回"这个目录算不算空"（true = 空，可以列/删）。
        ///
        /// ⚠ 只要有一个子目录"看不透"（系统目录 / junction / 没权限 / 太深），父目录就一律
        /// **不算空** —— 这是这条功能唯一会删错东西的地方，宁可漏报一百个，也不能把一个
        /// 里面挂着 junction 的目录当空删掉。</summary>
        private static bool Walk(string dir, int depth, int rootDepth, int maxDepth, ScanResult res)
        {
            res.Dirs++;
            bool blocked = false;

            string[] files = null;
            try { files = Directory.GetFiles(dir); }
            catch (Exception ex)
            {
                res.Skip(dir, "没有权限读这个目录（当成「不空」）：" + shortMsg(ex));
                return false;
            }
            if (files != null && files.Length > 0) { return false; }

            string[] subs = null;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception ex)
            {
                res.Skip(dir, "没有权限读它的子目录（当成「不空」）：" + shortMsg(ex));
                return false;
            }

            foreach (string sub in subs)
            {
                // 系统目录只挡"根的下一层"：用户自己选了系统盘的 Windows 目录当根，就让他扫。
                if (depth == rootDepth - 1 && IsSystemDirName(Path.GetFileName(sub)))
                {
                    res.Skip(sub, "系统目录，不扫描也不删（就算看起来是空的）");
                    blocked = true;
                    continue;
                }
                try
                {
                    FileAttributes at = File.GetAttributes(sub);
                    if ((at & FileAttributes.ReparsePoint) != 0)
                    {
                        res.Skip(sub, "它是 junction / 符号链接（指向别处），不钻进去、也不当成空的");
                        blocked = true;
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    res.Skip(sub, "读不了它的属性（当成「不空」）：" + shortMsg(ex));
                    blocked = true;
                    continue;
                }

                if (depth + 1 > maxDepth)
                {
                    res.Skip(sub, "比 --depth 更深了，不判定为空（怕误判）");
                    blocked = true;
                    continue;
                }
                if (!Walk(sub, depth + 1, rootDepth, maxDepth, res)) { blocked = true; }
            }

            if (blocked) { return false; }
            // 扫描根自己**永远不进列表**：用户选 C:\ 的时候，就算它看起来是空的也绝不能
            // 把它送去回收站（这条是从函数说明里抄下来的硬口径，回归 Y09 盯着）。
            if (depth > 0) { res.Empty.Add(dir); }
            return true;
        }

        private static bool IsSystemDirName(string name)
        {
            foreach (string n in SystemDirNames)
            {
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private static string shortMsg(Exception ex)
        {
            string m = (ex == null) ? "" : ex.Message;
            m = m.Replace("\r", " ").Replace("\n", " ").Trim();
            if (m.Length > 120) { m = m.Substring(0, 120) + "…"; }
            return m;
        }

        /// <summary>删一批空目录（走回收站）。**由深到浅**排好序再删。返回的结果里
        /// Failed 是"删完还在的"，FailedWhy 与之一一对应。</summary>
        public static Recycle.DeleteResult Delete(List<string> dirs)
        {
            List<string> ordered = new List<string>();
            if (dirs != null) { ordered.AddRange(dirs); }
            ordered.Sort(delegate(string a, string b)
            {
                int byLen = b.Length.CompareTo(a.Length);
                if (byLen != 0) { return byLen; }
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            return Recycle.DeleteToRecycleBin(ordered);
        }
    }
}
