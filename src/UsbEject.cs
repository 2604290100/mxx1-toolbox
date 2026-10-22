// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>一个可以安全弹出的可移动磁盘（U 盘 / 读卡器 / 移动硬盘 —— 只在系统把它报成「可移动」时才列）。</summary>
    internal sealed class RemovableDrive
    {
        public string Letter = "";     // "E:"
        public string Root = "";       // "E:\"
        public string Label = "";
        public bool Ready = true;
        public long TotalBytes;
        public long FreeBytes;
        public string Kind = "可移动磁盘";
        public string Note = "";
    }

    /// <summary>「U 盘安全弹出」的后端（GAP-03）。
    ///
    /// 三条口径：
    /// ① **只弹盘，不删盘上任何东西**：`CM_Request_Device_Eject`（系统自己的"安全删除硬件"那条 API），
    ///    弹出前先把卷锁住 + 卸载（这样系统会把缓存刷到盘上，拔了不丢数据）；
    /// ② **锁不住就说清是谁在挡**：锁卷失败 = 还有程序开着里面的文件 —— 窗口那边配合
    ///    `FileLock.Scan` 列出占用的程序，用户选了才结束；这里只负责"能不能弹 / 弹没弹掉"；
    /// ③ **弹不出就如实说弹不出**：系统拒绝时把 veto 名字（挡着的进程 / 驱动）原样念出来，
    ///    绝不说"已弹出，可以拔了"。
    ///
    /// ⚠️ 只有 `DriveType.Removable` 才列（移动硬盘常被系统报成「固定磁盘」，那种不会出现在这里 ——
    /// 窗口里写明了让用户走任务栏的「安全删除硬件」）。⚠️ 测试夹具：`MXX1_EJECT_FAKE`
    /// （`K:=标签;L:=标签`）造几个假盘出来，真去弹会失败 —— 界面 / 命令行回归靠它验"列盘 + 按钮状态"，
    /// 绝不真弹用户插着的盘。</summary>
    internal static class UsbEject
    {
        public static string LastError = "";

        // ------------------------------------------------------------------ 列盘

        public static List<RemovableDrive> List()
        {
            LastError = "";
            List<RemovableDrive> list = new List<RemovableDrive>();

            string fake = (Environment.GetEnvironmentVariable("MXX1_EJECT_FAKE") ?? "").Trim();
            if (fake.Length > 0)
            {
                foreach (string part in fake.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string p = part.Trim();
                    string letter, label;
                    int eq = p.IndexOf('=');
                    if (eq > 0) { letter = p.Substring(0, eq).Trim(); label = p.Substring(eq + 1).Trim(); }
                    else { letter = p; label = ""; }
                    letter = letter.TrimEnd('\\');
                    if (!letter.EndsWith(":", StringComparison.Ordinal)) { letter += ":"; }
                    if (letter.Length == 0) { continue; }
                    RemovableDrive d = new RemovableDrive();
                    d.Letter = letter.ToUpperInvariant();
                    d.Root = d.Letter + "\\";
                    d.Label = label.Length > 0 ? label : "（测试夹具）";
                    d.Ready = true;
                    d.TotalBytes = 8L * 1024 * 1024 * 1024;
                    d.FreeBytes = 4L * 1024 * 1024 * 1024;
                    d.Kind = "测试夹具";
                    d.Note = "MXX1_EJECT_FAKE 造出来的假盘，真去弹会失败";
                    list.Add(d);
                }
                return list;
            }

            try
            {
                foreach (DriveInfo di in DriveInfo.GetDrives())
                {
                    DriveType t;
                    try { t = di.DriveType; } catch { continue; }
                    if (t != DriveType.Removable) { continue; }
                    RemovableDrive d = new RemovableDrive();
                    d.Letter = (di.Name ?? "").TrimEnd('\\');
                    d.Root = d.Letter + "\\";
                    d.Ready = false;
                    try { d.Ready = di.IsReady; } catch { }
                    if (d.Ready)
                    {
                        try { d.Label = di.VolumeLabel ?? ""; } catch { }
                        try { d.TotalBytes = di.TotalSize; } catch { }
                        try { d.FreeBytes = di.AvailableFreeSpace; } catch { }
                    }
                    if (d.Label.Length == 0) { d.Label = "（没有卷标）"; }
                    d.Kind = "可移动磁盘";
                    list.Add(d);
                }
            }
            catch (Exception ex)
            {
                LastError = "列可移动磁盘失败：" + ex.Message;
            }
            return list;
        }

        public static string SizeText(long bytes)
        {
            if (bytes <= 0) { return "—"; }
            double gb = bytes / 1024.0 / 1024.0 / 1024.0;
            if (gb >= 1.0) { return gb.ToString("0.0", CultureInfo.InvariantCulture) + " GB"; }
            double mb = bytes / 1024.0 / 1024.0;
            return mb.ToString("0", CultureInfo.InvariantCulture) + " MB";
        }

        // ------------------------------------------------------------------ 弹出

        /// <summary>安全弹出一个可移动磁盘。成功 = 已经可以拔了；失败 = 原因原样念出来。</summary>
        public static bool Eject(RemovableDrive drive, out string message)
        {
            LastError = "";
            message = "";
            if (drive == null) { message = "没选盘"; return false; }
            if (string.IsNullOrEmpty(drive.Letter)) { message = "这一项没有盘符，弹不了。"; return false; }

            string volume = @"\\.\" + drive.Letter;
            IntPtr h = CreateFile(volume, GenericRead | GenericWrite,
                FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (h == InvalidHandleValue)
            {
                int err = Marshal.GetLastWin32Error();
                message = "打不开 " + drive.Letter + "（Win32=" + err.ToString(CultureInfo.InvariantCulture)
                    + "）：可能已经被拔掉 / 盘没插好 / 权限不够。";
                return false;
            }

            bool locked = false;
            try
            {
                uint returned;
                // ① 锁卷：锁不住 = 还有程序开着里面的文件（系统会拒绝"独占"这个卷）
                if (!DeviceIoControl(h, FsctlLockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out returned, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    message = "锁不住 " + drive.Letter + "（Win32=" + err.ToString(CultureInfo.InvariantCulture)
                        + "）：还有程序开着这个盘里的文件。先用上面的「看谁占着」把占用的程序关掉（或者把它自己的文件夹关掉），再来一次。";
                    return false;
                }
                locked = true;

                // ② 拿设备号（后面要按它找到对应的磁盘设备）
                STORAGE_DEVICE_NUMBER sdn = new STORAGE_DEVICE_NUMBER();
                int sdnSize = Marshal.SizeOf(typeof(STORAGE_DEVICE_NUMBER));
                IntPtr sdnBuf = Marshal.AllocHGlobal(sdnSize);
                bool gotNumber = false;
                try
                {
                    if (DeviceIoControl(h, IoctlStorageGetDeviceNumber, IntPtr.Zero, 0, sdnBuf, (uint)sdnSize, out returned, IntPtr.Zero))
                    {
                        sdn = (STORAGE_DEVICE_NUMBER)Marshal.PtrToStructure(sdnBuf, typeof(STORAGE_DEVICE_NUMBER));
                        gotNumber = true;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(sdnBuf);
                }
                if (!gotNumber)
                {
                    message = "读不到 " + drive.Letter + " 的设备号，没有弹（这不影响盘上的文件）。";
                    return false;
                }

                // ③ 卸载卷（把缓存刷下去；卸载之后盘上就没有"打开的文件"了）
                if (!DeviceIoControl(h, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out returned, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    message = "卸载 " + drive.Letter + " 这个卷失败（Win32=" + err.ToString(CultureInfo.InvariantCulture)
                        + "），没有弹。盘上的文件没有被动过。";
                    return false;
                }

                // ④ 找到磁盘设备 → 它的父设备（USB 那一层）→ 请系统弹出
                uint parent;
                string findError;
                if (!FindParentDevNode(sdn.DeviceNumber, out parent, out findError))
                {
                    message = "找不到 " + drive.Letter + " 对应的磁盘设备（" + findError + "），没有弹。";
                    return false;
                }

                int vetoType = 0;
                StringBuilder vetoName = new StringBuilder(260);
                int cr = CM_Request_Device_EjectW(parent, out vetoType, vetoName, (uint)vetoName.Capacity, 0);
                if (cr != 0)
                {
                    string who = vetoName.ToString();
                    message = "系统拒绝弹出 " + drive.Letter + "（CR=" + cr.ToString(CultureInfo.InvariantCulture);
                    if (who.Length > 0) { message += "，挡着的是：" + who; }
                    message += "）。盘上的文件没有被动过，可以稍后再试。";
                    return false;
                }

                message = "已经安全弹出 " + drive.Letter + "，现在可以拔了。";
                return true;
            }
            catch (Exception ex)
            {
                message = "弹出时出错：" + ex.Message;
                return false;
            }
            finally
            {
                if (locked) { uint r; DeviceIoControl(h, FsctlUnlockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out r, IntPtr.Zero); }
                CloseHandle(h);
            }
        }

        /// <summary>按设备号找磁盘设备的父设备（USB 那一层）的 devnode。</summary>
        private static bool FindParentDevNode(int deviceNumber, out uint parent, out string error)
        {
            parent = 0;
            error = "";
            Guid guid = GuidDiskInterface;
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (set == InvalidHandleValue)
            {
                error = "SetupDiGetClassDevs 失败，Win32=" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                return false;
            }
            try
            {
                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                for (int i = 0; ; i++)
                {
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref ifData))
                    {
                        if (i == 0) { error = "系统里一个磁盘设备都没枚举到"; }
                        else { error = "没找到设备号 " + deviceNumber.ToString(CultureInfo.InvariantCulture) + " 对应的磁盘"; }
                        return false;
                    }

                    string path = InterfacePath(set, ref ifData);
                    if (path.Length == 0) { continue; }

                    int num;
                    if (!DeviceNumberOf(path, out num)) { continue; }
                    if (num != deviceNumber) { continue; }

                    SP_DEVINFO_DATA info = new SP_DEVINFO_DATA();
                    info.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, IntPtr.Zero, ref info))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != ErrorInsufficientBuffer)
                        {
                            error = "读设备信息失败，Win32=" + err.ToString(CultureInfo.InvariantCulture);
                            return false;
                        }
                    }

                    StringBuilder id = new StringBuilder(512);
                    uint required;
                    if (!SetupDiGetDeviceInstanceId(set, ref info, id, (uint)id.Capacity, out required))
                    {
                        error = "读设备实例 ID 失败，Win32=" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                        return false;
                    }

                    uint devInst;
                    int cr = CM_Locate_DevNodeW(out devInst, id.ToString(), 0);
                    if (cr != 0)
                    {
                        error = "CM_Locate_DevNode 失败，CR=" + cr.ToString(CultureInfo.InvariantCulture);
                        return false;
                    }
                    uint par;
                    cr = CM_Get_Parent(out par, devInst, 0);
                    if (cr != 0)
                    {
                        error = "CM_Get_Parent 失败，CR=" + cr.ToString(CultureInfo.InvariantCulture);
                        return false;
                    }
                    parent = par;
                    return true;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static string InterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA ifData)
        {
            uint required = 0;
            SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required == 0) { return ""; }
            IntPtr buf = Marshal.AllocHGlobal((int)required);
            try
            {
                // cbSize 只认 6（x86）/ 8（x64），多一个字都不行
                Marshal.WriteInt32(buf, 0, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref ifData, buf, required, out required, IntPtr.Zero)) { return ""; }
                return Marshal.PtrToStringAuto(new IntPtr(buf.ToInt64() + 4)) ?? "";
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static bool DeviceNumberOf(string path, out int deviceNumber)
        {
            deviceNumber = -1;
            IntPtr h = CreateFile(path, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (h == InvalidHandleValue) { return false; }
            try
            {
                int size = Marshal.SizeOf(typeof(STORAGE_DEVICE_NUMBER));
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    uint returned;
                    if (!DeviceIoControl(h, IoctlStorageGetDeviceNumber, IntPtr.Zero, 0, buf, (uint)size, out returned, IntPtr.Zero)) { return false; }
                    STORAGE_DEVICE_NUMBER sdn = (STORAGE_DEVICE_NUMBER)Marshal.PtrToStructure(buf, typeof(STORAGE_DEVICE_NUMBER));
                    deviceNumber = sdn.DeviceNumber;
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            finally
            {
                CloseHandle(h);
            }
        }

        // ------------------------------------------------------------------ P/Invoke

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x1;
        private const uint FileShareWrite = 0x2;
        private const uint OpenExisting = 3;
        private const uint FsctlLockVolume = 0x00090018;
        private const uint FsctlUnlockVolume = 0x0009001C;
        private const uint FsctlDismountVolume = 0x00090020;
        private const uint IoctlStorageGetDeviceNumber = 0x002D1080;
        private const int DigcfPresent = 0x2;
        private const int DigcfDeviceInterface = 0x10;
        private const int ErrorInsufficientBuffer = 122;

        private static readonly Guid GuidDiskInterface = new Guid("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_DEVICE_NUMBER
        {
            public int DeviceType;
            public int DeviceNumber;
            public int PartitionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr device, uint code, IntPtr inBuffer, uint inSize,
            IntPtr outBuffer, uint outSize, out uint returned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid classGuid,
            int memberIndex, ref SP_DEVICE_INTERFACE_DATA ifData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA ifData,
            IntPtr detail, uint detailSize, out uint required, IntPtr devInfo);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA ifData,
            IntPtr detail, uint detailSize, IntPtr required, ref SP_DEVINFO_DATA devInfo);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA devInfo,
            StringBuilder instanceId, uint instanceIdSize, out uint required);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Request_Device_EjectW(uint devInst, out int vetoType,
            StringBuilder vetoName, uint vetoNameLength, uint flags);
    }
}
