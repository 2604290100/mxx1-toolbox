// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>一个文件 / 文件夹的「所有者」信息（2026-10-28 用户提的第 4 件事：文件 / 文件夹取得所有权）。
    ///
    /// 用户报的场景：删不掉、改不了、复制不进去，右键看权限是灰的 —— 那是**所有者不是自己**、
    /// 而且自己连「改权限」的资格都没有（典型：重装系统之后老系统留下的文件夹、别的电脑拷来的
    /// 移动硬盘、某些安装程序建的目录）。注册表那边「右键菜单管理」早就有「取得所有权」了
    /// （见 `src\RegAcl.cs`），这一页把它补到**文件和文件夹**上。
    ///
    /// 做的事（和 `takeown /f <路径> /a` 同一个道理，只是带记录、能还原）：
    /// ① 打开 `SeTakeOwnershipPrivilege`（管理员默认持有，但默认是关着的）；
    /// ② 把**所有者**改成当前用户；
    /// ③ 给当前用户追加一条「完全控制」（文件夹带继承，子项也跟着能改）；
    /// ④ 读回核对：所有者真的是自己、DACL 里真的有这条。
    ///
    /// 四条口径（同「此电脑多余图标」）：
    /// ① 扫描只读（只读所有者 + 访问规则，不碰 SACL —— 读 SACL 要另一个特权）；
    /// ② 动手前把**原样的安全描述符（SDDL：所有者 + DACL）**记进 TSV —— 记不下来就一个都不动；
    /// ③ 写完逐个读回核对；**只追加、不删任何原有 ACE**（就算还原失败，原来的权限也还在）；
    /// ④ 命令行 `takeown` 默认只读；写入口只在隔离根 `MXX1_TAKEOWN_ROOT` 设好、而且**目标必须
    ///    在这个根里面**时才放行（用户自己那台机器只能在窗口里点）。
    ///
    /// 测试隔离：`MXX1_TAKEOWN_ROOT`（一个目录：既当"允许写"的开关，也当"不许写到外面"的边界）、
    /// `MXX1_TAKEOWN_TSV`（记录文件挪走）。</summary>
    internal sealed class FileOwnItem
    {
        public string Path = "";
        public bool IsDir;
        public string Owner = "";
        public string OwnerSid = "";
        public bool CanRead = true;
        public bool IsSelf;
        public string Note = "";

        public string KindText() { return IsDir ? "文件夹" : "文件"; }
    }

    internal sealed class TakeOwnReport
    {
        public bool Ok = true;
        public int Changed;
        public int Skipped;
        public int Failed;
        public bool NeedAdmin;
        public string Message = "";
    }

    internal static class FileTakeOwn
    {
        private const string TsvHeader = "# mxx1-toolbox 文件取得所有权 原值记录（制表符分隔；路径 / SDDL 做了 base64，别手改）";

        // ------------------------------------------------------------------ 隔离根

        public static string IsoRoot()
        {
            return (Environment.GetEnvironmentVariable("MXX1_TAKEOWN_ROOT") ?? "").Trim().Trim('"').TrimEnd('\\');
        }

        public static bool Isolated { get { return IsoRoot().Length > 0; } }

        public static bool WriteAllowed() { return Isolated; }

        /// <summary>目标必须落在隔离根里面（防止命令行把别的地方改了）。</summary>
        public static bool InsideRoot(string path)
        {
            string root = IsoRoot();
            if (root.Length == 0) { return false; }
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\');
                string r = Path.GetFullPath(root).TrimEnd('\\');
                return full.Equals(r, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static string RecordFile()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_TAKEOWN_TSV") ?? "").Trim();
            if (env.Length > 0) { return env; }
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } } catch { }
            return Path.Combine(dir, "takeown-original.tsv");
        }

        // ------------------------------------------------------------------ 扫描（只读）

        public static FileOwnItem Probe(string path)
        {
            FileOwnItem it = new FileOwnItem();
            it.Path = path;
            it.IsDir = Directory.Exists(path);
            if (!it.IsDir && !File.Exists(path))
            {
                it.CanRead = false;
                it.Note = "看不见这个路径（不存在，或者连看都不让看）";
                return it;
            }
            try
            {
                FileSystemSecurity sec = ReadSecurity(path, it.IsDir);
                if (sec != null)
                {
                    IdentityReference owner = sec.GetOwner(typeof(SecurityIdentifier));
                    if (owner != null)
                    {
                        it.OwnerSid = owner.Value;
                        it.Owner = Translate(owner);
                    }
                }
            }
            catch (Exception ex)
            {
                it.CanRead = false;
                it.Note = "读不到所有者：" + ex.Message;
            }
            it.IsSelf = IsMine(it.OwnerSid);
            return it;
        }
        /// <summary>「这一项归你管吗」：所有者是你的用户 SID，**或者你所在的某个组**。
        /// ⚠️ 2026-10-29 量出来的：**管理员权限下新建的文件，所有者是 `BUILTIN\Administrators`
        /// 而不是你的用户**（令牌的默认所有者就是那个组）—— 只拿"用户 SID 相等"当判据会把
        /// 自己刚建的文件全标成"不是你"，纯属吓人。`TrustedInstaller` / `SYSTEM` 那些仍然不是。</summary>
        public static bool IsMine(string ownerSid)
        {
            if (string.IsNullOrEmpty(ownerSid)) { return false; }
            SecurityIdentifier me = CurrentUser();
            if (me != null && string.Equals(ownerSid, me.Value, StringComparison.OrdinalIgnoreCase)) { return true; }
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                foreach (IdentityReference g in id.Groups)
                {
                    SecurityIdentifier gs = g as SecurityIdentifier;
                    if (gs != null && string.Equals(ownerSid, gs.Value, StringComparison.OrdinalIgnoreCase)) { return true; }
                }
            }
            catch { }
            return false;
        }

        /// <summary>列一批路径（文件夹会**递归**展开，带 2 万个的护栏）。</summary>
        public static List<FileOwnItem> Scan(List<string> paths, bool recursive, ref bool truncated, int maxItems)
        {
            List<FileOwnItem> list = new List<FileOwnItem>();
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p)) { continue; }
                if (list.Count >= maxItems) { truncated = true; break; }
                FileOwnItem root = Probe(p);
                list.Add(root);
                if (!root.IsDir || !recursive) { continue; }
                try
                {
                    foreach (string f in Directory.EnumerateFileSystemEntries(p, "*", SearchOption.AllDirectories))
                    {
                        if (list.Count >= maxItems) { truncated = true; break; }
                        FileOwnItem it = new FileOwnItem();
                        it.Path = f;
                        it.IsDir = Directory.Exists(f);
                        list.Add(it);
                    }
                }
                catch (Exception ex)
                {
                    root.Note = "这个文件夹下面有几层读不完：" + ex.Message;
                }
            }
            return list;
        }

        private static FileSystemSecurity ReadSecurity(string path, bool isDir)
        {
            try
            {
                // ⚠️ 只读 Owner + Access 两段：**不读 SACL**（那要 SeSecurityPrivilege，
                // 普通管理员默认没开，读它会直接抛异常 —— 那就连所有者都显示不出来了）。
                if (isDir)
                {
                    DirectorySecurity d = Directory.GetAccessControl(path,
                        AccessControlSections.Owner | AccessControlSections.Access);
                    return d;
                }
                FileSecurity f = File.GetAccessControl(path,
                    AccessControlSections.Owner | AccessControlSections.Access);
                return f;
            }
            catch { throw; }
        }

        private static string Translate(IdentityReference id)
        {
            try { return id.Translate(typeof(NTAccount)).ToString(); }
            catch { return id.Value; }
        }

        public static SecurityIdentifier CurrentUser()
        {
            try { return WindowsIdentity.GetCurrent().User; }
            catch { return null; }
        }

        public static string CurrentUserName()
        {
            try { return WindowsIdentity.GetCurrent().Name; }
            catch { return ""; }
        }

        // ------------------------------------------------------------------ 取得所有权（写）

        /// <summary>对一批路径取得所有权（可选递归）。每一项都先记 SDDL，记不下来就一个都不动。</summary>
        public static TakeOwnReport TakeOwn(List<string> paths, bool recursive)
        {
            TakeOwnReport rep = new TakeOwnReport();
            if (paths == null || paths.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有选中要处理的文件 / 文件夹。";
                return rep;
            }
            bool truncated = false;
            List<FileOwnItem> items = Scan(paths, recursive, ref truncated, 20000);
            return TakeOwnItems(items);
        }

        public static TakeOwnReport TakeOwnItems(List<FileOwnItem> items)
        {
            TakeOwnReport rep = new TakeOwnReport();
            if (items == null || items.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有选中要处理的文件 / 文件夹。";
                return rep;
            }
            SecurityIdentifier me = CurrentUser();
            if (me == null)
            {
                rep.Ok = false;
                rep.Message = "读不到当前用户（WindowsIdentity 拿不到），为了不瞎改权限，这次什么都没动。";
                return rep;
            }

            // ① 先记原值：每一项的 SDDL（所有者 + DACL）
            List<string> lines = new List<string>();
            List<FileOwnItem> todo = new List<FileOwnItem>();
            StringBuilder note = new StringBuilder();
            foreach (FileOwnItem it in items)
            {
                string sddl;
                if (!TryReadSddl(it.Path, it.IsDir, out sddl))
                {
                    rep.Skipped++;
                    note.Append("　· ").Append(it.Path).AppendLine("（读不到权限，跳过）");
                    continue;
                }
                lines.Add(Now() + "\t" + B64(it.Path) + "\t" + (it.IsDir ? "1" : "0") + "\t" + B64(sddl));
                todo.Add(it);
            }
            if (lines.Count > 0)
            {
                try { AppendRecords(lines); }
                catch (Exception ex)
                {
                    rep.Ok = false;
                    rep.Message = "原值记不下来（" + RecordFile() + "）：" + ex.Message
                        + " —— 为了能还原，这次一个都没动。";
                    rep.Failed = items.Count;
                    return rep;
                }
            }

            // ② 逐个取得所有权 + 追加完全控制 + 读回核对
            if (!RegAcl.EnablePrivilege("SeTakeOwnershipPrivilege"))
            {
                rep.Ok = false;
                rep.NeedAdmin = true;
                rep.Message = "拿不到「取得所有权」特权（SeTakeOwnershipPrivilege）—— 这个动作要管理员。"
                    + "请用管理员身份重开这个窗口再点一次。";
                rep.Failed = todo.Count;
                return rep;
            }

            StringBuilder bad = new StringBuilder();
            foreach (FileOwnItem it in todo)
            {
                string err = TakeOwnOne(it.Path, it.IsDir, me);
                if (err.Length == 0) { rep.Changed++; }
                else
                {
                    rep.Failed++;
                    if (err.IndexOf("权限", StringComparison.Ordinal) >= 0
                        || err.IndexOf("拒绝", StringComparison.Ordinal) >= 0) { rep.NeedAdmin = true; }
                    bad.Append("　· ").Append(it.Path).Append("（").Append(err).AppendLine("）");
                }
            }

            rep.Ok = rep.Failed == 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("取得所有权 ").Append(rep.Changed.ToString(CultureInfo.InvariantCulture)).Append(" 项");
            if (rep.Skipped > 0) { sb.Append("，跳过 ").Append(rep.Skipped.ToString(CultureInfo.InvariantCulture)).Append(" 项"); }
            if (rep.Failed > 0) { sb.Append("，失败 ").Append(rep.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 项"); }
            sb.AppendLine("。每一项都读回来核对过（所有者是自己、DACL 里有完全控制）。");
            if (note.Length > 0) { sb.Append(note.ToString()); }
            if (bad.Length > 0) { sb.Append(bad.ToString()); }
            sb.Append("原值记在：").Append(RecordFile()).AppendLine("（点「还原成取得前」可以把所有者和权限还回去）");
            rep.Message = sb.ToString();
            return rep;
        }

        /// <summary>单项：改所有者 → 追加完全控制 → 读回核对。成功返回空串，失败返回原因。</summary>
        private static string TakeOwnOne(string path, bool isDir, SecurityIdentifier me)
        {
            try
            {
                if (isDir)
                {
                    DirectorySecurity sec = Directory.GetAccessControl(path, AccessControlSections.Owner);
                    sec.SetOwner(me);
                    Directory.SetAccessControl(path, sec);

                    DirectorySecurity sec2 = Directory.GetAccessControl(path, AccessControlSections.Access);
                    sec2.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None, AccessControlType.Allow));
                    Directory.SetAccessControl(path, sec2);
                }
                else
                {
                    FileSecurity sec = File.GetAccessControl(path, AccessControlSections.Owner);
                    sec.SetOwner(me);
                    File.SetAccessControl(path, sec);

                    FileSecurity sec2 = File.GetAccessControl(path, AccessControlSections.Access);
                    sec2.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl,
                        AccessControlType.Allow));
                    File.SetAccessControl(path, sec2);
                }

                // 读回核对
                FileSystemSecurity check = ReadSecurity(path, isDir);
                IdentityReference owner = check.GetOwner(typeof(SecurityIdentifier));
                if (owner == null || !string.Equals(owner.Value, me.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return "改完读回来所有者还不是自己";
                }
                bool found = false;
                foreach (FileSystemAccessRule r in check.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                {
                    if (!string.Equals(r.IdentityReference.Value, me.Value, StringComparison.OrdinalIgnoreCase)) { continue; }
                    if (r.AccessControlType != AccessControlType.Allow) { continue; }
                    if ((r.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl) { found = true; break; }
                }
                return found ? "" : "改完读回来 DACL 里没有「完全控制」那一条";
            }
            catch (UnauthorizedAccessException ex) { return "权限不够 —— " + ex.Message; }
            catch (Exception ex) { return ex.Message; }
        }

        private static bool TryReadSddl(string path, bool isDir, out string sddl)
        {
            sddl = "";
            try
            {
                FileSystemSecurity sec = ReadSecurity(path, isDir);
                if (sec == null) { return false; }
                sddl = sec.GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
                return sddl.Length > 0;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ 还原

        public static TakeOwnReport Restore()
        {
            TakeOwnReport rep = new TakeOwnReport();
            List<string[]> rows = ReadRecords();
            if (rows.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有待还原的记录（这台机器还没用这个窗口取得过所有权）。";
                return rep;
            }
            bool priv = RegAcl.EnablePrivilege("SeRestorePrivilege");
            StringBuilder bad = new StringBuilder();
            List<string> keep = new List<string>();
            foreach (string[] r in rows)
            {
                if (r.Length < 4) { continue; }
                string path = From64(r[1]);
                bool isDir = (r[2] == "1");
                string sddl = From64(r[3]);
                if (path.Length == 0 || sddl.Length == 0) { continue; }
                if (!Directory.Exists(path) && !File.Exists(path))
                {
                    rep.Skipped++;
                    keep.Add(Join(r));
                    bad.Append("　· ").Append(path).AppendLine("（已经不在了，记录留着）");
                    continue;
                }
                if (!priv)
                {
                    rep.NeedAdmin = true;
                    rep.Failed++;
                    keep.Add(Join(r));
                    bad.Append("　· ").Append(path).AppendLine("（拿不到 SeRestorePrivilege，要管理员）");
                    continue;
                }
                try
                {
                    if (isDir)
                    {
                        DirectorySecurity sec = new DirectorySecurity();
                        sec.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Owner | AccessControlSections.Access);
                        Directory.SetAccessControl(path, sec);
                    }
                    else
                    {
                        FileSecurity sec = new FileSecurity();
                        sec.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Owner | AccessControlSections.Access);
                        File.SetAccessControl(path, sec);
                    }
                    FileSystemSecurity check = ReadSecurity(path, isDir);
                    IdentityReference now = check.GetOwner(typeof(SecurityIdentifier));
                    string want = OwnerSidOf(sddl);
                    if (now != null && want.Length > 0
                        && string.Equals(now.Value, want, StringComparison.OrdinalIgnoreCase))
                    {
                        rep.Changed++;
                    }
                    else
                    {
                        rep.Failed++;
                        keep.Add(Join(r));
                        bad.Append("　· ").Append(path).AppendLine("（写回去了，但所有者读回来和存下的不一样）");
                    }
                }
                catch (Exception ex)
                {
                    rep.Failed++;
                    keep.Add(Join(r));
                    if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException) { rep.NeedAdmin = true; }
                    bad.Append("　· ").Append(path).Append("（写不进去：").Append(ex.Message).AppendLine("）");
                }
            }
            WriteRecords(keep);
            rep.Ok = rep.Failed == 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("还原 ").Append(rep.Changed.ToString(CultureInfo.InvariantCulture)).Append(" 项");
            if (rep.Skipped > 0) { sb.Append("，跳过 ").Append(rep.Skipped.ToString(CultureInfo.InvariantCulture)).Append(" 项"); }
            if (rep.Failed > 0) { sb.Append("，失败 ").Append(rep.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 项"); }
            sb.Append("。");
            if (bad.Length > 0) { sb.AppendLine().Append(bad.ToString()); }
            rep.Message = sb.ToString();
            return rep;
        }

        /// <summary>从 SDDL 里抠出所有者 SID。
        /// ⚠️ 2026-10-29 踩的坑：SDDL 里所有者**常写成两字母别名**（`O:BA` = Administrators、
        /// `O:SY` = SYSTEM、`O:LA` = 本机管理员），拿"读到 G/D/S 为止"的土办法抠出来是 `BA`
        /// 这种字符串，永远和 `S-1-5-…` 对不上 —— 表现是「还原成功但读回来说不一样」，
        /// 记录一直挂着（D16f 就是这么红的）。这里交给 `RawSecurityDescriptor` 去翻译。</summary>
        private static string OwnerSidOf(string sddl)
        {
            try
            {
                RawSecurityDescriptor sd = new RawSecurityDescriptor(sddl);
                if (sd.Owner != null) { return sd.Owner.Value; }
            }
            catch { }
            int i = sddl.IndexOf("O:", StringComparison.Ordinal);
            if (i < 0) { return ""; }
            int start = i + 2;
            int end = start;
            while (end < sddl.Length && sddl[end] != 'G' && sddl[end] != 'D' && sddl[end] != 'S') { end++; }
            return sddl.Substring(start, end - start).Trim();
        }

        // ------------------------------------------------------------------ 记录文件

        private static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }

        private static string From64(string s)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String((s ?? "").Trim())); }
            catch { return ""; }
        }

        private static string Join(string[] r) { return string.Join("\t", r); }

        private static void AppendRecords(List<string> lines)
        {
            string path = RecordFile();
            bool exists = File.Exists(path);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
            using (StreamWriter w = new StreamWriter(path, true, new UTF8Encoding(false)))
            {
                if (!exists) { w.WriteLine(TsvHeader); }
                foreach (string l in lines) { w.WriteLine(l); }
            }
        }

        private static void WriteRecords(List<string> lines)
        {
            string path = RecordFile();
            if (lines.Count == 0)
            {
                try { if (File.Exists(path)) { File.Delete(path); } } catch { }
                return;
            }
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
            using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine(TsvHeader);
                foreach (string l in lines) { w.WriteLine(l); }
            }
        }

        public static List<string[]> ReadRecords()
        {
            List<string[]> rows = new List<string[]>();
            string path = RecordFile();
            try
            {
                if (!File.Exists(path)) { return rows; }
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) { continue; }
                    string[] p = line.Split('\t');
                    if (p.Length >= 4) { rows.Add(p); }
                }
            }
            catch { }
            return rows;
        }

        public static int PendingRecords() { return ReadRecords().Count; }

        // ------------------------------------------------------------------ 报告

        public static string ReportText(List<FileOwnItem> items)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("当前用户：").AppendLine(CurrentUserName());
            sb.Append("共 ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 项：");
            foreach (FileOwnItem it in items)
            {
                sb.Append(it.IsSelf ? "· " : "× ").Append(it.Path).Append("　[").Append(it.KindText())
                    .Append("]　所有者=").Append(it.Owner.Length > 0 ? it.Owner : "（读不到）")
                    .AppendLine(it.IsSelf ? "（归你管）" : "（不是你 —— 可以点「取得所有权」拿过来）");
                if (it.Note.Length > 0) { sb.Append("    ").AppendLine(it.Note); }
            }
            sb.Append("pending=").Append(PendingRecords().ToString(CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("record=").AppendLine(RecordFile());
            sb.Append("（只读，没有改权限；取得所有权 / 还原只能在窗口里点）");
            return sb.ToString();
        }

        public static string JsonReport(List<FileOwnItem> items)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"user\":\"").Append(JsonText(CurrentUserName())).Append("\",\"pending\":")
                .Append(PendingRecords().ToString(CultureInfo.InvariantCulture)).Append(",\"items\":[");
            for (int i = 0; i < items.Count; i++)
            {
                FileOwnItem it = items[i];
                if (i > 0) { sb.Append(","); }
                sb.Append("{\"path\":\"").Append(JsonText(it.Path)).Append("\",\"dir\":")
                    .Append(it.IsDir ? "true" : "false").Append(",\"owner\":\"").Append(JsonText(it.Owner))
                    .Append("\",\"self\":").Append(it.IsSelf ? "true" : "false").Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static string JsonText(string s)
        {
            if (s == null) { return ""; }
            StringBuilder sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) { sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                        else { sb.Append(c); }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
