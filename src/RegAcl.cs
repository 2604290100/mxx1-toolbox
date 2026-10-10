// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace Mxx1Toolbox
{
    /// <summary>注册表权限那一层：判断"这个键能不能写"、以及**取得所有权**（给「Windows 保护」的菜单项用）。
    ///
    /// ## 为什么需要它（2026-10-10 用户报「为什么管理员权限启动也无法禁止？」）
    ///
    /// 他点「禁用」时记录里写着 `失败：不允许所请求的注册表访问权。` —— 量下来是这些键
    /// **所有者是 `NT SERVICE\TrustedInstaller`，`BUILTIN\Administrators` 只有 `ReadKey`**：
    ///
    /// ```
    /// HKLM\SOFTWARE\Classes\Drive\shell\cmd
    ///   Owner: NT SERVICE\TrustedInstaller
    ///   BUILTIN\Administrators      ReadKey            ← 管理员只能读
    ///   NT SERVICE\TrustedInstaller Delete + FullControl
    /// ```
    ///
    /// 这就是 Windows 保护自己那批菜单项的方式（「在此处打开命令窗口 / Powershell」这类按住 Shift
    /// 才出现的项），**跟提不提权无关** —— 所以"弹 UAC 再来一次"对它们没有用。
    ///
    /// ## 取得所有权这条路（用户拍板要的"取得所有权并禁用"）
    ///
    /// 两步，而且每一步都有前提：
    /// ① **改所有者** → 需要 `SeTakeOwnershipPrivilege`。⚠️ 管理员**默认持有**这个特权，但它
    ///    在进程令牌里是**禁用**状态，必须先 `AdjustTokenPrivileges` 打开（`EnablePrivilege`）；
    /// ② **该回原样的所有者**（比如还给 TrustedInstaller）→ 需要 `SeRestorePrivilege`，同样要先打开。
    ///
    /// 所以这里**先把原样的安全描述符（SDDL，含所有者 + DACL）存成一份文件**，还原就是把它写回去 ——
    /// 用户界面上那个「还原权限」按钮用的就是它。**没有这份 SDDL 就不许改**（宁可不做，也不留下
    /// 一个"权限被改过、还不知道原来是什么样"的系统键）。
    ///
    /// ⚠️ 只对**单个键**动手，只加一条"管理员完全控制"，不删任何原有 ACE（`AddAccessRule`），
    /// 这样即使还原失败，原有权也还在。</summary>
    internal static class RegAcl
    {
        // ---- 特权 ----
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public uint Count; public LUID_AND_ATTRIBUTES Privilege; }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string system, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
            ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);

        /// <summary>在当前进程里打开一个特权（管理员默认持有，但默认是关着的）。
        /// 已经在管理员上下文里才有意义；失败不抛异常，调用方按"拿不到就走不通"处理。</summary>
        public static bool EnablePrivilege(string name)
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                {
                    return false;
                }
                LUID luid;
                if (!LookupPrivilegeValue(null, name, out luid)) { return false; }
                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                tp.Count = 1;
                tp.Privilege.Luid = luid;
                tp.Privilege.Attributes = SE_PRIVILEGE_ENABLED;
                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) { return false; }
                // AdjustTokenPrivileges 只把错误放在 GetLastError 里（返回 true 也可能没成功）
                return (Marshal.GetLastWin32Error() == 0);
            }
            catch { return false; }
            finally { if (token != IntPtr.Zero) { try { CloseHandle(token); } catch { } } }
        }

        // ---- 键路径 ----
        private const string HiveCurrentUser = "HKEY_CURRENT_USER";
        private const string HiveLocalMachine = "HKEY_LOCAL_MACHINE";

        private static RegistryKey BaseOf(string full, out string sub)
        {
            string s = (full == null) ? "" : full.Trim();
            if (s.StartsWith(HiveLocalMachine + "\\", StringComparison.OrdinalIgnoreCase))
            {
                sub = s.Substring(HiveLocalMachine.Length + 1);
                return Registry.LocalMachine;
            }
            if (s.StartsWith(HiveCurrentUser + "\\", StringComparison.OrdinalIgnoreCase))
            {
                sub = s.Substring(HiveCurrentUser.Length + 1);
                return Registry.CurrentUser;
            }
            sub = s.TrimStart('\\');
            return Registry.CurrentUser;
        }

        private static NTAccount Administrators()
        {
            // 用 SID 而不是名字：中文系统上叫 "Administrators"，别的语言又不一样
            SecurityIdentifier sid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            return (NTAccount)sid.Translate(typeof(NTAccount));
        }

        /// <summary>现在能不能写这个键（**只读判断**：试着以可写方式打开一次，不写任何东西）。
        /// 界面用它把"改不动的项"标出来 —— 用户 2026-10-10 提的"点了才发现没反应"就是这么修的。</summary>
        public static bool CanWrite(string full)
        {
            try
            {
                string sub;
                RegistryKey baseKey = BaseOf(full, out sub);
                using (RegistryKey probe = baseKey.OpenSubKey(sub, false))
                {
                    if (probe == null) { return false; }        // 键不存在，谈不上能写
                }
                using (RegistryKey w = baseKey.OpenSubKey(sub, true))
                {
                    return (w != null);
                }
            }
            catch { return false; }
        }

        /// <summary>取一份原样的安全描述符（SDDL：所有者 + DACL + SACL）。**取得所有权之前必须先存这个** ——
        /// 没有它就不许改权限（界面上「还原权限」靠它把键还回原样）。</summary>
        public static string ReadSddl(string full)
        {
            try
            {
                string sub;
                RegistryKey baseKey = BaseOf(full, out sub);
                using (RegistryKey k = baseKey.OpenSubKey(sub, RegistryKeyPermissionCheck.ReadWriteSubTree,
                           RegistryRights.ReadPermissions))
                {
                    if (k == null) { return ""; }
                    RegistrySecurity sec = k.GetAccessControl(AccessControlSections.All);
                    return sec.GetSecurityDescriptorSddlForm(AccessControlSections.All);
                }
            }
            catch { return ""; }
        }

        /// <summary>取得所有权：把所有者改成内置管理员组，并**追加**一条"管理员完全控制"。
        /// ⚠️ 会先打开 `SeTakeOwnershipPrivilege`；只加 ACE、不删原有 ACE。</summary>
        public static string TakeOwn(string full, out bool ok)
        {
            ok = false;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("取得所有权：" + full);
            if (!EnablePrivilege("SeTakeOwnershipPrivilege"))
            {
                sb.AppendLine("结果：没成功 —— 拿不到「取得所有权」特权 SeTakeOwnershipPrivilege");
                sb.AppendLine("（这个特权只有管理员有，而且默认是关着的；请确认这个窗口是**以管理员身份**重开过的。）");
                return sb.ToString();
            }
            sb.AppendLine("· 已启用特权 SeTakeOwnershipPrivilege");
            try
            {
                string sub;
                RegistryKey baseKey = BaseOf(full, out sub);

                // ① 所有者改成管理员组
                using (RegistryKey k = baseKey.OpenSubKey(sub, RegistryKeyPermissionCheck.ReadWriteSubTree,
                           RegistryRights.TakeOwnership))
                {
                    if (k == null)
                    {
                        sb.AppendLine("结果：没成功 —— 打不开这个键（可能已经被删了）");
                        return sb.ToString();
                    }
                    RegistrySecurity sec = k.GetAccessControl(AccessControlSections.Owner);
                    sec.SetOwner(Administrators());
                    k.SetAccessControl(sec);
                }
                sb.AppendLine("· 所有者已改成 BUILTIN\\Administrators");

                // ② 追加一条"管理员完全控制"（不删任何原有 ACE）
                using (RegistryKey k = baseKey.OpenSubKey(sub, RegistryKeyPermissionCheck.ReadWriteSubTree,
                           RegistryRights.ChangePermissions))
                {
                    if (k == null)
                    {
                        sb.AppendLine("结果：没成功 —— 换了所有者之后仍然打不开（ChangePermissions）");
                        return sb.ToString();
                    }
                    RegistrySecurity sec = k.GetAccessControl(AccessControlSections.Access);
                    sec.AddAccessRule(new RegistryAccessRule(Administrators(), RegistryRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None, AccessControlType.Allow));
                    k.SetAccessControl(sec);
                }
                sb.AppendLine("· 已追加一条「Administrators：完全控制」（原有的权限一条都没删）");

                // ③ 读回核对：现在真的能写了吗
                if (!CanWrite(full))
                {
                    sb.AppendLine("结果：没成功 —— 改完权限还是不能可写打开");
                    return sb.ToString();
                }
                sb.AppendLine("结果：已取得所有权（现在这个键可以写了）。");
                ok = true;
            }
            catch (Exception ex)
            {
                sb.AppendLine("结果：失败 —— " + ex.Message);
            }
            return sb.ToString();
        }

        /// <summary>把安全描述符写回原样（界面上的「还原权限」）。要给回 TrustedInstaller 这样的所有者，
        /// 需要 `SeRestorePrivilege`（管理员也有、也是默认关着）。</summary>
        public static string RestoreSddl(string full, string sddl, out bool ok)
        {
            ok = false;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("还原权限：" + full);
            if (sddl == null || sddl.Length == 0)
            {
                sb.AppendLine("结果：没做 —— 没有存下这份原样的安全描述符（SDDL），不敢瞎改。");
                return sb.ToString();
            }
            if (!EnablePrivilege("SeRestorePrivilege"))
            {
                sb.AppendLine("结果：没成功 —— 拿不到「还原」特权 SeRestorePrivilege（要管理员，而且它默认关着）。");
                return sb.ToString();
            }
            try
            {
                string sub;
                RegistryKey baseKey = BaseOf(full, out sub);
                using (RegistryKey k = baseKey.OpenSubKey(sub, RegistryKeyPermissionCheck.ReadWriteSubTree,
                           RegistryRights.TakeOwnership | RegistryRights.ChangePermissions))
                {
                    if (k == null)
                    {
                        sb.AppendLine("结果：没成功 —— 打不开这个键");
                        return sb.ToString();
                    }
                    RegistrySecurity sec = new RegistrySecurity();
                    sec.SetSecurityDescriptorSddlForm(sddl);
                    k.SetAccessControl(sec);
                }
                using (RegistryKey check = baseKey.OpenSubKey(sub, RegistryKeyPermissionCheck.ReadWriteSubTree,
                           RegistryRights.ReadPermissions))
                {
                    string now = (check == null) ? "" : check.GetAccessControl(AccessControlSections.All)
                        .GetSecurityDescriptorSddlForm(AccessControlSections.All);
                    if (!string.Equals(now, sddl, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine("结果：写了但读回和存下的不一样（可能被系统改回去了）。");
                        return sb.ToString();
                    }
                }
                sb.AppendLine("结果：已还原成原来的所有者和权限。");
                ok = true;
            }
            catch (Exception ex)
            {
                sb.AppendLine("结果：失败 —— " + ex.Message);
            }
            return sb.ToString();
        }
    }
}
