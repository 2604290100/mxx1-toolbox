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

    /// <summary>「谁占着这个文件」——用 Windows 自带的 Restart Manager（RstrtMgr.dll）。
    ///
    /// 为什么不用 handle.exe / Unlocker：那是第三方工具（要下载、要签名、要管理员），
    /// 而 Restart Manager 是系统自带、微软文档公开的 API，不需要管理员就能查当前用户的进程。
    /// 它也是 Windows 自己回答"谁占着这个文件，我需要重启哪个程序"用的那套东西（安装程序 / 更新程序）。
    ///
    /// 两个已知边界（如实告诉用户，不假装万能）：
    /// ① 别的用户 / 更高权限下跑的进程，没提权时**查不到**；
    /// ② 来自内核态的占用（杀软实时扫描、驱动）根本不属于某个进程，查不到。
    ///
    /// 底线：**不做句柄级强杀**（关句柄那种内核动作有蓝屏风险），只提供"结束这个进程"这一条路，
    /// 而且系统关键进程禁止结束。</summary>
    internal static class FileLock
    {
        private const int ErrorSuccess = 0;
        private const int ErrorMoreData = 234;
        private const int ErrorAccessDenied = 5;
        private const int ErrorInvalidHandle = 6;
        private const int MaxFolderFiles = 200;

        /// <summary>整批失败之后逐个重试的上限（防止几万个文件时把界面卡住）。</summary>
        private const int SingleRetryLimit = 80;

        /// <summary>系统关键进程：列出来但不许结束。杀了会蓝屏或掉登录会话。</summary>
        private static readonly string[] CriticalNames = new string[]
        {
            "system", "registry", "idle", "memory compression", "smss", "csrss", "wininit",
            "winlogon", "services", "lsass", "fontdrvhost", "dwm", "svchost", "audiodg",
            "sihost", "shellexperiencehost", "startmenuexperiencehost", "securityhealthservice",
            "msmpeng", "nissrv", "windefend", "wscsvc", "trustedinstaller", "tiworker",
        };

        /// <summary>查到占着 path 的程序。error 非空表示这次查询本身失败了（和"查不到"不是一回事）。</summary>
        public static List<FileLocker> WhoLocks(string path, out string error)
        {
            List<FileLocker> list = new List<FileLocker>();
            error = "";
            string full;
            try { full = Path.GetFullPath(path); }
            catch { full = path; }

            List<string> resources = new List<string>();
            bool isDir = false;
            try { isDir = Directory.Exists(full); }
            catch { }
            if (isDir)
            {
                // 文件夹：只登记"里面第一层的文件"，**不登记文件夹本身** —— 实测（2026-10-04）把文件夹
                // 路径当资源登记进去，RmGetList 一律回 ERROR_ACCESS_DENIED（5），连"文件夹里有个文件
                // 正被打开"这种最常见的情况都查不出来。只按里面的文件查一样能定位到占用者。
                // 代价：文件夹被某个程序当成"当前工作目录"这种占用查不到 —— 这条限制如实写在结果窗口的
                // "没查到"说明里，不假装能查。
                try
                {
                    string[] files = Directory.GetFiles(full);
                    for (int i = 0; i < files.Length && i < MaxFolderFiles; i++) { resources.Add(files[i]); }
                }
                catch { }
                if (resources.Count == 0)
                {
                    error = "这是个文件夹，而且里面没有文件可以查（文件夹被某个程序当成当前目录的占用，"
                        + "Windows 这个接口查不到）";
                    return list;
                }
            }
            else
            {
                resources.Add(full);
            }

            List<FileLocker> found = Query(resources.ToArray(), out error);
            if (error.Length == 0 || resources.Count < 2) { return found; }

            // 整批失败 → 逐个文件再来一遍。实测（2026-10-04，子代理独立验过）：Restart Manager 是
            // **全有或全无**的 —— 批次里哪怕只有一个它不认的路径（含非法字符、kernel32.dll 这种
            // 已知 DLL、或者就是我们不该传的文件夹），整批直接返回 0 个结果 + 一个错误码，
            // 连那些明明锁着的好文件也一起丢。逐个重试就能把坏的那个隔离出去。
            List<FileLocker> merged = new List<FileLocker>();
            List<int> seen = new List<int>();
            string lastError = error;
            int tried = 0;
            foreach (string one in resources)
            {
                if (tried >= SingleRetryLimit) { break; }
                tried++;
                string oneError;
                List<FileLocker> one1 = Query(new string[] { one }, out oneError);
                if (oneError.Length > 0) { lastError = oneError; continue; }
                foreach (FileLocker f in one1)
                {
                    if (Contains(seen, f.Pid)) { continue; }
                    seen.Add(f.Pid);
                    merged.Add(f);
                }
            }
            if (merged.Count > 0) { error = ""; return merged; }
            error = lastError;
            return found;
        }

        /// <summary>一次查询（一个会话、一批资源）。</summary>
        private static List<FileLocker> Query(string[] names, out string error)
        {
            List<FileLocker> list = new List<FileLocker>();
            error = "";
            uint session;
            StringBuilder key = new StringBuilder(33);
            key.Append(Guid.NewGuid().ToString("N"));
            int rc = RmStartSession(out session, 0, key);
            if (rc != ErrorSuccess)
            {
                error = "起不了 Restart Manager 会话（错误码 " + rc.ToString(CultureInfo.InvariantCulture) + "）";
                return list;
            }
            try
            {
                rc = RmRegisterResources(session, (uint)names.Length, names, 0, null, 0, null);
                if (rc != ErrorSuccess)
                {
                    error = "登记资源失败（错误码 " + rc.ToString(CultureInfo.InvariantCulture) + "）";
                    return list;
                }

                uint needed = 0;
                uint count = 0;
                uint reasons = 0;
                rc = RmGetList(session, out needed, ref count, null, ref reasons);
                if (rc == ErrorSuccess && needed == 0)
                {
                    return list;   // 真的没人占着
                }
                if (rc == ErrorAccessDenied)
                {
                    error = "系统不给看（权限不够）—— 别的用户或更高权限下跑的进程查不到";
                    return list;
                }
                if (rc == ErrorInvalidHandle)
                {
                    error = "这批里有个路径系统不认（错误码 " + ErrorInvalidHandle.ToString(CultureInfo.InvariantCulture) + "）";
                    return list;
                }
                if (rc != ErrorMoreData)
                {
                    error = "查占用失败（错误码 " + rc.ToString(CultureInfo.InvariantCulture) + "）";
                    return list;
                }

                count = needed;
                RM_PROCESS_INFO[] infos = new RM_PROCESS_INFO[count];
                rc = RmGetList(session, out needed, ref count, infos, ref reasons);
                if (rc != ErrorSuccess)
                {
                    error = "取占用列表失败（错误码 " + rc.ToString(CultureInfo.InvariantCulture) + "）";
                    return list;
                }

                List<int> seen = new List<int>();
                for (uint i = 0; i < count; i++)
                {
                    RM_PROCESS_INFO pi = infos[i];
                    int pid = pi.Process.dwProcessId;
                    // 按 PID 去重：同一个 svchost 里挂着的几个服务会返回同一个 PID 好几行，
                    // 而"结束进程"是按 PID 干的，列四遍同名同号的东西只是噪音。
                    if (pid <= 0 || Contains(seen, pid)) { continue; }
                    seen.Add(pid);
                    list.Add(Describe(pid, pi));
                }
            }
            finally
            {
                RmEndSession(session);
            }
            return list;
        }

        private static bool Contains(List<int> list, int value)
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

        /// <summary>结束选中的进程，逐个报结果（成功的、没权限的、已经退出的都分开说）。</summary>
        public static string Kill(List<FileLocker> chosen)
        {
            StringBuilder sb = new StringBuilder();
            int done = 0;
            int failed = 0;
            foreach (FileLocker f in chosen)
            {
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
                    done++;
                    sb.Append("  √ ").Append(f.Exe).Append("（PID ").Append(f.Pid.ToString(CultureInfo.InvariantCulture))
                      .Append("）已结束").AppendLine();
                }
                catch (ArgumentException)
                {
                    done++;
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
            if (failed == 0) { sb.Append("  ").Append(done.ToString(CultureInfo.InvariantCulture)).Append(" 个都结束了。"); }
            else
            {
                sb.Append("  结束了 ").Append(done.ToString(CultureInfo.InvariantCulture)).Append(" 个，")
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
    }
}
