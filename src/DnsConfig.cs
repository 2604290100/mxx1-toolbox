// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>一块网卡现在用的是哪几个 DNS（只读读出来的样子）。</summary>
    internal sealed class DnsAdapter
    {
        public string Name = "";
        public string Description = "";
        public string Id = "";
        public bool IsUp;
        public List<string> Dns = new List<string>();
        public bool LooksDhcp;      // DNS 是空的 / 就是网关那台 → 多半是从 DHCP 拿的
    }

    /// <summary>「一键换 DNS」的后端（GAP-09）。
    ///
    /// 三条口径：
    /// ① **读**走 .NET 自带的 `NetworkInterface`（Win7 起就有），不解析 `ipconfig` 的本机化文字；
    /// ② **写**走 Windows 自带的 `netsh interface ipv4 set/add dnsservers`（关键字是英文、不随系统语言变），
    ///    写完再**读回来核对**，核对不上就如实说失败，不假装成功；
    /// ③ 三个选项里有个 **「改回自动（DHCP）」** 兜底 —— 用户改完发现更慢，点一下就回原样；
    ///    这个功能只动 DNS，**不改 IP / 网关 / 代理**。
    ///
    /// ⚠️ 测试隔离：写的那条路只认 `MXX1_NETSH` 指定的假程序（测试里指到一个只记参数、什么都不做的 .cmd），
    ///    没设这个变量时**命令行一律拒绝写**（退出码 2）—— 绝不能让一句命令把用户的 DNS 改掉。
    ///    窗口里点「应用」是**真的**改（它自己会弹确认框、要求管理员）。</summary>
    internal static class DnsConfig
    {
        public static string LastError = "";
        public static string LastCommand = "";

        /// <summary>114 / 阿里 / 自动（DHCP）三选。</summary>
        public const string Choice114 = "114";
        public const string ChoiceAli = "ali";
        public const string ChoiceDhcp = "dhcp";

        public static List<DnsAdapter> List()
        {
            LastError = "";
            List<DnsAdapter> list = new List<DnsAdapter>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) { continue; }
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) { continue; }
                    IPInterfaceProperties props;
                    try { props = ni.GetIPProperties(); }
                    catch { continue; }
                    if (props == null) { continue; }
                    if (props.UnicastAddresses.Count == 0) { continue; }

                    DnsAdapter a = new DnsAdapter();
                    a.Name = ni.Name;
                    a.Description = ni.Description;
                    a.Id = ni.Id;
                    a.IsUp = (ni.OperationalStatus == OperationalStatus.Up);
                    foreach (IPAddress ip in props.DnsAddresses)
                    {
                        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { a.Dns.Add(ip.ToString()); }
                    }
                    a.LooksDhcp = (a.Dns.Count == 0);
                    list.Add(a);
                }
            }
            catch (Exception ex)
            {
                LastError = "读网卡列表失败：" + ex.Message;
            }
            list.Sort(delegate(DnsAdapter x, DnsAdapter y)
            {
                if (x.IsUp != y.IsUp) { return x.IsUp ? -1 : 1; }
                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>这个选项要写的两个地址（dhcp 返回空数组 = 改回自动）。</summary>
        public static string[] ServersOf(string choice)
        {
            if (choice == Choice114) { return new string[] { "114.114.114.114", "114.114.115.115" }; }
            if (choice == ChoiceAli) { return new string[] { "223.5.5.5", "223.6.6.6" }; }
            return new string[0];
        }

        public static string TextOf(string choice)
        {
            if (choice == Choice114) { return "114 DNS（114.114.114.114 / 114.114.115.115）"; }
            if (choice == ChoiceAli) { return "阿里 DNS（223.5.5.5 / 223.6.6.6）"; }
            return "自动获取（改回 DHCP，等于「还原」）";
        }

        /// <summary>读现在这块网卡上到底是哪几个 DNS（写完之后拿它核对）。</summary>
        public static List<string> CurrentDns(string adapterName)
        {
            List<string> got = new List<string>();
            foreach (DnsAdapter a in List())
            {
                if (string.Equals(a.Name, adapterName, StringComparison.OrdinalIgnoreCase))
                {
                    got.AddRange(a.Dns);
                }
            }
            return got;
        }

        /// <summary>真改。成功后**读回核对**；`message` 里写明做了什么、核对结果是什么。</summary>
        public static bool Apply(string adapterName, string choice, out string message)
        {
            StringBuilder log = new StringBuilder();
            LastError = "";
            LastCommand = "";
            if (string.IsNullOrEmpty(adapterName)) { message = "没选网卡"; return false; }
            bool dhcp = (choice == ChoiceDhcp);
            string[] servers = ServersOf(choice);

            bool ok = true;
            if (dhcp)
            {
                ok = RunNetsh("interface ipv4 set dnsservers name=\"" + adapterName + "\" dhcp", log, out message);
            }
            else
            {
                ok = RunNetsh("interface ipv4 set dnsservers name=\"" + adapterName + "\" static " + servers[0] + " primary", log, out message);
                for (int i = 1; ok && i < servers.Length; i++)
                {
                    ok = RunNetsh("interface ipv4 add dnsservers name=\"" + adapterName + "\" " + servers[i] + " index=" + (i + 1).ToString(CultureInfo.InvariantCulture), log, out message);
                }
            }
            if (!ok) { return false; }

            // 刷新 DNS 缓存，免得看起来"改了没生效"
            FlushDns();

            // ---- 读回核对 ----
            List<string> now = CurrentDns(adapterName);
            if (dhcp)
            {
                // 改回自动之后可能立刻变成 DHCP 下发的地址，也可能暂时是空的 —— 两种都算成功
                message = "已经改回自动获取（DHCP）。现在这块网卡上的 DNS：" + (now.Count == 0 ? "（空，等 DHCP 下发）" : string.Join(" / ", now.ToArray()));
                return true;
            }
            List<string> missing = new List<string>();
            foreach (string s in servers)
            {
                if (!now.Contains(s)) { missing.Add(s); }
            }
            if (missing.Count > 0)
            {
                message = "命令发出去了，但读回来没看到 " + string.Join(" / ", missing.ToArray())
                    + "（现在这块网卡上是：" + (now.Count == 0 ? "空" : string.Join(" / ", now.ToArray())) + "）";
                return false;
            }
            message = "改好了并读回核对过：这块网卡现在的 DNS 是 " + string.Join(" / ", now.ToArray()) + "。"
                + "打不开的网页可以再试一次（浏览器可能还缓存着旧的解析结果）。";
            return true;
        }

        public static void FlushDns()
        {
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(ExeOf("ipconfig.exe"), "/flushdns");
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardOutput = true;
                si.RedirectStandardError = true;
                using (Process p = Process.Start(si))
                {
                    if (p != null)
                    {
                        p.StandardOutput.ReadToEnd();
                        p.StandardError.ReadToEnd();
                        p.WaitForExit(8000);
                    }
                }
            }
            catch { }
        }

        /// <summary>`netsh` 的路径（测试里被 `MXX1_NETSH` 顶掉）。</summary>
        public static string NetshPath()
        {
            string env = Environment.GetEnvironmentVariable("MXX1_NETSH");
            if (!string.IsNullOrEmpty(env)) { return env; }
            return "netsh.exe";
        }

        /// <summary>写的那条路**只允许在测试隔离下走**（命令行出口靠它拦）。</summary>
        public static bool WriteAllowed()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MXX1_NETSH"));
        }

        private static string ExeOf(string name)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(win))
            {
                string full = win + "\\System32\\" + name;
                try { if (System.IO.File.Exists(full)) { return full; } } catch { }
            }
            return name;
        }

        private static bool RunNetsh(string args, StringBuilder log, out string message)
        {
            message = "";
            string exe = NetshPath();
            if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !System.IO.Path.IsPathRooted(exe))
            {
                exe = ExeOf(NetshPath());
            }
            string realArgs = args;
            // 测试隔离的那个假 netsh 可能是一个 .cmd（记录参数 + 直接退 0）。
            // ProcessStartInfo 没法直接起 .cmd（UseShellExecute=false），这里补一层 cmd /c。
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                // ⚠️ cmd /c 的引号规则很绕：如果 /c 后面第一个字符是引号，它会把**整串**的首尾引号去掉。
                //    所以写成 cmd /c ""脚本" 参数…" 这样再套一层，参数才不会被吃掉。
                realArgs = "/c \"\"" + exe + "\" " + args + "\"";
                exe = ExeOf("cmd.exe");
            }
            LastCommand = exe + " " + realArgs;
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(exe, realArgs);
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardOutput = true;
                si.RedirectStandardError = true;
                using (Process p = Process.Start(si))
                {
                    if (p == null) { message = "没能启动 netsh"; return false; }
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(15000))
                    {
                        try { p.Kill(); } catch { }
                        message = "netsh 超时没返回（网络服务可能在忙，过一会儿再试）";
                        return false;
                    }
                    if (p.ExitCode != 0)
                    {
                        message = "netsh 报错（退出码 " + p.ExitCode.ToString(CultureInfo.InvariantCulture) + "）："
                            + OneLine((e + " " + o).Trim());
                        LastError = message;
                        return false;
                    }
                    if (log != null) { log.AppendLine(o); }
                }
                return true;
            }
            catch (Exception ex)
            {
                message = "执行 netsh 失败：" + ex.Message;
                LastError = message;
                return false;
            }
        }

        private static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
