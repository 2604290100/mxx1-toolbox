// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.IO;

namespace Mxx1Toolbox
{
    /// <summary>「无效快捷方式清理」的后端：扫桌面和开始菜单里的 .lnk，解析它指向哪里，
    /// 把**目标已经不在的**挑出来。窗口（BrokenLinksForm）和命令行（`brokenlinks`）共用这一份。
    ///
    /// 扫描范围（四个目录，一个不多一个不少 —— 这是用户天天会双击到快捷方式的地方）：
    ///   ① 当前用户桌面  ② 所有用户桌面（Win7 起都在 %PUBLIC%\Desktop）
    ///   ③ 当前用户开始菜单  ④ 所有用户开始菜单
    /// 子文件夹会递归进去（开始菜单本来就是一层层目录）。
    ///
    /// 解析走 ShellLink 的 **IShellLinkW**（Unicode 接口，见 src\ShellLinkFile.cs）—— 比自己解 .lnk
    /// 的二进制格式靠谱得多，而且它在 Win7～Win11 上一直都在。
    /// ⚠️ 以前用的是 WScript.Shell：那是 ANSI 组件，在**代码页不是中文**的 Windows 上（英文系统 /
    /// GitHub runner）中文文件名的快捷方式根本打不开，会被一律当成"读不出来"（2026-10-21 CI 抓到）。
    ///
    /// 分五类（**只有第一类会被删**，其余都只是"报出来"）：
    ///   broken    目标没了（文件/文件夹都找不到）—— 这一类的勾选框才能打勾、才允许删
    ///   network   指向 \\服务器\共享 —— 网络位置回收站管不到（删了就真没了），所以只报不删
    ///   removable 指向的盘现在没挂着（U 盘 / 移动硬盘没插）—— 不删（它可能只是没插）
    ///   skip      刻意的"跳过"：商店应用（没有文件目标）、shell: 位置、读不出来的相对目标 —— 一律不删
    ///   ok        目标好好的（不显示在列表里，只进统计）
    ///
    /// ⚠️ 测试用 `MXX1_LNK_ROOTS`（分号分隔）把扫描根指到夹具目录 —— 绝不去动用户真实桌面上的东西。</summary>
    internal static class Shortcuts
    {
        /// <summary>测试用的扫描根覆盖（分号分隔）。正式跑不设它，用下面四个真实目录。</summary>
        public const string RootsEnv = "MXX1_LNK_ROOTS";

        public sealed class LinkInfo
        {
            public string Path = "";      // .lnk 的完整路径
            public string Target = "";    // 解析出来的目标（可能为空）
            public string Args = "";      // 快捷方式的参数（只显示用，不参与判断）
            public string Kind = "ok";    // ok / broken / network / removable / skip
            public string Note = "";      // 人话原因（kind != ok 时都有）

            public string Name
            {
                get
                {
                    try { return System.IO.Path.GetFileNameWithoutExtension(Path); }
                    catch { return Path; }
                }
            }

            public string Dir
            {
                get
                {
                    try { return System.IO.Path.GetDirectoryName(Path); }
                    catch { return ""; }
                }
            }

            /// <summary>给人看的那一列（"坏链（目标不在了）"这种）。</summary>
            public string KindText
            {
                get
                {
                    switch (Kind)
                    {
                        case "broken": return "坏链";
                        case "network": return "网络位置";
                        case "removable": return "可移动盘";
                        case "skip": return "跳过";
                        default: return "正常";
                    }
                }
            }
        }

        /// <summary>默认的四个扫描根（也可以被环境变量覆盖，测试用）。</summary>
        public static string[] ScanRoots()
        {
            string env = Environment.GetEnvironmentVariable(RootsEnv);
            if (env != null && env.Trim().Length > 0)
            {
                List<string> custom = new List<string>();
                foreach (string part in env.Split(';'))
                {
                    if (part != null && part.Trim().Length > 0) { custom.Add(part.Trim()); }
                }
                return custom.ToArray();
            }

            List<string> roots = new List<string>();
            AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
            return roots.ToArray();
        }

        private static void AddIfExists(List<string> list, string dir)
        {
            try
            {
                if (dir != null && dir.Length > 0 && Directory.Exists(dir) && !list.Contains(dir)) { list.Add(dir); }
            }
            catch { }
        }

        /// <summary>扫一遍。errors 里是"个别目录读不进去"这种非致命问题（照扫，只是少扫一点）。</summary>
        public static List<LinkInfo> Scan(string[] roots, out List<string> errors)
        {
            errors = new List<string>();
            List<LinkInfo> found = new List<LinkInfo>();
            if (!ShellLinkFile.IsAvailable())
            {
                errors.Add("这台机器上叫不起 ShellLink（解析快捷方式要用它）—— 一个快捷方式都没法检查。");
                return found;
            }

            foreach (string root in roots)
            {
                List<string> files = new List<string>();
                Collect(root, 0, files, errors);
                foreach (string lnk in files)
                {
                    found.Add(Classify(lnk));
                }
            }
            return found;
        }

