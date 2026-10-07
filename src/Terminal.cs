// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Mxx1Toolbox
{
    /// <summary>「在此处打开终端」—— 在指定目录里开一个命令行窗口。
    ///
    /// 谁在用：① 右键菜单里**那两个**菜单项（`src\RightMenu.cs` 的 TerminalCmdVerb / TerminalPsVerb）
    /// —— 三个位置：右键文件夹（`%1`）、文件夹里的空白处（`%V`= 当前这个文件夹）、桌面空白处
    /// （`%V`= 桌面）；那两条命令各把终端**钉死**（`--cmd` / `--ps`），因为用户 2026-10-06 晚六要的是
    /// 「多选 一个是cmd 另外一个是powershell」；② 命令行 `terminal [<目录>] [--wt|--ps|--cmd] [--dry]`。
    ///
    /// 不点名时用哪一个终端（顺序写死，不猜用户的偏好）：**Windows Terminal → Windows PowerShell → cmd**。
    /// Win7 / 精简版 Win10 上没有 wt.exe 就落到 PowerShell（系统自带，一定有）。
    /// 真正"用了哪个"会**如实报出来**（输出里的 `terminal=`、日志、提示卡都写）——
    /// 用户点完只看到"冒出来一个黑窗口"，至少得知道那是哪一个。
    ///
    /// 边界（这几条都是用户会真的撞上的）：
    ///   · **不提权**：不开 UAC、不写 HKLM，就是一个普通用户的终端窗口；
    ///   · 目录不存在**直接报错**，不"退到上一层"也不硬开（猜出来的目录比报错更糟）；
    ///   · 路径里有空格 / 中文 / `&` / `'` 都要正确传递 —— wt 与 cmd 走双引号，PowerShell 走
    ///     `Set-Location -LiteralPath '<目录>'`（单引号里把 `'` 写成 `''`），这样 `&` 不会被当成命令分隔符；
    ///   · `--dry` 只把"会跑哪条命令"打印出来、**不起窗口**（回归测试靠它验引号，免得在别人桌面上
    ///     连开一堆终端窗口）。</summary>
    internal static class Terminal
    {
        public const string LogTag = "在此处打开终端";

        /// <summary>用哪个终端。Auto = 按 wt → powershell → cmd 的顺序挑。</summary>
        public const string Auto = "auto";
        public const string Wt = "wt";
        public const string PowerShell = "powershell";
        public const string Cmd = "cmd";

        /// <summary>给人看的名字（提示卡与输出里那句"用的是哪一个"）。</summary>
        public static string DisplayName(string kind)
        {
            if (kind == Wt) { return "Windows Terminal"; }
            if (kind == PowerShell) { return "Windows PowerShell"; }
            if (kind == Cmd) { return "命令提示符 cmd"; }
            return kind;
        }

        /// <summary>跑一次。`want` = auto / wt / powershell / cmd（用户用 `--wt` 那种开关点名要哪个）。
        /// 返回退出码：0 = 起来了（或 `--dry` 打印完了），2 = 用法错 / 目录不存在 / 这个终端没有。</summary>
        public static int Run(string dir, string want, bool dry, bool notify, int notifyMs)
        {
            string target;
            string error = ResolveDir(dir, out target);
            if (error.Length > 0)
            {
                Console.Error.WriteLine(error);
                Console.WriteLine("error=" + error);
                Logger.Write(LogTag, error);
                if (notify) { Balloon.Show("在此处打开终端 · 没打开", error, notifyMs, NoticeKind.Warn); }
                return 2;
            }

            string kind;
            string exe;
            string pickError = Pick(want, out kind, out exe);
            if (pickError.Length > 0)
            {
                Console.Error.WriteLine(pickError);
                Console.WriteLine("error=" + pickError);
                Logger.Write(LogTag, pickError);
                if (notify) { Balloon.Show("在此处打开终端 · 没打开", pickError, notifyMs, NoticeKind.Warn); }
                return 2;
            }

            string args = Arguments(kind, exe, target);
            Console.WriteLine("dir=" + target);
            Console.WriteLine("terminal=" + kind);
            Console.WriteLine("terminalname=" + DisplayName(kind));
            Console.WriteLine("exe=" + exe);
            Console.WriteLine("args=" + args);

            if (dry)
            {
                Console.WriteLine("started=dry");
                return 0;
            }

            int pid;
            string startError = Start(exe, args, target, out pid);
            if (startError.Length > 0)
            {
                Console.WriteLine("started=no");
                Console.Error.WriteLine("终端没起来：" + startError);
                Logger.Write(LogTag, "没起来（" + DisplayName(kind) + "）：" + startError);
                if (notify) { Balloon.Show("在此处打开终端 · 没打开", "没起来：" + startError, notifyMs, NoticeKind.Fail); }
                return 2;
            }

            Console.WriteLine("started=yes");
            Console.WriteLine("pid=" + pid.ToString(CultureInfo.InvariantCulture));
            Logger.Write(LogTag, "在 " + target + " 里打开了 " + DisplayName(kind) + "（pid="
                + pid.ToString(CultureInfo.InvariantCulture) + "）");
            if (notify)
            {
                Balloon.Show("在此处打开终端", "已在 " + target + " 里打开" + DisplayName(kind) + "。", notifyMs, NoticeKind.Ok);
            }
            return 0;
        }

        /// <summary>要开在哪个目录。空 = 当前目录（命令行用）。返回非空 = 出错原因。</summary>
        private static string ResolveDir(string dir, out string full)
        {
            full = "";
            string d = (dir == null) ? "" : AppPaths.Expand(dir).Trim().Trim('"');
            // 只有一个盘符的（`D:`）补上反斜杠 = 那个盘的根：右键**盘的根**时占位符换成的是 `D:\`，
            // 而 `"D:\"` 在命令行解析里那个反斜杠会把引号吃掉（`\"` = 转义引号），我们收到的是 `D:"`。
            // 不补的话 `Path.GetFullPath("D:")` 会当成"这个盘上的当前目录"，随 cwd 漂。
            if ((d.Length == 2) && (d[1] == ':')) { d += "\\"; }
            if (d.Length == 0) { d = Environment.CurrentDirectory; }
            try
            {
                full = Path.GetFullPath(d);
            }
            catch (Exception ex)
            {
                return "这个路径认不出来：" + d + "（" + ex.Message + "）";
            }
            bool exists = false;
            try { exists = Directory.Exists(full); }
            catch { }
            if (!exists)
            {
                return "没有这个文件夹：" + full + "（路径不存在就直说，不乱开一个目录）";
            }
            return "";
        }

        /// <summary>挑一个终端：点名要的那个优先（点了名却找不到就报错，不偷偷换成别的），
        /// 否则按 wt → PowerShell → cmd 的顺序。返回非空 = 没得用。</summary>
        private static string Pick(string want, out string kind, out string exe)
        {
            kind = "";
            exe = "";
            string wt = FindWt();
            if (want == Wt)
            {
                if (wt.Length == 0) { return "这台机器上没有 Windows Terminal（wt.exe）—— 用 --ps 或 --cmd 换一个"; }
                kind = Wt; exe = wt;
                return "";
            }
            if (want == PowerShell) { kind = PowerShell; exe = PsExe(); return ""; }
            if (want == Cmd) { kind = Cmd; exe = CmdExe(); return ""; }

            if (wt.Length > 0) { kind = Wt; exe = wt; return ""; }
            if (File.Exists(PsExe())) { kind = PowerShell; exe = PsExe(); return ""; }
            if (File.Exists(CmdExe())) { kind = Cmd; exe = CmdExe(); return ""; }
            return "这台机器上既没有 Windows Terminal，也没找到 PowerShell 和 cmd —— 没法开终端";
        }

        /// <summary>命令行参数。三条路各写各的引号规矩（见类说明里的边界那一段）。</summary>
        private static string Arguments(string kind, string exe, string dir)
        {
            if (kind == Wt)
            {
                // wt -d "<目录>"：wt 自己按引号解析，空格 / 中文都没问题
                return "-d \"" + dir + "\"";
            }
            if (kind == PowerShell)
            {
                // 单引号里的路径原样传给 Set-Location；单引号本身写成两个（PowerShell 的转义规矩），
                // 这样路径里带 & / 空格 / 中文都不会被当成命令的一部分
                return "-NoExit -Command \"Set-Location -LiteralPath '" + dir.Replace("'", "''") + "'\"";
            }
            return "/K cd /d \"" + dir + "\"";
        }

        /// <summary>起进程。UseShellExecute=true：控制台程序要靠它**开一个自己的窗口**
        /// （工具箱自己是 winexe、没有控制台，重定向那套在这里等于把终端藏起来）。</summary>
        private static string Start(string exe, string args, string dir, out int pid)
        {
            pid = 0;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = true;
                psi.WorkingDirectory = dir;
                Process p = Process.Start(psi);
                if (p != null)
                {
                    try { pid = p.Id; } catch { pid = 0; }
                }
                return "";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string PsExe()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell\\v1.0\\powershell.exe");
        }

        private static string CmdExe()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        }

        /// <summary>找 wt.exe：先看 `%LOCALAPPDATA%\Microsoft\WindowsApps\`（应用商店装的
        /// Windows Terminal 就是那个"应用执行别名"），再沿 PATH 找一遍。找不到返回空。</summary>
        private static string FindWt()
        {
            try
            {
                string alias = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft\\WindowsApps\\wt.exe");
                if (File.Exists(alias)) { return alias; }
            }
            catch { }
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (path == null) { return ""; }
                List<string> seen = new List<string>();
                foreach (string part in path.Split(';'))
                {
                    string d = part.Trim().Trim('"');
                    if (d.Length == 0 || Contains(seen, d)) { continue; }
                    seen.Add(d);
                    try
                    {
                        string candidate = Path.Combine(d, "wt.exe");
                        if (File.Exists(candidate)) { return candidate; }
                    }
                    catch { }
                }
            }
            catch { }
            return "";
        }

        private static bool Contains(List<string> list, string value)
        {
            foreach (string s in list)
            {
                if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }
    }
}
