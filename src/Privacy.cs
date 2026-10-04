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
    /// <summary>One registry value a privacy switch writes.</summary>
    internal sealed class PrivacyValue
    {
        public string Hive = "HKCU";   // HKCU | HKLM
        public string Key = "";
        public string Name = "";
        public int Off = 0;            // 「关闭…」写这个值
        public int On = 1;             // 「开启…」写这个值
    }

    /// <summary>One privacy switch. A switch is a GROUP of registry values: 「系统广告」alone is
    /// eight values under ContentDeliveryManager, and moving only some of them is what makes a
    /// privacy tweak look like it did nothing.</summary>
    internal sealed class PrivacyItem
    {
        public string Id = "";
        public string Name = "";
        public string What = "";
        public bool Admin = false;
        public PrivacyValue[] Values = new PrivacyValue[0];
    }

    /// <summary>隐私设置页签的后端：一张"开关 → 注册表值"的表，加上「改之前先记原值、随时能一键还原」。
    ///
    /// 三条设计底线（和这个项目其它地方一样）：
    ///  ① 只碰隐私/广告/遥测开关，绝不碰安全防线（Defender / 防火墙 / UAC / SmartScreen）；
    ///  ② 写之前先把原值记下来（%LOCALAPPDATA%\mxx1-toolbox\privacy-original.tsv），
    ///     「隐私一键还原」按这份记录逐条写回去（原来没这个值就把值删掉）；
    ///  ③ 写完必须读回来核对 —— 读了不等于写进去了（这个项目在「按流量计费」上踩过"假成功"）。
    ///
    /// 原值的记录方式是"第一次动这一项的时候记一次"：反复点「关闭…」不会把已经改过的值
    /// 当成原值，否则还原出来的就是"上一次关闭后的状态"而不是出厂状态。</summary>
    internal static class Privacy
    {
        public const string HiveCurrentUser = "HKCU";
        public const string HiveLocalMachine = "HKLM";

        private static PrivacyValue V(string hive, string key, string name, int off, int on)
        {
            PrivacyValue v = new PrivacyValue();
            v.Hive = hive; v.Key = key; v.Name = name; v.Off = off; v.On = on;
            return v;
        }

        private static PrivacyItem Item(string id, string name, string what, PrivacyValue[] values)
        {
            PrivacyItem i = new PrivacyItem();
            i.Id = id; i.Name = name; i.What = what; i.Values = values;
            foreach (PrivacyValue v in values) { if (v.Hive == HiveLocalMachine) { i.Admin = true; } }
            return i;
        }

        private const string DataCollection = "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection";
        private const string Wer = "SOFTWARE\\Microsoft\\Windows\\Windows Error Reporting";
        private const string Cdm = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager";
        private const string Search = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Search";

        /// <summary>The switches, in the order the page shows them. Everything here is a plain
        /// registry value: no service is stopped, no scheduled task is deleted, nothing is
        /// uninstalled -- all of it is one click away from being put back.</summary>
        private static readonly PrivacyItem[] Table = new PrivacyItem[]
        {
            Item("telemetry", "微软遥测", "诊断数据上报策略（0 = 最少量；Windows 专业版/家庭版只认「基本」，彻底停要关 DiagTrack 服务）",
                new PrivacyValue[] { V(HiveLocalMachine, DataCollection, "AllowTelemetry", 0, 1) }),

            Item("error-report", "错误报告", "程序崩溃时向微软上报（HKCU 那个值管「要不要弹窗口」）",
                new PrivacyValue[]
                {
                    V(HiveLocalMachine, Wer, "Disabled", 1, 0),
                    V(HiveCurrentUser, Wer, "DontShowUI", 1, 0)
                }),

            Item("cortana", "小娜助手", "Cortana / 搜索里的联网建议（Win11 已经没有小娜，写进去也不生效）",
                new PrivacyValue[]
                {
                    V(HiveLocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search", "AllowCortana", 0, 1)
                }),

            Item("bing-search", "搜索推荐", "开始菜单搜索里的必应网页建议和 Cortana 授权",
                new PrivacyValue[]
                {
                    V(HiveCurrentUser, Search, "BingSearchEnabled", 0, 1),
                    V(HiveCurrentUser, Search, "CortanaConsent", 0, 1)
                }),

            Item("speech", "语音收集", "在线语音识别的「允许微软收集我的语音」授权",
                new PrivacyValue[]
                {
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\Speech_OneCore\\Settings\\OnlineSpeechPrivacy", "HasAccepted", 0, 1)
                }),

            Item("typing", "打字收集", "打字 / 手写个性化数据的收集与上传",
                new PrivacyValue[]
                {
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\Input\\TIPC", "Enabled", 0, 1),
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\Input\\Settings", "InsightsEnabled", 0, 1),
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\InputPersonalization", "RestrictImplicitTextCollection", 1, 0),
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\InputPersonalization", "RestrictImplicitInkCollection", 1, 0)
                }),

            Item("activity", "活动历史", "时间线 / 活动历史记录（本机收集与跨设备同步）",
                new PrivacyValue[]
                {
                    V(HiveLocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\System", "PublishUserActivities", 0, 1),
                    V(HiveLocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\System", "EnableActivityFeed", 0, 1)
                }),

            Item("sys-ads", "系统广告", "开始菜单推荐、设置里的建议、锁屏 Spotlight 广告、静默装推广应用（共 8 个值）",
                new PrivacyValue[]
                {
                    V(HiveCurrentUser, Cdm, "SilentInstalledAppsEnabled", 0, 1),
                    V(HiveCurrentUser, Cdm, "SystemPaneSuggestionsEnabled", 0, 1),
                    V(HiveCurrentUser, Cdm, "SubscribedContent-338388Enabled", 0, 1),
                    V(HiveCurrentUser, Cdm, "SubscribedContent-338389Enabled", 0, 1),
                    V(HiveCurrentUser, Cdm, "SubscribedContent-310093Enabled", 0, 1),
                    V(HiveCurrentUser, Cdm, "SubscribedContent-338393Enabled", 0, 1),
                    V(HiveCurrentUser, Cdm, "SubscribedContent-353694Enabled", 0, 1),
                    V(HiveCurrentUser, Cdm, "SubscribedContent-353696Enabled", 0, 1)
                }),

            Item("ad-id", "个性化广告", "广告 ID（关掉后应用拿不到这个 ID 做定向广告）",
                new PrivacyValue[]
                {
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo", "Enabled", 0, 1)
                }),

            Item("delivery", "传递优化", "更新 / 商店应用的 P2P 上传下载（0 = 只走微软服务器，不再从别人的电脑上拉）",
                new PrivacyValue[]
                {
                    V(HiveLocalMachine, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\DeliveryOptimization\\Config", "DODownloadMode", 0, 1)
                }),

            Item("feedback", "反馈请求", "Windows 隔三差五弹的「你觉得 Windows 怎么样」（把频率设为 0）",
                new PrivacyValue[]
                {
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\Siuf\\Rules", "NumberOfSIUFInPeriod", 0, 1)
                })
        };

        public static PrivacyItem[] All { get { return Table; } }

        public static PrivacyItem Find(string id)
        {
            foreach (PrivacyItem i in Table)
            {
                if (string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)) { return i; }
            }
            return null;
        }

        /// <summary>Do the values of this switch live in HKLM? Then the write needs an elevated
        /// process (the button carries runAsAdmin, and Launcher re-launches the toolbox elevated).</summary>
        public static bool NeedsAdmin(string id)
        {
            if (string.Equals(id, "optimize", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (string.Equals(id, "restore", StringComparison.OrdinalIgnoreCase)) { return true; }
            PrivacyItem item = Find(id);
            return (item != null) && item.Admin;
        }

        // ---------------------------------------------------------------- registry

        private static RegistryKey Open(string hive, string key, bool writable)
        {
            RegistryKey root = (hive == HiveLocalMachine) ? Registry.LocalMachine : Registry.CurrentUser;
            if (writable) { return root.CreateSubKey(key); }
            return root.OpenSubKey(key, false);
        }

        private static bool TryRead(PrivacyValue v, out int value)
        {
            value = 0;
            try
            {
                using (RegistryKey k = Open(v.Hive, v.Key, false))
                {
                    if (k == null) { return false; }
                    object o = k.GetValue(v.Name);
                    if (o == null) { return false; }
                    value = Convert.ToInt32(o, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch { return false; }
        }

        private static bool Write(PrivacyValue v, int value, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey k = Open(v.Hive, v.Key, true))
                {
                    if (k == null) { error = "打不开这个键（可能被策略保护）"; return false; }
                    k.SetValue(v.Name, value, RegistryValueKind.DWord);
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "没有权限（要管理员）";
                return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static bool Delete(PrivacyValue v, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey k = Open(v.Hive, v.Key, true))
                {
                    if (k == null) { return true; }   // 键都不在 = 值也不在
                    k.DeleteValue(v.Name, false);
                }
                return true;
            }
            catch (UnauthorizedAccessException) { error = "没有权限（要管理员）"; return false; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ---------------------------------------------------------------- 原值备份

        public static string BackupPath { get { return AppPaths.PrivacyBackupFile; } }

        private sealed class Record
        {
            public string Id = "";
            public string Hive = "";
            public string Key = "";
            public string Name = "";
            public bool Existed = false;
            public int Value = 0;
        }

        private static List<Record> LoadBackup()
        {
            List<Record> list = new List<Record>();
            try
            {
                if (!File.Exists(BackupPath)) { return list; }
                foreach (string line in File.ReadAllLines(BackupPath, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0) { continue; }
                    string[] p = line.Split('\t');
                    if (p.Length < 6) { continue; }
                    Record r = new Record();
                    r.Id = p[0]; r.Hive = p[1]; r.Key = p[2]; r.Name = p[3];
                    r.Existed = (p[4].Trim() == "1");
                    int v = 0;
                    int.TryParse(p[5].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
                    r.Value = v;
                    list.Add(r);
                }
            }
            catch { }
            return list;
        }

        private static string SaveBackup(List<Record> list)
        {
            try
            {
                AppPaths.EnsureBase();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 萌新工具箱 · 隐私开关改动前的原值（「隐私一键还原」按它写回去；删掉这个文件就等于放弃还原）");
                foreach (Record r in list)
                {
                    sb.Append(r.Id).Append('\t').Append(r.Hive).Append('\t').Append(r.Key).Append('\t')
                      .Append(r.Name).Append('\t').Append(r.Existed ? "1" : "0").Append('\t')
                      .Append(r.Value.ToString(CultureInfo.InvariantCulture)).AppendLine();
                }
                File.WriteAllText(BackupPath, sb.ToString(), new UTF8Encoding(false));
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static bool Recorded(List<Record> list, PrivacyValue v)
        {
            foreach (Record r in list)
            {
                if (string.Equals(r.Hive, v.Hive, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Key, v.Key, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Name, v.Name, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        // ---------------------------------------------------------------- 对外动作

        /// <summary>Switches one privacy item off (off = true) or back on.</summary>
        public static string Set(string id, bool off, out bool ok)
        {
            PrivacyItem item = Find(id);
            if (item == null) { ok = false; return "不认识这个隐私开关：" + id; }
            return ApplyTo(item, off, out ok);
        }

        private static string ApplyTo(PrivacyItem item, bool off, out bool ok)
        {
            List<Record> backup = LoadBackup();
            List<Record> fresh = new List<Record>();
            StringBuilder sb = new StringBuilder();
            sb.Append("【").Append(item.Name).Append("】").Append(off ? "关闭" : "开启").AppendLine();
            sb.Append("  ").Append(item.What).AppendLine();
            sb.AppendLine();

            int written = 0;
            bool allOk = true;
            foreach (PrivacyValue v in item.Values)
            {
                int target = off ? v.Off : v.On;
                if (!Recorded(backup, v))
                {
                    int before;
                    bool had = TryRead(v, out before);
                    Record r = new Record();
                    r.Id = item.Id; r.Hive = v.Hive; r.Key = v.Key; r.Name = v.Name;
                    r.Existed = had; r.Value = had ? before : 0;
                    fresh.Add(r);
                }
                string error;
                if (!Write(v, target, out error))
                {
                    allOk = false;
                    sb.Append("  × ").Append(v.Name).Append("  写不进去：").Append(error).AppendLine();
                    continue;
                }
                int now;
                bool readBack = TryRead(v, out now);
                if (readBack && now == target)
                {
                    written++;
                    sb.Append("  √ ").Append(v.Name).Append(" = ").Append(now.ToString(CultureInfo.InvariantCulture)).AppendLine();
                }
                else
                {
                    allOk = false;
                    sb.Append("  ! ").Append(v.Name).Append(" 写了但读回来是 ")
                      .Append(readBack ? now.ToString(CultureInfo.InvariantCulture) : "读不到")
                      .AppendLine();
                }
            }

            if (fresh.Count > 0)
            {
                backup.AddRange(fresh);
                string saveError = SaveBackup(backup);
                if (saveError.Length > 0)
                {
                    allOk = false;
                    sb.AppendLine();
                    sb.Append("  原值备份没写成：").Append(saveError).AppendLine();
                    sb.Append("  （「隐私一键还原」会不完整，请手动记一下）").AppendLine();
                }
            }

            sb.AppendLine();
            if (allOk)
            {
                sb.Append("  ").Append(written).Append(" 个值都写进去并读回核对过了。");
            }
            else
            {
                sb.Append("  有值没写成功 —— 上面带 × 或 ! 的就是。");
            }
            sb.AppendLine();
            sb.Append("  反悔就点「隐私一键还原」（它按改动前的原值写回去）。");
            ok = allOk;
            return sb.ToString();
        }

        /// <summary>Turns every switch off; the report says what happened to each one.</summary>
        public static string Optimize(out bool ok)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("隐私一键优化：把这一页的开关全部关掉（安全防线一概不碰）");
            sb.AppendLine();
            int firstBefore = LoadBackup().Count;
            bool allOk = true;
            foreach (PrivacyItem item in Table)
            {
                bool oneOk;
                string report = ApplyTo(item, true, out oneOk);
                if (!oneOk) { allOk = false; }
                foreach (string raw in report.Split('\n'))
                {
                    string t = raw.TrimEnd('\r');
                    if (t.StartsWith("  √") || t.StartsWith("  ×") || t.StartsWith("  !")) { sb.Append(t).AppendLine(); }
                }
                sb.Append("  ── ").Append(item.Name).Append(oneOk ? "：改好了" : "：有值没改成功").AppendLine();
            }
            sb.AppendLine();
            sb.Append("原值备份：").Append(BackupPath).AppendLine();
            sb.Append("（这次多记了 ").Append((LoadBackup().Count - firstBefore).ToString(CultureInfo.InvariantCulture)).Append(" 条原值）").AppendLine();
            sb.Append("想恢复原样：点「隐私一键还原」。");
            ok = allOk;
            return sb.ToString();
        }

        /// <summary>Writes every recorded original value back, then drops the backup file.</summary>
        public static string Restore(out bool ok)
        {
            List<Record> backup = LoadBackup();
            if (backup.Count == 0)
            {
                ok = true;
                return "没有要还原的东西：还没用过本页的隐私开关。" + Environment.NewLine
                    + "（原值备份应该在 " + BackupPath + "）";
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("隐私一键还原：按改动前的原值写回去");
            sb.AppendLine();
            int done = 0;
            int failed = 0;
            foreach (Record r in backup)
            {
                PrivacyValue v = V(r.Hive, r.Key, r.Name, 0, 0);
                string error = "";
                bool oneOk;
                if (r.Existed) { oneOk = Write(v, r.Value, out error); }
                else { oneOk = Delete(v, out error); }
                if (oneOk)
                {
                    done++;
                    sb.Append("  √ ").Append(r.Name).Append(r.Existed
                        ? " → " + r.Value.ToString(CultureInfo.InvariantCulture)
                        : " → 删掉（原来没有这个值）").AppendLine();
                }
                else
                {
                    failed++;
                    sb.Append("  × ").Append(r.Name).Append("  还原失败：").Append(error).AppendLine();
                }
            }
            sb.AppendLine();
            if (failed == 0)
            {
                try { File.Delete(BackupPath); } catch { }
                sb.Append("  ").Append(done).Append(" 条都还原了；原值备份已经删掉（下次再用隐私开关会重新记当时的原值）。").AppendLine();
                sb.Append("  有些值要重启或重新登录才会生效。");
            }
            else
            {
                sb.Append("  有 ").Append(failed).Append(" 条没还原成功（多半是缺管理员权限），原值备份先留着。");
            }
            ok = (failed == 0);
            return sb.ToString();
        }

        /// <summary>Read-only: what every switch is set to right now.</summary>
        public static string Status()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("隐私开关当前状态（只读，没改任何东西）");
            sb.AppendLine();
            foreach (PrivacyItem item in Table)
            {
                int off = 0;
                int on = 0;
                int odd = 0;
                foreach (PrivacyValue v in item.Values)
                {
                    int cur;
                    if (!TryRead(v, out cur)) { odd++; continue; }
                    if (cur == v.Off) { off++; }
                    else if (cur == v.On) { on++; }
                    else { odd++; }
                }
                string state;
                if (off == item.Values.Length) { state = "已关闭"; }
                else if (on == item.Values.Length) { state = "还开着（系统默认）"; }
                else if (off == 0 && on == 0) { state = "没设置过（系统默认）"; }
                else { state = "部分关闭（" + off + "/" + item.Values.Length + "）"; }
                sb.Append("  ").Append(PadCjk(item.Name, 14)).Append(state);
                if (item.Admin) { sb.Append("　[要管理员]"); }
                sb.AppendLine();
            }
            sb.AppendLine();
            List<Record> backup = LoadBackup();
            if (backup.Count > 0)
            {
                sb.Append("  改动前的原值记了 ").Append(backup.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" 条：").Append(BackupPath).AppendLine();
                sb.Append("  （点「隐私一键还原」可以把它们写回去）");
            }
            else
            {
                sb.Append("  还没动过任何隐私开关（所以没有原值可还原）");
            }
            return sb.ToString();
        }

        /// <summary>Exercises the whole "record original → write → read back → restore" chain on a
        /// scratch key this toolbox owns, so the test suite can prove the mechanism without writing
        /// a single real privacy setting. The user's own backup file is moved aside and put back.</summary>
        public static string SelfTest(out bool ok)
        {
            ok = false;
            const string scratch = "SOFTWARE\\mxx1-toolbox\\privacy-selftest";
            StringBuilder sb = new StringBuilder();
            string savedBackup = null;
            bool hadBackup = File.Exists(BackupPath);
            if (hadBackup)
            {
                try { savedBackup = File.ReadAllText(BackupPath, Encoding.UTF8); File.Delete(BackupPath); }
                catch { savedBackup = null; }
            }
            try
            {
                PrivacyValue before = V(HiveCurrentUser, scratch, "probe", 0, 0);
                RegistryKey key = Registry.CurrentUser.CreateSubKey(scratch);
                if (key == null) { sb.AppendLine("建不出测试用的注册表键"); return sb.ToString(); }
                key.SetValue("probe", 7, RegistryValueKind.DWord);
                key.Close();
                sb.AppendLine("测试键 " + HiveCurrentUser + "\\" + scratch + "\\probe = 7，准备走一遍「关闭」");

                PrivacyItem fake = Item("selftest", "自检项", "只写工具箱自己的测试键",
                    new PrivacyValue[] { V(HiveCurrentUser, scratch, "probe", 1, 2) });

                bool setOk;
                string report = ApplyTo(fake, true, out setOk);
                sb.AppendLine(report);
                int now;
                bool read = TryRead(before, out now);
                if (!setOk || !read || now != 1) { sb.AppendLine("写入或读回核对失败：now=" + (read ? now.ToString(CultureInfo.InvariantCulture) : "读不到")); return sb.ToString(); }
                sb.AppendLine("写入 + 读回核对通过（probe = 1）");

                bool restoreOk;
                string back = Restore(out restoreOk);
                if (!restoreOk) { sb.AppendLine("还原失败：" + back); return sb.ToString(); }
                read = TryRead(before, out now);
                if (!read || now != 7) { sb.AppendLine("还原后读回不是 7：now=" + (read ? now.ToString(CultureInfo.InvariantCulture) : "读不到")); return sb.ToString(); }
                sb.AppendLine("还原通过（probe 回到 7），整条链路没问题");

                // 再试一次「原来没有这个值」的分支：删掉值 → 关闭 → 还原 → 应该还是"没有这个值"
                Delete(before, out back);
                string report2 = ApplyTo(fake, true, out setOk);
                string back2 = Restore(out restoreOk);
                read = TryRead(before, out now);
                if (!restoreOk || read) { sb.AppendLine("「原来没有这个值」的分支不对：read=" + read); return sb.ToString(); }
                sb.AppendLine("「原来没有这个值」的分支也对（还原后值被删掉了）");
                ok = true;
                return sb.ToString();
            }
            catch (Exception ex)
            {
                sb.AppendLine("自检出错：" + ex.Message);
                return sb.ToString();
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(scratch, false); } catch { }
                try
                {
                    if (hadBackup && savedBackup != null)
                    {
                        File.WriteAllText(BackupPath, savedBackup, new UTF8Encoding(false));
                    }
                    else if (File.Exists(BackupPath)) { File.Delete(BackupPath); }
                }
                catch { }
            }
        }

        /// <summary>CJK-aware padding: a Chinese character takes two columns, so PadRight lines up
        /// nothing at all in this report.</summary>
        private static string PadCjk(string text, int width)
        {
            int w = 0;
            foreach (char c in text) { w += (c > 0x2E7F) ? 2 : 1; }
            string s = text;
            while (w < width) { s += " "; w++; }
            return s;
        }
    }
}
