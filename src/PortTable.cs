// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;

namespace Mxx1Toolbox
{
    /// <summary>「端口与进程」的后端：列出**正在监听的 TCP 端口**和**绑着的 UDP 端口**，
    /// 以及每个端口属于哪个进程。窗口（PortsForm）和命令行（`ports`）共用这一份。
    ///
    /// 数据来源是 iphlpapi 的 `GetExtendedTcpTable` / `GetExtendedUdpTable`（Windows XP SP2 起就有，
    /// Win7～Win11 都在）：一次把整张表连同 PID 拿回来，**不需要管理员、不调 netstat、不解析本地化文本**。
    ///
    /// 三条口径：
    /// ① **UDP 不叫"监听"**：UDP 是无连接的，表里只有"绑在这个端口上的套接字"。窗口标题和列里
    ///    都写「UDP（已绑定）」，不跟着 netstat 的"UDP 监听"一起说谎；
    /// ② **进程路径读不到就说读不到**：不是管理员时有些进程的 MainModule 打不开 —— 那一列写
    ///    「读不到路径（需要管理员）」，不猜、不空着；
    /// ③ **签名是本地校验**：WinVerifyTrust 只查文件自己的签名，要联网的一律关掉
    ///    （WTD_CACHE_ONLY_URL_RETRIEVAL + 不查吊销）—— 这个工具任何时候都不联网。
    ///
    /// 保护名单和「一键解除占用」共用一份（`FileLock.IsProtectedName`）：系统关键进程、pid ≤ 4、
    /// explorer、工具箱自己，窗口里**列出来但不给结束**（命令行 `ports` 也只读，没有 kill 入口）。</summary>
    internal static class PortTable
    {
        public sealed class Endpoint
        {
            public string Proto = "TCP";      // TCP / UDP
            public string LocalAddr = "";     // 0.0.0.0 / :: / 127.0.0.1 …
            public int Port = 0;
            public int Pid = 0;
            public string ProcName = "";
            public string ProcPath = "";
            public string Signature = "";     // signed / unsigned / unknown / （空 = 没有路径）
            public bool Protected = false;
            public string ProtectedWhy = "";

            public string SignatureText
            {
                get
                {
                    if (Signature == "signed") { return "有"; }
                    if (Signature == "unsigned") { return "无"; }
                    if (Signature == "unknown") { return "读不到"; }
                    return "—";
                }
            }
        }

        // ------------------------------------------------------------ P/Invoke

