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

        /// <summary>`rightmenu unlock "&lt;路径&gt;"` = 要开那个小窗口（不是查一下就退出、也不是
        /// 那条不弹窗口的一键解除）。</summary>
        private static bool IsUnlockGui(string[] args)
        {
            if (args.Length < 2) { return false; }
            if (!string.Equals(args[0], "rightmenu", StringComparison.OrdinalIgnoreCase)) { return false; }
            if (!string.Equals(args[1], "unlock", StringComparison.OrdinalIgnoreCase)) { return false; }
            foreach (string a in args)
            {
                if (string.Equals(a, "--query-only", StringComparison.OrdinalIgnoreCase)) { return false; }
                // 「一键解除占用」那条路：不弹窗口，走命令行（见 src\AutoUnlock.cs）
                if (string.Equals(a, "--auto", StringComparison.OrdinalIgnoreCase)) { return false; }
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
                    case "hash": return HashCommand(args);
                    case "copypath": return CopyPathCommand(args);
                    case "terminal": return TerminalCommand(args);
                    case "privacy": return PrivacyCommand(args);
                    case "sysreg": return SysRegCommand(args);
                    case "rightmenu": return RightMenuCommand(args);
                    case "pin": return PinCommand(args, true);
                    case "unpin": return PinCommand(args, false);
                    case "export": return ExportCommand(args);
                    case "import": return ImportCommand(args);
                    case "checkupdate": return CheckUpdate();
                    case "check-update": return CheckUpdate();
                    case "disclaimer": return Disclaimer();
                    case "consent": return ConsentCommand(args);
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
            Console.WriteLine("  Mxx1Toolbox.exe tip [id] --full      打印按钮的「功能说明」全文（右键 →「功能说明…」那一页）");
            Console.WriteLine("  Mxx1Toolbox.exe hash <文件>           算出这个文件的 MD5 / SHA256（只读，不上传）");
            Console.WriteLine("  Mxx1Toolbox.exe hash <文件> --expect=<校验值>   顺便对一次：一致退出码 0，不一致 1");
            Console.WriteLine("  Mxx1Toolbox.exe privacy status       只读列出隐私开关的当前状态（不改任何东西）");
            Console.WriteLine("  Mxx1Toolbox.exe privacy selftest     用工具箱自己的测试键自检「原值 → 写入 → 还原」链路");
            Console.WriteLine("  Mxx1Toolbox.exe sysreg status        只读列出系统设置开关（任务栏/开始菜单/内核隔离…）的状态与原值");
            Console.WriteLine("  Mxx1Toolbox.exe sysreg selftest      自检系统设置那条链路（DWORD / 字符串 / 整棵键三种值）");
            Console.WriteLine("  Mxx1Toolbox.exe rightmenu status     只读列出右键菜单里装了什么（装 / 卸只在界面里点）");
            Console.WriteLine("  Mxx1Toolbox.exe rightmenu help       右键增强的说明（怎么卸干净 / 菜单没出现怎么办）");
            Console.WriteLine("  Mxx1Toolbox.exe rightmenu unlock --query-only <路径>   只查谁占着这个文件，不弹窗不结束进程");
            Console.WriteLine("  Mxx1Toolbox.exe rightmenu unlock --auto <路径>         一键解除占用：不弹窗口，直接结束占用它的程序");
            Console.WriteLine("                                                        （右键菜单里的「" + RightMenu.AutoTitle + "」用的就是它）");
            Console.WriteLine("                                                        提示卡 5 秒自动关闭（点一下也能提前关），--notify=<毫秒> 定它多久自己走");
            Console.WriteLine("                                                        （--notify=0 / --quiet 连提示卡也不要）");
            Console.WriteLine("  Mxx1Toolbox.exe copypath <文件…> [--name] [--relative] [--base=<目录>]   把名字 / 路径复制进剪贴板（一行一个）");
            Console.WriteLine("                                                        默认**不带引号**；--name = 只取名字（报告.txt）；--relative = 相对路径（基准默认是当前目录）");
            Console.WriteLine("                                                        （右键菜单里的「" + RightMenu.CopyNameTitle + "」用 --name，「" + RightMenu.CopyPathTitle + "」不用开关）");
            Console.WriteLine("  Mxx1Toolbox.exe terminal [<目录>] [--wt|--ps|--cmd]   在某个目录里开一个终端");
            Console.WriteLine("                                                        （默认挑 Windows Terminal → PowerShell → cmd；三个开关各钉死一个）");
            Console.WriteLine("                                                        （右键菜单里的「" + RightMenu.TerminalTitle + "」子菜单那两行用的就是它）");
            Console.WriteLine("                                                        （--dry 只打印会跑哪条命令，不起窗口）");
            Console.WriteLine("  Mxx1Toolbox.exe ui [log|settings]    打开界面并直接看日志 / 设置（右键子菜单的固定入口用它）");
            Console.WriteLine("  Mxx1Toolbox.exe pin <id> / unpin <id>  把按钮置顶 / 取消置顶（排在这一页最前面）");
            Console.WriteLine("  Mxx1Toolbox.exe export <文件>        把「我的工具」导出成一个文件");
            Console.WriteLine("  Mxx1Toolbox.exe import <文件>        把导出文件里的按钮并进来（同 id 覆盖）");
            Console.WriteLine("  Mxx1Toolbox.exe checkupdate          只读版本号：查 GitHub 上有没有新版本，不下载不替换");
            Console.WriteLine("  Mxx1Toolbox.exe disclaimer           打印《免责声明与服务条款》全文（窗口显示的就是它）");
            Console.WriteLine("  Mxx1Toolbox.exe consent [--accept|--reset]  查看 / 记录 / 清除使用条款的同意状态");
            Console.WriteLine("  Mxx1Toolbox.exe help                 这份帮助");
            Console.WriteLine();
            Console.WriteLine("页签 id: " + string.Join(" / ", Tabs.Ids));
            Console.WriteLine();
            Console.WriteLine("* 界面首次运行会要求勾选同意《免责声明与服务条款》（同意记录写在 settings.ini，记的是正文指纹，");
            Console.WriteLine("  条款一改就会重新要求确认）；命令行是非交互场景，不拦，脚本可以先用 consent --accept 记录同意；");
            Console.WriteLine("* 想彻底关掉联网检查：设置环境变量 MXX1_NO_UPDATE=1（关掉后一个字节都不发）；");
            Console.WriteLine("* 加按钮：写 tools\\*.json 要重新编译；把工具文件夹放进 bin-tools\\ 则不用 —— 会按");
            Console.WriteLine("  bin-tools\\<工具>\\tool.json 自动长出按钮（没有 tool.json、只有一个 exe 也会建一个）。");
        }

        /// <summary>Prints the hover text of every button (or of one id). It is the very same string
        /// MainForm.TipFor hands to the ToolTip control, so the test suite can prove a tooltip is a
        /// readable sentence instead of a screen-wide command line -- without moving the mouse.
        /// 加 `--full` 换成「功能说明」窗口里的那一整段正文（同一份 MainForm.HelpText）。</summary>
        private static int Tip(string[] args)
        {
            bool full = HasFlag(args, "--full");
            string want = "";
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("-")) { continue; }
                want = args[i];
                break;
            }
            List<ToolItem> tools = Load();
            Settings settings = Settings.Load();
            int shown = 0;
            foreach (ToolItem t in tools)
            {
                if (want.Length > 0 && !string.Equals(t.Id, want, StringComparison.OrdinalIgnoreCase)) { continue; }
                Console.WriteLine("--- " + t.Id);
                Console.WriteLine(full ? MainForm.HelpText(t, settings) : TipText(t, settings));
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

        /// <summary>`hash <文件> [--expect=<校验值>]` —— 算一个文件的 MD5 / SHA256。
        /// 界面里那个按钮用的是**同一份** HashTool 实现（见 src\HashTool.cs），所以命令行算出来的
        /// 值和窗口里显示的必然一致；测试也靠它拿 Get-FileHash 交叉验证（两套独立实现比一遍）。
        ///
        /// 退出码：0 = 算完（给了 --expect 且一致）；1 = 算完了但和给的校验值**不一致**；
        /// 2 = 用法错 / 文件读不了 / 给的校验值连算法都认不出。
        /// 只读：不写文件、不上传任何东西。</summary>
        private static int HashCommand(string[] args)
        {
            string path = "";
            string expect = "";
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--expect=", StringComparison.OrdinalIgnoreCase))
                {
                    expect = a.Substring("--expect=".Length);
                    continue;
                }
                if (a.StartsWith("-")) { continue; }
                if (path.Length == 0) { path = a; }
            }
            if (path.Trim().Length == 0)
            {
                Console.Error.WriteLine("用法: Mxx1Toolbox.exe hash <文件> [--expect=<校验值>]");
                return 2;
            }
            HashTool.HashResult r = HashTool.Compute(path, null);
            if (!r.Ok)
            {
                Console.Error.WriteLine("错误: " + r.Error);
                return 2;
            }
            Console.WriteLine("file=" + path);
            Console.WriteLine("size=" + r.Size.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("md5=" + r.Md5);
            Console.WriteLine("sha256=" + r.Sha256);
            if (expect.Trim().Length == 0) { return 0; }

            bool matched;
            string algo;
            string text = HashTool.Verdict(expect, r, out matched, out algo);
            Console.WriteLine("expected=" + HashTool.Normalize(expect));
            Console.WriteLine("algo=" + (algo.Length == 0 ? "unknown" : algo));
            Console.WriteLine("match=" + (algo.Length == 0 ? "unknown" : (matched ? "true" : "false")));
            Console.WriteLine("verdict=" + text);
            if (algo.Length == 0) { return 2; }
            return matched ? 0 : 1;
        }

        /// <summary>`copypath &lt;文件…&gt; [--name] [--relative] [--base=&lt;目录&gt;] [--quote] [--print] [--no-wait]`
        /// —— 把名字 / 路径复制进剪贴板（一行一个）。右键菜单里那**两个**菜单项用的就是它：
        /// 「复制文件名」= `copypath --name "%1"`、「复制文件路径」= `copypath "%1"`
        /// —— 两条都**不带引号**（用户 2026-10-06 晚六：「复制出来的路径两边都不可以带有引号」）。
        /// 实现与"多选怎么合批"的来龙去脉见 `src\CopyPath.cs`。
        ///
        /// 退出码：0 = 复制好了（含"同一批里别人收尾"），2 = 用法错 / 剪贴板写不进去。
        /// 只读：不碰任何文件；`--print` 连剪贴板都不碰（测试与排查用）。</summary>
        private static int CopyPathCommand(string[] args)
        {
            List<string> paths = new List<string>();
            bool relative = false;
            bool nameOnly = false;
            string baseDir = "";
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == null) { continue; }
                if (string.Equals(args[i], "--name", StringComparison.OrdinalIgnoreCase)) { nameOnly = true; continue; }
                if (string.Equals(args[i], "--relative", StringComparison.OrdinalIgnoreCase)) { relative = true; continue; }
                if (args[i].StartsWith("--base=", StringComparison.OrdinalIgnoreCase))
                {
                    baseDir = args[i].Substring(7);
                    continue;
                }
                if (args[i].StartsWith("--")) { continue; }
                paths.Add(args[i]);
            }
            return CopyPath.Run(paths, HasFlag(args, "--quote"), relative, baseDir, nameOnly,
                HasFlag(args, "--no-wait"), HasFlag(args, "--print"), NotifyWanted(args), NotifyMs(args));
        }

        /// <summary>`terminal [&lt;目录&gt;] [--wt|--ps|--cmd] [--dry]` —— 在某个目录里开一个终端
        /// （wt → powershell → cmd 的顺序，见 `src\Terminal.cs`）。右键菜单里的「在此处打开终端」
        /// 用的就是它（文件夹 / 文件夹空白处 / 桌面空白处三个位置）。
        ///
        /// 退出码：0 = 起来了（`--dry` = 打印完了），2 = 目录不存在 / 这个终端没有 / 起不来。
        /// **不提权**、不改任何设置；`--dry` 不起窗口（回归测试靠它验引号）。</summary>
        private static int TerminalCommand(string[] args)
        {
            string dir = "";
            string want = Terminal.Auto;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null) { continue; }
                if (string.Equals(a, "--wt", StringComparison.OrdinalIgnoreCase)) { want = Terminal.Wt; continue; }
                if (string.Equals(a, "--ps", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a, "--powershell", StringComparison.OrdinalIgnoreCase)) { want = Terminal.PowerShell; continue; }
                if (string.Equals(a, "--cmd", StringComparison.OrdinalIgnoreCase)) { want = Terminal.Cmd; continue; }
                if (a.StartsWith("--")) { continue; }
                if (dir.Length == 0) { dir = a; }
            }
            return Terminal.Run(dir, want, HasFlag(args, "--dry"), NotifyWanted(args), NotifyMs(args));
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
        /// 谁占着它（不弹窗、也**不结束任何进程**）；`rightmenu unlock --auto &lt;路径&gt;` 是唯一一个
        /// 会动手的命令行入口（右键菜单里那一项用的，不弹窗、直接结束占用它的程序，见 src\AutoUnlock.cs）。
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
                Console.WriteLine("titles=" + RightMenu.UnlockTitle + " / " + RightMenu.AutoTitle + " / "
                    + RightMenu.CommonTitle + " / " + RightMenu.CopyNameTitle + " / " + RightMenu.CopyPathTitle
                    + " / " + RightMenu.TerminalTitle);
                Console.WriteLine("children=" + RightMenu.TerminalCmdName + " / " + RightMenu.TerminalPsName);
                Console.WriteLine("shared=" + RightMenu.SharedKey);
                Console.WriteLine("trees=" + RightMenu.SharedKey + " / " + RightMenu.TerminalTreeKey + " / " + RightMenu.TerminalBgTreeKey);
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
            if (what == "handles")
            {
                return HandlesQuery(args);
            }
            Console.Error.WriteLine("用法: rightmenu status | items | help | unlock [--query-only|--auto] <路径> | handles <路径>");
            return 2;
        }

        /// <summary>`rightmenu unlock ...` 后面那串路径（跳过 -- 开头的开关；多选时资源管理器
        /// 会给多个路径，MultiSelectModel=Player 会一次全传进来）。</summary>
        private static List<string> PathArgs(string[] args)
        {
            List<string> list = new List<string>();
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == null || args[i].StartsWith("--")) { continue; }
                if (args[i].Trim().Length > 0) { list.Add(args[i].Trim()); }
            }
            return list;
        }

        /// <summary>有没有这个开关（`--auto` 和 `--notify=0` 两种写法都认）。</summary>
        private static bool HasFlag(string[] args, string name)
        {
            foreach (string a in args)
            {
                if (a == null) { continue; }
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) { return true; }
                if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        /// <summary>一键解除那条路要不要弹提示卡。`--quiet` / `--notify=0` / 环境变量
        /// MXX1_NO_NOTIFY=1 都能关掉（回归测试用它：测试不该在别人桌面上弹东西）。</summary>
        private static bool NotifyWanted(string[] args)
        {
            if (HasFlag(args, "--quiet") || HasFlag(args, "--notify=0")) { return false; }
            string env = AppPaths.Expand(Environment.GetEnvironmentVariable("MXX1_NO_NOTIFY"));
            return !(env != null && env.Trim() == "1");
        }

        /// <summary>提示卡显示多久（毫秒）。**不写 = 5 秒自动关闭**（用户 2026-10-06 晚五原话：
        /// 「一键解除占用改成 5 秒自动关闭，保留点击关闭」—— 上一轮他要的是"一直留着"，用了一晚
        /// 又改回来了；"点一下也能提前关"那条留着）。
        /// `--notify=<毫秒>` 写多少就多少（界面回归要用短的，不然一条检查就要等它自己消失），
        /// 0 = 不显示；给的数夹在 800ms - 60s：再短看不见，再长就成了"赖着不走"。</summary>
        private static int NotifyMs(string[] args)
        {
            foreach (string a in args)
            {
                if (a == null) { continue; }
                if (!a.StartsWith("--notify=", StringComparison.OrdinalIgnoreCase)) { continue; }
                int ms;
                if (!int.TryParse(a.Substring(9).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms))
                {
                    continue;
                }
                if (ms <= 0) { return 0; }
                if (ms < 800) { return 800; }
                if (ms > 60000) { return 60000; }
                return ms;
            }
            return DefaultNotifyMs;     // 没写 --notify：5 秒后自己走（点一下也能提前关）
        }

        /// <summary>不给 `--notify` 时提示卡显示多久 —— **5 秒**，用户 2026-10-06 晚五拍的数字。
        /// 只在这里写一次：卡片本身（src\Balloon.cs）拿的是"显示多久"这个数，不自己定默认值。</summary>
        public const int DefaultNotifyMs = 5000;

        /// <summary>只读：谁手里有这个文件 / 文件夹的**句柄**（全系统句柄表，像火绒那样）。
        /// 命令行只提供"查"，**不提供"关"** —— 抽句柄是危险动作，只能从界面点（还要过确认框）。
        /// 测试用它：共享打开（FileShare.ReadWrite）的文件 Restart Manager 看不见，句柄表看得见。</summary>
        private static int HandlesQuery(string[] args)
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
                Console.Error.WriteLine("用法: rightmenu handles <文件或文件夹路径>");
                return 2;
            }
            string note;
            List<HandleHit> hits = HandleUnlock.Find(new string[] { path }, out note);
            Console.WriteLine("path=" + path);
            Console.WriteLine("hits=" + hits.Count.ToString(CultureInfo.InvariantCulture));
            foreach (HandleHit h in hits) { Console.WriteLine("handle\t" + HandleUnlock.DescribeLine(h)); }
            Console.WriteLine("note=" + note);
            return 0;
        }

        /// <summary>只查不改：谁占着这个文件。测试用它（自己锁一个文件 → 断言能查到自己的 PID）。
        /// 加 `--auto` 就换成另一条路：**不弹窗口**，直接结束占用它的程序（见 src\AutoUnlock.cs）——
        /// 右键菜单里的「一键解除占用」用的就是它。</summary>
        private static int UnlockQuery(string[] args)
        {
            List<string> all = PathArgs(args);
            if (HasFlag(args, "--auto"))
            {
                int notifyMs = NotifyMs(args);
                // notifyMs == 0 时不弹卡片；给了数就显示那么久（不给 = 5 秒，见 Program.NotifyMs）
                return AutoUnlock.Run(all.ToArray(), NotifyWanted(args), notifyMs);
            }

            string path = (all.Count > 0) ? all[0] : "";
            if (path.Trim().Length == 0)
            {
                Console.Error.WriteLine("用法: rightmenu unlock [--query-only|--auto] <文件或文件夹路径>");
                return 2;
            }
            LockReport report = FileLock.Scan(new string[] { path });
            List<FileLocker> found = report.AllLockers();
            bool exists = false;
            try { exists = File.Exists(path) || Directory.Exists(path); }
            catch { }
            int lockHits = 0;
            foreach (LockHit h in report.Hits)
            {
                foreach (FileLocker f in h.Lockers) { if (f.IsLock) { lockHits++; break; } }
            }
            Console.WriteLine("path=" + path);
            Console.WriteLine("exists=" + (exists ? "yes" : "no"));
            Console.WriteLine("scanned=" + report.Scanned.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("truncated=" + (report.Truncated ? "yes" : "no"));
            Console.WriteLine("hits=" + lockHits.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("lockers=" + found.Count.ToString(CultureInfo.InvariantCulture));
            // 后两类不是"占用"，是另外两种情况：它自己在运行 / 某个窗口里开着它（见 FileLock 里的说明）
            Console.WriteLine("run=" + report.RunCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("open=" + report.OpenCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("badfiles=" + report.BadFiles.ToString(CultureInfo.InvariantCulture));
            // 哪个文件被占着（文件夹扫描时这是最有用的那一行）
            foreach (LockHit h in report.Hits)
            {
                if (h.File.Length == 0) { continue; }
                int n = 0;
                foreach (FileLocker f in h.Lockers) { if (f.IsLock) { n++; } }
                if (n == 0) { continue; }
                Console.WriteLine("file=" + h.File + "\tlockers=" + n.ToString(CultureInfo.InvariantCulture));
            }
            foreach (FileLocker f in report.AllRows())
            {
                Console.WriteLine(f.Source + "\t" + FileLock.DescribeLine(f)
                    + (f.Extra.Length > 0 ? ("\textra=" + f.Extra) : ""));
            }
            // 结束进程会连带结束的子进程（只读预览）
            foreach (FileLocker f in FileLock.ChildrenOf(found))
            {
                Console.WriteLine("child\tpid=" + f.Pid.ToString(CultureInfo.InvariantCulture) + "\texe=" + f.Exe);
            }
            Console.WriteLine("verdict=" + report.Verdict);
            // 「能不能删 / 改名」：用户真正要问的那句（拿 DELETE 权限试一次，只试不改）
            if (report.DeleteChecked)
            {
                Console.WriteLine("candelete=" + (report.DeleteOk ? "yes" : "no"));
                Console.WriteLine("deleteerror=" + report.DeleteError.ToString(CultureInfo.InvariantCulture));
                if (report.DeleteNote.Length > 0) { Console.WriteLine("deletenote=" + report.DeleteNote); }
            }
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
            bool show = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--admin") { admin = true; }
                if (args[i] == "--dry") { dry = true; }
                if (args[i] == "--confirm") { confirm = true; }
                if (args[i] == "--show") { show = true; }
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

            LaunchResult r = Launcher.Run(target, settings, admin, show);
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

            // --show：右键菜单那条路（资源管理器右键「常用功能」里的按钮）专用。
            //
            // 为什么必须有：这个 exe 是 **winexe，没有控制台** —— 上面那些 Console.WriteLine 写进去
            // 根本没人看得见，工具跑完就静悄悄地退出了。于是「激活状态」「查看设置改动」这种
            // **结果就是一段文字**的按钮，从右键菜单点等于"没有效果"（用户 2026-10-04 报的）。
            // 所以这里把结果弹成一个小窗口（复用主界面那个 OutputForm：复制全文 / 用记事本打开 / 关闭）。
            // 只在这两种情况下弹，免得给"本来就会自己开窗口的程序"添乱：
            // ① 有文字结果（脚本 / 内置报告）；② 失败了（如实把原因给用户看）。
            if (show && !r.Deferred)
            {
                bool hasText = (r.Output != null && r.Output.Trim().Length > 0);
                if (hasText || !r.Ok)
                {
                    StringBuilder body = new StringBuilder();
                    if (hasText) { body.Append(r.Output.TrimEnd()).AppendLine().AppendLine(); }
                    body.Append(r.Ok ? "（完成）" : "（失败）").Append(r.Message);
                    if (!r.Ok && r.ExitCode != 0)
                    {
                        body.Append("　退出码 ").Append(r.ExitCode.ToString(CultureInfo.InvariantCulture));
                    }
                    string headline = (r.Ok ? "「" : "「") + target.Name + (r.Ok ? "」跑完了：" : "」没成功：");
                    Application.Run(new OutputForm(target.Name + " · 结果", headline, body.ToString(),
                        Theme.Resolve(settings.Theme)));
                }
            }
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
                // 缺组件时要给一句解释：程序文件找不到说 Missing 那句，Win7 上没有
                // ms-settings: 这个入口说"去控制面板哪儿找"那句（见 SystemTarget.UnavailableMessage）。
                hint = st.Path.Length > 0 ? st.Missing
                    : (st.IsSettingsUrl ? st.UnavailableMessage : "这个入口在这台系统上打不开");
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
            // 图标也报出来：外挂工具（bin-tools）的图标是按文件夹里的 PNG 猜出来的，用户看不到
            // 按钮上那张图到底取自哪，就只能靠猜（R 组的自动按钮测试也靠这一行）。
            try
            {
                string ip = t.IconPath;
                // 只报**真在磁盘上**的那一份：内置按钮的图标是编译时内嵌进 exe 的，磁盘上没有文件，
                // 把那个路径打出来只会让人以为"图标文件丢了"。
                if (!string.IsNullOrEmpty(ip) && File.Exists(ip)) { Console.WriteLine("icon=" + ip); }
            }
            catch { }
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
            int autoButtons = 0;
            foreach (ToolItem t in tools) { if (t.AutoLayer) { autoButtons++; } }
            Console.WriteLine("autoButtons=" + autoButtons.ToString(CultureInfo.InvariantCulture));
            foreach (ToolItem t in tools)
            {
                if (!t.AutoLayer) { continue; }
                Console.WriteLine("autoButton=" + t.Tab + "\t" + t.Id + "\t" + t.Source);
            }
            Console.WriteLine("placeholders=" + placeholders.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dangerous=" + dangers.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("systemTargets=" + systemTotal.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("systemMissing=" + systemMissing.ToString(CultureInfo.InvariantCulture));
            // 兼容性现场：这台系统是哪一版、有没有 Windows 10/11 那个「设置」应用
            // （系统工具页里 7 个 ms-settings: 按钮在 Win7 上打不开，会改成一句"去控制面板哪儿找"）。
            Console.WriteLine("windows=" + Launcher.WindowsName());
            Console.WriteLine("settingsApp=" + (SystemTarget.HasSettingsApp ? "yes" : "no"));
            Console.WriteLine("theme=" + settings.Theme);
            Console.WriteLine("themeResolved=" + Settings.ResolveTheme(settings.Theme));
            // 使用条款的同意状态（首次运行的确认门 + consent 命令都读这一份）
            Console.WriteLine("consent=" + Consent.StateId());
            Console.WriteLine("consentAgreed=" + (Consent.IsAccepted() ? "yes" : "no"));
            Console.WriteLine("updateCheck=" + (UpdateCheck.Disabled ? "disabled" : "enabled"));
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

        /// <summary>只读版本号：查 GitHub 上有没有新版本。**不下载、不替换、失败静默**
        /// （三条底线见 docs/DISCLAIMER.md 第 4 节）；MXX1_NO_UPDATE=1 时一个字节都不发。</summary>
        private static int CheckUpdate()
        {
            UpdateResult r = UpdateCheck.Run(UpdateCheck.CurrentVersion);
            foreach (string line in r.Lines()) { Console.WriteLine(line); }
            Console.WriteLine("version=" + AboutForm.VersionText);
            Console.WriteLine("ui=" + r.UiText);
            // 退出码：0 = 查过了（有新版本也是 0），1 = 没查成 / 关掉了（脚本据此判断"这次没结论"）
            if (r.State == UpdateState.Failed || r.State == UpdateState.Disabled) { return 1; }
            return 0;
        }

        /// <summary>把条款正文打出来（命令行场景的非交互路径，见 docs/DISCLAIMER.md 5.1）。</summary>
        private static int Disclaimer()
        {
            Console.Write(DisclaimerForm.LoadText());
            Console.WriteLine();
            Console.WriteLine("source=" + DisclaimerForm.SourceHint);
            Console.WriteLine("hash=" + Consent.CurrentHash());
            Console.WriteLine("consent=" + Consent.StateId());
            return 0;
        }

        /// <summary>
        ///   consent            打印状态（key=value）
        ///   consent --accept   记录"已同意当前这版条款"（脚本 / 无人值守用）
        ///   consent --reset    清除记录（下次打开界面会重新要求确认）
        /// </summary>
        private static int ConsentCommand(string[] args)
        {
            bool accept = false;
            bool reset = false;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i].Trim().ToLowerInvariant();
                if (a == "--accept" || a == "accept") { accept = true; }
                else if (a == "--reset" || a == "reset") { reset = true; }
            }
            if (reset) { Consent.Reset(); }
            if (accept) { Consent.Accept(); }

            Console.WriteLine("action=consent");
            Console.WriteLine("consent=" + Consent.StateId());
            Console.WriteLine("consentAgreed=" + (Consent.IsAccepted() ? "yes" : "no"));
            Console.WriteLine("consentHash=" + Consent.StoredHash());
            Console.WriteLine("currentHash=" + Consent.CurrentHash());
            Console.WriteLine("agreedAt=" + Consent.AgreedAt());
            Console.WriteLine("settings=" + AppPaths.SettingsIni);
            return Consent.IsAccepted() ? 0 : 1;
        }
    }
}
