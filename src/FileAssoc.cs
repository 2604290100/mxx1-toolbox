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
    /// <summary>一条「文件关联体检」项：说白了就是「注册表里某个键的某个值，正常该是什么」。</summary>
    internal sealed class AssocItem
    {
        public string Group = "";                  // 分组标题：「快捷方式（.lnk）」这类
        public string Title = "";                  // 这一项叫什么（给人看的一句话）
        public string Key = "";                    // 相对根的键路径（如 `.lnk` / `lnkfile`）
        public string ValueName = "";              // "" = 那个键的(默认)值
        public RegistryValueKind Kind = RegistryValueKind.String;
        public string Want = "";                   // 正常应该是什么
        public bool Fixable = true;                // 只报告的项（用户级覆盖）是 false
        public string Note = "";                   // 这个值缺了 / 错了会出什么事
        public string Hive = "";                   // "" = 跟随根；"HKCU" = 固定读用户区（只报告用）

        // ---- 实测结果 ----
        public bool HasKey;                        // 那个键在不在
        public bool Exists;                        // 那个值在不在
        public string Actual = null;               // null = 这个值不存在
        public RegistryValueKind ActualKind = RegistryValueKind.Unknown;
        public string State = "ok";                // ok / missing（缺）/ wrong（有但不是默认）
    }

    /// <summary>一次「修复」/「还原」的结果（报告行 + 计数）。</summary>
    internal sealed class AssocFixReport
    {
        public bool Ok = true;
        public string Title = "";                  // 「修复」/「还原」
        public string Message = "";                // 一句话结论
        public List<string> Lines = new List<string>();
        public int Changed;
        public int Skipped;
        public int Failed;
        public bool NeedAdmin;                     // 失败原因是权限：窗口里会提示「用管理员重开」
        public string BackupPath = "";             // 原值记在哪个文件

        public string Body()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string s in Lines) { sb.AppendLine(s); }
            return sb.ToString();
        }
    }

    /// <summary>「文件关联体检 / 修复」的后端（用户 2026-10-24 报的那个错：装完 lolPing 点「运行 /
    /// 双击那个快捷方式」弹出「该文件没有与之关联的应用来执行该操作」）。
    ///
    /// 量出来的真因：`HKLM\SOFTWARE\Classes\lnkfile` 里的 **`IsShortcut`** 这个值不见了。它是 Windows
    /// 认定「这个文件是快捷方式」的唯一标记 —— 少了它，双击任何 .lnk 都会走「该用哪个应用打开？」那条路，
    /// 于是报那句「没有与之关联的应用」（.exe 的关联本身是好的）。实测补回去之后同一个快捷方式立刻能开。
    ///
    /// 五条口径（和「启动项管理」「电源与笔记本」一个规矩）：
    /// ① **只动「系统默认该有」的那几个值**：这张表里的键 / 值 / 数据全部写死在这儿，不认识的、
    ///    用户自己改过的一概不碰；
    /// ② **写前先记原值**（`assoc-original.tsv`：动作 `add`（原来没这个值）/ `set`（原来有别的值）、
    ///    原数据、写进去的数据）—— **记不下来就一个字都不改**；
    /// ③ **写完读回核对**，核对不一致就报出来，绝不说成「已修好」；
    /// ④ **能还原**：「还原」按记录把工具箱加的值删掉、被覆盖的值写回原样；如果那个值后来被别人改过
    ///    **跳过并在报告里说出来**（怕冲掉用户自己的改动）；
    /// ⑤ **值不对 ≠ 该改**：默认只补「缺的」；「有但不是默认」（用户 / 别的软件自己设的）要先勾
    ///    「连值不对的也改回默认」才动，报告里一直会写清楚它现在是什么。
    ///
    /// ⚠️ 测试隔离：`MXX1_ASSOC_ROOT`（把「根」换成 `HKCU\Software\mxx1-toolbox\assoc-test` 这种隔离根，
    /// 读 / 写 / 还原全在隔离根里跑，一个字节都不碰真实关联）、`MXX1_ASSOC_TSV`（记录文件挪走）。
    /// **写入口（修复 / 还原）只在窗口里点、或者在隔离根下走**（命令行的 `assoc` 默认只读）。</summary>
    internal static class FileAssoc
    {
        public static string LastError = "";

        private static readonly string[] TsvHeader = new string[]
        {
            "# mxx1 工具箱 · 文件关联「修复」前的原值（「还原」就照这份写回去；别手改，改坏了那一行会被跳过）",
            "# 时间\t完整键\t值名\t类型\t动作(add = 原来没这个值 / set = 原来有别的值)\t原数据\t工具箱写进去的数据"
        };

        // ------------------------------------------------------------------ 根

        private static string _hiveName = "";
        private static string _hivePath = "";
        private static bool _isolated;

        /// <summary>「根」= 这张表里所有项都在它下面：默认 `HKLM\SOFTWARE\Classes`（系统默认这一层），
        /// 测试用 `MXX1_ASSOC_ROOT` 换成隔离根。</summary>
        private static void LoadRoot()
        {
            _hiveName = "HKLM";
            _hivePath = @"SOFTWARE\Classes";
            _isolated = false;
            string env = (Environment.GetEnvironmentVariable("MXX1_ASSOC_ROOT") ?? "").Trim().TrimEnd('\\');
            if (env.Length == 0) { return; }
            int i = env.IndexOf('\\');
            string hive = (i < 0) ? env : env.Substring(0, i);
            _hiveName = HiveShort(hive);
            _hivePath = (i < 0) ? "" : env.Substring(i + 1);
            _isolated = true;
        }

        /// <summary>`HKEY_CURRENT_USER` / `HKCU` / `HKLM` / `HKEY_LOCAL_MACHINE` 都认。</summary>
        private static string HiveShort(string hive)
        {
            string h = (hive ?? "").Trim().ToUpperInvariant();
            if (h == "HKCU" || h == "HKEY_CURRENT_USER") { return "HKCU"; }
            if (h == "HKLM" || h == "HKEY_LOCAL_MACHINE") { return "HKLM"; }
            return "HKLM";
        }

        private static RegistryKey HiveByName(string shortName)
        {
            return (shortName == "HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
        }

        public static bool Isolated { get { LoadRoot(); return _isolated; } }

        /// <summary>给界面 / 报告用的一句话：「HKLM\SOFTWARE\Classes」。</summary>
        public static string RootText()
        {
            LoadRoot();
            return _hivePath.Length > 0 ? (_hiveName + "\\" + _hivePath) : _hiveName;
        }

        /// <summary>写的那条路（修复 / 还原）**只允许在 `MXX1_ASSOC_ROOT` 隔离下走**（命令行出口靠它拦）。
        /// 界面里那个窗口不靠这句判断 —— 它自己会过条款门 + 确认框 + 要管理员。</summary>
        public static bool WriteAllowed() { return Isolated; }

        // ------------------------------------------------------------------ 记录文件

        public static string RecordFile()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_ASSOC_TSV") ?? "").Trim();
            if (env.Length > 0) { return env; }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } } catch { }
            return Path.Combine(dir, "assoc-original.tsv");
        }

        /// <summary>还有几条没还原的记录（窗口里「还原」按钮按它亮不亮、报告末尾也念它）。</summary>
        public static int PendingRecords()
        {
            string path = RecordFile();
            int n = 0;
            try
            {
                if (!System.IO.File.Exists(path)) { return 0; }
                foreach (string line in System.IO.File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0) { continue; }
                    if (line.StartsWith("#")) { continue; }
                    if (line.Split('\t').Length < 7) { continue; }
                    n++;
                }
            }
            catch { return 0; }
            return n;
        }

        // ------------------------------------------------------------------ 体检表

        /// <summary>这张表就是「一台正常的 Win7 / Win10 / Win11 上该有的关联」。数据全是系统自带的默认值；
        /// `%SystemRoot%` 这种占位符原样写、不写死盘符（同「系统工具」页的规矩）。</summary>
        private static List<AssocItem> BuildTable()
        {
            List<AssocItem> l = new List<AssocItem>();

            // ---- 快捷方式（.lnk）：用户这次报的那个错就出在这一组 ----
            AddExt(l, "快捷方式（.lnk）", ".lnk", "lnkfile",
                "扩展名到「快捷方式」这一类的登记；缺了它双击 .lnk 会先问「用什么打开」");
            AddVal(l, "快捷方式（.lnk）", "快捷方式标记 lnkfile\\IsShortcut", "lnkfile", "IsShortcut",
                RegistryValueKind.String, "",
                "★ 就是这一条：它没了 = Windows 不把 .lnk 当快捷方式 → 双击任何快捷方式都报"
                + "「该文件没有与之关联的应用来执行该操作」。补回去立刻就好，不用重启");
            AddVal(l, "快捷方式（.lnk）", "快捷方式处理器 lnkfile\\CLSID", "lnkfile\\CLSID", "",
                RegistryValueKind.String, "{00021401-0000-0000-C000-000000000046}",
                "指向系统的「快捷方式」组件；不对的话快捷方式的图标 / 属性页会乱");
            AddVal(l, "快捷方式（.lnk）", "快捷方式不显示 .lnk 后缀 lnkfile\\NeverShowExt", "lnkfile", "NeverShowExt",
                RegistryValueKind.String, "",
                "缺了只是资源管理器里显示成「名字.lnk」（不影响能不能打开）");

            // ---- 程序（.exe）----
            AddExt(l, "程序（.exe）", ".exe", "exefile",
                "扩展名到「程序」这一类的登记；缺了就是双击 .exe 报没有与之关联的应用");
            AddVal(l, "程序（.exe）", "双击运行程序 exefile\\shell\\open\\command", "exefile\\shell\\open\\command", "",
                RegistryValueKind.String, "\"%1\" %*",
                "双击 .exe 走的就是这一句；缺了 = 所有程序都打不开");
            AddVal(l, "程序（.exe）", "右键「以管理员身份运行」 exefile\\shell\\runas\\command", "exefile\\shell\\runas\\command", "",
                RegistryValueKind.String, "\"%1\" %*",
                "缺了「以管理员身份运行」点了没反应");

            // ---- 批处理与脚本 ----
            AddExt(l, "批处理与脚本", ".bat", "batfile", "双击 .bat 靠它；缺了既跑不起来、右键也没有「打开方式」");
            AddVal(l, "批处理与脚本", "运行批处理 batfile\\shell\\open\\command", "batfile\\shell\\open\\command", "",
                RegistryValueKind.String, "\"%1\" %*", "双击 .bat 走这一句");
            AddExt(l, "批处理与脚本", ".cmd", "cmdfile", "同 .bat：双击 .cmd 靠它");
            AddVal(l, "批处理与脚本", "运行命令脚本 cmdfile\\shell\\open\\command", "cmdfile\\shell\\open\\command", "",
                RegistryValueKind.String, "\"%1\" %*", "双击 .cmd 走这一句");
            AddExt(l, "批处理与脚本", ".com", "comfile", "老式 DOS 程序；缺了 .com 双击没反应");
            AddVal(l, "批处理与脚本", "运行 .com 程序 comfile\\shell\\open\\command", "comfile\\shell\\open\\command", "",
                RegistryValueKind.String, "\"%1\" %*", "双击 .com 走这一句");

            // ---- 安装包与注册表 ----
            AddExt(l, "安装包与注册表", ".msi", "Msi.Package", "缺了装不了任何 .msi 安装包");
            AddVal(l, "安装包与注册表", "安装 .msi Msi.Package\\shell\\Open\\command", "Msi.Package\\shell\\Open\\command", "",
                RegistryValueKind.ExpandString, "\"%SystemRoot%\\System32\\msiexec.exe\" /i \"%1\" %*",
                "双击 .msi 走这一句；缺了安装包双击没反应");
            AddExt(l, "安装包与注册表", ".reg", "regfile", "缺了双击 .reg 不能导入注册表");
            AddVal(l, "安装包与注册表", "导入 .reg regfile\\shell\\open\\command", "regfile\\shell\\open\\command", "",
                RegistryValueKind.String, "regedit.exe \"%1\"", "双击 .reg 走这一句");

            // ---- 网址 · 屏幕保护 ----
            AddExt(l, "网址 · 屏幕保护", ".url", "InternetShortcut", "桌面上的网址快捷方式靠它；缺了双击没反应");
            AddVal(l, "网址 · 屏幕保护", "网址快捷方式标记 InternetShortcut\\IsShortcut", "InternetShortcut", "IsShortcut",
                RegistryValueKind.String, "",
                "和 .lnk 那条一模一样：没了双击 .url 也报没有与之关联的应用");
            AddExt(l, "网址 · 屏幕保护", ".pif", "piffile", "老的 DOS 程序快捷方式；缺了双击没反应");
            AddVal(l, "网址 · 屏幕保护", "DOS 快捷方式标记 piffile\\IsShortcut", "piffile", "IsShortcut",
                RegistryValueKind.String, "", "同上：没了双击 .pif 报没有与之关联的应用");
            AddExt(l, "网址 · 屏幕保护", ".scr", "scrfile", "缺了双击屏保文件不能预览 / 安装");
            AddVal(l, "网址 · 屏幕保护", "预览屏幕保护 scrfile\\shell\\open\\command", "scrfile\\shell\\open\\command", "",
                RegistryValueKind.String, "\"%1\" /S", "双击 .scr 走这一句");

            // ---- 文件夹 ----
            AddVal(l, "文件夹", "双击文件夹打开 Folder\\shell\\open\\command", "Folder\\shell\\open\\command", "",
                RegistryValueKind.ExpandString, "%SystemRoot%\\Explorer.exe",
                "★ 这一条 2026-10-11 真被删过一次：删掉之后双击任何文件夹都打不开");

            return l;
        }

        private static void AddExt(List<AssocItem> l, string group, string ext, string progid, string note)
        {
            AddVal(l, group, "「" + ext + "」用哪一类打开", ext, "", RegistryValueKind.String, progid, note);
        }

        private static void AddVal(List<AssocItem> l, string group, string title, string key, string valueName,
            RegistryValueKind kind, string want, string note)
        {
            AssocItem it = new AssocItem();
            it.Group = group;
            it.Title = title;
            it.Key = key;
            it.ValueName = valueName;
            it.Kind = kind;
            it.Want = want;
            it.Note = note;
            l.Add(it);
        }

        // ------------------------------------------------------------------ 体检

        public static List<AssocItem> Check()
        {
            LoadRoot();
            List<AssocItem> items = BuildTable();
            foreach (AssocItem it in items) { Measure(it); }
            // 用户区那一层：只报告、不改（动它等于替用户做「默认应用」的决定）
            if (!_isolated) { items.AddRange(UserOverrides()); }
            return items;
        }

        private static void Measure(AssocItem it)
        {
            LoadRoot();
            it.HasKey = false;
            it.Exists = false;
            it.Actual = null;
            it.ActualKind = RegistryValueKind.Unknown;
            string hive = (it.Hive.Length > 0) ? it.Hive : _hiveName;
            string path = (it.Hive.Length > 0) ? "" : _hivePath;
            try
            {
                RegistryKey baseKey = HiveByName(hive);
                RegistryKey root = (path.Length > 0) ? baseKey.OpenSubKey(path, false) : baseKey;
                try
                {
                    if (root == null) { it.State = "missing"; return; }
                    using (RegistryKey k = root.OpenSubKey(it.Key, false))
                    {
                        if (k == null) { it.State = "missing"; return; }
                        it.HasKey = true;
                        object raw = k.GetValue(it.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (raw == null) { it.State = "missing"; return; }
                        it.Exists = true;
                        it.ActualKind = k.GetValueKind(it.ValueName);
                        it.Actual = Text(raw);
                        it.State = SameValue(it.Actual, it.Want) ? "ok" : "wrong";
                    }
                }
                finally
                {
                    if (path.Length > 0)
                    {
                        if (root != null) { root.Dispose(); }
                        baseKey.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                it.State = "missing";
            }
        }

        private static string Text(object raw)
        {
            if (raw == null) { return ""; }
            if (raw is string) { return (string)raw; }
            if (raw is string[]) { return string.Join("|", (string[])raw); }
            if (raw is byte[]) { return Convert.ToBase64String((byte[])raw); }
            return Convert.ToString(raw, CultureInfo.InvariantCulture);
        }

        /// <summary>比「该有的值」和「现在的值」。`%SystemRoot%` 这种占位符和展开后的绝对路径都算对 ——
        /// 有的机器（尤其被「优化」过的）写的是展开后的形式，那不算坏。</summary>
        private static bool SameValue(string actual, string want)
        {
            if (actual == null) { return (want ?? "").Length == 0; }
            string a = actual.Trim();
            string w = (want ?? "").Trim();
            if (string.Equals(a, w, StringComparison.OrdinalIgnoreCase)) { return true; }
            if (w.IndexOf('%') < 0) { return false; }
            string wa = Environment.ExpandEnvironmentVariables(w);
            string aa = Environment.ExpandEnvironmentVariables(a);
            return string.Equals(aa, wa, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>用户区那一层（**只读**）：这三处有东西的话，「没有与之关联的应用」还有第二种原因。</summary>
        private static List<AssocItem> UserOverrides()
        {
            List<AssocItem> l = new List<AssocItem>();
            string[] exts = new string[] { ".lnk", ".exe", ".bat" };
            foreach (string ext in exts)
            {
                string sub = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\" + ext + "\\UserChoice";
                string progId = null;
                bool has = false;
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(sub, false))
                    {
                        if (k != null)
                        {
                            has = true;
                            object v = k.GetValue("ProgId", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                            progId = (v == null) ? "" : Text(v);
                        }
                    }
                }
                catch { }
                if (!has) { continue; }   // 正常机器上一般没有

                AssocItem it = new AssocItem();
                it.Group = "用户区（这里只报告，不改）";
                it.Title = "「" + ext + "」被指定过打开方式（FileExts\\" + ext + "\\UserChoice）";
                it.Hive = "HKCU";
                it.Key = sub;
                it.ValueName = "ProgId";
                it.Kind = RegistryValueKind.String;
                it.Want = "";
                it.Fixable = false;
                it.HasKey = true;
                it.Exists = true;
                it.Actual = progId;
                it.State = "wrong";
                it.Note = "有这一项 = 以前给 " + ext + " 选过「用什么打开」。它会让「打不开」继续存在；"
                    + "要清掉它：设置 → 应用 → 默认应用 → 按文件类型选择默认应用，把 " + ext + " 改回默认";
                l.Add(it);
            }
            return l;
        }

        // ------------------------------------------------------------------ 修复

        public static AssocFixReport Fix(bool alsoWrong)
        {
            AssocFixReport rep = new AssocFixReport();
            rep.Title = "修复";
            LoadRoot();
            List<AssocItem> items = Check();

            // ① 先算「要改哪些」
            List<AssocItem> plan = new List<AssocItem>();
            foreach (AssocItem it in items)
            {
                if (it.State == "ok") { continue; }
                if (!it.Fixable)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Title + " —— 这一项只报告：" + it.Note);
                    continue;
                }
                if (it.State == "wrong" && !alsoWrong)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Title + " —— 现在是 " + Show(it.Actual) + "，和默认的 "
                        + Show(it.Want) + " 不一样；要改它得勾上「连值不对的也改回默认」");
                    continue;
                }
                plan.Add(it);
            }
            if (plan.Count == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要补的项";
                rep.Lines.Insert(0, "根：" + RootText() + " —— 这张表里的项都不缺（" + rep.Skipped + " 项跳过，原因在下面）");
                return rep;
            }

            // ② 把原值写成记录（**记不下来就一个字都不改**）
            string path = RecordFile();
            rep.BackupPath = path;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            List<string> records = new List<string>();
            foreach (AssocItem it in plan)
            {
                records.Add(stamp + "\t" + _hiveName + "\\" + JoinKey(_hivePath, it.Key) + "\t"
                    + ValueNameText(it.ValueName) + "\t" + KindText(it.Kind) + "\t"
                    + (it.Exists ? "set" : "add") + "\t" + (it.Exists ? (it.Actual ?? "") : "") + "\t"
                    + (it.Want ?? ""));
            }
            try
            {
                bool fresh = !System.IO.File.Exists(path);
                using (StreamWriter w = new StreamWriter(path, true, new UTF8Encoding(false)))
                {
                    if (fresh) { foreach (string h in TsvHeader) { w.WriteLine(h); } }
                    foreach (string r in records) { w.WriteLine(r); }
                    w.Flush();
                }
            }
            catch (Exception ex)
            {
                rep.Ok = false;
                rep.Message = "写不进原值记录文件，所以一个字都没改：" + ex.Message;
                rep.Lines.Insert(0, "× " + rep.Message + "（" + path + "）");
                LastError = ex.Message;
                return rep;
            }

            // ③ 写 + 读回核对
            rep.Lines.Insert(0, "");
            rep.Lines.Insert(0, "根：" + RootText() + "；原值已记进 " + path);
            foreach (AssocItem it in plan)
            {
                string error;
                if (!WriteItem(it, out error))
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Title + " —— 没写成：" + error);
                    if (LooksLikePermission(error)) { rep.NeedAdmin = true; }
                    continue;
                }
                Measure(it);
                if (it.State == "ok")
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + it.Title + " —— 已写成 " + Show(it.Want) + "（读回核对一致）");
                }
                else
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Title + " —— 写进去了，但读回来还是「" + StateText(it) + "」");
                }
            }
            rep.Ok = (rep.Failed == 0);
            rep.Message = (rep.Failed == 0)
                ? ("补好了 " + rep.Changed + " 项（跳过 " + rep.Skipped + " 项）")
                : ("补好 " + rep.Changed + " 项，" + rep.Failed + " 项没成");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message + "。原值记在 " + path + "，点「还原成修复前」可以原样退回。");
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        private static string JoinKey(string a, string b)
        {
            if (a == null || a.Length == 0) { return b; }
            if (b == null || b.Length == 0) { return a; }
            return a + "\\" + b;
        }

        private static string ValueNameText(string valueName)
        {
            return (valueName == null || valueName.Length == 0) ? "-" : valueName;
        }

        private static bool WriteItem(AssocItem it, out string error)
        {
            error = "";
            RegistryKey baseKey = null;
            RegistryKey root = null;
            try
            {
                baseKey = HiveByName(_hiveName);
                root = baseKey.CreateSubKey(_hivePath);
                if (root == null) { error = "打不开根键 " + RootText(); return false; }
                using (RegistryKey k = root.CreateSubKey(it.Key))
                {
                    if (k == null) { error = "建不了键 " + JoinKey(_hivePath, it.Key); return false; }
                    k.SetValue(it.ValueName, it.Want, it.Kind);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (root != null) { root.Dispose(); }
                if (baseKey != null) { baseKey.Dispose(); }
            }
        }

        private static bool LooksLikePermission(string message)
        {
            if (string.IsNullOrEmpty(message)) { return false; }
            string m = message.ToLowerInvariant();
            return m.Contains("denied") || m.Contains("拒绝") || m.Contains("unauthorized")
                || m.Contains("权限") || m.Contains("privilege");
        }

        // ------------------------------------------------------------------ 还原

        /// <summary>按记录把工具箱改过的东西退回原样：加进去的值 = 删掉；覆盖掉的值 = 写回原数据。
        /// 后来的值被人改过 → **跳过**（并记下来），绝不把用户的改动冲掉。</summary>
        public static AssocFixReport Restore()
        {
            AssocFixReport rep = new AssocFixReport();
            rep.Title = "还原";
            string file = RecordFile();
            rep.BackupPath = file;

            List<string> lines = new List<string>();
            try
            {
                if (System.IO.File.Exists(file)) { lines.AddRange(System.IO.File.ReadAllLines(file, Encoding.UTF8)); }
            }
            catch (Exception ex)
            {
                rep.Ok = false;
                rep.Message = "读不到原值记录：" + ex.Message;
                rep.Lines.Add("× " + rep.Message);
                return rep;
            }

            List<string> keep = new List<string>();
            foreach (string raw in lines)
            {
                if (raw.Trim().Length == 0) { continue; }
                if (raw.StartsWith("#")) { continue; }
                string[] f = raw.Split('\t');
                if (f.Length < 7)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 跳过一行读不懂的记录：" + raw);
                    keep.Add(raw);
                    continue;
                }
                string fullKey = f[1];
                string valueName = (f[2] == "-") ? "" : f[2];
                string label = fullKey + (valueName.Length > 0 ? "\\" + valueName : "");

                int cut = fullKey.IndexOf('\\');
                string hive = (cut < 0) ? fullKey : fullKey.Substring(0, cut);
                string keyPath = (cut < 0) ? "" : fullKey.Substring(cut + 1);

                string note;
                bool ok = RestoreOne(HiveShort(hive), keyPath, valueName, f[3], f[4], f[5], f[6], out note);
                if (ok)
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + label + " —— " + (note.Length > 0 ? note : "已退回原样"));
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没退：" + label + " —— " + note);
                    keep.Add(raw);
                }
            }

            if (rep.Changed == 0 && rep.Skipped == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要还原的记录（这台机器还没用工具箱补过关联）";
                rep.Lines.Insert(0, rep.Message);
                return rep;
            }

            // 记录文件的善后：退干净了就删掉；还剩没退成的就把剩下的写回去
            try
            {
                if (keep.Count == 0)
                {
                    if (System.IO.File.Exists(file)) { System.IO.File.Delete(file); }
                }
                else if (System.IO.File.Exists(file))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine(TsvHeader[0]);
                    sb.AppendLine("# （下面这几行是上次没退成的，还留着）");
                    foreach (string k in keep) { sb.AppendLine(k); }
                    System.IO.File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                rep.Lines.Add("· 记录文件收尾没成：" + ex.Message);
            }

            rep.Ok = (rep.Skipped == 0);
            rep.Message = (rep.Skipped == 0)
                ? ("还原了 " + rep.Changed + " 项，记录已清空")
                : ("还原了 " + rep.Changed + " 项，" + rep.Skipped + " 项没退（原因在每一行后面）");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message);
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        private static bool RestoreOne(string hive, string keyPath, string valueName, string kindText, string action,
            string oldData, string newData, out string note)
        {
            note = "";
            RegistryKey baseKey = null;
            RegistryKey k = null;
            try
            {
                baseKey = HiveByName(hive);
                k = baseKey.OpenSubKey(keyPath, true);
                if (k == null)
                {
                    note = "这个键已经不在了，没什么可退的";
                    return true;
                }
                object cur = k.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (action == "add")
                {
                    if (cur == null) { note = "这个值已经不存在了，跳过"; return true; }
                    if (!SameValue(Text(cur), newData))
                    {
                        note = "现在这个值是「" + Text(cur) + "」，不是工具箱写进去的那个，跳过（怕删掉你自己的改动）";
                        return false;
                    }
                    k.DeleteValue(valueName, false);
                    object after = k.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (after != null) { note = "删了但读回来还在"; return false; }
                    return true;
                }
                // set：原来有别的值，写回原数据
                if (cur != null && !SameValue(Text(cur), newData))
                {
                    note = "现在这个值是「" + Text(cur) + "」，不是工具箱写进去的那个，跳过（怕冲掉你自己的改动）";
                    return false;
                }
                k.SetValue(valueName, oldData, ParseKind(kindText));
                object back = k.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (!SameValue(Text(back), oldData))
                {
                    note = "写回了但读回来是「" + Text(back) + "」，不是原值";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                note = ex.Message;
                return false;
            }
            finally
            {
                if (k != null) { k.Dispose(); }
                if (baseKey != null) { baseKey.Dispose(); }
            }
        }

        // ------------------------------------------------------------------ 文字

        private static string KindText(RegistryValueKind k)
        {
            switch (k)
            {
                case RegistryValueKind.ExpandString: return "ExpandString";
                case RegistryValueKind.DWord: return "DWord";
                case RegistryValueKind.QWord: return "QWord";
                case RegistryValueKind.MultiString: return "MultiString";
                case RegistryValueKind.Binary: return "Binary";
                default: return "String";
            }
        }

        private static RegistryValueKind ParseKind(string text)
        {
            switch ((text ?? "").Trim())
            {
                case "ExpandString": return RegistryValueKind.ExpandString;
                case "DWord": return RegistryValueKind.DWord;
                case "QWord": return RegistryValueKind.QWord;
                case "MultiString": return RegistryValueKind.MultiString;
                case "Binary": return RegistryValueKind.Binary;
                default: return RegistryValueKind.String;
            }
        }

        /// <summary>报告里「一个值」的人话：空的写成 (空)、没有的写成 (缺)。</summary>
        private static string Show(string value)
        {
            if (value == null) { return "(缺)"; }
            if (value.Length == 0) { return "(空)"; }
            return value;
        }

        private static string StateText(AssocItem it)
        {
            if (it.State == "ok") { return "正常"; }
            if (it.State == "missing") { return it.HasKey ? "缺这个值" : "连键都没有"; }
            return "是「" + Show(it.Actual) + "」，和默认不一样";
        }

        /// <summary>窗口 / 命令行共用的那份「体检报告」正文。</summary>
        public static string ReportText(List<AssocItem> items)
        {
            LoadRoot();
            StringBuilder sb = new StringBuilder();
            int ok = 0, missing = 0, wrong = 0, ro = 0;
            sb.AppendLine("文件关联体检 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            if (_isolated) { sb.AppendLine("根：" + RootText() + "（⚠️ 测试隔离根：这里改的不是真的系统关联）"); }
            else { sb.AppendLine("根：" + RootText() + "（系统默认这一层；改它对这台机器所有用户生效）"); }
            sb.AppendLine();

            string group = "";
            foreach (AssocItem it in items)
            {
                if (it.Group != group)
                {
                    group = it.Group;
                    sb.AppendLine();
                    sb.AppendLine("【" + group + "】");
                }
                string mark;
                if (!it.Fixable) { mark = "·"; ro++; }
                else if (it.State == "ok") { mark = "√"; ok++; }
                else if (it.State == "missing") { mark = "×"; missing++; }
                else { mark = "!"; wrong++; }

                sb.Append("  ").Append(mark).Append(' ').Append(it.Title);
                if (!it.Fixable) { sb.Append("：有（ProgId=").Append(Show(it.Actual)).Append("）"); }
                else if (it.State == "ok") { sb.Append("：").Append(Show(it.Want)); }
                else if (it.State == "missing") { sb.Append("：缺（该是 ").Append(Show(it.Want)).Append("）"); }
                else { sb.Append("：现在是 ").Append(Show(it.Actual)).Append("（默认 ").Append(Show(it.Want)).Append("）"); }
                sb.AppendLine();
                if (it.State != "ok" && it.Note.Length > 0)
                {
                    sb.Append("      → ").AppendLine(it.Note);
                }
            }

            sb.AppendLine();
            int pending = PendingRecords();
            sb.AppendLine("汇总：" + items.Count + " 项里 " + ok + " 项正常"
                + (missing > 0 ? ("，" + missing + " 项缺") : "")
                + (wrong > 0 ? ("，" + wrong + " 项和默认不一样") : "")
                + (ro > 0 ? ("，" + ro + " 项只报告") : "")
                + (pending > 0 ? ("；有 " + pending + " 条修复记录还没还原") : "；没有待还原的记录"));
            if (missing == 0 && wrong == 0)
            {
                sb.AppendLine("结论：这一层没发现问题 —— 双击还是打不开的话，看「用户区」那几条，或者点下面「修复」再看一遍。");
            }
            return sb.ToString();
        }

        /// <summary>命令行 `assoc --json` 用的机器可读输出。</summary>
        public static string JsonReport(List<AssocItem> items)
        {
            StringBuilder sb = new StringBuilder();
            int ok = 0, missing = 0, wrong = 0;
            foreach (AssocItem it in items)
            {
                if (!it.Fixable) { continue; }
                if (it.State == "ok") { ok++; }
                else if (it.State == "missing") { missing++; }
                else { wrong++; }
            }
            sb.Append("{\"root\":").Append(JsonText(RootText()));
            sb.Append(",\"isolated\":").Append(Isolated ? "true" : "false");
            sb.Append(",\"count\":").Append(items.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"ok\":").Append(ok.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"missing\":").Append(missing.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"wrong\":").Append(wrong.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"pending\":").Append(PendingRecords().ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"items\":[");
            for (int i = 0; i < items.Count; i++)
            {
                AssocItem it = items[i];
                if (i > 0) { sb.Append(","); }
                sb.Append("{\"title\":").Append(JsonText(it.Title));
                sb.Append(",\"key\":").Append(JsonText(it.Hive.Length > 0 ? ("HKCU\\" + it.Key) : it.Key));
                sb.Append(",\"value\":").Append(JsonText(it.ValueName));
                sb.Append(",\"state\":").Append(JsonText(it.State));
                sb.Append(",\"fixable\":").Append(it.Fixable ? "true" : "false");
                sb.Append(",\"want\":").Append(JsonText(it.Want));
                sb.Append(",\"now\":").Append(JsonText(it.Actual == null ? "" : it.Actual));
                sb.Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string JsonText(string s)
        {
            if (s == null) { return "\"\""; }
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
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
                        if (c < ' ') { sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                        else { sb.Append(c); }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
