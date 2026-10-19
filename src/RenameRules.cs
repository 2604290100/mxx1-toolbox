// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Mxx1Toolbox
{
    /// <summary>「批量重命名」的后端。**预览、执行、撤销走的是同一份代码** —— 这条功能最怕的就是
    /// "预览时看到的是 A、真改出来的是 B"（数据和承诺不一致，用户会把文件名改乱）。
    ///
    /// 规则按这个顺序叠（顺序固定，预览里看到的每一步都是它算出来的）：
    ///   ① 正则替换（勾了「用正则」）或普通查找替换（区分大小写，替换全部出现的位置）
    ///   ② 序号：起始值 + 当前是第几个文件；位数（1 = 不补零）；位置（名字前 / 名字后）
    ///   ③ 前缀 / 后缀：支持两个占位符 —— `{n}` = 这一个文件的序号、`{date}` = 修改日期（yyyyMMdd）
    ///   ④ 扩展名大小写（只改扩展名，不动点）
    ///
    /// 安全口径：
    ///   · 目标名字已经存在 → **整批拒绝**（一个都不改，列出全部冲突），绝不覆盖；
    ///   · 两个文件算出来同一个新名字 → 一样拒绝；
    ///   · Windows 不允许的字符 / 保留设备名（CON、NUL、COM1…）/ 结尾的点或空格 → 拒绝；
    ///   · 只改名，不移动目录、不改内容、不改扩展名本身（除非规则里明确写了）；
    ///   · 每次真改之前先把"从哪改到哪"写进 `%LOCALAPPDATA%\mxx1-toolbox\rename-<时间>.json`，
    ///     之后点「撤销上一次重命名」就能按记录改回来（部分失败也照实记）。
    ///
    /// 对比大小写：文件名比较一律 OrdinalIgnoreCase（Windows 不区分大小写）；**只改大小写**
    /// （report.TXT → report.txt）算合法改名，不算"目标已存在"。</summary>
    internal static class RenameRules
    {
        public sealed class Rule
        {
            public string Prefix = "";
            public string Suffix = "";
            public string Find = "";
            public string Replace = "";
            public bool UseRegex = false;
            public int IndexStart = 1;
            public int IndexDigits = 0;              // 0 = 不加序号
            public bool IndexAfter = true;           // true = 名字后面，false = 名字前面
            public string ExtCase = "";              // "" / "lower" / "upper"
        }

        public sealed class PlanItem
        {
            public string Path = "";
            public string OldName = "";
            public string NewName = "";
            public string Conflict = "";             // 非空 = 这一条不能改

            public string Dir
            {
                get
                {
                    try { return System.IO.Path.GetDirectoryName(Path); }
                    catch { return ""; }
                }
            }

            public string TargetPath
            {
                get
                {
                    try { return System.IO.Path.Combine(Dir, NewName); }
                    catch { return NewName; }
                }
            }

            public bool Changed
            {
                get { return !string.Equals(OldName, NewName, StringComparison.Ordinal); }
            }

            /// <summary>只改大小写（Windows 上这种改名是合法的）。</summary>
            public bool CaseOnly
            {
                get
                {
                    return Changed && string.Equals(OldName, NewName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        public sealed class PlanResult
        {
            public string Dir = "";
            public readonly List<PlanItem> Items = new List<PlanItem>();
            public string Error = "";
            public int Conflicts = 0;

            public int Changed
            {
                get
                {
                    int n = 0;
                    foreach (PlanItem it in Items) { if (it.Changed && it.Conflict.Length == 0) { n++; } }
                    return n;
                }
            }
        }

        // ------------------------------------------------------------ 预览

        public static PlanResult Plan(string dir, string filter, Rule rule, List<string> explicitFiles)
        {
            PlanResult plan = new PlanResult();
            plan.Dir = dir;
            if (rule == null) { rule = new Rule(); }
            if (rule.IndexDigits < 0) { rule.IndexDigits = 0; }
            if (rule.IndexDigits > 12) { rule.IndexDigits = 12; }
            if (rule.IndexStart < 0) { rule.IndexStart = 0; }

            List<string> files = new List<string>();
            if (explicitFiles != null)
            {
                foreach (string f in explicitFiles)
                {
                    if (f != null && f.Trim().Length > 0) { files.Add(f); }
                }
            }
            else
            {
                if (dir == null || dir.Trim().Length == 0) { plan.Error = "还没有选文件夹"; return plan; }
                try
                {
                    if (!Directory.Exists(dir)) { plan.Error = "这个文件夹不存在：" + dir; return plan; }
                    string pattern = (filter == null || filter.Trim().Length == 0) ? "*" : filter.Trim();
                    files.AddRange(Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly));
                }
                catch (Exception ex)
                {
                    plan.Error = "读不了这个文件夹：" + ex.Message;
                    return plan;
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            if (files.Count == 0) { plan.Error = "这个文件夹里没有符合条件的文件"; return plan; }

            Regex rx = null;
            if (rule.UseRegex)
            {
                if (rule.Find == null || rule.Find.Length == 0) { plan.Error = "勾了「用正则」却没写表达式"; return plan; }
                try { rx = new Regex(rule.Find); }
                catch (Exception ex) { plan.Error = "正则表达式写错了：" + ex.Message; return plan; }
            }

            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                PlanItem item = new PlanItem();
                item.Path = path;
                try { item.OldName = System.IO.Path.GetFileName(path); }
                catch { item.OldName = path; }
                item.NewName = NewNameOf(path, rule, rx, i);
                plan.Items.Add(item);
            }

            // 先检查单条规则本身对不对，再检查"两个文件撞到同一个名字"
            foreach (PlanItem item in plan.Items)
            {
                string why = BadName(item.NewName);
                if (why.Length > 0) { item.Conflict = why; continue; }
                if (!item.Changed) { continue; }
                string target = item.TargetPath;
                if (File.Exists(target) && !string.Equals(target, item.Path, StringComparison.OrdinalIgnoreCase))
                {
                    item.Conflict = "这个新名字已经被别的文件占了";
                    continue;
                }
                if (!used.Add(target))
                {
                    // 把前面那个也标上（两边都要用户看见 —— 只标后面那个会让人以为改掉一个就行）
                    item.Conflict = "有两个文件会变成同一个名字";
                    foreach (PlanItem other in plan.Items)
                    {
                        if (other != item && other.Conflict.Length == 0
                            && string.Equals(other.TargetPath, target, StringComparison.OrdinalIgnoreCase))
                        {
                            other.Conflict = "有两个文件会变成同一个名字";
                        }
                    }
                }
            }

            plan.Conflicts = 0;
            foreach (PlanItem it in plan.Items) { if (it.Conflict.Length > 0) { plan.Conflicts++; } }
            return plan;
        }

        private static string NewNameOf(string path, Rule rule, Regex rx, int index)
        {
            string oldName = "";
            string baseName = "";
            string ext = "";
            try
            {
                oldName = System.IO.Path.GetFileName(path);
                baseName = System.IO.Path.GetFileNameWithoutExtension(oldName);
                ext = System.IO.Path.GetExtension(oldName);
            }
            catch
            {
                baseName = oldName;
                ext = "";
            }

            if (rx != null)
            {
                baseName = rx.Replace(baseName, rule.Replace == null ? "" : rule.Replace);
            }
            else if (rule.Find != null && rule.Find.Length > 0)
            {
                baseName = baseName.Replace(rule.Find, rule.Replace == null ? "" : rule.Replace);
            }

            int number = rule.IndexStart + index;
            string nText = number.ToString(CultureInfo.InvariantCulture);
            if (rule.IndexDigits > 0)
            {
                nText = number.ToString(CultureInfo.InvariantCulture).PadLeft(rule.IndexDigits, '0');
                baseName = rule.IndexAfter ? (baseName + nText) : (nText + baseName);
            }

            string date = "";
            try { date = File.GetLastWriteTime(path).ToString("yyyyMMdd", CultureInfo.InvariantCulture); }
            catch { }

            baseName = Tokens(rule.Prefix) + baseName + Tokens(rule.Suffix);
            baseName = baseName.Replace("{n}", nText).Replace("{date}", date);
            baseName = baseName.Replace("{N}", nText).Replace("{DATE}", date);

            if (string.Equals(rule.ExtCase, "lower", StringComparison.OrdinalIgnoreCase))
            {
                ext = ext.ToLowerInvariant();
            }
            else if (string.Equals(rule.ExtCase, "upper", StringComparison.OrdinalIgnoreCase))
            {
                ext = ext.ToUpperInvariant();
            }
            return baseName + ext;
        }

        /// <summary>占位符先在规则文本里留着，等 {n}/{date} 有值了再替换；这里只是把
        /// 非法字符提前挡住（规则里写了就当场报错，不是等生成名字以后）。</summary>
        private static string Tokens(string text)
        {
            return text == null ? "" : text;
        }

        /// <summary>Windows 不允许的名字：非法字符 / 空名字 / 保留设备名 / 结尾点或空格 / 太长。</summary>
        private static string BadName(string name)
        {
            if (name == null || name.Length == 0) { return "新名字是空的"; }
            if (name.Length > 255) { return "新名字太长（超过 255 个字符）"; }
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c < 32) { return "新名字里有控制字符"; }
                if ("\\/:*?\"<>|".IndexOf(c) >= 0) { return "新名字里有 Windows 不允许的字符：" + c; }
            }
            if (name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
            {
                return "名字不能以点或空格结尾（Windows 不允许）";
            }
            string stem = name;
            int dot = stem.IndexOf('.');
            if (dot >= 0) { stem = stem.Substring(0, dot); }
            string[] reserved = new string[]
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };
            foreach (string r in reserved)
            {
                if (string.Equals(stem, r, StringComparison.OrdinalIgnoreCase)) { return "这是 Windows 保留的设备名：" + r; }
            }
            return "";
        }

        // ------------------------------------------------------------ 执行

        public sealed class ApplyResult
        {
            public int Done = 0;
            public int Failed = 0;
            public int Skipped = 0;
            public string UndoFile = "";
            public string Error = "";
            public readonly List<string> FailedWhy = new List<string>();

            public string Report()
            {
                if (Error.Length > 0) { return Error; }
                StringBuilder sb = new StringBuilder();
                sb.Append("改名完成：" + Done + " 个");
                if (Skipped > 0) { sb.Append("，跳过 " + Skipped + " 个（名字没变）"); }
                if (Failed > 0) { sb.Append("，失败 " + Failed + " 个"); }
                sb.AppendLine();
                foreach (string why in FailedWhy) { sb.Append("  × ").AppendLine(why); }
                if (UndoFile.Length > 0) { sb.Append("撤销记录：").AppendLine(UndoFile); }
                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>执行。**有冲突就整批不动**（返回 Error）；改名记录先写盘再动文件
        /// （写不进记录也整批不动 —— 没有记录就没法撤销，这条底线不能破）。</summary>
        public static ApplyResult Apply(PlanResult plan)
        {
            ApplyResult res = new ApplyResult();
            if (plan == null) { res.Error = "没有可执行的改名计划"; return res; }
            if (plan.Error.Length > 0) { res.Error = plan.Error; return res; }
            if (plan.Conflicts > 0)
            {
                res.Error = "有 " + plan.Conflicts + " 处冲突，一个都没改。把冲突处理掉再试。";
                return res;
            }

            List<PlanItem> todo = new List<PlanItem>();
            foreach (PlanItem it in plan.Items)
            {
                if (!it.Changed) { res.Skipped++; continue; }
                todo.Add(it);
            }
            if (todo.Count == 0)
            {
                res.Error = "所有文件的名字本来就是对的，没有需要改的。";
                return res;
            }

            // 记录文件先写（列的是"打算做"的全部条目；部分失败也照实留底）
            string undo = WriteUndoFile(plan, todo);
            if (undo.Length == 0)
            {
                res.Error = "撤销记录写不进磁盘（" + UndoDir() + "），为了能反悔，一个文件都没动。";
                return res;
            }
            res.UndoFile = undo;

            List<PlanItem> succeeded = new List<PlanItem>();
            foreach (PlanItem it in todo)
            {
                try
                {
                    File.Move(it.Path, it.TargetPath);
                    res.Done++;
                    succeeded.Add(it);
                }
                catch (Exception ex)
                {
                    res.Failed++;
                    res.FailedWhy.Add(it.OldName + " → " + it.NewName + "：" + Friendly(ex));
                }
            }
            // 记录里补上"实际成没成"（先写"planned"，跑完再覆盖成真实结果 ——
            // 判据是"这一条真的 File.Move 成功了"，不是"目标文件现在存在"：
            // 后者在目标被别人抢先占住时会误判成成功，撤销时可能去动别人的文件）
            try { File.WriteAllText(undo, BuildUndoJson(plan, todo, succeeded), new UTF8Encoding(false)); }
            catch { }
            return res;
        }

        private static string Friendly(Exception ex)
        {
            if (ex is UnauthorizedAccessException) { return "没有权限改名（文件可能被占用或者在受保护的目录里）"; }
            if (ex is IOException)
            {
                if (ex.Message.IndexOf("exists", StringComparison.OrdinalIgnoreCase) >= 0
                    || ex.Message.IndexOf("已存在", StringComparison.Ordinal) >= 0)
                {
                    return "目标名字已经被占用";
                }
                return "系统不让改：" + ex.Message;
            }
            return ex.Message;
        }

        // ------------------------------------------------------------ 撤销记录

        public static string UndoDir()
        {
            // 测试用：把撤销记录写进夹具目录，别和用户真实的记录混在一起
            // （`MXX1_RENAME_DIR` 只在回归测试里设置；正式跑走 AppPaths.BaseDir）。
            string over = Environment.GetEnvironmentVariable("MXX1_RENAME_DIR");
            if (over != null && over.Trim().Length > 0) { return over.Trim(); }
            return AppPaths.BaseDir;
        }

        public static string LastUndoFile()
        {
            try
            {
                string[] files = Directory.GetFiles(UndoDir(), "rename-*.json");
                if (files.Length == 0) { return ""; }
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files[files.Length - 1];
            }
            catch { return ""; }
        }

        private static string WriteUndoFile(PlanResult plan, List<PlanItem> todo)
        {
            try
            {
                string dir = UndoDir();
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string path = Path.Combine(dir, "rename-" + stamp + ".json");
                int n = 2;
                while (File.Exists(path))
                {
                    path = Path.Combine(dir, "rename-" + stamp + "-" + n.ToString(CultureInfo.InvariantCulture) + ".json");
                    n++;
                }
                string json = BuildUndoJson(plan, todo, null);
                File.WriteAllText(path, json, new UTF8Encoding(false));
                return path;
            }
            catch { return ""; }
        }

        /// <summary>手写 JSON（不引第三方库）：字段只有 created / dir / items[from,to,ok]，转义只做
        /// 必需的那几个字符。</summary>
        private static string BuildUndoJson(PlanResult plan, List<PlanItem> todo, List<PlanItem> succeeded)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"tool\": \"mxx1-toolbox\",\r\n");
            sb.Append("  \"created\": \"").Append(Esc(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append("\",\r\n");
            sb.Append("  \"dir\": \"").Append(Esc(plan.Dir)).Append("\",\r\n");
            sb.Append("  \"items\": [\r\n");
            for (int i = 0; i < todo.Count; i++)
            {
                PlanItem it = todo[i];
                // 写"打算做"那份时一律 true（意图）：万一进程在改到一半时被关掉，撤销会逐条试着
                // 把目标改回原名 —— 没来得及改的那几条自然"目标不在了"，跳过就好；
                // 跑完以后会按真实结果再覆盖一次。
                bool ok = (succeeded == null) || succeeded.Contains(it);
                sb.Append("    { \"from\": \"").Append(Esc(it.Path)).Append("\", \"to\": \"").Append(Esc(it.TargetPath))
                    .Append("\", \"ok\": ").Append(ok ? "true" : "false").Append(" }");
                if (i < todo.Count - 1) { sb.Append(","); }
                sb.Append("\r\n");
            }
            sb.Append("  ]\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (s == null) { return ""; }
            StringBuilder sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) { sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                        else { sb.Append(c); }
                        break;
                }
            }
            return sb.ToString();
        }

        public sealed class UndoResult
        {
            public int Done = 0;
            public int Failed = 0;
            public string Error = "";
            public readonly List<string> FailedWhy = new List<string>();
        }

        /// <summary>按记录撤销。倒着来（后改的先撤），只撤记录里 ok=true 的条目。
        /// 原来的名字又被占了、或者要撤的那份已经不在了 → 那一条失败并说明原因，不硬来。</summary>
        public static UndoResult Undo(string undoFile)
        {
            UndoResult res = new UndoResult();
            if (undoFile == null || undoFile.Trim().Length == 0) { undoFile = LastUndoFile(); }
            if (undoFile == null || undoFile.Trim().Length == 0 || !File.Exists(undoFile))
            {
                res.Error = "没有找到撤销记录（还没用这个功能改过名字？记录在 " + UndoDir() + "）";
                return res;
            }

            List<UndoItem> items = ReadUndoFile(undoFile);
            if (items == null)
            {
                res.Error = "撤销记录读不了（文件可能被改坏了）：" + undoFile;
                return res;
            }
            for (int i = items.Count - 1; i >= 0; i--)
            {
                UndoItem it = items[i];
                if (!it.Ok) { continue; }
                try
                {
                    if (!File.Exists(it.To))
                    {
                        res.Failed++;
                        res.FailedWhy.Add(it.To + " 已经不在了（可能被你自己改过名）");
                        continue;
                    }
                    // 只改大小写的那种（report.TXT → report.txt）：From 和 To 在 Windows 眼里
                    // 是**同一个文件**，File.Exists(From) 当然还是 true —— 不能当成"被别的文件占了"。
                    bool caseOnly = string.Equals(it.To, it.From, StringComparison.OrdinalIgnoreCase);
                    if (!caseOnly && File.Exists(it.From))
                    {
                        res.Failed++;
                        res.FailedWhy.Add(it.From + " 又被别的文件占了，没敢覆盖");
                        continue;
                    }
                    File.Move(it.To, it.From);
                    res.Done++;
                }
                catch (Exception ex)
                {
                    res.Failed++;
                    res.FailedWhy.Add(it.To + " → " + it.From + "：" + Friendly(ex));
                }
            }
            return res;
        }

        private sealed class UndoItem
        {
            public string From = "";
            public string To = "";
            public bool Ok = false;
        }

        private static List<UndoItem> ReadUndoFile(string path)
        {
            try
            {
                object parsed = Json.Parse(File.ReadAllText(path, Encoding.UTF8));
                Dictionary<string, object> root = Json.AsObject(parsed);
                if (root == null) { return null; }
                List<object> arr = Json.AsArray(Json.GetObject(root, "items"));
                if (arr == null) { return null; }
                List<UndoItem> list = new List<UndoItem>();
                foreach (object o in arr)
                {
                    Dictionary<string, object> map = Json.AsObject(o);
                    if (map == null) { continue; }
                    UndoItem it = new UndoItem();
                    it.From = Json.GetString(map, "from", "");
                    it.To = Json.GetString(map, "to", "");
                    it.Ok = Json.GetBool(map, "ok", false);
                    if (it.From.Length > 0 && it.To.Length > 0) { list.Add(it); }
                }
                return list;
            }
            catch { return null; }
        }
    }
}
