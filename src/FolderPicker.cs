// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>选文件夹的窗口（2026-10-23 用户提的：「点击浏览的时候弹出的系统窗口只能一个个选择
    /// 文件夹，要改成那个可以直接复制路径到输入框后回车直接定位文件夹的」）。
    ///
    /// 用 Windows 自带的 `IFileOpenDialog` + `FOS_PICKFOLDERS`（Vista / Win7 起就有）：
    /// 它上面有地址栏和「文件夹」输入框 —— **把路径粘进去、回车就直接跳过去**；
    /// 老的 `FolderBrowserDialog` 只有一棵树，只能一层层点。
    ///
    /// 万一在这台系统上建不出来（COM 组件被精简掉 / 调用失败），退回老对话框：
    /// 只是退回"只能点"的老样子，不会崩、也不会弹一堆错误框。</summary>
    internal static class FolderPicker
    {
        /// <summary>选文件夹；取消返回 null（和 FolderBrowserDialog 的语义一致）。</summary>
        public static string Pick(IWin32Window owner, string title, string initial)
        {
            try
            {
                bool supported;
                string path = PickModern(owner, title, initial, out supported);
                if (supported) { return path; }
            }
            catch { }
            return PickLegacy(owner, title, initial);
        }

        private static string PickLegacy(IWin32Window owner, string title, string initial)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = title;
                dlg.ShowNewFolderButton = false;
                if (initial != null && initial.Trim().Length > 0 && System.IO.Directory.Exists(initial.Trim()))
                {
                    dlg.SelectedPath = initial.Trim();
                }
                return (dlg.ShowDialog(owner) == DialogResult.OK) ? dlg.SelectedPath : null;
            }
        }

        // ------------------------------------------------------------------ 现代的选文件夹窗口

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(IntPtr ppenum);
            void GetSelectedItems(IntPtr ppsai);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        /// <summary>supported=false = 这台系统上用不了新对话框，调用方退回老的那套。</summary>
        private static string PickModern(IWin32Window owner, string title, string initial, out bool supported)
        {
            supported = false;
            object com = null;
            IFileOpenDialog dlg = null;
            IShellItem result = null;
            try
            {
                Type type = Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7"));
                if (type == null) { return null; }
                com = Activator.CreateInstance(type);
                dlg = (IFileOpenDialog)com;
                if (dlg == null) { return null; }

                uint opts;
                dlg.GetOptions(out opts);
                dlg.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                if (title != null && title.Length > 0) { dlg.SetTitle(title); }
                dlg.SetOkButtonLabel("选这个文件夹");

                if (initial != null && initial.Trim().Length > 0 && System.IO.Directory.Exists(initial.Trim()))
                {
                    object item = null;
                    try
                    {
                        Guid iid = typeof(IShellItem).GUID;
                        SHCreateItemFromParsingName(initial.Trim(), IntPtr.Zero, ref iid, out item);
                        IShellItem start = item as IShellItem;
                        if (start != null) { dlg.SetFolder(start); }
                    }
                    catch { }
                    finally
                    {
                        if (item != null) { try { Marshal.ReleaseComObject(item); } catch { } }
                    }
                }

                supported = true;
                IntPtr parent = IntPtr.Zero;
                if (owner != null) { parent = owner.Handle; }
                int hr = dlg.Show(parent);
                if (hr != 0) { return null; }

                dlg.GetResult(out result);
                if (result == null) { return null; }

                IntPtr psz = IntPtr.Zero;
                try
                {
                    result.GetDisplayName(SIGDN_FILESYSPATH, out psz);
                    if (psz == IntPtr.Zero) { return null; }
                    return Marshal.PtrToStringUni(psz);
                }
                finally
                {
                    if (psz != IntPtr.Zero) { Marshal.FreeCoTaskMem(psz); }
                }
            }
            finally
            {
                if (result != null) { try { Marshal.ReleaseComObject(result); } catch { } }
                if (dlg != null) { try { Marshal.ReleaseComObject(dlg); } catch { } }
                else if (com != null) { try { Marshal.ReleaseComObject(com); } catch { } }
            }
        }
    }
}
