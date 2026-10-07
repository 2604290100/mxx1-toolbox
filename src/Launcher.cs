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
        public bool Deferred;              // 真正干活的是"提升权限后另起的那个进程"，结果稍后由交接文件送回来
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
        public string Legacy = "";  // sentence for Win7 (no Settings app) -- "" = use NoSettingsApp
        public bool UiAction;       // handled by MainForm (the 常用链接 window)

        /// <summary>Win7 上没有「设置」应用，这个入口会得到 NoSettingsApp / Legacy 那句说明。</summary>
        public const string NoSettingsApp = "这台系统没有 Windows 10 / 11 那个「设置」应用，这个页面打不开。"
            + "同样的开关在控制面板里，工具箱的「控制面板」按钮能到那儿。";

        /// <summary>这台系统认不认 ms-settings: 这类入口（「设置」应用是 Windows 10 起才有的）。
        /// 先看协议在注册表里有没有注册 —— 不看版本号，是因为没带清单的 exe 在 Win8.1 以上拿到的是
        /// 被"骗"过的版本号；注册表不会骗人。读不到注册表才退回版本号兜底。</summary>
        public static readonly bool HasSettingsApp = ProbeSettingsApp();

        private static bool ProbeSettingsApp()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k =
                    Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("ms-settings"))
                {
                    if (k != null) { return true; }
                }
            }
            catch { }
            try { return Environment.OSVersion.Version.Major >= 10; }
            catch { return true; }
        }

        public bool Exists
        {
            get
            {
                if (UiAction) { return true; }
                if (Path.Length > 0) { return File.Exists(AppPaths.Expand(Path)); }
                if (IsSettingsUrl) { return HasSettingsApp; }
                return true;    // shell: 文件夹（控制面板、设备和打印机）从 XP 起就有
            }
        }

        /// <summary>这个目标是 ms-settings: 页面（只有 Windows 10 / 11 有这个「设置」应用）。</summary>
        public bool IsSettingsUrl
        {
            get { return Url.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>系统上没有这个入口时该说的那句话。有名字的目标说自己那句，其余用通用那句。</summary>
        public string UnavailableMessage
        {
            get { return Legacy.Length > 0 ? Legacy : NoSettingsApp; }
        }
    }

    /// <summary>Starts whatever a button points at. Every run is logged; black console windows
    /// are suppressed by default and the child output is captured asynchronously.</summary>
    internal static class Launcher
    {
        /// <summary>这台机器是哪一版 Windows。清单里声明了 Win7 / 8 / 8.1 / 10，所以
        /// GetVersionEx（Environment.OSVersion）报的是**真实**版本号，不会像没清单的程序那样
        /// 在 Win8.1 以上一律报 6.2。只认我们支持的 Win7 / Win10 / Win11，别的一律如实报数字。</summary>
        public static string WindowsName()
        {
            try
            {
                Version v = Environment.OSVersion.Version;
                if (v.Major == 6 && v.Minor == 1) { return "Windows 7"; }
                if (v.Major == 6 && v.Minor == 2) { return "Windows 8"; }
                if (v.Major == 6 && v.Minor == 3) { return "Windows 8.1"; }
                if (v.Major == 10)
                {
                    return (v.Build >= 22000) ? "Windows 11" : "Windows 10";
                }
                return "Windows " + v.Major.ToString(CultureInfo.InvariantCulture)
                    + "." + v.Minor.ToString(CultureInfo.InvariantCulture);
            }
            catch { return "Windows（版本号读不出来）"; }
        }
        public const string ModulePermdel = "permdel";
        public const string ModuleApp = "app";
        public const string ModuleSystem = "system";
        public const string ModulePrivacy = "privacy";

        /// <summary>「常用设置」里那批直接写注册表的按钮（任务栏合并方式 / 开始菜单对齐 / 驱动自动
        /// 安装 / 内核隔离 / 资源管理器与右键菜单的 CLSID 覆盖）。和隐私开关共用同一套
        /// 记原值 + 读回核对 + 一键还原的机制（见 RegEngine）。</summary>
        public const string ModuleSysreg = "sysreg";

        /// <summary>「右键增强」：把「解除文件占用」「常用功能」装进 Windows 右键菜单
        /// （只写 HKCU\Software\Classes，见 src\RightMenu.cs 和 docs\DESIGN.md §14）。</summary>
        public const string ModuleRightMenu = "rightmenu";

        /// <summary>The「系统工具」page. Every entry is a read-only viewer or a Windows settings
        /// page: nothing here changes the system, so none of them needs elevation.</summary>
        private static readonly SystemTarget[] SystemTargets = new SystemTarget[]
        {
            FileTarget("devmgmt", "设备管理器", "%SystemRoot%\\System32\\devmgmt.msc",
                "这台电脑上找不到设备管理器（devmgmt.msc）"),
            UrlTarget("sound", "声音设置", "ms-settings:sound",
                "Win7 上没有「设置」应用。声音请到控制面板的「硬件和声音 - 声音」里调。"),
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
            ShellTarget("controlpanel", "控制面板", "shell:ControlPanelFolder"),

            // 诊断类：同样是「先探测再打开」，组件被精简掉的系统会得到一句说明而不是静默失败。
            FileTarget("eventvwr", "事件查看器", "%SystemRoot%\\System32\\eventvwr.msc",
                "这台电脑上找不到事件查看器（eventvwr.msc）—— 系统日志、蓝屏记录都在它里面"),
            FileTarget("perfmon", "性能监视器", "%SystemRoot%\\System32\\perfmon.exe",
                "这台电脑上找不到性能监视器（perfmon.exe）"),

            // 隐私设置页签上那 4 个"权限"按钮：权限逐个应用的开关只能在官方页面里点，
            // 但页面本身可以直达（工具箱不代改权限，也不假装能改）。
            UrlTarget("privacy-camera", "相机权限", "ms-settings:privacy-webcam",
                "Win7 上没有「设置」应用，也没有这套「按应用开关权限」的页面。相机在设备管理器里管（「图像设备」），"
                + "软件的相机权限由软件自己问。"),
            UrlTarget("privacy-microphone", "麦克风权限", "ms-settings:privacy-microphone",
                "Win7 上没有「设置」应用，也没有这套「按应用开关权限」的页面。麦克风在「声音 - 录制」里管，"
                + "软件的麦克风权限由软件自己问。"),
            UrlTarget("privacy-location", "位置权限", "ms-settings:privacy-location",
                "Win7 上没有「设置」应用。位置功能在控制面板的「位置和其他传感器」里，Win7 默认是关的。"),
            UrlTarget("privacy-background", "后台应用", "ms-settings:privacy-backgroundapps",
                "Win7 上没有「设置」应用，也没有「后台应用」这个概念（那是 Windows 10 的应用商店应用才有的）。"),

            // 应用管理页签上那两个"打开官方页面"的按钮。默认程序 / 应用和功能都在这些页面里改，
            // 工具箱不代改（改默认程序要按文件类型逐个设，代改只会把关联搞乱）。
            UrlTarget("defaultapps", "默认应用", "ms-settings:defaultapps",
                "Win7 上没有「设置」应用。默认程序请到控制面板的「默认程序」里改。"),
            UrlTarget("appfeatures", "应用和功能", "ms-settings:appsfeatures",
                "Win7 上没有「设置」应用。卸载程序请到控制面板的「程序和功能」里（工具箱的「程序和功能」按钮就是它）。")
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
            return UrlTarget(action, name, url, "");
        }

        /// <summary>legacy = Win7 上（没有「设置」应用）改说哪句话：直接告诉他去控制面板的哪儿。</summary>
        private static SystemTarget UrlTarget(string action, string name, string url, string legacy)
        {
            SystemTarget t = new SystemTarget();
            t.Action = action; t.Name = name; t.Url = url; t.Legacy = legacy;
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
                    return Quote(AppPaths.Resolve(t.Path)) + (t.Args.Length > 0 ? " " + ExpandArgs(t.Args) : "");
                case "script":
                    if (t.Inline.Length > 0)
                    {
                        // cmd has no -Command switch (it takes /c), so do not render a cmd script as
                        // "cmd -Command ..." -- that display cost a debugging round when read in the log.
                        return (t.Shell == "cmd")
                            ? "cmd /c \"" + t.Inline + "\""
                            : "powershell -Command \"" + t.Inline + "\"";
                    }
                    return t.Shell + " -File " + Quote(AppPaths.Resolve(t.Path))
                        + (t.Args.Length > 0 ? " " + ExpandArgs(t.Args) : "");
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
                    if (t.Module == ModulePrivacy)
                    {
                        // 悬停说明里附的就是这一行，所以写成一句人话，别把 options 里的 id 甩出去
                        if (string.Equals(t.Action, "optimize", StringComparison.OrdinalIgnoreCase)) { return "把这一页的开关全部关掉（可一键还原）"; }
                        if (string.Equals(t.Action, "restore", StringComparison.OrdinalIgnoreCase)) { return "按改动前的原值写回去（可重复还原）"; }
                        if (string.Equals(t.Action, "status", StringComparison.OrdinalIgnoreCase)) { return "只读：列出每个开关现在的状态"; }
                        RegItemSpec pi = Privacy.Find(t.Options);
                        if (pi == null) { return "隐私设置里没有这个开关：" + t.Options; }
                        return (string.Equals(t.Action, "on", StringComparison.OrdinalIgnoreCase) ? "开启「" : "关闭「") + pi.Name + "」（写注册表，可一键还原）";
                    }
                    if (t.Module == ModuleSysreg)
                    {
                        // 同样写成一句人话：悬停说明里附的就是这一行
                        if (string.Equals(t.Action, "restore", StringComparison.OrdinalIgnoreCase)) { return "按改动前的原值写回去（可重复还原）"; }
                        if (string.Equals(t.Action, "status", StringComparison.OrdinalIgnoreCase)) { return "只读：列出每个系统设置开关现在的状态和记下的原值"; }
                        bool on = string.Equals(t.Action, "on", StringComparison.OrdinalIgnoreCase);
                        if (string.Equals(t.Action, "off", StringComparison.OrdinalIgnoreCase) || on)
                        {
                            return SysReg.Describe(t.Options, on);
                        }
                        return "内置动作: " + t.Module + "/" + t.Action;
                    }
                    if (t.Module == ModuleRightMenu)
                    {
                        // 同样写成一句人话：悬停说明里附的就是这一行
                        string a = t.Action.ToLowerInvariant();
                        if (a == "unlock.on") { return "把「" + RightMenu.UnlockTitle + "」装进右键菜单（只写当前用户，可一键撤掉）"; }
                        if (a == "unlock.off") { return "把「" + RightMenu.UnlockTitle + "」从右键菜单里撤掉（只删工具箱自己写的键）"; }
                        if (a == "auto.on") { return "把「" + RightMenu.AutoTitle + "」装进右键菜单（点了不弹窗口，查到占用就直接结束那些程序）"; }
                        if (a == "auto.off") { return "把「" + RightMenu.AutoTitle + "」从右键菜单里撤掉（只删工具箱自己写的键）"; }
                        if (a == "common.on") { return "把「" + RightMenu.CommonTitle + "」子菜单装进右键菜单（内容 = 「常用」页：置顶 + 最近使用）"; }
                        if (a == "common.off") { return "把「" + RightMenu.CommonTitle + "」子菜单撤掉（只删工具箱自己写的键）"; }
                        if (a == "copy.on") { return "把「" + RightMenu.CopyTitle + "」装进右键菜单（右键文件 / 文件夹 → 完整路径进剪贴板）"; }
                        if (a == "copy.off") { return "把「" + RightMenu.CopyTitle + "」从右键菜单里撤掉（只删工具箱自己写的键）"; }
                        if (a == "terminal.on") { return "把「" + RightMenu.TerminalTitle + "」装进右键菜单（右键文件夹 / 空白处 → 在那个目录里开终端）"; }
                        if (a == "terminal.off") { return "把「" + RightMenu.TerminalTitle + "」从右键菜单里撤掉（只删工具箱自己写的键）"; }
                        if (a == "status") { return "只读：列出右键菜单里装了什么、子菜单现在几项"; }
                        if (a == "rebuild") { return "重写「" + RightMenu.CommonTitle + "」子菜单的内容（置顶 / 最近使用变了之后手动兜底）"; }
                        if (a == "help") { return "一页说明：装在哪、怎么卸干净、右键里看不到怎么办"; }
                        return "内置动作: " + t.Module + "/" + t.Action;
                    }
                    if (t.Module == ModuleApp)
                    {
                        // 程序**自己**的窗口（不是启动别的程序）：说明里必须写成一句人话。
                        // 原来没有这一支，于是悬停说明和「功能说明」窗口里显示的会是
                        // "内置动作: app/newtool" —— 那是给排查问题看的，不该出现在给用户看的文案里。
                        string ua = t.Action.ToLowerInvariant();
                        if (ua == "newtool") { return "在本程序里打开「新建按钮」窗口（不启动别的程序）"; }
                        if (ua == "exporttools") { return "把「我的工具」导出到一个文件（不启动别的程序）"; }
                        if (ua == "importtools") { return "从导出文件里把按钮并进来（不启动别的程序）"; }
                        if (ua == "hash") { return "在本程序里打开「文件哈希校验」窗口（只读算校验码，不上传、不启动别的程序）"; }
                        return "在本程序里打开的窗口: " + t.Action;
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

        /// <summary>Prefixed to every inline PowerShell button before it is encoded. PowerShell
        /// writes progress records ("正在准备首次使用模块。") to the redirected stderr as CLIXML,
        /// which the result window then shows as "#&lt; CLIXML &lt;Objs ...&gt;" garbage; turning
        /// progress off inside the script keeps the output readable.</summary>
        private const string NoProgressPrelude = "$ProgressPreference='SilentlyContinue';";

        /// <summary>Expands %SystemRoot% / %USERPROFILE% placeholders inside an argument list.
        /// The path field was always expanded but the arguments were passed verbatim, so
        /// 「hosts 修改」 handed Notepad the literal text "%SystemRoot%\System32\drivers\etc\hosts"
        /// and Notepad answered "找不到文件" instead of opening the hosts file (a live report from
        /// the user, 2026-10-04). Only %VAR% is touched: switches, quotes and several arguments
        /// stay exactly as the manifest wrote them.</summary>
        private static string ExpandArgs(string args)
        {
            if (string.IsNullOrEmpty(args)) { return ""; }
            return AppPaths.Expand(args);
        }

        /// <summary>showResult：这条命令是**从资源管理器右键菜单**调起来的（`run <id> --show`）——
        /// 那个 exe 是 winexe、没有控制台，所以需要弹窗口的那条路要一路传下去（提权那一步尤其：
        /// 真正干活的是提升权限后的子进程，只有让**它**知道 `--show`，结果才弹得出来）。</summary>
        public static LaunchResult Run(ToolItem t, Settings settings, bool asAdmin)
        {
            return Run(t, settings, asAdmin, false);
        }

        public static LaunchResult Run(ToolItem t, Settings settings, bool asAdmin, bool showResult)
        {
            LaunchResult r = new LaunchResult();
            if (t == null) { r.Ok = false; r.Message = "按钮定义为空"; return r; }

            switch (t.Kind)
            {
                case "builtin":
                    return RunBuiltin(t, settings, asAdmin, showResult);
                case "exe":
                    {
                        string file = AppPaths.Resolve(t.Path);
                        string exeArgs = ExpandArgs(t.Args);
                        // A button can still point at a .lnk (made by an older build, or typed in by
                        // hand): resolve it so the program starts even if the shortcut is moved later.
                        // Dropping a shortcut now stores the real target directly (see DroppedFile).
                        if (DroppedFile.IsShortcut(file))
                        {
                            string lnkTarget, lnkArgs, lnkDir;
                            if (DroppedFile.ResolveShortcut(file, out lnkTarget, out lnkArgs, out lnkDir)
                                && lnkTarget.Length > 0)
                            {
                                file = lnkTarget;
                                if (exeArgs.Length == 0) { exeArgs = lnkArgs; }
                            }
                        }
                        if (Directory.Exists(file))
                        {
                            // e.g. a shortcut that points at a folder: open it like a double click.
                            try
                            {
                                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                                r.Message = "已打开文件夹：" + file;
                            }
                            catch (Exception ex) { r.Ok = false; r.Message = "打开失败：" + ex.Message; }
                            return r;
                        }
                        if (!File.Exists(file))
                        {
                            r.Ok = false; r.Message = "找不到程序：" + file; return r;
                        }
                        return Exec(file, exeArgs, AppPaths.Resolve(t.WorkDir), asAdmin, t.Wait, t.TimeoutSec);
                    }
                case "script":
                    {
                        string file = AppPaths.Resolve(t.Path);
                        if (t.Inline.Length == 0 && !File.Exists(file))
                        {
                            r.Ok = false; r.Message = "找不到脚本：" + file; return r;
                        }
                        // 需要管理员的脚本不走"runas 起 powershell"：那样每次都会弹出一个可见的
                        // PowerShell 控制台窗口（用户 2026-10-04 的反馈）。改成把自己以管理员身份
                        // 再起一遍（本程序是 winexe，没有控制台），由那个进程静默跑、结果写交接文件。
                        if (asAdmin && !IsAdmin()) { return LaunchElevatedCopy(t, showResult); }
                        string shell = (t.Shell == "cmd") ? "cmd.exe" : "powershell.exe";
                        string args = (t.Shell == "cmd")
                            ? "/c " + (t.Inline.Length > 0 ? t.Inline : Quote(file) + " " + ExpandArgs(t.Args))
                            : "-NoProfile -ExecutionPolicy Bypass " +
                              (t.Inline.Length > 0 ? "-EncodedCommand " + ToBase64(NoProgressPrelude + t.Inline) : "-File " + Quote(file) + " " + ExpandArgs(t.Args));
                        return Exec(shell, args, AppPaths.Resolve(t.WorkDir), false, true, t.TimeoutSec);
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

        private static LaunchResult RunBuiltin(ToolItem t, Settings settings, bool asAdmin, bool showResult)
        {
            LaunchResult r = new LaunchResult();
            if (t.Module == ModuleApp)
            {
                r.UiAction = true;
                r.UiActionName = t.Action;
                return r;
            }

            if (t.Module == ModuleSystem) { return RunSystem(t); }

            if (t.Module == ModulePrivacy) { return RunPrivacy(t, asAdmin, showResult); }

            if (t.Module == ModuleSysreg) { return RunSysreg(t, asAdmin, showResult); }

            if (t.Module == ModuleRightMenu) { return RunRightMenu(t); }

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

            // 两条不同的"打不开"：① 组件被精简掉 / 版本里没有 → Missing 那句；
            // ② 这台系统没有这个入口（Win7 没有 ms-settings: 的「设置」应用）→ UnavailableMessage。
            if (!target.Exists)
            {
                r.Ok = false;
                r.Message = target.Path.Length > 0
                    ? (target.Missing.Length > 0 ? target.Missing : ("找不到 " + AppPaths.Expand(target.Path)))
                    : target.UnavailableMessage;
                return r;
            }

            string file = "";
            string args = "";
            if (target.Path.Length > 0)
            {
                file = AppPaths.Expand(target.Path);
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

        /// <summary>「隐私设置」page. The registry write happens inside this process (it must, so the
        /// value can be read back and verified), which means an HKLM switch cannot elevate itself --
        /// so the toolbox re-launches ITSELF with `run &lt;id&gt; --admin`. That child is a winexe with no
        /// console, so its report goes to the log file and to a hand-off file the parent picks up a
        /// few seconds later (MainForm.PickUpElevatedResult) instead of leaving the user with
        /// "已请求管理员权限" and no outcome.</summary>
        private static LaunchResult RunPrivacy(ToolItem t, bool asAdmin, bool showResult)
        {
            LaunchResult r = new LaunchResult();
            if (Privacy.NeedsAdmin(t.Options) && asAdmin && !IsAdmin())
            {
                return LaunchElevatedCopy(t, showResult);
            }

            bool ok;
            string report;
            if (string.Equals(t.Action, "optimize", StringComparison.OrdinalIgnoreCase)) { report = Privacy.Optimize(out ok); }
            else if (string.Equals(t.Action, "restore", StringComparison.OrdinalIgnoreCase)) { report = Privacy.Restore(out ok); }
            else if (string.Equals(t.Action, "status", StringComparison.OrdinalIgnoreCase)) { report = Privacy.Status(); ok = true; }
            else { report = Privacy.Set(t.Options, string.Equals(t.Action, "on", StringComparison.OrdinalIgnoreCase), out ok); }

            r.Ok = ok;
            r.Output = report;
            r.Message = ok ? "隐私设置已处理" : "有值没成功（细节见报告）";
            // 提升权限的那个子进程没有控制台，日志是它唯一的出口；写在这里两边都覆盖
            Logger.Write(t.Name, report);
            return r;
        }

        /// <summary>「常用设置」里写注册表的那批按钮。和隐私开关同一条路：HKLM 的改动没法在
        /// 本进程里自我提权，于是工具箱用 `run &lt;id&gt; --admin` 把自己再起一遍，那个子进程没有
        /// 控制台，报告写进日志和交接文件，父进程过几秒取出来弹窗口。</summary>
        private static LaunchResult RunSysreg(ToolItem t, bool asAdmin, bool showResult)
        {
            LaunchResult r = new LaunchResult();
            // 「还原设置改动」要把 HKLM 的原值写回去，所以它自己也要管理员；它的 options 是空的，
            // 问不出开关是谁，只能按 action 判断。
            bool needAdmin = SysReg.NeedsAdmin(t.Options)
                || string.Equals(t.Action, "restore", StringComparison.OrdinalIgnoreCase);
            if (needAdmin && asAdmin && !IsAdmin())
            {
                return LaunchElevatedCopy(t, showResult);
            }

            bool ok;
            string report;
            if (string.Equals(t.Action, "restore", StringComparison.OrdinalIgnoreCase)) { report = SysReg.Restore(out ok); }
            else if (string.Equals(t.Action, "status", StringComparison.OrdinalIgnoreCase)) { report = SysReg.Status(); ok = true; }
            else { report = SysReg.Set(t.Options, string.Equals(t.Action, "on", StringComparison.OrdinalIgnoreCase), out ok); }

            r.Ok = ok;
            r.Output = report;
            r.Message = ok ? "系统设置已处理" : "有地方没成功（细节见报告）";
            Logger.Write(t.Name, report);
            return r;
        }

        /// <summary>「右键增强」：把三件事装进 / 撤出 Windows 右键菜单，外加状态、重建、说明。
        /// 只写 HKCU（**不需要管理员**，所以没有 runas 那条路），写之前记原值、写完读回核对，
        /// 撤掉按记录只删自己那几个键。三条底线见 src\RightMenu.cs 的注释和 docs\DESIGN.md §14.7。</summary>
        private static LaunchResult RunRightMenu(ToolItem t)
        {
            LaunchResult r = new LaunchResult();
            bool ok;
            string report;
            string a = t.Action.ToLowerInvariant();
            if (a == "unlock.on") { report = RightMenu.Install(One(RightMenu.ItemUnlock), out ok); }
            else if (a == "unlock.off") { report = RightMenu.Uninstall(One(RightMenu.ItemUnlock), out ok); }
            else if (a == "auto.on") { report = RightMenu.Install(One(RightMenu.ItemAuto), out ok); }
            else if (a == "auto.off") { report = RightMenu.Uninstall(One(RightMenu.ItemAuto), out ok); }
            else if (a == "common.on") { report = RightMenu.Install(One(RightMenu.ItemCommon), out ok); }
            else if (a == "common.off") { report = RightMenu.Uninstall(One(RightMenu.ItemCommon), out ok); }
            else if (a == "copy.on") { report = RightMenu.Install(One(RightMenu.ItemCopy), out ok); }
            else if (a == "copy.off") { report = RightMenu.Uninstall(One(RightMenu.ItemCopy), out ok); }
            else if (a == "terminal.on") { report = RightMenu.Install(One(RightMenu.ItemTerminal), out ok); }
            else if (a == "terminal.off") { report = RightMenu.Uninstall(One(RightMenu.ItemTerminal), out ok); }
            else if (a == "status") { report = RightMenu.Status(); ok = true; }
            else if (a == "rebuild") { report = RightMenu.Rebuild(out ok); }
            else if (a == "help") { report = RightMenu.Help(); ok = true; }
            else { report = "右键增强里没有这个动作：" + t.Action; ok = false; }

            r.Ok = ok;
            r.Output = report;
            r.Message = ok ? "右键增强已处理" : "有地方没成功（细节见报告）";
            Logger.Write(t.Name, report);
            return r;
        }

        /// <summary>装 / 撤只要一项时的小包装（`RightMenu.Install` 收的是"要动哪几项"的清单）。</summary>
        private static System.Collections.Generic.List<string> One(string item)
        {
            return new System.Collections.Generic.List<string>(new string[] { item });
        }

        /// <summary>「需要管理员」的统一做法：把自己以管理员身份再起一遍，命令是 `run &lt;id&gt; --admin`。
        /// 子进程是 winexe（没有控制台），所以它把结果写进交接文件，父进程过几秒读出来弹窗口
        /// （MainForm.PickUpElevatedResult）—— 否则用户只看到一句"已请求管理员权限"就没了下文。
        /// 只有这一条路能既提权又不闪出控制台窗口：直接 runas 起 powershell / cmd 会开一个真窗口。</summary>
        internal static LaunchResult LaunchElevatedCopy(ToolItem t)
        {
            return LaunchElevatedCopy(t, false);
        }

        internal static LaunchResult LaunchElevatedCopy(ToolItem t, bool showResult)
        {
            LaunchResult r = new LaunchResult();
            try
            {
                try { File.Delete(AppPaths.ElevatedResultFile); } catch { }
                // 提权子进程也要知道"--show"：否则从右键菜单点一个需要管理员的按钮，
                // 结果又消失在那个没有控制台的进程里（结果交接文件那条路只服务主界面）。
                ProcessStartInfo psi = new ProcessStartInfo(AppPaths.ExePath,
                    "run " + t.Id + " --admin" + (showResult ? " --show" : ""));
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                r.Deferred = true;
                r.Message = "已请求以管理员身份运行（请在 UAC 窗口确认）—— 结果几秒后自动弹出来";
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Message = "请求管理员权限失败：" + ex.Message;
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

                // 已经提权了就别再 runas 一次：UseShellExecute=true 会**开一个真控制台窗口**
                // （CreateNoWindow 被忽略），于是每点一次需要管理员的按钮就闪一下 PowerShell ——
                // 用户 2026-10-04 的反馈。已经在管理员上下文里时直接静默跑就行。
                if (elevate && IsAdmin()) { elevate = false; }

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
