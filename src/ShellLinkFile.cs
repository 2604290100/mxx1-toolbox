// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>读 .lnk（Windows 快捷方式）指向哪里 —— 走的是 Shell 的 **IShellLinkW**（Unicode
    /// 那一套 COM 接口），不是 `WScript.Shell`。
    ///
    /// 为什么换掉 WScript.Shell（2026-10-21 由 CI 抓出来）：那个组件内部是 **ANSI** 的，
    /// `CreateShortcut("…\中文 坏链.lnk")` 在**代码页不是中文**的机器上（GitHub 的 windows runner
    /// 就是：OEM 437 / ANSI 1252）根本打不开，抛 "Unable to save shortcut …\?? ??.lnk"。
    /// 结果就是「无效快捷方式清理」在英文 Windows 上把所有中文名的快捷方式都当成"读不出来"——
    /// 安全但清不掉，而且用户看到的原因也不对。IShellLinkW 的 Load()/GetPath() 收的是宽字符
    /// （LPWSTR），中文文件名 / 中文目标都能正常读。
    ///
    /// 这个接口从 Windows 2000 起就有（Win7 / 10 / 11 全都在），不需要任何额外的组件。
    /// 编译时不用引用 COM 程序集：用 ComImport 声明接口 + 从 CLSID 创建对象（和原来反射调
    /// WScript.Shell 一样，只依赖系统里现成的 shell32）。</summary>
    internal static class ShellLinkFile
    {
        /// <summary>CLSID_ShellLink（{00021401-0000-0000-C000-000000000046}）。</summary>
        private static readonly Guid ClsidShellLink = new Guid("00021401-0000-0000-C000-000000000046");

        /// <summary>IShellLinkW。方法顺序 / 签名必须和 shobjidl.h 里的 vtable 完全一致
        /// （少一个、多一个、顺序错了都会读到别的函数上，表现是读出来的目标乱码或直接崩）。</summary>
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        /// <summary>IPersistFile（IPersist 的第一个方法 GetClassID 也要照样写出来）。</summary>
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig]
            int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile(out IntPtr ppszFileName);
        }

        private const uint StgmRead = 0;   // STGM_READ：只读打开，绝不改这个 .lnk

        /// <summary>读一个 .lnk 的三种结果。</summary>
        public enum ReadResult
        {
            /// <summary>读到了文件目标（普通程序 / 文件 / 文件夹）。</summary>
            Ok,
            /// <summary>文件本身能打开，但没有"文件目标"——商店应用（shell:AppsFolder）、
            /// shell: 位置这类，GetPath 什么都不给。调用方该把它算"跳过"，**不能算坏链**。</summary>
            NoFileTarget,
            /// <summary>连快捷方式文件本身都打不开（被截断 / 不是 .lnk）。</summary>
            Unreadable
        }

        /// <summary>这台机器上叫不叫得起 ShellLink 这个 COM 类（读快捷方式全靠它）。</summary>
        public static bool IsAvailable()
        {
            object o = null;
            try
            {
                o = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidShellLink, true));
                return o != null;
            }
            catch { return false; }
            finally { Release(o); }
        }

        /// <summary>读一个 .lnk 的目标 / 参数 / 工作目录。
        /// ⚠️ 只读：`STGM_READ`，一个字节都不写回文件。
        /// ⚠️ 区分「文件坏了」和「它本来就是没有文件目标的商店应用」—— 2026-10-21 换接口时
        /// 一律当成"读不出来"，结果 `store.lnk` 被判成坏链、差点被删（X 组当场抓红）。</summary>
        public static ReadResult Read(string lnkPath, out string target, out string args, out string workDir)
        {
            target = ""; args = ""; workDir = "";
            if (string.IsNullOrEmpty(lnkPath)) { return ReadResult.Unreadable; }
            object com = null;
            try
            {
                if (!System.IO.File.Exists(lnkPath)) { return ReadResult.Unreadable; }
                com = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidShellLink, true));
                if (com == null) { return ReadResult.Unreadable; }
                IShellLinkW link = (IShellLinkW)com;
                IPersistFile persist = (IPersistFile)link;
                persist.Load(lnkPath, StgmRead);   // 打不开 = 文件本身坏了

                StringBuilder buf = new StringBuilder(1024);
                try { link.GetPath(buf, buf.Capacity, IntPtr.Zero, 0); }
                catch { buf.Length = 0; }          // 没有文件目标（商店应用）时这里会失败，不算坏文件
                target = buf.ToString();
                try
                {
                    buf.Length = 0;
                    link.GetArguments(buf, buf.Capacity);
                    args = buf.ToString();
                    buf.Length = 0;
                    link.GetWorkingDirectory(buf, buf.Capacity);
                    workDir = buf.ToString();
                }
                catch { }
                return target.Length > 0 ? ReadResult.Ok : ReadResult.NoFileTarget;
            }
            catch { return ReadResult.Unreadable; }
            finally { Release(com); }
        }

        private static void Release(object com)
        {
            if (com == null) { return; }
            try { Marshal.FinalReleaseComObject(com); } catch { }
        }
    }
}
