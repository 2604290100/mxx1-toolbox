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
            }            string command = args[0].ToLowerInvariant().TrimStart('-', '/');
            try
            {
                switch (command)
                {
                    case "list": return List(args);
                    case "run": return RunOne(args);
                    case "status": return Status();
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
            Console.WriteLine("  Mxx1Toolbox.exe status               打印 key=value 状态（脚本用）");
            Console.WriteLine("  Mxx1Toolbox.exe checkupdate          只读版本号，不下载不替换");
            Console.WriteLine("  Mxx1Toolbox.exe help                 这份帮助");
            Console.WriteLine();
            Console.WriteLine("页签 id: " + string.Join(" / ", Tabs.Ids));
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
                Console.Error.WriteLine("用法: Mxx1Toolbox.exe run <id> [--admin]");
                return 2;
            }
            string id = args[1];
            bool admin = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--admin") { admin = true; }
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
            Console.WriteLine("command=" + command);

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
            if (r.Output != null && r.Output.Length > 0) { Console.WriteLine(r.Output); }
            Console.WriteLine("result=" + (r.Ok ? "ok" : "failed"));
            Console.WriteLine("message=" + r.Message);
            Console.WriteLine("exit=" + r.ExitCode.ToString(CultureInfo.InvariantCulture));
            Logger.Write(target.Name, (r.Ok ? "完成" : "失败") + " · 命令行 · " + r.Message);
            return r.Ok ? 0 : 1;
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

            Console.WriteLine("name=" + AboutForm.ProductTitle);
            Console.WriteLine("version=" + AboutForm.VersionText);
            Console.WriteLine("buttons=" + tools.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("placeholders=" + placeholders.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("dangerous=" + dangers.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("theme=" + settings.Theme);
            Console.WriteLine("themeResolved=" + Settings.ResolveTheme(settings.Theme));
            Console.WriteLine("clickMode=" + settings.ClickMode);
            Console.WriteLine("admin=" + (Launcher.IsAdmin() ? "yes" : "no"));
            Console.WriteLine("log=" + Logger.CurrentFile());
            Console.WriteLine("settings=" + AppPaths.SettingsIni);
            Console.WriteLine("userTools=" + AppPaths.UserToolsJson);
            Console.WriteLine("permdelExe=" + (permdel.Length > 0 ? permdel : "(未找到)"));
            Console.WriteLine("permdelLog=" + AppPaths.PermdelEngineLog);
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
