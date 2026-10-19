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
    /// <summary>一条电源方案现在长什么样（只读这些值，改的时候不碰别的项）。
    /// -1 = 读不到（键在但值不在、或者根本打不开那个键）。</summary>
    internal sealed class PowerScheme
    {
        public string Guid = "";
        public string Name = "";
        public bool Active = false;
        public int LidAc = -1, LidDc = -1;   // 合上盖子
        public int PwrAc = -1, PwrDc = -1;   // 按电源按钮
        public int SlpAc = -1, SlpDc = -1;   // 按睡眠按钮
        public bool ThirdParty = false;      // GUID 不在 Windows 自带的那几个里

        public string Label { get { return Name.Length > 0 ? Name : Guid; } }
    }

    /// <summary>改动前的原值（一行一个方案）。</summary>
    internal sealed class PowerRecord
    {
        public string Guid = "";
        public int LidAc = -1, LidDc = -1, PwrAc = -1, PwrDc = -1, SlpAc = -1, SlpDc = -1;
    }

    /// <summary>一次 powercfg 调用的结果。Started=false = 连这个程序都没起来（路径不对 / 找不到）。</summary>
    internal sealed class PowerRun
    {
        public bool Started = false;
        public int ExitCode = -1;
        public string Output = "";
    }

    /// <summary>「电源与笔记本」段的后端：读方案状态、改「合盖 / 电源按钮 / 睡眠按钮」这三个动作、
    /// 记改动前的原值、随时一口还原；另外三个只读功能（体检 / 电池报告 / 谁在阻止睡眠）。
    ///
    /// 三条硬约定（都是这次真踩出来的，见 docs\DESIGN.md §12.71 与 PITFALLS 73/74）：
    /// ① **改的是所有电源方案**，不是只改"当前生效"的那个。原因：第三方工具（游戏加速器这类）
    ///    会自己建一个方案并切过去 —— 只改当前方案的话，它一切走你的设置就失效了。用户 2026-10-19
    ///    报的「合盖不关机」就是这个形状（活动方案被换成了 `Pavise 竞技`，它的合盖动作是睡眠）。
    /// ② **改之前必须先把原值写进磁盘**，存不下来就一个字都不动（同 RegEngine 的口径：
    ///    不能出现"改了但没法还原"的状态）。
    /// ③ **写完读回核对**，读回的和要写的不一样就算失败（不能报"已完成"）。
    ///
    /// 读状态直接读注册表（`...\Power\User\PowerSchemes\&lt;方案&gt;\&lt;子组&gt;\&lt;设置&gt;` 的
    /// ACSettingIndex / DCSettingIndex）—— 用 `powercfg /query` 的话输出是本地化文本，
    /// 换个语言就解析不出来了。写走 `powercfg /setacvalueindex`（官方那条路，
    /// 写完再 `/setactive` 当前方案让它立刻生效）。
    ///
    /// 测试用三个环境变量把整条链路隔离到测试自己的地方，**绝不碰真机的电源方案**：
    /// `MXX1_POWERCFG` = 假的 powercfg.exe；`MXX1_POWER_ROOT` = `HIVE|注册表路径`；
    /// `MXX1_POWER_TSV` = 原值记录文件挪到哪儿。</summary>
    internal static class Power
    {
        // ---- Windows 里这几个设置项的 GUID（"电源按钮和盖子" 这一组） ----
        public const string SubButtons = "4f971e89-eebd-4455-a8de-9e59040e7347";
        public const string SetLid = "5ca83367-6e45-459f-a27b-476b1d01c936";
        public const string SetPowerButton = "7648efa3-dd9c-4e3e-b566-50f929386280";
        public const string SetSleepButton = "96996bc0-ad50-47ec-923b-6f41874dd9eb";

        /// <summary>四个可选动作，下标就是注册表里存的值。</summary>
        public static readonly string[] ActionLabels = new string[]
        {
            "不采取任何操作", "睡眠", "休眠", "关机"
        };

        public static string ActionLabel(int v)
        {
            if (v < 0) { return Word(v); }
            if (v < ActionLabels.Length) { return ActionLabels[v]; }
            return "其它（" + v.ToString(CultureInfo.InvariantCulture) + "）" + HibernateNote(v);
        }

        /// <summary>「休眠」这一档要在界面上特别提醒：休眠被关掉时选它等于什么都不做。</summary>
        public static string HibernateNote(int v)
        {
            return (v == 2) ? "（休眠被关着时它等于什么都不做）" : "";
        }

        /// <summary>休眠现在开着吗（HibernateEnabled）。合盖动作选「休眠」时这一条决定它到底有没有用。
        /// 读的就是「隔离根」那个键下面的值（默认 `HKLM\…\Control\Power`，测试指到别处时跟着走）。</summary>
        public static bool HibernateEnabled()
        {
            try
            {
                using (RegistryKey k = RootOpen(null, false))
                {
                    if (k == null) { return false; }
                    object o = k.GetValue("HibernateEnabled", null);
                    if (o == null) { return false; }
                    return Convert.ToInt32(o, CultureInfo.InvariantCulture) != 0;
                }
            }
            catch { return false; }
        }

        /// <summary>记录原值的文件（和隐私 / 系统设置那两份是**分开**的：那两份记的是注册表值的原样，
        /// 还原只要写回去；电源这几个值写回注册表还不够，必须再让电源管理器重新读一次
        /// （`powercfg /setactive`），所以走自己这一套）。
        /// 环境变量 `MXX1_POWER_TSV` 可以把这份记录整个挪走（回归测试用它，绝不碰用户真实那份）。</summary>
        public static string BackupPath
        {
            get
            {
                string e = Environment.GetEnvironmentVariable("MXX1_POWER_TSV");
                if (e != null && e.Trim().Length > 0) { return AppPaths.Expand(e.Trim()); }
                return Path.Combine(AppPaths.BaseDir, "power-original.tsv");
            }
        }

        // ---------------------------------------------------------------- 隔离根 / 外部程序

        /// <summary>`HIVE|路径`，不给就是真机的 `HKLM|SYSTEM\CurrentControlSet\Control\Power`。
        /// 测试把它指到 HKCU 下面的测试根。</summary>
        public static string RootSpec()
        {
            string spec = Environment.GetEnvironmentVariable("MXX1_POWER_ROOT");
            if (spec == null) { spec = ""; }
            spec = spec.Trim();
            if (spec.Length == 0) { return "HKLM|SYSTEM\\CurrentControlSet\\Control\\Power"; }
            return spec;
        }

        public static string RootLabel() { return RootSpec().Replace("|", "\\"); }

        private static string RootHive()
        {
            string spec = RootSpec();
            int bar = spec.IndexOf('|');
            return (bar > 0) ? spec.Substring(0, bar).Trim().ToUpperInvariant() : "HKLM";
        }

        private static string RootPath()
        {
            string spec = RootSpec();
            int bar = spec.IndexOf('|');
            return (bar > 0) ? spec.Substring(bar + 1).Trim() : spec;
        }

        private static RegistryKey RootKey(bool writable)
        {
            return RootOpen(null, writable);
        }

        /// <summary>按「隔离根 + 子路径」开一个键。⚠ 读的路径**必须**和写走同一个 hive
        /// （测试把根指到 HKCU 时，读还去 HKLM 的话读回核对会永远失败 —— 第一版就是这样，
        /// 探针一跑就抓到了：`读回来有 6 个值不是要写的那样`）。</summary>
        private static RegistryKey RootOpen(string subPath, bool writable)
        {
            RegistryKey root = string.Equals(RootHive(), "HKLM", StringComparison.OrdinalIgnoreCase)
                ? Registry.LocalMachine : Registry.CurrentUser;
            string p = RootPath();
            if (subPath != null && subPath.Length > 0) { p = p + "\\" + subPath; }
            try
            {
                return writable ? root.CreateSubKey(p) : root.OpenSubKey(p, false);
            }
            catch { return null; }
        }

        /// <summary>要调用的 powercfg。`MXX1_POWERCFG` 指到别处时用它（回归测试的假程序）。</summary>
        public static string CfgExe()
        {
            string e = Environment.GetEnvironmentVariable("MXX1_POWERCFG");
            if (e != null && e.Trim().Length > 0) { return AppPaths.Expand(e.Trim()); }
            try { return Path.Combine(Environment.SystemDirectory, "powercfg.exe"); }
            catch { return "powercfg.exe"; }
        }

        /// <summary>powercfg 的输出是控制台编码（这台机器是 GBK），不换编码中文方案名会变乱码。</summary>
        private static Encoding ConsoleEncoding()
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.Default; }
        }

        private static PowerRun Exec(string exe, string args)
        {
            PowerRun r = new PowerRun();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = ConsoleEncoding();
                psi.StandardErrorEncoding = ConsoleEncoding();
                using (Process p = Process.Start(psi))
                {
                    // 先异步读、再等退出：输出一多，父子互等会卡到超时（这个仓库踩过一次）
                    System.Threading.Tasks.Task<string> o = p.StandardOutput.ReadToEndAsync();
                    System.Threading.Tasks.Task<string> e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(90000)) { r.Output = "（powercfg 90 秒没退出）"; r.Started = false; return r; }
                    r.Started = true;
                    r.ExitCode = p.ExitCode;
                    r.Output = (o.Result + e.Result).Trim();
                    return r;
                }
            }
            catch (Exception ex)
            {
                r.Started = false;
                r.Output = ex.Message;
                return r;
            }
        }

        private static PowerRun Cfg(string args) { return Exec(CfgExe(), args); }

        // ---------------------------------------------------------------- 读状态

        /// <summary>读一个方案里某个设置的值。三种结果：
        /// &gt;= 0 = 值；-1 = **这个方案没单独设**（键在、值不在，或者那个键根本没有 —— 系统会用默认值）；
        /// -2 = 打不开、读不了（权限之类）。⚠ 读和写都要按项兜住：权限不够时 OpenSubKey 会抛（PITFALLS 48）。</summary>
        public static int ReadIndex(string scheme, string sub, string setting, bool ac)
        {
            try
            {
                using (RegistryKey k = RootOpen("User\\PowerSchemes\\" + scheme + "\\" + sub + "\\" + setting, false))
                {
                    if (k == null) { return -1; }
                    object o = k.GetValue(ac ? "ACSettingIndex" : "DCSettingIndex", null);
                    if (o == null) { return -1; }
                    return Convert.ToInt32(o, CultureInfo.InvariantCulture);
                }
            }
            catch { return -2; }
        }

        private static readonly Regex GuidLine = new Regex(
            "([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\\s*(?:\\(([^)]*)\\))?");

        private static readonly string[] BuiltInSchemes = new string[]
        {
            "381b4222-f694-41f0-9685-ff5bb260df2e",   // 平衡
            "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",   // 高性能
            "a1841308-3541-4fab-bc81-f71556f20b4a",   // 节能
            "e9a42b02-d5df-448d-aa00-03f14749eb61"    // 卓越性能
        };

        /// <summary>这台机器上有哪些电源方案（`powercfg /list` 里的 GUID 是唯一的机器可读部分，
        /// 名字和"哪个是当前生效"都从那一行里取）。读不到就返回空表，调用方如实说明。</summary>
        public static List<PowerScheme> Schemes(out string error)
        {
            error = "";
            List<PowerScheme> list = new List<PowerScheme>();
            PowerRun r = Cfg("/list");
            if (!r.Started)
            {
                error = "叫不起 powercfg：" + r.Output;
                return list;
            }
            if (r.ExitCode != 0)
            {
                error = "powercfg /list 失败（退出码 " + r.ExitCode.ToString(CultureInfo.InvariantCulture) + "）"
                    + (r.Output.Length > 0 ? "：" + r.Output : "");
                return list;
            }
            foreach (string line in r.Output.Split('\n'))
            {
                Match m = GuidLine.Match(line);
                if (!m.Success) { continue; }
                string guid = m.Groups[1].Value.ToLowerInvariant();
                PowerScheme s = new PowerScheme();
                s.Guid = guid;
                s.Name = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
                // 当前生效那条尾巴上多一个 *（各语言版本都一样，是符号不是词）
                string tail = line.Substring(m.Index + m.Length);
                s.Active = (tail.IndexOf('*') >= 0);
                s.ThirdParty = true;
                foreach (string b in BuiltInSchemes)
                {
                    if (string.Equals(b, guid, StringComparison.OrdinalIgnoreCase)) { s.ThirdParty = false; break; }
                }
                // 同一个 GUID 出现两次（有些系统会重复列）只留第一条
                bool dup = false;
                foreach (PowerScheme old in list)
                {
                    if (string.Equals(old.Guid, s.Guid, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (dup) { continue; }
                s.LidAc = ReadIndex(s.Guid, SubButtons, SetLid, true);
                s.LidDc = ReadIndex(s.Guid, SubButtons, SetLid, false);
                s.PwrAc = ReadIndex(s.Guid, SubButtons, SetPowerButton, true);
                s.PwrDc = ReadIndex(s.Guid, SubButtons, SetPowerButton, false);
                s.SlpAc = ReadIndex(s.Guid, SubButtons, SetSleepButton, true);
                s.SlpDc = ReadIndex(s.Guid, SubButtons, SetSleepButton, false);
                list.Add(s);
            }
            if (list.Count == 0) { error = "powercfg /list 里没认出一个方案（输出：" + r.Output + "）"; }
            return list;
        }

        private static string Pair(int ac, int dc)
        {
            return Word(ac) + " / " + Word(dc);
        }

        /// <summary>一个值的写法：-1 = 没单独设（跟系统默认一样）、-2 = 读不了。</summary>
        public static string Word(int v)
        {
            if (v == -1) { return "没单独设"; }
            if (v == -2) { return "读不了"; }
            return v.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>多数方案用的那个值（某个设置项）。都读不到就 -1。
        /// 窗口里下拉框拿它当兜底：当前方案没单独设的时候，显示"大多数方案用的值"比显示 0 靠谱。</summary>
        public static int Majority(List<PowerScheme> list, int which)
        {
            int best = -1, bestN = 0;
            int[] candidates = new int[6];
            int c = 0;
            foreach (PowerScheme s in list)
            {
                int v = Which(s, which, true);
                if (v < 0) { continue; }
                bool seen = false;
                for (int i = 0; i < c; i++) { if (candidates[i] == v) { seen = true; } }
                if (!seen && c < candidates.Length) { candidates[c++] = v; }
            }
            for (int i = 0; i < c; i++)
            {
                int n = 0;
                foreach (PowerScheme s in list)
                {
                    if (Which(s, which, true) == candidates[i]) { n++; }
                    if (Which(s, which, false) == candidates[i]) { n++; }
                }
                if (n > bestN) { bestN = n; best = candidates[i]; }
            }
            return best;
        }

        /// <summary>which: 0 = 合盖、1 = 电源按钮、2 = 睡眠按钮；ac = 接电源那个值。</summary>
        private static int Which(PowerScheme s, int which, bool ac)
        {
            if (which == 0) { return ac ? s.LidAc : s.LidDc; }
            if (which == 1) { return ac ? s.PwrAc : s.PwrDc; }
            return ac ? s.SlpAc : s.SlpDc;
        }

        // ---------------------------------------------------------------- 体检（只读）

        /// <summary>只读体检：列出每个方案 × 三个动作 × 接电源/用电池，把"跟多数方案不一样"的挑出来，
        /// 并且明说当前生效的是哪个、它是不是第三方工具建的。
        /// 这一条就是用户 2026-10-19 那个问题的判据：改了当前方案却失效，是因为活动方案被换走了。</summary>
        public static string Fit()
        {
            string error;
            List<PowerScheme> list = Schemes(out error);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("电源方案体检（只读，一个字节都没改）");
            sb.AppendLine();
            if (list.Count == 0)
            {
                sb.Append("  读不了电源方案：").Append(error).AppendLine();
                return sb.ToString();
            }

            PowerScheme active = null;
            int thirdParty = 0;
            foreach (PowerScheme s in list)
            {
                if (s.Active && active == null) { active = s; }
                if (s.ThirdParty) { thirdParty++; }
            }

            // "多数方案"的合盖动作：按 (AC,DC) 数一遍，出现最多的那个就是对大多数
            int bestAc = 3, bestDc = 3, bestN = -1;
            foreach (PowerScheme a in list)
            {
                if (a.LidAc < 0 || a.LidDc < 0) { continue; }
                int n = 0;
                foreach (PowerScheme b in list)
                {
                    if (b.LidAc == a.LidAc && b.LidDc == a.LidDc) { n++; }
                }
                if (n > bestN) { bestN = n; bestAc = a.LidAc; bestDc = a.LidDc; }
            }

            sb.Append("  这台机器上共 ").Append(list.Count.ToString(CultureInfo.InvariantCulture))
              .Append(" 个电源方案");
            if (active != null)
            {
                sb.Append("，当前生效的是「").Append(active.Label).Append("」");
                if (active.ThirdParty) { sb.Append("（不是 Windows 自带的方案）"); }
            }
            sb.AppendLine("。");
            if (thirdParty > 0)
            {
                sb.Append("  其中 ").Append(thirdParty.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个不是 Windows 自带的（多半是别的软件建出来的）——")
                  .AppendLine("那类工具会自己换方案，所以只改某一个方案是不够的。");
            }
            sb.AppendLine();

            sb.Append("  ").Append(RegEngine.PadCjk("方案", 26))
              .Append(RegEngine.PadCjk("合盖（接电源/电池）", 22))
              .Append(RegEngine.PadCjk("电源按钮", 16)).Append("睡眠按钮").AppendLine();
            int odd = 0;
            foreach (PowerScheme s in list)
            {
                bool differs = (s.LidAc >= 0 && s.LidDc >= 0 && (s.LidAc != bestAc || s.LidDc != bestDc));
                if (differs) { odd++; }
                string name = s.Label + (s.Active ? "（当前）" : "") + (s.ThirdParty ? "（第三方）" : "");
                sb.Append("  ").Append(differs ? "! " : "  ").Append(RegEngine.PadCjk(name, 26))
                  .Append(RegEngine.PadCjk(Pair(s.LidAc, s.LidDc), 22))
                  .Append(RegEngine.PadCjk(Pair(s.PwrAc, s.PwrDc), 16))
                  .Append(Pair(s.SlpAc, s.SlpDc));
                if (differs)
                {
                    sb.Append("   ← 跟多数方案不一样（多数是「").Append(ActionLabel(bestAc)).Append("」）");
                }
                sb.AppendLine();
            }
            sb.AppendLine();

            if (odd == 0 && bestN > 0)
            {
                sb.Append("  所有方案的合盖动作都是一致的（").Append(ActionLabel(bestAc)).Append("）——")
                  .AppendLine("闭合盖子时不会因为你切了方案而变。");
            }
            else if (bestN <= 0)
            {
                sb.AppendLine("  没读到任何方案的合盖动作值（0 = 不操作 / 1 = 睡眠 / 2 = 休眠 / 3 = 关机 都没读到）。");
            }
            else
            {
                sb.Append("  有 ").Append(odd.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个方案的合盖动作跟多数不一样：切到那个方案，合盖就不是「")
                  .Append(ActionLabel(bestAc)).Append("」了。").AppendLine();
                sb.Append("  点「合盖与电源键动作」可以把所有方案一次改成一样（改前会记原值，随时能还原）。").AppendLine();
            }
            if (!HibernateEnabled())
            {
                sb.AppendLine("  另外：这台机器的休眠是关着的 —— 哪个方案把合盖设成「休眠」，合盖时就等于什么都不做。");
            }
            bool anyUnset = false, anyUnreadable = false;
            foreach (PowerScheme s in list)
            {
                int[] vs = new int[] { s.LidAc, s.LidDc, s.PwrAc, s.PwrDc, s.SlpAc, s.SlpDc };
                foreach (int v in vs)
                {
                    if (v == -1) { anyUnset = true; }
                    if (v == -2) { anyUnreadable = true; }
                }
            }
            if (anyUnset)
            {
                sb.AppendLine("  表里写「没单独设」的地方 = 那个方案没给这一项设过值（用的是系统默认），不是读失败。");
            }
            if (anyUnreadable)
            {
                sb.AppendLine("  表里写「读不了」的地方 = 权限不够，这一项没读到（用管理员跑一次就能看见）。");
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 改（记原值 → 写 → 读回核对）

        public static List<PowerRecord> LoadRecords()
        {
            List<PowerRecord> list = new List<PowerRecord>();
            try
            {
                if (!File.Exists(BackupPath)) { return list; }
                foreach (string line in File.ReadAllLines(BackupPath, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) { continue; }
                    string[] p = line.Split('\t');
                    if (p.Length < 7) { continue; }
                    PowerRecord r = new PowerRecord();
                    r.Guid = p[0].Trim();
                    r.LidAc = ParseInt(p[1]); r.LidDc = ParseInt(p[2]);
                    r.PwrAc = ParseInt(p[3]); r.PwrDc = ParseInt(p[4]);
                    r.SlpAc = ParseInt(p[5]); r.SlpDc = ParseInt(p[6]);
                    list.Add(r);
                }
            }
            catch { }
            return list;
        }

        private static int ParseInt(string s)
        {
            int n;
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { return n; }
            return -1;
        }

        public static int RecordCount() { return LoadRecords().Count; }

        /// <summary>第一次改之前，「选择关闭盖子的功能」那一页在控制面板里是藏着的吗？
        /// 1 = 藏着的、0 = 本来就看得见、-1 = 没有记录（没改过）。
        /// 不记这一条的话，「还原成改前的值」会把\"我们把它翻出来了\"这个改动留下（用户会以为没还原干净）。</summary>
        public static int RecordedVisible()
        {
            try
            {
                if (!File.Exists(BackupPath)) { return -1; }
                foreach (string line in File.ReadAllLines(BackupPath, Encoding.UTF8))
                {
                    string t = line.Trim();
                    if (!t.StartsWith("#")) { continue; }
                    int eq = t.IndexOf('=');
                    if (eq < 0) { continue; }
                    if (t.Substring(1, eq - 1).Trim() != "原本可见") { continue; }
                    return ParseInt(t.Substring(eq + 1));
                }
            }
            catch { }
            return -1;
        }

        private static string SaveRecords(List<PowerRecord> list, int visibleAtStart)
        {
            try
            {
                AppPaths.EnsureBase();
                StringBuilder sb = new StringBuilder();
                sb.Append("# 萌新工具箱 · 电源方案改动前的原值（「还原成改前的值」按它写回去；删掉这个文件就等于放弃还原）").AppendLine();
                sb.Append("# 原本可见=").Append(visibleAtStart < 0 ? "?" : visibleAtStart.ToString(CultureInfo.InvariantCulture))
                  .AppendLine("（1 = 改之前「选择关闭盖子的功能」那一页在控制面板里看得见，0 = 藏着）");
                sb.Append("# 方案GUID\t合盖(接电源)\t合盖(用电池)\t电源按钮(接电源)\t电源按钮(用电池)\t睡眠按钮(接电源)\t睡眠按钮(用电池)").AppendLine();
                foreach (PowerRecord r in list)
                {
                    sb.Append(r.Guid).Append('\t')
                      .Append(r.LidAc.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.LidDc.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.PwrAc.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.PwrDc.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.SlpAc.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.SlpDc.ToString(CultureInfo.InvariantCulture)).AppendLine();
                }
                File.WriteAllText(BackupPath, sb.ToString(), new UTF8Encoding(false));
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>把一个值写进去（走 powercfg 官方那条路）。</summary>
        private static string WriteIndex(string scheme, string sub, string setting, bool ac, int value)
        {
            string args = (ac ? "/setacvalueindex " : "/setdcvalueindex ")
                + scheme + " " + sub + " " + setting + " "
                + value.ToString(CultureInfo.InvariantCulture);
            PowerRun r = Cfg(args);
            if (!r.Started) { return "叫不起 powercfg：" + r.Output; }
            if (r.ExitCode != 0)
            {
                return "powercfg 退出码 " + r.ExitCode.ToString(CultureInfo.InvariantCulture)
                    + (r.Output.Length > 0 ? "：" + r.Output : "");
            }
            return "";
        }

        /// <summary>让当前方案重新生效一次（写完不重新激活的话，活动方案可能还用着旧的策略）。</summary>
        private static string Activate(string scheme)
        {
            PowerRun r = Cfg("/setactive " + scheme);
            if (!r.Started) { return "叫不起 powercfg：" + r.Output; }
            if (r.ExitCode != 0)
            {
                return "powercfg /setactive 退出码 " + r.ExitCode.ToString(CultureInfo.InvariantCulture)
                    + (r.Output.Length > 0 ? "：" + r.Output : "");
            }
            return "";
        }

        /// <summary>把三个动作的所有方案都改成给定值。返回人看的报告；ok=false 表示有地方没成功。</summary>
        public static string ApplyValues(int lidAc, int lidDc, int pwrAc, int pwrDc, int slpAc, int slpDc, out bool ok)
        {
            ok = false;
            StringBuilder sb = new StringBuilder();
            string error;
            List<PowerScheme> list = Schemes(out error);
            if (list.Count == 0)
            {
                sb.Append("  读不了电源方案，一个字都没改：").Append(error).AppendLine();
                return sb.ToString();
            }

            sb.Append("【合盖 / 电源按钮 / 睡眠按钮】改成：合盖「").Append(ActionLabel(lidAc)).Append("」")
              .Append("、电源按钮「").Append(ActionLabel(pwrAc)).Append("」")
              .Append("、睡眠按钮「").Append(ActionLabel(slpAc)).Append("」").AppendLine();
            sb.Append("  接电源和用电池分开设：合盖 ")
              .Append(ActionLabel(lidAc)).Append(" / ").Append(ActionLabel(lidDc))
              .Append("，电源按钮 ").Append(ActionLabel(pwrAc)).Append(" / ").Append(ActionLabel(pwrDc))
              .Append("，睡眠按钮 ").Append(ActionLabel(slpAc)).Append(" / ").Append(ActionLabel(slpDc))
              .AppendLine();
            sb.Append("  改的是所有 ").Append(list.Count.ToString(CultureInfo.InvariantCulture))
              .AppendLine(" 个电源方案（只改当前那种做法会被第三方工具换方案换掉）。");
            sb.AppendLine();

            // ① 先把没记过的方案的原值写进磁盘：这一步失败就一个字都不动
            // ⚠ 「原本可见」只在**第一次**记（后面又冒出个新方案时不能拿当时的可见状态覆盖第一次的原样）
            int visibleAtStart = RecordedVisible();
            if (visibleAtStart < 0) { visibleAtStart = Hidden() ? 0 : 1; }
            List<PowerRecord> records = LoadRecords();
            List<PowerRecord> fresh = new List<PowerRecord>();
            foreach (PowerScheme s in list)
            {
                bool known = false;
                foreach (PowerRecord r in records)
                {
                    if (string.Equals(r.Guid, s.Guid, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                }
                if (known) { continue; }
                PowerRecord nr = new PowerRecord();
                nr.Guid = s.Guid;
                nr.LidAc = s.LidAc; nr.LidDc = s.LidDc;
                nr.PwrAc = s.PwrAc; nr.PwrDc = s.PwrDc;
                nr.SlpAc = s.SlpAc; nr.SlpDc = s.SlpDc;
                fresh.Add(nr);
            }
            if (fresh.Count > 0)
            {
                records.AddRange(fresh);
                string saveError = SaveRecords(records, visibleAtStart);
                if (saveError.Length > 0)
                {
                    sb.Append("  原值没记下来（").Append(saveError).Append("），所以一个字都没改。").AppendLine();
                    sb.Append("  没法还原的动作工具箱不做 —— 先看看 ").Append(BackupPath)
                      .Append(" 这个文件能不能写。").AppendLine();
                    return sb.ToString();
                }
                sb.Append("  写之前记下了 ").Append(fresh.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个方案的原值，记在 ").Append(BackupPath).AppendLine();
            }
            else
            {
                sb.Append("  这些方案的原值早就记着了（第一次改的时候就记了，不会被这次覆盖）：")
                  .Append(BackupPath).AppendLine();
            }
            sb.AppendLine();

            // ② 逐方案写（只写跟目标不一样的，省得白跑几十次 powercfg）
            int written = 0, already = 0, failed = 0;
            List<string> errors = new List<string>();
            foreach (PowerScheme s in list)
            {
                StringBuilder line = new StringBuilder();
                line.Append("  ").Append(RegEngine.PadCjk(s.Label + (s.Active ? "（当前生效）" : ""), 26));
                bool[] acs = new bool[] { true, false };
                string[] subs = new string[] { SubButtons, SubButtons, SubButtons };
                string[] sets = new string[] { SetLid, SetPowerButton, SetSleepButton };
                int[] acv = new int[] { lidAc, pwrAc, slpAc };
                int[] dcv = new int[] { lidDc, pwrDc, slpDc };
                int[] nowAc = new int[] { s.LidAc, s.PwrAc, s.SlpAc };
                int[] nowDc = new int[] { s.LidDc, s.PwrDc, s.SlpDc };
                bool schemeOk = true;
                for (int i = 0; i < 3; i++)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        bool ac = (k == 0);
                        int want = ac ? acv[i] : dcv[i];
                        int have = ac ? nowAc[i] : nowDc[i];
                        if (have == want)
                        {
                            already++;
                            continue;
                        }
                        string werr = WriteIndex(s.Guid, subs[i], sets[i], ac, want);
                        if (werr.Length > 0)
                        {
                            schemeOk = false;
                            failed++;
                            errors.Add(s.Label + " · " + (ac ? "接电源" : "用电池") + " · "
                                + SettingName(sets[i]) + "：" + werr);
                            continue;
                        }
                        written++;
                    }
                }
                // ③ 读回核对（三个动作、两种供电都读一遍）
                int bad = 0;
                if (ReadIndex(s.Guid, SubButtons, SetLid, true) != lidAc) { bad++; }
                if (ReadIndex(s.Guid, SubButtons, SetLid, false) != lidDc) { bad++; }
                if (ReadIndex(s.Guid, SubButtons, SetPowerButton, true) != pwrAc) { bad++; }
                if (ReadIndex(s.Guid, SubButtons, SetPowerButton, false) != pwrDc) { bad++; }
                if (ReadIndex(s.Guid, SubButtons, SetSleepButton, true) != slpAc) { bad++; }
                if (ReadIndex(s.Guid, SubButtons, SetSleepButton, false) != slpDc) { bad++; }
                if (bad > 0)
                {
                    schemeOk = false;
                    failed++;
                    errors.Add(s.Label + "：写完读回来有 " + bad.ToString(CultureInfo.InvariantCulture)
                        + " 个值不是要写的那样");
                }
                line.Append(schemeOk ? "√ " : "× ")
                    .Append(RegEngine.PadCjk("合盖 " + Pair(lidAc, lidDc), 18))
                    .Append(RegEngine.PadCjk("电源键 " + Pair(pwrAc, pwrDc), 18))
                    .Append("睡眠键 " + Pair(slpAc, slpDc));
                if (s.Active) { line.Append("   （写完会重新激活它，立刻生效）"); }
                sb.AppendLine(line.ToString());
            }

            // ④ 重新激活当前方案，让新值立刻生效
            string actError = "";
            if (failed == 0)
            {
                if (ActiveGuid(list).Length > 0) { actError = Activate(ActiveGuid(list)); }
            }
            sb.AppendLine();
            foreach (string e in errors) { sb.Append("  × ").Append(e).AppendLine(); }
            if (failed > 0)
            {
                sb.Append("  有地方没成功 —— 上面带 × 的就是（多半是缺管理员权限）。").AppendLine();
                sb.Append("  已经写进去的那部分没回滚：再点一次「应用」或者点「还原成改前的值」都行。").AppendLine();
                return sb.ToString();
            }
            if (actError.Length > 0)
            {
                sb.Append("  值都写对了，但重新激活当前方案那一步失败（").Append(actError).Append("）：")
                  .AppendLine("关一次盖子 / 切一下电源方案就会读到新值。");
                ok = true;   // 值确实改对了，只是"立刻生效"差一步
                return sb.ToString();
            }

            sb.Append("  改了 ").Append(written.ToString(CultureInfo.InvariantCulture)).Append(" 处，")
              .Append(already.ToString(CultureInfo.InvariantCulture)).AppendLine(" 处本来就是对的（没白跑）。");
            sb.Append("  全部读回核对过；当前方案已经重新激活。").AppendLine();
            if (HibernateEnabled())
            {
                sb.AppendLine("  （休眠现在开着，所以选「休眠」是有用的。）");
            }
            else
            {
                sb.AppendLine("  注意：这台机器的休眠是关着的，所以选「休眠」等于合盖什么都不做。");
            }
            sb.Append("  想回到改之前：点「还原成改前的值」（记录在 ").Append(BackupPath).Append("）。").AppendLine();
            ok = true;
            return sb.ToString();
        }

        private static string SettingName(string setting)
        {
            if (string.Equals(setting, SetLid, StringComparison.OrdinalIgnoreCase)) { return "合上盖子"; }
            if (string.Equals(setting, SetPowerButton, StringComparison.OrdinalIgnoreCase)) { return "按电源按钮"; }
            if (string.Equals(setting, SetSleepButton, StringComparison.OrdinalIgnoreCase)) { return "按睡眠按钮"; }
            return setting;
        }

        private static string ActiveGuid(List<PowerScheme> list)
        {
            foreach (PowerScheme s in list)
            {
                if (s.Active) { return s.Guid; }
            }
            return "";
        }

        // ---------------------------------------------------------------- 桌面模式（一键）

        /// <summary>「桌面模式」现在是开着的吗：所有方案的合盖动作（接电源和用电池）都是"不采取任何操作"。</summary>
        public static bool DesktopModeOn()
        {
            string error;
            List<PowerScheme> list = Schemes(out error);
            if (list.Count == 0) { return false; }
            foreach (PowerScheme s in list)
            {
                if (s.LidAc != 0 || s.LidDc != 0) { return false; }
            }
            return true;
        }

        /// <summary>一键切换「桌面模式（合盖不睡）」：合盖动作全改成"不采取任何操作"；已经全是的话改回"关机"。
        /// 电源按钮 / 睡眠按钮那两个动作保持原样（用当前值再写一遍，等于没动）。</summary>
        public static string DesktopToggle(out bool ok)
        {
            string error;
            List<PowerScheme> list = Schemes(out error);
            if (list.Count == 0) { ok = false; return "  读不了电源方案，一个字都没改：" + error + Environment.NewLine; }
            bool on = DesktopModeOn();
            int lidAc = on ? 3 : 0;
            int lidDc = on ? 3 : 0;
            // 其它两个动作：保持每个方案现在的值（读不到就读当前方案的值兜底）
            int pwrAc = 3, pwrDc = 3, slpAc = 1, slpDc = 1;
            PowerScheme first = list[0];
            foreach (PowerScheme s in list)
            {
                if (s.Active) { first = s; break; }
            }
            pwrAc = first.PwrAc >= 0 ? first.PwrAc : pwrAc;
            pwrDc = first.PwrDc >= 0 ? first.PwrDc : pwrDc;
            slpAc = first.SlpAc >= 0 ? first.SlpAc : slpAc;
            slpDc = first.SlpDc >= 0 ? first.SlpDc : slpDc;

            string report = ApplyValues(lidAc, lidDc, pwrAc, pwrDc, slpAc, slpDc, out ok);
            StringBuilder sb = new StringBuilder();
            sb.Append(on
                ? "桌面模式：关掉（合盖动作改回「关机」）" + Environment.NewLine
                : "桌面模式：打开（合盖动作改成「不采取任何操作」）" + Environment.NewLine);
            sb.AppendLine();
            sb.Append(report);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 还原

        public static string Restore(out bool ok)
        {
            ok = false;
            List<PowerRecord> records = LoadRecords();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("还原电源方案改动：按改动前的原值写回去");
            sb.AppendLine();
            if (records.Count == 0)
            {
                ok = true;
                sb.AppendLine("  没有要还原的东西：还没动过这一页的电源设置。");
                sb.Append("  （原值记录应该在 ").Append(BackupPath).Append("）").AppendLine();
                return sb.ToString();
            }

            string error;
            List<PowerScheme> list = Schemes(out error);
            if (list.Count == 0)
            {
                sb.Append("  读不了电源方案，什么都没还原：").Append(error).AppendLine();
                return sb.ToString();
            }

            int done = 0, failed = 0;
            foreach (PowerRecord rec in records)
            {
                string label = rec.Guid;
                bool active = false;
                foreach (PowerScheme s in list)
                {
                    if (string.Equals(s.Guid, rec.Guid, StringComparison.OrdinalIgnoreCase))
                    {
                        label = s.Label;
                        active = s.Active;
                        break;
                    }
                }
                string[] sets = new string[] { SetLid, SetLid, SetPowerButton, SetPowerButton, SetSleepButton, SetSleepButton };
                bool[] acs = new bool[] { true, false, true, false, true, false };
                int[] vals = new int[] { rec.LidAc, rec.LidDc, rec.PwrAc, rec.PwrDc, rec.SlpAc, rec.SlpDc };
                bool lineOk = true;
                for (int i = 0; i < 6; i++)
                {
                    if (vals[i] < 0) { continue; }   // 当时读不到 → 不猜，跳过
                    string werr = WriteIndex(rec.Guid, SubButtons, sets[i], acs[i], vals[i]);
                    if (werr.Length > 0)
                    {
                        lineOk = false;
                        sb.Append("  × ").Append(label).Append(" · ").Append(SettingName(sets[i]))
                          .Append(acs[i] ? "（接电源）" : "（用电池）").Append("：").Append(werr).AppendLine();
                    }
                }
                if (lineOk)
                {
                    done++;
                    sb.Append("  √ ").Append(RegEngine.PadCjk(label, 26))
                      .Append("合盖 ").Append(Pair(rec.LidAc, rec.LidDc))
                      .Append(" · 电源键 ").Append(Pair(rec.PwrAc, rec.PwrDc))
                      .Append(" · 睡眠键 ").Append(Pair(rec.SlpAc, rec.SlpDc)).AppendLine();
                    // 读回核对
                    if (ReadIndex(rec.Guid, SubButtons, SetLid, true) != rec.LidAc
                        && rec.LidAc >= 0)
                    {
                        failed++;
                        lineOk = false;
                        sb.Append("      注意：合盖（接电源）写回去之后读回来不是原值。").AppendLine();
                    }
                }
                else
                {
                    failed++;
                }
                if (active && lineOk) { sb.Append("      （当前方案，下面会重新激活它）").AppendLine(); }
            }

            if (failed == 0)
            {
                if (ActiveGuid(list).Length > 0)
                {
                    string actError = Activate(ActiveGuid(list));
                    if (actError.Length > 0)
                    {
                        sb.Append("  重新激活当前方案失败（").Append(actError).Append("）—— 切一下电源方案就会生效。").AppendLine();
                    }
                }
                try
                {
                    // 「让控制面板能看见」那个开关也放回去：改之前它是藏着的就再藏起来
                    // （这一条不放进 tmp 文件的重试里 —— 它不影响几个动作值，失败只是多说一句）
                    if (RecordedVisible() == 0 && !Hidden())
                    {
                        bool hok;
                        SetHidden(true, out hok);
                        sb.Append("  「选择关闭盖子的功能」那一页也").Append(hok ? "藏回去了" : "没能藏回去")
                          .AppendLine("（改之前它就是藏着的）。");
                    }
                    File.Delete(BackupPath);
                    sb.AppendLine();
                    sb.Append("  ").Append(done.ToString(CultureInfo.InvariantCulture))
                      .AppendLine(" 个方案都还原了；原值记录已经清空（下次再改会重新记当时的原值）。");
                }
                catch (Exception ex)
                {
                    sb.AppendLine();
                    sb.Append("  ").Append(done.ToString(CultureInfo.InvariantCulture))
                      .Append(" 个方案都还原了，但原值记录没删掉（").Append(ex.Message).Append("）。").AppendLine();
                }
                ok = true;
            }
            else
            {
                sb.AppendLine();
                sb.Append("  有 ").Append(failed.ToString(CultureInfo.InvariantCulture))
                  .AppendLine(" 处没还原成功（多半是缺管理员权限），原值记录先留着。");
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 电池健康报告

        /// <summary>`powercfg /batteryreport` 生成 HTML 再打开它；顺手从报告里把
        /// 设计容量 / 现在满充 / 循环次数读出来念一遍（读不出来就只说"报告生成好了"）。</summary>
        public static string BatteryReport(out bool ok)
        {
            ok = false;
            StringBuilder sb = new StringBuilder();
            string file = Path.Combine(AppPaths.BaseDir, "battery-report.html");
            try
            {
                AppPaths.EnsureBase();
                Directory.CreateDirectory(Path.GetDirectoryName(file));
            }
            catch { }
            PowerRun r = Cfg("/batteryreport /output \"" + file + "\"");
            if (!r.Started)
            {
                sb.Append("  叫不起 powercfg：").Append(r.Output).AppendLine();
                return sb.ToString();
            }
            sb.AppendLine("电池健康报告（powercfg /batteryreport）");
            sb.AppendLine();
            if (r.ExitCode != 0 || !File.Exists(file))
            {
                sb.Append("  没生成出来（退出码 ").Append(r.ExitCode.ToString(CultureInfo.InvariantCulture)).Append("）");
                if (r.Output.Length > 0) { sb.Append("：").Append(r.Output); }
                sb.AppendLine();
                sb.AppendLine("  台式机 / 虚拟机没有电池，这条命令本来就会失败。");
                return sb.ToString();
            }
            string html = "";
            try { html = File.ReadAllText(file); } catch { }
            string design = ExtractCell(html, "DESIGN CAPACITY");
            string full = ExtractCell(html, "FULL CHARGE CAPACITY");
            string cycles = ExtractCell(html, "CYCLE COUNT");
            if (design.Length > 0 || full.Length > 0)
            {
                sb.Append("  设计容量：").Append(design.Length > 0 ? design : "读不到").AppendLine();
                sb.Append("  现在满充：").Append(full.Length > 0 ? full : "读不到").AppendLine();
                sb.Append("  循环次数：").Append(cycles.Length > 0 ? cycles : "读不到").AppendLine();
                long d = FirstNumber(design);
                long f = FirstNumber(full);
                if (d > 0 && f > 0)
                {
                    long pct = (f * 100) / d;
                    sb.Append("  健康度：约 ").Append(pct.ToString(CultureInfo.InvariantCulture))
                      .Append("%（现在满充 ÷ 设计容量）—— 低于 80% 就该考虑换电池了").AppendLine();
                }
            }
            else
            {
                sb.AppendLine("  报告生成好了（这台机器的报告里没读到容量那一行，直接打开看）。");
            }
            sb.AppendLine();
            sb.Append("  报告文件：").Append(file).AppendLine();
            bool opened = false;
            if (Environment.GetEnvironmentVariable("MXX1_NO_OPEN") != "1")
            {
                try
                {
                    Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                    opened = true;
                }
                catch { }
            }
            sb.Append(opened ? "  已经用浏览器打开了。" : "  （没自动打开：自己双击那个文件就行）").AppendLine();
            ok = true;
            return sb.ToString();
        }

        /// <summary>从报告 HTML 里取 `&lt;td&gt;标签&lt;/td&gt;&lt;td&gt;值&lt;/td&gt;` 的那个值。</summary>
        private static string ExtractCell(string html, string label)
        {
            if (html.Length == 0) { return ""; }
            Match m = Regex.Match(html, Regex.Escape(label) + "\\s*</td>\\s*<td[^>]*>\\s*([^<]*)",
                RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static long FirstNumber(string text)
        {
            Match m = Regex.Match(text, "[0-9][0-9,\\.]*");
            if (!m.Success) { return 0; }
            string s = m.Value.Replace(",", "").Replace(".", "");
            long n;
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { return n; }
            return 0;
        }

        // ---------------------------------------------------------------- 谁在阻止睡眠

        /// <summary>只读：谁手里按着"不许睡"（/requests）、上次是谁把它唤醒的（/lastwake）、
        /// 哪些设备允许唤醒（/devicequery wake_armed）。</summary>
        public static string WakeReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("谁在阻止睡眠 / 谁唤醒了它（只读，一个字节都没改）");
            sb.AppendLine();
            sb.AppendLine("【谁按着不让电脑睡（powercfg /requests）】");
            PowerRun a = Cfg("/requests");
            if (!a.Started) { sb.Append("  叫不起 powercfg：").Append(a.Output).AppendLine(); }
            else
            {
                sb.AppendLine(Indent(a.Output));
                if (a.Output.Length == 0) { sb.AppendLine("  （没有输出）"); }
            }
            bool isAdmin = false;
            try { isAdmin = Launcher.IsAdmin(); } catch { }
            if (!isAdmin)
            {
                sb.AppendLine("  这一条要管理员才看得全（不是管理员时，系统只列得出本进程自己那几条）。");
            }
            sb.AppendLine();
            sb.AppendLine("【上次是谁把它唤醒的（powercfg /lastwake）】");
            PowerRun b = Cfg("/lastwake");
            sb.AppendLine(b.Started ? Indent(b.Output) : "  叫不起 powercfg：" + b.Output);
            sb.AppendLine();
            sb.AppendLine("【哪些设备能唤醒它（powercfg /devicequery wake_armed）】");
            PowerRun c = Cfg("/devicequery wake_armed");
            sb.AppendLine(c.Started ? Indent(c.Output) : "  叫不起 powercfg：" + c.Output);
            sb.AppendLine();
            sb.Append("（这几条都是「念系统给的原文」，工具箱一个字都没改。）");
            return sb.ToString();
        }

        private static string Indent(string text)
        {
            if (text == null) { return ""; }
            text = text.Trim();
            if (text.Length == 0) { return ""; }
            StringBuilder sb = new StringBuilder();
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                sb.Append("  ").Append(line.TrimEnd()).AppendLine();
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 「选择关闭盖子的功能」这一页

        /// <summary>这几个设置在控制面板里是不是藏着的（Attributes 的 0x1 = 隐藏）。
        /// 用户 2026-10-19 报的"想设置却找不到地方"就是这个：系统默认把这一页藏起来。</summary>
        public static bool Hidden()
        {
            string[] sets = new string[] { SetLid, SetPowerButton, SetSleepButton };
            foreach (string s in sets)
            {
                try
                {
                    using (RegistryKey k = RootOpen("PowerSettings\\" + SubButtons + "\\" + s, false))
                    {
                        if (k == null) { continue; }
                        object o = k.GetValue("Attributes", null);
                        if (o == null) { continue; }
                        if ((Convert.ToInt32(o, CultureInfo.InvariantCulture) & 1) != 0) { return true; }
                    }
                }
                catch { }
            }
            return false;
        }

        /// <summary>让控制面板里能（或不能）看见「选择关闭盖子的功能」这一页。
        /// 用系统自己的开关 `powercfg -attributes &lt;子组&gt; &lt;设置&gt; ±ATTRIB_HIDE`，
        /// 不是一个自创的注册表写法（可逆：再点一次就是还原）。</summary>
        public static string SetHidden(bool hide, out bool ok)
        {
            ok = true;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(hide ? "把「选择关闭盖子的功能」这一页重新藏起来" : "让「选择关闭盖子的功能」这一页出现在控制面板里");
            sb.AppendLine();
            string[] sets = new string[] { SetLid, SetPowerButton, SetSleepButton };
            foreach (string s in sets)
            {
                PowerRun r = Cfg("-attributes " + SubButtons + " " + s + (hide ? " +ATTRIB_HIDE" : " -ATTRIB_HIDE"));
                if (!r.Started || r.ExitCode != 0)
                {
                    ok = false;
                    sb.Append("  × ").Append(SettingName(s)).Append("：")
                      .Append(!r.Started ? "叫不起 powercfg：" + r.Output : "退出码 " + r.ExitCode.ToString(CultureInfo.InvariantCulture))
                      .AppendLine();
                    continue;
                }
                sb.Append("  √ ").Append(SettingName(s)).AppendLine();
            }
            sb.AppendLine();
            if (ok)
            {
                sb.Append(hide
                    ? "  现在这一页又藏起来了（和系统默认一样）。"
                    : "  现在打开「控制面板 - 电源选项 - 选择电源按钮的功能」就能看到这三项了。").AppendLine();
            }
            else
            {
                sb.Append("  有没成功的（多半是缺管理员权限）。").AppendLine();
            }
            return sb.ToString();
        }
    }
}
