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
        /// toolbox sits somewhere read only). Returns the folder it managed to create.</summary>
        public static string EnsurePayloadDir()
        {
            try
            {
                Directory.CreateDirectory(PayloadDir);
                return PayloadDir;
            }
            catch { }
            try
            {
                Directory.CreateDirectory(UserPayloadDir);
                return UserPayloadDir;
            }
            catch { }
            return PayloadDir;
        }
    }
}
