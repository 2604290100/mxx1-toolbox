// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace Mxx1Toolbox
{
    /// <summary>系统代理里的一个设置项（关掉代理要动的就这四个）。</summary>
    internal sealed class ProxyValue
    {
        public string Name = "";
        public bool Exists;
        public bool IsDword;
        public int Dword;
        public string Text = "";
    }

    /// <summary>系统代理现在的样子 —— 只读 WinINET 那一层（HKCU 的 Internet Settings）。</summary>
    internal sealed class ProxyState
    {
        public List<ProxyValue> Values = new List<ProxyValue>();
        public string Error = "";
        public bool HasBackup;
        public string WinHttp = "";      // netsh 的原样输出（读不出来就是空的）
        public string Probe = "";        // 代理指向本机时，那个端口通不通

        public ProxyValue Get(string name)
        {
            foreach (ProxyValue v in Values)
            {
                if (v.Name == name) { return v; }
            }
            return null;
        }

        public bool Enabled
        {
            get
            {
                ProxyValue v = Get("ProxyEnable");
                return (v != null) && v.Exists && v.IsDword && v.Dword != 0;
            }
        }

        public string Server
        {
            get
            {
                ProxyValue v = Get("ProxyServer");
                return (v != null && v.Exists) ? v.Text : "";
            }
        }

        public string Pac
        {
            get
            {
                ProxyValue v = Get("AutoConfigURL");
                return (v != null && v.Exists) ? v.Text : "";
            }
        }

        public bool AutoDetect
        {
            get
            {
                ProxyValue v = Get("AutoDetect");
                return (v != null) && v.Exists && v.IsDword && v.Dword != 0;
            }
        }

        /// <summary>一句话说清现在是什么样（窗口和命令行用同一句话，两边不许各说一套）。</summary>
        public string Describe()
        {
            if (!Enabled && Pac.Length == 0)
            {
                return AutoDetect
                    ? "没有代理服务器，但开着「自动检测设置」（WPAD，有些网络会自己找出一个代理）"
                    : "没有开系统代理（浏览器直连）";
            }
            StringBuilder sb = new StringBuilder();
            if (Enabled)
            {
                sb.Append("代理开着");
                sb.Append(Server.Length > 0 ? "（" + Server + "）" : "（但没填地址）");
            }
            if (Pac.Length > 0)
            {
                if (sb.Length > 0) { sb.Append("，另外还有"); }
                sb.Append("自动配置脚本 ").Append(Pac);
            }
            if (AutoDetect) { sb.Append("，还开着「自动检测设置」"); }
            return sb.ToString();
        }
    }

    /// <summary>「关掉系统代理」的后端（2026-10-23 的 Q1）。
    ///
    /// 三条口径：
    /// ① **只关浏览器用的那一层**（WinINET，HKCU 的 `Internet Settings`）——不用管理员，
    ///    也不用碰 WinHTTP（那一层只有管理员能改，而且改坏了不好还原；窗口里只**原样显示**它，
    ///    让用户知道"为什么有些程序还是走代理"）。
    /// ② 动手之前把 **ProxyEnable / ProxyServer / AutoConfigURL / AutoDetect** 四个值原样记进一份 TSV
    ///    （第一次动才记，和 RegEngine 一样：重复点不会把已经改过的值当成原值），恢复 = 按记录写回；
    ///    记不下来就**一个字都不改**。
    /// ③ 写完**读回核对**，核对不上就如实说失败；最后叫一声 `InternetSetOption`，
    ///    让已经开着的浏览器别再抱着旧设置在内存里。
    ///
    /// ⚠️ 测试隔离：`MXX1_PROXY_ROOT` 把那个键换成夹具路径（写了它就等于"允许写"，
    /// 和 startup / eject 同一套口径），`MXX1_PROXY_TSV` 换掉那份原值记录。</summary>
    internal static class ProxyConfig
    {
        public const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        private static readonly string[] Names = new string[]
        {
            "ProxyEnable", "ProxyServer", "AutoConfigURL", "AutoDetect"
        };

        public static string KeyPath()
        {
            string e = Environment.GetEnvironmentVariable("MXX1_PROXY_ROOT");
            if (!string.IsNullOrEmpty(e) && e.Trim().Length > 0) { return e.Trim(); }
            return DefaultKeyPath;
        }

        /// <summary>命令行能不能改 —— 只认测试用的隔离根，真实机器上写只能在窗口里点。</summary>
        public static bool WriteAllowed()
        {
            string e = Environment.GetEnvironmentVariable("MXX1_PROXY_ROOT");
            return !string.IsNullOrEmpty(e) && e.Trim().Length > 0;
        }

        public static string BackupPath()
        {
            string e = Environment.GetEnvironmentVariable("MXX1_PROXY_TSV");
            if (!string.IsNullOrEmpty(e) && e.Trim().Length > 0) { return e.Trim(); }
            return Path.Combine(AppPaths.BaseDir, "proxy-original.tsv");
        }

        public static bool HasBackup() { return File.Exists(BackupPath()); }

        // ------------------------------------------------------------------ 读

        public static ProxyState Read() { return Read(true); }

        public static ProxyState Read(bool withWinHttp)
        {
            ProxyState st = new ProxyState();
            RegistryKey k = null;
            try { k = Registry.CurrentUser.OpenSubKey(KeyPath(), false); }
            catch (Exception ex) { st.Error = "读不到系统代理的设置：" + ex.Message; return st; }

            if (k == null)
            {
                st.Error = "这台电脑上找不到那个注册表键，读不到系统代理的设置（" + KeyPath() + "）";
            }
            else
            {
                try
                {
                    foreach (string n in Names)
                    {
                        ProxyValue v = new ProxyValue();
                        v.Name = n;
                        try
                        {
                            object raw = k.GetValue(n, null);
                            if (raw != null)
                            {
                                RegistryValueKind kind = k.GetValueKind(n);
                                v.Exists = true;
                                if (kind == RegistryValueKind.DWord)
                                {
                                    v.IsDword = true;
                                    v.Dword = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                                }
                                else
                                {
                                    v.Text = Convert.ToString(raw, CultureInfo.InvariantCulture);
                                }
                            }
                        }
                        catch { }
                        st.Values.Add(v);
                    }
                }
                finally { k.Close(); }
            }

            st.HasBackup = HasBackup();
            if (withWinHttp) { st.WinHttp = ReadWinHttp(); }
            st.Probe = ProbeLocal(st);
            return st;
        }

        /// <summary>系统级（WinHTTP）代理：**原样**交给界面显示，不做本地化文字解析
        /// （`netsh` 的输出随系统语言变，解析它正是坑 74）。</summary>
        private static string ReadWinHttp()
        {
            try
            {
                string exe = Environment.GetEnvironmentVariable("MXX1_NETSH");
                if (string.IsNullOrEmpty(exe) || exe.Trim().Length == 0) { exe = "netsh.exe"; }
                ProcessStartInfo psi = new ProcessStartInfo(exe, "winhttp show proxy");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) { return ""; }
                    string text = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(2000))
                    {
                        try { p.Kill(); } catch { }
                        return "";
                    }
                    return text.Trim();
                }
            }
            catch { return ""; }
        }

        /// <summary>代理地址指向本机时，顺手看一眼那个端口有没有程序在听 ——
        /// 「代理软件退了、设置还留着」正是这个按钮要治的病，能直接写出来最好。</summary>
        private static string ProbeLocal(ProxyState st)
        {
            string srv = st.Server;
            if (srv.Length == 0) { return ""; }
            string host = "";
            int port = 0;
            foreach (string part in srv.Split(';'))
            {
                string v = part.Trim();
                int eq = v.IndexOf('=');
                if (eq >= 0) { v = v.Substring(eq + 1).Trim(); }
                if (v.Length == 0) { continue; }
                int colon = v.LastIndexOf(':');
                if (colon <= 0) { continue; }
                int p;
                if (!int.TryParse(v.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out p)) { continue; }
                if (p <= 0) { continue; }
                host = v.Substring(0, colon).Trim();
                port = p;
                break;
            }
            if (port <= 0) { return ""; }
            string h = host.ToLowerInvariant();
            if (h != "127.0.0.1" && h != "localhost" && h != "::1" && h != "[::1]") { return ""; }
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect(host.Trim('[', ']'), port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(250, false))
                    {
                        return "本机 " + port.ToString(CultureInfo.InvariantCulture) + " 端口没有程序在听（代理软件多半已经退了）";
                    }
                    try { c.EndConnect(ar); }
                    catch { return "本机 " + port.ToString(CultureInfo.InvariantCulture) + " 端口连不上（代理软件多半已经退了）"; }
                    return "本机 " + port.ToString(CultureInfo.InvariantCulture) + " 端口是通的（代理软件还开着）";
                }
            }
            catch { return ""; }
        }

        // ------------------------------------------------------------------ 写

        /// <summary>关掉系统代理。返回 false 时 message 一定说清"为什么没成 / 哪一步没核对上"。</summary>
        public static bool Off(out string message)
        {
            ProxyState st = Read(false);
            if (st.Error.Length > 0) { message = st.Error; return false; }

            string before = st.Describe();
            bool nothingToDo = !st.Enabled && st.Pac.Length == 0 && !st.AutoDetect;
            if (nothingToDo)
            {
                message = "本来就是关着的（" + before + "），这次一个字都没改。";
                return true;
            }

            if (!File.Exists(BackupPath()))
            {
                try { SaveSnapshot(st); }
                catch (Exception ex)
                {
                    message = "改之前的原值没记下来，所以这次一个字都没改（" + ex.Message + "）";
                    return false;
                }
            }

            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath()))
                {
                    if (k == null) { message = "打不开注册表键，改不了：" + KeyPath(); return false; }
                    k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    k.SetValue("AutoDetect", 0, RegistryValueKind.DWord);
                    ProxyValue pac = st.Get("AutoConfigURL");
                    if (pac != null && pac.Exists) { k.DeleteValue("AutoConfigURL", false); }
                }
            }
            catch (Exception ex)
            {
                message = "写注册表失败：" + ex.Message;
                return false;
            }

            ProxyState after = Read(false);
            if (after.Error.Length == 0 && (after.Enabled || after.Pac.Length > 0 || after.AutoDetect))
            {
                message = "写完了，但读回来还是「" + after.Describe() + "」，没改掉（别当成成功）。";
                return false;
            }

            NotifyBrowsers();
            message = "已经关掉了（原来是：" + before + "）。浏览器里开着的网页按一下 F5；"
                + "想恢复就点「恢复上次的代理设置」。";
            return true;
        }

        /// <summary>按上次关掉之前记下的原值写回去（没记过就说清楚，不猜）。</summary>
        public static bool Restore(out string message)
        {
            if (!File.Exists(BackupPath()))
            {
                message = "没有记下过原来的代理设置（这个工具还没关过代理，或者那份记录被删了），恢复不了。";
                return false;
            }

            List<ProxyValue> saved;
            try { saved = LoadSnapshot(); }
            catch (Exception ex) { message = "那份原值记录读不出来：" + ex.Message; return false; }
            if (saved.Count == 0) { message = "那份原值记录是空的，恢复不了。"; return false; }

            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath()))
                {
                    if (k == null) { message = "打不开注册表键，改不了：" + KeyPath(); return false; }
                    foreach (ProxyValue v in saved)
                    {
                        if (!v.Exists) { k.DeleteValue(v.Name, false); }
                        else if (v.IsDword) { k.SetValue(v.Name, v.Dword, RegistryValueKind.DWord); }
                        else { k.SetValue(v.Name, v.Text, RegistryValueKind.String); }
                    }
                }
            }
            catch (Exception ex)
            {
                message = "写回去失败：" + ex.Message;
                return false;
            }

            ProxyState now = Read(false);
            bool ok = true;
            string diff = "";
            foreach (ProxyValue v in saved)
            {
                ProxyValue cur = now.Get(v.Name);
                bool same;
                if (v.Exists) { same = (cur != null) && cur.Exists; }
                else { same = (cur == null) || !cur.Exists; }
                if (same && v.Exists)
                {
                    same = v.IsDword ? (cur.IsDword && cur.Dword == v.Dword) : (!cur.IsDword && cur.Text == v.Text);
                }
                if (!same) { ok = false; diff += (diff.Length > 0 ? " / " : "") + v.Name; }
            }

            NotifyBrowsers();
            message = ok
                ? "已经恢复成关掉之前的样子（" + now.Describe() + "）。"
                : "写回去了，但读回来对不上：" + diff + "（现在是「" + now.Describe() + "」）。";
            return ok;
        }

        /// <summary>叫 IE / Edge / Chrome 一声"设置变了"，别让已经开着的窗口抱着旧设置在内存里。</summary>
        public static void NotifyBrowsers()
        {
            try
            {
                const int INTERNET_OPTION_REFRESH = 37;
                const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        // ------------------------------------------------------------------ 原值记录

        private const string SnapshotHeader = "# mxx1-toolbox proxy snapshot v1";

        private static string Clean(string s)
        {
            if (s == null) { return ""; }
            return s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }

        private static void SaveSnapshot(ProxyState st)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(SnapshotHeader).Append("\r\n");
            foreach (ProxyValue v in st.Values)
            {
                string kind = !v.Exists ? "absent" : (v.IsDword ? "dword" : "sz");
                string val = v.IsDword ? v.Dword.ToString(CultureInfo.InvariantCulture) : Clean(v.Text);
                sb.Append(Clean(v.Name)).Append('\t').Append(kind).Append('\t').Append(val).Append("\r\n");
            }
            string p = BackupPath();
            try
            {
                string dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }
            }
            catch { }
            File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
        }

        private static List<ProxyValue> LoadSnapshot()
        {
            List<ProxyValue> list = new List<ProxyValue>();
            string[] lines = File.ReadAllLines(BackupPath(), new UTF8Encoding(false));
            foreach (string raw in lines)
            {
                string line = raw;
                if (line == null) { continue; }
                line = line.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) { continue; }
                string[] parts = line.Split('\t');
                if (parts.Length < 3) { continue; }
                ProxyValue v = new ProxyValue();
                v.Name = parts[0];
                string kind = parts[1];
                v.Exists = (kind != "absent");
                v.IsDword = (kind == "dword");
                if (v.IsDword)
                {
                    int n = 0;
                    int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                    v.Dword = n;
                }
                else { v.Text = parts[2]; }
                list.Add(v);
            }
            return list;
        }

        /// <summary>窗口里那句"上次关掉之前是什么样"（没记录就返回空）。</summary>
        public static string BackupSummary()
        {
            try
            {
                List<ProxyValue> saved = LoadSnapshot();
                ProxyState fake = new ProxyState();
                fake.Values = saved;
                if (saved.Count == 0) { return ""; }
                return fake.Describe();
            }
            catch { return ""; }
        }
    }
}
