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
    /// <summary>「此电脑 → 设备和驱动器」里的一个**非磁盘**图标（2026-10-26 用户报：装完百度网盘，
    /// 「此电脑」里多了一个「双击运行百度网盘」的图标）。
    ///
    /// 量出来的机制：这类图标不是磁盘，是软件安装时往注册表里写的**挂载点** ——
    /// `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{GUID}`
    /// （每用户）和 `HKLM\SOFTWARE\…\Explorer\MyComputer\NameSpace\{GUID}`（这台机器所有用户）。
    /// 资源管理器打开「此电脑」时枚举这两个键，每个 GUID 去 CLSID 键里读显示名（默认值）、
    /// 图标（DefaultIcon）和双击命令（Shell\Open\Command），于是「设备和驱动器」里就多出一个。
    /// 本机实测：百度网盘 = `{679F137C-3162-45da-BE3C-2F9C3D093F64}`，HKCU 和 HKLM 两处都有。
    ///
    /// 所以「移除」= 只删 NameSpace 里的那个挂载子键（HKCU + HKLM 同名 GUID 两处一起删），
    /// **绝不碰 CLSID 键、绝不碰软件本身** —— 图标从「此电脑」消失，软件照常能用；
    /// 「此电脑」每次打开都重新枚举，**即时生效、不用重启资源管理器**。
    /// ⚠️ 软件重启 / 重装可能把挂载点写回来（特性不是 bug），报告里会指路去软件设置关。
    ///
    /// 五条口径（和「文件关联体检」「启动项管理」一个规矩）：
    /// ① **系统自带的挂载点只报告、不给删**：内置一份本机实测的系统 GUID 名单
    ///    （Win10 的 MyComputer\NameSpace 默认条目），命中就标「系统」；
    /// ② **写前先记原值**（`pc-icons-original.tsv`：时间、GUID、当时挂在哪、两个完整键路径、
    ///    (默认)值和类型）—— **记不下来就一个字都不改**；
    /// ③ **删完逐处读回核对**，核对不一致就报出来，绝不说成「已移除」；
    /// ④ **能还原**：「还原」按记录把挂载子键重建回去；如果那个键后来被别人（软件重装）加回来了
    ///    **跳过并在报告里说出来**；
    /// ⑤ 真实磁盘（C / D / E 那些）**不是 NameSpace 条目**，永远不会出现在列表里，不会误删。
    ///
    /// ⚠️ 测试隔离：`MXX1_PCICONS_HKCU` / `MXX1_PCICONS_HKLM`（把两处「根」换成隔离根 —— 值可以是
    /// 任意 hive 的完整路径，测试两个都指到 HKCU 下面就不需要管理员；读 / 写 / 还原全在隔离根里跑，
    /// 一个字节都不碰真实的 MyComputer\NameSpace）、`MXX1_PCICONS_TSV`（记录文件挪走）。
    /// **写入口（移除 / 还原）只在窗口里点、或者在隔离根下走**（命令行的 `pcicons` 默认只读）。</summary>
    internal sealed class PcIconItem
    {
        public string Guid = "";          // NameSpace 下的子键名，如 {679F137C-3162-45da-BE3C-2F9C3D093F64}
        public string Name = "";          // CLSID 的(默认)值 = 显示名（HKCU 优先、HKLM 兜底）
        public string Icon = "";          // CLSID\DefaultIcon 的(默认)值
        public string Command = "";       // CLSID\Shell\Open\Command 的(默认)值 = 双击干什么
        public bool InHkcu;               // 挂载点在 HKCU 那层
        public bool InHklm;               // 挂载点在 HKLM 那层
        public bool SystemItem;           // 在系统名单里 → 只报告、不给删
        public string Kind = "unknown";   // system（系统自带）/ third（有显示名，第三方）/ unknown（有挂载点但没名字）

        public string SourceText() { return InHkcu && InHklm ? "both" : (InHkcu ? "HKCU" : "HKLM"); }
        public bool Removable { get { return !SystemItem; } }
    }

    /// <summary>一次「移除」/「还原」的结果（报告行 + 计数）。</summary>
    internal sealed class PcIconsReport
    {
        public bool Ok = true;
        public string Title = "";                  // 「移除」/「还原」
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

    /// <summary>「此电脑多余图标」的后端。见上面 PcIconItem 的注释（2026-10-26 用户报的那个
    /// 「百度网盘」图标就是从这两个挂载点来的）。</summary>
    internal static class PcIcons
    {
        public static string LastError = "";

        private static readonly string[] TsvHeader = new string[]
        {
            "# mxx1 工具箱 · 「此电脑多余图标」移除前的记录（「还原」就照这份把挂载子键重建回去；别手改，改坏了那一行会被跳过）",
            "# 时间\tGUID\t当时挂在哪(both = 用户+系统两层都有 / HKCU / HKLM)\tHKCU完整键\tHKLM完整键\t当时(默认)值\t类型"
        };

        // ------------------------------------------------------------------ 系统名单

        /// <summary>系统自带的 MyComputer\NameSpace 挂载点（**本机 Win10 实测**，2026-10-26，
        /// HKLM 那层默认就有这些；另有 {088e3905-…} = 网络）。命中 = 只报告、不给删。
        /// 注意 {679F137C-…}（百度网盘）**不在**名单里 —— 它虽在 HKLM 有同名挂载，却是软件写的。
        /// 别的机器可能多几个系统条目，测试判据不依赖这份名单的完整性（只看 679F137C 认出第三方）。</summary>
        private static readonly string[] SystemGuidList = new string[]
        {
            // DelegateFolders 不是 CLSID GUID，是 Windows 聚合「设备和驱动器」的特殊键名 —— 必须算系统
            "DelegateFolders",
            "{088E3905-0323-4B02-9826-5D99428E115F}",   // 网络
            "{1CF1260C-4DD0-4EBB-811F-33C572699FDE}",
            "{24AD3AD4-A569-4530-98E1-AB02F9417AA8}",
            "{374DE290-123F-4565-9164-39C4925E467B}",
            "{3ADD1653-EB32-4CB0-BBD7-DFA0ABB5ACCA}",
            "{3DFDF296-DBEC-4FB4-81D1-6A3438BCF4DE}",
            "{A0953C92-50DC-43BF-BE83-3742FED03C9C}",
            "{A8CDFF1C-4878-43BE-B5FD-F8091C1C60D0}",
            "{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}",
            "{D3162B92-9365-467A-956B-92703ACA08AF}",
            "{F86FA3AB-70D2-4FC7-9C99-FCBF05467F3A}"
        };

        private static HashSet<string> _systemSet = null;
        private static HashSet<string> SystemSet()
        {
            if (_systemSet == null)
            {
                _systemSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string g in SystemGuidList) { _systemSet.Add(g); }
            }
            return _systemSet;
        }

        // ------------------------------------------------------------------ 根

        private static string _hkcuHive = "HKCU";
        private static string _hkcuPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace";
        private static string _hklmHive = "HKLM";
        private static string _hklmPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace";
        private static bool _isolated;

        /// <summary>「根」= 两处 NameSpace：默认每用户那层 + 这台机器所有用户那层。
        /// 测试用 `MXX1_PCICONS_HKCU` / `MXX1_PCICONS_HKLM` 换成隔离根 —— 值可以是任意 hive 的
        /// 完整路径（测试两个都指到 HKCU 下面就不用管理员）。</summary>
        private static void LoadRoot()
        {
            _hkcuHive = "HKCU";
            _hkcuPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace";
            _hklmHive = "HKLM";
            _hklmPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace";
            _isolated = false;
            string hkcu = (Environment.GetEnvironmentVariable("MXX1_PCICONS_HKCU") ?? "").Trim().TrimEnd('\\');
            string hklm = (Environment.GetEnvironmentVariable("MXX1_PCICONS_HKLM") ?? "").Trim().TrimEnd('\\');
            if (hkcu.Length > 0) { ParseRoot(hkcu, ref _hkcuHive, ref _hkcuPath); }
            if (hklm.Length > 0) { ParseRoot(hklm, ref _hklmHive, ref _hklmPath); }
            if (hkcu.Length > 0 && hklm.Length > 0) { _isolated = true; }
        }

        private static void ParseRoot(string value, ref string hive, ref string path)
        {
            int i = value.IndexOf('\\');
            hive = HiveShort((i < 0) ? value : value.Substring(0, i));
            path = (i < 0) ? "" : value.Substring(i + 1);
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

        /// <summary>给界面 / 报告用的一句话：「HKCU\… | HKLM\…」。</summary>
        public static string RootText()
        {
            LoadRoot();
            return _hkcuHive + "\\" + _hkcuPath + " | " + _hklmHive + "\\" + _hklmPath;
        }

        /// <summary>写的那条路（移除 / 还原）**只允许在两个隔离根都设好的情况下走**（命令行出口靠它拦）。
        /// 界面里那个窗口不靠这句判断 —— 它自己会过条款门 + 确认框 + 要管理员。</summary>
        public static bool WriteAllowed() { return Isolated; }

        // ------------------------------------------------------------------ 记录文件

        public static string RecordFile()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_PCICONS_TSV") ?? "").Trim();
            if (env.Length > 0) { return env; }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } } catch { }
            return Path.Combine(dir, "pc-icons-original.tsv");
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

        // ------------------------------------------------------------------ 扫描

        /// <summary>只读扫一遍两处 NameSpace，把每一个挂载点解析成一行报告。
        /// CLSID 的显示名 / 图标 / 双击命令**永远从真实注册表读**（只读、无害），隔离根只管 NameSpace。</summary>
        public static List<PcIconItem> Scan()
        {
            LoadRoot();
            Dictionary<string, PcIconItem> map = new Dictionary<string, PcIconItem>(StringComparer.OrdinalIgnoreCase);
            AddFrom(map, _hkcuHive, _hkcuPath, true);
            AddFrom(map, _hklmHive, _hklmPath, false);

            List<PcIconItem> list = new List<PcIconItem>(map.Values);
            // 有名字的排前面（一眼认出是谁），名字空的（系统条目 / 来历不明）排后面，再按 GUID 兜底
            list.Sort(delegate (PcIconItem a, PcIconItem b)
            {
                bool an = a.Name.Length > 0, bn = b.Name.Length > 0;
                if (an != bn) { return an ? -1 : 1; }
                int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (c != 0) { return c; }
                return string.Compare(a.Guid, b.Guid, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static void AddFrom(Dictionary<string, PcIconItem> map, string hive, string path, bool isHkcu)
        {
            RegistryKey baseKey = null;
            RegistryKey root = null;
            try
            {
                baseKey = HiveByName(hive);
                root = baseKey.OpenSubKey(path, false);
                if (root == null) { return; }
                foreach (string sub in root.GetSubKeyNames())
                {
                    PcIconItem it;
                    if (!map.TryGetValue(sub, out it))
                    {
                        it = new PcIconItem();
                        it.Guid = sub;
                        ReadClsid(sub, out it.Name, out it.Icon, out it.Command);
                        it.SystemItem = SystemSet().Contains(sub);
                        it.Kind = it.SystemItem ? "system" : (it.Name.Length > 0 ? "third" : "unknown");
                        map[sub] = it;
                    }
                    if (isHkcu) { it.InHkcu = true; } else { it.InHklm = true; }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                if (root != null) { root.Dispose(); }
                if (baseKey != null) { baseKey.Dispose(); }
            }
        }

        /// <summary>从 CLSID 键读显示名 / 图标 / 双击命令（HKCU\Software\Classes 优先、HKLM 兜底）。
        /// 只读；隔离根下照常读真实 CLSID —— 这一层本来就一个字都不改。</summary>
        private static void ReadClsid(string guid, out string name, out string icon, out string command)
        {
            name = ""; icon = ""; command = "";
            string sub = @"Software\Classes\CLSID\" + guid;
            ClsidValue(Registry.CurrentUser, sub, "", ref name);
            if (name.Length == 0) { ClsidValue(Registry.LocalMachine, @"SOFTWARE\Classes\CLSID\" + guid, "", ref name); }
            ClsidValue(Registry.CurrentUser, sub + @"\DefaultIcon", "", ref icon);
            if (icon.Length == 0) { ClsidValue(Registry.LocalMachine, @"SOFTWARE\Classes\CLSID\" + guid + @"\DefaultIcon", "", ref icon); }
            ClsidValue(Registry.CurrentUser, sub + @"\Shell\Open\Command", "", ref command);
            if (command.Length == 0) { ClsidValue(Registry.LocalMachine, @"SOFTWARE\Classes\CLSID\" + guid + @"\Shell\Open\Command", "", ref command); }
        }

        private static void ClsidValue(RegistryKey baseKey, string path, string valueName, ref string target)
        {
            RegistryKey k = null;
            try
            {
                k = baseKey.OpenSubKey(path, false);
                if (k == null) { return; }
                object raw = k.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                string text = Text(raw);
                if (text.Length > 0) { target = text; }
            }
            catch { }
            finally { if (k != null) { k.Dispose(); } }
        }

        // ------------------------------------------------------------------ 移除

        /// <summary>把选中的挂载点移除：**记原值 → 删（HKCU + HKLM 同名 GUID 两处一起）→ 逐处读回核对**。
        /// 只删 NameSpace 里的挂载子键，绝不碰 CLSID、绝不碰软件本身。</summary>
        public static PcIconsReport Remove(List<PcIconItem> targets)
        {
            PcIconsReport rep = new PcIconsReport();
            rep.Title = "移除";
            LoadRoot();

            List<PcIconItem> plan = new List<PcIconItem>();
            foreach (PcIconItem it in targets)
            {
                if (it == null) { continue; }
                if (!it.Removable)
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没动：" + Label(it) + " —— 系统自带的挂载点，这一页不会删它");
                    continue;
                }
                plan.Add(it);
            }
            if (plan.Count == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要移除的第三方图标";
                rep.Lines.Insert(0, "扫描根：" + RootText() + "（系统条目只报告，一个都不会动）");
                return rep;
            }

            // ① 把原值写成记录（**记不下来就一个字都不改**）
            string path = RecordFile();
            rep.BackupPath = path;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            List<string> records = new List<string>();
            foreach (PcIconItem it in plan)
            {
                records.Add(stamp + "\t" + it.Guid + "\t" + it.SourceText() + "\t"
                    + (it.InHkcu ? FullKey(_hkcuHive, _hkcuPath, it.Guid) : "-") + "\t"
                    + (it.InHklm ? FullKey(_hklmHive, _hklmPath, it.Guid) : "-") + "\t"
                    + (it.Name ?? "") + "\tString");
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

            // ② 删 + 读回核对
            rep.Lines.Insert(0, "");
            rep.Lines.Insert(0, "扫描根：" + RootText() + "；原值已记进 " + path);
            foreach (PcIconItem it in plan)
            {
                string error;
                if (!RemoveOne(it, out error))
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + Label(it) + " —— 没删掉：" + error);
                    if (LooksLikePermission(error)) { rep.NeedAdmin = true; }
                    continue;
                }
                if (GoneEverywhere(it))
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + Label(it) + " —— 已移除（两处都读回核对过：挂载点不在了）");
                }
                else
                {
                    rep.Failed++;
                    rep.Lines.Add("× " + Label(it) + " —— 删了但读回来还有挂载点");
                }
            }
            rep.Ok = (rep.Failed == 0);
            rep.Message = (rep.Failed == 0)
                ? ("移除了 " + rep.Changed + " 个（跳过 " + rep.Skipped + " 个）")
                : ("移除 " + rep.Changed + " 个，" + rep.Failed + " 个没成");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message + "。原值记在 " + path + "，点「还原成移除前」可以原样放回去。");
            rep.Lines.Add("只删了「此电脑」里的挂载点，软件本身没动；软件重启 / 重装可能把图标写回来（去软件设置里关）。");
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        private static string FullKey(string hive, string path, string guid)
        {
            return hive + "\\" + path + "\\" + guid;
        }

        private static string Label(PcIconItem it)
        {
            string name = it.Name.Length > 0 ? it.Name : "（来历不明）";
            return name + " " + it.Guid + "（挂在 " + it.SourceText() + "）";
        }

        /// <summary>删掉一个 GUID 挂载点（挂在哪几层就删哪几层），全删掉才算成功。</summary>
        private static bool RemoveOne(PcIconItem it, out string error)
        {
            error = "";
            RegistryKey baseKey = null;
            RegistryKey root = null;
            try
            {
                if (it.InHkcu)
                {
                    baseKey = HiveByName(_hkcuHive);
                    root = baseKey.OpenSubKey(_hkcuPath, true);
                    if (root == null) { error = "打不开 HKCU 那层根键"; return false; }
                    root.DeleteSubKeyTree(it.Guid, false);
                    if (root.OpenSubKey(it.Guid, false) != null) { error = "HKCU 那层删了但还在"; return false; }
                    root.Dispose(); root = null;
                    baseKey.Dispose(); baseKey = null;
                }
                if (it.InHklm)
                {
                    baseKey = HiveByName(_hklmHive);
                    root = baseKey.OpenSubKey(_hklmPath, true);
                    if (root == null) { error = "打不开 HKLM 那层根键"; return false; }
                    root.DeleteSubKeyTree(it.Guid, false);
                    if (root.OpenSubKey(it.Guid, false) != null) { error = "HKLM 那层删了但还在"; return false; }
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

        /// <summary>它原来挂过的每一层现在都读不到这个挂载点了（RemoveOne 已经逐层核对过，
        /// 这里再整体复核一遍，给报告用）。</summary>
        private static bool GoneEverywhere(PcIconItem it)
        {
            return !HasMount(it.InHkcu, _hkcuHive, _hkcuPath, it.Guid)
                && !HasMount(it.InHklm, _hklmHive, _hklmPath, it.Guid);
        }

        private static bool HasMount(bool enabled, string hive, string path, string guid)
        {
            if (!enabled) { return false; }
            RegistryKey baseKey = null;
            RegistryKey root = null;
            try
            {
                baseKey = HiveByName(hive);
                root = baseKey.OpenSubKey(path, false);
                return root != null && root.OpenSubKey(guid, false) != null;
            }
            catch { return true; }
            finally
            {
                if (root != null) { root.Dispose(); }
                if (baseKey != null) { baseKey.Dispose(); }
            }
        }

        // ------------------------------------------------------------------ 还原

        /// <summary>按记录把挂载子键重建回去（来源写哪层就建哪层）：原值记的是「这个当时挂在
        /// both / HKCU / HKLM」，还原就照着建。现在这个键已经在（软件重装加回来了）→ 跳过并说明。</summary>
        public static PcIconsReport Restore()
        {
            PcIconsReport rep = new PcIconsReport();
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
                string guid = f[1];
                string source = f[2];
                string hkcuFull = f[3];
                string hklmFull = f[4];
                string defaultValue = f[5];

                string note;
                bool ok = RestoreOne(guid, source, hkcuFull, hklmFull, defaultValue, out note);
                if (ok)
                {
                    rep.Changed++;
                    rep.Lines.Add("√ " + guid + " —— " + (note.Length > 0 ? note : "挂载点已放回去（读回核对在）"));
                }
                else
                {
                    rep.Skipped++;
                    rep.Lines.Add("· 没放回：" + guid + " —— " + note);
                    keep.Add(raw);
                }
            }

            if (rep.Changed == 0 && rep.Skipped == 0)
            {
                rep.Ok = true;
                rep.Message = "没有需要还原的记录（这台机器还没用工具箱移除过「此电脑」图标）";
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
                ? ("还原了 " + rep.Changed + " 个，记录已清空")
                : ("还原了 " + rep.Changed + " 个，" + rep.Skipped + " 个没放回（原因在每一行后面）");
            rep.Lines.Add("");
            rep.Lines.Add("汇总：" + rep.Message);
            LastError = rep.Ok ? "" : rep.Message;
            return rep;
        }

        /// <summary>还原一个：原来挂在哪层就重建哪层的挂载子键（带当时的(默认)值），读回核对在。
        /// 现在这个键已经在 → 跳过（软件可能重装加回来了，别覆盖它）。</summary>
        private static bool RestoreOne(string guid, string source, string hkcuFull, string hklmFull,
            string defaultValue, out string note)
        {
            note = "";
            bool wantHkcu = (source == "both" || source == "HKCU") && hkcuFull != "-" && hkcuFull.Length > 0;
            bool wantHklm = (source == "both" || source == "HKLM") && hklmFull != "-" && hklmFull.Length > 0;

            bool skipped = false;
            if (wantHkcu && !CreateBack(hkcuFull, defaultValue, ref skipped, ref note)) { return false; }
            if (wantHklm && !CreateBack(hklmFull, defaultValue, ref skipped, ref note)) { return false; }
            if (skipped)
            {
                note = (note.Length > 0 ? note + "；" : "") + "其中一层已经有这个挂载点了（可能软件重装加回来了），没覆盖";
                return false;
            }
            if (note.Length == 0) { note = "已放回去（读回核对在）"; }
            return true;
        }

        /// <summary>重建一个挂载子键：完整键路径自带 hive 前缀（「HKCU\…\NameSpace\{GUID}」），
        /// 拆成 hive + 相对路径；先读回（在就跳过），不在就建 + 写(默认)值 + 再读回核对。</summary>
        private static bool CreateBack(string fullKey, string defaultValue,
            ref bool skipped, ref string note)
        {
            int cut = fullKey.IndexOf('\\');
            string hive = (cut < 0) ? fullKey : fullKey.Substring(0, cut);
            string rel = (cut < 0) ? "" : fullKey.Substring(cut + 1);
            RegistryKey baseKey = HiveByName(HiveShort(hive));
            try
            {
                if (baseKey.OpenSubKey(rel, false) != null)
                {
                    skipped = true;
                    return true;
                }
                using (RegistryKey k = baseKey.CreateSubKey(rel))
                {
                    if (k == null) { note = "建不了挂载键 " + fullKey; return false; }
                    if (defaultValue.Length > 0) { k.SetValue("", defaultValue, RegistryValueKind.String); }
                }
                if (baseKey.OpenSubKey(rel, false) == null)
                {
                    note = "建了但读回来不在：" + fullKey;
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
        public static string ReportText(List<PcIconItem> items)
        {
            LoadRoot();
            StringBuilder sb = new StringBuilder();
            int sys = 0, third = 0, unknown = 0;
            sb.AppendLine("此电脑多余图标 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("扫描这两个挂载点（「此电脑 → 设备和驱动器」里的非磁盘图标就是从这来的）：");
            sb.AppendLine("  " + _hkcuHive + "\\" + _hkcuPath + "（每用户）");
            sb.AppendLine("  " + _hklmHive + "\\" + _hklmPath + "（这台机器所有用户）");
            if (_isolated) { sb.AppendLine("⚠️ 测试隔离根：这里改的不是真的「此电脑」图标（上面两行是隔离路径）。"); }
            sb.AppendLine();
            sb.AppendLine("记号：○ 系统自带（只列、不动）· ◇ 第三方（可移除）· · 来历不明（可移除）");
            sb.AppendLine();

            foreach (PcIconItem it in items)
            {
                string mark = it.Kind == "system" ? "○" : (it.Kind == "third" ? "◇" : "·");
                if (it.Kind == "system") { sys++; } else if (it.Kind == "third") { third++; } else { unknown++; }
                sb.Append("  ").Append(mark).Append(' ');
                if (it.Name.Length > 0) { sb.Append(it.Name); }
                else if (it.SystemItem) { sb.Append("（系统条目）"); }
                else { sb.Append("（来历不明）"); }
                sb.Append("  [").Append(it.Guid).Append("]");
                sb.Append("  · 挂在 ").Append(it.SourceText());
                if (it.Icon.Length > 0) { sb.Append("  · 图标 ").Append(it.Icon); }
                if (it.Command.Length > 0) { sb.Append("  · 双击 ").Append(it.Command); }
                sb.AppendLine();
                if (it.SystemItem && it.Name.Length == 0)
                {
                    sb.Append("      → 系统自带的挂载点，这一页不会动它");
                    sb.AppendLine();
                }
            }

            sb.AppendLine();
            int pending = PendingRecords();
            sb.AppendLine("汇总：共 " + items.Count + " 个 —— 系统 " + sys + " 个（不动）、第三方 " + third + " 个、"
                + "来历不明 " + unknown + " 个"
                + (pending > 0 ? ("；有 " + pending + " 条移除记录还没还原") : "；没有待还原的记录"));
            if (third + unknown > 0)
            {
                sb.AppendLine("结论：有 " + (third + unknown) + " 个第三方 / 来历不明的图标，点「移除全部第三方图标…」"
                    + "把它们从「此电脑」里清掉（只删挂载点、不删软件；删前记原值、随时能还原）。");
            }
            else
            {
                sb.AppendLine("结论：没有第三方图标 —— 「设备和驱动器」里的非磁盘图标全是系统自带的，这一页不会动它们。");
            }
            return sb.ToString();
        }

        /// <summary>命令行 `pcicons --json` 用的机器可读输出。</summary>
        public static string JsonReport(List<PcIconItem> items)
        {
            LoadRoot();
            StringBuilder sb = new StringBuilder();
            int sys = 0, third = 0, unknown = 0;
            foreach (PcIconItem it in items)
            {
                if (it.Kind == "system") { sys++; } else if (it.Kind == "third") { third++; } else { unknown++; }
            }
            sb.Append("{\"root\":").Append(JsonText(RootText()));
            sb.Append(",\"isolated\":").Append(Isolated ? "true" : "false");
            sb.Append(",\"count\":").Append(items.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"system\":").Append(sys.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"third\":").Append(third.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"unknown\":").Append(unknown.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"pending\":").Append(PendingRecords().ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"items\":[");
            for (int i = 0; i < items.Count; i++)
            {
                PcIconItem it = items[i];
                if (i > 0) { sb.Append(","); }
                sb.Append("{\"guid\":").Append(JsonText(it.Guid));
                sb.Append(",\"name\":").Append(JsonText(it.Name));
                sb.Append(",\"source\":").Append(JsonText(it.SourceText()));
                sb.Append(",\"kind\":").Append(JsonText(it.Kind));
                sb.Append(",\"removable\":").Append(it.Removable ? "true" : "false");
                sb.Append(",\"icon\":").Append(JsonText(it.Icon));
                sb.Append(",\"command\":").Append(JsonText(it.Command));
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
