// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args != null && args.Length > 0)
            {
                Native.AttachParentConsole();
                return Cli.Run(args);
            }

            Logger.PruneOld(Settings.Load().LogKeepDays);
            Application.Run(new MainForm());
            return 0;
        }
    }

    /// <summary>Command line used by scripts and by the test suites.
    /// Exit codes: 0 = ok, 1 = the action failed, 2 = wrong usage / unknown button.</summary>
    internal static class Cli
    {
        public static int Run(string[] args)
        {
            // Console.OutputEncoding cannot be changed when stdout is a pipe (that setter needs a real
            // console), so install a UTF-8 writer by hand: otherwise the Chinese button names reach
            // the caller as GBK mojibake.
            if (Console.IsOutputRedirected)
            {
                try
                {
                    StreamWriter utf8 = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                    utf8.AutoFlush = true;
                    Console.SetOut(utf8);
                }
                catch { }
            }
            // stderr needs the same treatment: error messages are Chinese too, and without this the
            // caller reads "用法:" / "找不到..." as GBK mojibake (caught by the draft regression G06).
            if (Console.IsErrorRedirected)
            {
                try
                {
                    StreamWriter utf8err = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
                    utf8err.AutoFlush = true;
                    Console.SetError(utf8err);
                }
                catch { }
            }

            string command = args[0].ToLowerInvariant().TrimStart('-', '/');
            try
            {
                switch (command)
                {
                    case "list": return List(args);
                    case "run": return RunOne(args);
                    case "draft": return Draft(args);
                    case "status": return Status();
                    case "tip": return Tip(args);
                    case "privacy": return PrivacyCommand(args);
                    case "checkupdate": return CheckUpdate();
                    case "help":
                    case "h":
                    case "?":
                        Help();
                        return 0;
                    default:
                        Console.Error.WriteLine("不认识的命令: " + args[0]);
                        Help();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("错误: " + ex.Message);
                return 1;
            }
        }

        private static void Help()
        {
            Console.WriteLine("萌新工具箱 v" + AboutForm.VersionText);
            Console.WriteLine();
            Console.WriteLine("用法:");
            Console.WriteLine("  Mxx1Toolbox.exe                      打开界面（不带参数时）");
            Console.WriteLine("  Mxx1Toolbox.exe list [--tab <页签>]  列出全部按钮");
            Console.WriteLine("  Mxx1Toolbox.exe run <id> [--admin]   执行一个按钮（和界面同一条路径）");
            Console.WriteLine("  Mxx1Toolbox.exe run <id> --dry       只解析按钮指向哪里，不真的启动");
            Console.WriteLine("  Mxx1Toolbox.exe draft <路径>         把文件/文件夹按「拖进窗口」的规则变成按钮草稿");
            Console.WriteLine("  Mxx1Toolbox.exe status               打印 key=value 状态（脚本用）");
            Console.WriteLine("  Mxx1Toolbox.exe tip [id]             打印按钮的悬停说明（界面上鼠标停住时看到的那段）");
            Console.WriteLine("  Mxx1Toolbox.exe privacy status       只读列出隐私开关的当前状态（不改任何东西）");
            Console.WriteLine("  Mxx1Toolbox.exe privacy selftest     用工具箱自己的测试键自检「原值 → 写入 → 还原」链路");
            Console.WriteLine("  Mxx1Toolbox.exe checkupdate          只读版本号，不下载不替换");
            Console.WriteLine("  Mxx1Toolbox.exe help                 这份帮助");
            Console.WriteLine();
            Console.WriteLine("页签 id: " + string.Join(" / ", Tabs.Ids));
        }

        /// <summary>Prints the hover text of every button (or of one id). It is the very same string
        /// MainForm.TipFor hands to the ToolTip control, so the test suite can prove a tooltip is a
        /// readable sentence instead of a screen-wide command line -- without moving the mouse.</summary>
        private static int Tip(string[] args)
        {
            string want = (args.Length > 1 && !args[1].StartsWith("-")) ? args[1] : "";
            List<ToolItem> tools = Load();
            Settings settings = Settings.Load();
            int shown = 0;
            foreach (ToolItem t in tools)
            {
                if (want.Length > 0 && !string.Equals(t.Id, want, StringComparison.OrdinalIgnoreCase)) { continue; }
                Console.WriteLine("--- " + t.Id);
                Console.WriteLine(TipText(t, settings));
                shown++;
            }
            Console.WriteLine("tips=" + shown.ToString(CultureInfo.InvariantCulture));
            if (want.Length > 0 && shown == 0)
            {
                Console.Error.WriteLine("没有这个按钮: " + want);
                return 2;
            }
            return 0;
        }

        private static string TipText(ToolItem t, Settings settings)
        {
            return MainForm.TipFor(t, settings);
        }

        /// <summary>`privacy status` 只读地列出每个隐私开关现在是什么状态；`privacy selftest` 用工具箱
        /// 自己的一个测试键把「记下原值 → 写入 → 读回核对 → 还原」整条链路走一遍。
        /// 命令行**故意不提供**"真的去改隐私设置"的入口：那只能从界面点，而且要么弹确认框、
        /// 要么是明确标着"可一键还原"的成对开关。自检只碰
        /// HKCU\Software\mxx1-toolbox\privacy-selftest，不碰任何真实设置。</summary>
        private static int PrivacyCommand(string[] args)
        {
            string what = (args.Length > 1) ? args[1].ToLowerInvariant() : "status";
            if (what == "status")
            {
                Console.Write(Privacy.Status());
                return 0;
            }
            if (what == "items")
            {
                foreach (PrivacyItem i in Privacy.All)
                {
                    Console.WriteLine(i.Id + "\t" + i.Name + "\t" + i.Values.Length + " 个值\t"
                        + (i.Admin ? "要管理员" : "不用管理员"));
                }
                return 0;
            }
            if (what == "selftest")
            {
                bool ok;
                string report = Privacy.SelfTest(out ok);
                Console.Write(report);
                Console.WriteLine("selftest=" + (ok ? "pass" : "fail"));
                return ok ? 0 : 1;
            }
            Console.Error.WriteLine("用法: privacy status | items | selftest");
            return 2;
        }

        private static List<ToolItem> Load()
        {
            List<string> warnings = new List<string>();
            List<ToolItem> tools = ToolRegistry.LoadAll(warnings);
            foreach (string w in warnings) { Console.Error.WriteLine("警告: " + w); }
            return tools;
        }

        private static int List(string[] args)
        {
            string tab = "";
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--tab" && i + 1 < args.Length) { tab = args[i + 1]; }
            }
            List<ToolItem> tools = Load();
            int shown = 0;
            foreach (ToolItem t in tools)
            {
                if (tab.Length > 0 && !string.Equals(t.Tab, ToolItem.NormalizeTab(tab), StringComparison.OrdinalIgnoreCase)) { continue; }
                Console.WriteLine(t.Describe());
                shown++;
            }
            Console.WriteLine("buttons=" + tools.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("shown=" + shown.ToString(CultureInfo.InvariantCulture));
            foreach (string id in Tabs.Ids)
            {
                int n = 0;
                foreach (ToolItem t in tools) { if (t.Tab == id) { n++; } }
                Console.WriteLine("tab." + id + "=" + n.ToString(CultureInfo.InvariantCulture));
            }
            return 0;
        }

        private static int RunOne(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("用法: Mxx1Toolbox.exe run <id> [--admin] [--dry]");
                return 2;
            }
            string id = args[1];
            bool admin = false;
            bool dry = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--admin") { admin = true; }
                if (args[i] == "--dry") { dry = true; }
            }

            ToolItem target = null;
            foreach (ToolItem t in Load())
            {
                if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) { target = t; break; }
            }
            if (target == null)
            {
                Console.Error.WriteLine("没有这个按钮: " + id);
                return 2;
            }

            Settings settings = Settings.Load();
            string command = Launcher.DescribeCommand(target, settings, admin);
            Console.WriteLine("id=" + target.Id);
            Console.WriteLine("name=" + target.Name);
            Console.WriteLine("command=" + command);

            // --dry only resolves the target (and says whether it is there) without starting
            // anything: that is how the test suite checks all twelve 系统工具 buttons without
            // opening Device Manager twelve times.
            if (dry) { return DryRun(target, settings); }

            if (target.Placeholder)
            {
                string hint = target.Hint.Length > 0 ? target.Hint : "P1";
                Console.WriteLine("placeholder=" + hint);
                Logger.Write(target.Name, "功能待接入（" + hint + "）");
                return 0;
            }
            if (target.Kind == "builtin" && target.Module == Launcher.ModuleApp)
            {
                Console.WriteLine("uiAction=" + target.Action);
                Console.WriteLine("[提示] 这是界面动作，命令行不执行。");
                return 0;
            }

            LaunchResult r = Launcher.Run(target, settings, admin);
            // 需要管理员的动作是"提升权限后另起一个自己"去做的，而那个子进程是 winexe、没有控制台：
            // 它把报告写在这个交接文件里，没提升权限的父进程几秒后读出来弹给用户看。
            if (admin && r.Output != null && r.Output.Trim().Length > 0)
            {
                try
                {
                    AppPaths.EnsureBase();
                    File.WriteAllText(AppPaths.ElevatedResultFile, r.Output, new UTF8Encoding(false));
                }
                catch { }
            }
            if (r.Output != null && r.Output.Length > 0) { Console.WriteLine(r.Output); }
            Console.WriteLine("result=" + (r.Ok ? "ok" : "failed"));
            Console.WriteLine("message=" + r.Message);
            Console.WriteLine("exit=" + r.ExitCode.ToString(CultureInfo.InvariantCulture));
            Logger.Write(target.Name, (r.Ok ? "完成" : "失败") + " · 命令行 · " + r.Message);
            return r.Ok ? 0 : 1;
        }

        /// <summary>Resolves where a button points, without running it. Prints
        /// kind / target / exists and, when something is missing, a sentence explaining what.
        /// <summary>`draft &lt;路径&gt;` —— 把一个文件 / 文件夹按"拖进窗口"的规则变成按钮草稿，只打印结果、
        /// 不写任何文件。界面上的拖拽手势没法在测试里合成（不是 WM_DROPFILES，是 OLE 拖放），
        /// 所以回归测试盯的是这个决策本身：.lnk 必须解析成它指向的真程序（用户实测报过
        /// "拖入快捷方式图标程序会失败"）；顺手也是拖拽出问题时的排查工具。</summary>
        private static int Draft(string[] args)
        {
            if (args.Length < 2 || args[1].Trim().Length == 0)
            {
                Console.Error.WriteLine("用法: draft <文件或文件夹路径>");
                return 2;
            }
            string path = args[1];
            ToolItem t = DroppedFile.Draft(path);
            if (t == null)
            {
                Console.Error.WriteLine("这个路径做不成按钮: " + path);
                return 1;
            }
            Console.WriteLine("input=" + path);
            Console.WriteLine("name=" + t.Name);
            Console.WriteLine("kind=" + t.Kind);
            Console.WriteLine("path=" + t.Path);
            Console.WriteLine("args=" + t.Args);
            Console.WriteLine("workdir=" + t.WorkDir);
            Console.WriteLine("target=" + t.Target);
            Console.WriteLine("shell=" + t.Shell);
            Console.WriteLine("isShortcut=" + (DroppedFile.IsShortcut(path) ? "yes" : "no"));
            return 0;
        }

        /// Always exits 0: it answers a question, it does not fail at anything.</summary>
        private static int DryRun(ToolItem t, Settings settings)
        {
            Console.WriteLine("dry=yes");
            if (t.Placeholder)
            {
                // A grey button is disabled in the interface, so "where does it point" has exactly
                // one answer: nowhere. Say that instead of inventing a target.
                Console.WriteLine("kind=none");
                Console.WriteLine("target=");
                Console.WriteLine("exists=no");
                Console.WriteLine("placeholder=" + (t.Hint.Length > 0 ? t.Hint : "P1"));
                return 0;
            }
            string kind = "unknown";
            string target = "";
            bool exists = true;
            string hint = "";

            if (t.Kind == "builtin" && t.Module == Launcher.ModulePrivacy)
            {
                // 隐私开关指向的是注册表里的值，不是文件：kind=registry，target=要改的值（人话）。
                kind = "registry";
                target = Launcher.DescribeCommand(t, settings, false);
                PrivacyItem pi = Privacy.Find(t.Options);
                if (pi == null && (t.Action == "off" || t.Action == "on"))
                {
                    kind = "unknown";
                    target = "";
                    exists = false;
                    hint = "隐私设置里没有这个开关: " + t.Options;
                }
                else { hint = pi != null ? pi.What : ""; }
            }
            else if (t.Kind == "builtin" && t.Module == Launcher.ModuleSystem)
            {
                SystemTarget st = Launcher.FindSystemTarget(t.Action);
                if (st == null)
                {
                    Console.WriteLine("kind=unknown");
                    Console.WriteLine("target=");
                    Console.WriteLine("exists=no");
                    Console.WriteLine("hint=系统工具里没有这个动作: " + t.Action);
                    return 0;
                }
                if (st.UiAction) { kind = "window"; target = "（本程序内的窗口）"; }
                else if (st.Path.Length > 0) { kind = "file"; target = AppPaths.Expand(st.Path); }
                else if (st.Shell.Length > 0) { kind = "shell"; target = st.Shell; }
                else { kind = "url"; target = st.Url; }
                exists = st.Exists;
                hint = st.Missing;
            }
            else if (t.Kind == "builtin" && t.Module == Launcher.ModulePermdel)
            {
                kind = "exe";
                string exe = Launcher.FindPermanentDeleteExe(settings);
                target = exe.Length > 0 ? exe : "(未找到 PermanentDeleteSetup.exe)";
                exists = exe.Length > 0;
                hint = "把 PermanentDeleteSetup.exe 放进工具目录 " + AppPaths.PayloadDir + "，或在「设置」里指定路径";
            }
            else if (t.Kind == "builtin" && t.Module == Launcher.ModuleApp)
            {
                kind = "window";
                target = "（本程序内的窗口）";
            }
            else if (t.Kind == "exe" || t.Kind == "script")
            {
                kind = t.Kind;
                if (t.Kind == "script" && t.Inline.Length > 0) { target = "（内联脚本）"; }
                else
                {
                    // Resolve, not Expand: a relative path means "in the toolbox folder / bin-tools",
                    // which is what makes "drop the exe in bin-tools and name it in the manifest" work.
                    target = AppPaths.Resolve(t.Path);
                    exists = File.Exists(target);
                    hint = "这个文件不在这台电脑上；相对路径按工具箱目录和 " + AppPaths.PayloadDirName + " 解析";
                }
            }
            else if (t.Kind == "open")
            {
                kind = "open";
                target = AppPaths.Resolve(t.Target);
                // A URL or a ms-settings:/shell: target cannot be "missing", only a local path can.
                if (target.IndexOf(':') < 0) { exists = File.Exists(target) || Directory.Exists(target); }
                hint = "找不到这个路径";
            }

            Console.WriteLine("kind=" + kind);
            Console.WriteLine("target=" + target);
            Console.WriteLine("exists=" + (exists ? "yes" : "no"));
            if (!exists && hint.Length > 0) { Console.WriteLine("hint=" + hint); }
            return 0;
        }

        private static int Status()
        {
            List<ToolItem> tools = Load();
            Settings settings = Settings.Load();
            int placeholders = 0;
            int dangers = 0;
            foreach (ToolItem t in tools)
            {
                if (t.Placeholder) { placeholders++; }
                if (t.Danger) { dangers++; }
            }
            string permdel = Launcher.FindPermanentDeleteExe(settings);
            int systemTotal = 0;
            int systemMissing = 0;
            foreach (ToolItem t in tools)
            {
                if (t.Kind != "builtin" || t.Module != Launcher.ModuleSystem) { continue; }
                SystemTarget st = Launcher.FindSystemTarget(t.Action);
                if (st == null) { continue; }
                systemTotal++;
                if (!st.Exists) { systemMissing++; }
            }

            Console.WriteLine("name=" + AboutForm.ProductTitle);
            Console.WriteLine("version=" + AboutForm.VersionText);
            Console.WriteLine("buttons=" + tools.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("placeholders=" + placeholders.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dangerous=" + dangers.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("systemTargets=" + systemTotal.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("systemMissing=" + systemMissing.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("theme=" + settings.Theme);
            Console.WriteLine("themeResolved=" + Settings.ResolveTheme(settings.Theme));
            Console.WriteLine("clickMode=" + settings.ClickMode);
            Console.WriteLine("admin=" + (Launcher.IsAdmin() ? "yes" : "no"));
            Console.WriteLine("log=" + Logger.CurrentFile());
            Console.WriteLine("settings=" + AppPaths.SettingsIni);
            Console.WriteLine("userTools=" + AppPaths.UserToolsJson);
            Console.WriteLine("permdelExe=" + (permdel.Length > 0 ? permdel : "(未找到)"));
            Console.WriteLine("permdelLog=" + AppPaths.PermdelEngineLog);
            Console.WriteLine("toolDir=" + AppPaths.PayloadDir);
            Console.WriteLine("toolDirName=" + AppPaths.PayloadDirName);
            Console.WriteLine("userToolDir=" + AppPaths.UserPayloadDir);
            foreach (string id in Tabs.Ids)
            {
                int n = 0;
                foreach (ToolItem t in tools) { if (t.Tab == id) { n++; } }
                Console.WriteLine("tab." + id + "=" + n.ToString(CultureInfo.InvariantCulture));
            }
            return 0;
        }

        private static int CheckUpdate()
        {
            // Bottom line of the sibling project, kept here as well: read only, never download.
            Console.WriteLine("update=disabled");
            Console.WriteLine("reason=工具箱自身的更新检查将在 P2 接入");
            Console.WriteLine("version=" + AboutForm.VersionText);
            return 0;
        }
    }
}
