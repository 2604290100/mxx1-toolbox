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
    /// <summary>隐私设置页签的后端：一张"开关 → 注册表值"的表，加上「改之前先记原值、随时能一键还原」。
    ///
    /// 三条设计底线（和这个项目其它地方一样）：
    ///  ① 只碰隐私/广告/遥测开关，绝不碰安全防线（Defender / 防火墙 / UAC / SmartScreen）；
    ///  ② 写之前先把原值记下来（%LOCALAPPDATA%\mxx1-toolbox\privacy-original.tsv），
    ///     「隐私一键还原」按这份记录逐条写回去（原来没这个值就把值删掉）；
    ///  ③ 写完必须读回来核对 —— 读了不等于写进去了（这个项目在「按流量计费」上踩过"假成功"）。
    ///
    /// 记原值 / 读回核对 / 还原这套机制本身在 `RegEngine.cs`（「常用设置」里那批写注册表的按钮
    /// 也用它，两边共用一份实现，免得分叉成两种行为）。这个文件只负责隐私这一张表 + 中文报告。</summary>
    internal static class Privacy
    {
        public const string HiveCurrentUser = "HKCU";
        public const string HiveLocalMachine = "HKLM";

        private static RegValueSpec V(string hive, string key, string name, int off, int on)
        {
            RegValueSpec v = new RegValueSpec();
            v.Hive = hive; v.Key = key; v.Name = name; v.Dword = true;
            v.OffValue = off.ToString(CultureInfo.InvariantCulture);
            v.OnValue = on.ToString(CultureInfo.InvariantCulture);
            return v;
        }

        private static RegItemSpec Item(string id, string name, string what, RegValueSpec[] values)
        {
            RegItemSpec i = new RegItemSpec();
            i.Id = id; i.Name = name; i.What = what; i.Values = values;
            foreach (RegValueSpec v in values) { if (v.Hive == HiveLocalMachine) { i.Admin = true; } }
            return i;
        }

        private const string DataCollection = "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection";
        private const string Wer = "SOFTWARE\\Microsoft\\Windows\\Windows Error Reporting";
        private const string Cdm = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager";
        private const string Search = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Search";

        /// <summary>The switches, in the order the page shows them. Everything here is a plain
        /// registry value: no service is stopped, no scheduled task is deleted, nothing is
        /// uninstalled -- all of it is one click away from being put back.</summary>
        private static readonly RegItemSpec[] Table = new RegItemSpec[]
        {
            Item("telemetry", "微软遥测", "诊断数据上报策略（0 = 最少量；Windows 专业版/家庭版只认「基本」，彻底停要关 DiagTrack 服务）",
                new RegValueSpec[] { V(HiveLocalMachine, DataCollection, "AllowTelemetry", 0, 1) }),

            Item("error-report", "错误报告", "程序崩溃时向微软上报（HKCU 那个值管「要不要弹窗口」）",
                new RegValueSpec[]
                {
                    V(HiveLocalMachine, Wer, "Disabled", 1, 0),
                    V(HiveCurrentUser, Wer, "DontShowUI", 1, 0)
                }),

            Item("cortana", "小娜助手", "Cortana / 搜索里的联网建议（Win11 已经没有小娜，写进去也不生效）",
                new RegValueSpec[]
                {
                    V(HiveLocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search", "AllowCortana", 0, 1)
                }),

            Item("bing-search", "搜索推荐", "开始菜单搜索里的必应网页建议和 Cortana 授权",
                new RegValueSpec[]
                {
                    V(HiveCurrentUser, Search, "BingSearchEnabled", 0, 1),
                    V(HiveCurrentUser, Search, "CortanaConsent", 0, 1)
                }),

            Item("speech", "语音收集", "在线语音识别的「允许微软收集我的语音」授权",
                new RegValueSpec[]
                {
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\Speech_OneCore\\Settings\\OnlineSpeechPrivacy", "HasAccepted", 0, 1)
                }),

            Item("typing", "打字收集", "打字 / 手写个性化数据的收集与上传",
                new RegValueSpec[]
                {
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\Input\\TIPC", "Enabled", 0, 1),
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\Input\\Settings", "InsightsEnabled", 0, 1),
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\InputPersonalization", "RestrictImplicitTextCollection", 1, 0),
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\InputPersonalization", "RestrictImplicitInkCollection", 1, 0)
                }),

            Item("activity", "活动历史", "时间线 / 活动历史记录（本机收集与跨设备同步）",
                new RegValueSpec[]
                {
                    V(HiveLocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\System", "PublishUserActivities", 0, 1),
                    V(HiveLocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\System", "EnableActivityFeed", 0, 1)
                }),

            Item("sys-ads", "系统广告", "开始菜单推荐、设置里的建议、锁屏 Spotlight 广告、静默装推广应用（共 8 个值）",
                new RegValueSpec[]
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
                new RegValueSpec[]
                {
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo", "Enabled", 0, 1)
                }),

            Item("delivery", "传递优化", "更新 / 商店应用的 P2P 上传下载（0 = 只走微软服务器，不再从别人的电脑上拉）",
                new RegValueSpec[]
                {
                    V(HiveLocalMachine, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\DeliveryOptimization\\Config", "DODownloadMode", 0, 1)
                }),

            Item("feedback", "反馈请求", "Windows 隔三差五弹的「你觉得 Windows 怎么样」（把频率设为 0）",
                new RegValueSpec[]
                {
                    V(HiveCurrentUser, "SOFTWARE\\Microsoft\\Siuf\\Rules", "NumberOfSIUFInPeriod", 0, 1)
                })
        };

        private static readonly RegEngine Engine = new RegEngine(Table, AppPaths.PrivacyBackupFile, "隐私开关");

        public static RegItemSpec[] All { get { return Table; } }

        public static RegItemSpec Find(string id) { return Engine.Find(id); }

        /// <summary>Do the values of this switch live in HKLM? Then the write needs an elevated
        /// process (the button carries runAsAdmin, and Launcher re-launches the toolbox elevated).</summary>
        public static bool NeedsAdmin(string id)
        {
            if (string.Equals(id, "optimize", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (string.Equals(id, "restore", StringComparison.OrdinalIgnoreCase)) { return true; }
            RegItemSpec item = Find(id);
            return (item != null) && item.Admin;
        }

        // ---------------------------------------------------------------- 对外动作

        /// <summary>Switches one privacy item off (off = true) or back on.</summary>
        public static string Set(string id, bool off, out bool ok)
        {
            RegItemSpec item = Find(id);
            if (item == null) { ok = false; return "不认识这个隐私开关：" + id; }
            return ApplyTo(item, off, out ok);
        }

        private static string ApplyTo(RegItemSpec item, bool off, out bool ok)
        {
            RegApplyReport report = Engine.Apply(item, !off);
            StringBuilder sb = new StringBuilder();
            sb.Append("【").Append(item.Name).Append("】").Append(off ? "关闭" : "开启").AppendLine();
            sb.Append("  ").Append(item.What).AppendLine();
            sb.AppendLine();

            foreach (RegOutcome o in report.Outcomes)
            {
                if (o.Ok)
                {
                    sb.Append("  √ ").Append(o.Label).Append(" = ").Append(o.Now).AppendLine();
                }
                else if (!o.Verified && o.Now.Length > 0)
                {
                    // 写了但读回来不是目标值：这类"假成功"必须和"根本没写进去"分开显示
                    sb.Append("  ! ").Append(o.Label).Append(" 写了但读回来是 ").Append(o.Now).AppendLine();
                }
                else
                {
                    sb.Append("  × ").Append(o.Label).Append("  写不进去：").Append(o.Error).AppendLine();
                }
            }

            if (report.BackupError.Length > 0)
            {
                sb.AppendLine();
                sb.Append("  原值备份没写成：").Append(report.BackupError).AppendLine();
                sb.Append("  （「隐私一键还原」会不完整，请手动记一下）").AppendLine();
            }

            sb.AppendLine();
            if (report.Ok)
            {
                sb.Append("  ").Append(report.Written.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个值都写进去并读回核对过了。");
            }
            else
            {
                sb.Append("  有值没写成功 —— 上面带 × 或 ! 的就是。");
            }
            sb.AppendLine();
            sb.Append("  反悔就点「隐私一键还原」（它按改动前的原值写回去）。");
            ok = report.Ok;
            return sb.ToString();
        }

        /// <summary>Turns every switch off; the report says what happened to each one.</summary>
        public static string Optimize(out bool ok)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("隐私一键优化：把这一页的开关全部关掉（安全防线一概不碰）");
            sb.AppendLine();
            int firstBefore = Engine.RecordCount();
            bool allOk = true;
            foreach (RegItemSpec item in Table)
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
            sb.Append("原值备份：").Append(Engine.BackupPath).AppendLine();
            sb.Append("（这次多记了 ").Append((Engine.RecordCount() - firstBefore).ToString(CultureInfo.InvariantCulture)).Append(" 条原值）").AppendLine();
            sb.Append("想恢复原样：点「隐私一键还原」。");
            ok = allOk;
            return sb.ToString();
        }

        /// <summary>Writes every recorded original value back, then drops the backup file.</summary>
        public static string Restore(out bool ok)
        {
            RegRestoreReport report = Engine.Restore();
            if (report.Outcomes.Count == 0)
            {
                ok = true;
                return "没有要还原的东西：还没用过本页的隐私开关。" + Environment.NewLine
                    + "（原值备份应该在 " + Engine.BackupPath + "）";
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("隐私一键还原：按改动前的原值写回去");
            sb.AppendLine();
            foreach (RegOutcome o in report.Outcomes)
            {
                if (o.Ok)
                {
                    sb.Append("  √ ").Append(o.Label).Append(" → ").Append(o.Existed
                        ? o.Original + "（写回原值）"
                        : "删掉（原来没有这个值）").AppendLine();
                }
                else
                {
                    sb.Append("  × ").Append(o.Label).Append("  还原失败：").Append(o.Error).AppendLine();
                }
            }
            sb.AppendLine();
            if (report.Failed == 0)
            {
                sb.Append("  ").Append(report.Done.ToString(CultureInfo.InvariantCulture))
                  .Append(" 条都还原了；原值备份已经删掉（下次再用隐私开关会重新记当时的原值）。").AppendLine();
                sb.Append("  有些值要重启或重新登录才会生效。");
            }
            else
            {
                sb.Append("  有 ").Append(report.Failed.ToString(CultureInfo.InvariantCulture))
                  .Append(" 条没还原成功（多半是缺管理员权限），原值备份先留着。");
            }
            ok = (report.Failed == 0);
            return sb.ToString();
        }

        /// <summary>Read-only: what every switch is set to right now.</summary>
        public static string Status()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("隐私开关当前状态（只读，没改任何东西）");
            sb.AppendLine();
            foreach (RegItemSpec item in Table)
            {
                int off = 0;
                int on = 0;
                int odd = 0;
                foreach (RegValueSpec v in item.Values)
                {
                    int cur;
                    if (!Engine.TryReadInt(v, out cur)) { odd++; continue; }
                    int offValue;
                    int onValue;
                    int.TryParse(v.OffValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out offValue);
                    int.TryParse(v.OnValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out onValue);
                    if (cur == offValue) { off++; }
                    else if (cur == onValue) { on++; }
                    else { odd++; }
                }
                string state;
                if (off == item.Values.Length) { state = "已关闭"; }
                else if (on == item.Values.Length) { state = "还开着（系统默认）"; }
                else if (off == 0 && on == 0) { state = "没设置过（系统默认）"; }
                else { state = "部分关闭（" + off + "/" + item.Values.Length + "）"; }
                sb.Append("  ").Append(RegEngine.PadCjk(item.Name, 14)).Append(state);
                if (item.Admin) { sb.Append("　[要管理员]"); }
                sb.AppendLine();
            }
            sb.AppendLine();
            int records = Engine.RecordCount();
            if (records > 0)
            {
                sb.Append("  改动前的原值记了 ").Append(records.ToString(CultureInfo.InvariantCulture))
                  .Append(" 条：").Append(Engine.BackupPath).AppendLine();
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
            bool hadBackup = File.Exists(Engine.BackupPath);
            if (hadBackup)
            {
                try { savedBackup = File.ReadAllText(Engine.BackupPath, Encoding.UTF8); File.Delete(Engine.BackupPath); }
                catch { savedBackup = null; }
            }
            try
            {
                RegValueSpec before = V(HiveCurrentUser, scratch, "probe", 0, 0);
                RegistryKey key = Registry.CurrentUser.CreateSubKey(scratch);
                if (key == null) { sb.AppendLine("建不出测试用的注册表键"); return sb.ToString(); }
                key.SetValue("probe", 7, RegistryValueKind.DWord);
                key.Close();
                sb.AppendLine("测试键 " + HiveCurrentUser + "\\" + scratch + "\\probe = 7，准备走一遍「关闭」");

                RegItemSpec fake = Item("selftest", "自检项", "只写工具箱自己的测试键",
                    new RegValueSpec[] { V(HiveCurrentUser, scratch, "probe", 1, 2) });

                bool setOk;
                string report = ApplyTo(fake, true, out setOk);
                sb.AppendLine(report);
                int now;
                bool read = Engine.TryReadInt(before, out now);
                if (!setOk || !read || now != 1) { sb.AppendLine("写入或读回核对失败：now=" + (read ? now.ToString(CultureInfo.InvariantCulture) : "读不到")); return sb.ToString(); }
                sb.AppendLine("写入 + 读回核对通过（probe = 1）");

                bool restoreOk;
                string back = Restore(out restoreOk);
                if (!restoreOk) { sb.AppendLine("还原失败：" + back); return sb.ToString(); }
                read = Engine.TryReadInt(before, out now);
                if (!read || now != 7) { sb.AppendLine("还原后读回不是 7：now=" + (read ? now.ToString(CultureInfo.InvariantCulture) : "读不到")); return sb.ToString(); }
                sb.AppendLine("还原通过（probe 回到 7），整条链路没问题");

                // 再试一次「原来没有这个值」的分支：删掉值 → 关闭 → 还原 → 应该还是"没有这个值"
                RegValueSpec probe = V(HiveCurrentUser, scratch, "probe", 0, 0);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(scratch))
                {
                    if (k != null) { k.DeleteValue("probe", false); }
                }
                string report2 = ApplyTo(fake, true, out setOk);
                string back2 = Restore(out restoreOk);
                read = Engine.TryReadInt(probe, out now);
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
                        File.WriteAllText(Engine.BackupPath, savedBackup, new UTF8Encoding(false));
                    }
                    else if (File.Exists(Engine.BackupPath)) { File.Delete(Engine.BackupPath); }
                }
                catch { }
            }
        }
    }
}
