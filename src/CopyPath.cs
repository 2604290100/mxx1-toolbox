// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「复制文件路径」—— 把选中文件 / 文件夹的完整路径复制进剪贴板（一行一个）。
    ///
    /// 谁在用：① 右键菜单里那一项（`src\RightMenu.cs` 的 CopyVerb，命令是
    /// `"<工具箱 exe>" copypath --quote "%1"`）；② 命令行 `copypath <文件…> [--quote]`。
    ///
    /// **多选那件事这里自己做了一遍**（`AppendBatch` / `SettleMs` / `JoinMs`，别删）：
    /// 右键菜单里那一项写的是 `MultiSelectModel=Player`，按微软文档 legacy verb 的 Player 模型
    /// 资源管理器**一次把选中的路径全传进来**（[How to Employ the Verb Selection Model]）——
    /// 可这条规矩我们在**真实资源管理器里一次都没实测过**（tests 是人造命令行的，DESIGN §14.10 里
    /// 挂着"要人眼确认一次"）。万一它其实是"每个文件起一个进程"，那就变成"选中 3 个、剪贴板里只剩
    /// 最后一个"—— 看着像坏了。所以这里做成一件事：
    ///   单个路径进来时先**记进一个批次文件**、等 `SettleMs`（400ms）看有没有别的进程跟着来：
    ///     · 后面还有人追加 → 让**最后一个**进程收尾（它手里是完整列表）；
    ///     · 自己就是最后一个 → 把整批路径一起复制出去。
    /// 于是"一次全传进来"和"每个文件一次"这两种情况**都是对的**（多进程并发有测试：O 组）。
    /// 代价写在 DESIGN 里：两次独立的复制挨得比 `JoinMs`（1.5 秒）还近时会被合成一批。
    ///
    /// 底线：**只读**（不移动、不改名、不删除任何文件）、不联网；路径里有空格 / 中文 / `&` 都照原样复制
    /// （剪贴板里的文本不做任何转义，`--quote` 那种加引号也是"照 Windows 自己那个「复制为路径」的写法"）。
    ///
    /// 剪贴板只有一个，谁都可能正开着它（输入法 / 剪贴板历史 / 另一个工具），所以写失败**重试 3 次**，
    /// 还失败就如实说"剪贴板忙"——不谎报"已复制"。</summary>
    internal static class CopyPath
    {
        public const string LogTag = "复制文件路径";

        /// <summary>批次文件"安静"多久之后才认"我是最后一个"（毫秒）。**别改成固定 sleep**：
        /// 2026-10-06 晚五第一次写的是"死等 400ms 再回头看"，实测三个进程并发时**被切成两批**
        /// （前一个 400ms 早过了、后两个才刚启动 —— 每个进程都要现冷启动一次 .NET + WinForms），
        /// 剪贴板里只剩后两个。现在是"轮询到批次文件不再变为止"。
        /// **实测在 §12.62 那张表里**（三个并发进程 → 一行一个全在）。</summary>
        private const int QuietMs = 220;

        /// <summary>最多等多久（毫秒）：等满了还是"后面有人"就让位，还是"我就是最后一个"就自己收尾
        /// （**不能直接放弃** —— 放弃了就没人复制了）。</summary>
        private const int MaxWaitMs = 1500;

        /// <summary>轮询间隔（毫秒）。</summary>
        private const int PollMs = 100;

        /// <summary>上次追加之后多久之内的追加算**同一批**（毫秒）。见类说明里的取舍。</summary>
        private const int JoinMs = 1500;

        /// <summary>一批最多记多少个路径（资源管理器 Player 模型的上限是 100，这里放宽一倍）。</summary>
        private const int MaxBatch = 200;

        /// <summary>跨进程串行化批次文件（同一批的几个进程会同时读写它）。</summary>
        private const string MutexName = "Mxx1Toolbox.CopyPath.Batch";

        public static string BatchFile
        {
            get { return Path.Combine(AppPaths.BaseDir, "copypath-batch.txt"); }
        }

        /// <summary>跑一次。返回退出码：0 = 成功（含"别人收尾，我什么都不用做"），2 = 用法错 / 剪贴板写不进去。</summary>
        public static int Run(List<string> rawPaths, bool quote, bool noWait, bool print, bool notify, int notifyMs)
        {
            List<string> paths = Normalize(rawPaths);
            if (paths.Count == 0)
            {
                Console.Error.WriteLine("用法: copypath <文件…> [--quote] [--print] [--no-wait]");
                Console.Error.WriteLine("      路径里可以有空格和中文；--quote 每行加上英文引号（方便粘进命令行）");
                return 2;
            }

            Console.WriteLine("input=" + paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine("quoted=" + (quote ? "yes" : "no"));

            // 多个路径 = 资源管理器一次全传进来了（或者用户自己一次给了好几个），不用猜也不用等
            bool batch = (!noWait && paths.Count == 1);
            List<string> finalList;
            string role;
            int waited = 0;
            if (batch)
            {
                Append(paths[0]);
                // 轮询到"批次文件不再变、而且最后一条就是我"为止 —— 见 QuietMs 的说明
                DateTime t0 = DateTime.UtcNow;
                finalList = null;
                while (true)
                {
                    Thread.Sleep(PollMs);
                    waited = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                    bool last = false;
                    List<string> now;
                    using (BatchLock gate = new BatchLock())
                    {
                        now = ReadBatchLocked(JoinMs);
                        last = (now.Count > 0) && Same(now[now.Count - 1], paths[0]);
                        if (last && (QuietAgeMs() >= QuietMs)) { finalList = now; }
                    }
                    if (finalList != null) { break; }
                    if (!last && (QuietAgeMs() >= QuietMs))
                    {
                        // 后面那条已经安静下来了，而它一定看得见"自己是最后一条"→ 由它收尾，我退场。
                        // （批次文件**不删**：晚到的兄弟还能并进同一批 —— 见类说明里那条取舍）
                        break;
                    }
                    if (waited >= MaxWaitMs)
                    {
                        // 等满了：还是"我就是最后一条"就自己收尾（不能谁也不复制），否则让位给后面那个
                        if (last) { finalList = now; }
                        break;
                    }
                }
                if (finalList == null)
                {
                    // 后面还有进程跟着进这一批 —— 让最后一个收尾（它手里是完整列表），这里静静地退场
                    Console.WriteLine("mode=batch");
                    Console.WriteLine("role=waiter");
                    Console.WriteLine("count=0");
                    Console.WriteLine("clipboard=skipped");
                    Console.WriteLine("waited=" + waited.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return 0;
                }
                role = "closer";
            }
            else
            {
                finalList = Dedupe(paths);
                role = "closer";
            }

            string text = Format(finalList, quote);
            string error = "";
            if (print)
            {
                Console.WriteLine("clipboard=skipped");
            }
            else
            {
                error = WriteClipboard(text);
            }

            Console.WriteLine("mode=" + (batch ? "batch" : "direct"));
            Console.WriteLine("role=" + role);
            Console.WriteLine("count=" + finalList.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine("batchfile=" + BatchFile);
            if (!print) { Console.WriteLine("clipboard=" + ((error.Length == 0) ? "ok" : "busy")); }
            int missing = 0;
            foreach (string p in finalList)
            {
                bool exists = false;
                try { exists = File.Exists(p) || Directory.Exists(p); }
                catch { }
                if (!exists) { missing++; }
                Console.WriteLine("path=" + p);
            }
            Console.WriteLine("missing=" + missing.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (print)
            {
                foreach (string line in SplitText(text)) { Console.WriteLine("copied=" + line); }
                return 0;
            }
            if (error.Length > 0)
            {
                Logger.Write(LogTag, "剪贴板写不进去：" + error);
                if (notify) { Balloon.Show("复制文件路径 · 失败", "剪贴板被别的程序占着，复制没成功（过一会儿再试一次）。", notifyMs, NoticeKind.Fail); }
                Console.Error.WriteLine("剪贴板写不进去：" + error);
                return 2;
            }

            Logger.Write(LogTag, "复制了 " + finalList.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " 个路径（" + (quote ? "带引号" : "不带引号") + "）：" + string.Join(" | ", finalList.ToArray()));
            if (notify)
            {
                Balloon.Show("复制文件路径", Summary(finalList, quote), notifyMs, NoticeKind.Ok);
            }
            return 0;
        }

        /// <summary>去掉没给全的开关，并把"路径里混着引号"这件事挡掉：Windows 文件名里**不可能**有
        /// 双引号，所以参数里出现的每一个 `"` 都是资源管理器替换 `%1` 时留下的（或者用户手打多了），
        /// 按它切开是安全的。这样"一整串被包在一对引号里、里面又各自带引号"的那种写法
        /// （多选时真出现过）也能正确认成两个路径。</summary>
        private static List<string> Normalize(List<string> raw)
        {
            List<string> list = new List<string>();
            if (raw == null) { return list; }
            foreach (string r in raw)
            {
                if (r == null) { continue; }
                string s = r.Trim();
                if (s.Length == 0 || s.StartsWith("--")) { continue; }
                foreach (string piece in s.Split('"'))
                {
                    string p = piece.Trim();
                    if (p.Length == 0) { continue; }
                    if (!Contains(list, p)) { list.Add(p); }
                }
            }
            return list;
        }

        private static List<string> Dedupe(List<string> src)
        {
            List<string> list = new List<string>();
            foreach (string s in src) { if (!Contains(list, s)) { list.Add(s); } }
            return list;
        }

        private static bool Contains(List<string> list, string value)
        {
            foreach (string s in list)
            {
                if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>剪贴板里那一串：一行一个（Windows 自己的「复制为路径」也是这个格式，
        /// 粘贴到命令行 / 聊天窗口 / 表格里都是一行一条）。</summary>
        private static string Format(List<string> paths, bool quote)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string p in paths)
            {
                if (sb.Length > 0) { sb.Append("\r\n"); }
                sb.Append(quote ? ("\"" + p + "\"") : p);
            }
            return sb.ToString();
        }

        private static string[] SplitText(string text)
        {
            return text.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>卡片上那两行话（名字取路径最后一段，整条路径太长了）。</summary>
        private static string Summary(List<string> paths, bool quote)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < paths.Count && i < 4; i++)
            {
                string n = "";
                try { n = Path.GetFileName(paths[i].TrimEnd('\\', '/')); }
                catch { }
                names.Add((n.Length > 0) ? n : paths[i]);
            }
            string joined = string.Join("、", names.ToArray());
            if (paths.Count > names.Count)
            {
                joined += " 等 " + paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个";
            }
            return paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个路径已经复制好（一行一个"
                + (quote ? "，带引号" : "") + "）：" + joined;
        }

        // ------------------------------------------------------------------ 批次（多选）

        /// <summary>把这一次的路径记进批次文件。已经在里面的（同一批里资源管理器重复调用）就不再记。</summary>
        private static void Append(string path)
        {
            using (BatchLock gate = new BatchLock())
            {
                List<string> list = ReadBatchLocked(JoinMs);
                if (!Contains(list, path)) { list.Add(path); }
                if (list.Count > MaxBatch) { list.RemoveRange(0, list.Count - MaxBatch); }
                try
                {
                    AppPaths.EnsureBase();
                    File.WriteAllLines(BatchFile, list.ToArray(), new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    // 记不进批次文件就当"这一批只有我一个"：宁可只复制自己这一条，也不能复制出错
                    Logger.Write(LogTag, "批次文件写不进去（这一批只认自己）：" + ex.Message);
                }
            }
        }

        /// <summary>读这一批（**调用方必须已经拿着 BatchLock**）。`joinMs` 之前的批次（隔得太久）
        /// 当成"已经过去的那一批"，返回空 —— 不能把用户上一次复制的东西并进来。</summary>
        private static List<string> ReadBatchLocked(int joinMs)
        {
            List<string> list = new List<string>();
            try
            {
                if (!File.Exists(BatchFile)) { return list; }
                if (joinMs > 0)
                {
                    TimeSpan age = DateTime.UtcNow - File.GetLastWriteTimeUtc(BatchFile);
                    if (age.TotalMilliseconds > joinMs) { return list; }
                }
                foreach (string line in File.ReadAllLines(BatchFile, Encoding.UTF8))
                {
                    string s = line.Trim();
                    if (s.Length > 0) { list.Add(s); }
                }
            }
            catch { }
            return list;
        }

        /// <summary>批次文件"安静"了多久（毫秒）。文件不在 = 很大（当它早就安静了）。</summary>
        private static double QuietAgeMs()
        {
            try
            {
                if (!File.Exists(BatchFile)) { return double.MaxValue; }
                return (DateTime.UtcNow - File.GetLastWriteTimeUtc(BatchFile)).TotalMilliseconds;
            }
            catch { return double.MaxValue; }
        }

        /// <summary>跨进程锁（同一批的几个进程会几乎同时读写批次文件）。等不到锁也不死等：
        /// 超时就当自己一个人（宁可少并一条，也不能卡住用户的右键）。
        /// **释放要显式 `ReleaseMutex`**：直接 Dispose 一个还持有身份的 Mutex，下一个等待者会收到
        /// `AbandonedMutexException`（虽然也能跑，但那是"上一个进程崩了"的语义，别混用）。</summary>
        private sealed class BatchLock : IDisposable
        {
            private readonly Mutex _m;
            private readonly bool _held;

            public BatchLock()
            {
                _m = new Mutex(false, MutexName);
                try { _held = _m.WaitOne(3000); }
                catch (AbandonedMutexException) { _held = true; }   // 上一个进程没释放就退出了：身份归我
                catch { _held = false; }
            }

            public void Dispose()
            {
                try
                {
                    if (_held) { _m.ReleaseMutex(); }
                }
                catch { }
                try { _m.Dispose(); } catch { }
            }
        }

        /// <summary>写剪贴板。**必须 copy=true**：我们下一秒就退出，不留在剪贴板里的话粘贴时是空的
        /// （`Clipboard.SetText` 之外再显式走 `SetDataObject`，这条以前在别的工具上踩过）。
        /// 失败重试 3 次（剪贴板是全局资源，别的程序正开着它很正常）。</summary>
        private static string WriteClipboard(string text)
        {
            string last = "";
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return "";
                }
                catch (Exception ex)
                {
                    last = ex.Message;
                    Thread.Sleep(150);
                }
            }
            return (last.Length > 0) ? last : "剪贴板打不开";
        }
    }
}
