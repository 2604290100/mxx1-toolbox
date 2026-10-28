// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>开始菜单「所有应用」列表里的一条（一个 .lnk 快捷方式，2026-10-27 用户要求：
    /// 管一管开始菜单和「程序和功能」两个列表，参考 HideUL）。
    ///
    /// 量出来的机制：开始菜单「所有应用」里的每一项 = 两个文件夹里的一个 `.lnk` 文件 ——
    /// 系统级 `%ProgramData%\Microsoft\Windows\Start Menu\Programs`（这台机器所有用户都看得到，
    /// 装软件时多半写这儿）和用户级 `%APPDATA%\Microsoft\Windows\Start Menu\Programs`
    /// （只有当前用户看得到，部分软件 / 绿色版写这儿）。「最近添加」是 Windows 按最近写入的
    /// .lnk 自己排出来的，跟着这两个目录走。本机实测：系统级 107 个、用户级 42 个。
    ///
    /// 所以「隐藏」= 把 .lnk 移进工具箱的备份目录（保留原相对路径结构）→ 开始菜单立刻不显示，
    /// 程序本体没动、照常能用；「还原」= 按记录移回原位；「删除」= 进回收站（删错了能捞回来）。
    ///
    /// 四条口径（和「此电脑多余图标」一个规矩）：
    /// ① 写前先记原值（`startmenu-hidden.tsv`：时间、来源、相对路径、原完整路径、隐藏后的完整路径）
    ///    —— **记不下来就一个字都不动**；
    /// ② 移完读回核对（原位不在 = 移成了；原位还在 = 没成，如实报）；
    /// ③ **能还原**：按记录移回原位；原位已经被别的文件占了（软件重装 / 别人放回去了）→ 跳过并说明；
    /// ④ 删除只进回收站（走 Recycle.cs），绝不永久删。
    ///
    /// ⚠️ 测试隔离：`MXX1_STARTMENU_SYS`（系统级 Programs 目录的替换）、`MXX1_STARTMENU_USER`
    /// （用户级替换）、`MXX1_STARTMENU_HIDDEN`（备份目录替换）、`MXX1_STARTMENU_TSV`（记录文件）。
    /// 三个目录都设了才算隔离，写入口（隐藏 / 还原 / 删除）才允许走命令行；**界面里那个窗口不靠它**
    /// —— 窗口自己过条款门 + 确认框 + 系统级要管理员。
    /// 命令行 `startmenu` / `startmenu --json` 只读。</summary>
    internal sealed class StartMenuItem
    {
        public string Rel = "";        // 相对 Programs 根的路径，如 7-Zip\7-Zip 文件管理器.lnk
        public string Full = "";       // 完整路径（系统级或用户级根 + Rel）
        public string Source = "";     // system（系统级）/ user（用户级）
        public string Root = "";       // 它所在的 Programs 根目录
        public bool SystemItem;        // Windows 自带的（Accessories / Administrative Tools 等）→ 只是标注，不锁
        public bool OurHidden;         // 工具箱隐藏过的（来自记录文件）→ 列表里标「已隐藏」，可以单独「显示这个…」
        public string HiddenDest = ""; // 隐藏时移去的位置（记录文件第 5 列；已隐藏条目的文件真的在那儿）

        public string DisplayName { get { return Rel; } }
    }

    /// <summary>一次「隐藏 / 还原 / 删除」的结果（报告行 + 计数）。</summary>
    internal sealed class StartMenuReport
    {
        public bool Ok = true;
        public string Title = "";
        public string Message = "";
        public List<string> Lines = new List<string>();
        public int Changed;
        public int Skipped;
        public int Failed;
        public bool NeedAdmin;
        public string BackupPath = "";

        public string Body()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string s in Lines) { sb.AppendLine(s); }
            return sb.ToString();
        }
    }

    /// <summary>「开始菜单程序管理」的后端：只读扫两个 Programs 目录 → 隐藏 / 还原 / 删除。</summary>
    internal static class StartMenu
    {
        public static string LastError = "";

        private static readonly string[] TsvHeader = new string[]
        {
            "# mxx1 工具箱 · 「开始菜单程序管理」隐藏前的记录（「还原」就照这份把 .lnk 移回原位；别手改，改坏了那一行会被跳过）",
            "# 时间\t来源(system/user)\t相对路径\t原完整路径\t隐藏后的完整路径"
        };

        // ------------------------------------------------------------------ 根

        private static string _sysRoot = "";
        private static string _userRoot = "";
        private static string _hiddenRoot = "";
        private static bool _isolated;

        private static string DefaultSys()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
        }
        private static string DefaultUser()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        }

        /// <summary>「根」= 两个 Programs 目录 + 一个隐藏备份目录。测试用 `MXX1_STARTMENU_*`
        /// 三个环境变量全换成隔离根（两个源目录 + 一个备份目录），读写都在隔离根里跑。</summary>
        private static void LoadRoot()
        {
            _sysRoot = DefaultSys();
            _userRoot = DefaultUser();
            _hiddenRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "mxx1-toolbox", "startmenu-hidden");
            _isolated = false;
            string sys = (Environment.GetEnvironmentVariable("MXX1_STARTMENU_SYS") ?? "").Trim();
            string user = (Environment.GetEnvironmentVariable("MXX1_STARTMENU_USER") ?? "").Trim();
            string hidden = (Environment.GetEnvironmentVariable("MXX1_STARTMENU_HIDDEN") ?? "").Trim();
            if (sys.Length > 0) { _sysRoot = sys.TrimEnd('\\'); }
            if (user.Length > 0) { _userRoot = user.TrimEnd('\\'); }
            if (hidden.Length > 0) { _hiddenRoot = hidden.TrimEnd('\\'); }
            if (sys.Length > 0 && user.Length > 0 && hidden.Length > 0) { _isolated = true; }
        }

        public static bool Isolated { get { LoadRoot(); return _isolated; } }
        public static string SysRoot { get { LoadRoot(); return _sysRoot; } }
        public static string UserRoot { get { LoadRoot(); return _userRoot; } }
        public static string HiddenRoot { get { LoadRoot(); return _hiddenRoot; } }
        public static bool WriteAllowed() { return Isolated; }

        /// <summary>给界面 / 报告用的一句话。</summary>
        public static string RootText()
        {
            LoadRoot();
            return "系统级 " + _sysRoot + " | 用户级 " + _userRoot + " | 隐藏到 " + _hiddenRoot;
        }

        // ------------------------------------------------------------------ 记录文件

        public static string RecordFile()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_STARTMENU_TSV") ?? "").Trim();
            if (env.Length > 0) { return env; }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } } catch { }
            return Path.Combine(dir, "startmenu-hidden.tsv");
        }

        /// <summary>还有几条没还原的记录（窗口「还原」按钮按它亮不亮）。</summary>
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
                    if (line.Split('\t').Length < 5) { continue; }
                    n++;
                }
            }
            catch { return 0; }
            return n;
        }

        // ------------------------------------------------------------------ 扫描

        /// <summary>只读扫两个 Programs 目录里的全部 .lnk（含子目录），系统级在前、用户级在后，
        /// 组内按路径排；再把记录文件里**已经隐藏的**也补进来（标 OurHidden，像「程序和功能」
        /// 窗口一样：隐藏后条目还在列表里，能单独「显示这个…」放回去）。</summary>
        public static List<StartMenuItem> Scan()
        {
            LoadRoot();
            List<StartMenuItem> list = new List<StartMenuItem>();
            AddFrom(list, _sysRoot, "system");
            AddFrom(list, _userRoot, "user");
            AddHidden(list);
            return list;
        }

        private static void AddHidden(List<StartMenuItem> list)
        {
            string path = RecordFile();
            try
            {
                if (!System.IO.File.Exists(path)) { return; }
                foreach (string line in System.IO.File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0) { continue; }
                    if (line.StartsWith("#")) { continue; }
                    string[] f = line.Split('\t');
                    if (f.Length < 5) { continue; }
                    StartMenuItem it = new StartMenuItem();
                    it.Source = f[1];
                    it.Rel = f[2];
                    it.Full = f[3];
                    it.HiddenDest = f[4];
                    it.Root = Path.GetDirectoryName(f[3]);
                    it.SystemItem = LooksSystem(it.Rel);
                    it.OurHidden = true;
                    list.Add(it);
                }
            }
            catch { }
        }

        private static void AddFrom(List<StartMenuItem> list, string root, string source)
        {
            if (root.Length == 0) { return; }
            if (!Directory.Exists(root)) { return; }
            try
            {
                foreach (string f in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    StartMenuItem it = new StartMenuItem();
                    it.Root = root;
                    it.Full = f;
                    it.Source = source;
                    it.Rel = f.Substring(root.Length).TrimStart('\\');
                    it.SystemItem = LooksSystem(it.Rel);
                    list.Add(it);
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            list.Sort(delegate (StartMenuItem a, StartMenuItem b)
            {
                int c = string.Compare(a.Rel, b.Rel, StringComparison.OrdinalIgnoreCase);
                return c;
            });
        }

        /// <summary>Windows 自带的那几组（Accessories / Administrative Tools / System Tools /
        /// Windows PowerShell / Windows Kits 这几个目录，加上根目录里的「Immersive Control Panel」
        /// 这类）。只是**标注**（窗口里标红提示），开始菜单的 .lnk 本来就是可以自由增删的，
        /// 这一页不锁它们 —— 锁的是「别把程序本体弄丢」。</summary>
        private static bool LooksSystem(string rel)
        {
            string r = rel.ToLowerInvariant();
            if (r.StartsWith("accessories\\")) { return true; }
            if (r.StartsWith("administrative tools\\")) { return true; }
            if (r.StartsWith("system tools\\")) { return true; }
            if (r.StartsWith("windows powershell\\")) { return true; }
            if (r.StartsWith("windows kits\\")) { return true; }
            if (r == "accessories\\notepad.lnk" || r == "accessories\\paint.lnk"
                || r == "accessories\\snipping tool.lnk" || r == "accessories\\wordpad.lnk") { return true; }
            return false;
        }

        // ------------------------------------------------------------------ 隐藏

        /// <summary>把选中的 .lnk 隐藏：**记原值 → 移进备份目录（保留目录结构）→ 读回核对**。
        /// 只移 .lnk，程序本体没动。移不走的（被占用 / 权限）如实报。</summary>
        public static StartMenuReport Hide(List<StartMenuItem> targets)
        {
            StartMenuReport rep = new StartMenuReport();
            rep.Title = "隐藏";
            LoadRoot();

            List<StartMenuItem> plan = new List<StartMenuItem>();
            foreach (StartMenuItem it in targets)
            {
                if (it == null) { continue; }
                if (!System.IO.File.Exists(it.Full))
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Rel + " —— 这个 .lnk 已经不在了（可能被别的程序清掉了）");
                    continue;
                }
                plan.Add(it);
            }
            if (plan.Count == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要隐藏的条目";
                rep.Lines.Insert(0, "扫描根：" + RootText());
                return rep;
            }

            // ① 记原值（**记不下来就一个字都不动**）
            string path = RecordFile();
            rep.BackupPath = path;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            List<string> records = new List<string>();
            foreach (StartMenuItem it in plan)
            {
                string dest = HiddenDest(it.Rel);
                it.HiddenDest = dest;
                records.Add(stamp + "\t" + it.Source + "\t" + it.Rel + "\t" + it.Full + "\t" + dest);
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
                rep.Message = "写不进原值记录文件，所以一个字都没动：" + ex.Message;
                rep.Lines.Insert(0, "× " + rep.Message + "（" + path + "）");
                LastError = ex.Message;
                return rep;
            }

            // ② 移 + 读回核对
            rep.Lines.Insert(0, "");
            rep.Lines.Insert(0, "扫描根：" + RootText() + "；原值已记进 " + path);
            foreach (StartMenuItem it in plan)
            {
                string error;
                if (!MoveOne(it, out error))
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Rel + " —— 没隐藏成：" + error);
                    if (LooksLikePermission(error)) { rep.NeedAdmin = true; }
                    continue;
                }
                if (!System.IO.File.Exists(it.Full) && System.IO.File.Exists(HiddenDest(it.Rel)))
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + it.Rel + " —— 已隐藏（原位不在了、备份目录里在）");
                }
                else
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Rel + " —— 移了但读回核对不上");
                }
            }
            rep.Ok = (rep.Failed == 0);
            rep.Message = (rep.Failed == 0)
                ? ("隐藏了 " + rep.Changed + " 条（跳过 " + rep.Skipped + " 条）")
                : ("隐藏 " + rep.Changed + " 条，" + rep.Failed + " 条没成");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message + "。原值记在 " + path + "，点「还原成隐藏前」可以原样放回去。");
            rep.Lines.Add("只移了开始菜单的 .lnk，程序本体没动；软件重装可能把它的快捷方式写回来（去软件设置里关）。");
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        /// <summary>备份目录里的目标路径（保留原相对路径结构，同名两级目录都隐藏时不会撞）。</summary>
        private static string HiddenDest(string rel)
        {
            return Path.Combine(_hiddenRoot, rel);
        }

        private static bool MoveOne(StartMenuItem it, out string error)
        {
            error = "";
            try
            {
                string dest = HiddenDest(it.Rel);
                string dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
                if (System.IO.File.Exists(dest))
                {
                    error = "备份目录里已经有一个同名的了（" + dest + "）—— 先把那个处理掉再试";
                    return false;
                }
                System.IO.File.Move(it.Full, dest);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------ 显示（单条还原）

        /// <summary>把选中的一条放回原位（按记录）：原位已经被别的文件占了 → 跳过并说明；
        /// 成功了那一行从记录里删掉（没选中的记录原样留着）。窗口里「显示这个…」走这条。</summary>
        public static StartMenuReport Show(List<StartMenuItem> targets)
        {
            StartMenuReport rep = new StartMenuReport();
            rep.Title = "显示";
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

            HashSet<string> want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StartMenuItem t in targets)
            {
                if (t == null || string.IsNullOrEmpty(t.Rel)) { continue; }
                want.Add(t.Source + "\t" + t.Rel);
            }

            List<string> keep = new List<string>();
            foreach (string raw in lines)
            {
                if (raw.Trim().Length == 0) { continue; }
                if (raw.StartsWith("#")) { continue; }
                string[] f = raw.Split('\t');
                if (f.Length < 5)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 跳过一行读不懂的记录：" + raw);
                    keep.Add(raw);
                    continue;
                }
                if (!want.Contains(f[1] + "\t" + f[2])) { keep.Add(raw); continue; }

                string note;
                bool ok = RestoreOne(f[1], f[2], f[3], f[4], out note);
                if (ok)
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + f[2] + " —— " + note);
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没放回：" + f[2] + " —— " + note);
                    keep.Add(raw);
                }
            }

            if (rep.Changed == 0 && rep.Skipped == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要显示的记录（这台机器还没用工具箱隐藏过开始菜单条目）";
                rep.Lines.Insert(0, rep.Message);
                return rep;
            }

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
                    sb.AppendLine("# （下面这几行是还没放回原位的，还留着）");
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
                ? ("放回原位 " + rep.Changed + " 条，记录已清掉这几行")
                : ("放回原位 " + rep.Changed + " 条，" + rep.Skipped + " 条没放回（原因在每一行后面）");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message);
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        // ------------------------------------------------------------------ 还原

        /// <summary>按记录把 .lnk 移回原位：原位置已经被别的文件占了（软件重装 / 别人放回去了）
        /// → 跳过并说明（不覆盖它）；退干净了记录文件就删掉。</summary>
        public static StartMenuReport Restore()
        {
            StartMenuReport rep = new StartMenuReport();
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
                if (f.Length < 5)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 跳过一行读不懂的记录：" + raw);
                    keep.Add(raw);
                    continue;
                }
                string source = f[1];
                string rel = f[2];
                string full = f[3];
                string dest = f[4];

                string note;
                bool ok = RestoreOne(source, rel, full, dest, out note);
                if (ok)
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + rel + " —— " + note);
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没放回：" + rel + " —— " + note);
                    keep.Add(raw);
                }
            }

            if (rep.Changed == 0 && rep.Skipped == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要还原的记录（这台机器还没用工具箱隐藏过开始菜单条目）";
                rep.Lines.Insert(0, rep.Message);
                return rep;
            }

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
                ? ("还原了 " + rep.Changed + " 条，记录已清空")
                : ("还原了 " + rep.Changed + " 条，" + rep.Skipped + " 条没放回（原因在每一行后面）");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message);
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        private static bool RestoreOne(string source, string rel, string full, string dest, out string note)
        {
            note = "";
            try
            {
                if (!System.IO.File.Exists(dest))
                {
                    note = "备份目录里那个 .lnk 已经不在了（可能被清掉了），没法移回";
                    return false;
                }
                if (System.IO.File.Exists(full))
                {
                    note = "原位已经有一个 .lnk 了（可能软件重装加回来了），没覆盖它";
                    return false;
                }
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
                System.IO.File.Move(dest, full);
                if (!System.IO.File.Exists(full) || System.IO.File.Exists(dest))
                {
                    note = "移了但读回核对不上";
                    return false;
                }
                note = "已放回原位（读回核对在）";
                return true;
            }
            catch (Exception ex)
            {
                note = ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------ 删除

        /// <summary>把选中的 .lnk **删到回收站**（不是永久删）。删完逐个复查，还在的如实报。</summary>
        public static StartMenuReport Delete(List<StartMenuItem> targets)
        {
            StartMenuReport rep = new StartMenuReport();
            rep.Title = "删除";
            LoadRoot();

            List<string> paths = new List<string>();
            List<StartMenuItem> gone = new List<StartMenuItem>();
            foreach (StartMenuItem it in targets)
            {
                if (it == null) { continue; }
                string p = it.OurHidden ? it.HiddenDest : it.Full;
                if (p.Length > 0 && System.IO.File.Exists(p))
                {
                    paths.Add(p);
                    gone.Add(it);
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Rel + " —— 这个 .lnk 已经不在了");
                }
            }
            if (paths.Count == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要删除的条目";
                rep.Lines.Insert(0, "扫描根：" + RootText());
                return rep;
            }

            Recycle.DeleteResult r = Recycle.DeleteToRecycleBin(paths);
            rep.Changed = r.Deleted;
            rep.Failed = r.Failed.Count;
            for (int i = 0; i < r.Failed.Count; i++)
            {
                string rel = RelOf(r.Failed[i]);
                rep.Lines.Add("× " + rel + " —— 没删成：" + r.ReasonOf(i));
            }
            foreach (StartMenuItem it in targets)
            {
                string check = it.OurHidden ? it.HiddenDest : it.Full;
                if (check.Length > 0 && System.IO.File.Exists(check)) { continue; }
                bool already = false;
                foreach (string fp in r.Failed) { if (string.Equals(fp, check, StringComparison.OrdinalIgnoreCase)) { already = true; break; } }
                if (already) { continue; }
                rep.Skipped++;
                rep.Lines.Add("· 没动：" + it.Rel + " —— 删除接口没把它算进去");
            }
            // 已隐藏的条目删的是备份目录里的那个 .lnk —— 删成了就把记录里那一行也清掉（还原没东西可还）
            if (rep.Failed == 0)
            {
                foreach (StartMenuItem it in gone)
                {
                    if (!it.OurHidden) { continue; }
                    DropRecordLines(it.Source + "\t" + it.Rel);
                }
            }
            rep.Ok = (rep.Failed == 0);
            rep.Message = (rep.Failed == 0)
                ? ("删到回收站 " + rep.Changed + " 条（跳过 " + rep.Skipped + " 条）")
                : ("删了 " + rep.Changed + " 条，" + rep.Failed + " 条没成（原因在每一行后面）");
            rep.Lines.Insert(0, "扫描根：" + RootText());
            rep.Lines.Insert(0, "");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message + "。删的是开始菜单里的 .lnk，**程序本体没动**；删错了去回收站捞回来。");
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        private static string RelOf(string full)
        {
            try { return full.Substring(Path.GetDirectoryName(full).Length).TrimStart('\\'); }
            catch { return full; }
        }

        /// <summary>把记录文件里匹配 `来源\t相对路径` 的那几行删掉（删已隐藏条目时用：
        /// 备份目录里的 .lnk 进了回收站，记录里就没东西可还原了，别让「还原」对着空记录报错）。</summary>
        private static void DropRecordLines(string key)
        {
            string file = RecordFile();
            try
            {
                if (!System.IO.File.Exists(file)) { return; }
                List<string> keep = new List<string>();
                bool any = false;
                foreach (string raw in System.IO.File.ReadAllLines(file, Encoding.UTF8))
                {
                    string t = raw.Trim();
                    if (t.Length == 0) { keep.Add(raw); continue; }
                    if (t.StartsWith("#")) { keep.Add(raw); continue; }
                    string[] f = raw.Split('\t');
                    if (f.Length >= 3 && string.Equals(f[1] + "\t" + f[2], key, StringComparison.OrdinalIgnoreCase))
                    {
                        any = true;
                        continue;
                    }
                    keep.Add(raw);
                }
                if (!any) { return; }
                if (keep.Count == 0)
                {
                    System.IO.File.Delete(file);
                }
                else
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (string k in keep) { sb.AppendLine(k); }
                    System.IO.File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ 文字

        private static string Text(object raw)
        {
            if (raw == null) { return ""; }
            return Convert.ToString(raw, CultureInfo.InvariantCulture);
        }

        private static bool LooksLikePermission(string message)
        {
            if (string.IsNullOrEmpty(message)) { return false; }
            string m = message.ToLowerInvariant();
            return m.Contains("denied") || m.Contains("拒绝") || m.Contains("unauthorized")
                || m.Contains("权限") || m.Contains("privilege") || m.Contains("access");
        }

        /// <summary>窗口 / 命令行共用的那份「扫描报告」正文。</summary>
        public static string ReportText(List<StartMenuItem> items)
        {
            LoadRoot();
            StringBuilder sb = new StringBuilder();
            int sys = 0, user = 0, hidden = 0;
            sb.AppendLine("开始菜单程序管理 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("开始菜单「所有应用」里的每一项 = 这两个文件夹里的一个 .lnk：");
            sb.AppendLine("  " + _sysRoot + "（系统级，这台机器所有用户）");
            sb.AppendLine("  " + _userRoot + "（用户级，只有当前用户）");
            if (_isolated) { sb.AppendLine("⚠️ 测试隔离根：这里改的不是真的开始菜单（上面两行是隔离路径）。"); }
            sb.AppendLine();
            sb.AppendLine("记号：S 系统级 · U 用户级 · # Windows 自带的（只是标注，一样可以隐藏 / 删除）· H 已隐藏（在列表里看得到，点「显示这个…」放回去）");
            sb.AppendLine();

            foreach (StartMenuItem it in items)
            {
                if (it.Source == "system") { sys++; } else { user++; }
                if (it.OurHidden) { hidden++; }
                sb.Append("  ").Append(it.Source == "system" ? "S" : "U").Append(' ');
                if (it.SystemItem) { sb.Append("# "); }
                if (it.OurHidden) { sb.Append("H "); }
                sb.Append(it.Rel).Append("  · 完整路径 ").Append(it.Full).AppendLine();
            }

            sb.AppendLine();
            int pending = PendingRecords();
            sb.AppendLine("汇总：共 " + items.Count + " 个 —— 系统级 " + sys + " 个、用户级 " + user + " 个"
                + (hidden > 0 ? ("；其中 " + hidden + " 个已隐藏") : "")
                + (pending > 0 ? ("；有 " + pending + " 个隐藏记录还没还原") : "；没有待还原的记录"));
            if (items.Count > 0)
            {
                sb.AppendLine("结论：选中一个点「隐藏这个…」（或右键）把 .lnk 移进备份目录，开始菜单立刻不显示；"
                    + "隐藏过的还在列表里（标「已隐藏」），点「显示这个…」随时放回去；「还原成隐藏前」按记录全部移回原位；"
                    + "右键「删除这个…」删到回收站（程序本体不删）。");
            }
            else
            {
                sb.AppendLine("结论：两个目录里都没有 .lnk（多半被精简系统清掉了）。");
            }
            return sb.ToString();
        }

        /// <summary>命令行 `startmenu --json` 用的机器可读输出。</summary>
        public static string JsonReport(List<StartMenuItem> items)
        {
            LoadRoot();
            StringBuilder sb = new StringBuilder();
            int sys = 0, user = 0, system = 0, hidden = 0;
            foreach (StartMenuItem it in items)
            {
                if (it.Source == "system") { sys++; } else { user++; }
                if (it.SystemItem) { system++; }
                if (it.OurHidden) { hidden++; }
            }
            sb.Append("{\"sys\":").Append(sys.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"user\":").Append(user.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"system\":").Append(system.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"hidden\":").Append(hidden.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"isolated\":").Append(Isolated ? "true" : "false");
            sb.Append(",\"count\":").Append(items.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"pending\":").Append(PendingRecords().ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"sysRoot\":").Append(JsonText(_sysRoot));
            sb.Append(",\"userRoot\":").Append(JsonText(_userRoot));
            sb.Append(",\"hiddenRoot\":").Append(JsonText(_hiddenRoot));
            sb.Append(",\"items\":[");
            for (int i = 0; i < items.Count; i++)
            {
                StartMenuItem it = items[i];
                if (i > 0) { sb.Append(","); }
                sb.Append("{\"rel\":").Append(JsonText(it.Rel));
                sb.Append(",\"source\":").Append(JsonText(it.Source));
                sb.Append(",\"full\":").Append(JsonText(it.Full));
                sb.Append(",\"system\":").Append(it.SystemItem ? "true" : "false");
                sb.Append(",\"hidden\":").Append(it.OurHidden ? "true" : "false");
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
