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

    /// <summary>Starts whatever a button points at. Every run is logged; black console windows
    /// are suppressed by default and the child output is captured asynchronously.</summary>
    internal static class Launcher
    {
        public const string ModulePermdel = "permdel";
        public const string ModuleApp = "app";

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
                candidates.Add(AppPaths.Expand(settings.PermanentDeleteExe));
            }
            string env = Environment.GetEnvironmentVariable("MXX1_PERMDEL_EXE");
            if (!string.IsNullOrEmpty(env)) { candidates.Add(env); }

            candidates.Add(Path.Combine(AppPaths.ExeDir, "PermanentDeleteSetup.exe"));
            // Walk up three levels looking for a sibling checkout: bin\ -> toolbox\ -> workspace\.
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

        /// <summary>Command line shown by "复制启动命令" and in the log.</summary>
        public static string DescribeCommand(ToolItem t, Settings settings, bool asAdmin)
        {
            if (t.Placeholder) { return "（P1 接入，暂不执行）" + t.Name; }
            switch (t.Kind)
            {
                case "exe":
                    return Quote(AppPaths.Expand(t.Path)) + (t.Args.Length > 0 ? " " + t.Args : "");
                case "script":
                    if (t.Inline.Length > 0) { return t.Shell + " -Command \"" + t.Inline + "\""; }
                    return t.Shell + " -File " + Quote(AppPaths.Expand(t.Path)) + (t.Args.Length > 0 ? " " + t.Args : "");
                case "open":
                    return "start \"\" " + AppPaths.Expand(t.Target);
                case "builtin":
                    if (t.Module == ModulePermdel)
                    {
                        string exe = FindPermanentDeleteExe(settings);
                        return Quote(exe.Length > 0 ? exe : "PermanentDeleteSetup.exe") + " " + PermdelArgs(t);
                    }
                    return "内置动作: " + t.Module + "/" + t.Action;
            }
            return t.Kind + " (未实现)";
        }

        private static string PermdelArgs(ToolItem t)
        {
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
                        string file = AppPaths.Expand(t.Path);
                        if (!File.Exists(file))
                        {
                            r.Ok = false; r.Message = "找不到程序：" + file; return r;
                        }
                        return Exec(file, t.Args, AppPaths.Expand(t.WorkDir), asAdmin, t.Wait, t.TimeoutSec);
                    }
                case "script":
                    {
                        string file = AppPaths.Expand(t.Path);
                        if (t.Inline.Length == 0 && !File.Exists(file))
                        {
                            r.Ok = false; r.Message = "找不到脚本：" + file; return r;
                        }
                        string shell = (t.Shell == "cmd") ? "cmd.exe" : "powershell.exe";
                        string args = (t.Shell == "cmd")
                            ? "/c " + (t.Inline.Length > 0 ? t.Inline : Quote(file) + " " + t.Args)
                            : "-NoProfile -ExecutionPolicy Bypass " +
                              (t.Inline.Length > 0 ? "-EncodedCommand " + ToBase64(t.Inline) : "-File " + Quote(file) + " " + t.Args);
                        return Exec(shell, args, AppPaths.Expand(t.WorkDir), asAdmin, true, t.TimeoutSec);
                    }
                case "open":
                    {
                        string target = AppPaths.Expand(t.Target);
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
                r.Message = "没找到 PermanentDeleteSetup.exe —— 请在「设置」里指定它的路径";
                return r;
            }

            switch (t.Action)
            {
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