        private const int AF_INET = 2;
        private const int AF_INET6 = 23;
        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const int UDP_TABLE_OWNER_PID = 1;
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;
        private const uint MIB_TCP_STATE_LISTEN = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] localAddr;
            public uint localScopeId;
            public uint localPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] remoteAddr;
            public uint remoteScopeId;
            public uint remotePort;
            public uint state;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            public uint localPort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] localAddr;
            public uint localScopeId;
            public uint localPort;
            public uint owningPid;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder,
            int ulAf, int tableClass, uint reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize, bool bOrder,
            int ulAf, int tableClass, uint reserved);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, EntryPoint = "WinVerifyTrust", ExactSpelling = true)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid actionId, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class WinTrustFileInfo
        {
            public int cbStruct = Marshal.SizeOf(typeof(WinTrustFileInfo));
            public string pcwszFilePath;
            public IntPtr hFile = IntPtr.Zero;
            public IntPtr pgKnownSubject = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class WinTrustData
        {
            public int cbStruct = Marshal.SizeOf(typeof(WinTrustData));
            public IntPtr pPolicyCallbackData = IntPtr.Zero;
            public IntPtr pSIPClientData = IntPtr.Zero;
            public int dwUIChoice = 2;              // WTD_UI_NONE
            public int fdwRevocationChecks = 0;     // WTD_REVOKE_NONE（不联网）
            public int dwUnionChoice = 1;           // WTD_CHOICE_FILE
            public IntPtr pFile = IntPtr.Zero;
            public int dwStateAction = 0;
            public IntPtr hWVTStateData = IntPtr.Zero;
            public IntPtr pwszURLReference = IntPtr.Zero;
            public int dwProvFlags = 0x10;          // WTD_CACHE_ONLY_URL_RETRIEVAL（不联网）
            public int dwUIContext = 0;
        }

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        private const uint TRUST_E_NOSIGNATURE = 0x800B0100;

        // ------------------------------------------------------------ 读表

        /// <summary>读一遍当前所有 TCP（仅 LISTEN）与 UDP 端点。error 非空 = 完全没读成。</summary>
        public static List<Endpoint> List(out string error)
        {
            error = "";
            List<Endpoint> result = new List<Endpoint>();
            try
            {
                ReadTcp4(result);
                ReadTcp6(result);
                ReadUdp4(result);
                ReadUdp6(result);
            }
            catch (Exception ex)
            {
                error = "读端口表失败：" + ex.Message;
                return result;
            }

            Dictionary<string, string> sigCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Endpoint ep in result)
            {
                FillProcess(ep, sigCache);
            }

            result.Sort(delegate(Endpoint a, Endpoint b)
            {
                int byPort = a.Port.CompareTo(b.Port);
                if (byPort != 0) { return byPort; }
                int byProto = string.Compare(a.Proto, b.Proto, StringComparison.Ordinal);
                if (byProto != 0) { return byProto; }
                return string.Compare(a.LocalAddr, b.LocalAddr, StringComparison.Ordinal);
            });
            return result;
        }

        private static void ReadTcp4(List<Endpoint> result)
        {
            byte[] table = ReadTable(false, AF_INET, TCP_TABLE_OWNER_PID_ALL);
            if (table == null) { return; }
            int count = BitConverter.ToInt32(table, 0);
            int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
            int offset = 4;
            for (int i = 0; i < count && offset + rowSize <= table.Length; i++, offset += rowSize)
            {
                MIB_TCPROW_OWNER_PID row = (MIB_TCPROW_OWNER_PID)BytesToStruct(table, offset, typeof(MIB_TCPROW_OWNER_PID));
                if (row.state != MIB_TCP_STATE_LISTEN) { continue; }
                Endpoint ep = new Endpoint();
                ep.Proto = "TCP";
                ep.LocalAddr = IPv4Text(row.localAddr);
                ep.Port = PortOf(row.localPort);
                ep.Pid = (int)row.owningPid;
                result.Add(ep);
            }
        }

        private static void ReadTcp6(List<Endpoint> result)
        {
            byte[] table = ReadTable(false, AF_INET6, TCP_TABLE_OWNER_PID_ALL);
            if (table == null) { return; }
            int count = BitConverter.ToInt32(table, 0);
            int rowSize = Marshal.SizeOf(typeof(MIB_TCP6ROW_OWNER_PID));
            int offset = 4;
            for (int i = 0; i < count && offset + rowSize <= table.Length; i++, offset += rowSize)
            {
                MIB_TCP6ROW_OWNER_PID row = (MIB_TCP6ROW_OWNER_PID)BytesToStruct(table, offset, typeof(MIB_TCP6ROW_OWNER_PID));
                if (row.state != MIB_TCP_STATE_LISTEN) { continue; }
                Endpoint ep = new Endpoint();
                ep.Proto = "TCP";
                ep.LocalAddr = IPv6Text(row.localAddr, row.localScopeId);
                ep.Port = PortOf(row.localPort);
                ep.Pid = (int)row.owningPid;
                result.Add(ep);
            }
        }

        private static void ReadUdp4(List<Endpoint> result)
        {
            byte[] table = ReadTable(true, AF_INET, UDP_TABLE_OWNER_PID);
            if (table == null) { return; }
            int count = BitConverter.ToInt32(table, 0);
            int rowSize = Marshal.SizeOf(typeof(MIB_UDPROW_OWNER_PID));
            int offset = 4;
            for (int i = 0; i < count && offset + rowSize <= table.Length; i++, offset += rowSize)
            {
                MIB_UDPROW_OWNER_PID row = (MIB_UDPROW_OWNER_PID)BytesToStruct(table, offset, typeof(MIB_UDPROW_OWNER_PID));
                Endpoint ep = new Endpoint();
                ep.Proto = "UDP";
                ep.LocalAddr = IPv4Text(row.localAddr);
                ep.Port = PortOf(row.localPort);
                ep.Pid = (int)row.owningPid;
                result.Add(ep);
            }
        }

        private static void ReadUdp6(List<Endpoint> result)
        {
            byte[] table = ReadTable(true, AF_INET6, UDP_TABLE_OWNER_PID);
            if (table == null) { return; }
            int count = BitConverter.ToInt32(table, 0);
            int rowSize = Marshal.SizeOf(typeof(MIB_UDP6ROW_OWNER_PID));
            int offset = 4;
            for (int i = 0; i < count && offset + rowSize <= table.Length; i++, offset += rowSize)
            {
                MIB_UDP6ROW_OWNER_PID row = (MIB_UDP6ROW_OWNER_PID)BytesToStruct(table, offset, typeof(MIB_UDP6ROW_OWNER_PID));
                Endpoint ep = new Endpoint();
                ep.Proto = "UDP";
                ep.LocalAddr = IPv6Text(row.localAddr, row.localScopeId);
                ep.Port = PortOf(row.localPort);
                ep.Pid = (int)row.owningPid;
                result.Add(ep);
            }
        }

        /// <summary>问一次大小 → 分配 → 再问一次 → 拷成 byte[]（结构体直接用 Marshal 读）。
        /// 读失败返回 null（调用方当成"这一类没有"，整个功能不因此崩掉）。</summary>
        private static byte[] ReadTable(bool udp, int af, int tableClass)
        {
            int size = 0;
            uint rc = udp
                ? GetExtendedUdpTable(IntPtr.Zero, ref size, false, af, tableClass, 0)
                : GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, tableClass, 0);
            if (rc != ERROR_INSUFFICIENT_BUFFER || size <= 0) { return null; }

            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                rc = udp
                    ? GetExtendedUdpTable(buf, ref size, false, af, tableClass, 0)
                    : GetExtendedTcpTable(buf, ref size, false, af, tableClass, 0);
                if (rc != 0) { return null; }
                byte[] data = new byte[size];
                Marshal.Copy(buf, data, 0, size);
                return data;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static object BytesToStruct(byte[] data, int offset, Type type)
        {
            IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(type));
            try
            {
                Marshal.Copy(data, offset, p, Marshal.SizeOf(type));
                return Marshal.PtrToStructure(p, type);
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        /// <summary>IPv4 地址：dwLocalAddr 是网络字节序，在小端机器上读成 uint 后低字节就是第一段。</summary>
        private static string IPv4Text(uint addr)
        {
            return ((addr) & 0xFF).ToString(CultureInfo.InvariantCulture) + "."
                + ((addr >> 8) & 0xFF).ToString(CultureInfo.InvariantCulture) + "."
                + ((addr >> 16) & 0xFF).ToString(CultureInfo.InvariantCulture) + "."
                + ((addr >> 24) & 0xFF).ToString(CultureInfo.InvariantCulture);
        }

        private static string IPv6Text(byte[] addr, uint scopeId)
        {
            try
            {
                if (addr == null || addr.Length != 16) { return ""; }
                IPAddress ip = (scopeId > 0) ? new IPAddress(addr, scopeId) : new IPAddress(addr);
                return ip.ToString();
            }
            catch { return ""; }
        }

        private static int PortOf(uint portInNetworkOrder)
        {
            return (int)(ushort)IPAddress.NetworkToHostOrder((short)portInNetworkOrder);
        }

        // ------------------------------------------------------------ 进程与签名

        private static void FillProcess(Endpoint ep, Dictionary<string, string> sigCache)
        {
            string name = "";
            string path = "";
            try
            {
                if (ep.Pid == 0) { name = "System Idle Process"; }
                else if (ep.Pid == 4) { name = "System"; }
                else
                {
                    Process p = Process.GetProcessById(ep.Pid);
                    name = p.ProcessName;
                    try { path = p.MainModule.FileName; }
                    catch { path = ""; }
                }
            }
            catch
            {
                name = "";
            }
            ep.ProcName = name.Length > 0 ? name : ("（进程 " + ep.Pid.ToString(CultureInfo.InvariantCulture) + " 读不到名字，可能需要管理员）");
            ep.ProcPath = path;
            ep.Signature = SignatureOf(path, sigCache);

            string bare = name.ToLowerInvariant();
            if (bare.EndsWith(".exe", StringComparison.Ordinal)) { bare = bare.Substring(0, bare.Length - 4); }
            if (ep.Pid <= 4)
            {
                ep.Protected = true;
                ep.ProtectedWhy = "系统进程，不能结束";
            }
            else if (bare.Length > 0 && FileLock.IsProtectedName(bare))
            {
                ep.Protected = true;
                ep.ProtectedWhy = "系统关键程序，不能结束";
            }
            else if (string.Equals(bare, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                ep.Protected = true;
                ep.ProtectedWhy = "结束它 = 桌面重启一次";
            }
            else if (ep.Pid == Process.GetCurrentProcess().Id)
            {
                ep.Protected = true;
                ep.ProtectedWhy = "这是工具箱自己";
            }
        }

        /// <summary>"有这个文件、签名校验通过"= signed；明确没有签名 = unsigned；
        /// 其它错误（证书不受信任、读不了文件）一律 unknown —— 不把"验不了"说成"没有签名"。</summary>
        private static string SignatureOf(string path, Dictionary<string, string> cache)
        {
            if (path == null || path.Trim().Length == 0) { return ""; }
            string hit;
            if (cache.TryGetValue(path, out hit)) { return hit; }

            string state = "unknown";
            IntPtr filePtr = IntPtr.Zero;
            IntPtr dataPtr = IntPtr.Zero;
            try
            {
                WinTrustFileInfo file = new WinTrustFileInfo();
                file.pcwszFilePath = path;
                filePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(file, filePtr, false);

                WinTrustData data = new WinTrustData();
                data.pFile = filePtr;
                dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData)));
                Marshal.StructureToPtr(data, dataPtr, false);

                Guid action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                uint rc = WinVerifyTrust(IntPtr.Zero, ref action, dataPtr);
                if (rc == 0) { state = "signed"; }
                else if (rc == TRUST_E_NOSIGNATURE) { state = "unsigned"; }
                else { state = "unknown"; }
            }
            catch { state = "unknown"; }
            finally
            {
                if (filePtr != IntPtr.Zero) { Marshal.FreeHGlobal(filePtr); }
                if (dataPtr != IntPtr.Zero) { Marshal.FreeHGlobal(dataPtr); }
            }
            cache[path] = state;
            return state;
        }

        // ------------------------------------------------------------ 结束进程（窗口里点）

        /// <summary>结束一个进程。**保护名单先过一遍**（这个函数自己再挡一次，别只靠界面灰按钮）。
        /// 失败说人话：拒绝访问 = 需要管理员；进程已经没了 = 它自己退了。</summary>
        public static bool Kill(Endpoint ep, out string message)
        {
            message = "";
            if (ep == null) { message = "没有选中一行"; return false; }
            if (ep.Protected) { message = "这个不能结束：" + ep.ProtectedWhy; return false; }
            try
            {
                Process p = Process.GetProcessById(ep.Pid);
                p.Kill();
                bool exited = p.WaitForExit(4000);
                message = exited
                    ? ("已结束 " + ep.ProcName + "（PID " + ep.Pid.ToString(CultureInfo.InvariantCulture) + "）")
                    : ("已经让 " + ep.ProcName + " 结束了，但它 4 秒内还没退干净（可能正卡在驱动里）");
                return exited;
            }
            catch (ArgumentException)
            {
                message = "这个进程已经不在了（PID " + ep.Pid.ToString(CultureInfo.InvariantCulture) + "）";
                return false;
            }
            catch (Exception ex)
            {
                string why = ex.Message;
                if (ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
                {
                    why = "没有权限结束它（它可能属于别的账户或系统服务）—— 需要以管理员身份运行工具箱";
                }
                message = "没结束成：" + why;
                return false;
            }
        }
    }
}
