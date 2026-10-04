// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>The few Win32 calls the toolbox needs (dark title bar, dark scrollbars,
    /// console attach for the CLI mode). All of them are best effort.</summary>
    internal static class Native
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        public static void ApplyDarkTitleBar(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) { return; }
            try
            {
                int value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref value, sizeof(int));
                }
            }
            catch { }
        }

        /// <summary>Gives a child control (text box, list view) dark scrollbars on Windows 10/11.</summary>
        public static void ApplyDarkControl(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) { return; }
            try { SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null); }
            catch { }
        }

        private const int STD_OUTPUT_HANDLE = -11;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        /// <summary>Lets the GUI-subsystem exe print its CLI output into the calling console.
        /// Called ONLY when there is no usable stdout handle: when the caller redirected or piped
        /// our output, attaching to its console would steal the output away from that pipe
        /// (which made `Mxx1Toolbox.exe status` print nothing into a PowerShell pipeline).</summary>
        public static void AttachParentConsole()
        {
            try
            {
                IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle != IntPtr.Zero && handle != new IntPtr(-1)) { return; }
                if (AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    StreamWriter w = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                    w.AutoFlush = true;
                    Console.SetOut(w);
                }
            }
            catch { }
        }
    }
}
