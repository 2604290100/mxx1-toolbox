// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>Append-only run log. The user interface always shows the newest line FIRST.</summary>
    internal static class Logger
    {
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string CurrentFile()
        {
            return Path.Combine(AppPaths.LogDir, "toolbox-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
        }

        public static void Write(string toolName, string message)
        {
            try
            {
                AppPaths.EnsureLogDir();
                string name = string.IsNullOrEmpty(toolName) ? "-" : toolName;
                string line = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    + "  " + name + "  " + message + Environment.NewLine;
                lock (Gate)
                {
                    File.AppendAllText(CurrentFile(), line, Utf8NoBom);
                }
            }
            catch
            {
                // logging must never take the tool down
            }
        }

        /// <summary>Newest first, current day file.</summary>
        public static string[] TailNewest(int max)
        {
            return TailNewestOf(CurrentFile(), max);
        }

        /// <summary>Newest first. Reads with FileShare.ReadWrite: the writer may be appending
        /// right now, and an exclusive open would show an empty box.</summary>
        public static string[] TailNewestOf(string path, int max)
        {
            List<string> result = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { return result.ToArray(); }
                List<string> all = new List<string>();
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null) { all.Add(line); }
                }
                for (int i = all.Count - 1; i >= 0 && result.Count < max; i--)
                {
                    if (all[i].Trim().Length > 0) { result.Add(all[i]); }
                }
            }
            catch
            {
                // unreadable log must not break the window
            }
            return result.ToArray();
        }

        /// <summary>Deletes toolbox log files older than the configured retention.</summary>
        public static void PruneOld(int keepDays)
        {
            if (keepDays <= 0) { return; }
            try
            {
                if (!Directory.Exists(AppPaths.LogDir)) { return; }
                DateTime cutoff = DateTime.Now.AddDays(-keepDays);
                foreach (string f in Directory.GetFiles(AppPaths.LogDir, "toolbox-*.log"))
                {
                    try { if (File.GetLastWriteTime(f) < cutoff) { File.Delete(f); } }
                    catch { }
                }
            }
            catch { }
        }
    }
}
