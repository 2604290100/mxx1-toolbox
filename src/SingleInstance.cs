// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Mxx1Toolbox
{
    /// <summary>「工具箱只开一个界面」—— 用户 2026-10-20 的原话：
    /// 「重复启动的情况下，直接激活显示前一个程序窗口」。
    ///
    /// 三条口径：
    /// ① 判断"是不是已经有一个界面在跑"用**命名互斥体**（名字按 exe 的完整路径算，
    ///    所以 bin 里那份和拷到别处的副本互不干扰；`Local\` 前缀 = 只在本登录会话里算）。
    ///    命令行（status / run / consent …）**根本不碰这里** —— 脚本可以同时跑一百个。
    /// ② 已经有一个在跑时，第二个进程不建窗口，而是把第一个**叫到前面来**：先
    ///    `AllowSetForegroundWindow` 把"可以抢前台"的许可交给它，再往它的主窗口投一条
    ///    `RegisterWindowMessage` 注册消息。对方在 `MainForm.WndProc` 里收到就把窗口显示出来
    ///    （缩到托盘时也管用 —— 隐藏的窗口照样收得到消息）。
    /// ③ 对方还没建好窗口（刚双击、正在启动）时消息会丢 → 这里**重试约 3.5 秒**，
    ///    实在没反应就直接 `ShowWindow + SetForegroundWindow` 兜底。
    ///
    /// `MXX1_NO_SINGLE=1` = 关掉这套（界面回归整套都设着它：测试里会连着起好几个实例，
    /// 而且它们必须各自是一个真进程；收尾复原）。</summary>
    internal static class SingleInstance
    {
        /// <summary>注册消息的名字。换名字 = 旧版和新版互相叫不动（没必要换）。</summary>
        private const string MessageName = "Mxx1Toolbox.ShowMainWindow.v1";

        /// <summary>`ui &lt;动作&gt;` 第二个实例要转达给第一个的动作（0 = 只把窗口叫出来）。</summary>
        public const int ActionShow = 0;
        public const int ActionLog = 1;
        public const int ActionSettings = 2;
        public const int ActionAbout = 3;

        private const int HwndBroadcast = 0xFFFF;
        private const int SwShow = 5;

        private static Mutex _mutex;
        private static int _messageId;

        /// <summary>测试开关：`MXX1_NO_SINGLE=1` 时完全不拦（每次都当真进程起）。</summary>
        public static bool Disabled
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("MXX1_NO_SINGLE");
                return (v != null) && (v.Trim() == "1");
            }
        }

        /// <summary>那条注册消息的 id（两个进程按同一个名字注册，id 才会一样）。</summary>
        public static int MessageId
        {
            get
            {
                if (_messageId == 0)
                {
                    try { _messageId = (int)RegisterWindowMessageW(MessageName); }
                    catch { }
                }
                return _messageId;
            }
        }

        /// <summary>`ui &lt;动作&gt;` → 消息里的动作码。</summary>
        public static int ActionCode(string action)
        {
            if (string.Equals(action, "log", StringComparison.OrdinalIgnoreCase)) { return ActionLog; }
            if (string.Equals(action, "settings", StringComparison.OrdinalIgnoreCase)) { return ActionSettings; }
            if (string.Equals(action, "about", StringComparison.OrdinalIgnoreCase)) { return ActionAbout; }
            return ActionShow;
        }

        /// <summary>第一个实例返回 true（继续启动）；已经有一个在跑就把它叫到前面、返回 false
        /// （调用方直接退出，**不要**再建窗口）。</summary>
        public static bool TryAcquire(int action)
        {
            if (Disabled) { return true; }
            try
            {
                bool created;
                _mutex = new Mutex(true, MutexName(), out created);
                if (created) { return true; }   // 我们就是唯一那个：拿到名字、一直持有到进程退出
                _mutex.Dispose();
                _mutex = null;
                Activate(action);
                return false;
            }
            catch
            {
                // 互斥体建不出来（极少见）时按"没有别的实例"处理：宁可多开一个，也不能打不开
                return true;
            }
        }

        private static string MutexName()
        {
            string path = "";
            try { path = Process.GetCurrentProcess().MainModule.FileName ?? ""; }
            catch { }
            if (path.Length == 0) { path = "Mxx1Toolbox"; }
            return @"Local\Mxx1Toolbox.UI." + ShortHash(path.ToLowerInvariant());
        }

        private static string ShortHash(string text)
        {
            try
            {
                using (SHA1 sha = SHA1.Create())
                {
                    byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < 6 && i < bytes.Length; i++) { sb.Append(bytes[i].ToString("x2")); }
                    return sb.ToString();
                }
            }
            catch { return "0"; }
        }

        /// <summary>把已经在跑的那个叫到前面来（找不到 / 叫不动就什么都不做，安静退出）。</summary>
        private static void Activate(int action)
        {
            try
            {
                Process me = Process.GetCurrentProcess();
                string myPath = ExePath(me);
                List<Process> others = new List<Process>();
                foreach (Process p in Process.GetProcessesByName(me.ProcessName))
                {
                    if (p.Id == me.Id) { continue; }
                    if (myPath.Length > 0 && ExePath(p) != myPath) { continue; }
                    others.Add(p);
                }
                if (others.Count == 0) { return; }

                // 用户刚在资源管理器里双击了第二个：把"允许抢前台"的许可转交给第一个
                foreach (Process p in others)
                {
                    try { AllowSetForegroundWindow(p.Id); } catch { }
                }

                int msg = MessageId;
                for (int i = 0; i < 28; i++)
                {
                    bool alive = false;
                    bool visible = false;
                    foreach (Process p in others)
                    {
                        if (p.HasExited) { continue; }
                        alive = true;
                        IntPtr w = MainWindowOf(p.Id);
                        if (w != IntPtr.Zero)
                        {
                            PostMessageW(w, msg, new IntPtr(action), IntPtr.Zero);
                            visible = visible || IsWindowVisible(w);
                        }
                        else
                        {
                            // 窗口还没建好（或者叫不出标题）：广播也投一份，迟早有一个窗口收得到
                            PostMessageW(new IntPtr(HwndBroadcast), msg, new IntPtr(action), IntPtr.Zero);
                        }
                    }
                    if (!alive) { return; }
                    if (visible) { return; }          // 对方已经显示出来了
                    Thread.Sleep(120);
                }

                // 兜底：对方主线程卡着 / 消息没人处理 —— 我们自己把它的窗口显示出来
                foreach (Process p in others)
                {
                    if (p.HasExited) { continue; }
                    IntPtr w = MainWindowOf(p.Id);
                    if (w != IntPtr.Zero)
                    {
                        ShowWindow(w, SwShow);
                        SetForegroundWindow(w);
                    }
                }
            }
            catch { }
        }

        private static string ExePath(Process p)
        {
            try { return p.MainModule.FileName ?? ""; }
            catch { return ""; }
        }

        /// <summary>那个进程的主窗口（标题以「萌新工具箱」开头；**隐藏的也找得到** ——
        /// 缩到托盘之后窗口还在，只是不可见）。</summary>
        private static IntPtr MainWindowOf(int pid)
        {
            _searchPid = pid;
            _searchFound = IntPtr.Zero;
            try { EnumWindows(OnEnumWindow, IntPtr.Zero); }
            catch { }
            return _searchFound;
        }

        private static int _searchPid;
        private static IntPtr _searchFound;

        private static bool OnEnumWindow(IntPtr hwnd, IntPtr lParam)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if ((int)pid != _searchPid) { return true; }
                StringBuilder sb = new StringBuilder(256);
                if (GetWindowTextW(hwnd, sb, sb.Capacity) <= 0) { return true; }
                if (!sb.ToString().StartsWith(AboutForm.ProductTitle, StringComparison.Ordinal)) { return true; }
                _searchFound = hwnd;
                return false;   // 找到就停
            }
            catch { return true; }
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW", ExactSpelling = true)]
        private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW", ExactSpelling = true)]
        private static extern uint RegisterWindowMessageW(string name);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
