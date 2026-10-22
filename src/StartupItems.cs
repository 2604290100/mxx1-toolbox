// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Mxx1Toolbox
{
    /// <summary>一个开机自启项（注册表 Run 键里的一条值，或者「启动」文件夹里的一个文件 / 快捷方式）。</summary>
    internal sealed class StartupItem
    {
        public string Kind = "reg";     // reg / file
        public string Name = "";
        public string Command = "";
        public string Source = "";      // 给人看的位置（「当前用户 · 注册表 Run」这种）
        public string Hive = "";        // HKEY_CURRENT_USER / HKEY_LOCAL_MACHINE
        public string KeyPath = "";     // SOFTWARE\…\Run
        public string ValueName = "";   // 注册表值名 / 文件名
        public string OriginalPath = ""; // 文件项的原路径
        public bool Enabled = true;
        public bool Critical;           // 系统自带组件：列出来但这一版不给禁
        public string Note = "";        // 说明（为什么不能禁 / 已禁用可恢复）
    }

    /// <summary>「启动项管理」的后端（GAP-05）。
    ///
    /// 三条口径：
    /// ① **只禁用、不删除**：注册表项 = 把原值（名字 / 类型 / 数据）记进
    ///    `%LOCALAPPDATA%\mxx1-toolbox\startup-disabled.tsv` 再删值；「启动」文件夹里的
    ///    文件 / 快捷方式 = **挪到备份目录**（`…\startup-disabled\`）。两种情况都能一键还原；
    /// ② **系统自带组件标灰、不给禁**（命令落在 Windows\System32 / SysWOW64 里、或者名字是
    ///    Defender / SecurityHealth 那几个）—— 禁了轻则没声音没网络，重则开不了机；
    /// ③ 记不下原值（写 TSV 失败）就**一个字都不改**，绝不出现"禁了但还原不回来"。
    ///
    /// ⚠️ 测试隔离：`MXX1_STARTUP_ROOT`（只看这一个键，比如 `HKCU\Software\mxx1-toolbox\startup-test`）、
    /// `MXX1_STARTUP_FOLDER`（只看这一个文件夹）、`MXX1_STARTUP_TSV`（记录文件挪走）——
    /// 界面回归靠它们端到端验「禁用 → 值真的没了 → 还原 → 值原样回来」，不碰用户真实自启项。</summary>
    internal static class StartupItems
    {
        public static string LastError = "";

        private const string RunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string RunWowPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

        // ------------------------------------------------------------------ 路径

        public static string RecordFile()
        {
            string env = Environment.GetEnvironmentVariable("MXX1_STARTUP_TSV");
            if (!string.IsNullOrEmpty(env)) { return env; }
            return Path.Combine(DataDir(), "startup-disabled.tsv");
        }

        public static string BackupDir()
        {
            string env = Environment.GetEnvironmentVariable("MXX1_STARTUP_TSV");
            if (!string.IsNullOrEmpty(env))
            {
                try { return Path.Combine(Path.GetDirectoryName(env), "startup-disabled"); }
                catch { }
            }
            return Path.Combine(DataDir(), "startup-disabled");
        }

        private static string DataDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } }
            catch { }
            return dir;
        }

        private static string IsolatedRoot()
        {
            return (Environment.GetEnvironmentVariable("MXX1_STARTUP_ROOT") ?? "").Trim();
        }

        private static string IsolatedFolder()
        {
            return (Environment.GetEnvironmentVariable("MXX1_STARTUP_FOLDER") ?? "").Trim();
        }

        /// <summary>写的那条路（禁用 / 还原）**只允许在 `MXX1_STARTUP_ROOT` 隔离下走**（命令行出口靠它拦）。</summary>
        public static bool WriteAllowed()
        {
            return IsolatedRoot().Length > 0;
        }

        // ------------------------------------------------------------------ 列

        /// <summary>列出全部自启项（含已经禁用的那些 —— 它们从记录文件里读回来，标「已禁用（可恢复）」）。</summary>
        public static List<StartupItem> List()
        {
            LastError = "";
            List<StartupItem> list = new List<StartupItem>();
            string iso = IsolatedRoot();
            if (iso.Length > 0)
            {
                string hive, keyPath;
                if (SplitHive(iso, out hive, out keyPath))
                {
                    ScanKey(hive, keyPath, "测试隔离 · 注册表", list);
                    // 隔离根一般给到"父键"（比如 …\startup-test），值在它下面的 Run 里 —— 这里两层都扫
                    string tail = keyPath.Substring(keyPath.LastIndexOf('\\') + 1);
                    if (!string.Equals(tail, "Run", StringComparison.OrdinalIgnoreCase))
                    {
                        ScanKey(hive, keyPath + "\\Run", "测试隔离 · 注册表", list);
                    }
                }
            }
            else
            {
                ScanKey("HKEY_CURRENT_USER", RunPath, "当前用户 · 注册表 Run", list);
                ScanKey("HKEY_LOCAL_MACHINE", RunPath, "所有用户 · 注册表 Run", list);
                ScanKey("HKEY_LOCAL_MACHINE", RunWowPath, "所有用户 · 注册表 Run（32 位）", list);
            }

            string isoFolder = IsolatedFolder();
            if (isoFolder.Length > 0)
            {
                ScanFolder(isoFolder, "测试隔离 · 启动文件夹", list);
            }
            else if (iso.Length == 0)
            {
                ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "当前用户 · 启动文件夹", list);
                ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "所有用户 · 启动文件夹", list);
            }

            // 已经禁用的：从记录里补回列表（输了之后用户还能在同一个窗口里还原）
            foreach (string[] rec in ReadRecords())
            {
                if (rec.Length < 7) { continue; }
                if (rec[1] == "reg")
                {
                    string hive = rec[2], keyPath = rec[3], valueName = rec[4];
                    bool dup = false;
                    foreach (StartupItem it in list)
                    {
                        if (it.Kind == "reg" && it.Hive == hive && it.KeyPath == keyPath && it.ValueName == valueName) { dup = true; break; }
                    }
                    if (dup) { continue; }
                    StartupItem d = new StartupItem();
                    d.Kind = "reg";
                    d.Name = valueName;
                    d.Command = rec[6];
                    d.Hive = hive;
                    d.KeyPath = keyPath;
                    d.ValueName = valueName;
                    d.Source = SourceOf(hive, keyPath);
                    d.Enabled = false;
                    d.Note = "已禁用（可恢复）· " + rec[0];
                    d.Critical = IsCritical(d.Name, d.Command);
                    list.Add(d);
                }
                else if (rec[1] == "file")
                {
                    StartupItem d = new StartupItem();
                    d.Kind = "file";
                    d.Name = rec[4];
                    d.OriginalPath = rec[2];
                    d.Command = rec[2];
                    d.Source = "启动文件夹 · " + SafeDirName(Path.GetDirectoryName(rec[2]));
                    d.Enabled = false;
                    d.Note = "已禁用（文件在备份目录里，可恢复）· " + rec[0];
                    list.Add(d);
                }
            }

            list.Sort(delegate(StartupItem x, StartupItem y)
            {
                if (x.Enabled != y.Enabled) { return x.Enabled ? -1 : 1; }
                int c = string.Compare(x.Source, y.Source, StringComparison.OrdinalIgnoreCase);
                if (c != 0) { return c; }
                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static string SafeDirName(string dir)
        {
            return string.IsNullOrEmpty(dir) ? "" : dir;
        }

        private static string SourceOf(string hive, string keyPath)
        {
            string who = (hive == "HKEY_CURRENT_USER") ? "当前用户" : "所有用户";
            if (keyPath.IndexOf("WOW6432Node", StringComparison.OrdinalIgnoreCase) >= 0) { return who + " · 注册表 Run（32 位）"; }
            return who + " · 注册表 Run";
        }

        private static bool SplitHive(string full, out string hive, out string keyPath)
        {
            hive = "HKEY_CURRENT_USER";
            keyPath = full;
            string s = full.Trim();
            if (s.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase)) { hive = "HKEY_CURRENT_USER"; keyPath = s.Substring(18); return true; }
            if (s.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase)) { hive = "HKEY_LOCAL_MACHINE"; keyPath = s.Substring(19); return true; }
            if (s.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)) { hive = "HKEY_CURRENT_USER"; keyPath = s.Substring(5); return true; }
            if (s.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)) { hive = "HKEY_LOCAL_MACHINE"; keyPath = s.Substring(5); return true; }
            return false;
        }

        private static void ScanKey(string hive, string keyPath, string source, List<StartupItem> list)
        {
            try
            {
                using (RegistryKey baseKey = OpenBase(hive))
                {
                    if (baseKey == null) { return; }
                    using (RegistryKey k = baseKey.OpenSubKey(keyPath, false))
                    {
                        if (k == null) { return; }
                        foreach (string name in k.GetValueNames())
                        {
                            if (string.IsNullOrEmpty(name)) { continue; }
                            object v = null;
                            string type = "REG_SZ";
                            try
                            {
                                v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                                type = k.GetValueKind(name).ToString();
                            }
                            catch { }
                            StartupItem it = new StartupItem();
                            it.Kind = "reg";
                            it.Name = name;
                            it.Command = (v == null) ? "" : v.ToString();
                            it.Hive = hive;
                            it.KeyPath = keyPath;
                            it.ValueName = name;
                            it.Source = source;
                            it.Enabled = true;
                            it.Critical = IsCritical(it.Name, it.Command);
                            it.Note = it.Critical ? "系统自带组件 —— 禁了可能导致没声音 / 没网 / 开不了机，这一版不给禁" : "";
                            list.Add(it);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = "读注册表失败：" + ex.Message;
            }
        }

        private static void ScanFolder(string folder, string source, List<StartupItem> list)
        {
            if (string.IsNullOrEmpty(folder)) { return; }
            try
            {
                if (!Directory.Exists(folder)) { return; }
                foreach (string f in Directory.GetFiles(folder))
                {
                    FileInfo fi = new FileInfo(f);
                    if ((fi.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) { continue; }
                    if (string.Equals(fi.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) { continue; }
                    StartupItem it = new StartupItem();
                    it.Kind = "file";
                    it.Name = fi.Name;
                    it.Command = fi.FullName;
                    it.OriginalPath = fi.FullName;
                    it.Source = source;
                    it.Enabled = true;
                    it.Critical = false;
                    list.Add(it);
                }
            }
            catch (Exception ex)
            {
                LastError = "读启动文件夹失败：" + ex.Message;
            }
        }

        /// <summary>系统自带组件（禁了容易出事）：命令落在 Windows 目录里，或者名字是那几个安全组件。</summary>
        public static bool IsCritical(string name, string command)
        {
            string n = (name ?? "").ToLowerInvariant();
            string c = (command ?? "").ToLowerInvariant();
            if (n.IndexOf("securityhealth") >= 0 || n.IndexOf("windowsdefender") >= 0 || n.IndexOf("defender") >= 0) { return true; }
            if (c.IndexOf("\\windows\\system32\\", StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            if (c.IndexOf("\\windows\\syswow64\\", StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            if (c.IndexOf("%systemroot%\\system32", StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            if (c.IndexOf("%windir%\\system32", StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            return false;
        }

        private static RegistryKey OpenBase(string hive)
        {
            if (hive == "HKEY_LOCAL_MACHINE") { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); }
            return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
        }

        // ------------------------------------------------------------------ 禁用

        /// <summary>禁用一项（只记原值 + 删值 / 挪文件；系统自带组件直接拒绝）。</summary>
        public static bool Disable(StartupItem item, out string message)
        {
            LastError = "";
            message = "";
            if (item == null) { message = "没选项目"; return false; }
            if (item.Critical) { message = "这一项是系统自带组件，这一版不给禁（禁了可能导致没声音 / 没网 / 开不了机）。"; return false; }

            if (item.Kind == "reg")
            {
                string type = "REG_SZ";
                string data = "";
                try
                {
                    using (RegistryKey baseKey = OpenBase(item.Hive))
                    {
                        if (baseKey == null) { message = "打不开注册表根：" + item.Hive; return false; }
                        using (RegistryKey k = baseKey.OpenSubKey(item.KeyPath, false))
                        {
                            if (k == null) { message = "这个键已经不在了：" + item.Hive + "\\" + item.KeyPath; return false; }
                            object v = k.GetValue(item.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                            if (v == null) { message = "这条值已经不在了：" + item.ValueName; return false; }
                            data = v.ToString();
                            type = k.GetValueKind(item.ValueName).ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    message = "读原值失败：" + ex.Message;
                    return false;
                }

                // 先记原值 —— 记不下就绝不动手
                if (!AppendRecord(new string[] { Now(), "reg", item.Hive, item.KeyPath, item.ValueName, type, data }))
                {
                    message = "记不下原值（记录文件写不进去），所以什么都没改。";
                    return false;
                }
                try
                {
                    using (RegistryKey baseKey = OpenBase(item.Hive))
                    using (RegistryKey k = baseKey.OpenSubKey(item.KeyPath, true))
                    {
                        if (k == null) { message = "打不开这个键（可能被 Windows 保护）：" + item.Hive + "\\" + item.KeyPath; return false; }
                        k.DeleteValue(item.ValueName, false);
                    }
                }
                catch (Exception ex)
                {
                    message = "删值失败：" + ex.Message;
                    RemoveRecord(item);
                    return false;
                }
                // 读回核对
                if (ValueExists(item.Hive, item.KeyPath, item.ValueName))
                {
                    message = "命令发出去了，但读回来这条值还在（可能被别的程序又写回去了）。";
                    return false;
                }
                message = "已禁用「" + item.Name + "」。原值已经记下来了（记录文件：startup-disabled.tsv），在同一个窗口里点「恢复选中的」就能还原。";
                return true;
            }

            // 启动文件夹里的文件 / 快捷方式：挪到备份目录
            string src = item.OriginalPath;
            if (string.IsNullOrEmpty(src) || !File.Exists(src)) { message = "这个文件已经不在了：" + src; return false; }
            string backupDir = BackupDir();
            string backupPath = Path.Combine(backupDir, NowStamp() + "_" + Path.GetFileName(src));
            try
            {
                if (!Directory.Exists(backupDir)) { Directory.CreateDirectory(backupDir); }
                File.Move(src, backupPath);
            }
            catch (Exception ex)
            {
                message = "挪文件失败：" + ex.Message;
                return false;
            }
            if (!AppendRecord(new string[] { Now(), "file", src, backupPath, Path.GetFileName(src), "", "" }))
            {
                // 记录写不进去 → 把文件挪回去，绝不留下"禁了但没记录"的状态
                try { File.Move(backupPath, src); } catch { }
                message = "记不下原位置（记录文件写不进去），文件已经挪回原处，什么都没改。";
                return false;
            }
            message = "已禁用「" + item.Name + "」：文件挪到了备份目录（" + backupPath + "），点「恢复选中的」就能挪回去。";
            return true;
        }

        // ------------------------------------------------------------------ 还原

        /// <summary>把一项还原回去（注册表值写回 / 文件挪回原处）。</summary>
        public static bool Restore(StartupItem item, out string message)
        {
            LastError = "";
            message = "";
            if (item == null) { message = "没选项目"; return false; }
            if (item.Enabled) { message = "「" + item.Name + "」现在是开着的，不用还原。"; return false; }

            foreach (string[] rec in ReadRecords())
            {
                if (rec.Length < 7) { continue; }
                bool match = false;
                if (item.Kind == "reg" && rec[1] == "reg")
                {
                    match = (rec[2] == item.Hive && rec[3] == item.KeyPath && rec[4] == item.ValueName);
                }
                else if (item.Kind == "file" && rec[1] == "file")
                {
                    match = (string.Equals(rec[2], item.OriginalPath, StringComparison.OrdinalIgnoreCase));
                }
                if (!match) { continue; }

                if (rec[1] == "reg")
                {
                    try
                    {
                        RegistryValueKind kind = RegistryValueKind.String;
                        try { kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), rec[5], true); }
                        catch { }
                        using (RegistryKey baseKey = OpenBase(rec[2]))
                        {
                            if (baseKey == null) { message = "打不开注册表根：" + rec[2]; return false; }
                            using (RegistryKey k = baseKey.CreateSubKey(rec[3]))
                            {
                                if (k == null) { message = "建不了 / 打不开这个键：" + rec[3]; return false; }
                                k.SetValue(rec[4], rec[6], kind);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        message = "写回原值失败：" + ex.Message;
                        return false;
                    }
                    if (!ValueExists(rec[2], rec[3], rec[4]))
                    {
                        message = "写回去了，但读回来没看到这条值（可能被权限挡住了）。";
                        return false;
                    }
                    RemoveRecord(item);
                    message = "已还原「" + item.Name + "」，下次开机会照原样启动。";
                    return true;
                }

                // 文件项：从备份目录挪回原位置
                string original = rec[2];
                string backup = rec[3];
                if (!File.Exists(backup)) { message = "备份的那个文件已经不在了：" + backup; return false; }
                if (File.Exists(original)) { message = "原位置已经有一个同名文件了（" + original + "），没有覆盖它。"; return false; }
                try
                {
                    string dir = Path.GetDirectoryName(original);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
                    File.Move(backup, original);
                }
                catch (Exception ex)
                {
                    message = "挪回原处失败：" + ex.Message;
                    return false;
                }
                RemoveRecord(item);
                message = "已还原「" + item.Name + "」，文件挪回了启动文件夹。";
                return true;
            }
            message = "记录里找不到这一项（记录文件可能被删了）。";
            return false;
        }

        private static bool ValueExists(string hive, string keyPath, string valueName)
        {
            try
            {
                using (RegistryKey baseKey = OpenBase(hive))
                {
                    if (baseKey == null) { return false; }
                    using (RegistryKey k = baseKey.OpenSubKey(keyPath, false))
                    {
                        if (k == null) { return false; }
                        foreach (string n in k.GetValueNames())
                        {
                            if (string.Equals(n, valueName, StringComparison.OrdinalIgnoreCase)) { return true; }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        // ------------------------------------------------------------------ 记录文件

        public static List<string[]> ReadRecords()
        {
            List<string[]> rows = new List<string[]>();
            string path = RecordFile();
            try
            {
                if (!File.Exists(path)) { return rows; }
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrEmpty(line)) { continue; }
                    string[] parts = line.Split('\t');
                    if (parts.Length >= 7) { rows.Add(parts); }
                }
            }
            catch (Exception ex)
            {
                LastError = "读记录文件失败：" + ex.Message;
            }
            return rows;
        }

        private static bool AppendRecord(string[] cols)
        {
            string path = RecordFile();
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < cols.Length; i++)
                {
                    if (i > 0) { sb.Append('\t'); }
                    sb.Append(Clean(cols[i]));
                }
                sb.Append("\r\n");
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                LastError = "写记录文件失败：" + ex.Message;
                return false;
            }
        }

        private static void RemoveRecord(StartupItem item)
        {
            string path = RecordFile();
            try
            {
                if (!File.Exists(path)) { return; }
                List<string> keep = new List<string>();
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Length == 0) { continue; }
                    string[] p = line.Split('\t');
                    if (p.Length >= 7)
                    {
                        bool hit = false;
                        if (item.Kind == "reg" && p[1] == "reg")
                        {
                            hit = (p[2] == item.Hive && p[3] == item.KeyPath && p[4] == item.ValueName);
                        }
                        else if (item.Kind == "file" && p[1] == "file")
                        {
                            hit = string.Equals(p[2], item.OriginalPath, StringComparison.OrdinalIgnoreCase);
                        }
                        if (hit) { continue; }
                    }
                    keep.Add(line);
                }
                File.WriteAllLines(path, keep.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static string Clean(string s)
        {
            if (s == null) { return ""; }
            return s.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " ");
        }

        private static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string NowStamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        }
    }
}
