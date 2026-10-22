// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.IO;

namespace Mxx1Toolbox
{
    /// <summary>Turns something the user dropped on the window into a button draft.
    ///
    /// A shortcut (.lnk) is resolved to the program it points at: the user reported that dropping a
    /// desktop shortcut produced a button that failed to start, and asked for the button to point at
    /// the real target ("拖入快捷方式图标程序会失败修复一下（直接指向目标地址）"). The .lnk path used to
    /// be stored as-is, so the button broke as soon as the shortcut was moved or deleted.</summary>
    internal static class DroppedFile
    {
        public static bool IsShortcut(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsInternetShortcut(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Resolves a .lnk through ShellLink (IShellLinkW, Unicode — see src\ShellLinkFile.cs).
        /// Returns false when the shortcut cannot be read (then the caller keeps the .lnk itself).
        /// ⚠️ 以前走的是 WScript.Shell：那个是 ANSI 组件，英文 Windows 上中文名 / 中文目标的快捷方式
        /// 读不出来（2026-10-21 CI 抓到）；换 IShellLinkW 之后两种名字都能读。</summary>
        public static bool ResolveShortcut(string lnk, out string target, out string args, out string workDir)
        {
            return ShellLinkFile.Read(lnk, out target, out args, out workDir) == ShellLinkFile.ReadResult.Ok;
        }

        /// <summary>The URL= line of a .url (internet shortcut), or "" when there is none.</summary>
        public static string ReadInternetShortcut(string urlFile)
        {
            try
            {
                foreach (string line in File.ReadAllLines(urlFile))
                {
                    string s = line.Trim();
                    if (s.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        return s.Substring(4).Trim();
                    }
                }
            }
            catch { }
            return "";
        }

        /// <summary>Builds the draft. A dropped .lnk keeps the shortcut's own name (that is what the
        /// user sees on the desktop) but points at the target program.</summary>
        public static ToolItem Draft(string path)
        {
            if (string.IsNullOrEmpty(path)) { return null; }
            ToolItem t = new ToolItem();
            t.UserLayer = true;
            t.Source = "tools.json（用户）";
            t.Tab = Tabs.Mine;

            string shown = path;             // what the button is called after
            string target = path;
            string args = "";
            string workDir = "";
            bool resolved = false;
            try
            {
                if (IsShortcut(path) && ResolveShortcut(path, out target, out args, out workDir))
                {
                    resolved = true;
                }
                else
                {
                    target = path;
                    args = "";
                    workDir = "";
                }

                if (IsInternetShortcut(target))
                {
                    string url = ReadInternetShortcut(target);
                    t.Kind = "open";
                    t.Target = url.Length > 0 ? url : target;
                    t.Name = BaseName(shown);
                    t.Hint = "拖进来的网址快捷方式";
                }
                else if (Directory.Exists(target))
                {
                    t.Kind = "open";
                    t.Target = target;
                    t.Name = BaseName(shown);
                }
                else
                {
                    string ext = Extension(target);
                    t.Name = BaseName(shown);
                    if (ext == ".bat" || ext == ".cmd") { t.Kind = "script"; t.Path = target; t.Shell = "cmd"; }
                    else if (ext == ".ps1" || ext == ".vbs") { t.Kind = "script"; t.Path = target; t.Shell = "powershell"; }
                    else if (ext == ".exe" || ext == ".com")
                    {
                        t.Kind = "exe";
                        t.Path = target;
                        t.Args = args;
                        t.WorkDir = workDir;
                    }
                    else if (!resolved && IsShortcut(path))
                    {
                        // A .lnk the shell could not resolve (it may point at a shell folder such as
                        // 「此电脑」): keep the shortcut and let Windows open it like a double click.
                        t.Kind = "open";
                        t.Target = path;
                        t.Hint = "快捷方式（由系统打开）";
                    }
                    else
                    {
                        // Anything else is a document: open it like a double click.
                        t.Kind = "open";
                        t.Target = target;
                    }
                }
            }
            catch { return null; }

            if (t.Name.Length > 24) { t.Name = t.Name.Substring(0, 24); }
            if (resolved && t.Hint.Length == 0) { t.Hint = "快捷方式指向：" + target; }
            return t;
        }

        private static string BaseName(string path)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.Length == 0) { name = Path.GetFileName(path); }
                return name;
            }
            catch { return "新按钮"; }
        }

        private static string Extension(string path)
        {
            try { return Path.GetExtension(path).ToLowerInvariant(); }
            catch { return ""; }
        }
    }
}
