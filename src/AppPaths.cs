// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.IO;
using System.Reflection;

namespace Mxx1Toolbox
{
    /// <summary>Every path the toolbox touches. No absolute developer path is ever hard coded
    /// here: the permanent-delete installer is located at run time (see Launcher).</summary>
    internal static class AppPaths
    {
        public static string LocalAppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        public static string BaseDir { get { return Path.Combine(LocalAppData, "mxx1-toolbox"); } }
        public static string LogDir { get { return Path.Combine(BaseDir, "logs"); } }
        public static string SettingsIni { get { return Path.Combine(BaseDir, "settings.ini"); } }
        public static string UserToolsJson { get { return Path.Combine(BaseDir, "tools.json"); } }

        /// <summary>改动前的原值：隐私开关的「一键还原」按这份记录写回去（改动前才记，重复点不会
        /// 把已经改过的值当成原值）。</summary>
        public static string PrivacyBackupFile { get { return Path.Combine(BaseDir, "privacy-original.tsv"); } }

        /// <summary>同一套机制，记的是「常用设置」里那批写注册表的系统设置（任务栏合并方式 /
        /// 开始菜单对齐 / 驱动自动安装 / 内核隔离 / 资源管理器与右键菜单的 CLSID 覆盖）。</summary>
        public static string SysRegBackupFile { get { return Path.Combine(BaseDir, "sysreg-original.tsv"); } }

        /// <summary>结果交接文件。按钮需要管理员权限时工具箱会把自己以管理员身份再起一遍，而那个
        /// 子进程是 winexe（没有控制台），Console 输出等于扔掉 —— 所以它把结果写在这里，
        /// 父进程过几秒读出来，照常弹结果窗口（否则用户只看到一句"已请求管理员权限"就没了下文）。</summary>
        public static string ElevatedResultFile { get { return Path.Combine(BaseDir, "last-elevated-result.txt"); } }

        /// <summary>Every external tool lives in one sub folder next to the exe, so adding a tool is
        /// just dropping the file in: &lt;工具箱目录&gt;\bin-tools\Xxx.exe. The name is deliberately not
        /// "tools" -- the repository's tools\ folder holds the button manifests, and the packaged
        /// folder puts those JSON files at the top level, so a second "tools" would be confusing.</summary>
        public const string PayloadDirName = "bin-tools";

        /// <summary>Tool folder next to the exe (the one the user is meant to drop files into).</summary>
        public static string PayloadDir { get { return Path.Combine(ExeDir, PayloadDirName); } }

        /// <summary>Per user tool folder: the place a future embedded payload would be extracted to,
        /// used as a fall back when the folder next to the exe is not writable (Program Files, USB).</summary>
        public static string UserPayloadDir { get { return Path.Combine(BaseDir, PayloadDirName); } }

        /// <summary>%LOCALAPPDATA%\PermanentDelete -- where the sibling project keeps its logs.</summary>
        public static string PermdelDir { get { return Path.Combine(LocalAppData, "PermanentDelete"); } }
        public static string PermdelEngineLog { get { return Path.Combine(PermdelDir, "delete.log"); } }
        public static string PermdelSetupLog { get { return Path.Combine(PermdelDir, "setup.log"); } }

        public static string ExePath
        {
            get
            {
                try
                {
                    Assembly a = Assembly.GetEntryAssembly();
                    if (a != null && a.Location.Length > 0) { return a.Location; }
                }
                catch { }
                return Path.Combine(Environment.CurrentDirectory, "Mxx1Toolbox.exe");
            }
        }

        public static string ExeDir { get { return Path.GetDirectoryName(ExePath); } }
        public static string IconsDir { get { return Path.Combine(ExeDir, "assets\\icons"); } }

        public static void EnsureBase()
        {
            try { Directory.CreateDirectory(BaseDir); }
            catch { }
        }

        public static void EnsureLogDir()
        {
            try { Directory.CreateDirectory(LogDir); }
            catch { }
        }

        /// <summary>Expands %ProgramFiles% / %LOCALAPPDATA% / %USERPROFILE% style placeholders.</summary>
        public static string Expand(string value)
        {
            if (string.IsNullOrEmpty(value)) { return ""; }
            try { return Environment.ExpandEnvironmentVariables(value); }
            catch { return value; }
        }

