// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>一个占着文件的程序。</summary>
    internal sealed class FileLocker
    {
        /// <summary>真占着（Restart Manager 报出来的）。</summary>
        public const string SourceLock = "lock";

        /// <summary>它自己正在运行，而它的 exe 就是你右键的那个文件、或者就在你右键的那个文件夹里。
        /// 这类程序**不持有文件句柄**（镜像是内存映射），句柄类接口查不到它。</summary>
        public const string SourceRun = "run";

        /// <summary>某个窗口里开着它（记事本 / 看图 / 播放器这类"读进来就关句柄"的程序）。
        /// 它**没有锁住文件**，只是让用户觉得"我明明开着它"。</summary>
        public const string SourceOpen = "open";

        public int Pid;
        public string Exe = "";        // WINWORD.EXE（拿不到就用系统报的名字）
        public string AppName = "";    // 系统报的友好名（Windows Explorer / Microsoft Word）
        public string Kind = "";       // 主窗口 / 服务(xxx) / 资源管理器 / 后台程序
        public bool Protected;         // 禁止结束
        public string Why = "";        // 为什么禁止 / 该注意什么
        public bool Checked = true;    // 默认勾不勾

        /// <summary>这条是怎么查出来的：lock / run / open（见上面的常量）。</summary>
        public string Source = SourceLock;

        /// <summary>附带信息：run 是镜像路径，open 是那个窗口的标题。</summary>
        public string Extra = "";

        public bool IsLock { get { return Source == SourceLock; } }
    }

    /// <summary>哪个文件被占着 + 占着它的那几个程序。</summary>
    internal sealed class LockHit
    {
        public string File = "";
        public List<FileLocker> Lockers = new List<FileLocker>();
    }

    /// <summary>一次「谁占着它」的完整结果。
    ///
    /// 分成三块说清楚，因为它们**不是一回事**：
    /// ① Hits：真查到了谁占着哪个文件；
    /// ② Verdict：我自己去试着独占打开它得到的一句话结论（没查到人时全靠它说话）；
    /// ③ Error：这次查询**本身**失败了（权限不给看 / 路径系统不认），不是"没查到"。</summary>
    internal sealed class LockReport
    {
        public List<LockHit> Hits = new List<LockHit>();

        public int Scanned;            // 一共登记了多少个文件去查
        public bool FolderScanned;     // 选的路径里有文件夹
        public bool Truncated;         // 有东西没扫到（数量上限 / 深度上限 / 软链接）
        public int BadFiles;           // 系统不认、跳过的那几个路径

        public string Error = "";      // 查询本身失败
        public string Note = "";       // 补一句说明（比如"命中在文件夹里但没定位到具体文件"）

        /// <summary>「删 / 改名会不会被拒绝」—— 这是用户真正要问的事（2026-10-04 加）。
        /// 做法：拿 DELETE 权限去开一次（共享模式放开 read|write|delete）：开得成 = 系统允许删除它；
        /// 开不成 = 真被拦着（32 = 有程序开着它且没放开删除共享，5 = 权限）。
        /// 注意：**正在运行的程序**这招测不出来（镜像文件照样能拿 DELETE 权限打开），
        /// 那种由「它自己在运行」那条负责说 —— 见 FinishDeleteNote。</summary>
        public bool DeleteChecked;
        public bool DeleteOk;
        public int DeleteError;
        public string DeleteNote = "";

        public string Verdict = "";    // 自查结论（人话）
        public bool VerdictLocked;     // 自查：确实被占着，但名字报不出来
        public bool VerdictDenied;     // 自查：是权限 / 只读，不是占用
        public bool VerdictExists = true;

        /// <summary>所有**真占着**东西的程序，按 PID 去重（同一个程序占着两个文件只算一个）。</summary>
        public List<FileLocker> AllLockers()
        {
            return OfSource(FileLocker.SourceLock);
        }

        /// <summary>界面上要列出来的所有行：真占着的 + 正在运行的 + 窗口里开着它的（按 PID 去重）。
        /// 后两类不是"占用"，但都是回答"为什么它删不掉 / 谁在用它"必须说的话。</summary>
        public List<FileLocker> AllRows()
        {
            return OfSource(null);
        }

        /// <summary>只算某一类（传 null = 全部）的程序，按 **PID + 来源** 去重。
        ///
        /// 为什么要带上种类：同一个进程完全可能既是"真占着它"（Restart Manager 报的）又是
        /// "它自己在运行"（你右键的正好是一个正在跑的 exe）—— 这两句话都得说；按 PID 一去重
        /// 就会把"它自己在运行（所以删不掉）"那条**最有用的**信息吞掉（实测踩过）。</summary>
        public List<FileLocker> OfSource(string source)
        {
            List<FileLocker> list = new List<FileLocker>();
            foreach (LockHit h in Hits)
            {
                foreach (FileLocker f in h.Lockers)
                {
                    if (source != null && f.Source != source) { continue; }
                    if (HasRow(list, f.Pid, f.Source)) { continue; }
                    list.Add(f);
                }
            }
            return list;
        }

        private static bool HasRow(List<FileLocker> list, int pid, string source)
        {
            foreach (FileLocker f in list)
            {
                if (f.Pid == pid && f.Source == source) { return true; }
            }
            return false;
        }

        public int LockerCount { get { return AllLockers().Count; } }

        /// <summary>「正在运行」那一类有几个（界面 / 命令行都要念）。</summary>
        public int RunCount { get { return OfSource(FileLocker.SourceRun).Count; } }

        /// <summary>「窗口里开着它」那一类有几个。</summary>
        public int OpenCount { get { return OfSource(FileLocker.SourceOpen).Count; } }

        /// <summary>真被占着的那些文件（命中在谁身上）。</summary>
        public List<string> LockedFiles()
        {
            List<string> list = new List<string>();
            foreach (LockHit h in Hits)
            {
                if (h.File.Length == 0) { continue; }
                foreach (FileLocker f in h.Lockers)
                {
                    if (!f.IsLock) { continue; }
                    if (!ContainsText(list, h.File)) { list.Add(h.File); }
                    break;
                }
            }
            return list;
        }

        /// <summary>这个程序占着的那几个文件（用来在界面上写"占着哪些文件"）。</summary>
        public List<string> FilesOf(int pid)
        {
            List<string> list = new List<string>();
            foreach (LockHit h in Hits)
            {
                foreach (FileLocker f in h.Lockers)
                {
                    if (f.Pid == pid && !ContainsText(list, h.File)) { list.Add(h.File); }
                }
            }
            return list;
        }

        private static bool HasPid(List<FileLocker> list, int pid)
        {
            foreach (FileLocker f in list) { if (f.Pid == pid) { return true; } }
            return false;
        }

        private static bool ContainsText(List<string> list, string s)
        {
            foreach (string x in list) { if (string.Equals(x, s, StringComparison.OrdinalIgnoreCase)) { return true; } }
            return false;
        }
    }

    /// <summary>「谁占着这个文件」——用 Windows 自带的 Restart Manager（RstrtMgr.dll）。
    ///
    /// 为什么不用 handle.exe / Unlocker：那是第三方工具（要下载、要签名、要管理员），
    /// 而 Restart Manager 是系统自带、微软文档公开的 API，不需要管理员就能查当前用户的进程。
    /// 它也是 Windows 自己回答"谁占着这个文件，我需要重启哪个程序"用的那套东西（安装程序 / 更新程序）。
    ///
    /// 三层做法（2026-10-04 大改：用户报「右键一个文件夹，没扫描到占用文件」）：
    /// ① **文件夹要往下扫**：原来只登记文件夹里第一层的文件，第一层只有子文件夹时直接放弃
    ///    —— 而"右键一个文件夹"恰恰是最常见的用法（占用的多半是子文件夹里的 Office / PDF 文件）。
    ///    现在按层（BFS）往下扫：深度 ≤ MaxScanDepth、文件数 ≤ MaxScanFiles、跳过软链接/联接点。
    /// ② **一批查不出来就劈开重查**：Restart Manager 是**全有或全无**的 —— 批次里只要有一个它不认的
    ///    路径，整批返回错误 + 0 个结果，把明明锁着的好文件也一起丢了。所以失败就二分递归，直到
    ///    单个文件为止（单个还失败就记下来跳过，不拖累别的）。
    /// ③ **命中之后要说是哪个文件**：一批登记 400 个文件，RM 只告诉你"哪几个进程"，不告诉你是哪个
    ///    文件。所以先把整批问一次（10ms 级）判"有没有"，有命中再逐个文件问一遍（≈11ms/个）定位，
    ///    上限 MaxAttributeFiles 个。
    ///
    /// 两个已知边界（如实告诉用户，不假装万能）：
    /// ① 别的用户 / 更高权限下跑的进程，没提权时**查不到**；
    /// ② 来自内核态的占用（杀软实时扫描、驱动）根本不属于某个进程，查不到。
    /// 这两条都由 SelfCheck 兜底说清楚（"确实被占着，但报不出是谁" / "是权限不是占用"）。
    ///
    /// 底线：**不做句柄级强杀**（关句柄那种内核动作有蓝屏风险），只提供"结束这个进程"这一条路，
    /// 而且系统关键进程禁止结束。</summary>
    internal static class FileLock
    {
        private const int ErrorSuccess = 0;
        private const int ErrorMoreData = 234;
        private const int ErrorAccessDenied = 5;
        private const int ErrorInvalidHandle = 6;

        /// <summary>文件夹往下扫的上限：文件数（够用且不会把界面卡住）。</summary>
        public const int MaxScanFiles = 400;

        /// <summary>往下扫几层（右键的那个文件夹本身算第 1 层）。</summary>
        public const int MaxScanDepth = 4;

        /// <summary>整次扫描的时间上限（网络盘 / 十万个文件的目录不能把窗口挂死）。</summary>
        private const int MaxScanMs = 4000;

        /// <summary>命中之后逐个文件定位的上限（≈11ms/个，60 个约 0.7 秒）。</summary>
        private const int MaxAttributeFiles = 60;

        /// <summary>系统关键进程：列出来但不许结束。杀了会蓝屏或掉登录会话。</summary>
        private static readonly string[] CriticalNames = new string[]
        {
            "system", "registry", "idle", "memory compression", "smss", "csrss", "wininit",
            "winlogon", "services", "lsass", "fontdrvhost", "dwm", "svchost", "audiodg",
            "sihost", "shellexperiencehost", "startmenuexperiencehost", "securityhealthservice",
            "msmpeng", "nissrv", "windefend", "wscsvc", "trustedinstaller", "tiworker",
        };

        // ------------------------------------------------------------------ 对外

        /// <summary>查这些路径上谁占着东西（路径可以是文件，也可以是文件夹）。
        /// 只读：不结束任何进程、不改任何东西。</summary>
        public static LockReport Scan(string[] paths)
        {
            LockReport r = new LockReport();
            List<string> resources = new List<string>();
            List<string> targets = new List<string>();
            Stopwatch clock = Stopwatch.StartNew();

            if (paths != null)
            {
                foreach (string p in paths)
                {
                    if (p == null || p.Trim().Length == 0) { continue; }
                    string full;
                    try { full = Path.GetFullPath(p.Trim()); }
                    catch { full = p.Trim(); }
                    // 8.3 短名先换成规范长名再比对：脚本 / 环境变量给的路径常常是短名
                    // （CI 的临时目录就是 RUNNER~1 那种写法），而镜像路径 / 句柄路径报回来的是长名，
                    // 纯字符串比较会漏掉"它自己在运行"（2026-10-05 在 CI 上抓到）。
                    full = LongPath(full);
                    AddResource(targets, full);

                    bool isDir = false;
                    try { isDir = Directory.Exists(full); }
                    catch { }
                    if (isDir) { CollectFolder(full, resources, r, clock); }
                    else { AddResource(resources, full); }
                }
            }

            if (targets.Count > 0) { SelfCheck(targets[0], r); }

            if (resources.Count > 0)
            {
                r.Scanned = resources.Count;
                List<FileLocker> who = new List<FileLocker>();
                QueryResilient(resources.ToArray(), who, r, 0, clock);
                if (who.Count > 0) { Attribute(resources, who, r); }
            }
            else if (r.Error.Length == 0 && r.FolderScanned)
            {
                // 文件夹里一个文件都没有（或者压根没传路径）：说不出"谁占着它"，
                // 但要老实说清楚，不能让用户以为"查过了，没人占"。
                r.Note = "这个文件夹（连里面几层）一个文件都没有，没东西可查。";
            }

            // 两种便宜又准的查法（2026-10-04 加）。Restart Manager 看不见这两种程序：
            // ① 正在运行的程序 —— 它的镜像是内存映射，**不持有文件句柄**；
            // ② 窗口里开着它的程序 —— 记事本这类"读进来就关句柄"，本来就没锁。
            // 而这两种恰恰是用户最常遇到的（"我明明开着它" / "文件夹说被占着却报不出是谁"）。
            ProbeRunners(targets, r);
            ProbeWindows(targets, r);
            FinishDeleteNote(targets, r);
            return r;
        }

        /// <summary>一批里到底是谁占着（不区分文件）。整批失败就劈开重查。</summary>
        private static void QueryResilient(string[] files, List<FileLocker> into, LockReport r, int depth, Stopwatch clock)
        {
            List<FileLocker> part;
            string error;
            if (TryQuery(files, out part, out error))
            {
                MergeLockers(into, part);
                return;
            }
            if (files.Length <= 1)
            {
                // 单个文件都问不出来：这个路径系统不认（实测：非法字符、kernel32.dll 这类已知 DLL、
                // 或者干脆是个目录）。记下来跳过 —— 不能因为一个坏路径把整次查询判成失败。
                r.BadFiles++;
                if (r.Error.Length == 0) { r.Error = error; }
                return;
            }
            if (clock.ElapsedMilliseconds > MaxScanMs * 3) { r.Truncated = true; return; }
            int half = files.Length / 2;
            string[] left = new string[half];
            string[] right = new string[files.Length - half];
            Array.Copy(files, 0, left, 0, half);
            Array.Copy(files, half, right, 0, files.Length - half);
            QueryResilient(left, into, r, depth + 1, clock);
            QueryResilient(right, into, r, depth + 1, clock);
        }

        /// <summary>把"谁占着"落到"占着哪个文件"上（逐个文件再问一遍，有上限）。</summary>
        private static void Attribute(List<string> files, List<FileLocker> batch, LockReport r)
        {
            if (files.Count == 1)
            {
                LockHit one = new LockHit();
                one.File = files[0];
                one.Lockers = batch;
                r.Hits.Add(one);
                return;
            }

            int tried = 0;
            List<string> covered = new List<string>();
            foreach (string f in files)
            {
                if (tried >= MaxAttributeFiles) { break; }
                tried++;
                covered.Add(f);
                List<FileLocker> part;
                string error;
                if (!TryQuery(new string[] { f }, out part, out error)) { continue; }
                if (part.Count == 0) { continue; }
                LockHit hit = new LockHit();
                hit.File = f;
                hit.Lockers = part;
                r.Hits.Add(hit);
            }

            if (r.Hits.Count > 0)
            {
                if (tried < files.Count)
                {
                    r.Note = "文件夹里还有 " + (files.Count - tried).ToString(CultureInfo.InvariantCulture)
                        + " 个文件没逐个定位（上限 " + MaxAttributeFiles.ToString(CultureInfo.InvariantCulture)
                        + " 个），上面列的是已经定位到的。";
                }
                return;
            }

            // 整批说"有人占着"，但逐个问又都说没有：理论上不会发生，真发生了也要说清楚，
            // 不能显示一个空列表让用户以为查完了。
            LockHit unknown = new LockHit();
            unknown.File = "";
            unknown.Lockers = batch;
            r.Hits.Add(unknown);
            r.Note = "查到了占用的程序，但没定位到是文件夹里哪个文件（可以直接结束它们试试）。";
        }

        /// <summary>文件夹往下扫（BFS：浅的在前；命中"当前目录"这种占用时先看靠上的文件）。</summary>
        private static void CollectFolder(string root, List<string> resources, LockReport r, Stopwatch clock)
        {
            r.FolderScanned = true;
            Queue<string> dirs = new Queue<string>();
            Queue<int> levels = new Queue<int>();
            dirs.Enqueue(root);
            levels.Enqueue(0);

            while (dirs.Count > 0)
            {
                if (resources.Count >= MaxScanFiles) { r.Truncated = true; return; }
                if (clock.ElapsedMilliseconds > MaxScanMs) { r.Truncated = true; return; }
                string dir = dirs.Dequeue();
                int level = levels.Dequeue();

                try
                {
                    string[] files = Directory.GetFiles(dir);
                    foreach (string f in files)
                    {
                        if (resources.Count >= MaxScanFiles) { r.Truncated = true; return; }
                        AddResource(resources, f);
                    }
                }
                catch { }

                if (level >= MaxScanDepth) { continue; }
                try
                {
                    string[] subs = Directory.GetDirectories(dir);
                    foreach (string d in subs)
                    {
                        // 软链接 / 联接点：可能是别的盘、可能是回路，不跟进去（并如实说"有东西没扫"）。
                        if (IsLink(d)) { r.Truncated = true; continue; }
                        dirs.Enqueue(d);
                        levels.Enqueue(level + 1);
                    }
                }
                catch { }
            }
        }

        private static bool IsLink(string path)
        {
            try
            {
                FileAttributes a = File.GetAttributes(path);
                return (a & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch { return true; }   // 问不出来就别跟进去
        }

        private static void AddResource(List<string> list, string path)
        {
            if (path == null || path.Length == 0) { return; }
            foreach (string s in list)
            {
                if (string.Equals(s, path, StringComparison.OrdinalIgnoreCase)) { return; }
            }
            list.Add(path);
        }

        private static void MergeLockers(List<FileLocker> into, List<FileLocker> part)
        {
            foreach (FileLocker f in part)
            {
                bool seen = false;
                foreach (FileLocker x in into) { if (x.Pid == f.Pid) { seen = true; break; } }
                if (!seen) { into.Add(f); }
            }
        }

        /// <summary>「删 / 改名会不会被拒绝」—— 用户真正要问的那句。拿 DELETE 权限开一次就知道：
        /// 开得成 = 系统允许删除它（别人开着它也无所谓）；开不成 = 真被拦着。
        /// 传目录要带 FILE_FLAG_BACKUP_SEMANTICS，共享模式放开 read|write|delete（不然任何
        /// 一个开着它的程序都会被我自己的共享模式误判成"拦着"。</summary>
        private static void DeleteCheck(string path, LockReport r)
        {
            r.DeleteChecked = true;
            int code = 0;
            IntPtr h = CreateFile(path, DELETE_ACCESS, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (h == new IntPtr(-1)) { code = Marshal.GetLastWin32Error(); }
            else { CloseHandle(h); }
            r.DeleteError = code;
            r.DeleteOk = (code == 0);

            if (code == 0) { r.DeleteNote = "删 / 改名：系统允许（没人拦着）。"; }
            else if (code == 32) { r.DeleteNote = "删 / 改名：会被系统拒绝 —— 有程序开着它、没放开删除共享。"; }
            else if (code == 5) { r.DeleteNote = "删 / 改名：会被拒绝 —— 不是占用，是权限（ACL）不允许。"; }
            else { r.DeleteNote = "删 / 改名：试不成（错误码 " + code.ToString(CultureInfo.InvariantCulture) + "）。"; }
        }

        /// <summary>把"正在运行"这条接到"能不能删"上：可执行文件的镜像是内存映射，
        /// **拿 DELETE 权限照样开得成**（所以 DeleteCheck 测不出它），但真去删/改名会被系统拒绝。
        /// 有这类程序时以它为准（用户右键一个正在跑的安装包时，这才是他真正需要知道的事）。</summary>
        private static void FinishDeleteNote(List<string> targets, LockReport r)
        {
            if (!r.DeleteChecked) { return; }
            List<FileLocker> runs = r.OfSource(FileLocker.SourceRun);
            if (runs.Count == 0) { return; }

            StringBuilder sb = new StringBuilder();
            sb.Append("删 / 改名：会被系统拒绝 —— ");
            int shown = 0;
            foreach (FileLocker f in runs)
            {
                if (shown >= 3) { sb.Append(" 等"); break; }
                if (shown > 0) { sb.Append("、"); }
                sb.Append(f.Exe).Append("（PID ").Append(f.Pid.ToString(CultureInfo.InvariantCulture)).Append("）");
                shown++;
            }
            sb.Append(" 正在运行，先结束它（它自己就是那个 .exe）。");
            r.DeleteNote = sb.ToString();
        }

        /// <summary>自查：这个路径现在到底能不能独占打开。
        /// 这是"没查到人"时唯一能给出的**确定**结论 —— 而不是让用户对着"查不到"发懵。</summary>
        public static void SelfCheck(string path, LockReport r)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch { full = path; }

            bool isDir = false;
            bool isFile = false;
            try
            {
                isDir = Directory.Exists(full);
                isFile = File.Exists(full);
            }
            catch { }

            if (!isDir && !isFile)
            {
                r.VerdictExists = false;
                r.Verdict = "这个路径不存在 —— 资源管理器没把路径传过来（旧版装的菜单带的是 %1，"
                    + "文件夹里的空白处和桌面要用 %V），点一次「装上…」重写菜单就好。";
                return;
            }

            if (isDir)
            {
                int code = DirExclusiveError(full);
                DeleteCheck(full, r);
                if (code == 0) { r.Verdict = "文件夹本身没被任何程序独占着"; }
                else if (code == 32)
                {
                    // "被打开着"不等于"你删不掉"：实测用户桌面上那个文件夹被打开着，
                    // 但系统照样允许删除它（对方放开了删除共享）。所以这里再拿 DELETE 权限试一次，
                    // 能删就明说能删 —— 别让用户以为"有人占着"就一定动不了。
                    if (r.DeleteOk)
                    {
                        r.Verdict = "文件夹被某个程序打开着，但这不挡你删它 / 给它改名（系统允许删除）";
                    }
                    else
                    {
                        r.VerdictLocked = true;
                        r.Verdict = "有程序拦着它：删 / 改名会被系统拒绝（文件夹正被某个程序开着，"
                            + "而句柄类接口报不出是哪个 —— 多半是它自己里面有程序在跑，或者资源管理器开着它）";
                    }
                }
                else if (code == 5)
                {
                    r.VerdictDenied = true;
                    r.Verdict = "不是被占用，是权限不允许访问这个文件夹";
                }
                else { r.Verdict = "检查这个文件夹时系统返回错误码 " + code.ToString(CultureInfo.InvariantCulture); }
                return;
            }

            DeleteCheck(full, r);

            try
            {
                using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                r.Verdict = "工具箱能独占打开它 —— 现在确实没有程序占着它";
            }
            catch (UnauthorizedAccessException)
            {
                r.VerdictDenied = true;
                bool readOnlyAttr = false;
                try { readOnlyAttr = (File.GetAttributes(full) & FileAttributes.ReadOnly) != 0; }
                catch { }
                bool canRead = false;
                try
                {
                    using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    canRead = true;
                }
                catch { }
                if (canRead)
                {
                    r.Verdict = readOnlyAttr
                        ? "不是被占用：这个文件是「只读」的，我没法写它（所以我没被占用拦着，是被属性拦着）"
                        : "不是被占用：权限（ACL）不允许写它，但我能独占读它 —— 说明没人在用它";
                }
                else { r.Verdict = "不是被占用，是权限（ACL）不允许访问它"; }
            }
            catch (IOException ex)
            {
                int code = ex.HResult & 0xFFFF;
                if (code == 32)
                {
                    r.VerdictLocked = true;
                    r.Verdict = "确实有程序占着它（工具箱试着独占打开，被系统拒绝了），但报不出是哪个程序";
                }
                else if (code == 2) { r.VerdictExists = false; r.Verdict = "这个路径不存在"; }
                else { r.Verdict = "打开它时报错（错误码 " + code.ToString(CultureInfo.InvariantCulture) + "）"; }
            }
            catch (Exception ex)
            {
                r.Verdict = "检查时出错：" + ex.Message;
            }
        }

        /// <summary>一次 RM 会话（一批资源）。成功返回 true。</summary>
        private static bool TryQuery(string[] names, out List<FileLocker> list, out string error)
        {
            list = new List<FileLocker>();
            error = "";
            if (names.Length == 0) { return true; }

            uint session;
            StringBuilder key = new StringBuilder(33);
            key.Append(Guid.NewGuid().ToString("N"));
            int rc = RmStartSession(out session, 0, key);
            if (rc != ErrorSuccess)
            {
                error = "起不了 Restart Manager 会话（错误码 " + rc.ToString(CultureInfo.InvariantCulture) + "）";
                return false;
            }
            try
            {
                rc = RmRegisterResources(session, (uint)names.Length, names, 0, null, 0, null);
                if (rc != ErrorSuccess)
                {
                    error = "登记资源失败（错误码 " + rc.ToString(CultureInfo.InvariantCulture) + "）";
                    return false;
                }

                uint needed = 0;
                uint count = 0;
                uint reasons = 0;
                rc = RmGetList(session, out needed, ref count, null, ref reasons);
                if (rc == ErrorSuccess && needed == 0) { return true; }   // 真的没人占着
                if (rc == ErrorAccessDenied)
                {
                    error = "系统不给看（权限不够）—— 别的用户或更高权限下跑的进程查不到";
                    return false;
                }
                if (rc != ErrorMoreData)
                {
                    error = "查占用失败（错误码 " + rc.ToString(CultureInfo.InvariantCulture) + "）";
                    return false;
                }

                count = needed;
                RM_PROCESS_INFO[] infos = new RM_PROCESS_INFO[count];
                rc = RmGetList(session, out needed, ref count, infos, ref reasons);
                if (rc != ErrorSuccess)
                {
                    error = "取占用列表失败（错误码 " + rc.ToString(CultureInfo.InvariantCulture) + "）";
                    return false;
                }

                List<int> seen = new List<int>();
                for (uint i = 0; i < count; i++)
                {
                    RM_PROCESS_INFO pi = infos[i];
                    int pid = pi.Process.dwProcessId;
                    // 按 PID 去重：同一个 svchost 里挂着的几个服务会返回同一个 PID 好几行，
                    // 而"结束进程"是按 PID 干的，列四遍同名同号的东西只是噪音。
                    if (pid <= 0 || ContainsPid(seen, pid)) { continue; }
                    seen.Add(pid);
                    list.Add(Describe(pid, pi));
                }
                return true;
            }
            finally
            {
                RmEndSession(session);
            }
        }

        private static bool ContainsPid(List<int> list, int value)
        {
            foreach (int v in list) { if (v == value) { return true; } }
            return false;
        }

        private static FileLocker Describe(int pid, RM_PROCESS_INFO pi)
        {
            FileLocker f = new FileLocker();
            f.Pid = pid;
            f.AppName = (pi.strAppName == null) ? "" : pi.strAppName;
            f.Exe = ExeNameOf(pid);
            if (f.Exe.Length == 0) { f.Exe = (f.AppName.Length > 0) ? f.AppName : ("PID " + pid.ToString(CultureInfo.InvariantCulture)); }

            switch (pi.ApplicationType)
            {
                case RM_APP_TYPE.RmMainWindow: f.Kind = "主窗口"; break;
                case RM_APP_TYPE.RmOtherWindow: f.Kind = "有窗口"; break;
                case RM_APP_TYPE.RmService:
                    f.Kind = (pi.strServiceShortName != null && pi.strServiceShortName.Length > 0)
                        ? ("服务 " + pi.strServiceShortName) : "服务";
                    break;
                case RM_APP_TYPE.RmExplorer: f.Kind = "资源管理器"; break;
                case RM_APP_TYPE.RmConsole: f.Kind = "控制台"; break;
                case RM_APP_TYPE.RmCritical: f.Kind = "系统关键"; break;
                default: f.Kind = "后台程序"; break;
            }

            string bare = f.Exe.ToLowerInvariant();
            if (bare.EndsWith(".exe")) { bare = bare.Substring(0, bare.Length - 4); }

            if (pid <= 4)
            {
                f.Protected = true;
                f.Checked = false;
                f.Why = "系统进程，不能结束";
            }
            else if (pi.ApplicationType == RM_APP_TYPE.RmCritical || NameIn(CriticalNames, bare))
            {
                f.Protected = true;
                f.Checked = false;
                f.Why = "系统关键程序，结束它可能蓝屏或掉登录，不能结束";
            }
            else if (string.Equals(bare, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                f.Checked = false;   // 列出来、可以选，但默认不勾
                f.Why = "结束它 = 桌面重启一次（图标和任务栏会闪一下，不影响文件）";
            }
            else if (pi.ApplicationType == RM_APP_TYPE.RmService)
            {
                f.Why = "是个服务（结束它可能被系统自动拉起来）";
            }
            return f;
        }

        private static bool NameIn(string[] names, string bare)
        {
            foreach (string n in names)
            {
                if (string.Equals(n, bare, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        /// <summary>这个名字是不是"系统关键进程"（句柄级强制解锁那边也要用同一份名单，别两边各写一套）。</summary>
        public static bool IsProtectedName(string exe)
        {
            if (exe == null || exe.Trim().Length == 0) { return false; }
            string bare = exe.Trim().ToLowerInvariant();
            if (bare.EndsWith(".exe", StringComparison.Ordinal)) { bare = bare.Substring(0, bare.Length - 4); }
            return NameIn(CriticalNames, bare);
        }

        private static string ExeNameOf(int pid)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                string name = p.ProcessName;
                if (name.Length == 0) { return ""; }
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { name = name + ".exe"; }
                return name;
            }
            catch { return ""; }
        }

        /// <summary>根 PID（含）和它们所有后代的层数：根 = 0、子 = 1 ……（只读，不改任何东西）。
        /// names 里带回每个 PID 的进程名，省得再抓一次进程表。</summary>
        private static Dictionary<int, int> TreeDepth(List<int> roots, out Dictionary<int, string> names)
        {
            Dictionary<int, int> depth = new Dictionary<int, int>();
            names = new Dictionary<int, string>();
            try
            {
                List<ProcNode> all = Processes();
                List<List<int>> kids = new List<List<int>>();
                Dictionary<int, int> indexOf = new Dictionary<int, int>();
                for (int i = 0; i < all.Count; i++)
                {
                    names[all[i].Pid] = all[i].Name;
                    if (!indexOf.ContainsKey(all[i].Pid)) { indexOf[all[i].Pid] = i; }
                    kids.Add(new List<int>());
                }
                for (int i = 0; i < all.Count; i++)
                {
                    int parent = all[i].Parent;
                    int at;
                    if (parent > 0 && indexOf.TryGetValue(parent, out at) && at != i) { kids[at].Add(i); }
                }

                Queue<int> queue = new Queue<int>();
                foreach (int pid in roots)
                {
                    if (depth.ContainsKey(pid)) { continue; }
                    depth[pid] = 0;
                    queue.Enqueue(pid);
                }
                while (queue.Count > 0)
                {
                    int pid = queue.Dequeue();
                    int at;
                    if (!indexOf.TryGetValue(pid, out at)) { continue; }
                    foreach (int child in kids[at])
                    {
                        int cpid = all[child].Pid;
                        if (depth.ContainsKey(cpid)) { continue; }
                        depth[cpid] = depth[pid] + 1;
                        queue.Enqueue(cpid);
                    }
                }
            }
            catch { }
            return depth;
        }

        /// <summary>这些程序启动的子进程（只读预览：界面上说清"会连带结束哪些"，命令行也念一遍）。</summary>
        public static List<FileLocker> ChildrenOf(List<FileLocker> chosen)
        {
            List<FileLocker> list = new List<FileLocker>();
            if (chosen == null || chosen.Count == 0) { return list; }
            List<int> roots = new List<int>();
            foreach (FileLocker f in chosen)
            {
                if (!ContainsPid(roots, f.Pid)) { roots.Add(f.Pid); }
            }
            Dictionary<int, string> names;
            Dictionary<int, int> depth = TreeDepth(roots, out names);
            List<int> kids = new List<int>();
            foreach (int pid in depth.Keys)
            {
                if (depth[pid] <= 0 || ContainsPid(roots, pid)) { continue; }
                kids.Add(pid);
            }
            // 按层数排（浅的在前），读起来跟"谁拉起来的"顺序一致
            kids.Sort(delegate(int a, int b) { return depth[a].CompareTo(depth[b]); });
            foreach (int pid in kids)
            {
                FileLocker f = new FileLocker();
                f.Pid = pid;
                f.Exe = names.ContainsKey(pid) ? names[pid] : "";
                if (f.Exe.Length == 0) { f.Exe = "PID " + pid.ToString(CultureInfo.InvariantCulture); }
                f.Source = FileLocker.SourceRun;
                f.Kind = (depth[pid] > 1) ? "子进程（隔了一层）" : "子进程";
                f.Checked = true;
                list.Add(f);
            }
            return list;
        }

        /// <summary>结束进程整段的时间上限：过了就不再等剩下的（窗口不能被它挂住）。</summary>
        private const int KillBudgetMs = 15000;

        /// <summary>结束选中的进程，逐个报结果（成功的、没权限的、已经退出的都分开说）。
        /// 同一个 PID 只结束一次（界面上它可能占着好几个文件、出现好几行）。
        ///
        /// **连带它启动的子进程一起结束，而且先子后父**（2026-10-04 用户实测踩到的）：
        /// 安装包 / 启动器都是"父进程拉一个子进程干活"——用户跑 qingjian 安装包，右键结束进程后
        /// **文件锁松开了（父进程死了）、窗口却还留着**（窗口是那个子进程的）。只结束父进程
        /// 等于"解了锁、留了个窗"，用户会以为没生效。先杀子再杀父，还顺手断了"父进程被杀了
        /// 又被守护子进程拉起来"这种回魂。</summary>
        public static string Kill(List<FileLocker> chosen)
        {
            StringBuilder sb = new StringBuilder();
            if (chosen == null || chosen.Count == 0) { return "  没有选中任何程序。"; }

            // 目标（去重）+ 它们的后代（BFS 记层数，深的先杀）
            Dictionary<int, FileLocker> target = new Dictionary<int, FileLocker>();
            List<int> targetPids = new List<int>();
            foreach (FileLocker f in chosen)
            {
                if (target.ContainsKey(f.Pid)) { continue; }
                target[f.Pid] = f;
                targetPids.Add(f.Pid);
            }

            Dictionary<int, string> nameOf;
            Dictionary<int, int> depth = TreeDepth(targetPids, out nameOf);

            List<int> order = new List<int>(depth.Keys);
            order.Sort(delegate(int a, int b) { return depth[b].CompareTo(depth[a]); });
            Stopwatch clock = Stopwatch.StartNew();
            int killed = 0;
            int failed = 0;
            int childKilled = 0;
            int skipped = 0;
            foreach (int pid in order)
            {
                bool isTarget = target.ContainsKey(pid);
                if (isTarget && target[pid].Protected)
                {
                    failed++;
                    FileLocker pf = target[pid];
                    sb.Append("  × ").Append(pf.Exe).Append("（PID ").Append(pid.ToString(CultureInfo.InvariantCulture))
                      .Append("）").Append(pf.Why.Length > 0 ? pf.Why : "这个不能结束").AppendLine();
                    continue;
                }

                string exe = isTarget ? target[pid].Exe : (nameOf.ContainsKey(pid) ? nameOf[pid] : "");
                if (exe.Length == 0) { exe = "PID " + pid.ToString(CultureInfo.InvariantCulture); }
                string tag = isTarget ? "" : "子进程 ";

                if (!isTarget)
                {
                    // 子进程也要过一遍底线：系统关键进程、工具箱自己，一个都不许顺手带走。
                    string bare = exe.ToLowerInvariant();
                    if (bare.EndsWith(".exe")) { bare = bare.Substring(0, bare.Length - 4); }
                    if (pid <= 4 || IsSelf(pid) || NameIn(CriticalNames, bare))
                    {
                        skipped++;
                        sb.Append("  · ").Append(tag).Append(exe).Append("（PID ").Append(pid.ToString(CultureInfo.InvariantCulture))
                          .Append("）没结束（系统关键程序 / 工具箱自己）").AppendLine();
                        continue;
                    }
                }

                if (clock.ElapsedMilliseconds > KillBudgetMs)
                {
                    skipped++;
                    sb.Append("  · ").Append(tag).Append(exe).Append("（PID ").Append(pid.ToString(CultureInfo.InvariantCulture))
                      .Append("）没结束（已经花了不少时间，剩下的没再等）").AppendLine();
                    continue;
                }

                try
                {
                    Process p = Process.GetProcessById(pid);
                    p.Kill();
                    // 给它一点时间真的走掉：Kill 返回不代表进程已经退出。
                    if (!p.WaitForExit(isTarget ? 4000 : 2000))
                    {
                        failed++;
                        sb.Append("  × ").Append(tag).Append(exe).Append("（PID ").Append(pid.ToString(CultureInfo.InvariantCulture))
                          .Append("）让它结束但还没退出").AppendLine();
                        continue;
                    }
                    killed++;
                    if (!isTarget) { childKilled++; }
                    sb.Append("  √ ").Append(tag).Append(exe).Append("（PID ").Append(pid.ToString(CultureInfo.InvariantCulture))
                      .Append("）已结束").AppendLine();
                }
                catch (ArgumentException)
                {
                    killed++;
                    if (!isTarget) { childKilled++; }
                    sb.Append("  · ").Append(tag).Append(exe).Append("（PID ").Append(pid.ToString(CultureInfo.InvariantCulture))
                      .Append("）已经不在了").AppendLine();
                }
                catch (Exception ex)
                {
                    failed++;
                    sb.Append("  × ").Append(tag).Append(exe).Append("（PID ").Append(pid.ToString(CultureInfo.InvariantCulture))
                      .Append("）结束不了：").Append(ex.Message).AppendLine();
                }
            }

            sb.AppendLine();
            if (failed == 0)
            {
                sb.Append("  ").Append(killed.ToString(CultureInfo.InvariantCulture)).Append(" 个都结束了");
                if (childKilled > 0)
                {
                    sb.Append("（其中 ").Append(childKilled.ToString(CultureInfo.InvariantCulture))
                      .Append(" 个是它启动的子进程 —— 窗口没关掉就是它们在撑着）");
                }
                sb.Append("。");
            }
            else
            {
                sb.Append("  结束了 ").Append(killed.ToString(CultureInfo.InvariantCulture)).Append(" 个，")
                  .Append(failed.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个没成功（没权限结束别的用户 / 更高权限的进程，那种要用管理员身份再试）。");
            }
            if (skipped > 0)
            {
                sb.Append(" 另有 ").Append(skipped.ToString(CultureInfo.InvariantCulture)).Append(" 个没动。");
            }
            return sb.ToString();
        }

        /// <summary>查询结果的一行文字（命令行 --query-only 用，也是回归测试读的东西）。</summary>
        public static string DescribeLine(FileLocker f)
        {
            return "pid=" + f.Pid.ToString(CultureInfo.InvariantCulture)
                + "\texe=" + f.Exe
                + "\tkind=" + f.Kind
                + "\tprotected=" + (f.Protected ? "yes" : "no")
                + "\tchecked=" + (f.Checked ? "yes" : "no")
                + (f.AppName.Length > 0 ? ("\tapp=" + f.AppName) : "");
        }

        // ------------------------------------------------------------------ 另外两种（不是"占用"，但必须说）

        /// <summary>正在运行的程序：它的 exe 就是你右键的那个文件，或者就在你右键的那个文件夹里。
        ///
        /// 为什么要单独查（2026-10-04 用户报「右键文件夹说有程序占用着但找不到进程」，根因就是这个）：
        /// 正在运行的程序**不持有文件句柄**（可执行文件是内存映射，加载器读完就把句柄关了），
        /// 所以 Restart Manager 报不出来、"我自己独占打开试试"也照样能成功 —— 可它让文件删不掉、
        /// 让文件夹松不开。用户那个文件夹里正好放着一个正在跑的安装包。</summary>
        private static void ProbeRunners(List<string> targets, LockReport r)
        {
            if (targets.Count == 0) { return; }
            List<ProcNode> all = Processes();
            if (all.Count == 0) { return; }

            foreach (ProcNode n in all)
            {
                string img = ImagePathOf(n.Pid);
                if (img.Length == 0) { continue; }
                foreach (string t in targets)
                {
                    bool isDir = IsDirectory(t);
                    // 文件：exe 就是它自己；文件夹：exe 在它里面（任意一层）
                    bool hit = isDir ? IsUnder(img, t) : string.Equals(img, t, StringComparison.OrdinalIgnoreCase);
                    if (!hit) { continue; }
                    if (HasRow(r, n.Pid, FileLocker.SourceRun)) { continue; }

                    FileLocker f = new FileLocker();
                    f.Pid = n.Pid;
                    f.Source = FileLocker.SourceRun;
                    f.Extra = img;
                    f.Exe = FileNameOf(img);
                    if (f.Exe.Length == 0) { f.Exe = n.Name; }
                    f.Kind = "正在运行";
                    f.Why = isDir
                        ? "它就是装在这个文件夹里的程序，现在正跑着 —— 占着文件夹的就是它"
                        : "它自己正在运行 —— 删除 / 改名会被系统拒绝（跟文件锁无关）";
                    if (IsSelf(n.Pid))
                    {
                        f.Protected = true;
                        f.Checked = false;
                        f.Why = "是工具箱自己（就装在这个文件夹里），不用结束";
                    }
                    else { Guard(f); }
                    AddRow(r, f, isDir ? img : t);
                }
            }
        }

        /// <summary>窗口里开着它：某个可见窗口的标题里带着这个文件的名字（记事本 / 看图 / VS Code /
        /// 播放器 / Office），或者某个资源管理器窗口正开着这个文件夹。
        ///
        /// 为什么要查：记事本这类程序是"读进来就关句柄"，**根本没锁文件** —— 所以"没查到占用"其实
        /// 是对的，但用户会觉得"我明明开着它，怎么说没找到"（2026-10-04 报的就是这条）。把它列出来、
        /// 并写明"只是打开着，没锁住文件，一般不用结束"，比一句"没找到"有用得多。</summary>
        private static void ProbeWindows(List<string> targets, LockReport r)
        {
            if (targets.Count == 0) { return; }
            List<WinInfo> wins = WindowList();
            if (wins.Count == 0) { return; }

            foreach (WinInfo w in wins)
            {
                if (w.Title.Length == 0) { continue; }
                foreach (string t in targets)
                {
                    bool isDir = IsDirectory(t);
                    string name = FileNameOf(t);
                    if (name.Length == 0) { continue; }

                    bool hit;
                    if (isDir)
                    {
                        // 文件夹只认"资源管理器窗口的标题正好是这个文件夹名"，避免误报
                        hit = w.IsExplorer && string.Equals(w.Title, name, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        hit = w.Title.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    if (!hit) { continue; }
                    if (HasRow(r, w.Pid, FileLocker.SourceOpen)) { continue; }

                    FileLocker f = new FileLocker();
                    f.Pid = w.Pid;
                    f.Source = FileLocker.SourceOpen;
                    f.Extra = w.Title;
                    f.Exe = ExeNameOf(w.Pid);
                    f.Kind = "窗口里开着";
                    f.Why = "只是开着它，并没有锁住文件（这类程序读完就关句柄）—— 一般不用结束";
                    f.Checked = false;
                    AddRow(r, f, t);
                }
            }
        }

        /// <summary>把一条记录挂到"哪个文件"上（同一个文件复用同一个 LockHit）。</summary>
        private static void AddRow(LockReport r, FileLocker f, string file)
        {
            foreach (LockHit h in r.Hits)
            {
                if (string.Equals(h.File, file, StringComparison.OrdinalIgnoreCase))
                {
                    h.Lockers.Add(f);
                    return;
                }
            }
            LockHit hit = new LockHit();
            hit.File = file;
            hit.Lockers.Add(f);
            r.Hits.Add(hit);
        }

        private static bool HasRow(LockReport r, int pid, string source)
        {
            foreach (LockHit h in r.Hits)
            {
                foreach (FileLocker f in h.Lockers)
                {
                    if (f.Pid == pid && f.Source == source) { return true; }
                }
            }
            return false;
        }

        /// <summary>系统关键进程 / explorer / 装箱自己：列出来但不许随便结束（和 RM 那批一个规矩）。</summary>
        private static void Guard(FileLocker f)
        {
            string bare = f.Exe.ToLowerInvariant();
            if (bare.EndsWith(".exe")) { bare = bare.Substring(0, bare.Length - 4); }

            if (f.Pid <= 4) { f.Protected = true; f.Checked = false; f.Why = "系统进程，不能结束"; }
            else if (NameIn(CriticalNames, bare)) { f.Protected = true; f.Checked = false; f.Why = "系统关键程序，不能结束"; }
            else if (string.Equals(bare, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                f.Checked = false;
                f.Why = "结束它 = 桌面重启一次（图标和任务栏会闪一下，不影响文件）";
            }
        }

        private static bool IsSelf(int pid)
        {
            try { return pid == Process.GetCurrentProcess().Id; }
            catch { return false; }
        }

        private static bool IsDirectory(string path)
        {
            try { return Directory.Exists(path); }
            catch { return false; }
        }

        /// <summary>img 在 dir 里面（任意一层），而不是"文件名前缀撞上"。</summary>
        private static bool IsUnder(string img, string dir)
        {
            string d = dir;
            if (!d.EndsWith("\\", StringComparison.Ordinal)) { d = d + "\\"; }
            if (img.Length <= d.Length) { return false; }
            return img.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }

        private static string FileNameOf(string path)
        {
            if (path == null || path.Length == 0) { return ""; }
            try
            {
                string n = Path.GetFileName(path);
                return (n == null) ? "" : n;
            }
            catch { return ""; }
        }

        /// <summary>进程表（Toolhelp）：PID + 父 PID + 进程名。结束进程要连子进程一起，靠的就是父子关系。</summary>
        private static List<ProcNode> Processes()
        {
            List<ProcNode> list = new List<ProcNode>();
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == new IntPtr(-1)) { return list; }
            try
            {
                PROCESSENTRY32 pe = new PROCESSENTRY32();
                pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                if (!Process32First(snap, ref pe)) { return list; }
                while (true)
                {
                    ProcNode n = new ProcNode();
                    n.Pid = (int)pe.th32ProcessID;
                    n.Parent = (int)pe.th32ParentProcessID;
                    n.Name = (pe.szExeFile == null) ? "" : pe.szExeFile;
                    if (n.Pid > 0) { list.Add(n); }
                    pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                    if (!Process32Next(snap, ref pe)) { break; }
                }
            }
            catch { }
            finally { CloseHandle(snap); }
            return list;
        }

        /// <summary>某个进程的镜像全路径（QueryFullProcessImageName + 最低查询权限：
        /// 不用 PROCESS_VM_READ，别的用户 / 更高权限的进程拿不到就返回空，不抛）。
        /// 拿到之后统一成规范长名（见 LongPath）：它报回来的是长名，而用户给的路径可能是 8.3 短名，
        /// 两边不统一就会漏掉"它自己在运行"。</summary>
        private static string ImagePathOf(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) { return ""; }
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                uint len = (uint)sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref len)) { return ""; }
                return LongPath(StripDevicePrefix(sb.ToString()));
            }
            catch { return ""; }
            finally { CloseHandle(h); }
        }

        /// <summary>去掉 `\\?\` / `\\?\UNC\` 设备前缀（句柄那边报回来的路径带这个前缀，
        /// 我们的目标是普通 DOS 路径，不去掉就永远比不上）。</summary>
        private static string StripDevicePrefix(string path)
        {
            if (string.IsNullOrEmpty(path)) { return path; }
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + path.Substring(8);
            }
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path.Substring(4);
            }
            return path;
        }

        /// <summary>把 8.3 短名（`…\RUNNER~1\…` 这种写法）换成规范长名；路径不存在时原样返回。
        /// 同一台机器上同一个文件夹可能有两种写法，比较前必须归一，否则"谁在运行 / 谁占着它"会漏报。</summary>
        private static string LongPath(string path)
        {
            if (string.IsNullOrEmpty(path)) { return path; }
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                uint n = GetLongPathName(path, sb, (uint)sb.Capacity);
                if (n > 0 && n < (uint)sb.Capacity)
                {
                    string got = sb.ToString();
                    if (got.Length > 0) { return got; }
                }
            }
            catch { }
            return path;
        }

        /// <summary>可见、有标题的顶层窗口。</summary>
        private static List<WinInfo> WindowList()
        {
            List<WinInfo> list = new List<WinInfo>();
            try
            {
                EnumWindowsProc cb = delegate(IntPtr hwnd, IntPtr param)
                {
                    try
                    {
                        if (!IsWindowVisible(hwnd)) { return true; }
                        int len = GetWindowTextLength(hwnd);
                        if (len <= 0) { return true; }
                        StringBuilder title = new StringBuilder(len + 2);
                        GetWindowText(hwnd, title, title.Capacity);

                        StringBuilder cls = new StringBuilder(64);
                        GetClassName(hwnd, cls, cls.Capacity);

                        uint pid = 0;
                        GetWindowThreadProcessId(hwnd, out pid);

                        WinInfo w = new WinInfo();
                        w.Pid = (int)pid;
                        w.Title = title.ToString();
                        w.Class = cls.ToString();
                        list.Add(w);
                    }
                    catch { }
                    return true;
                };
                EnumWindows(cb, IntPtr.Zero);
                GC.KeepAlive(cb);
            }
            catch { }
            return list;
        }

        private sealed class ProcNode
        {
            public int Pid;
            public int Parent;
            public string Name = "";
        }

        private sealed class WinInfo
        {
            public int Pid;
            public string Title = "";
            public string Class = "";

            /// <summary>资源管理器窗口：Win10/11 的文件窗口类名就这两个。</summary>
            public bool IsExplorer
            {
                get { return Class == "CabinetWClass" || Class == "ExploreWClass"; }
            }
        }

        // ------------------------------------------------------------------ P/Invoke

        private enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;

            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;

            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
            ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        private const uint GENERIC_READ = 0x80000000;
        private const uint DELETE_ACCESS = 0x00010000;
        private const uint FILE_SHARE_ALL = 0x00000007;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        /// <summary>文件夹能不能被独占打开（0 = 能；32 = 有别的程序开着它）。
        /// 传目录必须带 FILE_FLAG_BACKUP_SEMANTICS，否则系统一律回"拒绝访问"。</summary>
        private static int DirExclusiveError(string dir)
        {
            IntPtr h = CreateFile(dir, GENERIC_READ, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (h == new IntPtr(-1)) { return Marshal.GetLastWin32Error(); }
            CloseHandle(h);
            return 0;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // ---- 进程表（父子关系）/ 镜像路径 / 顶层窗口

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        /// <summary>8.3 短名 → 规范长名（见 LongPath）。</summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLongPathName(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
