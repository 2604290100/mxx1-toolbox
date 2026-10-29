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
    /// <summary>一个程序的「高 DPI 兼容」设置（2026-10-28 用户提的第 3 件事：高 DPI 兼容批量设置）。
    ///
    /// 用户报的现象：笔记本接了大屏 / 开了 125%～150% 缩放之后，有些程序界面糊成一团、
    /// 或者字小得看不清 —— 那是老程序没有声明自己"懂 DPI"，Windows 只好把它整张画面拉伸。
    ///
    /// 那个设置写在哪（右键 exe → 属性 → 兼容性 → 更改高 DPI 设置，就是它）：
    ///
    ///     HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers
    ///     值名 = exe 的完整路径，值 = `~ HIGHDPIAWARE`（让程序自己按 DPI 缩放）
    ///            `~ DPIUNAWARE`（按系统缩放拉伸）/ `~ GDIDPISCALING DPIUNAWARE` …
    ///
    /// 这一页做的事：把已经设过的列出来（连同 RUNASADMIN 这类别的兼容性标记一起念出来），
    /// 你再加几个 exe，选中点「设为高 DPI 感知」→ 写 `~ HIGHDPIAWARE`；随时能「还原」。
    ///
    /// 四条口径（同「此电脑多余图标」）：
    /// ① **只写 HKCU**（当前用户，不用管理员、不动别的用户）；
    /// ② 写前把**每个 exe 原来的整串标记**记进 TSV（记不下来就一个都不写）；
    /// ③ 写完逐个读回核对；**只替换 DPI 相关的三个词**，`RUNASADMIN` 这类别的标记原样保留；
    /// ④ 命令行 `dpiflag` 默认只读，写入口只在隔离根 `MXX1_DPIFLAG_ROOT` 设好时放行。
    ///
    /// 测试隔离：`MXX1_DPIFLAG_ROOT`（替换 Layers 键路径，指到 HKCU 下面即可）、`MXX1_DPIFLAG_TSV`。</summary>
    internal sealed class DpiFlagItem
    {
        public string Path = "";
        public string Data = "";
        public bool Present;
        public string Product = "";
        public bool JustAdded;      // 用户刚加进来、注册表里还没有

        /// <summary>DPI 相关的三个词（我们只动这三个）：HIGHDPIAWARE / DPIUNAWARE / GDIDPISCALING。</summary>
        public static readonly string[] DpiTokens = new string[]
        {
            "HIGHDPIAWARE", "DPIUNAWARE", "GDIDPISCALING"
        };

        public bool Has(string token)
        {
            foreach (string t in Tokens()) { if (string.Equals(t, token, StringComparison.OrdinalIgnoreCase)) { return true; } }
            return false;
        }

        public string[] Tokens()
        {
            string d = (Data ?? "").Replace("~", " ").Trim();
            return d.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>别的兼容性标记（RUNASADMIN / WIN7RTM / DISABLEDXMAXIMIZEDWINDOWEDMODE…）。</summary>
        public string OtherTokens()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string t in Tokens())
            {
                bool dpi = false;
                foreach (string d in DpiTokens) { if (string.Equals(t, d, StringComparison.OrdinalIgnoreCase)) { dpi = true; } }
                if (dpi) { continue; }
                if (sb.Length > 0) { sb.Append(' '); }
                sb.Append(t);
            }
            return sb.ToString();
        }

        public string StateText()
        {
            if (!Present && !JustAdded) { return "（还没设）"; }
            if (Has("HIGHDPIAWARE")) { return "高 DPI 感知（自己缩放）"; }
            if (Has("DPIUNAWARE")) { return "按系统缩放（会被拉伸）"; }
            if (Has("GDIDPISCALING")) { return "GDI 缩放"; }
            return Data.Trim().Length > 0 ? ("别的兼容性标记：" + Data.Trim()) : "（还没设）";
        }
    }

    internal sealed class DpiFlagReport
    {
        public bool Ok = true;
        public int Changed;
        public int Skipped;
        public int Failed;
        public string Message = "";
    }

    internal static class DpiFlag
    {
        private const string DefaultHive = "HKCU";
        private const string DefaultPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        private const string TsvHeader = "# mxx1-toolbox 高 DPI 兼容 原值记录（制表符分隔；路径做了 base64，别手改）";

        private static string _hive = DefaultHive;
        private static string _path = DefaultPath;
        private static bool _isolated;

        private static void LoadRoot()
        {
            _hive = DefaultHive;
            _path = DefaultPath;
            _isolated = false;
            string v = (Environment.GetEnvironmentVariable("MXX1_DPIFLAG_ROOT") ?? "").Trim().Trim('"').TrimEnd('\\');
            if (v.Length == 0) { return; }
            int i = v.IndexOf('\\');
            string h = (i < 0) ? v : v.Substring(0, i);
            _hive = ((h.Trim().ToUpperInvariant() == "HKLM" || h.Trim().ToUpperInvariant() == "HKEY_LOCAL_MACHINE")) ? "HKLM" : "HKCU";
            _path = (i < 0) ? "" : v.Substring(i + 1);
            _isolated = true;
        }

        private static RegistryKey HiveByName(string shortName)
        {
            return (shortName == "HKLM") ? Registry.LocalMachine : Registry.CurrentUser;
        }

        public static bool Isolated { get { LoadRoot(); return _isolated; } }
        public static bool WriteAllowed() { return Isolated; }

        public static string RootText()
        {
            LoadRoot();
            return _hive + "\\" + _path;
        }

        public static string RecordFile()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_DPIFLAG_TSV") ?? "").Trim();
            if (env.Length > 0) { return env; }
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } } catch { }
            return Path.Combine(dir, "dpi-flag-original.tsv");
        }

        // ------------------------------------------------------------------ 扫描（只读）

        public static List<DpiFlagItem> Scan()
        {
            LoadRoot();
            List<DpiFlagItem> list = new List<DpiFlagItem>();
            using (RegistryKey k = HiveByName(_hive).OpenSubKey(_path, false))
            {
                if (k == null) { return list; }
                foreach (string name in k.GetValueNames())
                {
                    if (name.Trim().Length == 0) { continue; }
                    string data = "";
                    try
                    {
                        object o = k.GetValue(name);
                        if (o is string) { data = (string)o; }
                        else if (o != null) { data = Convert.ToString(o, CultureInfo.InvariantCulture); }
                    }
                    catch { }
                    DpiFlagItem it = new DpiFlagItem();
                    it.Path = name;
                    it.Data = data;
                    it.Present = true;
                    it.Product = ProductOf(name);
                    list.Add(it);
                }
            }
            list.Sort(delegate (DpiFlagItem a, DpiFlagItem b)
            {
                bool ad = a.Has("HIGHDPIAWARE") || a.Has("DPIUNAWARE");
                bool bd = b.Has("HIGHDPIAWARE") || b.Has("DPIUNAWARE");
                if (ad != bd) { return ad ? -1 : 1; }
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>exe 的显示名（`FileVersionInfo.FileDescription` → 文件名，读不到就文件名）。</summary>
        public static string ProductOf(string exe)
        {
            try
            {
                if (!File.Exists(exe)) { return ""; }
                System.Diagnostics.FileVersionInfo vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrEmpty(vi.FileDescription)) { return vi.FileDescription.Trim(); }
                if (!string.IsNullOrEmpty(vi.ProductName)) { return vi.ProductName.Trim(); }
            }
            catch { }
            return "";
        }

        // ------------------------------------------------------------------ 设为高 DPI 感知（写）

        /// <summary>把一批程序设成 `~ HIGHDPIAWARE`（别的兼容性标记原样保留）。</summary>
        public static DpiFlagReport SetHighDpi(List<DpiFlagItem> items)
        {
            DpiFlagReport rep = new DpiFlagReport();
            LoadRoot();
            if (items == null || items.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有要设置的程序。";
                return rep;
            }

            // ① 先记原值（原本没有这条例也记下来，"还原"时才知道该删掉）
            List<string> lines = new List<string>();
            foreach (DpiFlagItem it in items)
            {
                string cur;
                bool exists = TryRead(it.Path, out cur);
                it.Data = exists ? cur : "";
                it.Present = exists;
                lines.Add(Now() + "\t" + B64(it.Path) + "\t" + (exists ? "1" : "0") + "\t"
                    + (exists ? B64(cur) : "-"));
            }
            if (lines.Count > 0)
            {
                try { AppendRecords(lines); }
                catch (Exception ex)
                {
                    rep.Ok = false;
                    rep.Message = "原值记不下来（" + RecordFile() + "）：" + ex.Message + " —— 为了能还原，这次一个都没改。";
                    rep.Failed = items.Count;
                    return rep;
                }
            }

            // ② 逐个写 + 读回核对
            StringBuilder bad = new StringBuilder();
            using (RegistryKey k = HiveByName(_hive).CreateSubKey(_path))
            {
                if (k == null)
                {
                    rep.Ok = false;
                    rep.Message = "打不开注册表键：" + RootText() + "（权限？）";
                    rep.Failed = items.Count;
                    return rep;
                }
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DpiFlagItem it in items)
                {
                    if (seen.Contains(it.Path)) { continue; }
                    seen.Add(it.Path);
                    if (it.Path.Trim().Length == 0 || !it.Path.TrimEnd().EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        rep.Skipped++;
                        bad.Append("　· ").Append(it.Path).AppendLine("（不是 .exe，跳过）");
                        continue;
                    }
                    string want = BuildHighDpi(it.Data);
                    try
                    {
                        k.SetValue(it.Path, want, RegistryValueKind.String);
                        string back;
                        if (!TryRead(it.Path, out back) || !string.Equals(back, want, StringComparison.Ordinal))
                        {
                            rep.Failed++;
                            bad.Append("　· ").Append(it.Path).AppendLine("（写进去读回来不一样）");
                        }
                        else { rep.Changed++; }
                    }
                    catch (Exception ex)
                    {
                        rep.Failed++;
                        bad.Append("　· ").Append(it.Path).Append("（写不进去：").Append(ex.Message).AppendLine("）");
                    }
                }
            }

            rep.Ok = rep.Failed == 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("设为高 DPI 感知 ").Append(rep.Changed.ToString(CultureInfo.InvariantCulture)).Append(" 个");
            if (rep.Skipped > 0) { sb.Append("，跳过 ").Append(rep.Skipped.ToString(CultureInfo.InvariantCulture)).Append(" 个"); }
            if (rep.Failed > 0) { sb.Append("，失败 ").Append(rep.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 个"); }
            sb.AppendLine("。每个都读回来核对过。");
            sb.AppendLine("⚠️ 关掉那个程序、重新打开才生效（它启动时读这一条）。");
            if (bad.Length > 0) { sb.AppendLine().Append(bad.ToString()); }
            sb.Append("原值记在：").Append(RecordFile());
            rep.Message = sb.ToString();
            return rep;
        }

        /// <summary>把一串兼容性标记改成"去掉 DPI 相关的三个词、加上 HIGHDPIAWARE"。
        /// 别的词（RUNASADMIN 等）一个不动 —— 那可能是用户自己为了别的目的设的。</summary>
        public static string BuildHighDpi(string current)
        {
            StringBuilder sb = new StringBuilder("~ HIGHDPIAWARE");
            DpiFlagItem tmp = new DpiFlagItem();
            tmp.Data = current;
            foreach (string t in tmp.Tokens())
            {
                bool dpi = false;
                foreach (string d in DpiFlagItem.DpiTokens) { if (string.Equals(t, d, StringComparison.OrdinalIgnoreCase)) { dpi = true; } }
                if (dpi) { continue; }
                sb.Append(' ').Append(t);
            }
            return sb.ToString();
        }

        public static DpiFlagReport Restore()
        {
            DpiFlagReport rep = new DpiFlagReport();
            LoadRoot();
            List<string[]> rows = ReadRecords();
            if (rows.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有待还原的记录（这台机器还没用这个窗口设过高 DPI 兼容）。";
                return rep;
            }
            List<string> keep = new List<string>();
            StringBuilder bad = new StringBuilder();
            using (RegistryKey k = HiveByName(_hive).OpenSubKey(_path, true))
            {
                foreach (string[] r in rows)
                {
                    if (r.Length < 4) { continue; }
                    string exe = From64(r[1]);
                    bool existed = (r[2] == "1");
                    string old = existed ? From64(r[3]) : "";
                    try
                    {
                        if (k == null) { throw new InvalidOperationException("打不开注册表键：" + RootText()); }
                        if (existed) { k.SetValue(exe, old, RegistryValueKind.String); }
                        else { k.DeleteValue(exe, false); }
                        string back;
                        bool now = TryRead(exe, out back);
                        bool ok = existed ? (now && string.Equals(back, old, StringComparison.Ordinal)) : !now;
                        if (ok) { rep.Changed++; }
                        else
                        {
                            rep.Failed++;
                            keep.Add(Join(r));
                            bad.Append("　· ").Append(exe).AppendLine("（写回后读数不对）");
                        }
                    }
                    catch (Exception ex)
                    {
                        rep.Failed++;
                        keep.Add(Join(r));
                        bad.Append("　· ").Append(exe).Append("（写不进去：").Append(ex.Message).AppendLine("）");
                    }
                }
            }
            WriteRecords(keep);
            rep.Ok = rep.Failed == 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("还原 ").Append(rep.Changed.ToString(CultureInfo.InvariantCulture)).Append(" 个");
            if (rep.Failed > 0) { sb.Append("，失败 ").Append(rep.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 个"); }
            sb.Append("。关掉那个程序重新打开才生效。");
            if (bad.Length > 0) { sb.AppendLine().Append(bad.ToString()); }
            rep.Message = sb.ToString();
            return rep;
        }

        private static bool TryRead(string exe, out string data)
        {
            data = "";
            LoadRoot();
            try
            {
                using (RegistryKey k = HiveByName(_hive).OpenSubKey(_path, false))
                {
                    if (k == null) { return false; }
                    object o = k.GetValue(exe);
                    if (o == null) { return false; }
                    data = (o is string) ? (string)o : Convert.ToString(o, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ 记录文件

        private static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }

        private static string From64(string s)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String((s ?? "").Trim())); }
            catch { return ""; }
        }

        private static string Join(string[] r) { return string.Join("\t", r); }

        private static void AppendRecords(List<string> lines)
        {
            string path = RecordFile();
            bool exists = File.Exists(path);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
            using (StreamWriter w = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                if (!exists) { w.WriteLine(TsvHeader); }
                foreach (string l in lines) { w.WriteLine(l); }
            }
        }

        private static void WriteRecords(List<string> lines)
        {
            string path = RecordFile();
            if (lines.Count == 0)
            {
                try { if (File.Exists(path)) { File.Delete(path); } } catch { }
                return;
            }
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
            using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine(TsvHeader);
                foreach (string l in lines) { w.WriteLine(l); }
            }
        }

        public static List<string[]> ReadRecords()
        {
            List<string[]> rows = new List<string[]>();
            string path = RecordFile();
            try
            {
                if (!File.Exists(path)) { return rows; }
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) { continue; }
                    string[] p = line.Split('\t');
                    if (p.Length >= 4) { rows.Add(p); }
                }
            }
            catch { }
            return rows;
        }

        public static int PendingRecords() { return ReadRecords().Count; }

        // ------------------------------------------------------------------ 报告

        public static string ReportText(List<DpiFlagItem> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("兼容性标记根：").AppendLine(RootText());
            sb.Append("共 ").Append(list.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个程序有兼容性标记：");
            foreach (DpiFlagItem it in list)
            {
                sb.Append("· ").Append(it.Path).Append("　").AppendLine(it.StateText());
            }
            sb.Append("pending=").Append(PendingRecords().ToString(CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("record=").AppendLine(RecordFile());
            sb.Append("（只读，没有改注册表；设为高 DPI 感知 / 还原只能在窗口里点）");
            return sb.ToString();
        }

        public static string JsonReport(List<DpiFlagItem> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"root\":\"").Append(JsonText(RootText())).Append("\",\"pending\":")
                .Append(PendingRecords().ToString(CultureInfo.InvariantCulture)).Append(",\"items\":[");
            for (int i = 0; i < list.Count; i++)
            {
                DpiFlagItem it = list[i];
                if (i > 0) { sb.Append(","); }
                sb.Append("{\"path\":\"").Append(JsonText(it.Path)).Append("\",\"data\":\"")
                    .Append(JsonText(it.Data)).Append("\",\"highdpi\":")
                    .Append(it.Has("HIGHDPIAWARE") ? "true" : "false").Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static string JsonText(string s)
        {
            if (s == null) { return ""; }
            StringBuilder sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) { sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                        else { sb.Append(c); }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
