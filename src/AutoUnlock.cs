// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>「一键解除占用」—— **不弹任何窗口**的那条路。
    ///
    /// 谁在用：右键菜单里单独那一项（`src\RightMenu.cs` 的 AutoVerb，命令是
    /// `"<工具箱 exe>" rightmenu unlock --auto "<路径>"`）。用户 2026-10-05 要的：
    /// 「保留现有的功能的前提下加个不弹窗的一键解除」—— 所以原来那个"列出来让你勾"的
    /// 窗口一个字节都没改，这是**另外一条**入口。
    ///
    /// 它做什么：查谁占着它 → 把**真占着文件的**和**它自己在运行的**那些程序结束掉（连子进程）
    /// → 右下角一个气泡说结果 → 退出。全程没有窗口、没有确认框。
    ///
    /// 三条底线（没有确认框，所以底线必须更窄，测试盯着）：
    ///   ① 能结束谁由 `FileLock.AutoUnlockTarget` 一条规则说了算：系统关键进程 / pid ≤ 4 /
    ///      工具箱自己 / explorer.exe / 只是"窗口里开着它"（根本没锁文件）—— 一律不动；
    ///   ② 没同意过《免责声明与服务条款》就**一个进程都不碰**（这条路没有窗口可以弹确认框，
    ///      所以是"不同意就不动手"，只写日志）；
    ///   ③ 干了什么全写日志（`Logger`），气泡看不到（系统通知被关了）也查得到。
    ///
    /// 命令行故意只输出 key=value（回归测试读它），不打印中文报告 —— 报告进日志。</summary>
    internal static class AutoUnlock
    {
        private const string LogTag = "一键解除占用";

        /// <summary>跑一次。返回进程退出码：0 = 正常（不管解没解开），1 = 这次查询本身出错。</summary>
        public static int Run(string[] paths, bool notify, int notifyMs)
        {
            List<string> targets = new List<string>();
            if (paths != null)
            {
                foreach (string p in paths)
                {
                    if (p == null || p.Trim().Length == 0) { continue; }
                    if (!Contains(targets, p.Trim())) { targets.Add(p.Trim()); }
                }
            }
            if (targets.Count == 0)
            {
                Console.Error.WriteLine("用法: rightmenu unlock --auto <文件或文件夹路径>");
                return 2;
            }

            Console.WriteLine("paths=" + targets.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("path=" + targets[0]);

            // ---- ① 条款门：没同意过就不动手（这条路没有窗口可以弹确认框）
            if (!Consent.IsAccepted())
            {
                string why = "还没同意《免责声明与服务条款》，这次一个进程都没碰（点一次工具箱界面里的"
                    + "「同意并继续」就好）。";
                Logger.Write(LogTag, why + " 路径=" + Join(targets));
                Console.WriteLine("auto=skipped-consent");
                Console.WriteLine("killed=0");
                Console.WriteLine("failed=0");
                Console.WriteLine("summary=" + why);
                if (notify) { Balloon.Show("一键解除占用 · 没执行", why, notifyMs); }
                return 0;
            }

            // ---- ② 查谁占着它（只读）
            LockReport report;
            try
            {
                report = FileLock.Scan(targets.ToArray());
            }
            catch (Exception ex)
            {
                Logger.Write(LogTag, "这次查询本身出错了：" + ex.Message + " 路径=" + Join(targets));
                Console.WriteLine("auto=error");
                Console.WriteLine("error=" + ex.Message);
                if (notify) { Balloon.Show("一键解除占用 · 失败", "查询出错：" + ex.Message, notifyMs); }
                return 1;
            }

            // ---- ③ 挑出"该结束的"（规则只有 FileLock.AutoUnlockTarget 一处）
            List<FileLocker> chosen = new List<FileLocker>();
            List<int> seen = new List<int>();
            int notTouched = 0;
            foreach (FileLocker f in report.AllRows())
            {
                if (!FileLock.AutoUnlockTarget(f)) { notTouched++; continue; }
                if (seen.Contains(f.Pid)) { continue; }
                seen.Add(f.Pid);
                chosen.Add(f);
            }

            if (chosen.Count == 0)
            {
                string summary = NothingToDo(report, notTouched);
                string state = report.VerdictLocked ? "locked" : "none";
                Logger.Write(LogTag, summary + " 路径=" + Join(targets)
                    + "（查到占用 " + report.LockerCount.ToString(CultureInfo.InvariantCulture)
                    + " 个 / 它自己在运行 " + report.RunCount.ToString(CultureInfo.InvariantCulture)
                    + " 个 / 窗口里开着 " + report.OpenCount.ToString(CultureInfo.InvariantCulture) + " 个，"
                    + "没有可以结束的）");
                Console.WriteLine("auto=" + state);
                Console.WriteLine("killed=0");
                Console.WriteLine("failed=0");
                Console.WriteLine("summary=" + summary);
                if (notify) { Balloon.Show("一键解除占用", summary, notifyMs); }
                return 0;
            }

            // ---- ④ 动手（结束进程，连它们启动的子进程一起 —— 只结束父进程会"锁解开了、窗口还在"）
            int killed;
            int failed;
            string killReport = FileLock.Kill(chosen, out killed, out failed);
            string who = Who(chosen);
            string text;
            if (failed == 0)
            {
                text = "已解锁：结束了 " + killed.ToString(CultureInfo.InvariantCulture) + " 个程序"
                    + (who.Length > 0 ? ("（" + who + "）") : "") + "，现在应该能删 / 改名了。";
            }
            else
            {
                text = "结束掉 " + killed.ToString(CultureInfo.InvariantCulture) + " 个，还有 "
                    + failed.ToString(CultureInfo.InvariantCulture) + " 个没成功"
                    + "（多半是权限不够：那些程序是管理员 / 别的用户开的，用管理员身份再试一次）。";
            }
            Logger.Write(LogTag, text + Environment.NewLine + "  路径=" + Join(targets)
                + Environment.NewLine + killReport);

            Console.WriteLine("auto=" + ((failed == 0) ? "killed" : "partial"));
            Console.WriteLine("killed=" + killed.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("failed=" + failed.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("summary=" + text);
            if (notify) { Balloon.Show("一键解除占用 · 已处理", text, notifyMs); }
            return 0;
        }

        /// <summary>一条都没结束时的说法。要说清"是真没人占着"还是"有东西占着但报不出名字"——
        /// 这两件事对用户的意思完全不同（前者可以直接删，后者得另想办法）。实测过：拿系统事件
        /// 日志那个永远被 svchost 占着的文件试，一开始只会说"报不出是哪个程序"—— 明明报出来了
        /// （4 个服务），是**我们不肯动**。所以顺序是：真没人占 > 知道是谁但不许动 > 报不出名字。</summary>
        private static string NothingToDo(LockReport r, int notTouched)
        {
            if (r.Error.Length > 0 && r.LockerCount == 0)
            {
                return "这次查询没成功（不是「没人占用」）：" + r.Error;
            }
            if (!r.VerdictLocked && r.LockerCount == 0 && r.RunCount == 0)
            {
                return "没有程序占着它 —— 可以直接删 / 改名。";
            }
            if (notTouched > 0)
            {
                return "占着它的都是不能自动结束的（系统关键进程 / 资源管理器 / 工具箱自己），"
                    + "这些一律没动 —— 想看清楚是谁，用带窗口那个「" + RightMenu.UnlockTitle + "」。";
            }
            if (r.VerdictLocked)
            {
                return "确实有东西占着它，但报不出是哪个程序（多半是杀毒软件的实时扫描或驱动），"
                    + "没能解锁 —— 用带窗口那个「" + RightMenu.UnlockTitle + "」看详细情况。";
            }
            return "没有可以自动结束的程序 —— 用带窗口那个「" + RightMenu.UnlockTitle + "」看详细情况。";
        }

        /// <summary>气泡里那串程序名（最多三个，太长了气泡显示不全）。</summary>
        private static string Who(List<FileLocker> chosen)
        {
            StringBuilder sb = new StringBuilder();
            int n = 0;
            foreach (FileLocker f in chosen)
            {
                if (n >= 3) { sb.Append(" 等").Append(chosen.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个"); break; }
                if (n > 0) { sb.Append("、"); }
                sb.Append(f.Exe);
                n++;
            }
            return sb.ToString();
        }

        private static string Join(List<string> list)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string s in list)
            {
                if (sb.Length > 0) { sb.Append(" | "); }
                sb.Append(s);
            }
            return sb.ToString();
        }

        private static bool Contains(List<string> list, string s)
        {
            foreach (string x in list) { if (string.Equals(x, s, StringComparison.OrdinalIgnoreCase)) { return true; } }
            return false;
        }
    }
}
