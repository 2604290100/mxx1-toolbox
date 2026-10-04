// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    internal sealed class LaunchResult
    {
        public bool Ok = true;
        public string Message = "";
        public int ExitCode;
        public string Output = "";
        public bool UiAction;              // handled by MainForm itself
        public string UiActionName = "";
    }

    /// <summary>One entry of the「系统工具」page: which Windows component it starts and what to say
    /// when that component is not installed (Windows Home has no gpedit, for example). No machine
    /// specific path is stored: %SystemRoot% style placeholders are expanded when it runs, so the
    /// same exe works on every Windows install and on every drive.</summary>
    internal sealed class SystemTarget
    {
        public string Action = "";
        public string Name = "";
        public string Path = "";    // file started through ShellExecute
        public string Shell = "";   // shell: folder opened through explorer.exe
        public string Url = "";     // ms-settings: / https: target
        public string Missing = ""; // sentence shown when Path is not on this machine
        public bool UiAction;       // handled by MainForm (the 常用链接 window)

        public bool Exists
        {
            get
            {
                if (UiAction) { return true; }
                if (Path.Length > 0) { return File.Exists(AppPaths.Expand(Path)); }
                return true;    // shell: folders and ms-settings: pages always exist
            }
        }
    }

    /// <summary>Starts whatever a button points at. Every run is logged; black console windows
    /// are suppressed by default and the child output is captured asynchronously.</summary>
    internal static class Launcher
    {
        public const string ModulePermdel = "permdel";
        public const string ModuleApp = "app";
        public const string ModuleSystem = "system";

        /// <summary>The「系统工具」page. Every entry is a read-only viewer or a Windows settings
        /// page: nothing here changes the system, so none of them needs elevation.</summary>
        private static readonly SystemTarget[] SystemTargets = new SystemTarget[]
        {
            FileTarget("devmgmt", "设备管理器", "%SystemRoot%\\System32\\devmgmt.msc",
                "这台电脑上找不到设备管理器（devmgmt.msc）"),
            UrlTarget("sound", "声音设置", "ms-settings:sound"),
            ShellTarget("printers", "设备和打印机", "shell:PrintersFolder"),
            FileTarget("taskschd", "任务计划程序", "%SystemRoot%\\System32\\taskschd.msc",
                "这台电脑上找不到任务计划程序（taskschd.msc）"),

            FileTarget("regedit", "注册表编辑器", "%SystemRoot%\\regedit.exe",
                "这台电脑上找不到注册表编辑器（regedit.exe）"),
            FileTarget("services", "服务", "%SystemRoot%\\System32\\services.msc",
                "这台电脑上找不到服务管理器（services.msc）"),
            FileTarget("gpedit", "本地组策略编辑器", "%SystemRoot%\\System32\\gpedit.msc",
                "这台电脑是 Windows 家庭版，没有「本地组策略编辑器」（gpedit.msc）"),
            FileTarget("appwiz", "程序和功能", "%SystemRoot%\\System32\\appwiz.cpl",
                "这台电脑上找不到「程序和功能」（appwiz.cpl）"),

            FileTarget("taskmgr", "任务管理器", "%SystemRoot%\\System32\\taskmgr.exe",
                "这台电脑上找不到任务管理器（taskmgr.exe）"),
            FileTarget("sysinfo", "系统信息", "%SystemRoot%\\System32\\msinfo32.exe",
                "这台电脑上找不到系统信息（msinfo32.exe）"),
            WindowTarget("links", "常用链接"),
            ShellTarget("controlpanel", "控制面板", "shell:ControlPanelFolder")
        };

        // NOTE: these helpers must not be called File / Shell / Url / Links -- a method named File
        // hides System.IO.File inside this class and every File.Exists call stops compiling (CS0119).
        private static SystemTarget FileTarget(string action, string name, string path, string missing)
        {
            SystemTarget t = new SystemTarget();
            t.Action = action; t.Name = name; t.Path = path; t.Missing = missing;
            return t;
        }

        private static SystemTarget ShellTarget(string action, string name, string shell)
        {
            SystemTarget t = new SystemTarget();
            t.Action = action; t.Name = name; t.Shell = shell;
            return t;
        }

        private static SystemTarget UrlTarget(string action, string name, string url)
        {
            SystemTarget t = new SystemTarget();
            t.Action = action; t.Name = name; t.Url = url;
            return t;
        }

        private static SystemTarget WindowTarget(string action, string name)
        {
            SystemTarget t = new SystemTarget();
            t.Action = action; t.Name = name; t.UiAction = true;
            return t;
        }

        public static SystemTarget FindSystemTarget(string action)
        {
            foreach (SystemTarget t in SystemTargets)
            {
                if (string.Equals(t.Action, action, StringComparison.OrdinalIgnoreCase)) { return t; }
            }
            return null;
        }

        public static SystemTarget[] AllSystemTargets() { return SystemTargets; }

        public static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>Locates the sibling project's installer. Nothing is hard coded:
        /// settings first, then an environment override, then relative and per-user locations.</summary>
        public static string FindPermanentDeleteExe(Settings settings)
        {
            List<string> candidates = new List<string>();
            if (settings != null && !string.IsNullOrEmpty(settings.PermanentDeleteExe))
            {
                candidates.Add(AppPaths.Resolve(settings.PermanentDeleteExe));
            }
            string env = Environment.GetEnvironmentVariable("MXX1_PERMDEL_EXE");
            if (!string.IsNullOrEmpty(env)) { candidates.Add(env); }

            // ① the tool folder next to the exe: drop PermanentDeleteSetup.exe in and it is found.
            //    This is the folder the user is meant to use for every external tool (bin-tools\).
            candidates.Add(Path.Combine(AppPaths.PayloadDir, "PermanentDeleteSetup.exe"));
            // ② the same file lying loose next to the exe (the "one flat folder" case)
            candidates.Add(Path.Combine(AppPaths.ExeDir, "PermanentDeleteSetup.exe"));
            // ③ per user tool folder (where an embedded payload would be extracted to, later)
            candidates.Add(Path.Combine(AppPaths.UserPayloadDir, "PermanentDeleteSetup.exe"));
            // ④ Walk up three levels looking for a sibling checkout: bin\ -> toolbox\ -> workspace\.
            try
            {
                DirectoryInfo dir = new DirectoryInfo(AppPaths.ExeDir);
                for (int level = 0; level < 3 && dir != null; level++)
                {
                    candidates.Add(Path.Combine(dir.FullName, "permanent-delete-menu\\bin\\PermanentDeleteSetup.exe"));
                    candidates.Add(Path.Combine(dir.FullName, "permanent-delete-menu\\PermanentDeleteSetup.exe"));
                    dir = dir.Parent;
                }
            }
            catch { }
            candidates.Add(Path.Combine(AppPaths.PermdelDir, "PermanentDeleteSetup.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs\\PermanentDelete\\PermanentDeleteSetup.exe"));

            foreach (string c in candidates)
            {
                try { if (!string.IsNullOrEmpty(c) && File.Exists(c)) { return c; } }
                catch { }
            }
            return "";
        }

        /// <summary>Shown when an external tool is missing: it names the folder to drop it into.</summary>
        public static string MissingToolMessage(string fileName)
        {
            return "没找到 " + fileName + "。把它放进工具目录：" + Environment.NewLine
                + AppPaths.PayloadDir + Environment.NewLine
                + "或者在本程序的「设置」里指定它的完整路径。";
        }

        /// <summary>Command line shown by "复制启动命令" and in the log.</summary>
        public static string DescribeCommand(ToolItem t, Settings settings, bool asAdmin)
        {
            if (t.Placeholder) { return "（P1 接入，暂不执行）" + t.Name; }
            switch (t.Kind)
            {
                case "exe":
                    return Quote(AppPaths.Resolve(t.Path)) + (t.Args.Length > 0 ? " " + t.Args : "");
                case "script":
                    if (t.Inline.Length > 0) { return t.Shell + " -Command \"" + t.Inline + "\""; }
                    return t.Shell + " -File " + Quote(AppPaths.Resolve(t.Path)) + (t.Args.Length > 0 ? " " + t.Args : "");
                case "open":
                    return "start \"\" " + AppPaths.Resolve(t.Target);
                case "builtin":
                    if (t.Module == ModulePermdel)
                    {
                        string exe = FindPermanentDeleteExe(settings);
                        string args = PermdelArgs(t);
                        return Quote(exe.Length > 0 ? exe : "PermanentDeleteSetup.exe")
                            + (args.Length > 0 ? " " + args : "");
                    }
                    if (t.Module == ModuleSystem)
                    {
                        SystemTarget st = FindSystemTarget(t.Action);
                        if (st == null) { return "系统工具里没有这个动作：" + t.Action; }
                        if (st.Path.Length > 0) { return AppPaths.Expand(st.Path); }
                        if (st.Shell.Length > 0) { return "explorer.exe " + st.Shell; }
                        if (st.Url.Length > 0) { return st.Url; }
                        return "本程序内的窗口（" + st.Name + "）";
                    }
                    return "内置动作: " + t.Module + "/" + t.Action;
            }
            return t.Kind + " (未实现)";
        }

        private static string PermdelArgs(ToolItem t)
        {
            // "gui" means "just open the installer window": the sibling program shows its own
            // window when it is started without a command, so no argument is passed at all.
            if (string.Equals(t.Action, "gui", StringComparison.OrdinalIgnoreCase)) { return ""; }
            string args = t.Action;
            if (!string.IsNullOrEmpty(t.Options)) { args += " " + t.Options; }
            return args.Trim();
        }

        public static LaunchResult Run(ToolItem t, Settings settings, bool asAdmin)
        {
            LaunchResult r = new LaunchResult();
            if (t == null) { r.Ok = false; r.Message = "按钮定义为空"; return r; }

            switch (t.Kind)
            {
                case "builtin":
                    return RunBuiltin(t, settings, asAdmin);
                case "exe":
                    {
                        string file = AppPaths.Resolve(t.Path);
                        if (!File.Exists(file))
                        {
                            r.Ok = false; r.Message = "找不到程序：" + file; return r;
                        }
                        return Exec(file, t.Args, AppPaths.Resolve(t.WorkDir), asAdmin, t.Wait, t.TimeoutSec);
                    }
                case "script":
                    {
                        string file = AppPaths.Resolve(t.Path);
                        if (t.Inline.Length == 0 && !File.Exists(file))
                        {
                            r.Ok = false; r.Message = "找不到脚本：" + file; return r;
                        }
                        string shell = (t.Shell == "cmd") ? "cmd.exe" : "powershell.exe";
                        string args = (t.Shell == "cmd")
                            ? "/c " + (t.Inline.Length > 0 ? t.Inline : Quote(file) + " " + t.Args)
                            : "-NoProfile -ExecutionPolicy Bypass " +
                              (t.Inline.Length > 0 ? "-EncodedCommand " + ToBase64(t.Inline) : "-File " + Quote(file) + " " + t.Args);
                        return Exec(shell, args, AppPaths.Resolve(t.WorkDir), asAdmin, true, t.TimeoutSec);
                    }
                case "open":
                    {
                        string target = AppPaths.Resolve(t.Target);
                        if (target.Length == 0) { r.Ok = false; r.Message = "按钮没有配置 target"; return r; }
                        try
                        {
                            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                            r.Message = "已打开：" + target;
                        }
                        catch (Exception ex)
                        {
                            r.Ok = false; r.Message = "打开失败：" + ex.Message;
                        }
                        return r;
                    }
            }
            r.Ok = false;
            r.Message = "还不支持的按钮类型：" + t.Kind;
            return r;
        }

        private static LaunchResult RunBuiltin(ToolItem t, Settings settings, bool asAdmin)
        {
            LaunchResult r = new LaunchResult();
            if (t.Module == ModuleApp)
            {
                r.UiAction = true;
                r.UiActionName = t.Action;
                return r;
            }

            if (t.Module == ModuleSystem) { return RunSystem(t); }

            if (t.Module != ModulePermdel)
            {
                r.Ok = false;
                r.Message = "不认识的内置模块：" + t.Module;
                return r;
            }

            string exe = FindPermanentDeleteExe(settings);
            if (exe.Length == 0)
            {
                r.Ok = false;
                r.Message = MissingToolMessage("PermanentDeleteSetup.exe");
                return r;
            }

            switch (t.Action)
            {
                case "gui":
                    {
                        LaunchResult inner = Exec(exe, "", "", false, false, 0);
                        if (inner.Ok) { inner.Message = "已打开「永久删除」安装器窗口"; }
                        return inner;
                    }
                case "install":
                case "uninstall":
                    {
                        bool elevate = asAdmin && !IsAdmin();
                        LaunchResult inner = Exec(exe, PermdelArgs(t), "", elevate, false, t.TimeoutSec);
                        if (inner.Ok)
                        {
                            inner.Message = elevate ? "已请求" + t.Name + "（请在 UAC 窗口确认）" : t.Name + " 已执行";
                        }
                        return inner;
                    }
                case "enginelog":
                    {
                        string[] lines = Logger.TailNewestOf(AppPaths.PermdelEngineLog, 500);
                        r.Output = (lines.Length == 0)
                            ? "还没有引擎日志：" + AppPaths.PermdelEngineLog
                            : string.Join(Environment.NewLine, lines);
                        r.Message = "引擎日志（最新在最上）";
                        return r;
                    }
                default:
                    {
                        LaunchResult inner = Exec(exe, PermdelArgs(t), "", asAdmin && !IsAdmin(), true, t.TimeoutSec);
                        return inner;
                    }
            }
        }

        /// <summary>「系统工具」page: open the Windows component behind the button. Every target is
        /// reached through ShellExecute so Windows chooses the right host (mmc for a .msc, the
        /// control panel for a .cpl, Settings for ms-settings:) and no console window appears.
        /// A component that is not installed on this Windows edition produces a full sentence
        /// instead of a silent no-op.</summary>
        private static LaunchResult RunSystem(ToolItem t)
        {
            LaunchResult r = new LaunchResult();
            SystemTarget target = FindSystemTarget(t.Action);
            if (target == null)
            {
                r.Ok = false;
                r.Message = "系统工具里没有这个动作：" + t.Action;
                return r;
            }
            if (target.UiAction)
            {
                r.UiAction = true;
                r.UiActionName = target.Action;
                return r;
            }

            string file = "";
            string args = "";
            if (target.Path.Length > 0)
            {
                file = AppPaths.Expand(target.Path);
                if (!File.Exists(file))
                {
                    r.Ok = false;
                    r.Message = target.Missing.Length > 0 ? target.Missing : ("找不到 " + file);
                    return r;
                }
            }
            else if (target.Shell.Length > 0)
            {
                // explorer.exe hands a shell: folder to the running Explorer; starting it directly
                // would open a console window on some systems.
                file = "explorer.exe";
                args = target.Shell;
            }
            else if (target.Url.Length > 0)
            {
                file = target.Url;
            }
            else
            {
                r.Ok = false;
                r.Message = "这个系统工具没有配置目标：" + t.Action;
                return r;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = true;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                r.Message = "已打开" + target.Name;
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Message = "打开" + target.Name + "失败：" + ex.Message;
            }
            return r;
        }

        private static LaunchResult Exec(string file, string args, string workDir, bool elevate, bool wait, int timeoutSec)
        {
            LaunchResult r = new LaunchResult();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir)) { psi.WorkingDirectory = workDir; }

                if (elevate)
                {
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                    r.Message = "已请求以管理员身份运行（请在 UAC 窗口确认）";
                    return r;
                }

                // Anything that is not a real executable (.vbs / .lnk / .bat / a document) cannot be
                // started with CreateProcess, so those go through the shell like a double click.
                // No output is captured for them -- the shell owns the process from then on.
                string ext = "";
                try { ext = Path.GetExtension(file).ToLowerInvariant(); }
                catch { }
                if (ext != ".exe" && ext != ".com")
                {
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                    r.Message = "已启动：" + Path.GetFileName(file);
                    return r;
                }

                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    StringBuilder err = new StringBuilder();
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (err) { err.AppendLine(e.Data); } }
                    };
                    p.Start();
                    p.BeginErrorReadLine();
                    // Read while the child runs: waiting first and reading afterwards deadlocks
                    // as soon as the output exceeds the 4 KB pipe buffer.
                    System.Threading.Tasks.Task<string> outTask = p.StandardOutput.ReadToEndAsync();

                    if (!wait)
                    {
                        r.Message = "已启动：" + Path.GetFileName(file);
                        return r;
                    }

                    bool finished;
                    if (timeoutSec > 0) { finished = p.WaitForExit(timeoutSec * 1000); }
                    else { p.WaitForExit(); finished = true; }

                    if (timeoutSec > 0 && !finished)
                    {
                        try { p.Kill(); } catch { }
                        r.Ok = false;
                        r.Message = "超时（" + timeoutSec + " 秒）已终止";
                        return r;
                    }

                    string stdout = outTask.Result;
                    r.ExitCode = p.ExitCode;
                    string stderr = err.ToString();
                    StringBuilder sb = new StringBuilder();
                    if (stdout != null && stdout.Trim().Length > 0) { sb.Append(stdout.TrimEnd()); }
                    if (stderr.Trim().Length > 0)
                    {
                        if (sb.Length > 0) { sb.AppendLine(); }
                        sb.Append("[stderr] ").Append(stderr.TrimEnd());
                    }
                    r.Output = sb.ToString();
                    r.Ok = (p.ExitCode == 0);
                    r.Message = Path.GetFileName(file) + " 退出码 " + p.ExitCode.ToString(CultureInfo.InvariantCulture);
                    return r;
                }
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Message = "启动失败：" + ex.Message;
                return r;
            }
        }

        private static string ToBase64(string text)
        {
            return Convert.ToBase64String(Encoding.Unicode.GetBytes(text));
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) { return "\"\""; }
            return value.IndexOf(' ') >= 0 ? "\"" + value + "\"" : value;
        }

        /// <summary>Opens Explorer with the target selected (used by the button context menu).</summary>
        public static void RevealInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) { return; }
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", Quote(path)) { UseShellExecute = true });
                    return;
                }
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select," + Quote(path)) { UseShellExecute = true });
                }
            }
            catch { }
        }
    }
}
