// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「复制文件名」/「复制文件路径」—— 把选中文件 / 文件夹的**名字**或**完整路径**
    /// 复制进剪贴板（一行一个）。
    ///
    /// 谁在用：① 右键菜单里那**两个**菜单项（`src\RightMenu.cs` 的 CopyNameVerb / CopyPathVerb），
    /// 命令分别是 `"<工具箱 exe>" copypath --name "%1"` 和 `"<工具箱 exe>" copypath "%1"`；
    /// ② 命令行 `copypath <文件…> [--name] [--relative] [--base=<目录>] [--quote]`。
    /// 用户 2026-10-06 晚六要的是"两条菜单项：一个文件名、一个完整路径"，而且**两边都不带引号**
    /// —— 所以菜单里那两条命令都不传 `--quote`（`--quote` 只留作命令行的一个可选开关）。
    /// （原来那条「相对路径」菜单项已经撤掉：右键时的工作目录**就是**文件所在的文件夹，
    /// 折出来正好等于文件名 —— `--relative` 作为命令行开关留着，菜单不用它。）
    ///
    /// **多选那件事这里自己做了一遍**（`AppendBatch` / `SettleMs` / `JoinMs`，别删）：
    /// 右键菜单里那一项写的是 `MultiSelectModel=Player`，按微软文档 legacy verb 的 Player 模型
    /// 资源管理器**一次把选中的路径全传进来**（[How to Employ the Verb Selection Model]）——
    /// 可这条规矩我们在**真实资源管理器里一次都没实测过**（tests 是人造命令行的，DESIGN §14.10 里
    /// 挂着"要人眼确认一次"）。万一它其实是"每个文件起一个进程"，那就变成"选中 3 个、剪贴板里只剩
    /// 最后一个"—— 看着像坏了。所以这里做成一件事：
    ///   单个路径进来时先**记进一个批次文件**、等 `SettleMs`（400ms）看有没有别的进程跟着来：
    ///     · 后面还有人追加 → 让**最后一个**进程收尾（它手里是完整列表）；
    ///     · 自己就是最后一个 → 把整批路径一起复制出去。
    /// 于是"一次全传进来"和"每个文件一次"这两种情况**都是对的**（多进程并发有测试：O 组）。
    /// 代价写在 DESIGN 里：两次独立的复制挨得比 `JoinMs`（1.5 秒）还近时会被合成一批。
    /// ⚠️ 模式（文件名 / 相对 / 绝对）也记在批次文件的头一行：**模式对不上就不并批** ——
    /// 两条菜单项就是两个模式，合成一批会把文件名折成路径（或反过来）。
    ///
    /// 底线：**只读**（不移动、不改名、不删除任何文件）、不联网；路径里有空格 / 中文 / `&` 都照原样复制
    /// （剪贴板里的文本不做任何转义，`--quote` 那种加引号也是"照 Windows 自己那个「复制为路径」的写法"）。
    ///
    /// 剪贴板只有一个，谁都可能正开着它（输入法 / 剪贴板历史 / 另一个工具），所以写失败**重试 3 次**，
    /// 还失败就如实说"剪贴板忙"——不谎报"已复制"。</summary>
    internal static class CopyPath
    {
        /// <summary>日志里那个标签。两种模式共用（日志正文里会写明是名字还是路径）。</summary>
        public const string LogTag = "复制文件路径";

        /// <summary>批次文件"安静"多久之后才认"我是最后一个"（毫秒）。**别改成固定 sleep**：
        /// 2026-10-06 晚五第一次写的是"死等 400ms 再回头看"，实测三个进程并发时**被切成两批**
        /// （前一个 400ms 早过了、后两个才刚启动 —— 每个进程都要现冷启动一次 .NET + WinForms），
        /// 剪贴板里只剩后两个。现在是"轮询到批次文件不再变为止"。
        /// **实测在 §12.62 那张表里**（三个并发进程 → 一行一个全在）。</summary>
        private const int QuietMs = 220;

        /// <summary>最多等多久（毫秒）：等满了还是"后面有人"就让位，还是"我就是最后一个"就自己收尾
        /// （**不能直接放弃** —— 放弃了就没人复制了）。</summary>
        private const int MaxWaitMs = 1500;

        /// <summary>轮询间隔（毫秒）。</summary>
        private const int PollMs = 100;

        /// <summary>上次追加之后多久之内的追加算**同一批**（毫秒）。见类说明里的取舍。</summary>
        private const int JoinMs = 1500;

        /// <summary>一批最多记多少个路径（资源管理器 Player 模型的上限是 100，这里放宽一倍）。</summary>
        private const int MaxBatch = 200;

        /// <summary>跨进程串行化批次文件（同一批的几个进程会同时读写它）。</summary>
        private const string MutexName = "Mxx1Toolbox.CopyPath.Batch";

        public static string BatchFile
        {
            get { return Path.Combine(AppPaths.BaseDir, "copypath-batch.txt"); }
        }

        /// <summary>跑一次。返回退出码：0 = 成功（含"别人收尾，我什么都不用做"），2 = 用法错 / 剪贴板写不进去。
        ///
        /// `nameOnly` = 复制**名字**（最后一段：文件 `报告.txt`、文件夹 `2026`）—— 右键菜单第一条菜单项。
        /// `relative` = 复制**相对路径**（相对 `baseDir`；`baseDir` 空 = 自己按规矩挑一个基准，
        /// 见 ResolveBase）。`nameOnly` 和 `relative` 同时给时以 `nameOnly` 为准。</summary>
        public static int Run(List<string> rawPaths, bool quote, bool relative, string baseDir, bool nameOnly,
            bool noWait, bool print, bool notify, int notifyMs)
        {
            List<string> paths = Normalize(rawPaths);
            if (paths.Count == 0)
            {
                Console.Error.WriteLine("用法: copypath <文件…> [--name] [--relative] [--base=<目录>] [--quote] [--print] [--no-wait]");
                Console.Error.WriteLine("      路径里可以有空格和中文；--name 只取名字（报告.txt）；--relative 复制相对路径（基准 = --base，没给就用当前目录）");
                Console.Error.WriteLine("      默认**不带引号**；--quote 是每行加上英文引号（可选，给要粘进命令行的场合）");
                return 2;
            }

            string baseFull = "";
            if (relative && !nameOnly)
            {
                string baseError = ResolveBase(paths[0], baseDir, out baseFull);
                if (baseError.Length > 0)
                {
                    Console.Error.WriteLine(baseError);
                    Console.WriteLine("error=" + baseError);
                    return 2;
                }
            }

            Console.WriteLine("input=" + paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine("quoted=" + (quote ? "yes" : "no"));
            Console.WriteLine("name=" + (nameOnly ? "yes" : "no"));
            Console.WriteLine("relative=" + (relative ? "yes" : "no"));
            if (relative && !nameOnly) { Console.WriteLine("base=" + baseFull); }

            // 多个路径 = 资源管理器一次全传进来了（或者用户自己一次给了好几个），不用猜也不用等
            bool batch = (!noWait && paths.Count == 1);
            List<string> finalList;
            string role;
            int waited = 0;
            if (batch)
            {
                Append(paths[0], relative, nameOnly, baseFull);
                // 轮询到"批次文件不再变、而且最后一条就是我"为止 —— 见 QuietMs 的说明
                DateTime t0 = DateTime.UtcNow;
                finalList = null;
                while (true)
                {
                    Thread.Sleep(PollMs);
                    waited = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                    bool last = false;
                    List<string> now;
                    using (BatchLock gate = new BatchLock())
                    {
                        now = ReadBatchLocked(JoinMs, relative, nameOnly, baseFull);
                        last = (now.Count > 0) && Same(now[now.Count - 1], paths[0]);
                        if (last && (QuietAgeMs() >= QuietMs)) { finalList = now; }
                    }
                    if (finalList != null) { break; }
                    if (!last && (QuietAgeMs() >= QuietMs))
                    {
                        // 后面那条已经安静下来了，而它一定看得见"自己是最后一条"→ 由它收尾，我退场。
                        // （批次文件**不删**：晚到的兄弟还能并进同一批 —— 见类说明里那条取舍）
                        break;
                    }
                    if (waited >= MaxWaitMs)
                    {
                        // 等满了：还是"我就是最后一条"就自己收尾（不能谁也不复制），否则让位给后面那个
                        if (last) { finalList = now; }
                        break;
                    }
                }
                if (finalList == null)
                {
                    // 后面还有进程跟着进这一批 —— 让最后一个收尾（它手里是完整列表），这里静静地退场
                    Console.WriteLine("mode=batch");
                    Console.WriteLine("role=waiter");
                    Console.WriteLine("count=0");
                    Console.WriteLine("clipboard=skipped");
                    Console.WriteLine("waited=" + waited.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return 0;
                }
                role = "closer";
            }
            else
            {
                finalList = Dedupe(paths);
                role = "closer";
            }

            // 剪贴板里最终写什么：文件名模式取最后一段；相对模式把每一行折成相对路径（跨盘符那一行会退回
            // 完整路径）；绝对模式原样。折完还要去一次重：绝对路径和相对路径指着同一个文件时会折成同一行
            // （命令行上混着给才会出现，右键菜单不会 —— 但"复制出两行一模一样的"就是坏的，顺手挡掉）。
            List<string> shown = new List<string>();
            foreach (string p in finalList)
            {
                string s = nameOnly ? NameOf(p) : (relative ? RelativeOne(p, baseFull) : p);
                if (!Contains(shown, s)) { shown.Add(s); }
            }

            string text = Format(shown, quote);
            string error = "";
            if (print)
            {
                Console.WriteLine("clipboard=skipped");
            }
            else
            {
                error = WriteClipboard(text);
            }

            Console.WriteLine("mode=" + (batch ? "batch" : "direct"));
            Console.WriteLine("role=" + role);
            Console.WriteLine("count=" + shown.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine("batchfile=" + BatchFile);
            if (!print) { Console.WriteLine("clipboard=" + ((error.Length == 0) ? "ok" : "busy")); }
            int missing = 0;
            foreach (string p in finalList)
            {
                bool exists = false;
                try { exists = File.Exists(p) || Directory.Exists(p); }
                catch { }
                if (!exists) { missing++; }
                Console.WriteLine("path=" + p);
            }
            Console.WriteLine("missing=" + missing.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (print)
            {
                foreach (string line in SplitText(text)) { Console.WriteLine("copied=" + line); }
                return 0;
            }
            if (error.Length > 0)
            {
                Logger.Write(LogTag, "剪贴板写不进去：" + error);
                if (notify) { Balloon.Show(TitleOf(nameOnly) + " · 失败", "剪贴板被别的程序占着，复制没成功（过一会儿再试一次）。", notifyMs, NoticeKind.Fail); }
                Console.Error.WriteLine("剪贴板写不进去：" + error);
                return 2;
            }

            Logger.Write(LogTag, "复制了 " + shown.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " 个" + (nameOnly ? "名字" : "路径")
                + "（" + (nameOnly ? "只取文件名、" : (relative ? "相对路径、" : "")) + (quote ? "带引号" : "不带引号") + "）："
                + string.Join(" | ", shown.ToArray()));
            if (notify)
            {
                Balloon.Show(TitleOf(nameOnly), Summary(shown, quote, nameOnly), notifyMs, NoticeKind.Ok);
            }
            return 0;
        }

        /// <summary>提示卡 / 日志里那个名字（两种模式各一个，用户 2026-10-06 晚六定的那两个菜单项）。</summary>
        private static string TitleOf(bool nameOnly)
        {
            return nameOnly ? "复制文件名" : "复制文件路径";
        }

        /// <summary>去掉没给全的开关，并把"路径里混着引号"这件事挡掉：Windows 文件名里**不可能**有
        /// 双引号，所以参数里出现的每一个 `"` 都是资源管理器替换 `%1` 时留下的（或者用户手打多了），
        /// 按它切开是安全的。这样"一整串被包在一对引号里、里面又各自带引号"的那种写法
        /// （多选时真出现过）也能正确认成两个路径。</summary>
        private static List<string> Normalize(List<string> raw)
        {
            List<string> list = new List<string>();
            if (raw == null) { return list; }
            foreach (string r in raw)
            {
                if (r == null) { continue; }
                string s = r.Trim();
                if (s.Length == 0 || s.StartsWith("--")) { continue; }
                foreach (string piece in s.Split('"'))
                {
                    string p = piece.Trim();
                    if (p.Length == 0) { continue; }
                    // 只有一个盘符的（`D:`）补上反斜杠 = 那个盘的根。为什么会有这种东西：右键**盘的根**
                    // 时占位符换成的是 `D:\`，而 `"D:\"` 在命令行解析里那个反斜杠会把引号吃掉
                    // （`\"` = 转义引号），于是我们收到的是 `D:"` → 去掉引号就成了 `D:`。
                    // 不补的话 `Path.GetFullPath("D:")` 会当成"这个盘上的当前目录"，跟着 cwd 漂。
                    if ((p.Length == 2) && (p[1] == ':')) { p += "\\"; }
                    if (!Contains(list, p)) { list.Add(p); }
                }
            }
            return list;
        }

        private static List<string> Dedupe(List<string> src)
        {
            List<string> list = new List<string>();
            foreach (string s in src) { if (!Contains(list, s)) { list.Add(s); } }
            return list;
        }

        private static bool Contains(List<string> list, string value)
        {
            foreach (string s in list)
            {
                if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>剪贴板里那一串：一行一个（Windows 自己的「复制为路径」也是这个格式，
        /// 粘贴到命令行 / 聊天窗口 / 表格里都是一行一条）。</summary>
        private static string Format(List<string> paths, bool quote)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string p in paths)
            {
                if (sb.Length > 0) { sb.Append("\r\n"); }
                sb.Append(quote ? ("\"" + p + "\"") : p);
            }
            return sb.ToString();
        }

        private static string[] SplitText(string text)
        {
            return text.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        // ------------------------------------------------------------------ 相对路径

        /// <summary>相对路径的基准目录。挑的次序（2026-10-06 晚六定的）：
        ///   ① `--base=&lt;目录&gt;` 给了、而且真是个目录 → 用它（命令行 / 测试用这条）；
        ///   ② **当前工作目录** —— 右键菜单就是靠这一条：资源管理器起的进程，工作目录就是
        ///      你正在浏览的那个文件夹，所以"相对路径"= 相对你右键时所在的那个文件夹；
        ///      只有它确实存在、而且目标**真的在它下面**才用（不然会得到一串 `..\..\` 这种东西）；
        ///   ③ 目标自己所在的目录（那时相对路径就是文件名，短而正确）。
        /// 返回非空 = 出错原因（只有"明确给了 --base 却不是目录"这一种，别的都能退到下一档）。</summary>
        private static string ResolveBase(string target, string baseDir, out string full)
        {
            full = "";
            string want = (baseDir == null) ? "" : AppPaths.Expand(baseDir).Trim().Trim('"');
            // 占位符没被替换掉（字面量 %V / %1 传进来）时不能拿它当目录 —— 那是环境的问题，不是用户的输入
            if ((want == "%V") || (want == "%1")) { want = ""; }
            if (want.Length > 0)
            {
                try { full = Path.GetFullPath(want); }
                catch (Exception ex) { return "这个基准目录认不出来：" + want + "（" + ex.Message + "）"; }
                bool ok = false;
                try { ok = Directory.Exists(full); }
                catch { }
                if (!ok) { return "没有这个基准目录：" + full + "（--base 给的不是一个存在的目录）"; }
                return "";
            }

            string cwd = "";
            try { cwd = Environment.CurrentDirectory; } catch { }
            if (cwd.Length > 0)
            {
                string cwdFull = "";
                try { cwdFull = Path.GetFullPath(cwd); } catch { }
                if (cwdFull.Length > 0)
                {
                    bool ok = false;
                    try { ok = Directory.Exists(cwdFull); }
                    catch { }
                    if (ok && Under(cwdFull, target)) { full = cwdFull; return ""; }
                }
            }

            try
            {
                string t = Path.GetFullPath(target);
                DirectoryInfo up = Directory.GetParent(TrimTail(t));
                full = (up != null) ? up.FullName : t;
            }
            catch { full = ""; }
            return "";
        }

        /// <summary>target 是不是在 dir 里面（含等于）。不区分大小写。</summary>
        private static bool Under(string dir, string target)
        {
            string d = TrimTail(dir);
            string t = "";
            try { t = TrimTail(Path.GetFullPath(target)); } catch { return false; }
            if (string.Equals(d, t, StringComparison.OrdinalIgnoreCase)) { return true; }
            if (!t.StartsWith(d, StringComparison.OrdinalIgnoreCase)) { return false; }
            return t.Length > d.Length && (t[d.Length] == '\\' || t[d.Length] == '/');
        }

        /// <summary>一个路径的相对形式（相对 baseDir）。右键的那个就是基准目录本身时，
        /// 往上一层当基准 —— 否则只能得到"."（没法用）。跨盘符时没有相对路径，原样返回完整路径。</summary>
        private static string RelativeOne(string target, string baseDir)
        {
            string full;
            try { full = Path.GetFullPath(target); }
            catch { return target; }
            string b = baseDir;
            if (b.Length == 0)
            {
                try
                {
                    DirectoryInfo up = Directory.GetParent(TrimTail(full));
                    b = (up != null) ? up.FullName : full;
                }
                catch { b = full; }
            }
            if (string.Equals(TrimTail(full), TrimTail(b), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    DirectoryInfo up = Directory.GetParent(TrimTail(b));
                    if (up != null) { b = up.FullName; }
                }
                catch { }
            }
            return MakeRelative(full, b);
        }

        /// <summary>手写的相对路径（.NET Framework 4.x 没有 `Path.GetRelativePath`，
        /// 用 `Uri.MakeRelativeUri` 那条路会把空格 / `#` / `%` 转义成 `%20` 这种，粘出去是坏的）。
        /// 规则：盘符（或 UNC 的 `\\server\share`）不一样就没有相对路径，原样返回绝对路径。</summary>
        private static string MakeRelative(string target, string baseDir)
        {
            string t = TrimTail(target);
            string b = TrimTail(baseDir);
            string tr = RootOf(t);
            string br = RootOf(b);
            if (!string.Equals(tr, br, StringComparison.OrdinalIgnoreCase)) { return target; }

            string[] tp = Parts(t, tr);
            string[] bp = Parts(b, br);
            int common = 0;
            while ((common < tp.Length) && (common < bp.Length)
                && string.Equals(tp[common], bp[common], StringComparison.OrdinalIgnoreCase))
            {
                common++;
            }
            StringBuilder sb = new StringBuilder();
            for (int i = common; i < bp.Length; i++) { sb.Append("..\\"); }
            for (int i = common; i < tp.Length; i++)
            {
                if ((sb.Length > 0) && (sb[sb.Length - 1] != '\\')) { sb.Append('\\'); }
                sb.Append(tp[i]);
            }
            string r = sb.ToString();
            return (r.Length == 0) ? "." : r;
        }

        /// <summary>去掉结尾的分隔符（`C:\` 这种只留盘符的不能变成 `C:`）。</summary>
        private static string TrimTail(string p)
        {
            string s = (p == null) ? "" : p.TrimEnd('\\', '/');
            if (s.EndsWith(":") ) { s += "\\"; }
            return s;
        }

        /// <summary>这条路径的"根"：普通盘符是 `C:\`，UNC 是 `\\server\share\`。</summary>
        private static string RootOf(string p)
        {
            if (p.StartsWith("\\\\"))
            {
                int i = p.IndexOf('\\', 2);
                if (i < 0) { return p; }
                int j = p.IndexOf('\\', i + 1);
                return (j < 0) ? p : p.Substring(0, j + 1);
            }
            return (p.Length >= 2) ? (p.Substring(0, 2) + "\\") : p;
        }

        /// <summary>去掉根之后按分隔符切开（空段丢掉）。</summary>
        private static string[] Parts(string p, string root)
        {
            string rest = (p.Length > root.Length) ? p.Substring(root.Length) : "";
            return rest.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>一个路径的**名字**（最后一段）：文件 `报告.txt`、文件夹 `2026`。
        /// 取不到名字时原样返回（比如盘符根 `D:\`，`GetFileName` 给的是空串）。</summary>
        private static string NameOf(string target)
        {
            string p = (target == null) ? "" : target;
            string n = "";
            try { n = Path.GetFileName(TrimTail(p)); }
            catch { }
            return (n.Length > 0) ? n : p;
        }

        /// <summary>卡片上那两行话（名字取路径最后一段，整条路径太长了）。</summary>
        private static string Summary(List<string> paths, bool quote, bool nameOnly)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < paths.Count && i < 4; i++)
            {
                names.Add(NameOf(paths[i]));
            }
            string joined = string.Join("、", names.ToArray());
            if (paths.Count > names.Count)
            {
                joined += " 等 " + paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个";
            }
            return paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (nameOnly ? " 个文件名" : " 个路径")
                + "已经复制好（一行一个" + (quote ? "，带引号" : "") + "）：" + joined;
        }

        // ------------------------------------------------------------------ 批次（多选）

        /// <summary>把这一次的路径记进批次文件。已经在里面的（同一批里资源管理器重复调用）就不再记。
        ///
        /// 文件第一行是**模式头**（`#abs` / `#name` / `#rel &lt;基准目录&gt;`）：同一个模式的几条才能并成
        /// 一批（"复制文件名"和"复制文件路径"是两条菜单项，合成一批就会把文件名折成路径），
        /// 相对模式还要求基准也一样。模式或基准对不上就当"上一批已经过去了"，从头写一份新的。
        /// 这样收尾的那个进程只要按自己的模式处理就行，不会把绝对路径当成相对路径去折。
        /// （`#` 开头的一行不可能是路径：Windows 路径要么从盘符开头，要么从 `\\` 开头。）</summary>
        private static void Append(string path, bool relative, bool nameOnly, string baseDir)
        {
            using (BatchLock gate = new BatchLock())
            {
                List<string> list = ReadBatchLocked(JoinMs, relative, nameOnly, baseDir);
                if (!Contains(list, path)) { list.Add(path); }
                if (list.Count > MaxBatch) { list.RemoveRange(0, list.Count - MaxBatch); }
                try
                {
                    AppPaths.EnsureBase();
                    StringBuilder sb = new StringBuilder();
                    sb.Append(HeaderOf(relative, nameOnly, baseDir)).Append("\r\n");
                    foreach (string s in list) { sb.Append(s).Append("\r\n"); }
                    File.WriteAllText(BatchFile, sb.ToString(), new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    // 记不进批次文件就当"这一批只有我一个"：宁可只复制自己这一条，也不能复制出错
                    Logger.Write(LogTag, "批次文件写不进去（这一批只认自己）：" + ex.Message);
                }
            }
        }

        /// <summary>批次文件的模式头。基准目录里的制表符 / 换行会让这一行坏掉，
        /// 那种路径（理论上有、实际上没见过）走不带基准的写法，收尾时自己再挑一次基准。</summary>
        private static string HeaderOf(bool relative, bool nameOnly, string baseDir)
        {
            if (nameOnly) { return "#name"; }
            if (!relative) { return "#abs"; }
            string b = (baseDir == null) ? "" : baseDir;
            if ((b.IndexOf('\t') >= 0) || (b.IndexOf('\r') >= 0) || (b.IndexOf('\n') >= 0)) { b = ""; }
            return "#rel\t" + b;
        }

        /// <summary>读这一批（**调用方必须已经拿着 BatchLock**）。`joinMs` 之前的批次（隔得太久）
        /// 当成"已经过去的那一批"，返回空 —— 不能把用户上一次复制的东西并进来。
        /// 模式头跟这次对不上（模式不同、或相对模式但基准不同）也返回空。</summary>
        private static List<string> ReadBatchLocked(int joinMs, bool relative, bool nameOnly, string baseDir)
        {
            List<string> list = new List<string>();
            try
            {
                if (!File.Exists(BatchFile)) { return list; }
                if (joinMs > 0)
                {
                    TimeSpan age = DateTime.UtcNow - File.GetLastWriteTimeUtc(BatchFile);
                    if (age.TotalMilliseconds > joinMs) { return list; }
                }
                string[] lines = File.ReadAllLines(BatchFile, Encoding.UTF8);
                if ((lines.Length == 0) || !lines[0].StartsWith("#")) { return list; }
                if (!string.Equals(lines[0].TrimEnd(), HeaderOf(relative, nameOnly, baseDir), StringComparison.OrdinalIgnoreCase))
                {
                    return list;    // 上一批是另一种模式 / 另一个基准：不并进来
                }
                for (int i = 1; i < lines.Length; i++)
                {
                    string s = lines[i].Trim();
                    if (s.Length > 0) { list.Add(s); }
                }
            }
            catch { }
            return list;
        }

        /// <summary>批次文件"安静"了多久（毫秒）。文件不在 = 很大（当它早就安静了）。</summary>
        private static double QuietAgeMs()
        {
            try
            {
                if (!File.Exists(BatchFile)) { return double.MaxValue; }
                return (DateTime.UtcNow - File.GetLastWriteTimeUtc(BatchFile)).TotalMilliseconds;
            }
            catch { return double.MaxValue; }
        }

        /// <summary>跨进程锁（同一批的几个进程会几乎同时读写批次文件）。等不到锁也不死等：
        /// 超时就当自己一个人（宁可少并一条，也不能卡住用户的右键）。
        /// **释放要显式 `ReleaseMutex`**：直接 Dispose 一个还持有身份的 Mutex，下一个等待者会收到
        /// `AbandonedMutexException`（虽然也能跑，但那是"上一个进程崩了"的语义，别混用）。</summary>
        private sealed class BatchLock : IDisposable
        {
            private readonly Mutex _m;
            private readonly bool _held;

            public BatchLock()
            {
                _m = new Mutex(false, MutexName);
                try { _held = _m.WaitOne(3000); }
                catch (AbandonedMutexException) { _held = true; }   // 上一个进程没释放就退出了：身份归我
                catch { _held = false; }
            }

            public void Dispose()
            {
                try
                {
                    if (_held) { _m.ReleaseMutex(); }
                }
                catch { }
                try { _m.Dispose(); } catch { }
            }
        }

        /// <summary>写剪贴板。**必须 copy=true**：我们下一秒就退出，不留在剪贴板里的话粘贴时是空的
        /// （`Clipboard.SetText` 之外再显式走 `SetDataObject`，这条以前在别的工具上踩过）。
        /// 失败重试 3 次（剪贴板是全局资源，别的程序正开着它很正常）。</summary>
        private static string WriteClipboard(string text)
        {
            string last = "";
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return "";
                }
                catch (Exception ex)
                {
                    last = ex.Message;
                    Thread.Sleep(150);
                }
            }
            return (last.Length > 0) ? last : "剪贴板打不开";
        }
    }
}