        /// <summary>递归收 .lnk。junction / 符号链接（ReparsePoint）**不钻进去**：
        /// 开始菜单里偶尔有指向别处的目录链接，钻进去会扫到扫描范围以外的快捷方式。</summary>
        private static void Collect(string dir, int depth, List<string> files, List<string> errors)
        {
            if (depth > 24) { return; }   // 防环（正常目录结构到不了这么深）
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.lnk"))
                {
                    files.Add(f);
                }
            }
            catch (Exception ex)
            {
                errors.Add("读不了这个目录（跳过）:" + dir + " —— " + ex.Message);
                return;
            }
            try
            {
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    try
                    {
                        FileAttributes at = File.GetAttributes(sub);
                        if ((at & FileAttributes.ReparsePoint) != 0) { continue; }
                    }
                    catch { continue; }
                    Collect(sub, depth + 1, files, errors);
                }
            }
            catch (Exception ex)
            {
                errors.Add("读不了这个目录下的子目录（跳过）:" + dir + " —— " + ex.Message);
            }
        }

        private static LinkInfo Classify(string lnkPath)
        {
            LinkInfo li = new LinkInfo();
            li.Path = lnkPath;
            string target = "";
            string args = "";
            string workDir = "";
            ShellLinkFile.ReadResult rr = ShellLinkFile.Read(lnkPath, out target, out args, out workDir);
            if (rr == ShellLinkFile.ReadResult.Unreadable)
            {
                // 读不出来 = 这个 .lnk 文件本身坏了（被截断 / 不是快捷方式），不是"目标不在了"。
                // 这类文件删掉不会有别的损失，但仍然和"坏链"分开说。
                li.Kind = "broken";
                li.Note = "读不出这个快捷方式本身（文件可能坏了）—— 它指向哪里看不出来，删了不会有别的损失";
                return li;
            }

            li.Target = target;
            li.Args = args;
            if (target.Trim().Length == 0)
            {
                li.Kind = "skip";
                li.Note = "没有可解析的文件目标（可能是商店应用，或者系统内置位置）—— 不碰它";
                return li;
            }

            string expanded = target;
            try { expanded = Environment.ExpandEnvironmentVariables(target); }
            catch { }

            if (expanded.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || expanded.StartsWith("::"))
            {
                li.Kind = "skip";
                li.Note = "指向的是系统位置（shell: 那种），不是文件路径 —— 不碰它";
                return li;
            }
            if (expanded.StartsWith("\\\\", StringComparison.Ordinal))
            {
                li.Kind = "network";
                li.Note = "指向网络位置（\\\\服务器\\共享）—— 回收站管不到网络，删了就真没了，自己看过再删";
                return li;
            }
            if (!(expanded.Length >= 2 && expanded[1] == ':'))
            {
                li.Kind = "skip";
                li.Note = "目标不是完整的盘符路径（相对路径或者特殊写法）—— 看不出指向哪里，不碰它";
                return li;
            }

            char drive = char.ToUpperInvariant(expanded[0]);
            if (!HasDrive(drive))
            {
                li.Kind = "removable";
                li.Note = "这个盘现在没挂着（U 盘 / 移动硬盘没插，或者盘符变了）—— 先别删";
                return li;
            }
            try
            {
                DriveInfo di = new DriveInfo(drive.ToString() + ":\\");
                if (di.DriveType == DriveType.Removable && !di.IsReady)
                {
                    li.Kind = "removable";
                    li.Note = "可移动盘现在插着但读不出来 —— 先别删";
                    return li;
                }
            }
            catch { }

            bool exists = false;
            try { exists = File.Exists(expanded) || Directory.Exists(expanded); }
            catch { exists = false; }
            if (exists)
            {
                li.Kind = "ok";
                return li;
            }

            li.Kind = "broken";
            li.Note = "目标不在了：" + expanded;
            return li;
        }

        private static bool HasDrive(char letter)
        {
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.Name.Length >= 2 && char.ToUpperInvariant(d.Name[0]) == letter) { return true; }
                }
            }
            catch { }
            return false;
        }

        /// <summary>删掉这批里**唯一的 broken**（调用方即使把别的类别混进来也不会误删；
        /// 其余类别一个都不碰）。走回收站。</summary>
        public static Recycle.DeleteResult DeleteBroken(List<LinkInfo> links)
        {
            List<string> paths = new List<string>();
            foreach (LinkInfo li in links)
            {
                if (li != null && li.Kind == "broken") { paths.Add(li.Path); }
            }
            return Recycle.DeleteToRecycleBin(paths);
        }

        /// <summary>统计用：某个类别的条数。</summary>
        public static int CountKind(List<LinkInfo> links, string kind)
        {
            int n = 0;
            foreach (LinkInfo li in links)
            {
                if (li != null && string.Equals(li.Kind, kind, StringComparison.Ordinal)) { n++; }
            }
            return n;
        }
    }
}
