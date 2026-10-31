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
    /// <summary>控制面板「程序和功能」/ 设置「应用」列表里的一条（2026-10-27 用户要求：管一管
    /// 开始菜单和「程序和功能」两个列表，参考 HideUL —— HideUL 只做「程序和功能」这一半）。
    ///
    /// 量出来的机制：这个列表 = 注册表里三根 `Uninstall` 子键，每根下一个子键 = 一个已装程序
    /// （本机实测：HKLM 64 位视图 95 条 / HKLM 32 位视图（WOW6432Node）173 条 / HKCU 13 条）。
    /// 控制面板读每条的 `DisplayName`（显示名）→ 显示在列表里；`SystemComponent=1` 的条目
    /// **不显示** —— 这正是 HideUL 的隐藏手法（和 Windows 隐藏自家组件是同一个机制）。
    ///
    /// 所以「隐藏」= 给该条写 `SystemComponent=1`（记原值 → 写 → 读回核对）；「显示」= 按记录
    /// 把 SystemComponent 删掉 / 写回原值；「锁定卸载」= 写 `NoRemove=1`（P&F 里卸载按钮变灰）；
    /// 「删除条目」= 先把整条导出成 .reg 备份、再删掉该子键（**程序本体还在**，只是从列表消失
    /// —— 「工商银行安全控件 (仅用做移除)」那种条目正合用），可一键导回。
    ///
    /// 五条口径（和「文件关联体检」「此电脑多余图标」一个规矩）：
    /// ① 只列**有 DisplayName** 的条目（P&F 就显示这些）；SystemComponent 本来就是 1 且不是
    ///    工具箱隐藏的（系统组件 / HideUL 藏的）→ 标「系统组件」只列不给动；
    /// ② 写前先记原值（`uninstall-changes.tsv`：时间、根、键路径、名称、SystemComponent / NoRemove
    ///    当时是什么）—— **记不下来就一个字都不改**；
    /// ③ 写完逐处读回核对，核对不一致就报出来，绝不说成「已隐藏」；
    /// ④ **能还原**：按记录把值写回 / 删掉；「删除条目」另有 `uninstall-deleted.tsv`（时间、根、
    ///    键路径、.reg 备份路径），「还原」会把这些备份导回注册表重建条目；
    /// ⑤ 删除条目**只删卸载列表里的注册表条目**，绝不碰程序文件、绝不跑它的卸载程序。
    ///
    /// ⚠️ 测试隔离：`MXX1_UNINST_HKLM64` / `MXX1_UNINST_HKLM32` / `MXX1_UNINST_HKCU`
    /// （把三根「根」换成隔离根 —— 测试全指到 HKCU 下面就不用管理员）、`MXX1_UNINST_TSV`
    /// （改动记录）、`MXX1_UNINST_DELTSV`（删除记录）、`MXX1_UNINST_BACKUP`（.reg 备份目录）。
    /// 三个根都设了才算隔离，写入口才允许走命令行；**界面里那个窗口不靠它** —— 窗口自己过
    /// 条款门 + 确认框 + 要管理员。命令行 `uninstall` / `uninstall --json` 只读。</summary>
    internal sealed class UninstallItem
    {
        public string KeyFull = "";    // 完整键路径（hive 前缀 + 相对路径 + 子键名），如 HKLM\SOFTWARE\…\Uninstall\{GUID}
        public string Name = "";       // DisplayName（P&F 列表显示名）
        public string Publisher = "";  // 发布者
        public string InstallDate = "";// 安装时间（YYYYMMDD）
        public string InstallLocation = ""; // InstallLocation（通常为空）
        public string DisplayIcon = "";// DisplayIcon（列表图标来源）
        public string Root = "";       // HKLM64（64 位视图）/ HKLM32（32 位视图）/ HKCU
        public bool SystemItem;        // SystemComponent 本来就是 1 且不是工具箱隐藏的 → 只列不给动
        public bool OurHidden;         // 是工具箱藏的（tsv 里有 hide 记录）→ 可「显示」
        public bool OurLocked;         // 是工具箱锁的（tsv 里有 lock 记录）→ 可「恢复卸载」
        public bool LockedUninstall;   // NoRemove=1（卸载按钮被锁）

        public string RootText() { return Root; }
        /// <summary>子键名（KeyFull 最后一个反斜杠后面的部分）：`{GUID}` 或 `Weixin` 这种，
        /// 命令行 `--hide=HKLM32:Weixin` 用它定位。</summary>
        public string KeyName { get { int i = KeyFull.LastIndexOf('\\'); return i >= 0 ? KeyFull.Substring(i + 1) : KeyFull; } }
        public string StatusText()
        {
            if (SystemItem) { return "系统组件"; }
            if (OurHidden) { return "已隐藏"; }
            if (OurLocked) { return "已锁定卸载"; }
            return "显示中";
        }
    }

    /// <summary>一次「隐藏 / 显示 / 锁定 / 还原 / 删除条目」的结果。</summary>
    internal sealed class UninstallReport
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

    /// <summary>「程序和功能管理」的后端：只读扫三根 Uninstall → 隐藏 / 显示 / 锁定卸载 /
    /// 删除条目 / 还原。</summary>
    internal static class UninstallItems
    {
        public static string LastError = "";

        private static readonly string[] TsvHeader = new string[]
        {
            "# mxx1 工具箱 · 「程序和功能」改动前的记录（「还原」就照这份把值写回 / 删掉；别手改）",
            "# 时间\t根(HKLM64/HKLM32/HKCU)\t键路径\t名称\t动作(hide/lock)\t原SystemComponent(absent/0/1)\t原NoRemove(absent/1)"
        };
        private static readonly string[] DelTsvHeader = new string[]
        {
            "# mxx1 工具箱 · 「程序和功能」删除条目的备份记录（「还原」按这份把 .reg 导回注册表）",
            "# 时间\t根(HKLM64/HKLM32/HKCU)\t键路径\t名称\t.reg备份路径"
        };

        // ------------------------------------------------------------------ 根

        private static string _hklm64Hive = "HKLM";
        private static string _hklm64Path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private static string _hklm32Hive = "HKLM";
        private static string _hklm32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        private static string _hkcuHive = "HKCU";
        private static string _hkcuPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private static string _backupDir = "";
        private static bool _isolated;

        private static void LoadRoot()
        {
            _hklm64Hive = "HKLM";
            _hklm64Path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            _hklm32Hive = "HKLM";
            _hklm32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
            _hkcuHive = "HKCU";
            _hkcuPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            _backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "mxx1-toolbox", "uninstall-backup");
            _isolated = false;
            string h64 = (Environment.GetEnvironmentVariable("MXX1_UNINST_HKLM64") ?? "").Trim().TrimEnd('\\');
            string h32 = (Environment.GetEnvironmentVariable("MXX1_UNINST_HKLM32") ?? "").Trim().TrimEnd('\\');
            string cu = (Environment.GetEnvironmentVariable("MXX1_UNINST_HKCU") ?? "").Trim().TrimEnd('\\');
            if (h64.Length > 0) { ParseRoot(h64, ref _hklm64Hive, ref _hklm64Path); }
            if (h32.Length > 0) { ParseRoot(h32, ref _hklm32Hive, ref _hklm32Path); }
            if (cu.Length > 0) { ParseRoot(cu, ref _hkcuHive, ref _hkcuPath); }
            string bk = (Environment.GetEnvironmentVariable("MXX1_UNINST_BACKUP") ?? "").Trim().TrimEnd('\\');
            if (bk.Length > 0) { _backupDir = bk; }
            if (h64.Length > 0 && h32.Length > 0 && cu.Length > 0) { _isolated = true; }
        }

        private static void ParseRoot(string value, ref string hive, ref string path)
        {
            int i = value.IndexOf('\\');
            hive = HiveShort((i < 0) ? value : value.Substring(0, i));
            path = (i < 0) ? "" : value.Substring(i + 1);
        }

        private static string HiveShort(string hive)
        {
            string h = (hive ?? "").Trim().ToUpperInvariant();
            if (h == "HKCU" || h == "HKEY_CURRENT_USER") { return "HKCU"; }
            return "HKLM";
        }

        private static RegistryKey HiveByName(string shortName)
        {
            return (shortName == "HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
        }

        public static bool Isolated { get { LoadRoot(); return _isolated; } }
        public static bool WriteAllowed() { return Isolated; }

        public static string RootText()
        {
            LoadRoot();
            return _hklm64Hive + "\\" + _hklm64Path + " | " + _hklm32Hive + "\\" + _hklm32Path
                + " | " + _hkcuHive + "\\" + _hkcuPath;
        }

        // ------------------------------------------------------------------ 记录文件

        public static string RecordFile()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_UNINST_TSV") ?? "").Trim();
            if (env.Length > 0) { return env; }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } } catch { }
            return Path.Combine(dir, "uninstall-changes.tsv");
        }

        public static string DeletedRecordFile()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_UNINST_DELTSV") ?? "").Trim();
            if (env.Length > 0) { return env; }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } } catch { }
            return Path.Combine(dir, "uninstall-deleted.tsv");
        }

        public static string BackupDir()
        {
            LoadRoot();
            try { if (!Directory.Exists(_backupDir)) { Directory.CreateDirectory(_backupDir); } } catch { }
            return _backupDir;
        }

        /// <summary>还有几条没还原的改动记录 + 删除记录（窗口「还原」按钮按它亮不亮）。</summary>
        public static int PendingRecords()
        {
            return CountRecords(RecordFile()) + CountRecords(DeletedRecordFile());
        }

        private static int CountRecords(string path)
        {
            int n = 0;
            try
            {
                if (!System.IO.File.Exists(path)) { return 0; }
                foreach (string line in System.IO.File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0) { continue; }
                    if (line.StartsWith("#")) { continue; }
                    if (line.Split('\t').Length < 6) { continue; }
                    n++;
                }
            }
            catch { return 0; }
            return n;
        }

        // ------------------------------------------------------------------ 扫描

        /// <summary>只读扫三根 Uninstall：有 DisplayName 的条目都列出（P&F 就显示这些），
        /// 外加「工具箱自己隐藏过的」也要列出来（这样才能点「显示」）。</summary>
        public static List<UninstallItem> Scan()
        {
            LoadRoot();
            HashSet<string> ours = LoadOurKeys();
            HashSet<string> ourLocks = LoadOurLocks();
            List<UninstallItem> list = new List<UninstallItem>();
            AddFrom(list, _hklm64Hive, _hklm64Path, "HKLM64", ours, ourLocks);
            AddFrom(list, _hklm32Hive, _hklm32Path, "HKLM32", ours, ourLocks);
            AddFrom(list, _hkcuHive, _hkcuPath, "HKCU", ours, ourLocks);
            list.Sort(delegate (UninstallItem a, UninstallItem b)
            {
                int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (c != 0) { return c; }
                return string.Compare(a.KeyFull, b.KeyFull, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>把记录文件里「工具箱动过的键」读成集合（完整键路径，OrdinalIgnoreCase）。</summary>
        private static HashSet<string> LoadOurKeys()
        {
            return LoadOurKeysByAction("hide");
        }

        /// <summary>把记录文件里「工具箱锁定过、还没恢复」的键读成集合（坑 105：tsv 混记多种动作，
        /// 读回必须按动作列过滤 —— lock 行只标"工具箱锁过的"，不能拿 hide 行的逻辑混着认）。</summary>
        private static HashSet<string> LoadOurLocks()
        {
            return LoadOurKeysByAction("lock");
        }

        private static HashSet<string> LoadOurKeysByAction(string action)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string path = RecordFile();
            try
            {
                if (!System.IO.File.Exists(path)) { return set; }
                foreach (string line in System.IO.File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) { continue; }
                    string[] f = line.Split('\t');
                    if (f.Length >= 6 && f[2].Length > 0 && string.Equals(f[4], action, StringComparison.OrdinalIgnoreCase)) { set.Add(f[2]); }
                }
            }
            catch { }
            return set;
        }

        private static void AddFrom(List<UninstallItem> list, string hive, string path, string root, HashSet<string> ours, HashSet<string> ourLocks)
        {
            RegistryKey baseKey = null;
            RegistryKey rootKey = null;
            try
            {
                baseKey = HiveByName(hive);
                rootKey = baseKey.OpenSubKey(path, false);
                if (rootKey == null) { return; }
                foreach (string sub in rootKey.GetSubKeyNames())
                {
                    string keyFull = hive + "\\" + path + "\\" + sub;
                    RegistryKey k = null;
                    try
                    {
                        k = rootKey.OpenSubKey(sub, false);
                        if (k == null) { continue; }
                        string name = Text(k.GetValue("DisplayName", null));
                        if (name.Length == 0 && !ours.Contains(keyFull)) { continue; } // P&F 显示的就是有名字的；我们自己隐藏的没名字也要列
                        UninstallItem it = new UninstallItem();
                        it.KeyFull = keyFull;
                        it.Name = name.Length > 0 ? name : "（工具箱隐藏的条目）";
                        it.Publisher = Text(k.GetValue("Publisher", null));
                        it.InstallDate = Text(k.GetValue("InstallDate", null));
                        it.InstallLocation = Text(k.GetValue("InstallLocation", null));
                        it.DisplayIcon = Text(k.GetValue("DisplayIcon", null));
                        it.Root = root;
                        object sc = k.GetValue("SystemComponent", null);
                        bool sc1 = (sc != null && Convert.ToInt32(sc, CultureInfo.InvariantCulture) == 1);
                        object nr = k.GetValue("NoRemove", null);
                        it.LockedUninstall = (nr != null && Convert.ToInt32(nr, CultureInfo.InvariantCulture) == 1);
                        it.OurHidden = ours.Contains(keyFull);
                        it.OurLocked = ourLocks.Contains(keyFull);
                        it.SystemItem = sc1 && !it.OurHidden;
                        list.Add(it);
                    }
                    catch { }
                    finally { if (k != null) { k.Dispose(); } }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                if (rootKey != null) { rootKey.Dispose(); }
                if (baseKey != null) { baseKey.Dispose(); }
            }
        }

        // ------------------------------------------------------------------ 隐藏

        /// <summary>把选中的条目隐藏：**记原值 → 写 SystemComponent=1 → 读回核对**。
        /// 控制面板 / 设置「应用」立刻不显示（不用重启）。</summary>
        public static UninstallReport Hide(List<UninstallItem> targets)
        {
            UninstallReport rep = new UninstallReport();
            rep.Title = "隐藏";
            LoadRoot();

            List<UninstallItem> plan = new List<UninstallItem>();
            foreach (UninstallItem it in targets)
            {
                if (it == null) { continue; }
                if (it.SystemItem)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Name + " —— 这是系统组件 / 别的工具藏的，这一页不碰它");
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

            string path = RecordFile();
            rep.BackupPath = path;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            List<string> records = new List<string>();
            foreach (UninstallItem it in plan)
            {
                string scBefore = ReadDword(it.KeyFull, "SystemComponent");
                string nrBefore = ReadDword(it.KeyFull, "NoRemove");
                records.Add(stamp + "\t" + it.Root + "\t" + it.KeyFull + "\t" + it.Name
                    + "\thide\t" + scBefore + "\t" + nrBefore);
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

            rep.Lines.Insert(0, "");
            rep.Lines.Insert(0, "扫描根：" + RootText() + "；原值已记进 " + path);
            foreach (UninstallItem it in plan)
            {
                string error;
                if (!WriteDword(it.KeyFull, "SystemComponent", 1, out error))
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Name + " —— 没隐藏成：" + error);
                    if (LooksLikePermission(error)) { rep.NeedAdmin = true; }
                    continue;
                }
                string back = ReadDword(it.KeyFull, "SystemComponent");
                if (back == "1")
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + it.Name + " —— 已隐藏（读回核对：SystemComponent=1）");
                }
                else
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Name + " —— 写了但读回来不是 1（" + back + "）");
                }
            }
            rep.Ok = (rep.Failed == 0);
            rep.Message = (rep.Failed == 0)
                ? ("隐藏了 " + rep.Changed + " 条（跳过 " + rep.Skipped + " 条）")
                : ("隐藏 " + rep.Changed + " 条，" + rep.Failed + " 条没成");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message + "。原值记在 " + path + "，点「还原成操作前」可以显示回来。");
            rep.Lines.Add("隐藏只是让列表不显示这条（SystemComponent=1），程序本体没动、卸载入口还在注册表里。");
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        // ------------------------------------------------------------------ 显示（还原一条 hide）

        /// <summary>把选中的条目显示回来（只还原**工具箱藏的那条**）：按记录把 SystemComponent
        /// 删掉 / 写回原值 → 读回核对 → 那行记录从 tsv 里清掉。</summary>
        public static UninstallReport Show(List<UninstallItem> targets)
        {
            UninstallReport rep = new UninstallReport();
            rep.Title = "显示";
            LoadRoot();

            List<UninstallItem> plan = new List<UninstallItem>();
            foreach (UninstallItem it in targets)
            {
                if (it == null) { continue; }
                if (!it.OurHidden)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Name + " —— 这条不是工具箱隐藏的（是系统组件或别的工具藏的）");
                    continue;
                }
                plan.Add(it);
            }
            if (plan.Count == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要显示的条目";
                rep.Lines.Insert(0, "扫描根：" + RootText());
                return rep;
            }

            string path = RecordFile();
            rep.BackupPath = path;
            List<string> lines = new List<string>();
            try
            {
                if (System.IO.File.Exists(path)) { lines.AddRange(System.IO.File.ReadAllLines(path, Encoding.UTF8)); }
            }
            catch (Exception ex)
            {
                rep.Ok = false;
                rep.Message = "读不到原值记录：" + ex.Message;
                rep.Lines.Add("× " + rep.Message);
                return rep;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UninstallItem it in plan) { keys.Add(it.KeyFull); }
            List<string> keep = new List<string>();
            foreach (string raw in lines)
            {
                if (raw.Trim().Length == 0) { continue; }
                if (raw.StartsWith("#")) { continue; }
                string[] f = raw.Split('\t');
                if (f.Length < 6)
                {
                    keep.Add(raw);
                    continue;
                }
                if (f[4] != "hide") { keep.Add(raw); continue; }   // lock 记录不是这条要处理的
                if (!keys.Contains(f[2])) { keep.Add(raw); continue; }

                string error;
                bool ok = ShowOne(f[2], f[5], out error);
                if (ok)
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + f[3] + " —— 已显示回来（读回核对在）");
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没显示回来：" + f[3] + " —— " + error);
                    keep.Add(raw);
                }
            }

            try
            {
                if (keep.Count == 0)
                {
                    if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); }
                }
                else if (System.IO.File.Exists(path))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine(TsvHeader[0]);
                    foreach (string k in keep) { sb.AppendLine(k); }
                    System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                rep.Lines.Add("· 记录文件收尾没成：" + ex.Message);
            }

            if (rep.Changed == 0 && rep.Skipped == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要显示的记录";
                rep.Lines.Insert(0, rep.Message);
                return rep;
            }
            rep.Ok = (rep.Skipped == 0);
            rep.Message = (rep.Skipped == 0)
                ? ("显示了 " + rep.Changed + " 条，记录已清掉")
                : ("显示了 " + rep.Changed + " 条，" + rep.Skipped + " 条没成");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message);
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        /// <summary>按记录还原 SystemComponent：原值 absent → 删值；原值 0 → 写 0；原值 1 → 本来就是
        /// 系统组件（不可能被我们藏），跳过。写完读回核对。</summary>
        private static bool ShowOne(string keyFull, string before, out string error)
        {
            error = "";
            try
            {
                if (before == "absent")
                {
                    if (!DeleteValue(keyFull, "SystemComponent", out error)) { return false; }
                    return ReadDword(keyFull, "SystemComponent") == "absent";
                }
                if (before == "0" || before == "1")
                {
                    if (!WriteDword(keyFull, "SystemComponent", int.Parse(before, CultureInfo.InvariantCulture), out error))
                    {
                        return false;
                    }
                    return ReadDword(keyFull, "SystemComponent") == before;
                }
                error = "记录里原值看不懂（" + before + "）";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------ 锁定卸载

        /// <summary>把选中的条目「锁定卸载」：写 NoRemove=1（P&F 里卸载按钮变灰，防手滑）。
        /// 记原值 → 写 → 读回核对。已经锁着的直接跳过。</summary>
        public static UninstallReport Lock(List<UninstallItem> targets)
        {
            UninstallReport rep = new UninstallReport();
            rep.Title = "锁定卸载";
            LoadRoot();

            List<UninstallItem> plan = new List<UninstallItem>();
            foreach (UninstallItem it in targets)
            {
                if (it == null) { continue; }
                if (it.SystemItem)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Name + " —— 系统组件，这一页不碰它");
                    continue;
                }
                if (it.LockedUninstall)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Name + " —— 它已经是锁定状态了");
                    continue;
                }
                plan.Add(it);
            }
            if (plan.Count == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要锁定的条目";
                rep.Lines.Insert(0, "扫描根：" + RootText());
                return rep;
            }

            string path = RecordFile();
            rep.BackupPath = path;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            List<string> records = new List<string>();
            foreach (UninstallItem it in plan)
            {
                string scBefore = ReadDword(it.KeyFull, "SystemComponent");
                string nrBefore = ReadDword(it.KeyFull, "NoRemove");
                records.Add(stamp + "\t" + it.Root + "\t" + it.KeyFull + "\t" + it.Name
                    + "\tlock\t" + scBefore + "\t" + nrBefore);
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

            rep.Lines.Insert(0, "");
            rep.Lines.Insert(0, "扫描根：" + RootText() + "；原值已记进 " + path);
            foreach (UninstallItem it in plan)
            {
                string error;
                if (!WriteDword(it.KeyFull, "NoRemove", 1, out error))
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Name + " —— 没锁成：" + error);
                    if (LooksLikePermission(error)) { rep.NeedAdmin = true; }
                    continue;
                }
                if (ReadDword(it.KeyFull, "NoRemove") == "1")
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + it.Name + " —— 已锁定卸载（读回核对：NoRemove=1）");
                }
                else
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Name + " —— 写了但读回来不是 1");
                }
            }
            rep.Ok = (rep.Failed == 0);
            rep.Message = (rep.Failed == 0)
                ? ("锁定了 " + rep.Changed + " 条（跳过 " + rep.Skipped + " 条）")
                : ("锁定 " + rep.Changed + " 条，" + rep.Failed + " 条没成");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message + "。原值记在 " + path + "，点「还原成操作前」可以解锁。");
            rep.Lines.Add("锁定 = 卸载按钮变灰（NoRemove=1），防止手滑点到卸载。程序本体没动。");
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        // ------------------------------------------------------------------ 恢复卸载（单条）

        /// <summary>把选中的条目「恢复卸载」（解锁）：按记录把 NoRemove 删掉 / 写回原值 →
        /// 读回核对 → 那行 lock 记录从 tsv 里清掉。只还原**工具箱锁的那条**（tsv 里有 lock 记录）；
        /// 本来 NoRemove=1 的（程序自己 / 别的工具设的）不动 —— 对齐「显示」只还原工具箱藏的语义。</summary>
        public static UninstallReport Unlock(List<UninstallItem> targets)
        {
            UninstallReport rep = new UninstallReport();
            rep.Title = "恢复卸载";
            LoadRoot();

            List<UninstallItem> plan = new List<UninstallItem>();
            foreach (UninstallItem it in targets)
            {
                if (it == null) { continue; }
                if (it.SystemItem)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Name + " —— 系统组件，这一页不碰它");
                    continue;
                }
                if (!it.LockedUninstall)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Name + " —— 它没有锁定（NoRemove 不是 1）");
                    continue;
                }
                if (!it.OurLocked)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Name + " —— 这条的 NoRemove 不是工具箱写的（程序自己 / 别的工具设的），工具箱不动它");
                    continue;
                }
                plan.Add(it);
            }
            if (plan.Count == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要恢复卸载的条目";
                rep.Lines.Insert(0, "扫描根：" + RootText());
                return rep;
            }

            string path = RecordFile();
            rep.BackupPath = path;
            List<string> lines = new List<string>();
            try
            {
                if (System.IO.File.Exists(path)) { lines.AddRange(System.IO.File.ReadAllLines(path, Encoding.UTF8)); }
            }
            catch (Exception ex)
            {
                rep.Ok = false;
                rep.Message = "读不到原值记录：" + ex.Message;
                rep.Lines.Add("× " + rep.Message);
                return rep;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UninstallItem it in plan) { keys.Add(it.KeyFull); }
            List<string> keep = new List<string>();
            foreach (string raw in lines)
            {
                if (raw.Trim().Length == 0) { continue; }
                if (raw.StartsWith("#")) { continue; }
                string[] f = raw.Split('\t');
                if (f.Length < 6)
                {
                    keep.Add(raw);
                    continue;
                }
                if (f[4] != "lock") { keep.Add(raw); continue; }   // hide 记录不是这条要处理的（坑 105：按动作列过滤）
                if (!keys.Contains(f[2])) { keep.Add(raw); continue; }

                string error;
                bool ok = RestoreNoRemove(f[2], f[6], out error);
                if (ok)
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + f[3] + " —— 已恢复卸载（读回核对在）");
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没恢复：" + f[3] + " —— " + error);
                    keep.Add(raw);
                }
            }

            try
            {
                if (keep.Count == 0)
                {
                    if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); }
                }
                else if (System.IO.File.Exists(path))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine(TsvHeader[0]);
                    foreach (string k in keep) { sb.AppendLine(k); }
                    System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                rep.Lines.Add("· 记录文件收尾没成：" + ex.Message);
            }

            if (rep.Changed == 0 && rep.Skipped == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要恢复的记录";
                rep.Lines.Insert(0, rep.Message);
                return rep;
            }
            rep.Ok = (rep.Skipped == 0);
            rep.Message = (rep.Skipped == 0)
                ? ("恢复了卸载 " + rep.Changed + " 条，记录已清掉")
                : ("恢复了卸载 " + rep.Changed + " 条，" + rep.Skipped + " 条没成");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message);
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        /// <summary>按记录还原 NoRemove：原值 absent → 删值；原值 0 → 写 0（lock 记录的原值不会 1 ——
        /// Lock 前 NoRemove=1 的会直接跳过）。写完读回核对。</summary>
        private static bool RestoreNoRemove(string keyFull, string before, out string error)
        {
            error = "";
            try
            {
                if (string.Equals(before, "absent", StringComparison.OrdinalIgnoreCase))
                {
                    if (!DeleteValue(keyFull, "NoRemove", out error)) { return false; }
                    return ReadDword(keyFull, "NoRemove") == "absent";
                }
                if (before == "0")
                {
                    if (!WriteDword(keyFull, "NoRemove", 0, out error)) { return false; }
                    return ReadDword(keyFull, "NoRemove") == "0";
                }
                error = "记录里原值看不懂（" + before + "）";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------ 删除条目

        /// <summary>把选中的条目从「程序和功能」列表里删掉：**先整条导出 .reg 备份 → 删子键 →
        /// 记删除记录**。程序本体不删、不跑卸载程序 —— 只是列表里不再有它（「仅用做移除」的
        /// 条目正合用）。删错了可以点「还原」把 .reg 导回。</summary>
        public static UninstallReport DeleteEntry(List<UninstallItem> targets)
        {
            UninstallReport rep = new UninstallReport();
            rep.Title = "删除条目";
            LoadRoot();

            List<UninstallItem> plan = new List<UninstallItem>();
            foreach (UninstallItem it in targets)
            {
                if (it == null) { continue; }
                if (it.SystemItem)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Name + " —— 系统组件，这一页不碰它");
                    continue;
                }
                plan.Add(it);
            }
            if (plan.Count == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要删除的条目";
                rep.Lines.Insert(0, "扫描根：" + RootText());
                return rep;
            }

            string delTsv = DeletedRecordFile();
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            List<string> delRecords = new List<string>();
            List<string> regBackups = new List<string>();
            foreach (UninstallItem it in plan)
            {
                string backup = Path.Combine(BackupDir(),
                    "uninstall-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + "-" + SafeName(it.Name) + ".reg");
                string error;
                if (!RegExport(it.KeyFull, backup, out error))
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Name + " —— 备份没成，所以这条没动：" + error);
                    continue;
                }
                regBackups.Add(backup);
                delRecords.Add(stamp + "\t" + it.Root + "\t" + it.KeyFull + "\t" + it.Name + "\t" + backup);
            }
            if (regBackups.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "备份全都没成，一个字都没删";
                rep.Lines.Insert(0, rep.Message);
                LastError = rep.Message;
                return rep;
            }

            try
            {
                bool fresh = !System.IO.File.Exists(delTsv);
                using (StreamWriter w = new StreamWriter(delTsv, true, new UTF8Encoding(false)))
                {
                    if (fresh) { foreach (string h in DelTsvHeader) { w.WriteLine(h); } }
                    foreach (string r in delRecords) { w.WriteLine(r); }
                    w.Flush();
                }
            }
            catch (Exception ex)
            {
                rep.Ok = false;
                rep.Message = "删除记录写不进去（" + delTsv + "），所以一个字都没删：" + ex.Message;
                rep.Lines.Insert(0, "× " + rep.Message);
                LastError = ex.Message;
                return rep;
            }

            rep.Lines.Insert(0, "");
            rep.Lines.Insert(0, "扫描根：" + RootText() + "；备份与删除记录在 " + delTsv);
            foreach (UninstallItem it in plan)
            {
                if (!DeletedFor(it.KeyFull, delRecords))
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + it.Name + " —— 备份阶段就没它的份");
                    continue;
                }
                string error;
                if (!DeleteKey(it.KeyFull, out error))
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Name + " —— 删了备份但键没删成：" + error);
                    if (LooksLikePermission(error)) { rep.NeedAdmin = true; }
                    continue;
                }
                if (KeyExists(it.KeyFull))
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + it.Name + " —— 删了但读回来还在");
                    continue;
                }
                rep.Changed++;
                rep.Lines.Add("√ " + it.Name + " —— 已从列表删除（备份 .reg 在，点「还原」可导回）");
            }
            rep.Ok = (rep.Failed == 0);
            rep.Message = (rep.Failed == 0)
                ? ("删除了 " + rep.Changed + " 条（跳过 " + rep.Skipped + " 条）")
                : ("删除 " + rep.Changed + " 条，" + rep.Failed + " 条没成");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message + "。程序本体没删、卸载程序也没跑，只是列表里没有它了；"
                + "备份在 " + BackupDir() + "，点「还原成操作前」按记录导回。");
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        private static bool DeletedFor(string keyFull, List<string> delRecords)
        {
            foreach (string r in delRecords)
            {
                string[] f = r.Split('\t');
                if (f.Length >= 3 && string.Equals(f[2], keyFull, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private static string SafeName(string name)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
                if (sb.Length >= 24) { break; }
            }
            if (sb.Length == 0) { sb.Append("entry"); }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ 还原（全部）

        /// <summary>按两份记录全部还原：改动记录（hide/lock）→ 值写回 / 删掉；删除记录 →
        /// .reg 导回注册表。退干净的记录行从文件里清掉。</summary>
        public static UninstallReport Restore()
        {
            UninstallReport rep = new UninstallReport();
            rep.Title = "还原";
            LoadRoot();

            string path = RecordFile();
            rep.BackupPath = path;
            List<string> lines = new List<string>();
            try
            {
                if (System.IO.File.Exists(path)) { lines.AddRange(System.IO.File.ReadAllLines(path, Encoding.UTF8)); }
            }
            catch (Exception ex)
            {
                rep.Ok = false;
                rep.Message = "读不到改动记录：" + ex.Message;
                rep.Lines.Add("× " + rep.Message);
                return rep;
            }

            List<string> keep = new List<string>();
            foreach (string raw in lines)
            {
                if (raw.Trim().Length == 0) { continue; }
                if (raw.StartsWith("#")) { continue; }
                string[] f = raw.Split('\t');
                if (f.Length < 6)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 跳过一行读不懂的记录：" + raw);
                    keep.Add(raw);
                    continue;
                }
                string keyFull = f[2];
                string action = f[4];
                string error;
                bool ok;
                if (action == "hide")
                {
                    ok = ShowOne(keyFull, f[5], out error);
                }
                else if (action == "lock")
                {
                    ok = RestoreLock(keyFull, f[6], out error);
                }
                else
                {
                    ok = false;
                    error = "记录里的动作看不懂（" + action + "）";
                }
                if (ok)
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + f[3] + " —— " + (action == "hide" ? "已显示回来" : "已解锁卸载"));
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没还原：" + f[3] + " —— " + error);
                    keep.Add(raw);
                }
            }

            try
            {
                if (keep.Count == 0)
                {
                    if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); }
                }
                else if (System.IO.File.Exists(path))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine(TsvHeader[0]);
                    sb.AppendLine("# （下面这几行是上次没还原成的，还留着）");
                    foreach (string k in keep) { sb.AppendLine(k); }
                    System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                rep.Lines.Add("· 改动记录收尾没成：" + ex.Message);
            }

            // 删除记录：.reg 导回
            string delPath = DeletedRecordFile();
            List<string> delKeep = new List<string>();
            List<string> delLines = new List<string>();
            try
            {
                if (System.IO.File.Exists(delPath)) { delLines.AddRange(System.IO.File.ReadAllLines(delPath, Encoding.UTF8)); }
            }
            catch (Exception ex)
            {
                rep.Lines.Add("· 读不到删除记录：" + ex.Message);
            }
            foreach (string raw in delLines)
            {
                if (raw.Trim().Length == 0) { continue; }
                if (raw.StartsWith("#")) { continue; }
                string[] f = raw.Split('\t');
                if (f.Length < 5)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 跳过一行读不懂的删除记录：" + raw);
                    delKeep.Add(raw);
                    continue;
                }
                string backup = f[4];
                string error;
                if (RestoreDeleted(backup, out error))
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + f[3] + " —— 已导回注册表（列表里会重新出现这条）");
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没导回：" + f[3] + " —— " + error + "（备份在 " + backup + "）");
                    delKeep.Add(raw);
                }
            }
            try
            {
                if (delKeep.Count == 0)
                {
                    if (System.IO.File.Exists(delPath)) { System.IO.File.Delete(delPath); }
                }
                else if (System.IO.File.Exists(delPath))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine(DelTsvHeader[0]);
                    sb.AppendLine("# （下面这几行是上次没导回的，还留着）");
                    foreach (string k in delKeep) { sb.AppendLine(k); }
                    System.IO.File.WriteAllText(delPath, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                rep.Lines.Add("· 删除记录收尾没成：" + ex.Message);
            }

            if (rep.Changed == 0 && rep.Skipped == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要还原的记录（这台机器还没用工具箱动过「程序和功能」列表）";
                rep.Lines.Insert(0, rep.Message);
                return rep;
            }
            rep.Ok = (rep.Skipped == 0);
            rep.Message = (rep.Skipped == 0)
                ? ("还原了 " + rep.Changed + " 条，记录已清空")
                : ("还原了 " + rep.Changed + " 条，" + rep.Skipped + " 条没成（原因在每一行后面）");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message);
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        /// <summary>单条「还原更改前」：把这一条的工具箱改动（隐藏 hide 记录 / 锁定 lock 记录 /
        /// 删除的 .reg 备份）全部按记录还原；别条的记录原样留着。只还原**工具箱动过**的 ——
        /// 本来就这样 / 别的工具改的不碰。右键菜单那条「还原更改前…」就是它。</summary>
        public static UninstallReport RestoreOne(UninstallItem target)
        {
            UninstallReport rep = new UninstallReport();
            rep.Title = "还原更改前";
            LoadRoot();
            if (target == null)
            {
                rep.Ok = true;
                rep.Message = "没有选中条目";
                return rep;
            }

            string path = RecordFile();
            rep.BackupPath = path;
            List<string> lines = new List<string>();
            try
            {
                if (System.IO.File.Exists(path)) { lines.AddRange(System.IO.File.ReadAllLines(path, Encoding.UTF8)); }
            }
            catch (Exception ex)
            {
                rep.Ok = false;
                rep.Message = "读不到改动记录：" + ex.Message;
                rep.Lines.Add("× " + rep.Message);
                return rep;
            }

            List<string> keep = new List<string>();
            foreach (string raw in lines)
            {
                if (raw.Trim().Length == 0) { continue; }
                if (raw.StartsWith("#")) { continue; }
                string[] f = raw.Split('\t');
                if (f.Length < 6)
                {
                    keep.Add(raw);
                    continue;
                }
                if (!string.Equals(f[2], target.KeyFull, StringComparison.OrdinalIgnoreCase)) { keep.Add(raw); continue; }
                string action = f[4];
                string error;
                bool ok;
                if (action == "hide")
                {
                    ok = ShowOne(f[2], f[5], out error);
                }
                else if (action == "lock")
                {
                    ok = RestoreLock(f[2], f[6], out error);
                }
                else
                {
                    ok = false;
                    error = "记录里的动作看不懂（" + action + "）";
                }
                if (ok)
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + f[3] + " —— " + (action == "hide" ? "已显示回来" : "已解锁卸载"));
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没还原：" + f[3] + " —— " + error);
                    keep.Add(raw);
                }
            }

            try
            {
                if (keep.Count == 0)
                {
                    if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); }
                }
                else if (System.IO.File.Exists(path))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine(TsvHeader[0]);
                    sb.AppendLine("# （下面这几行是上次没还原成的，还留着）");
                    foreach (string k in keep) { sb.AppendLine(k); }
                    System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                rep.Lines.Add("· 改动记录收尾没成：" + ex.Message);
            }

            // 删除记录：只导回这一条的 .reg
            string delPath = DeletedRecordFile();
            List<string> delKeep = new List<string>();
            List<string> delLines = new List<string>();
            try
            {
                if (System.IO.File.Exists(delPath)) { delLines.AddRange(System.IO.File.ReadAllLines(delPath, Encoding.UTF8)); }
            }
            catch (Exception ex)
            {
                rep.Lines.Add("· 读不到删除记录：" + ex.Message);
            }
            foreach (string raw in delLines)
            {
                if (raw.Trim().Length == 0) { continue; }
                if (raw.StartsWith("#")) { continue; }
                string[] f = raw.Split('\t');
                if (f.Length < 5)
                {
                    delKeep.Add(raw);
                    continue;
                }
                if (!string.Equals(f[2], target.KeyFull, StringComparison.OrdinalIgnoreCase)) { delKeep.Add(raw); continue; }
                string error;
                if (RestoreDeleted(f[4], out error))
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + f[3] + " —— 已导回注册表（列表里会重新出现这条）");
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没导回：" + f[3] + " —— " + error + "（备份在 " + f[4] + "）");
                    delKeep.Add(raw);
                }
            }
            try
            {
                if (delKeep.Count == 0)
                {
                    if (System.IO.File.Exists(delPath)) { System.IO.File.Delete(delPath); }
                }
                else if (System.IO.File.Exists(delPath))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine(DelTsvHeader[0]);
                    sb.AppendLine("# （下面这几行是上次没导回的，还留着）");
                    foreach (string k in delKeep) { sb.AppendLine(k); }
                    System.IO.File.WriteAllText(delPath, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                rep.Lines.Add("· 删除记录收尾没成：" + ex.Message);
            }

            if (rep.Changed == 0 && rep.Skipped == 0)
            {
                rep.Ok = true;
                rep.Message = "这条没有需要还原的记录（工具箱还没动过它 / 改动已经还原过了）";
                rep.Lines.Insert(0, rep.Message);
                return rep;
            }
            rep.Ok = (rep.Skipped == 0);
            rep.Message = (rep.Skipped == 0)
                ? ("还原了这条的 " + rep.Changed + " 项改动，记录已清掉")
                : ("还原了 " + rep.Changed + " 项，" + rep.Skipped + " 项没成（原因在每一行后面）");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message);
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        /// <summary>还原一条 lock：原值 absent → 删 NoRemove；原值 1 → 保持（写 1 等于没变，算成）。
        /// 写完读回核对。</summary>
        private static bool RestoreLock(string keyFull, string before, out string error)
        {
            error = "";
            try
            {
                if (before == "absent")
                {
                    if (!DeleteValue(keyFull, "NoRemove", out error)) { return false; }
                    return ReadDword(keyFull, "NoRemove") == "absent";
                }
                if (before == "1")
                {
                    if (!WriteDword(keyFull, "NoRemove", 1, out error)) { return false; }
                    return ReadDword(keyFull, "NoRemove") == "1";
                }
                error = "记录里原值看不懂（" + before + "）";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool RestoreDeleted(string backup, out string error)
        {
            error = "";
            try
            {
                if (!System.IO.File.Exists(backup))
                {
                    error = "备份 .reg 文件不在了（" + backup + "）";
                    return false;
                }
                System.Diagnostics.Process p = new System.Diagnostics.Process();
                p.StartInfo.FileName = "reg.exe";
                p.StartInfo.Arguments = "import \"" + backup + "\"";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.Start();
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    error = "reg import 返回 " + p.ExitCode.ToString(CultureInfo.InvariantCulture);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------ 注册表小工具

        private static string FullRegKey(string keyFull)
        {
            // 「HKLM\SOFTWARE\…」→ 「HKEY_LOCAL_MACHINE\SOFTWARE\…」（reg.exe 认的完整 hive 名）
            if (keyFull.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            {
                return "HKEY_LOCAL_MACHINE\\" + keyFull.Substring(5);
            }
            if (keyFull.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            {
                return "HKEY_CURRENT_USER\\" + keyFull.Substring(5);
            }
            return keyFull;
        }

        /// <summary>导出整条键成 .reg（reg.exe export，/y 覆盖）。</summary>
        private static bool RegExport(string keyFull, string backup, out string error)
        {
            error = "";
            try
            {
                System.Diagnostics.Process p = new System.Diagnostics.Process();
                p.StartInfo.FileName = "reg.exe";
                p.StartInfo.Arguments = "export \"" + FullRegKey(keyFull) + "\" \"" + backup + "\" /y";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.Start();
                p.WaitForExit();
                if (p.ExitCode != 0 || !System.IO.File.Exists(backup))
                {
                    error = "reg export 返回 " + p.ExitCode.ToString(CultureInfo.InvariantCulture);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool DeleteKey(string keyFull, out string error)
        {
            error = "";
            int cut = keyFull.IndexOf('\\');
            string hive = (cut < 0) ? keyFull : keyFull.Substring(0, cut);
            string rel = (cut < 0) ? "" : keyFull.Substring(cut + 1);
            RegistryKey baseKey = null;
            RegistryKey parent = null;
            try
            {
                baseKey = HiveByName(hive);
                int last = rel.LastIndexOf('\\');
                string parentPath = (last < 0) ? "" : rel.Substring(0, last);
                string leaf = (last < 0) ? rel : rel.Substring(last + 1);
                parent = baseKey.OpenSubKey(parentPath, true);
                if (parent == null) { error = "打不开上一层键"; return false; }
                parent.DeleteSubKeyTree(leaf, false);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (parent != null) { parent.Dispose(); }
                if (baseKey != null) { baseKey.Dispose(); }
            }
        }

        private static bool KeyExists(string keyFull)
        {
            int cut = keyFull.IndexOf('\\');
            string hive = (cut < 0) ? keyFull : keyFull.Substring(0, cut);
            string rel = (cut < 0) ? "" : keyFull.Substring(cut + 1);
            RegistryKey baseKey = null;
            try
            {
                baseKey = HiveByName(hive);
                return baseKey.OpenSubKey(rel, false) != null;
            }
            catch { return true; }
            finally { if (baseKey != null) { baseKey.Dispose(); } }
        }

        private static bool WriteDword(string keyFull, string valueName, int value, out string error)
        {
            error = "";
            int cut = keyFull.IndexOf('\\');
            string hive = (cut < 0) ? keyFull : keyFull.Substring(0, cut);
            string rel = (cut < 0) ? "" : keyFull.Substring(cut + 1);
            RegistryKey baseKey = null;
            RegistryKey k = null;
            try
            {
                baseKey = HiveByName(hive);
                k = baseKey.OpenSubKey(rel, true);
                if (k == null) { error = "打不开这个键（可能已经被删了）"; return false; }
                k.SetValue(valueName, value, RegistryValueKind.DWord);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (k != null) { k.Dispose(); }
                if (baseKey != null) { baseKey.Dispose(); }
            }
        }

        private static bool DeleteValue(string keyFull, string valueName, out string error)
        {
            error = "";
            int cut = keyFull.IndexOf('\\');
            string hive = (cut < 0) ? keyFull : keyFull.Substring(0, cut);
            string rel = (cut < 0) ? "" : keyFull.Substring(cut + 1);
            RegistryKey baseKey = null;
            RegistryKey k = null;
            try
            {
                baseKey = HiveByName(hive);
                k = baseKey.OpenSubKey(rel, true);
                if (k == null) { error = "打不开这个键（可能已经被删了）"; return false; }
                if (k.GetValue(valueName, null) != null) { k.DeleteValue(valueName, false); }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (k != null) { k.Dispose(); }
                if (baseKey != null) { baseKey.Dispose(); }
            }
        }

        /// <summary>读一个 DWORD：值不在 → "absent"；在 → 数字字符串。读不到 → "absent"（保守：按没有处理）。</summary>
        private static string ReadDword(string keyFull, string valueName)
        {
            int cut = keyFull.IndexOf('\\');
            string hive = (cut < 0) ? keyFull : keyFull.Substring(0, cut);
            string rel = (cut < 0) ? "" : keyFull.Substring(cut + 1);
            RegistryKey baseKey = null;
            RegistryKey k = null;
            try
            {
                baseKey = HiveByName(hive);
                k = baseKey.OpenSubKey(rel, false);
                if (k == null) { return "absent"; }
                object v = k.GetValue(valueName, null);
                if (v == null) { return "absent"; }
                return Convert.ToInt32(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            }
            catch { return "absent"; }
            finally
            {
                if (k != null) { k.Dispose(); }
                if (baseKey != null) { baseKey.Dispose(); }
            }
        }

        // ------------------------------------------------------------------ 文字

        private static string Text(object raw)
        {
            if (raw == null) { return ""; }
            if (raw is string) { return (string)raw; }
            if (raw is string[]) { return string.Join("|", (string[])raw); }
            if (raw is byte[]) { return Convert.ToBase64String((byte[])raw); }
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
        public static string ReportText(List<UninstallItem> items)
        {
            LoadRoot();
            StringBuilder sb = new StringBuilder();
            int sys = 0, hidden = 0, shown = 0, locked = 0;
            sb.AppendLine("程序和功能管理 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("控制面板「程序和功能」/ 设置「应用」列表 = 注册表里这三根 Uninstall（一根下一个子键 = 一个已装程序）：");
            sb.AppendLine("  " + _hklm64Hive + "\\" + _hklm64Path + "（64 位程序的卸载登记）");
            sb.AppendLine("  " + _hklm32Hive + "\\" + _hklm32Path + "（32 位程序的卸载登记）");
            sb.AppendLine("  " + _hkcuHive + "\\" + _hkcuPath + "（当前用户的卸载登记）");
            if (_isolated) { sb.AppendLine("⚠️ 测试隔离根：这里改的不是真的「程序和功能」列表（上面三行是隔离路径）。"); }
            sb.AppendLine();
            sb.AppendLine("记号：◇ 显示中（可隐藏 / 可锁定）· ● 已隐藏（工具箱藏的，可显示）· ○ 系统组件 / 别的工具藏的（只列不动）");
            sb.AppendLine();

            foreach (UninstallItem it in items)
            {
                if (it.SystemItem) { sys++; } else if (it.OurHidden) { hidden++; } else { shown++; }
                if (it.LockedUninstall && !it.SystemItem) { locked++; }
                string mark = it.SystemItem ? "○" : (it.OurHidden ? "●" : "◇");
                sb.Append("  ").Append(mark).Append(' ').Append(it.Name)
                    .Append("  [").Append(it.Root).Append("]");
                if (it.Publisher.Length > 0) { sb.Append("  · ").Append(it.Publisher); }
                if (it.InstallDate.Length > 0) { sb.Append("  · 装于 ").Append(it.InstallDate); }
                if (it.LockedUninstall) { sb.Append("  · 已锁定卸载"); }
                sb.AppendLine();
            }

            sb.AppendLine();
            int pending = PendingRecords();
            sb.AppendLine("汇总：共 " + items.Count + " 条 —— 显示中 " + shown + " 条、已隐藏 " + hidden + " 条、"
                + "系统组件 " + sys + " 条（只列不动）"
                + (pending > 0 ? ("；有 " + pending + " 条改动/删除记录还没还原") : "；没有待还原的记录"));
            if (shown > 0)
            {
                sb.AppendLine("结论：选中一条点「隐藏这个…」（或右键）把它从列表藏起来（SystemComponent=1，程序本体不删、照常能用）；"
                    + "「锁定卸载」把卸载按钮变灰防手滑；右键「删除条目…」先从列表里摘掉（先备份 .reg、可导回）。");
            }
            else
            {
                sb.AppendLine("结论：没有显示中的第三方条目可以动。");
            }
            return sb.ToString();
        }

        /// <summary>命令行 `uninstall --json` 用的机器可读输出。</summary>
        public static string JsonReport(List<UninstallItem> items)
        {
            LoadRoot();
            StringBuilder sb = new StringBuilder();
            int sys = 0, hidden = 0, shown = 0, locked = 0;
            foreach (UninstallItem it in items)
            {
                if (it.SystemItem) { sys++; } else if (it.OurHidden) { hidden++; } else { shown++; }
                if (it.LockedUninstall && !it.SystemItem) { locked++; }
            }
            sb.Append("{\"root\":").Append(JsonText(RootText()));
            sb.Append(",\"isolated\":").Append(Isolated ? "true" : "false");
            sb.Append(",\"count\":").Append(items.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"shown\":").Append(shown.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"hidden\":").Append(hidden.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"system\":").Append(sys.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"locked\":").Append(locked.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"pending\":").Append(PendingRecords().ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"items\":[");
            for (int i = 0; i < items.Count; i++)
            {
                UninstallItem it = items[i];
                if (i > 0) { sb.Append(","); }
                sb.Append("{\"name\":").Append(JsonText(it.Name));
                sb.Append(",\"root\":").Append(JsonText(it.Root));
                sb.Append(",\"key\":").Append(JsonText(it.KeyFull));
                sb.Append(",\"publisher\":").Append(JsonText(it.Publisher));
                sb.Append(",\"installDate\":").Append(JsonText(it.InstallDate));
                sb.Append(",\"icon\":").Append(JsonText(it.DisplayIcon));
                sb.Append(",\"system\":").Append(it.SystemItem ? "true" : "false");
                sb.Append(",\"hidden\":").Append(it.OurHidden ? "true" : "false");
                sb.Append(",\"locked\":").Append(it.LockedUninstall ? "true" : "false");
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