        /// <summary>Expands placeholders AND resolves a relative path against the toolbox folder,
        /// so a manifest can simply say "PermanentDeleteSetup.exe" for a file that sits in
        /// bin-tools\. A relative path is looked for next to the exe first and in bin-tools\ second
        /// (a plain relative path used to resolve against the process working directory, which is
        /// not the toolbox folder when the user starts it from somewhere else).</summary>
        public static string Resolve(string value)
        {
            string p = Expand(value);
            if (p.Length == 0) { return p; }
            try
            {
                if (Path.IsPathRooted(p)) { return p; }
            }
            catch { return p; }
            // A URI style target (ms-settings: / shell: / https: / mailto:) must be handed to
            // Windows untouched -- joining it to the toolbox folder produced a nonsense path like
            // <exe dir>\ms-settings:storagesense and "打开失败：系统找不到指定的文件"
            // (a live report from the user, 2026-10-04).
            int colon = p.IndexOf(':');
            if (colon > 0 && p.IndexOf('\\') < 0 && p.IndexOf('/') < 0) { return p; }

            string beside = Path.Combine(ExeDir, p);
            string inTools = Path.Combine(PayloadDir, p);
            try
            {
                if (File.Exists(inTools) || Directory.Exists(inTools)) { return inTools; }
            }
            catch { }
            return beside;
        }

        /// <summary>Creates bin-tools next to the exe (falls back to the per user folder when the
        /// toolbox sits somewhere read only). Returns the folder it managed to create.
        /// A short 说明.txt is dropped in as well: an empty folder looks broken (the user asked
        /// "bin-tools 里面为什么是空的？" on 2026-10-04).</summary>
        public static string EnsurePayloadDir()
        {
            try
            {
                Directory.CreateDirectory(PayloadDir);
                WritePayloadNote(PayloadDir);
                return PayloadDir;
            }
            catch { }
            try
            {
                Directory.CreateDirectory(UserPayloadDir);
                WritePayloadNote(UserPayloadDir);
                return UserPayloadDir;
            }
            catch { }
            return PayloadDir;
        }

        /// <summary>Name of the note that explains what bin-tools is for.</summary>
        public const string PayloadNoteName = "说明.txt";

        /// <summary>Written once (never overwritten), so the user can edit or delete it freely.</summary>
        private static readonly string[] PayloadNoteLines = new string[]
        {
            "这个文件夹是给「外部工具」用的：把别的 exe / 脚本丢进来，按钮就能找到它们。",
            "现在是空的完全正常 —— 工具箱不依赖这里的东西。",
            "",
            "为什么空着也能用：「永久删除工具」按钮按这个顺序找 PermanentDeleteSetup.exe",
            "    设置里指定的路径 → 环境变量 MXX1_PERMDEL_EXE → 工具箱同目录 → 本目录 bin-tools\\",
            "    → 上一层相邻的 permanent-delete-menu\\bin\\ → %LOCALAPPDATA%\\PermanentDelete\\",
            "  开发机上后面那两条就能命中，所以这里不放东西照样能用。",
            "",
            "什么时候该往这里放东西：",
            "  1. 想让工具箱「自带」某个工具（把整个 bin 目录拷到别的机器也还能用）→ 把那个 exe 复制进来；",
            "  2. 按钮里写的是相对路径（kind: exe 的 path、open 的 target）→ 按「工具箱目录 → bin-tools\\」解析；",
            "  3. 放进来是按文件名匹配的，名字要和按钮期望的完全一致。",
            "",
            "注意：这里的东西优先于上面那些兜底路径 —— 隔壁工程重新编译过之后，",
            "记得把这里的旧副本一起换掉，否则用的还是旧版本。",
            "",
            "打包时（build.ps1 -Package）隔壁的安装器会复制进「发布包」里的 bin-tools\\，不是这一个。",
        };

        private static void WritePayloadNote(string dir)
        {
            try
            {
                string p = Path.Combine(dir, PayloadNoteName);
                if (File.Exists(p)) { return; }
                File.WriteAllText(p,
                    string.Join(Environment.NewLine, PayloadNoteLines) + Environment.NewLine,
                    new System.Text.UTF8Encoding(false));
            }
            catch { }
        }
    }
}
