// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Mxx1Toolbox
{
    /// <summary>一个音频端点（输出或输入设备）。</summary>
    internal sealed class AudioDevice
    {
        public string Id = "";             // {0.0.0.00000000}.{guid} —— 系统里的端点 id
        public string Flow = "";           // 「输出」/「输入」
        public int FlowValue;              // 0 = eRender，1 = eCapture
        public string Name = "";           // 人看得懂的名字
        public string StateText = "";      // 正在使用 / 已停用 / 没插 / 不在了
        public int State;                  // DEVICE_STATE_*
        public bool IsDefault;             // 是「任一角色」的默认设备
        public string DefaultRoles = "";   // 普通 / 多媒体 / 通话 —— 用在哪几个角色上
        public string Problem = "";        // 状态不对劲时的一句话（给窗口/命令行直接用）
    }

    /// <summary>「输出设备体检」的后端（GAP-02）—— 枚举音频端点、看谁是默认、**真切**默认设备。
    ///
    /// 为什么不用 `Get-Device` 这类 cmdlet：那套是 Windows 10 才有的，而且"设为默认"在
    /// Windows 自己的命令行里根本没有（设置应用里才点得到）。这里走的是 Windows 自带的
    /// **MMDevice**（Core Audio）COM 接口 + 系统内部那个 `IPolicyConfig`，从 Win7 到 Win11 都在：
    ///   · 枚举 / 状态：IMMDeviceEnumerator.EnumAudioEndpoints（含已停用 / 没插的，方便体检）
    ///   · 默认设备：IMMDeviceEnumerator.GetDefaultAudioEndpoint（三个角色各问一次）
    ///   · 改默认：IPolicyConfig.SetDefaultEndpoint（三个角色一起设，和设置应用里的效果一致）
    ///
    /// ⚠️ 设备名走注册表（`MMDevices\Audio\Render|Capture\{guid}\Properties` 里的
    /// Device_FriendlyName / DeviceInterface_FriendlyName / DeviceDesc），**不用** IPropertyStore ——
    /// 少一层 PROPVARIANT 手写 marshaling，少一类会崩的地方；读不到就如实写「（没读到名字）」。
    /// ⚠️ 名字只读，写入只有 SetDefaultEndpoint 这一处；不删设备、不禁用设备、不改音量。</summary>
    internal static class AudioDevices
    {
        // ---------------------------------------------------------------- COM 声明

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        /// <summary>系统内部的 IPolicyConfig（没有公开文档，但 Win7～Win11 都在）。
        /// ⚠️ 方法顺序必须和 vtable 完全一致 —— 错一个，SetDefaultEndpoint 就会调到别的函数上。</summary>
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr ppFormat);
            [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                [MarshalAs(UnmanagedType.Bool)] bool bDefault, out IntPtr ppFormat);
            [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
            [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                IntPtr pEndpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                [MarshalAs(UnmanagedType.Bool)] bool bDefault, out long pmftDefaultPeriod, out long pmftMinimumPeriod);
            [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ref long pmftPeriod);
            [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
            [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);
            [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                [MarshalAs(UnmanagedType.Bool)] bool bFxStore, IntPtr key, IntPtr pv);
            [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                [MarshalAs(UnmanagedType.Bool)] bool bFxStore, IntPtr key, IntPtr pv);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                [MarshalAs(UnmanagedType.Bool)] bool bVisible);
        }

        private static readonly Guid ClsidPolicyConfig = new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9");
        private static readonly Guid ClsidPolicyConfigVista = new Guid("294935CE-F637-4E7C-A41B-AB255460B862");

        private const int EDataFlowRender = 0;
        private const int EDataFlowCapture = 1;
        private const int EConsole = 0;
        private const int EMultimedia = 1;
        private const int ECommunications = 2;
        private const int StateMaskAll = 0x0F;   // ACTIVE | DISABLED | NOTPRESENT | UNPLUGGED
        private const int DeviceStateActive = 0x01;
        private const int DeviceStateDisabled = 0x02;
        private const int DeviceStateNotPresent = 0x04;
        private const int DeviceStateUnplugged = 0x08;

        /// <summary>最近一次失败的原因（窗口 / 命令行都直接显示这句，不自己编）。</summary>
        public static string LastError = "";

        // ---------------------------------------------------------------- 枚举

        /// <summary>列出音频端点。includeInactive = 连"已停用 / 没插 / 不在了"也列出来（体检要看它们）。
        /// 返回空列表时看 LastError —— 这台机器可能连音频服务都没有（精简版 Windows）。</summary>
        public static List<AudioDevice> List(bool includeInactive)
        {
            LastError = "";
            List<AudioDevice> list = new List<AudioDevice>();
            object com = null;
            try
            {
                com = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), false));
            }
            catch (Exception ex)
            {
                LastError = "这台机器上叫不起 Windows 的音频接口（Core Audio）：" + ex.Message;
                return list;
            }

            try
            {
                IMMDeviceEnumerator en = (IMMDeviceEnumerator)com;
                AddFlow(en, EDataFlowRender, "输出", includeInactive, list);
                AddFlow(en, EDataFlowCapture, "输入", includeInactive, list);
            }
            catch (Exception ex)
            {
                LastError = "读音频设备时出错：" + ex.Message;
            }
            finally
            {
                Release(com);
            }
            return list;
        }

        private static void AddFlow(IMMDeviceEnumerator en, int flow, string flowText, bool includeInactive, List<AudioDevice> list)
        {
            // 每个角色各问一次默认设备：三个角色可能不是同一台（比如通话用耳机、放歌用音箱）
            string defConsole = DefaultId(en, flow, EConsole);
            string defMedia = DefaultId(en, flow, EMultimedia);
            string defComm = DefaultId(en, flow, ECommunications);

            IMMDeviceCollection col = null;
            int hr = en.EnumAudioEndpoints(flow, includeInactive ? StateMaskAll : DeviceStateActive, out col);
            if (hr != 0 || col == null)
            {
                LastError = "系统没返回" + flowText + "设备列表（HRESULT 0x" + hr.ToString("X8", CultureInfo.InvariantCulture) + "）";
                return;
            }
            int count;
            if (col.GetCount(out count) != 0) { return; }
            for (int i = 0; i < count; i++)
            {
                IMMDevice dev;
                if (col.Item(i, out dev) != 0 || dev == null) { continue; }
                try
                {
                    string id;
                    int st;
                    if (dev.GetId(out id) != 0) { continue; }
                    if (dev.GetState(out st) != 0) { st = 0; }
                    AudioDevice d = new AudioDevice();
                    d.Id = id ?? "";
                    d.Flow = flowText;
                    d.FlowValue = flow;
                    d.State = st;
                    d.StateText = StateText(st);
                    d.Name = FriendlyName(flow, d.Id);
                    List<string> roles = new List<string>();
                    if (SameId(id, defConsole)) { roles.Add("普通"); }
                    if (SameId(id, defMedia)) { roles.Add("多媒体"); }
                    if (SameId(id, defComm)) { roles.Add("通话"); }
                    d.IsDefault = roles.Count > 0;
                    d.DefaultRoles = string.Join(" / ", roles.ToArray());
                    d.Problem = ProblemFor(d);
                    list.Add(d);
                }
                finally { Release(dev); }
            }
            Release(col);
        }

        private static string DefaultId(IMMDeviceEnumerator en, int flow, int role)
        {
            IMMDevice dev = null;
            try
            {
                if (en.GetDefaultAudioEndpoint(flow, role, out dev) != 0 || dev == null) { return ""; }
                string id;
                if (dev.GetId(out id) != 0) { return ""; }
                return id ?? "";
            }
            catch { return ""; }
            finally { Release(dev); }
        }

        private static bool SameId(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string StateText(int state)
        {
            if ((state & DeviceStateActive) != 0) { return "正在使用"; }
            if ((state & DeviceStateDisabled) != 0) { return "已停用"; }
            if ((state & DeviceStateUnplugged) != 0) { return "没插着"; }
            if ((state & DeviceStateNotPresent) != 0) { return "不在了"; }
            return "状态未知";
        }

        private static string ProblemFor(AudioDevice d)
        {
            if (d.State == DeviceStateActive)
            {
                return d.IsDefault ? "" : "能用，但不是默认设备";
            }
            if (d.State == DeviceStateDisabled) { return "被停用了 —— 要在「设备管理器」里重新启用"; }
            if (d.State == DeviceStateUnplugged) { return "没插着（线 / 蓝牙没连上）"; }
            if (d.State == DeviceStateNotPresent) { return "驱动里还记着，但设备已经不在了"; }
            return "";
        }

        // ---------------------------------------------------------------- 名字（只读注册表）

        /// <summary>设备的人话名字：优先 FriendlyName，其次接口名，最后设备描述。
        /// 都没有就说「（没读到名字）」，**不拿 id 冒充名字**。</summary>
        private static string FriendlyName(int flow, string id)
        {
            string guid = GuidPart(id);
            if (guid.Length == 0) { return "（没读到名字）"; }
            string root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\"
                + (flow == EDataFlowCapture ? "Capture" : "Render");
            string sub = root + @"\" + guid + @"\Properties";
            string[] names = new string[]
            {
                "{a45c254e-df1c-4efd-8020-67d146a850e0},14",   // PKEY_Device_FriendlyName
                "{b3f8fa53-0004-438e-9003-51a46e139bfc},6",    // PKEY_DeviceInterface_FriendlyName
                "{a45c254e-df1c-4efd-8020-67d146a850e0},2"     // PKEY_Device_DeviceDesc（往往是"耳机"这种泛称）
            };
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(sub, false))
                {
                    if (k != null)
                    {
                        foreach (string n in names)
                        {
                            string v = k.GetValue(n) as string;
                            if (!string.IsNullOrEmpty(v)) { return v.Trim(); }
                        }
                    }
                }
            }
            catch { }
            return "（没读到名字）";
        }

        /// <summary>从 `{0.0.0.00000000}.{guid}` 里取最后那个 {guid}（注册表按它存）。</summary>
        private static string GuidPart(string id)
        {
            if (string.IsNullOrEmpty(id)) { return ""; }
            int dot = id.LastIndexOf('.');
            if (dot < 0 || dot + 1 >= id.Length) { return ""; }
            string g = id.Substring(dot + 1).Trim();
            if (g.StartsWith("{") && g.EndsWith("}") && g.Length > 2) { return g; }
            return "";
        }

        // ---------------------------------------------------------------- 改默认设备

        /// <summary>把某个端点设成**三个角色**（普通 / 多媒体 / 通话）的默认设备。
        /// 返回 false 时 message 里是失败原因（不猜、不吞）。</summary>
        public static bool SetDefault(string deviceId, out string message)
        {
            message = "";
            if (string.IsNullOrEmpty(deviceId)) { message = "没给设备 id"; return false; }
            object pol = null;
            try
            {
                try
                {
                    pol = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidPolicyConfig, false));
                }
                catch
                {
                    // Win7 / 老一点的系统上是 Vista 那个 CLSID
                    pol = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidPolicyConfigVista, false));
                }
                if (pol == null) { message = "这台机器上叫不起系统的音频策略接口（IPolicyConfig）"; return false; }
                IPolicyConfig cfg = (IPolicyConfig)pol;
                int[] roles = new int[] { EConsole, EMultimedia, ECommunications };
                string[] roleNames = new string[] { "普通", "多媒体", "通话" };
                List<string> bad = new List<string>();
                for (int i = 0; i < roles.Length; i++)
                {
                    int hr = cfg.SetDefaultEndpoint(deviceId, roles[i]);
                    if (hr != 0)
                    {
                        bad.Add(roleNames[i] + "（0x" + hr.ToString("X8", CultureInfo.InvariantCulture) + "）");
                    }
                }
                if (bad.Count > 0)
                {
                    message = "这些角色没设成：" + string.Join("、", bad.ToArray());
                    return false;
                }
                message = "已设为默认设备（普通 / 多媒体 / 通话三个角色都改了）";
                return true;
            }
            catch (Exception ex)
            {
                message = "设为默认设备失败：" + ex.Message;
                return false;
            }
            finally
            {
                Release(pol);
            }
        }

        // ---------------------------------------------------------------- 音频服务（只读）

        /// <summary>音频服务状态（"Audiosrv=Running" 这种）。服务不存在就如实说 —— 精简版 Windows 上真没有。</summary>
        public static string[] Services()
        {
            List<string> lines = new List<string>();
            foreach (string name in new string[] { "Audiosrv", "AudioEndpointBuilder" })
            {
                try
                {
                    using (System.ServiceProcess.ServiceController sc = new System.ServiceProcess.ServiceController(name))
                    {
                        lines.Add(name + "=" + sc.Status.ToString());
                    }
                }
                catch (Exception ex)
                {
                    lines.Add(name + "=（读不到：" + ex.Message + "）");
                }
            }
            return lines.ToArray();
        }

        private static void Release(object com)
        {
            if (com == null) { return; }
            try { Marshal.FinalReleaseComObject(com); } catch { }
        }
    }
}
