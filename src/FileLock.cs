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
        public int Pid;
        public string Exe = "";        // WINWORD.EXE（拿不到就用系统报的名字）
        public string AppName = "";    // 系统报的友好名（Windows Explorer / Microsoft Word）
        public string Kind = "";       // 主窗口 / 服务(xxx) / 资源管理器 / 后台程序
        public bool Protected;         // 禁止结束
        public string Why = "";        // 为什么禁止 / 该注意什么
        public bool Checked = true;    // 默认勾不勾
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

        public string Verdict = "";    // 自查结论（人话）
        public bool VerdictLocked;     // 自查：确实被占着，但名字报不出来
        public bool VerdictDenied;     // 自查：是权限 / 只读，不是占用
        public bool VerdictExists = true;

        /// <summary>所有占着东西的程序，按 PID 去重（同一个程序占着两个文件只算一个）。</summary>
        public List<FileLocker> AllLockers()
        {
            List<FileLocker> list = new List<FileLocker>();
            foreach (LockHit h in Hits)
            {
                foreach (FileLocker f in h.Lockers)
                {
                    if (HasPid(list, f.Pid)) { continue; }
                    list.Add(f);
                }
            }
            return list;
        }

        public int LockerCount { get { return AllLockers().Count; } }

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
            Stopwatch clock = Stopwatch.StartNew();
            string first = "";

            if (paths != null)
            {
                foreach (string p in paths)
                {
                    if (p == null || p.Trim().Length == 0) { continue; }
                    if (first.Length == 0) { first = p.Trim(); }
                    string full;
                    try { full = Path.GetFullPath(p.Trim()); }
                    catch { full = p.Trim(); }

                    bool isDir = false;
                    try { isDir = Directory.Exists(full); }
                    catch { }
                    if (isDir) { CollectFolder(full, resources, r, clock); }
                    else { AddResource(resources, full); }
                }
            }

            if (first.Length > 0) { SelfCheck(first, r); }
            if (resources.Count == 0)
            {
                // 文件夹里一个文件都没有（或者压根没传路径）：说不出"谁占着它"，
                // 但要老实说清楚，不能让用户以为"查过了，没人占"。
                if (r.Error.Length == 0 && r.FolderScanned)
                {
                    r.Note = "这个文件夹（连里面几层）一个文件都没有，没东西可查。";
                }
                return r;
            }

            r.Scanned = resources.Count;
            List<FileLocker> who = new List<FileLocker>();
            QueryResilient(resources.ToArray(), who, r, 0, clock);
            if (who.Count == 0) { return r; }   // 真的没人占着 —— 交给 Verdict 说话

            Attribute(resources, who, r);
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
                if (code == 0) { r.Verdict = "文件夹本身没被任何程序独占着"; }
                else if (code == 32)
                {
                    r.VerdictLocked = true;
                    r.Verdict = "文件夹本身正被某个程序打开着（多半是某个程序把它当成了「当前目录」）";
                }
                else if (code == 5)
                {
                    r.VerdictDenied = true;
                    r.Verdict = "不是被占用，是权限不允许访问这个文件夹";
                }
                else { r.Verdict = "检查这个文件夹时系统返回错误码 " + code.ToString(CultureInfo.InvariantCulture); }
                return;
            }

            try
            {
                using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                r.Verdict = "我自己能独占打开它 —— 现在真的没有程序占着它";
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
                    r.Verdict = "确实有程序占着它（我试着自己独占打开，被系统拒绝了），但报不出是哪个程序";
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

        /// <summary>结束选中的进程，逐个报结果（成功的、没权限的、已经退出的都分开说）。
        /// 同一个 PID 只结束一次（界面上它可能占着好几个文件、出现好几行）。</summary>
        public static string Kill(List<FileLocker> chosen)
        {
            StringBuilder sb = new StringBuilder();
            List<int> done = new List<int>();
            int killed = 0;
            int failed = 0;
            foreach (FileLocker f in chosen)
            {
                if (ContainsPid(done, f.Pid)) { continue; }
                done.Add(f.Pid);
                if (f.Protected)
                {
                    failed++;
                    sb.Append("  × ").Append(f.Exe).Append("（PID ").Append(f.Pid.ToString(CultureInfo.InvariantCulture))
                      .Append("）").Append(f.Why.Length > 0 ? f.Why : "这个不能结束").AppendLine();
                    continue;
                }
                try
                {
                    Process p = Process.GetProcessById(f.Pid);
                    p.Kill();
                    // 给它一点时间真的走掉：Kill 返回不代表进程已经退出。
                    if (!p.WaitForExit(4000))
                    {
                        failed++;
                        sb.Append("  × ").Append(f.Exe).Append("（PID ").Append(f.Pid.ToString(CultureInfo.InvariantCulture))
                          .Append("）让它结束但 4 秒还没退出").AppendLine();
                        continue;
                    }
                    killed++;
                    sb.Append("  √ ").Append(f.Exe).Append("（PID ").Append(f.Pid.ToString(CultureInfo.InvariantCulture))
                      .Append("）已结束").AppendLine();
                }
                catch (ArgumentException)
                {
                    killed++;
                    sb.Append("  · ").Append(f.Exe).Append("（PID ").Append(f.Pid.ToString(CultureInfo.InvariantCulture))
                      .Append("）已经不在了").AppendLine();
                }
                catch (Exception ex)
                {
                    failed++;
                    sb.Append("  × ").Append(f.Exe).Append("（PID ").Append(f.Pid.ToString(CultureInfo.InvariantCulture))
                      .Append("）结束不了：").Append(ex.Message).AppendLine();
                }
            }
            sb.AppendLine();
            if (failed == 0) { sb.Append("  ").Append(killed.ToString(CultureInfo.InvariantCulture)).Append(" 个都结束了。"); }
            else
            {
                sb.Append("  结束了 ").Append(killed.ToString(CultureInfo.InvariantCulture)).Append(" 个，")
                  .Append(failed.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个没成功（没权限结束别的用户 / 更高权限的进程，那种要用管理员身份再试）。");
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
    }
}
