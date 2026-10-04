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
    /// <summary>One registry value a switch writes. A switch is a GROUP of values, because moving
    /// only some of the values behind one setting is what makes a tweak look like it did nothing
    /// (「系统广告」alone is eight values under ContentDeliveryManager).
    ///
    /// <para>Two shapes are supported:</para>
    /// ① a plain value (DWORD or string) with a value for each direction (OnValue / OffValue);
    /// ② a whole KEY ({86ca1aa0-…} style shell overrides): on = create the key plus its sub values,
    ///    off = delete the key tree. That is how the classic Explorer / context menu overrides work,
    ///    and it is the reason the engine records key existence separately from values.</summary>
    internal sealed class RegValueSpec
    {
        public string Hive = "HKCU";      // HKCU | HKLM
        public string Key = "";
        public string Name = "";          // "" = the key's own (default) value
        public bool Dword = true;
        public string OnValue = "";       // written when the switch is turned on
        public string OffValue = "";      // written when it is turned off (ignored when OffDeletesKey)
        public bool OffDeletesKey = false;// off = delete this key tree (a shell override)
        public RegSubSpec[] Subs = null;  // extra values created together with the key

        public string Label { get { return (Name.Length == 0) ? "（默认值）" : Name; } }
    }

    /// <summary>A value created inside a key that the switch itself creates
    /// (e.g. InProcServer32\ThreadingModel = Apartment). Path is relative to the key.</summary>
    internal sealed class RegSubSpec
    {
        public string Path = "";
        public string Name = "";
        public bool Dword = false;
        public string Value = "";

        public RegSubSpec(string path, string name, string value)
        {
            Path = path; Name = name; Value = value;
        }

        public string FullLabel
        {
            get
            {
                string leaf = (Name.Length == 0) ? "（默认值）" : Name;
                return (Path.Length == 0) ? leaf : Path + "\\" + leaf;
            }
        }
    }

    /// <summary>One switch: a name a person reads, and the values behind it.</summary>
    internal sealed class RegItemSpec
    {
        public string Id = "";
        public string Name = "";
        public string What = "";
        public bool Admin = false;
        public RegValueSpec[] Values = new RegValueSpec[0];
    }

    /// <summary>改动前的原值。记的是「第一次动这一项的时候」的现场：反复点「关闭…」不会把已经
    /// 改过的值当成原值，否则还原出来的是"上一次关闭后的状态"而不是出厂状态。</summary>
    internal sealed class RegRecord
    {
        public string Id = "";
        public string Hive = "";
        public string Key = "";
        public string Name = "";
        public bool Existed = false;
        public RegistryValueKind Kind = RegistryValueKind.DWord;
        public string Value = "";
        public bool IsKey = false;      // 记的是"这个键当时在不在"（键级覆盖）

        public string Label
        {
            get
            {
                string leaf = (Name.Length == 0) ? "（默认值）" : Name;
                return IsKey ? (Key + "（整个键）") : leaf;
            }
        }
    }

    /// <summary>一个值这一步的结果。报告文字由各自的界面拼，引擎只说事实。</summary>
    internal sealed class RegOutcome
    {
        public string Label = "";
        public bool Ok = false;
        public bool Verified = true;      // 读回核对过
        public string Now = "";
        public string Error = "";
        public string Note = "";          // 「原来没有这个值 → 删掉」这类补充
        public bool Existed = false;      // 还原用：原来有没有这个值
        public string Original = "";      // 还原用：原值是什么
    }

    internal sealed class RegApplyReport
    {
        public List<RegOutcome> Outcomes = new List<RegOutcome>();
        public bool Ok = true;
        public int Written = 0;
        public int NewRecords = 0;
        public string BackupError = "";
    }

    internal sealed class RegRestoreReport
    {
        public List<RegOutcome> Outcomes = new List<RegOutcome>();
        public int Done = 0;
        public int Failed = 0;
        public bool Cleared = false;      // 备份文件已经删掉（全部还原成功）
        public string BackupError = "";
    }

    /// <summary>「改注册表 → 写之前先记原值 → 写完读回核对 → 随时一键还原」这套机制的唯一实现。
    ///
    /// 为什么要有这个文件：这套机制原来只长在 `Privacy.cs` 里，于是「隐私设置」的 29 个开关有
    /// 后悔药，而「常用设置」里同样写注册表的按钮（任务栏合并方式、开始菜单对齐、驱动自动安装、
    /// 内核隔离、资源管理器/右键菜单的 CLSID 覆盖）没有 —— 同一类动作两套待遇。两份各写一套
    /// 迟早会分叉成两种行为，所以抽成一份引擎，两张表（隐私 / 系统设置）共用它。
    ///
    /// 三条底线：① 只碰白名单里写死的键；② 只有「第一次动」才记原值；③ 写完必须读回来核对
    /// （读了不等于写进去了 —— 「按流量计费」那个键上踩过"假成功"）。</summary>
    internal sealed class RegEngine
    {
        private readonly RegItemSpec[] _table;
        private readonly string _backupPath;
        private readonly string _title;

        public RegEngine(RegItemSpec[] table, string backupPath, string title)
        {
            _table = table;
            _backupPath = backupPath;
            _title = title;
        }

        public RegItemSpec[] All { get { return _table; } }
        public string BackupPath { get { return _backupPath; } }
        public string Title { get { return _title; } }

        public RegItemSpec Find(string id)
        {
            foreach (RegItemSpec i in _table)
            {
                if (string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)) { return i; }
            }
            return null;
        }

        /// <summary>写 HKLM 的值要管理员（界面按钮带 runAsAdmin，Launcher 会把自己提权重起）。</summary>
        public bool NeedsAdmin(string id)
        {
            RegItemSpec item = Find(id);
            if (item == null) { return false; }
            return item.Admin;
        }

        // ---------------------------------------------------------------- registry

        private static RegistryKey Root(string hive)
        {
            return (string.Equals(hive, "HKLM", StringComparison.OrdinalIgnoreCase))
                ? Registry.LocalMachine : Registry.CurrentUser;
        }

        private static RegistryKey OpenKey(string hive, string key, bool writable)
        {
            try
            {
                RegistryKey root = Root(hive);
                return writable ? root.CreateSubKey(key) : root.OpenSubKey(key, false);
            }
            catch { return null; }
        }

        /// <summary>Reads the current value. exists=false means "this value is not there at all",
        /// which is a state of its own (Windows defaults skip plenty of values).</summary>
        public bool TryReadRaw(RegValueSpec v, out bool exists, out RegistryValueKind kind, out string text)
        {
            exists = false;
            kind = RegistryValueKind.DWord;
            text = "";
            try
            {
                using (RegistryKey k = OpenKey(v.Hive, v.Key, false))
                {
                    if (k == null) { return false; }
                    object o = k.GetValue(v.Name, null);
                    if (o == null) { return true; }   // 键在、值不在
                    try { kind = k.GetValueKind(v.Name); }
                    catch { kind = v.Dword ? RegistryValueKind.DWord : RegistryValueKind.String; }
                    text = ToText(o, kind);
                    exists = true;
                    return true;
                }
            }
            catch { return false; }
        }

        public bool TryReadInt(RegValueSpec v, out int value)
        {
            value = 0;
            bool exists;
            RegistryValueKind kind;
            string text;
            if (!TryReadRaw(v, out exists, out kind, out text) || !exists) { return false; }
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public bool KeyExists(string hive, string key)
        {
            try
            {
                using (RegistryKey k = OpenKey(hive, key, false)) { return k != null; }
            }
            catch { return false; }
        }

        private static string ToText(object o, RegistryValueKind kind)
        {
            if (o == null) { return ""; }
            if (kind == RegistryValueKind.DWord)
            {
                try { return Convert.ToInt32(o, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture); }
                catch { return o.ToString(); }
            }
            string[] multi = o as string[];
            if (multi != null) { return string.Join("|", multi); }
            byte[] bytes = o as byte[];
            if (bytes != null)
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes) { sb.Append(b.ToString("X2", CultureInfo.InvariantCulture)); }
                return sb.ToString();
            }
            return o.ToString();
        }

        private static object FromText(string text, RegistryValueKind kind)
        {
            if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.QWord)
            {
                int n;
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { return null; }
                return n;
            }
            return text;
        }

        /// <summary>Writes one value. Returns false with a plain sentence when Windows refuses.</summary>
        private bool WriteRaw(string hive, string key, string name, RegistryValueKind kind, string text, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey k = OpenKey(hive, key, true))
                {
                    if (k == null) { error = "打不开这个键（可能被策略保护）"; return false; }
                    object o = FromText(text, kind);
                    if (o == null) { error = "值「" + text + "」不像个数字"; return false; }
                    k.SetValue(name, o, kind);
                }
                return true;
            }
            catch (UnauthorizedAccessException) { error = "没有权限（要管理员）"; return false; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private bool DeleteValueRaw(string hive, string key, string name, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey k = OpenKey(hive, key, false))
                {
                    if (k == null) { return true; }   // 键都不在 = 值也不在
                }
                using (RegistryKey k = OpenKey(hive, key, true))
                {
                    if (k == null) { return true; }
                    k.DeleteValue(name, false);
                }
                return true;
            }
            catch (UnauthorizedAccessException) { error = "没有权限（要管理员）"; return false; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>Creates a key tree (on direction of a shell override). Existing values are kept,
        /// which is why the engine records them before writing.</summary>
        private bool CreateKeyTree(RegValueSpec v, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey k = OpenKey(v.Hive, v.Key, true))
                {
                    if (k == null) { error = "建不出这个键（可能被策略保护）"; return false; }
                    k.SetValue("", v.OnValue, RegistryValueKind.String);
                }
                if (v.Subs != null)
                {
                    foreach (RegSubSpec s in v.Subs)
                    {
                        string path = (s.Path.Length == 0) ? v.Key : (v.Key + "\\" + s.Path);
                        RegistryValueKind kind = s.Dword ? RegistryValueKind.DWord : RegistryValueKind.String;
                        // 子值里可以写 %SystemRoot% 这类占位符：注册表不认，必须在这里展开成真路径。
                        string text = AppPaths.Expand(s.Value);
                        string subError;
                        if (!WriteRaw(v.Hive, path, s.Name, kind, text, out subError))
                        {
                            error = s.FullLabel + " 写不进去：" + subError;
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (UnauthorizedAccessException) { error = "没有权限（要管理员）"; return false; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private bool DeleteKeyTree(RegValueSpec v, out string error)
        {
            error = "";
            try
            {
                RegistryKey root = Root(v.Hive);
                using (RegistryKey probe = root.OpenSubKey(v.Key, false))
                {
                    if (probe == null) { return true; }   // 本来就没有
                }
                root.DeleteSubKeyTree(v.Key, false);
                return true;
            }
            catch (UnauthorizedAccessException) { error = "没有权限（要管理员）"; return false; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ---------------------------------------------------------------- 原值备份

        public List<RegRecord> LoadRecords()
        {
            List<RegRecord> list = new List<RegRecord>();
            try
            {
                if (!File.Exists(_backupPath)) { return list; }
                foreach (string line in File.ReadAllLines(_backupPath, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) { continue; }
                    string[] p = line.Split('\t');
                    if (p.Length < 6) { continue; }
                    RegRecord r = new RegRecord();
                    r.Id = p[0]; r.Hive = p[1]; r.Key = p[2]; r.Name = p[3];
                    r.Existed = (p[4].Trim() == "1");
                    if (p.Length >= 8)
                    {
                        // 现行格式：… 存在 种类 值 是不是键
                        int kind = 4;
                        int.TryParse(p[5].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out kind);
                        r.Kind = (RegistryValueKind)kind;
                        r.Value = p[6];
                        r.IsKey = (p[7].Trim() == "1");
                    }
                    else
                    {
                        // 老格式（隐私设置第一版写的 6 列）：第 6 列是 DWORD 数值
                        r.Kind = RegistryValueKind.DWord;
                        r.Value = p[5].Trim();
                        r.IsKey = false;
                    }
                    list.Add(r);
                }
            }
            catch { }
            return list;
        }

        public int RecordCount() { return LoadRecords().Count; }

        public List<RegRecord> RecordsFor(string id)
        {
            List<RegRecord> mine = new List<RegRecord>();
            foreach (RegRecord r in LoadRecords())
            {
                if (string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) { mine.Add(r); }
            }
            return mine;
        }

        private string SaveRecords(List<RegRecord> list)
        {
            try
            {
                AppPaths.EnsureBase();
                StringBuilder sb = new StringBuilder();
                sb.Append("# 萌新工具箱 · ").Append(_title).Append("改动前的原值（一键还原按它写回去；删掉这个文件就等于放弃还原）").AppendLine();
                foreach (RegRecord r in list)
                {
                    sb.Append(r.Id).Append('\t').Append(r.Hive).Append('\t').Append(r.Key).Append('\t')
                      .Append(r.Name).Append('\t').Append(r.Existed ? "1" : "0").Append('\t')
                      .Append(((int)r.Kind).ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.Value).Append('\t').Append(r.IsKey ? "1" : "0").AppendLine();
                }
                File.WriteAllText(_backupPath, sb.ToString(), new UTF8Encoding(false));
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static bool Recorded(List<RegRecord> list, string hive, string key, string name)
        {
            foreach (RegRecord r in list)
            {
                if (string.Equals(r.Hive, hive, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private static RegRecord Snapshot(string id, string hive, string key, string name)
        {
            RegRecord r = new RegRecord();
            r.Id = id; r.Hive = hive; r.Key = key; r.Name = name; r.IsKey = false;
            // 直接开键读一次就够，不必为了记一条原值再建一个引擎实例。
            bool exists = false;
            RegistryValueKind kind = RegistryValueKind.DWord;
            string text = "";
            try
            {
                using (RegistryKey k = OpenKey(hive, key, false))
                {
                    if (k != null)
                    {
                        object o = k.GetValue(name, null);
                        if (o != null)
                        {
                            try { kind = k.GetValueKind(name); } catch { kind = RegistryValueKind.String; }
                            text = ToText(o, kind);
                            exists = true;
                        }
                    }
                }
            }
            catch { }
            r.Existed = exists;
            r.Kind = exists ? kind : RegistryValueKind.DWord;
            r.Value = exists ? text : "0";
            return r;
        }

        private static RegRecord SnapshotKey(string id, string hive, string key)
        {
            RegRecord r = new RegRecord();
            r.Id = id; r.Hive = hive; r.Key = key; r.Name = "";
            r.IsKey = true;
            bool exists = false;
            string text = "";
            RegistryValueKind kind = RegistryValueKind.String;
            try
            {
                using (RegistryKey k = OpenKey(hive, key, false))
                {
                    if (k != null)
                    {
                        exists = true;
                        object o = k.GetValue("", null);
                        if (o != null)
                        {
                            try { kind = k.GetValueKind(""); } catch { kind = RegistryValueKind.String; }
                            text = ToText(o, kind);
                        }
                    }
                }
            }
            catch { }
            r.Existed = exists;
            r.Kind = kind;
            r.Value = text;
            return r;
        }

        // ---------------------------------------------------------------- 对外动作

        /// <summary>Turns one switch on (on=true) or off, recording the original values first and
        /// reading every write back.</summary>
        public RegApplyReport Apply(RegItemSpec item, bool on)
        {
            RegApplyReport report = new RegApplyReport();
            List<RegRecord> backup = LoadRecords();
            List<RegRecord> fresh = new List<RegRecord>();

            foreach (RegValueSpec v in item.Values)
            {
                if (v.OffDeletesKey)
                {
                    if (!Recorded(backup, v.Hive, v.Key, ""))
                    {
                        fresh.Add(SnapshotKey(item.Id, v.Hive, v.Key));
                        if (v.Subs != null)
                        {
                            foreach (RegSubSpec s in v.Subs)
                            {
                                string path = (s.Path.Length == 0) ? v.Key : (v.Key + "\\" + s.Path);
                                if (!Recorded(backup, v.Hive, path, s.Name))
                                {
                                    fresh.Add(Snapshot(item.Id, v.Hive, path, s.Name));
                                }
                            }
                        }
                    }
                    string keyError;
                    bool keyOk = on ? CreateKeyTree(v, out keyError) : DeleteKeyTree(v, out keyError);
                    RegOutcome ko = new RegOutcome();
                    ko.Label = v.Key;
                    ko.Ok = keyOk;
                    ko.Verified = true;
                    ko.Now = KeyExists(v.Hive, v.Key) ? "在" : "不在";
                    ko.Error = keyError;
                    if (on && keyOk && !KeyExists(v.Hive, v.Key))
                    {
                        ko.Ok = false;
                        ko.Verified = false;
                        ko.Error = "写了但读回来这个键还是不在";
                    }
                    report.Outcomes.Add(ko);
                    if (ko.Ok) { report.Written++; } else { report.Ok = false; }
                    continue;
                }

                string target = on ? v.OnValue : v.OffValue;
                bool deleteInstead = (!on && v.OffValue == null);
                if (!Recorded(backup, v.Hive, v.Key, v.Name))
                {
                    fresh.Add(Snapshot(item.Id, v.Hive, v.Key, v.Name));
                }

                RegOutcome o = new RegOutcome();
                o.Label = v.Label;
                string error;
                if (deleteInstead)
                {
                    if (!DeleteValueRaw(v.Hive, v.Key, v.Name, out error))
                    {
                        o.Ok = false; o.Error = error;
                    }
                    else
                    {
                        bool exists2;
                        RegistryValueKind kind2;
                        string text2;
                        TryReadRaw(v, out exists2, out kind2, out text2);
                        o.Ok = !exists2;
                        o.Verified = o.Ok;
                        o.Now = exists2 ? text2 : "（没有这个值）";
                        if (!o.Ok) { o.Error = "写了但读回来还在"; }
                    }
                }
                else
                {
                    RegistryValueKind kind = v.Dword ? RegistryValueKind.DWord : RegistryValueKind.String;
                    if (!WriteRaw(v.Hive, v.Key, v.Name, kind, target, out error))
                    {
                        o.Ok = false; o.Error = error;
                    }
                    else
                    {
                        int now;
                        if (TryReadInt(v, out now) && now.ToString(CultureInfo.InvariantCulture) == target)
                        {
                            o.Ok = true; o.Now = target;
                        }
                        else if (!v.Dword)
                        {
                            bool exists3;
                            RegistryValueKind kind3;
                            string text3;
                            TryReadRaw(v, out exists3, out kind3, out text3);
                            o.Ok = exists3 && string.Equals(text3, target, StringComparison.Ordinal);
                            o.Now = exists3 ? text3 : "读不到";
                            o.Verified = o.Ok;
                            if (!o.Ok) { o.Error = "写了但读回来不是这个值"; }
                        }
                        else
                        {
                            int back;
                            bool read = TryReadInt(v, out back);
                            o.Ok = false; o.Verified = false;
                            o.Now = read ? back.ToString(CultureInfo.InvariantCulture) : "读不到";
                            o.Error = "写了但读回来不是这个值";
                        }
                    }
                }
                report.Outcomes.Add(o);
                if (o.Ok) { report.Written++; } else { report.Ok = false; }
            }

            if (fresh.Count > 0)
            {
                backup.AddRange(fresh);
                string saveError = SaveRecords(backup);
                report.NewRecords = fresh.Count;
                if (saveError.Length > 0)
                {
                    report.Ok = false;
                    report.BackupError = saveError;
                }
            }
            return report;
        }

        /// <summary>Writes every recorded original back; drops the record when nothing failed.</summary>
        public RegRestoreReport Restore()
        {
            RegRestoreReport report = new RegRestoreReport();
            List<RegRecord> backup = LoadRecords();
            if (backup.Count == 0) { return report; }

            // 先处理"整个键"的记录：本来没有这个键 → 整棵树删掉（我们建的那些子值跟着一起走），
            // 后面的值级记录就别再去把键建回来了。
            List<string> removedTrees = new List<string>();
            foreach (RegRecord r in backup)
            {
                if (!r.IsKey) { continue; }
                RegOutcome o = new RegOutcome();
                o.Label = r.Label;
                string error;
                if (!r.Existed)
                {
                    RegValueSpec v = new RegValueSpec();
                    v.Hive = r.Hive; v.Key = r.Key; v.OffDeletesKey = true;
                    bool ok = DeleteKeyTree(v, out error);
                    o.Ok = ok;
                    o.Note = ok ? "原来没有这个键 → 整棵树删掉" : "";
                    if (ok) { removedTrees.Add(r.Hive + "|" + r.Key); }
                }
                else
                {
                    // 键本来就在（别的工具装过同样的覆盖）：只把默认值写回去，键留着
                    bool ok = WriteRaw(r.Hive, r.Key, "", r.Kind, r.Value, out error);
                    o.Ok = ok;
                    o.Note = "这个键本来就在，只把默认值写回去了";
                }
                if (!r.IsKey)
                {
                    o.Existed = r.Existed;
                    o.Original = r.Value;
                }
                o.Now = KeyExists(r.Hive, r.Key) ? "在" : "不在";
                o.Error = error;
                report.Outcomes.Add(o);
                if (o.Ok) { report.Done++; } else { report.Failed++; }
            }

            foreach (RegRecord r in backup)
            {
                if (r.IsKey) { continue; }
                RegOutcome o = new RegOutcome();
                o.Label = r.Label;
                o.Existed = r.Existed;
                o.Original = r.Value;
                if (UnderRemovedTree(removedTrees, r.Hive, r.Key))
                {
                    o.Ok = true;
                    o.Note = "所在的键已经整棵删掉了";
                    report.Outcomes.Add(o);
                    report.Done++;
                    continue;
                }
                string error;
                bool ok;
                if (r.Existed)
                {
                    ok = WriteRaw(r.Hive, r.Key, r.Name, r.Kind, r.Value, out error);
                    o.Note = "写回原值 " + r.Value;
                }
                else
                {
                    ok = DeleteValueRaw(r.Hive, r.Key, r.Name, out error);
                    o.Note = "原来没有这个值 → 删掉";
                }
                o.Ok = ok;
                o.Error = error;
                report.Outcomes.Add(o);
                if (ok) { report.Done++; } else { report.Failed++; }
            }

            if (report.Failed == 0)
            {
                try { File.Delete(_backupPath); report.Cleared = true; }
                catch (Exception ex) { report.BackupError = ex.Message; }
            }
            return report;
        }

        private static bool UnderRemovedTree(List<string> trees, string hive, string key)
        {
            string target = hive + "|" + key;
            foreach (string t in trees)
            {
                if (string.Equals(target, t, StringComparison.OrdinalIgnoreCase)) { return true; }
                if (target.Length > t.Length
                    && target.StartsWith(t, StringComparison.OrdinalIgnoreCase)
                    && target[t.Length] == '\\') { return true; }
            }
            return false;
        }

        /// <summary>「现在这项是什么状态」的人话：值都写着 off 值 / 都写着 on 值 / 没设置过 / 混合。</summary>
        public string StateText(RegItemSpec item)
        {
            int count = 0, off = 0, on = 0, odd = 0, missing = 0;
            string oddText = "";
            foreach (RegValueSpec v in item.Values)
            {
                count++;
                if (v.OffDeletesKey)
                {
                    if (KeyExists(v.Hive, v.Key)) { on++; } else { off++; }
                    continue;
                }
                bool exists;
                RegistryValueKind kind;
                string text;
                TryReadRaw(v, out exists, out kind, out text);
                if (!exists) { missing++; continue; }
                if (string.Equals(text, v.OffValue, StringComparison.OrdinalIgnoreCase)) { off++; }
                else if (string.Equals(text, v.OnValue, StringComparison.OrdinalIgnoreCase)) { on++; }
                else { odd++; oddText = text; }
            }
            if (count == 0) { return "没配置"; }
            if (off == count) { return "关闭状态"; }
            if (on == count) { return "开启状态"; }
            if (missing == count) { return "没设置过（系统默认）"; }
            if (odd == count) { return "都不是（现在的值是 " + oddText + "）"; }
            return "混合（关 " + off + " / 开 " + on + " / 没设置 " + missing + " / 其它 " + odd + "）";
        }

        /// <summary>CJK-aware padding: a Chinese character takes two columns, so PadRight lines up
        /// nothing at all in these reports.</summary>
        public static string PadCjk(string text, int width)
        {
            int w = 0;
            foreach (char c in text) { w += (c > 0x2E7F) ? 2 : 1; }
            string s = text;
            while (w < width) { s += " "; w++; }
            return s;
        }
    }
}
