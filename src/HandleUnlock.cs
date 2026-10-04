// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Mxx1Toolbox
{
    /// <summary>一条"某个进程手里有这个文件 / 文件夹的句柄"的记录。</summary>
    internal sealed class HandleHit
    {
        public int Pid;
        public string Exe = "";
        public string Path = "";        // 这个句柄指向哪个文件 / 文件夹
        public long HandleValue;        // 句柄值（抽的时候要用；抽之前会再核对一遍路径）
        public bool Protected;          // 系统关键进程：不动
        public string Why = "";
    }

    /// <summary>句柄级「强制解锁」：像火绒那样，**不结束进程**，直接把对方手里的文件句柄关掉。
    ///
    /// 原理（用户态能做的那条路，2026-10-04 用户问"火绒是怎么做的"之后照它做的）：
    /// ① `NtQuerySystemInformation(SystemExtendedHandleInformation)` 一次拿全系统句柄表；
    /// ② 只留"文件"类型的句柄 —— 类型编号每个系统版本都不一样（Win11=40 / Win10=37 / Win7=28），
    ///    所以先自己开一个 NUL 句柄，再从表里反查出**这台机器上**"文件"是几号（不写死版本号）；
    /// ③ 先 `GetFileType` 过滤：不是磁盘文件（管道 / 设备）直接跳过 —— 管道句柄上查名字**会阻塞**
    ///    （社区文章原话；我自己也实测过 `GetFinalPathNameByHandle` 卡死），这一步把绝大多数会卡住的
    ///    对象挡在门外；
    /// ④ 名字查询再套一层"开线程 + 超时"的保险：卡住就跳过那一条，连着卡几次就收工（不把界面拖死）；
    /// ⑤ 按"对象地址"去重：同一个文件对象被 10 个进程打开，名字只查一次；
    /// ⑥ 路径匹配上了，就 `DuplicateHandle(..., DUPLICATE_CLOSE_SOURCE)` —— **这一句就是"解锁"**：
    ///    它在把句柄复制到我们这边来的同时，把**源进程里的那个句柄关掉**。
    ///
    /// 为什么它有风险（所以界面上必须过确认框、默认不勾）：
    /// · 从别人脚下抽走句柄，那个程序可能当场出错 / 丢数据 —— 比"结束进程"更难预料；
    /// · 系统进程 / 别的用户手里的句柄抽不动（`OpenProcess` 直接拒绝），只能如实说；
    /// · 内核驱动自己持有的句柄**谁都抽不掉** —— 火绒官方论坛原话「火绒剑无法摘除驱动句柄的」，
    ///   用户态更不可能。
    ///
    /// 只读和"真动手"分得很清楚：`Find` 只查（界面能先给你看），`Release` 才关句柄，
    /// 而且关之前会**再核对一遍路径**（句柄值会被系统回收再分配，不能拿旧值直接关）。</summary>
    internal static class HandleUnlock
    {
        /// <summary>整次扫描的时间上限（全系统句柄表几万到几十万条，不能把窗口拖死）。</summary>
        public const int ScanBudgetMs = 9000;

        /// <summary>单条句柄查名字的超时（卡住就跳过）。</summary>
        private const int NameTimeoutMs = 250;

        /// <summary>连着卡这么多条就收工（说明这台机器上不该查的对象很多，别再耗了）。</summary>
        private const int MaxNameTimeouts = 5;

        /// <summary>最多查多少个"对象"的名字（上限，防极端机器）。</summary>
        private const int MaxNameQueries = 20000;

        private const int SystemExtendedHandleInformation = 64;
        private const int EntrySize = 40;     // x64 的 SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
        private const int HeaderSize = 16;    // NumberOfHandles + Reserved（都是 ULONG_PTR）

        private const uint PROCESS_DUP_HANDLE = 0x0040;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint DUPLICATE_CLOSE_SOURCE = 0x00000001;
        private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
        private const uint FILE_TYPE_DISK = 0x0001;

        // ------------------------------------------------------------------ 只查

        /// <summary>谁手里有这些路径的句柄（只读，不关任何东西）。
        /// note 里带回这次扫的规模 / 有没有被上限截断，界面和命令行都会念出来。</summary>
        public static List<HandleHit> Find(string[] paths, out string note)
        {
            List<HandleHit> hits = new List<HandleHit>();
            note = "";
            List<string> targets = Normalize(paths);
            if (targets.Count == 0) { note = "没有给出路径，没得查。"; return hits; }

            Stopwatch clock = Stopwatch.StartNew();
            int fileType = FileTypeIndex();
            if (fileType < 0)
            {
                note = "拿不到「文件」类型的句柄编号（自查用的 NUL 句柄都没开成），这次扫不了。";
                return hits;
            }

            int used;
            IntPtr buf = EnumHandles(out used);
            if (buf == IntPtr.Zero)
            {
                note = "读不到系统句柄表（NtQuerySystemInformation 失败）。";
                return hits;
            }

            int me = 0;
            try { me = Process.GetCurrentProcess().Id; }
            catch { }

            Dictionary<int, IntPtr> procs = new Dictionary<int, IntPtr>();
            Dictionary<long, string> names = new Dictionary<long, string>();
            StringBuilder path = new StringBuilder(2600);
            int fileHandles = 0;
            int queried = 0;
            int timeouts = 0;
            bool truncated = false;

            try
            {
                long total = Marshal.ReadInt64(buf, 0);
                long usable = (used - HeaderSize) / EntrySize;
                if (total > usable) { total = usable; }

                for (long i = 0; i < total; i++)
                {
                    if (clock.ElapsedMilliseconds > ScanBudgetMs) { truncated = true; break; }
                    int at = HeaderSize + (int)(i * EntrySize);
                    int pid = (int)Marshal.ReadInt64(buf, at + 8);
                    long handle = Marshal.ReadInt64(buf, at + 16);
                    int typeIndex = Marshal.ReadInt16(buf, at + 30) & 0xFFFF;
                    long obj = Marshal.ReadInt64(buf, at);

                    if (typeIndex != fileType) { continue; }
                    if (pid <= 4 || pid == me || handle == 0 || obj == 0) { continue; }
                    fileHandles++;

                    string name;
                    if (!names.TryGetValue(obj, out name))
                    {
                        if (queried >= MaxNameQueries) { truncated = true; break; }
                        IntPtr ph0 = ProcessHandle(procs, pid);
                        if (ph0 == IntPtr.Zero) { names[obj] = ""; continue; }
                        string got = NameOfHandle(ph0, handle, path, ref timeouts);
                        queried++;
                        names[obj] = (got == null) ? "" : got;
                        if (timeouts >= MaxNameTimeouts)
                        {
                            // 卡得太频繁：这台机器上要跳过的对象太多，收工（如实说）
                            note = "有 " + MaxNameTimeouts.ToString(CultureInfo.InvariantCulture)
                                + " 条句柄查名字时卡住了（多半是管道），已经跳过它们；结果可能不全。";
                            truncated = true;
                            break;
                        }
                        name = names[obj];
                    }
                    if (name == null || name.Length == 0) { continue; }
                    if (!Matches(name, targets)) { continue; }
                    if (HasHit(hits, pid, handle)) { continue; }

                    HandleHit h = new HandleHit();
                    h.Pid = pid;
                    h.HandleValue = handle;
                    h.Path = name;
                    h.Exe = ExeOf(hits, procs, pid);
                    if (h.Exe.Length == 0) { h.Exe = "PID " + pid.ToString(CultureInfo.InvariantCulture); }
                    h.Protected = IsProtected(h.Exe);
                    if (h.Protected) { h.Why = "系统关键进程，不动它的句柄"; }
                    hits.Add(h);
                }
            }
            catch (Exception ex)
            {
                if (note.Length == 0) { note = "扫句柄的时候出错了：" + ex.Message; }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
                foreach (IntPtr ph in procs.Values)
                {
                    if (ph != IntPtr.Zero) { CloseHandle(ph); }
                }
            }

            string scale = "扫了 " + fileHandles.ToString(CultureInfo.InvariantCulture) + " 条文件句柄（查了 "
                + queried.ToString(CultureInfo.InvariantCulture) + " 个对象的名字，" 
                + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " 毫秒）";
            if (note.Length > 0) { note = scale + "；" + note; }
            else if (truncated) { note = scale + "；到上限了，可能没扫全。"; }
            else { note = scale + "。"; }
            return hits;
        }

        /// <summary>把选中的句柄抽掉（**真动手**）。关之前逐条再核对路径。</summary>
        public static string Release(List<HandleHit> chosen)
        {
            StringBuilder sb = new StringBuilder();
            if (chosen == null || chosen.Count == 0) { return "  没有选中任何句柄。"; }

            int closed = 0;
            int failed = 0;
            int skipped = 0;
            IntPtr me = GetCurrentProcess();
            StringBuilder path = new StringBuilder(2600);

            foreach (HandleHit h in chosen)
            {
                string who = h.Exe + "（PID " + h.Pid.ToString(CultureInfo.InvariantCulture) + "）";
                if (h.Protected)
                {
                    skipped++;
                    sb.Append("  · ").Append(who).Append(" 没动（").Append(h.Why).Append("）").AppendLine();
                    continue;
                }

                IntPtr ph = OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_LIMITED_INFORMATION, false, h.Pid);
                if (ph == IntPtr.Zero)
                {
                    failed++;
                    sb.Append("  × ").Append(who)
                      .Append(" 抽不动：系统不给开它的句柄表（系统进程 / 别的用户 —— 要用管理员身份再试）").AppendLine();
                    continue;
                }
                try
                {
                    IntPtr dup;
                    if (!DuplicateHandle(ph, (IntPtr)h.HandleValue, me, out dup, 0, false, DUPLICATE_SAME_ACCESS))
                    {
                        skipped++;
                        sb.Append("  · ").Append(who).Append(" 的那个句柄已经不在了（它自己关了）").AppendLine();
                        continue;
                    }
                    try
                    {
                        // 关之前再核对一遍：句柄值会被系统回收再分配，不能拿旧值直接关别人别的东西
                        int dummy = 0;
                        string now = NameOfHandle2(dup, path, ref dummy);
                        if (now == null || !SamePath(now, h.Path))
                        {
                            skipped++;
                            sb.Append("  · ").Append(who).Append(" 的这个句柄已经换成别的东西了，跳过（没动）").AppendLine();
                            continue;
                        }

                        IntPtr gone;
                        if (DuplicateHandle(ph, (IntPtr)h.HandleValue, me, out gone, 0, false,
                                DUPLICATE_SAME_ACCESS | DUPLICATE_CLOSE_SOURCE))
                        {
                            CloseHandle(gone);
                            closed++;
                            sb.Append("  √ ").Append(who).Append(" 手里的句柄已抽掉（进程还在跑，没有结束它）").AppendLine();
                        }
                        else
                        {
                            failed++;
                            sb.Append("  × ").Append(who).Append(" 的那个句柄关不掉（错误码 ")
                              .Append(Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture)).Append("）").AppendLine();
                        }
                    }
                    finally { CloseHandle(dup); }
                }
                catch (Exception ex)
                {
                    failed++;
                    sb.Append("  × ").Append(who).Append(" 出错了：").Append(ex.Message).AppendLine();
                }
                finally { CloseHandle(ph); }
            }

            sb.AppendLine();
            sb.Append("  抽掉了 ").Append(closed.ToString(CultureInfo.InvariantCulture)).Append(" 个句柄");
            if (skipped > 0) { sb.Append("，").Append(skipped.ToString(CultureInfo.InvariantCulture)).Append(" 个没动"); }
            if (failed > 0) { sb.Append("，").Append(failed.ToString(CultureInfo.InvariantCulture)).Append(" 个没成功"); }
            sb.Append("。");
            return sb.ToString();
        }

        /// <summary>命令行 / 测试读的一行。</summary>
        public static string DescribeLine(HandleHit h)
        {
            return "pid=" + h.Pid.ToString(CultureInfo.InvariantCulture)
                + "\texe=" + h.Exe
                + "\tprotected=" + (h.Protected ? "yes" : "no")
                + "\tpath=" + h.Path;
        }

        // ------------------------------------------------------------------ 内部

        /// <summary>目标路径规范化（去掉尾部的反斜杠，方便比对）。</summary>
        private static List<string> Normalize(string[] paths)
        {
            List<string> list = new List<string>();
            if (paths == null) { return list; }
            foreach (string p in paths)
            {
                if (p == null || p.Trim().Length == 0) { continue; }
                string full;
                try { full = System.IO.Path.GetFullPath(p.Trim()); }
                catch { full = p.Trim(); }
                full = TrimSlash(full);
                bool seen = false;
                foreach (string x in list) { if (string.Equals(x, full, StringComparison.OrdinalIgnoreCase)) { seen = true; break; } }
                if (!seen) { list.Add(full); }
            }
            return list;
        }

        private static string TrimSlash(string s)
        {
            if (s == null) { return ""; }
            string t = s;
            while (t.Length > 3 && (t.EndsWith("\\", StringComparison.Ordinal) || t.EndsWith("/", StringComparison.Ordinal)))
            {
                t = t.Substring(0, t.Length - 1);
            }
            return t;
        }

        /// <summary>句柄报出来的路径和我们的目标是不是同一个（\?\ 前缀、大小写、尾部斜杠都要抹平）。</summary>
        private static bool Matches(string handlePath, List<string> targets)
        {
            string name = Clean(handlePath);
            foreach (string t in targets)
            {
                if (SamePath(name, t)) { return true; }
            }
            return false;
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string path)
        {
            if (path == null) { return ""; }
            string s = path;
            if (s.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) { s = @"\\" + s.Substring(8); }
            else if (s.StartsWith(@"\\?\", StringComparison.Ordinal)) { s = s.Substring(4); }
            return TrimSlash(s);
        }

        /// <summary>这次的"文件句柄"在这台机器上是几号：开一个 NUL，再回表里查它自己。</summary>
        private static int FileTypeIndex()
        {
            IntPtr nul = CreateFile("NUL", 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (nul == new IntPtr(-1)) { return -1; }
            try
            {
                int me = 0;
                try { me = Process.GetCurrentProcess().Id; }
                catch { }
                int used;
                IntPtr buf = EnumHandles(out used);
                if (buf == IntPtr.Zero) { return -1; }
                try
                {
                    long total = Marshal.ReadInt64(buf, 0);
                    long usable = (used - HeaderSize) / EntrySize;
                    if (total > usable) { total = usable; }
                    for (long i = 0; i < total; i++)
                    {
                        int at = HeaderSize + (int)(i * EntrySize);
                        int pid = (int)Marshal.ReadInt64(buf, at + 8);
                        long handle = Marshal.ReadInt64(buf, at + 16);
                        if (pid == me && handle == nul.ToInt64())
                        {
                            return Marshal.ReadInt16(buf, at + 30) & 0xFFFF;
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { }
            finally { CloseHandle(nul); }
            return -1;
        }

        private static IntPtr EnumHandles(out int used)
        {
            used = 0;
            int size = 16 * 1024 * 1024;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                IntPtr buf = Marshal.AllocHGlobal(size);
                int ret;
                int st = NtQuerySystemInformation(SystemExtendedHandleInformation, buf, size, out ret);
                if (st == 0) { used = ret; return buf; }
                Marshal.FreeHGlobal(buf);
                // 0xC0000004 = STATUS_INFO_LENGTH_MISMATCH：缓冲区不够，翻倍再来
                if (st != unchecked((int)0xC0000004)) { break; }
                size *= 2;
            }
            return IntPtr.Zero;
        }

        private static IntPtr ProcessHandle(Dictionary<int, IntPtr> cache, int pid)
        {
            IntPtr ph;
            if (cache.TryGetValue(pid, out ph)) { return ph; }
            ph = OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            cache[pid] = ph;
            return ph;
        }

        /// <summary>句柄 → 路径。**套超时**：查不出来（卡住 / 不是磁盘文件）返回 null。</summary>
        private static string NameOfHandle(IntPtr proc, long handle, StringBuilder path, ref int timeouts)
        {
            IntPtr dup;
            if (!DuplicateHandle(proc, (IntPtr)handle, GetCurrentProcess(), out dup, 0, false, DUPLICATE_SAME_ACCESS))
            {
                return "";
            }
            try
            {
                // 管道 / 设备句柄上查名字会阻塞：先用 GetFileType 把非磁盘文件的挡掉
                if (GetFileType(dup) != FILE_TYPE_DISK) { return ""; }
                string got = NameGuarded(dup, path);
                if (got == null) { timeouts++; return null; }
                return got;
            }
            finally { CloseHandle(dup); }
        }

        /// <summary>已经复制到我们自己进程里的句柄 → 路径（抽句柄前的核对用）。</summary>
        private static string NameOfHandle2(IntPtr dup, StringBuilder path, ref int dummy)
        {
            if (GetFileType(dup) != FILE_TYPE_DISK) { return ""; }
            return NameGuarded(dup, path);
        }

        /// <summary>开线程 + 超时地取名字：卡住就返回 null（后台线程留着，不拖住程序退出）。</summary>
        private static string NameGuarded(IntPtr handle, StringBuilder path)
        {
            Query q = new Query();
            q.Handle = handle;
            q.Path = path;
            q.Done = new ManualResetEvent(false);

            Thread t = new Thread(delegate()
            {
                try { q.Result = QueryName(handle, path); }
                catch { q.Result = null; }
                finally { try { q.Done.Set(); } catch { } }
            });
            t.IsBackground = true;
            try { t.Start(); }
            catch { return null; }

            if (!q.Done.WaitOne(NameTimeoutMs, false)) { return null; }
            return q.Result;
        }

        private sealed class Query
        {
            public IntPtr Handle;
            public StringBuilder Path;
            public string Result;
            public ManualResetEvent Done;
        }

        private static string QueryName(IntPtr handle, StringBuilder path)
        {
            path.Length = 0;
            uint n = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
            if (n == 0) { return ""; }
            if (n > path.Capacity) { return ""; }
            return path.ToString();
        }

        private static string ExeOf(List<HandleHit> hits, Dictionary<int, IntPtr> procs, int pid)
        {
            foreach (HandleHit h in hits)
            {
                if (h.Pid == pid && h.Exe.Length > 0) { return h.Exe; }
            }
            string name = "";
            try
            {
                Process p = Process.GetProcessById(pid);
                name = p.ProcessName;
                if (name.Length > 0 && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { name = name + ".exe"; }
            }
            catch { }
            if (name.Length == 0)
            {
                IntPtr ph = ProcessHandle(procs, pid);
                if (ph != IntPtr.Zero)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder(1024);
                        uint len = (uint)sb.Capacity;
                        if (QueryFullProcessImageName(ph, 0, sb, ref len))
                        {
                            string full = sb.ToString();
                            int cut = full.LastIndexOf('\\');
                            name = (cut >= 0) ? full.Substring(cut + 1) : full;
                        }
                    }
                    catch { }
                }
            }
            return name;
        }

        private static bool HasHit(List<HandleHit> hits, int pid, long handle)
        {
            foreach (HandleHit h in hits)
            {
                if (h.Pid == pid && h.HandleValue == handle) { return true; }
            }
            return false;
        }

        private static bool IsProtected(string exe)
        {
            if (exe == null || exe.Length == 0) { return false; }
            return FileLock.IsProtectedName(exe);
        }

        // ------------------------------------------------------------------ P/Invoke

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DuplicateHandle(IntPtr srcProc, IntPtr src, IntPtr dstProc, out IntPtr dst,
            uint access, bool inherit, uint options);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetFileType(IntPtr h);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disp,
            uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(IntPtr h, StringBuilder path, uint len, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref uint size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
    }
}
