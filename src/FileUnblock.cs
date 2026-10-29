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
    /// <summary>磁盘上的一个「被 Windows 锁住」的文件（2026-10-28 用户提的第 1 件事：解除附件管理器锁定）。
    ///
    /// 机制（量出来的，不是猜的）：从网上下载的文件、从 Outlook / 微信 / QQ 收到的附件，Windows
    /// 会往文件里写一条 **备用数据流（ADS）** `文件名:Zone.Identifier`，内容是
    ///
    ///     [ZoneTransfer]
    ///     ZoneId=3
    ///     HostUrl=https://example.com/x.zip
    ///
    /// `ZoneId=3` = Internet（2 = 本地内网、4 = 受限制站点）。资源管理器看到这条流就把文件当成
    /// 「来自 Internet」→ 打开 Office 文档变保护视图 / 只读、EXE 弹「Windows 已保护你的电脑」、
    /// 压缩包解压出来的东西一层层都带着这个标记。文件属性窗口底下那个「解除锁定」勾选框，
    /// 干的就是**删掉这条数据流**这一件事。
    ///
    /// 所以这一页的「解除锁定」= 删 `Zone.Identifier` 这条 ADS：
    /// **文件内容一个字节都不动**（ADS 是主文件之外的一路数据，删它不改主文件），随时能按记录写回去。
    /// ⚠️ 只对 NTFS / ReFS 有效 —— FAT32 的 U 盘上根本没有 ADS，扫出来就是 0 个。
    ///
    /// 四条口径（和「此电脑多余图标」「文件关联体检」一个规矩）：
    /// ① 扫描**只读**：只读那条流的内容，不改任何东西；
    /// ② 解除前把**那条流的原文**记进 TSV（记不下来就一个文件都不动），所以「还原锁定」永远点得动；
    /// ③ 解除完**逐个头读回核对**（流真的没了才算成功，不靠返回值）；
    /// ④ 命令行 `unblock` 默认只读；写入口（`--unblock=` / `--restore`）**只在隔离根
    ///    `MXX1_UNBLOCK_ROOT` 设好的情况下才放行**（用户自己那台机器只能在窗口里点）。
    ///
    /// 测试隔离：`MXX1_UNBLOCK_ROOT`（把"扫哪个目录"钉死在一个测试目录上 —— 只要它设着，
    /// 命令行就允许写，而且绝不允许写到这个目录外面去）、`MXX1_UNBLOCK_TSV`（记录文件挪走）。</summary>
    internal sealed class UnblockItem
    {
        public string Path;
        public long Size;
        public string ZoneId = "";
        public string HostUrl = "";
        public string ReferrerUrl = "";
        public string Raw = "";

        /// <summary>真的被锁着（ZoneId 2/3/4 才算；只有 HostUrl 没有 ZoneId 的不算，如实标出来）。</summary>
        public bool Blocked
        {
            get
            {
                int z;
                if (!int.TryParse(ZoneId, NumberStyles.Integer, CultureInfo.InvariantCulture, out z)) { return false; }
                return z >= 2;
            }
        }

        /// <summary>`Internet` / `本地 Intranet` / `受限制站点` / `本机` 这种人话。</summary>
        public string ZoneText()
        {
            int z;
            if (!int.TryParse(ZoneId, NumberStyles.Integer, CultureInfo.InvariantCulture, out z)) { return "（没有 ZoneId）"; }
            switch (z)
            {
                case 0: return "本机";
                case 1: return "本地 Intranet";
                case 2: return "受信任站点";
                case 3: return "Internet（被锁的就是这种）";
                case 4: return "受限制站点";
                default: return "Zone " + ZoneId;
            }
        }

        public string Name()
        {
            try { return System.IO.Path.GetFileName(Path); }
            catch { return Path; }
        }

        public string Dir()
        {
            try { return System.IO.Path.GetDirectoryName(Path) ?? ""; }
            catch { return ""; }
        }
    }

    internal sealed class UnblockReport
    {
        public bool Ok = true;
        public int Changed;
        public int Skipped;
        public int Failed;
        public bool NeedAdmin;
        public string Message = "";
    }

    internal static class FileUnblock
    {
        private const string StreamName = ":Zone.Identifier";
        private const string TsvHeader = "# mxx1-toolbox 解除附件管理器锁定 原值记录（制表符分隔；内容做了 base64，别手改）";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFile(string lpFileName);

        // ⚠️ ADS 只能走 P/Invoke（2026-10-29 实测）：.NET 4.x 的 `FileStream` / `File.WriteAllText`
        // 对 `a.txt:Zone.Identifier` 这种路径**直接抛「不支持给定路径的格式」**（NotSupportedException），
        // 于是扫描永远 0 条、整个功能在这台机器上不工作。`DeleteFile` 那条一直是好的（它不解析路径）。
        // ⚠️ `CreateFileW` **必须写 EntryPoint**：只写方法名 + CharSet.Unicode 实测 err=2（打不开），
        // 补上 EntryPoint 之后同一条路径立刻成功 —— 别把这一行"简化"回去。
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;
        private const uint ShareAll = 0x00000007;          // READ | WRITE | DELETE
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToRead,
            out int lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToWrite,
            out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // ------------------------------------------------------------------ 根 / 记录文件

        /// <summary>隔离根就是"扫哪个目录"本身：设了就只在这个目录里干活，写入口也才放行。</summary>
        public static string IsoRoot()
        {
            string v = (Environment.GetEnvironmentVariable("MXX1_UNBLOCK_ROOT") ?? "").Trim().Trim('"');
            return v;
        }

        public static bool Isolated { get { return IsoRoot().Length > 0; } }

        /// <summary>默认扫「下载」文件夹（从网上下的东西九成落在这儿）。</summary>
        public static string DefaultRoot()
        {
            string iso = IsoRoot();
            if (iso.Length > 0) { return iso; }
            try
            {
                string d = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(d)) { return d; }
            }
            catch { }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public static string RootText() { return DefaultRoot(); }

        public static bool WriteAllowed() { return Isolated; }

        public static string RecordFile()
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_UNBLOCK_TSV") ?? "").Trim();
            if (env.Length > 0) { return env; }
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mxx1-toolbox");
            try { if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); } } catch { }
            return System.IO.Path.Combine(dir, "unblock-original.tsv");
        }

        // ------------------------------------------------------------------ 扫描（只读）

        /// <summary>扫一个目录（可选递归）。只读每条 `Zone.Identifier`，一个字节都不写。
        /// 只返回**有那条流**的文件（干净的普通文件不进来，列表才看得清）。
        /// `maxFiles` 是"最多看多少个文件"的护栏（默认 40000），超了会在报告里说一声。</summary>
        public static List<UnblockItem> Scan(string root, bool recurse, ref bool truncated, int maxFiles)
        {
            List<UnblockItem> list = new List<UnblockItem>();
            if (string.IsNullOrEmpty(root)) { root = DefaultRoot(); }
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("这个文件夹不存在：" + root);
            }
            SearchOption opt = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            int seen = 0;
            foreach (string f in Directory.EnumerateFiles(root, "*", opt))
            {
                seen++;
                if (seen > maxFiles) { truncated = true; break; }
                UnblockItem it = Probe(f);
                if (it != null) { list.Add(it); }
            }
            list.Sort(delegate (UnblockItem a, UnblockItem b)
            {
                if (a.Blocked != b.Blocked) { return a.Blocked ? -1 : 1; }
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>读一个文件的 `Zone.Identifier`；没有就返回 null。</summary>
        public static UnblockItem Probe(string path)
        {
            string raw;
            if (!TryReadStream(path, out raw)) { return null; }
            UnblockItem it = new UnblockItem();
            it.Path = path;
            it.Raw = raw;
            try { it.Size = new FileInfo(path).Length; } catch { it.Size = 0; }
            foreach (string line in raw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) { continue; }
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                if (string.Equals(k, "ZoneId", StringComparison.OrdinalIgnoreCase)) { it.ZoneId = v; }
                else if (string.Equals(k, "HostUrl", StringComparison.OrdinalIgnoreCase)) { it.HostUrl = v; }
                else if (string.Equals(k, "ReferrerUrl", StringComparison.OrdinalIgnoreCase)) { it.ReferrerUrl = v; }
            }
            return it;
        }

        /// <summary>读那条 ADS。读不到（文件没有这条流 / 不是 NTFS / 被占用）就返回 false。
        /// ⚠️ 这里**绝不能**用 `File.Exists(path + ":Zone.Identifier")` 判存在 —— 它对 ADS 会返回
        /// false（.NET 的 Exists 只看主文件），只有真去开一次才知道有没有。</summary>
        public static bool TryReadStream(string path, out string raw)
        {
            raw = "";
            IntPtr h = InvalidHandle;
            try
            {
                h = CreateFileW(path + StreamName, GenericRead, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (h == InvalidHandle) { return false; }   // 没有这条流（或者打不开）
                byte[] buf = new byte[64 * 1024];           // 正常的这条流只有几十字节
                int got;
                if (!ReadFile(h, buf, buf.Length, out got, IntPtr.Zero)) { return false; }
                if (got >= buf.Length) { return false; }    // 比 64KB 还大：不认，免得记下来的原值是截断的
                raw = (got <= 0) ? "" : Encoding.UTF8.GetString(buf, 0, got);
                return true;
            }
            catch
            {
                raw = "";
                return false;
            }
            finally
            {
                if (h != InvalidHandle) { try { CloseHandle(h); } catch { } }
            }
        }

        /// <summary>把原文写回 `文件名:Zone.Identifier`（还原用）。返回值只表示"写进去了"，
        /// 真的成没成由调用方读回核对。</summary>
        private static bool TryWriteStream(string path, string raw)
        {
            IntPtr h = InvalidHandle;
            try
            {
                h = CreateFileW(path + StreamName, GenericWrite, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (h == InvalidHandle)
                {
                    // 流不存在时 CreateFileW 不会替我们建（ADS 的"新建"要 CREATE_ALWAYS）
                    h = CreateFileW(path + StreamName, GenericWrite, ShareAll, IntPtr.Zero, 2 /*CREATE_ALWAYS*/, 0, IntPtr.Zero);
                    if (h == InvalidHandle) { return false; }
                }
                byte[] b = Encoding.UTF8.GetBytes(raw ?? "");
                int wrote;
                return WriteFile(h, b, b.Length, out wrote, IntPtr.Zero) && wrote == b.Length;
            }
            catch { return false; }
            finally
            {
                if (h != InvalidHandle) { try { CloseHandle(h); } catch { } }
            }
        }

        // ------------------------------------------------------------------ 解除（写）

        /// <summary>解除一批文件的锁定：先记原值 → 删那条 ADS → 读回核对。</summary>
        public static UnblockReport Unblock(List<UnblockItem> items)
        {
            UnblockReport rep = new UnblockReport();
            if (items == null || items.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有要解除的文件。";
                return rep;
            }
            string record = RecordFile();
            List<string> lines = new List<string>();
            StringBuilder bad = new StringBuilder();

            // ① 先记原值：一条记不下来就一个文件都不动（同「此电脑多余图标」）
            List<UnblockItem> todo = new List<UnblockItem>();
            foreach (UnblockItem it in items)
            {
                string raw;
                if (!TryReadStream(it.Path, out raw))
                {
                    rep.Skipped++;          // 扫描之后它自己变了（或者流已经被别的程序删了）
                    continue;
                }
                it.Raw = raw;
                todo.Add(it);
                lines.Add(Now() + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(it.Path)) + "\t"
                    + (it.ZoneId.Length > 0 ? it.ZoneId : "-") + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
            }
            if (lines.Count > 0)
            {
                try { AppendRecords(record, lines); }
                catch (Exception ex)
                {
                    rep.Ok = false;
                    rep.Message = "原值记不下来（" + record + "）：" + ex.Message + " —— 为了能还原，这次一个文件都没动。";
                    rep.Failed = items.Count;
                    return rep;
                }
            }

            // ② 逐个删 + 读回核对
            foreach (UnblockItem it in todo)
            {
                if (!DeleteStream(it.Path))
                {
                    rep.Failed++;
                    int err = Marshal.GetLastWin32Error();
                    if (err == 5) { rep.NeedAdmin = true; }
                    bad.Append("　· ").Append(it.Path)
                        .Append("（删不掉").Append(err > 0 ? "，错误 " + err.ToString(CultureInfo.InvariantCulture) : "")
                        .Append("）").AppendLine();
                    continue;
                }
                string after;
                if (TryReadStream(it.Path, out after))
                {
                    // 删了还在：不算成功（可能是另一个进程又写回来了）
                    rep.Failed++;
                    bad.Append("　· ").Append(it.Path).Append("（删完读回来还在）").AppendLine();
                }
                else
                {
                    rep.Changed++;
                }
            }

            rep.Ok = rep.Failed == 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("解除 ").Append(rep.Changed.ToString(CultureInfo.InvariantCulture)).Append(" 个");
            if (rep.Skipped > 0) { sb.Append("，跳过 ").Append(rep.Skipped.ToString(CultureInfo.InvariantCulture)).Append(" 个（扫描之后它自己变了）"); }
            if (rep.Failed > 0) { sb.Append("，失败 ").Append(rep.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 个"); }
            sb.Append("。都读过一遍确认过。");
            if (bad.Length > 0) { sb.AppendLine().Append(bad.ToString()); }
            sb.Append("原值记在：").Append(record);
            rep.Message = sb.ToString();
            return rep;
        }

        /// <summary>按记录把锁定写回去：那条流现在不在才写（被别的程序又锁上了就跳过、不覆盖）。</summary>
        public static UnblockReport Restore()
        {
            UnblockReport rep = new UnblockReport();
            List<string[]> rows = ReadRecords();
            if (rows.Count == 0)
            {
                rep.Ok = false;
                rep.Message = "没有待还原的记录（这台机器还没用这个窗口解除过锁定）。";
                return rep;
            }
            List<string> keep = new List<string>();
            StringBuilder bad = new StringBuilder();
            foreach (string[] r in rows)
            {
                if (r.Length < 4) { continue; }
                string path = From64(r[1]);
                string raw = From64(r[3]);
                if (path.Length == 0 || raw.Length == 0) { continue; }
                if (!File.Exists(path))
                {
                    rep.Skipped++;
                    keep.Add(Join(r));
                    bad.Append("　· ").Append(path).Append("（文件已经不在了，记录留着）").AppendLine();
                    continue;
                }
                string now;
                if (TryReadStream(path, out now))
                {
                    // 已经被重新锁上了：不动它，记录清掉（原本就是要这个状态）
                    rep.Skipped++;
                    continue;
                }
                try
                {
                    if (!TryWriteStream(path, raw))
                    {
                        rep.Failed++;
                        keep.Add(Join(r));
                        bad.Append("　· ").Append(path).AppendLine("（那条备用数据流写不进去）");
                        continue;
                    }
                    string back;
                    if (!TryReadStream(path, out back))
                    {
                        rep.Failed++;
                        keep.Add(Join(r));
                        bad.Append("　· ").Append(path).Append("（写回去读不回来）").AppendLine();
                    }
                    else
                    {
                        rep.Changed++;
                    }
                }
                catch (Exception ex)
                {
                    rep.Failed++;
                    keep.Add(Join(r));
                    if (ex is UnauthorizedAccessException) { rep.NeedAdmin = true; }
                    bad.Append("　· ").Append(path).Append("（写不进去：").Append(ex.Message).Append("）").AppendLine();
                }
            }
            WriteRecords(keep);
            rep.Ok = rep.Failed == 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("还原 ").Append(rep.Changed.ToString(CultureInfo.InvariantCulture)).Append(" 个");
            if (rep.Skipped > 0) { sb.Append("，跳过 ").Append(rep.Skipped.ToString(CultureInfo.InvariantCulture)).Append(" 个"); }
            if (rep.Failed > 0) { sb.Append("，失败 ").Append(rep.Failed.ToString(CultureInfo.InvariantCulture)).Append(" 个"); }
            sb.Append("。");
            if (bad.Length > 0) { sb.AppendLine().Append(bad.ToString()); }
            rep.Message = sb.ToString();
            return rep;
        }

        /// <summary>删 `文件名:Zone.Identifier`（ADS 只能用 `DeleteFile` 删）→ 读回核对由调用方做。</summary>
        private static bool DeleteStream(string path)
        {
            try
            {
                if (DeleteFile(path + StreamName)) { return true; }
                int err = Marshal.GetLastWin32Error();
                if (err == 2) { return true; }        // 本来就没有：当成已经好了
                return false;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ 记录文件

        private static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string Join(string[] r) { return string.Join("\t", r); }

        private static string From64(string s)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String((s ?? "").Trim())); }
            catch { return ""; }
        }

        private static void AppendRecords(string path, List<string> lines)
        {
            bool exists = File.Exists(path);
            string dir = System.IO.Path.GetDirectoryName(path);
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
            string dir = System.IO.Path.GetDirectoryName(path);
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
                    if (line.Trim().Length == 0) { continue; }
                    if (line.StartsWith("#")) { continue; }
                    string[] p = line.Split('\t');
                    if (p.Length >= 4) { rows.Add(p); }
                }
            }
            catch { }
            return rows;
        }

        public static int PendingRecords()
        {
            return ReadRecords().Count;
        }

        // ------------------------------------------------------------------ 报告

        public static string ReportText(List<UnblockItem> items, string root, bool truncated)
        {
            StringBuilder sb = new StringBuilder();
            int blocked = 0;
            foreach (UnblockItem it in items) { if (it.Blocked) { blocked++; } }
            sb.Append("扫描目录：").AppendLine(root);
            sb.Append("带 Zone.Identifier 的文件：").Append(items.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" 个（其中真的被锁住的 ").Append(blocked.ToString(CultureInfo.InvariantCulture)).Append(" 个）");
            if (truncated) { sb.Append("　⚠️ 文件太多，只看了前面一部分（用 --max= 调）"); }
            sb.AppendLine();
            foreach (UnblockItem it in items)
            {
                sb.Append(it.Blocked ? "× " : "· ").Append(it.Path)
                    .Append("　ZoneId=").Append(it.ZoneId.Length > 0 ? it.ZoneId : "-")
                    .Append("（").Append(it.ZoneText()).Append("）");
                if (it.HostUrl.Length > 0) { sb.Append("　来自 ").Append(it.HostUrl); }
                sb.AppendLine();
            }
            sb.Append("pending=").Append(PendingRecords().ToString(CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("record=").AppendLine(RecordFile());
            sb.Append("（只读，没有改任何文件；解除 / 还原只能在窗口里点）");
            return sb.ToString();
        }

        public static string JsonReport(List<UnblockItem> items, string root, bool truncated)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"root\":\"").Append(JsonText(root)).Append("\",\"truncated\":")
                .Append(truncated ? "true" : "false").Append(",\"pending\":")
                .Append(PendingRecords().ToString(CultureInfo.InvariantCulture)).Append(",\"items\":[");
            for (int i = 0; i < items.Count; i++)
            {
                UnblockItem it = items[i];
                if (i > 0) { sb.Append(","); }
                sb.Append("{\"path\":\"").Append(JsonText(it.Path)).Append("\",\"blocked\":")
                    .Append(it.Blocked ? "true" : "false").Append(",\"zone\":\"").Append(JsonText(it.ZoneId))
                    .Append("\",\"host\":\"").Append(JsonText(it.HostUrl)).Append("\"}");
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
