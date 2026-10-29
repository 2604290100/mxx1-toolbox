// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Mxx1Toolbox
{
    /// <summary>一条"会不会自己跑起来"的东西（2026-10-28 用户提的第 5 件事：计划任务 + 服务扫描）。
    ///
    /// 为什么要有这一页：「启动项管理」只管注册表的 `Run` 键和启动文件夹；可**真正**让软件
    /// 开机就活过来的还有两条路 —— **计划任务**（开机 / 登录 / 定时触发，很多国产软件走这条，
    /// 因为它还能"提权"跑）和**系统服务**（`Start=2` 自动启动）。用户报「明明关了自启，它还是
    /// 自己跑起来」多半就在这两处。
    ///
    /// ## 计划任务怎么读（量出来的）
    ///
    /// 每个任务在磁盘上是 `%SystemRoot%\System32\Tasks\<路径>` 一个 **XML 文件**
    /// （`encoding="UTF-16"`，带 BOM）。里面有 `<URI>\Microsoft\Windows\Foo</URI>`、触发条件
    /// （`<BootTrigger>` / `<LogonTrigger>` / `<CalendarTrigger>` / `<RegistrationTrigger>`）、
    /// 动作（`<Exec><Command>` + `<Arguments>`）、作者，以及**禁用时**才出现的
    /// `<Enabled>false</Enabled>`。全是英文标签 → 中文 / 英文系统都能读，不用解析本地化的命令输出。
    ///
    /// 写：`schtasks /change /tn "<名字>" /disable|/enable`。**实测（2026-10-28）**：
    /// 禁用之后那个 XML 里真的出现 `<Enabled>false</Enabled>`，再启用它又消失 —— 所以
    /// "写完读回核对"用的是同一个 XML（不是靠命令的退出码）。
    ///
    /// ## 服务怎么读
    ///
    /// `HKLM\SYSTEM\CurrentControlSet\Services\<名字>` 的 `Start`：2=自动、3=手动、4=禁用、
    /// 0/1=开机/系统驱动。`Type` < 0x10 是驱动（只报告，不动）。
    ///
    /// ## 底线（同「启动项管理」：**只禁用不删除**）
    ///
    /// ① **系统自带的一律只报告**：任务 URI 以 `\Microsoft\` 开头的、服务 ImagePath 落在
    ///    `%SystemRoot%` 下的、驱动 —— 选中了也只会告诉你"不给动"；
    /// ② 禁用前记原值（任务：原来是禁用还是启用；服务：原来的 `Start`），随时能还原；
    /// ③ 写完读回核对；
    /// ④ 命令行 `autoextra` 默认只读；写入口只在隔离根设好时放行
    ///    （`MXX1_AUTOEXTRA_TASKS` = 任务目录、`MXX1_AUTOEXTRA_SERVICES` = 服务注册表根）。
    ///
    /// 测试隔离：`MXX1_AUTOEXTRA_TASKS`（替换 `System32\Tasks`）、`MXX1_AUTOEXTRA_SERVICES`
    /// （替换 `HKLM\SYSTEM\CurrentControlSet\Services`）、`MXX1_AUTOEXTRA_TSV`（记录文件）、
    /// `MXX1_SCHTASKS`（假 schtasks 程序 —— 测试里它同时改夹具 XML，好让读回核对真的走得通）。</summary>
    internal sealed class AutoExtraItem
    {
        public string Kind = "task";     // task / service
        public string Id = "";           // 任务名（`\Foo` 或 `\Microsoft\Windows\Foo`）/ 服务名
        public string Name = "";
        public string Command = "";
        public string Author = "";
        public string Trigger = "";
        public string Where = "";
        public bool IsSystem;
        public bool IsDriver;
        public bool Disabled;
        public int StartValue = -1;      // 服务专用
        public int TypeValue = -1;

        public bool CanChange { get { return !IsSystem && !IsDriver; } }

        public string KindText()
        {
            if (Kind == "service") { return IsDriver ? "驱动" : "服务"; }
            return "计划任务";
        }

        public string StateText()
        {
            if (Disabled) { return Kind == "service" ? "已禁用（Start=4）" : "已禁用"; }
            if (Kind == "service")
            {
                switch (StartValue)
                {
                    case 0: return "开机驱动";
                    case 1: return "系统驱动";
                    case 2: return "自动启动";
                    case 3: return "手动";
                    default: return "Start=" + StartValue.ToString(CultureInfo.InvariantCulture);
                }
            }
            return "启用中";
        }
    }

    internal sealed class AutoExtraReport
    {
        public bool Ok = true;
        public int Changed;
        public int Skipped;
        public int Failed;
        public bool NeedAdmin;
        public string Message = "";
    }

    internal static class AutoExtra
    {
        private const string TsvHeader = "# mxx1-toolbox 计划任务 / 服务 自启 原值记录（制表符分隔；名字做了 base64，别手改）";
        private const string DefaultServicesHive = "HKLM";
        private const string DefaultServicesPath = @"SYSTEM\CurrentControlSet\Services";

        // ------------------------------------------------------------------ 根

        public static string TasksRoot()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_AUTOEXTRA_TASKS") ?? "").Trim().Trim('"');
            if (env.Length > 0) { return env; }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "Tasks");
        }

        public static bool TaskWriteAllowed()
        {
            return (Environment.GetEnvironmentVariable("MXX1_AUTOEXTRA_TASKS") ?? "").Trim().Length > 0;
        }

        public static void ServicesRoot(out string hive, out string path)
        {
            hive = DefaultServicesHive;
            path = DefaultServicesPath;
            string env = (Environment.GetEnvironmentVariable("MXX1_AUTOEXTRA_SERVICES") ?? "").Trim().Trim('"').TrimEnd('\\');
            if (env.Length == 0) { return; }
            int i = env.IndexOf('\\');
            string h = (i < 0) ? env : env.Substring(0, i);
            h = h.Trim().ToUpperInvariant();
            hive = (h == "HKCU" || h == "HKEY_CURRENT_USER") ? "HKCU" : "HKLM";
            path = (i < 0) ? "" : env.Substring(i + 1);
        }

        public static string ServicesRootText()
        {
            string hive, path;
            ServicesRoot(out hive, out path);
            return hive + "\\" + path;
        }

        public static bool ServicesWriteAllowed()
        {
            return (Environment.GetEnvironmentVariable("MXX1_AUTOEXTRA_SERVICES") ?? "").Trim().Length > 0;
        }

        public static string RecordFile()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_AUTOEXTRA_TSV") ?? "").Trim();
            if (env.Length > 0) { return env; }
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } } catch { }
            return Path.Combine(dir, "autoextra-original.tsv");
        }

        public static string SchtasksExe()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_SCHTASKS") ?? "").Trim().Trim('"');
            if (env.Length > 0) { return env; }
            return "schtasks.exe";
        }

        // ------------------------------------------------------------------ 扫描（只读）

        public static List<AutoExtraItem> Scan()
        {
            List<AutoExtraItem> list = new List<AutoExtraItem>();
            list.AddRange(ScanTasks());
            list.AddRange(ScanServices());
            list.Sort(delegate (AutoExtraItem a, AutoExtraItem b)
            {
                // 可动的排前面（第三方 / 启动类型的先看），再按种类 + 名字
                if (a.CanChange != b.CanChange) { return a.CanChange ? -1 : 1; }
                if (a.Kind != b.Kind) { return string.Compare(a.Kind, b.Kind, StringComparison.Ordinal); }
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static List<AutoExtraItem> ScanTasks()
        {
            List<AutoExtraItem> list = new List<AutoExtraItem>();
            string root = TasksRoot();
            if (!Directory.Exists(root)) { return list; }
            string[] files;
            try { files = Directory.GetFiles(root, "*", SearchOption.AllDirectories); }
            catch { return list; }
            foreach (string f in files)
            {
                AutoExtraItem it = ParseTask(f, root);
                if (it != null) { list.Add(it); }
            }
            return list;
        }

        /// <summary>读一个任务 XML。读不出来就当没有（任务目录里也有非 XML 的杂物）。</summary>
        public static AutoExtraItem ParseTask(string file, string root)
        {
            string text;
            try
            {
                // ⚠️ 任务 XML 是 UTF-16 + BOM；StreamReader 带 detectEncodingFromByteOrderMarks
                // 才能同时吃 UTF-16 和 UTF-8（**不能用 File.ReadAllText 猜**）。
                using (StreamReader r = new StreamReader(file, Encoding.UTF8, true))
                {
                    text = r.ReadToEnd();
                }
            }
            catch { return null; }
            if (text.IndexOf("<Task", StringComparison.OrdinalIgnoreCase) < 0) { return null; }

            AutoExtraItem it = new AutoExtraItem();
            it.Kind = "task";
            string uri = First(text, "<URI>");
            if (uri.Length == 0)
            {
                string rel = file.Substring(root.TrimEnd('\\').Length).TrimStart('\\');
                uri = "\\" + rel.Replace('\\', '\\');
            }
            if (!uri.StartsWith("\\", StringComparison.Ordinal)) { uri = "\\" + uri; }
            it.Id = uri;
            int cut = uri.LastIndexOf('\\');
            it.Name = (cut >= 0 && cut < uri.Length - 1) ? uri.Substring(cut + 1) : uri.TrimStart('\\');
            it.Where = file;
            it.IsSystem = uri.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);
            it.Disabled = Regex.IsMatch(text, @"<Enabled>\s*false\s*</Enabled>", RegexOptions.IgnoreCase);
            it.Author = First(text, "<Author>");
            string cmd = First(text, "<Command>");
            string args = First(text, "<Arguments>");
            it.Command = (cmd + (args.Length > 0 ? " " + args : "")).Trim();

            StringBuilder trig = new StringBuilder();
            if (text.IndexOf("<BootTrigger", StringComparison.OrdinalIgnoreCase) >= 0) { trig.Append("开机 "); }
            if (text.IndexOf("<LogonTrigger", StringComparison.OrdinalIgnoreCase) >= 0) { trig.Append("登录 "); }
            if (text.IndexOf("<RegistrationTrigger", StringComparison.OrdinalIgnoreCase) >= 0) { trig.Append("注册后 "); }
            if (text.IndexOf("<CalendarTrigger", StringComparison.OrdinalIgnoreCase) >= 0) { trig.Append("定时 "); }
            if (text.IndexOf("<IdleTrigger", StringComparison.OrdinalIgnoreCase) >= 0) { trig.Append("空闲 "); }
            it.Trigger = trig.Length > 0 ? trig.ToString().Trim() : "（没有触发条件 / 只能手动跑）";
            return it;
        }

        private static string First(string xml, string tag)
        {
            try
            {
                Match m = Regex.Match(xml, Regex.Escape(tag) + @"([^<]*)</" + Regex.Escape(tag.Substring(1)),
                    RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.Trim() : "";
            }
            catch { return ""; }
        }

        public static List<AutoExtraItem> ScanServices()
        {
            List<AutoExtraItem> list = new List<AutoExtraItem>();
            string hive, path;
            ServicesRoot(out hive, out path);
            RegistryKey baseKey = (hive == "HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
            using (RegistryKey k = baseKey.OpenSubKey(path, false))
            {
                if (k == null) { return list; }
                string[] names;
                try { names = k.GetSubKeyNames(); }
                catch { return list; }
                string win = "";
                try { win = Environment.GetFolderPath(Environment.SpecialFolder.Windows); } catch { }
                foreach (string name in names)
                {
                    using (RegistryKey sk = k.OpenSubKey(name, false))
                    {
                        if (sk == null) { continue; }
                        object st = null, ty = null;
                        try { st = sk.GetValue("Start"); ty = sk.GetValue("Type"); } catch { }
                        if (!(st is int)) { continue; }
                        int start = (int)st;
                        if (start < 0 || start > 4) { continue; }
                        int type = (ty is int) ? (int)ty : 0x10;
                        string img = "";
                        try
                        {
                            object o = sk.GetValue("ImagePath");
                            if (o != null) { img = Convert.ToString(o, CultureInfo.InvariantCulture).Trim(); }
                        }
                        catch { }
                        string disp = "";
                        try
                        {
                            object o = sk.GetValue("DisplayName");
                            if (o != null) { disp = Convert.ToString(o, CultureInfo.InvariantCulture).Trim(); }
                        }
                        catch { }
                        if (disp.StartsWith("@", StringComparison.Ordinal))
                        {
                            int semi = disp.LastIndexOf(';');
                            if (semi >= 0 && semi < disp.Length - 1) { disp = disp.Substring(semi + 1); }
                        }

                        AutoExtraItem it = new AutoExtraItem();
                        it.Kind = "service";
                        it.Id = name;
                        it.Name = disp.Length > 0 ? disp : name;
                        it.Command = img;
                        it.StartValue = start;
                        it.TypeValue = type;
                        it.IsDriver = (type > 0 && type < 0x10) || start <= 1;
                        it.Disabled = (start == 4);
                        it.Where = ServicesRootText() + "\\" + name;
                        // ⚠️ 读不到 ImagePath 的一律按系统自带处理（只报告、不给动）：判不出是谁的
                        // 程序就不该替用户禁用。这台精简版 Windows 上实测有一批只剩一个 `Start`
                        // 的空壳键（BDESVC / AxInstSV / Fax / BthHFSrv …），原口径会把它们当成
                        // "第三方"亮出禁用按钮 —— 那正是会把系统搞坏的那一类。
                        it.IsSystem = it.IsDriver || img.Length == 0 || IsSystemImage(img, win);
                        list.Add(it);
                    }
                }
            }
            return list;
        }

        /// <summary>ImagePath 落在 %SystemRoot% 里的 = Windows 自带（只报告）。
        /// 展开环境变量再看；`\SystemRoot\…` / `system32\…` 这种写法也认。</summary>
        private static bool IsSystemImage(string imagePath, string win)
        {
            if (string.IsNullOrEmpty(imagePath)) { return false; }
            string p;
            try { p = Environment.ExpandEnvironmentVariables(imagePath).Trim(); }
            catch { p = imagePath.Trim(); }
            p = Unquote(p);
            if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (p.StartsWith(@"\??\", StringComparison.Ordinal)) { p = p.Substring(4); }
            if (p.StartsWith("system32\\", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (win.Length > 0 && p.StartsWith(win, StringComparison.OrdinalIgnoreCase)) { return true; }
            return false;
        }

        private static string Unquote(string s)
        {
            string t = (s ?? "").Trim();
            if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"') { t = t.Substring(1, t.Length - 2); }
            return t;
        }

        // ------------------------------------------------------------------ 禁用（写）

        /// <summary>禁用一批（只禁用的第三方：系统自带 / 驱动会跳过并说明）。</summary>
        public static AutoExtraReport Disable(List<AutoExtraItem> items)
        {
            AutoExtraReport rep = new AutoExtraReport();
            if (items == null || items.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有选中要禁用的项。";
                return rep;
            }

            // ① 先记原值
            List<string> lines = new List<string>();
            List<AutoExtraItem> todo = new List<AutoExtraItem>();
            StringBuilder note = new StringBuilder();
            foreach (AutoExtraItem it in items)
            {
                if (!it.CanChange)
                {
                    rep.Skipped++;
                    note.Append("　· ").Append(it.Name).AppendLine(it.Kind == "service"
                        ? "（系统自带 / 驱动，不给禁用）" : "（系统自带的任务，不给禁用）");
                    continue;
                }
                if (it.Disabled)
                {
                    rep.Skipped++;
                    note.Append("　· ").Append(it.Name).AppendLine("（已经是禁用状态）");
                    continue;
                }
                todo.Add(it);
                string extra = (it.Kind == "service")
                    ? it.StartValue.ToString(CultureInfo.InvariantCulture)
                    : "enabled";
                lines.Add(Now() + "\t" + it.Kind + "\t" + B64(it.Id) + "\t" + extra);
            }
            if (lines.Count > 0)
            {
                try { AppendRecords(lines); }
                catch (Exception ex)
                {
                    rep.Ok = false;
                    rep.Message = "原值记不下来（" + RecordFile() + "）：" + ex.Message
                        + " —— 为了能还原，这次一个都没动。";
                    rep.Failed = items.Count;
                    return rep;
                }
            }

            // ② 逐个禁用 + 读回核对
            StringBuilder bad = new StringBuilder();
            foreach (AutoExtraItem it in todo)
            {
                string err = (it.Kind == "service") ? DisableService(it) : DisableTask(it);
                if (err.Length == 0) { rep.Changed++; }
                else
                {
                    rep.Failed++;
                    if (err.IndexOf("拒绝", StringComparison.Ordinal) >= 0
                        || err.IndexOf("权限", StringComparison.Ordinal) >= 0
                        || err.IndexOf("5", StringComparison.Ordinal) >= 0) { rep.NeedAdmin = true; }
                    bad.Append("　· ").Append(it.Name).Append("（").Append(err).AppendLine("）");
                }
            }

            rep.Ok = rep.Failed == 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("禁用 ").Append(rep.Changed.ToString(CultureInfo.InvariantCulture)).Append(" 项");
            if (rep.Skipped > 0) { sb.Append("，跳过 ").Append(rep.Skipped.ToString(CultureInfo.InvariantCulture)).Append(" 项"); }
            if (rep.Failed > 0) { sb.Append("，失败 ").Append(rep.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 项"); }
            sb.AppendLine("。每一项都读回来核对过。");
            if (note.Length > 0) { sb.Append(note.ToString()); }
            if (bad.Length > 0) { sb.Append(bad.ToString()); }
            sb.Append("原值记在：").Append(RecordFile()).AppendLine("（点「还原成禁用前」可以恢复）");
            rep.Message = sb.ToString();
            return rep;
        }

        private static string DisableTask(AutoExtraItem it)
        {
            string output, err2;
            int code = RunSchtasks("/change /tn \"" + it.Id + "\" /disable", out output, out err2);
            if (code != 0)
            {
                return "schtasks 返回 " + code.ToString(CultureInfo.InvariantCulture) + "：" + OneLine(output + " " + err2);
            }
            // 读回：同一个 XML 里必须有 <Enabled>false</Enabled>
            AutoExtraItem now = ParseTask(it.Where, TasksRoot());
            if (now == null) { return "禁用后读不回来那个任务文件：" + it.Where; }
            return now.Disabled ? "" : "命令返回成功，但重读任务文件没看到「已禁用」（可能被组策略 / 别的工具接管了）";
        }

        private static string DisableService(AutoExtraItem it)
        {
            string hive, path;
            ServicesRoot(out hive, out path);
            RegistryKey baseKey = (hive == "HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
            try
            {
                using (RegistryKey sk = baseKey.OpenSubKey(path + "\\" + it.Id, true))
                {
                    if (sk == null) { return "打不开这个服务的注册表键（要管理员）"; }
                    sk.SetValue("Start", 4, RegistryValueKind.DWord);
                }
                int now;
                if (ReadServiceStart(it.Id, out now) && now == 4) { return ""; }
                return "写完读回来还不是 4";
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    return "权限不够（要管理员）：" + ex.Message;
                }
                return ex.Message;
            }
        }

        private static bool ReadServiceStart(string name, out int start)
        {
            start = -1;
            string hive, path;
            ServicesRoot(out hive, out path);
            RegistryKey baseKey = (hive == "HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
            try
            {
                using (RegistryKey sk = baseKey.OpenSubKey(path + "\\" + name, false))
                {
                    if (sk == null) { return false; }
                    object o = sk.GetValue("Start");
                    if (!(o is int)) { return false; }
                    start = (int)o;
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>跑 schtasks，收输出（30 秒超时）。不弹控制台窗口。</summary>
        private static int RunSchtasks(string args, out string stdout, out string stderr)
        {
            stdout = "";
            stderr = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(SchtasksExe(), args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.Default;
                psi.StandardErrorEncoding = Encoding.Default;
                using (Process p = Process.Start(psi))
                {
                    stdout = p.StandardOutput.ReadToEnd();
                    stderr = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(30000))
                    {
                        try { p.Kill(); } catch { }
                        return -1;
                    }
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return -2;
            }
        }

        private static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            string t = s.Replace("\r", " ").Replace("\n", " ").Trim();
            while (t.IndexOf("  ", StringComparison.Ordinal) >= 0) { t = t.Replace("  ", " "); }
            return t.Length > 200 ? t.Substring(0, 200) + "…" : t;
        }

        // ------------------------------------------------------------------ 还原

        public static AutoExtraReport Restore()
        {
            AutoExtraReport rep = new AutoExtraReport();
            List<string[]> rows = ReadRecords();
            if (rows.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有待还原的记录（这台机器还没用这个窗口禁用过计划任务 / 服务）。";
                return rep;
            }
            StringBuilder bad = new StringBuilder();
            List<string> keep = new List<string>();
            foreach (string[] r in rows)
            {
                if (r.Length < 4) { continue; }
                string kind = r[1].Trim();
                string id = From64(r[2]);
                string extra = r[3].Trim();
                string err;
                if (kind == "service")
                {
                    int start;
                    int.TryParse(extra, NumberStyles.Integer, CultureInfo.InvariantCulture, out start);
                    err = RestoreService(id, start);
                }
                else
                {
                    err = RestoreTask(id, extra == "enabled");
                }
                if (err.Length == 0) { rep.Changed++; }
                else
                {
                    rep.Failed++;
                    if (err.IndexOf("拒绝", StringComparison.Ordinal) >= 0
                        || err.IndexOf("权限", StringComparison.Ordinal) >= 0) { rep.NeedAdmin = true; }
                    bad.Append("　· ").Append(id).Append("（").Append(err).AppendLine("）");
                    keep.Add(Join(r));
                }
            }
            WriteRecords(keep);
            rep.Ok = rep.Failed == 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("还原 ").Append(rep.Changed.ToString(CultureInfo.InvariantCulture)).Append(" 项");
            if (rep.Failed > 0) { sb.Append("，失败 ").Append(rep.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 项"); }
            sb.Append("。");
            if (bad.Length > 0) { sb.AppendLine().Append(bad.ToString()); }
            rep.Message = sb.ToString();
            return rep;
        }

        private static string RestoreTask(string name, bool wasEnabled)
        {
            if (!wasEnabled) { return ""; }        // 原来就是禁用的，不用动
            string output, err2;
            int code = RunSchtasks("/change /tn \"" + name + "\" /enable", out output, out err2);
            if (code != 0) { return "schtasks 返回 " + code.ToString(CultureInfo.InvariantCulture) + "：" + OneLine(output + " " + err2); }
            // 读回：任务 XML 里不该再有 <Enabled>false</Enabled>
            string root = TasksRoot();
            string file = Path.Combine(root, name.TrimStart('\\'));
            if (!File.Exists(file)) { return "找不到那个任务文件（任务可能被删了）：" + file; }
            AutoExtraItem now = ParseTask(file, root);
            return (now != null && !now.Disabled) ? "" : "命令返回成功，但重读任务文件还写着「已禁用」";
        }

        private static string RestoreService(string name, int start)
        {
            if (start < 0 || start > 4) { return "记录里的 Start 值不对：" + start.ToString(CultureInfo.InvariantCulture); }
            string hive, path;
            ServicesRoot(out hive, out path);
            RegistryKey baseKey = (hive == "HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
            try
            {
                using (RegistryKey sk = baseKey.OpenSubKey(path + "\\" + name, true))
                {
                    if (sk == null) { return "这个服务的注册表键已经不在了"; }
                    sk.SetValue("Start", start, RegistryValueKind.DWord);
                }
                int now;
                if (ReadServiceStart(name, out now) && now == start) { return ""; }
                return "写完读回来不是 " + start.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException) { return "权限不够（要管理员）：" + ex.Message; }
                return ex.Message;
            }
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

        public static string ReportText(List<AutoExtraItem> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("任务目录：").AppendLine(TasksRoot());
            sb.Append("服务根：").AppendLine(ServicesRootText());
            int tasks = 0, svcs = 0, movable = 0, disabled = 0;
            foreach (AutoExtraItem it in list)
            {
                if (it.Kind == "task") { tasks++; } else { svcs++; }
                if (it.CanChange) { movable++; }
                if (it.Disabled) { disabled++; }
            }
            sb.Append("共 ").Append(list.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" 项：计划任务 ").Append(tasks.ToString(CultureInfo.InvariantCulture))
                .Append(" 个、服务 ").Append(svcs.ToString(CultureInfo.InvariantCulture))
                .Append(" 个；第三方可禁用 ").Append(movable.ToString(CultureInfo.InvariantCulture))
                .Append(" 个、已禁用 ").Append(disabled.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个。");
            foreach (AutoExtraItem it in list)
            {
                sb.Append(it.IsSystem ? "· " : (it.Disabled ? "- " : "× ")).Append('[').Append(it.KindText()).Append("] ")
                    .Append(it.Name).Append("　").Append(it.StateText());
                if (it.Kind == "task") { sb.Append("　触发：").Append(it.Trigger); }
                if (it.Command.Length > 0) { sb.Append("　执行：").Append(it.Command); }
                sb.AppendLine();
            }
            sb.Append("pending=").Append(PendingRecords().ToString(CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("record=").AppendLine(RecordFile());
            sb.Append("（只读，没有改任何东西；禁用 / 还原只能在窗口里点）");
            return sb.ToString();
        }

        public static string JsonReport(List<AutoExtraItem> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"tasks\":\"").Append(JsonText(TasksRoot())).Append("\",\"services\":\"")
                .Append(JsonText(ServicesRootText())).Append("\",\"pending\":")
                .Append(PendingRecords().ToString(CultureInfo.InvariantCulture)).Append(",\"items\":[");
            for (int i = 0; i < list.Count; i++)
            {
                AutoExtraItem it = list[i];
                if (i > 0) { sb.Append(","); }
                sb.Append("{\"kind\":\"").Append(JsonText(it.Kind)).Append("\",\"id\":\"").Append(JsonText(it.Id))
                    .Append("\",\"name\":\"").Append(JsonText(it.Name)).Append("\",\"state\":\"")
                    .Append(JsonText(it.StateText())).Append("\",\"disabled\":")
                    .Append(it.Disabled ? "true" : "false").Append(",\"system\":")
                    .Append(it.IsSystem ? "true" : "false").Append(",\"command\":\"")
                    .Append(JsonText(it.Command)).Append("\"}");
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
