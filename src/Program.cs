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
                // 资源管理器右键「解除文件占用」调起来的就是这一条：只开那个小窗口，不打开主界面、
                // 不挂控制台 —— 它自己就是一个独立的窗口进程，看完关掉就走。
                if (IsUnlockGui(args))
                {
                    Application.Run(new UnlockForm(PathsFrom(args)));
                    return 0;
                }
                // `ui log` / `ui settings`：右键「常用功能」子菜单里那几个固定入口。打开主界面，
                // 然后替用户点一下那个界面动作（否则从资源管理器点出来会"什么都不发生"）。
                if (string.Equals(args[0], "ui", StringComparison.OrdinalIgnoreCase))
                {
                    MainForm.StartupAction = (args.Length > 1) ? args[1].Trim().ToLowerInvariant() : "";
                    Logger.PruneOld(Settings.Load().LogKeepDays);
                    AppPaths.EnsurePayloadDir();
                    Application.Run(new MainForm());
                    return 0;
                }
                Native.AttachParentConsole();
                return Cli.Run(args);
            }

            Logger.PruneOld(Settings.Load().LogKeepDays);
            // 工具目录 bin-tools 在第一次打开界面时就建好，并放一份「说明.txt」进去：空文件夹看着
            // 像坏了（用户 2026-10-04 就问过「bin-tools 里面为什么是空的？」）。位置不可写时
            // EnsurePayloadDir 自己会退到用户目录，所以这里不用管返回值。
            AppPaths.EnsurePayloadDir();
            Application.Run(new MainForm());
            return 0;
        }

        /// <summary>`rightmenu unlock "&lt;路径&gt;"` = 要开那个小窗口（不是查一下就退出）。</summary>
        private static bool IsUnlockGui(string[] args)
        {
            if (args.Length < 2) { return false; }
            if (!string.Equals(args[0], "rightmenu", StringComparison.OrdinalIgnoreCase)) { return false; }
            if (!string.Equals(args[1], "unlock", StringComparison.OrdinalIgnoreCase)) { return false; }
            foreach (string a in args)
            {
                if (string.Equals(a, "--query-only", StringComparison.OrdinalIgnoreCase)) { return false; }
            }
            return true;
        }

        /// <summary>解锁窗口要处理的路径（跳过开关；多选时资源管理器会给多个）。</summary>
        private static string[] PathsFrom(string[] args)
        {
            List<string> list = new List<string>();
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i].StartsWith("--")) { continue; }
                if (args[i].Trim().Length > 0) { list.Add(args[i]); }
            }
            return list.ToArray();
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
                    case "sysreg": return SysRegCommand(args);
                    case "rightmenu": return RightMenuCommand(args);
                    case "pin": return PinCommand(args, true);
                    case "unpin": return PinCommand(args, false);
                    case "export": return ExportCommand(args);
                    case "import": return ImportCommand(args);
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

        /// <summary>`pin &lt;id&gt;` / `unpin &lt;id&gt;`：把按钮置顶（排在这一页最前面）。界面上
        /// 右键按钮也有这一项，两条路走的是同一个文件 pinned.txt。</summary>
        private static int PinCommand(string[] args, bool pinned)
        {
            if (args.Length < 2 || args[1].Trim().Length == 0)
            {
                Console.Error.WriteLine("用法: " + (pinned ? "pin" : "unpin") + " <按钮 id>");
                return 2;
            }
            string name = "";
            foreach (ToolItem t in Load())
            {
                if (string.Equals(t.Id, args[1], StringComparison.OrdinalIgnoreCase)) { name = t.Name; break; }
            }
            if (name.Length == 0)
            {
                Console.Error.WriteLine("没有这个按钮: " + args[1]);
                return 2;
            }
            string error = UserTools.SetPinned(args[1], pinned);
            if (error.Length > 0)
            {
                Console.Error.WriteLine("失败: " + error);
                return 1;
            }
            Console.WriteLine("pinned=" + (pinned ? "yes" : "no"));
            Console.WriteLine("id=" + args[1]);
            Console.WriteLine("name=" + name);
            RightMenu.SyncIfInstalled();   // 置顶段就是右键「常用功能」里最上面那一段（没装则空转）
            Logger.Write(name, pinned ? "已置顶（命令行）" : "已取消置顶（命令行）");
            return 0;
        }

        /// <summary>`export &lt;文件&gt;`：把「我的工具」导出成一个文件（内置按钮不导出）。</summary>
        private static int ExportCommand(string[] args)
        {
            if (args.Length < 2 || args[1].Trim().Length == 0)
            {
                Console.Error.WriteLine("用法: export <文件路径>");
                return 2;
            }
            List<ToolItem> mine = UserTools.Collect(Load());
            string error = UserTools.Export(args[1], mine);
            if (error.Length > 0)
            {
                Console.Error.WriteLine("导出失败: " + error);
                return 1;
            }
            Console.WriteLine("exported=" + mine.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("file=" + args[1]);
            return 0;
        }

        /// <summary>`import &lt;文件&gt;`：把导出文件里的按钮并进用户层（同 id 覆盖）。</summary>
        private static int ImportCommand(string[] args)
        {
            if (args.Length < 2 || args[1].Trim().Length == 0)
            {
                Console.Error.WriteLine("用法: import <文件路径>");
                return 2;
            }
            List<ToolItem> mine = UserTools.Collect(Load());
            int added;
            int replaced;
            string error = UserTools.Import(args[1], mine, out added, out replaced);
            if (error.Length > 0)
            {
                Console.Error.WriteLine("导入失败: " + error);
                return 1;
            }
            string saveError = UserTools.Save(mine);
            if (saveError.Length > 0)
            {
                Console.Error.WriteLine("保存失败: " + saveError);
                return 1;
            }
            Console.WriteLine("added=" + added.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("replaced=" + replaced.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("total=" + mine.Count.ToString(CultureInfo.InvariantCulture));
            return 0;
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
            Console.WriteLine("  Mxx1Toolbox.exe run <id> --confirm   执行前先弹确认框（右键菜单里那批危险按钮用的）");
            Console.WriteLine("  Mxx1Toolbox.exe draft <路径>         把文件/文件夹按「拖进窗口」的规则变成按钮草稿");
            Console.WriteLine("  Mxx1Toolbox.exe status               打印 key=value 状态（脚本用）");
            Console.WriteLine("  Mxx1Toolbox.exe tip [id]             打印按钮的悬停说明（界面上鼠标停住时看到的那段）");
            Console.WriteLine("  Mxx1Toolbox.exe privacy status       只读列出隐私开关的当前状态（不改任何东西）");
            Console.WriteLine("  Mxx1Toolbox.exe privacy selftest     用工具箱自己的测试键自检「原值 → 写入 → 还原」链路");
            Console.WriteLine("  Mxx1Toolbox.exe sysreg status        只读列出系统设置开关（任务栏/开始菜单/内核隔离…）的状态与原值");
            Console.WriteLine("  Mxx1Toolbox.exe sysreg selftest      自检系统设置那条链路（DWORD / 字符串 / 整棵键三种值）");
            Console.WriteLine("  Mxx1Toolbox.exe rightmenu status     只读列出右键菜单里装了什么（装 / 卸只在界面里点）");
            Console.WriteLine("  Mxx1Toolbox.exe rightmenu help       右键增强的说明（怎么卸干净 / 菜单没出现怎么办）");
            Console.WriteLine("  Mxx1Toolbox.exe rightmenu unlock --query-only <路径>   只查谁占着这个文件，不弹窗不结束进程");
            Console.WriteLine("  Mxx1Toolbox.exe ui [log|settings]    打开界面并直接看日志 / 设置（右键子菜单的固定入口用它）");
            Console.WriteLine("  Mxx1Toolbox.exe pin <id> / unpin <id>  把按钮置顶 / 取消置顶（排在这一页最前面）");
            Console.WriteLine("  Mxx1Toolbox.exe export <文件>        把「我的工具」导出成一个文件");
            Console.WriteLine("  Mxx1Toolbox.exe import <文件>        把导出文件里的按钮并进来（同 id 覆盖）");
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
                foreach (RegItemSpec i in Privacy.All)
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

        /// <summary>`sysreg status | items | selftest`：「常用设置」里那批写注册表的按钮用的后端。
        /// 和隐私开关一样，命令行**故意不提供**"真的去改系统设置"的入口 —— 那只能从界面点
        /// （成对按钮 + 可一键还原），自检只碰 HKCU\Software\mxx1-toolbox\sysreg-selftest。</summary>
        private static int SysRegCommand(string[] args)
        {
            string what = (args.Length > 1) ? args[1].ToLowerInvariant() : "status";
            if (what == "status")
            {
                Console.Write(SysReg.Status());
                return 0;
            }
            if (what == "items")
            {
                foreach (Tweak t in SysReg.All)
                {
                    Console.WriteLine(t.Id + "\t" + t.Item.Name + "\t" + t.Item.Values.Length + " 个值\t"
                        + t.OnLabel + " / " + t.OffLabel + "\t"
                        + (t.Item.Admin ? "要管理员" : "不用管理员"));
                }
                return 0;
            }
            if (what == "selftest")
            {
                bool ok;
                string report = SysReg.SelfTest(out ok);
                Console.Write(report);
                Console.WriteLine("selftest=" + (ok ? "pass" : "fail"));
                return ok ? 0 : 1;
            }
            Console.Error.WriteLine("用法: sysreg status | items | selftest");
            return 2;
        }

        /// <summary>`rightmenu status | items | help` 只读；`rightmenu unlock --query-only &lt;路径&gt;` 只打印
        /// 谁占着它（不弹窗、也**不结束任何进程**）。
        ///
        /// 和 `sysreg` 同一条规矩：**写注册表的入口故意只在界面**（「装上 / 撤掉…」那两个按钮，
        /// 会过确认框），命令行不提供写入口 —— Test-Cli 的 L07 那条底线。
        /// 回归测试要验装 / 卸的键结构时，用环境变量 MXX1_RIGHTMENU_ROOT 把根挪到测试键下。</summary>
        private static int RightMenuCommand(string[] args)
        {
            string what = (args.Length > 1) ? args[1].ToLowerInvariant() : "status";
            if (what == "status")
            {
                Console.Write(RightMenu.Status());
                return 0;
            }
            if (what == "items")
            {
                foreach (RightMenuLocation loc in RightMenu.Locations)
                {
                    Console.WriteLine(loc.Id + "\t" + loc.Label + "\t" + loc.Key);
                }
                Console.WriteLine("titles=" + RightMenu.UnlockTitle + " / " + RightMenu.CommonTitle);
                Console.WriteLine("shared=" + RightMenu.SharedKey);
                Console.WriteLine("root=" + RightMenu.RootLabel);
                return 0;
            }
            if (what == "help")
            {
                Console.Write(RightMenu.Help());
                return 0;
            }
            if (what == "unlock")
            {
                return UnlockQuery(args);
            }
            Console.Error.WriteLine("用法: rightmenu status | items | help | unlock [--query-only] <文件或文件夹路径>");
            return 2;
        }

        /// <summary>只查不改：谁占着这个文件。测试用它（自己锁一个文件 → 断言能查到自己的 PID）。</summary>
        private static int UnlockQuery(string[] args)
        {
            string path = "";
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i].StartsWith("--")) { continue; }
                path = args[i];
                break;
            }
            if (path.Trim().Length == 0)
            {
                Console.Error.WriteLine("用法: rightmenu unlock [--query-only] <文件或文件夹路径>");
                return 2;
            }
            LockReport report = FileLock.Scan(new string[] { path });
            List<FileLocker> found = report.AllLockers();
            bool exists = false;
            try { exists = File.Exists(path) || Directory.Exists(path); }
            catch { }
            Console.WriteLine("path=" + path);
            Console.WriteLine("exists=" + (exists ? "yes" : "no"));
            Console.WriteLine("scanned=" + report.Scanned.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("truncated=" + (report.Truncated ? "yes" : "no"));
            Console.WriteLine("hits=" + report.Hits.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("lockers=" + found.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("badfiles=" + report.BadFiles.ToString(CultureInfo.InvariantCulture));
            // 哪个文件被占着（文件夹扫描时这是最有用的那一行）
            foreach (LockHit h in report.Hits)
            {
                if (h.File.Length > 0) { Console.WriteLine("file=" + h.File + "\tlockers=" + h.Lockers.Count.ToString(CultureInfo.InvariantCulture)); }
            }
            foreach (FileLocker f in found) { Console.WriteLine(FileLock.DescribeLine(f)); }
            Console.WriteLine("verdict=" + report.Verdict);
            Console.WriteLine("verdictlocked=" + (report.VerdictLocked ? "yes" : "no"));
            Console.WriteLine("verdictdenied=" + (report.VerdictDenied ? "yes" : "no"));
            if (report.Note.Length > 0) { Console.WriteLine("note=" + report.Note); }
            if (report.Error.Length > 0) { Console.WriteLine("error=" + report.Error); }
            return 0;
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
            bool confirm = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--admin") { admin = true; }
                if (args[i] == "--dry") { dry = true; }
                if (args[i] == "--confirm") { confirm = true; }
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

            // --confirm：右键菜单「常用功能」里标了 danger 的按钮命令带的就是这个 —— 右键菜单是
            // 误点高发区，所以走命令行这条路也要先弹自家确认框（界面里点按钮本来就过确认框，
            // 两条路必须一致）。用户把「危险按钮要先确认」关掉了就按他的设置来。
            if (confirm && settings.ConfirmDangerous)
            {
                DialogResult answer;
                using (ConfirmForm f = new ConfirmForm(target, command, admin,
                    Launcher.IsAdmin() && admin, Theme.Resolve(settings.Theme)))
                {
                    answer = f.ShowDialog();
                }
                if (answer != DialogResult.OK)
                {
                    Console.WriteLine("cancelled=yes");
                    Logger.Write(target.Name, "已取消（右键菜单里的确认框选了取消）");
                    return 0;
                }
            }

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
                RegItemSpec pi = Privacy.Find(t.Options);
                if (pi == null && (t.Action == "off" || t.Action == "on"))
                {
                    kind = "unknown";
                    target = "";
                    exists = false;
                    hint = "隐私设置里没有这个开关: " + t.Options;
                }
                else { hint = pi != null ? pi.What : ""; }
            }
            else if (t.Kind == "builtin" && t.Module == Launcher.ModuleSysreg)
            {
                // 系统设置开关也是注册表值（CLSID 覆盖那几条是"整个键"）：kind=registry
                kind = "registry";
                target = Launcher.DescribeCommand(t, settings, false);
                Tweak tw = SysReg.FindTweak(t.Options);
                if (tw == null && (t.Action == "off" || t.Action == "on"))
                {
                    kind = "unknown";
                    target = "";
                    exists = false;
                    hint = "系统设置里没有这个开关: " + t.Options;
                }
                else { hint = tw != null ? tw.Item.What : ""; }
            }
            else if (t.Kind == "builtin" && t.Module == Launcher.ModuleRightMenu)
            {
                // 右键增强动的也是注册表（HKCU\Software\Classes 下自己那几个键）：kind=registry
                kind = "registry";
                target = Launcher.DescribeCommand(t, settings, false);
                hint = "装 / 卸只在界面里点（命令行没有写入口）";
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
            Console.WriteLine("admin=" + (Launcher.IsAdmin() ? "yes" : "no"));
            Console.WriteLine("pinned=" + string.Join(",", UserTools.LoadPinned().ToArray()));
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
