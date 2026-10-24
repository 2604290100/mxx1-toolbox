// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Mxx1Toolbox
{
    /// <summary>一条定时任务的记录（存在 `%LOCALAPPDATA%\mxx1-toolbox\timer.txt`，一行一个
    /// `key=value`）。它就是"现在到底有没有在计时"的唯一凭据 —— 界面的状态行、命令行的
    /// `timer status`、后台进程"是不是该退出了"读的都是这一份。</summary>
    internal sealed class TimerRecord
    {
        public string Action = "";      // shutdown / restart / sleep
        public long At = 0;             // 到点的时刻（Unix 秒）
        public long Started = 0;        // 定下这条任务的时刻
        public int Pid = 0;             // 睡眠：等着它的后台进程；关机 / 重启 = 0（系统自己记着）
        public string Token = "";       // 每条任务一个随机串：换掉 / 取消之后，老的后台进程认得出"已经不是我"
        public string Mode = "";        // system = 交给 Windows；worker = 工具箱自己的后台进程

        public string Label { get { return PowerTimer.LabelOf(Action); } }
        public DateTime AtLocal { get { return PowerTimer.FromEpoch(At); } }
    }

    /// <summary>读过记录之后算出来的"现在是什么状态"（`PowerTimer.Status()` 的返回值）。</summary>
    internal sealed class TimerStatus
    {
        public bool Present;            // 磁盘上有记录
        public bool Active;             // 还没到点
        public bool Stale;              // 过了点（多半是电脑重启过 / 工具箱被结束 / 系统拦下了）
        public int Remaining;           // 还剩几秒（Active 时才有意义）
        public TimerRecord Record;
        public string Text = "";        // 直接显示给用户的那一句 / 两句话
    }

    /// <summary>「定时关机 / 重启 / 睡眠」的后端（2026-10-20 用户点名的 F4）。
    ///
    /// ## 三条分工（为什么不是一个办法全包）
    /// ① **关机和重启交给 Windows 自己**（`shutdown.exe /s|/r /t N`）：倒计时是系统记着的 ——
    ///    工具箱关掉、缩进托盘、甚至崩溃都不影响它；到点前 Windows 自己会弹「Windows 将在一分钟内
    ///    关闭」，任何程序（包括用户自己）都能用 `shutdown /a` 撤掉。我们点「取消定时」跑的就是它。
    /// ② **睡眠只能靠自己的后台进程**：系统没有"到点睡眠"的命令（`shutdown.exe` 只有 /h 休眠）。
    ///    所以起一个**自己这个 exe**的小进程（`timer worker ...`，不开窗、不弹控制台、不走 PowerShell
    ///    —— 火绒那条线别再踩）：它每隔 250 毫秒看一次记录还在不在，到点调 `SetSuspendState`。
    ///    到点前 60 秒弹一张提示卡（顺手能取消）。
    /// ③ **记录文件说了算**：换一条定时 = 先撤掉旧的、写新记录、再动手。后台进程每次都核对自己
    ///    那个 `token` —— 记录没了 / 换成新的了，它就安静退出。这样"取消了但还在等"的晕状态不会出现。
    ///
    /// ## 两条底线
    /// · 命令行**没有"开始计时"的入口**（开始只能从界面点，要过条款门 + 确认框）—— 同
    ///   `sysreg` / `rightmenu` / `power` 那几页的规矩。命令行只有只读的 `timer status` /
    ///   `timer plan` 和"撤掉"的 `timer cancel`。
    /// · 绝不谎报：状态行里"过了点但没看到执行"会如实说"可能电脑重启过 / 工具箱被结束 / 系统拦下了"。
    ///
    /// ## 测试怎么保证不真的关掉测试机
    /// `MXX1_TIMER_DRYRUN=1` 时**一个字节都不发**：不调 `shutdown.exe`、不调 `SetSuspendState`，
    /// 后台进程照常起来、照常等到点，只是把"本来要做什么"写进日志（界面套件整套都带这个开关）。
    /// `MXX1_TIMER_STATE` 把记录文件挪到测试自己的临时目录（绝不碰用户真实的定时任务）。
    /// `timer plan` 是只读的：打出来的命令就是真跑时用的那一条（同一份 `ShutdownArgs`）。</summary>
    internal static class PowerTimer
    {
        public const string ActionShutdown = "shutdown";
        public const string ActionRestart = "restart";
        public const string ActionSleep = "sleep";

        /// <summary>离现在少于这么多秒就不接受（太近了连确认框都来不及看）。</summary>
        public const int MinSeconds = 10;

        /// <summary>「N 分钟后」最多能定多久（1440 分钟 = 24 小时）。</summary>
        public const int MaxMinutes = 1440;

        /// <summary>睡眠那条：到点前多少秒弹提示卡。</summary>
        public const int WarnBeforeSec = 60;

        /// <summary>提示卡显示多久（毫秒）。比默认的 5 秒长一点：来晚了也还看得见。</summary>
        private const int WarnCardMs = 6000;

        // ------------------------------------------------------------------ 基本名词

        public static string LabelOf(string action)
        {
            string a = Normalize(action);
            if (a == ActionShutdown) { return "关机"; }
            if (a == ActionRestart) { return "重启"; }
            if (a == ActionSleep) { return "睡眠"; }
            return (action == null || action.Length == 0) ? "（没写动作）" : action;
        }

        /// <summary>只认这三个动作；别的一律返回空串。</summary>
        public static string Normalize(string action)
        {
            string a = (action == null) ? "" : action.Trim().ToLowerInvariant();
            if (a == ActionShutdown || a == ActionRestart || a == ActionSleep) { return a; }
            return "";
        }

        // ------------------------------------------------------------------ 记录文件 / 环境开关

        /// <summary>记录文件。`MXX1_TIMER_STATE` 可以把整份记录挪走（回归测试用它）。</summary>
        public static string StatePath
        {
            get
            {
                string e = Environment.GetEnvironmentVariable("MXX1_TIMER_STATE");
                if (e != null && e.Trim().Length > 0) { return AppPaths.Expand(e.Trim()); }
                return Path.Combine(AppPaths.BaseDir, "timer.txt");
            }
        }

        /// <summary>真跑时用的那个 shutdown.exe（固定是系统目录里那个，没有可配置的地方 ——
        /// 免得"配置里指了别的程序"变成一条暗门）。</summary>
        public static string ShutdownExe
        {
            get
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                return Path.Combine(sys, "shutdown.exe");
            }
        }

        /// <summary>试运行：不调 shutdown.exe、不真的睡（测试与排查用）。</summary>
        public static bool DryRun
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("MXX1_TIMER_DRYRUN");
                return v != null && v.Trim() == "1";
            }
        }

        // ------------------------------------------------------------------ 时间

        public static long Epoch(DateTime when)
        {
            DateTime utc = when.ToUniversalTime();
            return (long)Math.Floor((utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
        }

        public static DateTime FromEpoch(long epoch)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(epoch).ToLocalTime();
        }

        /// <summary>「N 分钟后」那条：从 `now` 往后 `minutes` 分钟。</summary>
        public static DateTime FromNow(int minutes, DateTime now)
        {
            if (minutes < 1) { minutes = 1; }
            if (minutes > MaxMinutes) { minutes = MaxMinutes; }
            return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second)
                .AddMinutes(minutes);
        }

        /// <summary>「指定时间 HH:mm」那条：今天这个点还没到就是今天，**已经过了就定到明天**
        /// （差 30 秒以内也算"过了"：给确认框留点时间）。</summary>
        public static DateTime AtClock(int hour, int minute, DateTime now)
        {
            if (hour < 0) { hour = 0; }
            if (hour > 23) { hour = 23; }
            if (minute < 0) { minute = 0; }
            if (minute > 59) { minute = 59; }
            DateTime at = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
            if (at <= now.AddSeconds(30)) { at = at.AddDays(1); }
            return at;
        }

        /// <summary>「今天 23:45」/「明天 08:30」/「10-22 08:30」—— 给状态行和确认框用。</summary>
        public static string WhenText(DateTime at, DateTime now)
        {
            string hm = at.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (at.Date == now.Date) { return "今天 " + hm; }
            if (at.Date == now.Date.AddDays(1)) { return "明天 " + hm; }
            return at.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>还剩多久："29 分 58 秒" / "1 小时 03 分" / "12 秒"。人话，给人看的。</summary>
        public static string SpanText(int seconds)
        {
            if (seconds < 0) { seconds = 0; }
            if (seconds >= 3600)
            {
                int h = seconds / 3600;
                int m = (seconds % 3600) / 60;
                return h.ToString(CultureInfo.InvariantCulture) + " 小时 "
                    + m.ToString("00", CultureInfo.InvariantCulture) + " 分";
            }
            if (seconds >= 60)
            {
                return (seconds / 60).ToString(CultureInfo.InvariantCulture) + " 分 "
                    + (seconds % 60).ToString("00", CultureInfo.InvariantCulture) + " 秒";
            }
            return seconds.ToString(CultureInfo.InvariantCulture) + " 秒";
        }

        /// <summary>状态行里那个"还剩 mm:ss"（短、不跳动得太难看）。</summary>
        public static string CountdownText(int seconds)
        {
            if (seconds < 0) { seconds = 0; }
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            int s = seconds % 60;
            if (h > 0)
            {
                return h.ToString(CultureInfo.InvariantCulture) + ":"
                    + m.ToString("00", CultureInfo.InvariantCulture) + ":"
                    + s.ToString("00", CultureInfo.InvariantCulture);
            }
            return m.ToString("00", CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ 记录文件读写

        public static TimerRecord Read()
        {
            // ⚠️ 读的时候要重试几次：别的程序（杀毒 / 索引 / 备份）正好在扫这个文件时，
            // 一次 IOException 就当成"没有记录"会出事 —— 后台进程会以为被取消了而退出。
            // 三次、每次 30 毫秒，足够躲开那种一闪而过的锁。
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    string path = StatePath;
                    if (!File.Exists(path)) { return null; }
                    TimerRecord rec = new TimerRecord();
                    foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        string line = (raw == null) ? "" : raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) { continue; }
                        int eq = line.IndexOf('=');
                        if (eq <= 0) { continue; }
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = line.Substring(eq + 1).Trim();
                        if (k == "action") { rec.Action = v; }
                        else if (k == "token") { rec.Token = v; }
                        else if (k == "mode") { rec.Mode = v; }
                        else if (k == "at") { rec.At = ParseLong(v); }
                        else if (k == "started") { rec.Started = ParseLong(v); }
                        else if (k == "pid") { rec.Pid = (int)ParseLong(v); }
                    }
                    if (rec.Action.Length == 0 || rec.At <= 0) { return null; }
                    if (rec.Mode.Length == 0) { rec.Mode = (rec.Action == ActionSleep) ? "worker" : "system"; }
                    return rec;
                }
                catch
                {
                    if (attempt < 2) { Thread.Sleep(30); }
                }
            }
            return null;
        }

        private static long ParseLong(string text)
        {
            long v;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) { return v; }
            return 0;
        }

        private static bool Write(TimerRecord rec, out string error)
        {
            error = "";
            try
            {
                AppPaths.EnsureBase();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 萌新工具箱 · 定时关机 / 重启 / 睡眠（取消 = 删掉这个文件，或者用界面 / timer cancel）");
                sb.AppendLine("action=" + rec.Action);
                sb.AppendLine("at=" + rec.At.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("started=" + rec.Started.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("pid=" + rec.Pid.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("token=" + rec.Token);
                sb.AppendLine("mode=" + rec.Mode);
                string path = StatePath;
                string dir = Path.GetDirectoryName(path);
                if (dir != null && dir.Length > 0) { Directory.CreateDirectory(dir); }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                error = "记录写不下去（" + StatePath + "）：" + ex.Message;
                return false;
            }
        }

        private static void Delete()
        {
            try
            {
                string path = StatePath;
                if (File.Exists(path)) { File.Delete(path); }
            }
            catch (Exception ex)
            {
                Logger.Write("定时关机", "记录删不掉：" + ex.Message);
            }
        }

        /// <summary>只有"还是我这条"才删（别把后来那条的记录删掉）。</summary>
        private static void DeleteIfToken(string token)
        {
            TimerRecord rec = Read();
            if (rec == null) { return; }
            if (string.Equals(rec.Token, token, StringComparison.Ordinal)) { Delete(); }
        }

        // ------------------------------------------------------------------ 状态

        /// <summary>现在是什么状态（只读：不改记录、不碰任何东西）。</summary>
        public static TimerStatus Status()
        {
            TimerStatus st = new TimerStatus();
            TimerRecord rec = Read();
            if (rec == null)
            {
                st.Text = "现在没有定时任务";
                return st;
            }
            st.Present = true;
            st.Record = rec;
            DateTime now = DateTime.Now;
            long left = rec.At - Epoch(now);
            if (left > 3)
            {
                st.Active = true;
                st.Remaining = (int)left;
                st.Text = WhenText(rec.AtLocal, now) + " " + rec.Label
                    + " · 还剩 " + CountdownText(st.Remaining)
                    + " —— 想取消就点「取消定时」";
                return st;
            }
            st.Stale = true;
            st.Remaining = 0;
            st.Text = "上一次的定时（" + WhenText(rec.AtLocal, now) + " " + rec.Label
                + "）已经过了时间，但没看到它执行 —— 电脑可能重启过、工具箱被强制结束过，或者系统拦下了。"
                + "点「取消定时」可以清掉这条记录。";
            return st;
        }

        /// <summary>状态行的完整正文（界面里那块"现在"下面显示的就是它 + 一句提醒）。</summary>
        public static string StatusText()
        {
            TimerStatus st = Status();
            if (!st.Present) { return st.Text; }
            if (st.Stale) { return st.Text; }
            string tail = (st.Record.Action == ActionSleep)
                ? "（睡眠由工具箱自己的后台进程等着，工具箱关了也照样执行；到点前 60 秒会弹一张提示卡）"
                : "（倒计时由 Windows 自己记着，工具箱关了也照样执行；到点前 Windows 会自己弹提示）";
            return st.Text + Environment.NewLine + tail;
        }

        // ------------------------------------------------------------------ 开始 / 取消

        /// <summary>命令行那条会真的执行的命令（`timer plan` 打的就是它，和真跑时同一份）。</summary>
        public static string ShutdownArgs(string action, int seconds)
        {
            string flag = (Normalize(action) == ActionRestart) ? "/r" : "/s";
            return flag + " /t " + seconds.ToString(CultureInfo.InvariantCulture)
                + " /c \"" + CommentFor(action) + "\"";
        }

        /// <summary>Windows 那个"要关机了"提示里带的一句话（`shutdown /c`）。</summary>
        public static string CommentFor(string action)
        {
            return "萌新工具箱：定时" + LabelOf(action) + "。要取消请打开工具箱 →「定时关机 / 重启 / 睡眠」→「取消定时」。";
        }

        /// <summary>定一条定时任务。成功返回 0；`report` 是给人看的结果（界面里那块结果区 /
        /// 命令行都显示它）。已经有一条在计时的话**先把旧的撤掉**（不可能同时有两条）。
        ///
        /// 顺序很重要：**先写记录、再动手**（关机的倒计时 / 睡眠的后台进程）。写不下去就一个字都不做 ——
        /// 那样不会出现"已经定了但记录里看不见"的定时（用户根本没法取消它）。</summary>
        public static int Start(string action, DateTime at, out string report)
        {
            return StartCore(action, at, false, out report);
        }

        /// <summary>把现在这条定时**改成新的**（动作 / 时间都能换）。和 `Start` 走的是同一条路
        /// （先撤旧的 → 写新记录 → 再动手），区别只在说法：「改好了」而不是「定好了」。
        /// 2026-10-24 的 P-7：界面上一颗「改一下」按钮，不用先取消再定一遍。</summary>
        public static int Modify(string action, DateTime at, out string report)
        {
            return StartCore(action, at, true, out report);
        }

        private static int StartCore(string action, DateTime at, bool modify, out string report)
        {
            string a = Normalize(action);
            if (a.Length == 0)
            {
                report = "不认识的动作：" + action;
                return 2;
            }
            DateTime now = DateTime.Now;
            long atEpoch = Epoch(at);
            int seconds = (int)(atEpoch - Epoch(now));
            if (seconds < MinSeconds)
            {
                report = "离现在太近了（不到 " + MinSeconds.ToString(CultureInfo.InvariantCulture)
                    + " 秒）：请选一个更远的时间。";
                return 2;
            }

            StringBuilder sb = new StringBuilder();
            TimerRecord old = Read();
            if (old != null)
            {
                string oldReport;
                Cancel(out oldReport);
                sb.AppendLine((modify ? "把原来那条撤掉了（" : "先把上一条定时撤掉了（") + old.Label + " · "
                    + old.AtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "）。")
                    .AppendLine();
            }

            TimerRecord rec = new TimerRecord();
            rec.Action = a;
            rec.At = atEpoch;
            rec.Started = Epoch(now);
            rec.Token = Guid.NewGuid().ToString("N");
            rec.Mode = (a == ActionSleep) ? "worker" : "system";
            rec.Pid = 0;

            string error;
            if (!Write(rec, out error))
            {
                report = error;
                return 1;
            }

            if (a == ActionSleep)
            {
                // 只写一次记录（pid 留 0），**由后台进程自己把 pid 补进去** —— 父进程在这里再写一次
                // 会和后台进程的"每 250 毫秒读一次"撞上（实测过一次：它正好读到写了一半的文件，
                // 就以为被取消了、安静退出了）。谁拥有那个进程谁写，就不会有这个竞态。
                try
                {
                    StartWorker(rec);
                }
                catch (Exception ex)
                {
                    Delete();
                    report = "后台进程没起来（这台机器可能不允许子进程）：" + ex.Message;
                    Logger.Write("定时" + LabelOf(a), report);
                    return 1;
                }
            }
            else if (!DryRun)
            {
                int exit;
                string output;
                if (!RunTool(ShutdownExe, ShutdownArgs(a, seconds), out exit, out output) || exit != 0)
                {
                    // 系统没接受这次定时：把记录撤掉（否则界面会显示一条根本不会发生的"定时"）
                    Delete();
                    report = "系统的 " + ShutdownExe + " 没接受这次定时（退出码 "
                        + exit.ToString(CultureInfo.InvariantCulture) + "）。" + Environment.NewLine
                        + "  " + output.Trim();
                    Logger.Write("定时" + LabelOf(a), report);
                    return 1;
                }
            }

            sb.Append((modify ? "已经改好了：" : "已经定好了：")).Append(at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append("（").Append(WhenText(at, now)).Append("，还有 ")
                .Append(SpanText(seconds)).Append("）执行「").Append(LabelOf(a)).AppendLine("」。");
            if (a == ActionSleep)
            {
                sb.AppendLine("· 等着它的是工具箱自己的一个后台小进程（`Mxx1Toolbox.exe timer worker`）—— 工具箱关了、"
                    + "缩进托盘都不影响它；到点前 60 秒会弹一张提示卡，点卡片也能取消。");
            }
            else
            {
                sb.AppendLine("· 倒计时交给 Windows 自己记着（" + ShutdownArgs(a, seconds)
                    + "）—— 工具箱关了也不影响；到点前 Windows 会自己弹提示。");
            }
            sb.Append("· 想取消：点上面的「取消定时」（命令行也可以：Mxx1Toolbox.exe timer cancel）。");
            if (DryRun)
            {
                sb.AppendLine().Append("（试运行：MXX1_TIMER_DRYRUN=1 —— 没有真的调 shutdown.exe / 没有真的睡，只写了这条记录。）");
            }
            report = sb.ToString();
            Logger.Write("定时" + LabelOf(a), (modify ? "改好了：" : "定好了：") + at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + " · 还有 " + seconds.ToString(CultureInfo.InvariantCulture) + " 秒 · "
                + (DryRun ? "试运行" : ("模式=" + rec.Mode)));
            return 0;
        }

        /// <summary>起那个"等着睡眠"的小进程：还是**自己这个 exe**（winexe，不开窗口、不弹控制台，
        /// 也就不必借 PowerShell —— 火绒对"隐藏窗口的 PowerShell"很敏感）。</summary>
        private static void StartWorker(TimerRecord rec)
        {
            string args = "timer worker --at=" + rec.At.ToString(CultureInfo.InvariantCulture)
                + " --action=" + rec.Action
                + " --token=" + rec.Token;
            ProcessStartInfo psi = new ProcessStartInfo(AppPaths.ExePath, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.WorkingDirectory = AppPaths.ExeDir;
            Process p = Process.Start(psi);
            if (p == null) { throw new InvalidOperationException("Process.Start 返回了空"); }
        }

        /// <summary>撤掉现在的定时。没有任务时也算成功（只是说一句"现在没有"）。</summary>
        public static bool Cancel(out string report)
        {
            TimerRecord rec = Read();
            if (rec == null)
            {
                report = "现在没有在计时的任务（什么都没做）。";
                return true;
            }

            StringBuilder sb = new StringBuilder();
            bool ok = true;
            if (rec.Mode == "worker")
            {
                if (rec.Pid > 0)
                {
                    try
                    {
                        using (Process p = Process.GetProcessById(rec.Pid))
                        {
                            string me = Path.GetFileNameWithoutExtension(AppPaths.ExePath);
                            // PID 会被系统复用：名字对不上就**不动**（宁可留下一个马上自己退出的进程，
                            // 也不能随手杀别人）
                            if (string.Equals(p.ProcessName, me, StringComparison.OrdinalIgnoreCase))
                            {
                                p.Kill();
                                sb.AppendLine("已经结束了等着" + rec.Label + "的那个后台进程（PID "
                                    + rec.Pid.ToString(CultureInfo.InvariantCulture) + "）。");
                            }
                            else
                            {
                                sb.AppendLine("那个 PID 现在不是工具箱的进程（系统可能把号发给别人了），没有动它。");
                            }
                        }
                    }
                    catch (ArgumentException)
                    {
                        sb.AppendLine("等着" + rec.Label + "的后台进程已经自己退出了。");
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        sb.AppendLine("结束后台进程失败：" + ex.Message);
                    }
                }
                else
                {
                    sb.AppendLine("记录里还没有后台进程的进程号（它可能刚起来、还没来得及写）。"
                        + "记录清掉之后，它下次醒来（250 毫秒之内）就会自己退出。");
                }
            }
            else
            {
                if (DryRun)
                {
                    sb.AppendLine("（试运行：没有去调 shutdown /a。）");
                }
                else
                {
                    int exit;
                    string output;
                    bool ran = RunTool(ShutdownExe, "/a", out exit, out output);
                    if (ran && exit == 0)
                    {
                        sb.AppendLine("已经让 Windows 撤掉了那次关机倒计时。");
                    }
                    else
                    {
                        // 1116 = "没有正在进行的关机"（到点了 / 本来就没有 / 系统那边的倒计时已经没了）
                        sb.AppendLine("Windows 那边现在没有待执行的关机倒计时（可能到过点了，或者本来就没有）"
                            + (output.Trim().Length > 0 ? "：" + output.Trim() : "。"));
                    }
                }
            }

            Delete();
            sb.AppendLine("这条定时记录已经清掉了（" + rec.Label + " · "
                + rec.AtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "）。");
            report = sb.ToString().TrimEnd();
            Logger.Write("定时" + rec.Label, "取消：" + report.Replace(Environment.NewLine, " "));
            return ok;
        }

        /// <summary>只读：这条定时真跑时会做什么（`timer plan` 用，回归测试靠它钉住那条命令行）。</summary>
        public static string PlanText(string action, DateTime at, DateTime now)
        {
            string a = Normalize(action);
            int seconds = (int)(Epoch(at) - Epoch(now));
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("action=" + (a.Length > 0 ? a : action));
            sb.AppendLine("label=" + LabelOf(a));
            sb.AppendLine("at=" + at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("epoch=" + Epoch(at).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("seconds=" + seconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("handler=" + (a == ActionSleep ? "worker" : "shutdown.exe"));
            if (a == ActionSleep)
            {
                sb.AppendLine("cmd=SetSuspendState(false, false, false)（由工具箱自己的后台进程到点调用）");
            }
            else if (a.Length > 0)
            {
                sb.AppendLine("cmd=" + ShutdownExe + " " + ShutdownArgs(a, seconds));
            }
            else
            {
                sb.AppendLine("cmd=");
            }
            sb.AppendLine("dryrun=" + (DryRun ? "1" : "0"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ 后台进程

        /// <summary>`timer worker ...` 那一条（由 `Start` 起，用户不会自己敲）。</summary>
        public static bool IsWorker(string[] args)
        {
            return args != null && args.Length >= 2
                && string.Equals(args[0], "timer", StringComparison.OrdinalIgnoreCase)
                && string.Equals(args[1], "worker", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>等时间 → 到点执行 → 清掉记录。记录没了（被取消 / 被新的换掉）就安静退出。</summary>
        public static int RunWorker(string[] args)
        {
            long at = 0;
            string action = "", token = "";
            for (int i = 2; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null) { continue; }
                if (a.StartsWith("--at=", StringComparison.OrdinalIgnoreCase)) { at = ParseLong(a.Substring(5)); }
                else if (a.StartsWith("--action=", StringComparison.OrdinalIgnoreCase)) { action = Normalize(a.Substring(9)); }
                else if (a.StartsWith("--token=", StringComparison.OrdinalIgnoreCase)) { token = a.Substring(8); }
            }
            if (at <= 0 || action.Length == 0 || token.Length == 0)
            {
                Logger.Write("定时关机", "后台进程的参数不全（--at / --action / --token），退出");
                return 2;
            }

            string name = "定时" + LabelOf(action);
            Logger.Write(name, "后台进程起来了（到点执行；要取消就在工具箱里点「取消定时」）");
            bool warned = false;
            bool pidSaved = false;
            while (true)
            {
                TimerRecord rec = Read();
                if (rec == null || !string.Equals(rec.Token, token, StringComparison.Ordinal))
                {
                    Logger.Write(name, "记录已经被取消或者换掉了，后台进程安静退出");
                    try { Console.WriteLine("timer-worker ignored=1 reason=record-gone-or-replaced"); }
                    catch { }
                    return 0;
                }
                // 头一圈顺手把自己的进程号写进记录（父进程**不写**它，见 Start 里的注释）：
                // 有了它，「取消定时」能直接结束这个进程，不必等它下次醒来看记录。
                if (!pidSaved)
                {
                    pidSaved = true;
                    try
                    {
                        int me = Process.GetCurrentProcess().Id;
                        if (rec.Pid != me)
                        {
                            rec.Pid = me;
                            string whoops;
                            if (!Write(rec, out whoops)) { Logger.Write(name, whoops); }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Write(name, "进程号没写进记录（不影响定时本身）：" + ex.Message);
                    }
                }
                long left = at - Epoch(DateTime.Now);
                if (left <= 0) { break; }

                // 睡眠到点前 60 秒弹一张卡：那一下最容易被"看见"（关机有 Windows 自己的提示）
                if (!warned && action == ActionSleep && left <= WarnBeforeSec && !DryRun)
                {
                    warned = true;
                    try
                    {
                        Balloon.Show(name,
                            "还有 " + SpanText((int)left) + "就" + LabelOf(action) + "了。要取消：打开工具箱 →"
                            + "「定时关机 / 重启 / 睡眠」→「取消定时」。",
                            WarnCardMs, NoticeKind.Warn);
                    }
                    catch (Exception ex)
                    {
                        Logger.Write(name, "提示卡没弹出来（不影响定时本身）：" + ex.Message);
                    }
                    continue;
                }

                int wait = (int)Math.Min(250, left * 1000);
                if (wait < 20) { wait = 20; }
                Thread.Sleep(wait);
            }

            // 到点了：再核一次"还是我这条"（正好在这一瞬间被取消过的话，就什么都不做）
            TimerRecord last = Read();
            if (last == null || !string.Equals(last.Token, token, StringComparison.Ordinal))
            {
                Logger.Write(name, "到点的这一刻记录已经不在了（刚被取消），什么都不做");
                return 0;
            }

            bool ok = true;
            string detail = "";
            if (DryRun)
            {
                detail = "（试运行：MXX1_TIMER_DRYRUN=1，本来要执行「" + LabelOf(action) + "」，只写了日志）";
            }
            else if (action == ActionSleep)
            {
                try
                {
                    if (!SetSuspendState(false, false, false))
                    {
                        ok = false;
                        detail = "系统拒绝了这次睡眠（可能有程序按着不让睡、或者这台机器不支持睡眠）";
                    }
                }
                catch (Exception ex)
                {
                    ok = false;
                    detail = "调睡眠失败：" + ex.Message;
                }
            }
            else
            {
                int exit;
                string output;
                string a = (action == ActionRestart) ? "/r /t 0" : "/s /t 0";
                if (!RunTool(ShutdownExe, a, out exit, out output) || exit != 0)
                {
                    ok = false;
                    detail = "shutdown.exe 退出码 " + exit.ToString(CultureInfo.InvariantCulture)
                        + "：" + output.Trim();
                }
            }

            Logger.Write(name, (ok ? "到点了，已经执行" : "到点了，但没能执行") + detail);
            // 纯 ASCII 的一行痕迹：后台进程平时没有控制台，但回归测试是**同步**把它跑起来
            // 并收 stdout 的 —— 有了它，"试运行真的没去关机"和"到点了真的执行了"不用去翻日志。
            try
            {
                Console.WriteLine("timer-worker done=" + (ok ? "1" : "0")
                    + " dryrun=" + (DryRun ? "1" : "0")
                    + " action=" + action
                    + " at=" + at.ToString(CultureInfo.InvariantCulture));
            }
            catch { }
            DeleteIfToken(token);
            return ok ? 0 : 1;
        }

        /// <summary>到点睡眠（`SetSuspendState(FALSE, FALSE, FALSE)` —— 普通睡眠，不强制、也不关掉
        /// 唤醒事件）。powrprof.dll 从 Windows 2000 起就在，Win7 / 10 / 11 都有。</summary>
        [DllImport("powrprof.dll")]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        // ------------------------------------------------------------------ 起外部程序

        /// <summary>跑一个外部程序，收它的输出。`shutdown.exe` 的输出很短（真的出问题时才看它），
        /// 所以这里同步读完再等它退出 —— 它不会像脚本按钮那样刷上千行。</summary>
        private static bool RunTool(string exe, string args, out int exit, out string output)
        {
            exit = -1;
            output = "";
            try
            {
                if (!File.Exists(exe)) { output = "找不到 " + exe; return false; }
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) { output = "没起来"; return false; }
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    exit = p.ExitCode;
                    output = (so + Environment.NewLine + se).Trim();
                    return true;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return false;
            }
        }
    }
}
