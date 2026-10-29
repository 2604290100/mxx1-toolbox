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
    /// <summary>一个 USB 设备（U 盘 / 鼠标 / 键盘 / 集线器 / 打印机…）在「设备管理器 → 属性 →
    /// 电源管理」那一页上的设置（2026-10-28 用户提的第 2 件事：USB 省电 / 断连）。
    ///
    /// 用户报的现象是「USB 设备自己断一下又连回来」：鼠标卡一下、U 盘突然掉、移动硬盘认不到 ——
    /// Windows 默认允许"为了省电把这个 USB 设备关掉"，空闲一会儿就真的把它断了。
    /// 那个勾选框写在哪：`HKLM\SYSTEM\CurrentControlSet\Enum\USB\<VID_xxxx&PID_xxxx>\<实例>\
    /// Device Parameters` 下的两个 DWORD ——
    ///
    ///     EnhancedPowerManagementEnabled = 1   允许计算机关闭此设备以节约电源
    ///     SelectiveSuspendEnabled        = 1   允许选择性挂起（USB 2.0 起）
    ///
    /// 所以「关掉省电」= 把这两个值写 0（没有就新建），「还原」= 按记录写回原值 / 没有的删掉。
    /// **这是 HKLM，要管理员**；改完要**拔一下再插上**（或者重启）才生效 —— 驱动在设备枚举时读一次。
    ///
    /// 四条口径（同「此电脑多余图标」）：
    /// ① 扫描只读；② 写前把**每个值原本有没有、是什么类型、什么数据**都记进 TSV（记不下来就一个都不写）；
    /// ③ 写完逐个读回核对；④ 命令行 `usbpower` 默认只读，写入口只在隔离根
    ///    `MXX1_USBPOWER_ROOT` 设好时放行（值可以是任意 hive 的完整路径，测试全指到 HKCU 下面就不用管理员）。
    ///
    /// 测试隔离：`MXX1_USBPOWER_ROOT`（替换 USB 枚举根）、`MXX1_USBPOWER_TSV`（记录文件挪走）。</summary>
    internal sealed class UsbPowerDevice
    {
        public string Instance = "";      // VID_1234&PID_5678\5&1a2b3c&0&1
        public string KeyPath = "";       // 相对 hive 的完整路径（含 Device Parameters 的父键）
        public string Name = "";
        public string Service = "";
        public string Vendor = "";
        public string ParamsPath = "";    // 相对 hive 的 Device Parameters 路径（""=没有这个键）
        public string State = "default";  // on / off / default
        public int OnCount;
        public int OffCount;

        public bool HasParams { get { return ParamsPath.Length > 0; } }

        /// <summary>要管的两个值（顺序固定，报告里一直按这个顺序念）。</summary>
        public static readonly string[] ValueNames = new string[]
        {
            "EnhancedPowerManagementEnabled",
            "SelectiveSuspendEnabled"
        };

        public string StateText()
        {
            if (!HasParams) { return "没有电源管理键（改不了）"; }
            if (State == "on") { return "允许省电（会被关掉）"; }
            if (State == "off") { return "已禁止省电"; }
            return "跟随系统默认";
        }
    }

    internal sealed class UsbPowerReport
    {
        public bool Ok = true;
        public int Changed;
        public int Skipped;
        public int Failed;
        public bool NeedAdmin;
        public string Message = "";
    }

    internal static class UsbPower
    {
        private const string DefaultHive = "HKLM";
        private const string DefaultPath = @"SYSTEM\CurrentControlSet\Enum\USB";
        private const string TsvHeader = "# mxx1-toolbox USB 省电 原值记录（制表符分隔；路径做了 base64，别手改）";

        private static string _hive = DefaultHive;
        private static string _path = DefaultPath;
        private static bool _isolated;

        // ------------------------------------------------------------------ 根

        private static void LoadRoot()
        {
            _hive = DefaultHive;
            _path = DefaultPath;
            _isolated = false;
            string v = (Environment.GetEnvironmentVariable("MXX1_USBPOWER_ROOT") ?? "").Trim().Trim('"').TrimEnd('\\');
            if (v.Length == 0) { return; }
            int i = v.IndexOf('\\');
            string h = (i < 0) ? v : v.Substring(0, i);
            _hive = HiveShort(h);
            _path = (i < 0) ? "" : v.Substring(i + 1);
            _isolated = true;
        }

        private static string HiveShort(string h)
        {
            string s = (h ?? "").Trim().ToUpperInvariant();
            if (s == "HKCU" || s == "HKEY_CURRENT_USER") { return "HKCU"; }
            if (s == "HKCR" || s == "HKEY_CLASSES_ROOT") { return "HKCR"; }
            if (s == "HKU" || s == "HKEY_USERS") { return "HKU"; }
            return "HKLM";
        }

        private static RegistryKey HiveByName(string shortName)
        {
            if (shortName == "HKCU") { return Registry.CurrentUser; }
            if (shortName == "HKCR") { return Registry.ClassesRoot; }
            if (shortName == "HKU") { return Registry.Users; }
            return Registry.LocalMachine;
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
            string env = (Environment.GetEnvironmentVariable("MXX1_USBPOWER_TSV") ?? "").Trim();
            if (env.Length > 0) { return env; }
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } } catch { }
            return Path.Combine(dir, "usb-power-original.tsv");
        }

        // ------------------------------------------------------------------ 扫描（只读）

        public static List<UsbPowerDevice> Scan()
        {
            LoadRoot();
            List<UsbPowerDevice> list = new List<UsbPowerDevice>();
            using (RegistryKey root = HiveByName(_hive).OpenSubKey(_path, false))
            {
                if (root == null) { return list; }
                foreach (string cls in root.GetSubKeyNames())
                {
                    using (RegistryKey ck = root.OpenSubKey(cls, false))
                    {
                        if (ck == null) { continue; }
                        foreach (string inst in ck.GetSubKeyNames())
                        {
                            UsbPowerDevice d = new UsbPowerDevice();
                            d.Instance = cls + "\\" + inst;
                            d.KeyPath = _path + "\\" + d.Instance;
                            d.Vendor = cls;
                            using (RegistryKey ik = ck.OpenSubKey(inst, false))
                            {
                                if (ik == null) { continue; }
                                d.Name = ReadName(ik, inst);
                                d.Service = Str(ik, "Service");
                                using (RegistryKey pk = ik.OpenSubKey("Device Parameters", false))
                                {
                                    if (pk != null)
                                    {
                                        d.ParamsPath = d.KeyPath + "\\Device Parameters";
                                        foreach (string vn in UsbPowerDevice.ValueNames)
                                        {
                                            int val;
                                            if (!TryDword(pk, vn, out val)) { continue; }
                                            if (val == 1) { d.OnCount++; }
                                            else if (val == 0) { d.OffCount++; }
                                        }
                                        if (d.OnCount > 0) { d.State = "on"; }
                                        else if (d.OffCount > 0) { d.State = "off"; }
                                    }
                                }
                            }
                            list.Add(d);
                        }
                    }
                }
            }
            list.Sort(delegate (UsbPowerDevice a, UsbPowerDevice b)
            {
                int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (c != 0) { return c; }
                return string.Compare(a.Instance, b.Instance, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>显示名：`FriendlyName` → `DeviceDesc`（`@usbhub.inf,%x%;USB 复合设备` 取分号后面那段）
        /// → 实例名。都不装样，读不到就把实例名念出来。</summary>
        private static string ReadName(RegistryKey ik, string inst)
        {
            string s = Str(ik, "FriendlyName");
            if (s.Length > 0) { return s; }
            s = Str(ik, "DeviceDesc");
            int semi = s.LastIndexOf(';');
            if (semi >= 0 && semi < s.Length - 1) { s = s.Substring(semi + 1); }
            return s.Length > 0 ? s.Trim() : inst;
        }

        private static string Str(RegistryKey k, string name)
        {
            try
            {
                object o = k.GetValue(name);
                return (o == null) ? "" : Convert.ToString(o, CultureInfo.InvariantCulture).Trim();
            }
            catch { return ""; }
        }

        private static bool TryDword(RegistryKey k, string name, out int value)
        {
            value = 0;
            try
            {
                object o = k.GetValue(name);
                if (o == null) { return false; }
                if (!(o is int)) { return false; }
                value = (int)o;
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ 关省电（写）

        /// <summary>把一批设备的两个电源管理值写 0（允许计算机关闭它 → 改成不允许）。</summary>
        public static UsbPowerReport DisablePowerSave(List<UsbPowerDevice> devices)
        {
            UsbPowerReport rep = new UsbPowerReport();
            return Write(devices, rep, true);
        }

        /// <summary>按记录还原（写回原值 / 原本没有的删掉）。</summary>
        public static UsbPowerReport Restore()
        {
            UsbPowerReport rep = new UsbPowerReport();
            List<string[]> rows = ReadRecords();
            if (rows.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有待还原的记录（这台机器还没用这个窗口关过 USB 省电）。";
                return rep;
            }
            LoadRoot();
            List<string> keep = new List<string>();
            StringBuilder bad = new StringBuilder();
            foreach (string[] r in rows)
            {
                if (r.Length < 5) { continue; }   // 一行 = 一个值（时间 / 键路径 / 值名 / 原本有没有 / 原值），共 5 段
                string keyPath = From64(r[1]);
                string valueName = From64(r[2]);
                string existed = r[3];
                int oldData = 0;
                int.TryParse(r[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out oldData);
                try
                {
                    using (RegistryKey k = HiveByName(_hive).OpenSubKey(keyPath, true))
                    {
                        if (k == null)
                        {
                            rep.Skipped++;
                            bad.Append("　· ").Append(keyPath).Append("（键已经不在了，记录留着）").AppendLine();
                            keep.Add(Join(r));
                            continue;
                        }
                        if (existed == "1") { k.SetValue(valueName, oldData, RegistryValueKind.DWord); }
                        else
                        {
                            try { k.DeleteValue(valueName, false); } catch { }
                        }
                    }
                    if (Verify(keyPath, valueName, existed == "1", oldData)) { rep.Changed++; }
                    else
                    {
                        rep.Failed++;
                        keep.Add(Join(r));
                        bad.Append("　· ").Append(keyPath).Append("\\").Append(valueName).Append("（写回后读数不对）").AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    rep.Failed++;
                    keep.Add(Join(r));
                    if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException) { rep.NeedAdmin = true; }
                    bad.Append("　· ").Append(keyPath).Append("\\").Append(valueName)
                        .Append("（写不进去：").Append(ex.Message).Append("）").AppendLine();
                }
            }
            WriteRecords(keep);
            rep.Ok = rep.Failed == 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("还原 ").Append(rep.Changed.ToString(CultureInfo.InvariantCulture)).Append(" 个值");
            if (rep.Skipped > 0) { sb.Append("，跳过 ").Append(rep.Skipped.ToString(CultureInfo.InvariantCulture)).Append(" 个"); }
            if (rep.Failed > 0) { sb.Append("，失败 ").Append(rep.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 个"); }
            sb.Append("。拔一下设备再插上（或重启）才生效。");
            if (bad.Length > 0) { sb.AppendLine().Append(bad.ToString()); }
            rep.Message = sb.ToString();
            return rep;
        }

        private static UsbPowerReport Write(List<UsbPowerDevice> devices, UsbPowerReport rep, bool off)
        {
            LoadRoot();
            if (devices == null || devices.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有选中要处理的设备。";
                return rep;
            }
            // ① 先记原值
            List<string> lines = new List<string>();
            List<UsbPowerDevice> todo = new List<UsbPowerDevice>();
            foreach (UsbPowerDevice d in devices)
            {
                if (!d.HasParams) { rep.Skipped++; continue; }
                RegistryKey pk = null;
                try { pk = HiveByName(_hive).OpenSubKey(d.ParamsPath, false); }
                catch { pk = null; }
                if (pk == null) { rep.Skipped++; continue; }
                todo.Add(d);
                using (pk)
                {
                    foreach (string vn in UsbPowerDevice.ValueNames)
                    {
                        object o = null;
                        try { o = pk.GetValue(vn); } catch { }
                        bool existed = (o is int);
                        int data = existed ? (int)o : 0;
                        lines.Add(Now() + "\t" + B64(d.ParamsPath) + "\t" + B64(vn) + "\t"
                            + (existed ? "1" : "0") + "\t" + data.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            if (lines.Count > 0)
            {
                try { AppendRecords(lines); }
                catch (Exception ex)
                {
                    rep.Ok = false;
                    rep.Message = "原值记不下来（" + RecordFile() + "）：" + ex.Message + " —— 为了能还原，这次一个设备都没动。";
                    rep.Failed = todo.Count;
                    return rep;
                }
            }

            // ② 逐个写 0 + 读回核对
            StringBuilder bad = new StringBuilder();
            foreach (UsbPowerDevice d in todo)
            {
                bool ok = true;
                string err = "";
                try
                {
                    using (RegistryKey pk = HiveByName(_hive).OpenSubKey(d.ParamsPath, true))
                    {
                        if (pk == null) { ok = false; err = "打不开（要管理员）"; }
                        else
                        {
                            foreach (string vn in UsbPowerDevice.ValueNames)
                            {
                                pk.SetValue(vn, 0, RegistryValueKind.DWord);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ok = false;
                    err = ex.Message;
                    if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException) { rep.NeedAdmin = true; }
                }
                if (ok)
                {
                    foreach (string vn in UsbPowerDevice.ValueNames)
                    {
                        if (!Verify(d.ParamsPath, vn, true, 0)) { ok = false; err = "写完读回来不是 0"; }
                    }
                }
                if (ok) { rep.Changed++; }
                else
                {
                    rep.Failed++;
                    bad.Append("　· ").Append(d.Name).Append("（").Append(d.Instance).Append("）：").Append(err).AppendLine();
                }
            }

            rep.Ok = rep.Failed == 0;
            StringBuilder sb = new StringBuilder();
            sb.Append(off ? "关闭省电 " : "写回 ")
                .Append(rep.Changed.ToString(CultureInfo.InvariantCulture)).Append(" 个设备");
            if (rep.Skipped > 0) { sb.Append("，跳过 ").Append(rep.Skipped.ToString(CultureInfo.InvariantCulture)).Append(" 个（没有电源管理键）"); }
            if (rep.Failed > 0) { sb.Append("，失败 ").Append(rep.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 个"); }
            sb.AppendLine("。每个值都读回来核对过。");
            if (off) { sb.AppendLine("⚠️ 要拔一下设备再插上（或者重启）才生效 —— 驱动是在设备枚举时读这几个值的。"); }
            if (bad.Length > 0) { sb.AppendLine().Append(bad.ToString()); }
            sb.Append("原值记在：").Append(RecordFile());
            rep.Message = sb.ToString();
            return rep;
        }

        private static bool Verify(string keyPath, string valueName, bool existed, int expect)
        {
            try
            {
                using (RegistryKey k = HiveByName(_hive).OpenSubKey(keyPath, false))
                {
                    if (k == null) { return false; }
                    object o = k.GetValue(valueName);
                    if (!existed) { return o == null; }
                    return (o is int) && (int)o == expect;
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
                    if (p.Length >= 5) { rows.Add(p); }
                }
            }
            catch { }
            return rows;
        }

        public static int PendingRecords() { return ReadRecords().Count; }

        /// <summary>记录里有几个**设备**（一条记录一行 = 一个值，一个设备两个值）。</summary>
        public static int PendingDevices()
        {
            List<string[]> rows = ReadRecords();
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] r in rows)
            {
                if (r.Length < 2) { continue; }
                if (!keys.Contains(r[1])) { keys.Add(r[1]); }
            }
            return keys.Count;
        }

        // ------------------------------------------------------------------ 报告

        public static string ReportText(List<UsbPowerDevice> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("USB 设备枚举根：").AppendLine(RootText());
            int on = 0, off = 0, none = 0;
            foreach (UsbPowerDevice d in list)
            {
                if (d.State == "on") { on++; }
                else if (d.State == "off") { off++; }
                else { none++; }
            }
            sb.Append("共 ").Append(list.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个设备：允许省电 ")
                .Append(on.ToString(CultureInfo.InvariantCulture)).Append(" 个、已禁止省电 ")
                .Append(off.ToString(CultureInfo.InvariantCulture)).Append(" 个、跟随默认 / 改不了 ")
                .Append(none.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个。");
            foreach (UsbPowerDevice d in list)
            {
                sb.Append(d.State == "on" ? "× " : (d.State == "off" ? "· " : "· "))
                    .Append(d.Name).Append("　").Append(d.Instance).Append("　→ ").AppendLine(d.StateText());
            }
            sb.Append("pending=").Append(PendingRecords().ToString(CultureInfo.InvariantCulture))
                .Append("（").Append(PendingDevices().ToString(CultureInfo.InvariantCulture)).AppendLine(" 个设备）");
            sb.Append("record=").AppendLine(RecordFile());
            sb.Append("（只读，没有改注册表；关掉省电 / 还原只能在窗口里点）");
            return sb.ToString();
        }

        public static string JsonReport(List<UsbPowerDevice> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"root\":\"").Append(JsonText(RootText())).Append("\",\"pending\":")
                .Append(PendingRecords().ToString(CultureInfo.InvariantCulture)).Append(",\"devices\":[");
            for (int i = 0; i < list.Count; i++)
            {
                UsbPowerDevice d = list[i];
                if (i > 0) { sb.Append(","); }
                sb.Append("{\"name\":\"").Append(JsonText(d.Name))
                    .Append("\",\"instance\":\"").Append(JsonText(d.Instance))
                    .Append("\",\"service\":\"").Append(JsonText(d.Service))
                    .Append("\",\"state\":\"").Append(JsonText(d.State))
                    .Append("\",\"params\":").Append(d.HasParams ? "true" : "false").Append("}");
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
