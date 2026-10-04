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
    }
}
