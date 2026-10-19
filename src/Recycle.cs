// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>「删到回收站」的唯一实现（无效快捷方式清理 / 空文件夹清理 / 重复文件查找都用它）。
    ///
    /// 为什么不用 `File.Delete`：那是**永久删除**，本项目所有会删用户文件的功能都只删到回收站
    /// （删错了还能从回收站捞回来，这是这几件功能敢自动勾选的底气）。
    /// 为什么不用 `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin)`：
    /// 那要给 csc 挂一个额外的程序集引用，而 `SHFileOperation` 是 shell32 里现成的（Windows 2000 起就在），
    /// 一份 P/Invoke 就够，不欠依赖。
    ///
    /// 三条口径：
    /// ① **没有确认框、没有错误弹窗**（FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI）——
    ///    确认由调用方自己的窗口负责（那些窗口本来就都过一遍《使用条款》+ 确认框）；
    /// ② **失败按文件归因**：SHFileOperation 按批返回一个总错误码，说不清是哪个文件。
    ///    所以删完以后**逐个复查还在不在**，"还在的"就是失败的（比解析错误码靠谱）；
    /// ③ **网络位置 / 插着没认出来的盘**：SHFileOperation 对它们其实是永久删除（回收站管不到），
    ///    所以那两个组**根本不往这里送**（调用方过滤，见 Shortcuts.Kind）。
    ///
    /// `SHFileOperationW` 这个拼写要写全（EntryPoint + ExactSpelling）：不写的话运行时会去找
    /// 带 W 后缀的名字（`SHFileOperationWW`），找不到就抛 EntryPointNotFoundException ——
    /// 和 `Balloon.FindWindowW` 踩的是同一个坑（见那边的注释）。</summary>
    internal static class Recycle
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;
        private const ushort FOF_NOCONFIRMMKDIR = 0x0200;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW", ExactSpelling = true)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

        /// <summary>一批删除的结果：Deleted 是"确实不在原位置了"的个数；
        /// Failed 是删完还留在原地的（每项 = 路径 + 人话原因）；
        /// ShellCode 是 shell 返回的总错误码（0 = 完全成功）。</summary>
        public sealed class DeleteResult
        {
            public int Deleted = 0;
            public int ShellCode = 0;
            public bool Aborted = false;
            public readonly List<string> Failed = new List<string>();
            public readonly List<string> FailedWhy = new List<string>();

            public string ReasonOf(int index)
            {
                if (index < 0 || index >= FailedWhy.Count) { return ""; }
                return FailedWhy[index];
            }
        }

        /// <summary>把一批路径（文件或文件夹，绝对路径）删到回收站。
        /// **一批一次调用**：回收站里它们会是一组，速度也快（逐个调用在删除几百个文件时很慢）。
        /// 调用完逐个复查：还在的 = 没删成（原因写清楚，绝不谎报"已删除"）。</summary>
        public static DeleteResult DeleteToRecycleBin(IList<string> paths)
        {
            DeleteResult r = new DeleteResult();
            if (paths == null || paths.Count == 0) { return r; }

            List<string> list = new List<string>();
            foreach (string p in paths)
            {
                if (p != null && p.Trim().Length > 0) { list.Add(p); }
            }
            if (list.Count == 0) { return r; }

            StringBuilder sb = new StringBuilder();
            foreach (string p in list) { sb.Append(p).Append('\0'); }
            sb.Append('\0');   // SHFileOperation 要的是"双空结尾"

            SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
            op.wFunc = FO_DELETE;
            op.pFrom = sb.ToString();
            op.pTo = null;
            op.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_NOCONFIRMMKDIR);
            try
            {
                r.ShellCode = SHFileOperationW(ref op);
                r.Aborted = op.fAnyOperationsAborted;
            }
            catch (Exception ex)
            {
                r.ShellCode = -1;
                foreach (string p in list) { r.Failed.Add(p); r.FailedWhy.Add("调用系统删除接口失败：" + ex.Message); }
                return r;
            }

            foreach (string p in list)
            {
                if (PathExists(p))
                {
                    r.Failed.Add(p);
                    r.FailedWhy.Add(r.ShellCode == 0
                        ? "系统说删掉了，但复查时它还在（多半是被别的程序占着）"
                        : ("系统删除接口返回错误码 " + r.ShellCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                           + "（0 以外都不是成功）"));
                }
                else
                {
                    r.Deleted++;
                }
            }
            return r;
        }

        /// <summary>删除时用"还在不在"当判据 —— 文件或文件夹任一存在都算还在。</summary>
        private static bool PathExists(string path)
        {
            try { return System.IO.File.Exists(path) || System.IO.Directory.Exists(path); }
            catch { return true; }   // 读不出来当"还在"（宁可说没删成，不谎报成功）
        }
    }
}
