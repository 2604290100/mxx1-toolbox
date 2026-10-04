// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>The user layer: %LOCALAPPDATA%\mxx1-toolbox\tools.json. Everything the graphical
    /// 新建按钮 / 编辑 / 删除 window writes goes through here, so the hand written manifest format
    /// and the generated one stay identical (and the file keeps working after a program update).
    ///
    /// The file is rewritten whole, so a copy of the previous version is kept next to it
    /// (tools.json.bak) -- a hand written comment or an exotic field would otherwise be lost.</summary>
    internal static class UserTools
    {
        public const string Comment = "萌新工具箱的用户按钮：图形化「新建按钮」写的就是这个文件。"
            + "同 id 覆盖内置按钮（tools\\*.json 里那份），升级不会冲掉。";

        /// <summary>User layer buttons (Source = tools.json（用户）) in file order.</summary>
        public static List<ToolItem> Collect(List<ToolItem> all)
        {
            List<ToolItem> list = new List<ToolItem>();
            foreach (ToolItem t in all)
            {
                if (t.UserLayer) { list.Add(t); }
            }
            list.Sort(CompareUser);
            return list;
        }

        /// <summary>Keeps the tab/segment/order the user sees, so an edit does not move buttons.</summary>
        private static int CompareUser(ToolItem a, ToolItem b)
        {
            int c = Tabs.Index(a.Tab).CompareTo(Tabs.Index(b.Tab));
            if (c != 0) { return c; }
            c = a.Segment.CompareTo(b.Segment);
            if (c != 0) { return c; }
            c = a.Order.CompareTo(b.Order);
            if (c != 0) { return c; }
            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Next free order inside a tab (so new buttons land at the end of the page).</summary>
        public static int NextOrder(List<ToolItem> mine, string tab)
        {
            int max = 0;
            foreach (ToolItem t in mine)
            {
                if (!string.Equals(t.Tab, tab, StringComparison.OrdinalIgnoreCase)) { continue; }
                if (t.Segment != 1) { continue; }
                if (t.Order > max) { max = t.Order; }
            }
            return max + 10;
        }

        /// <summary>A readable, unique id built from the button name ("记事本" -> mine.jishiben is
        /// not possible for Chinese, so the letters/digits are kept and a number is appended).</summary>
        public static string NextId(string name, List<ToolItem> mine, List<ToolItem> all)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in (name ?? ""))
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (c == ' ' || c == '-' || c == '_' || c == '.')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '-') { sb.Append('-'); }
                }
            }
            string baseId = sb.ToString().Trim('-');
            if (baseId.Length == 0) { baseId = "tool"; }
            if (baseId.Length > 24) { baseId = baseId.Substring(0, 24); }
            baseId = "mine." + baseId;

            string id = baseId;
            int n = 2;
            while (Exists(id, mine, all))
            {
                id = baseId + "-" + n.ToString(CultureInfo.InvariantCulture);
                n++;
            }
            return id;
        }

        private static bool Exists(string id, List<ToolItem> mine, List<ToolItem> all)
        {
            foreach (ToolItem t in mine) { if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) { return true; } }
            foreach (ToolItem t in all) { if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) { return true; } }
            return false;
        }

        // ---------------------------------------------------------------- 导出 / 导入

        /// <summary>把用户层原样写到一个文件里（换机器 / 重装之前备份）。只导用户自己建的按钮：
        /// 内置按钮跟着 exe 走，导出它们只会塞给用户一份巨大而且会过期的清单。</summary>
        public static string Export(string destination, List<ToolItem> mine)
        {
            if (destination.Length == 0) { return "没有指定导出文件"; }
            if (mine.Count == 0) { return "还没有自己建的按钮，没什么可导出的"; }
            try
            {
                string dir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }
                File.WriteAllText(destination, ToJson(mine), new UTF8Encoding(false));
                Logger.Write("导出按钮", mine.Count + " 个 → " + destination);
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>把另一个导出文件里的按钮并进用户层，按 id 去重（同 id 覆盖）。</summary>
        public static string Import(string source, List<ToolItem> mine, out int added, out int replaced)
        {
            added = 0;
            replaced = 0;
            if (!File.Exists(source)) { return "找不到这个文件：" + source; }
            try
            {
                object root = Json.Parse(File.ReadAllText(source, Encoding.UTF8));
                List<object> items = null;
                Dictionary<string, object> obj = Json.AsObject(root);
                if (obj != null) { items = Json.AsArray(Json.GetObject(obj, "tools")); }
                if (items == null) { items = Json.AsArray(root); }
                if (items == null) { return "这个文件里没有按钮清单（顶层既不是数组，也不是带 tools 的对象）"; }
                foreach (object entry in items)
                {
                    Dictionary<string, object> eo = Json.AsObject(entry);
                    if (eo == null) { continue; }
                    ToolItem t;
                    try { t = ToolItem.FromJson(eo, "导入"); }
                    catch (Exception ex) { return "有一个按钮读不出来：" + ex.Message; }
                    t.UserLayer = true;
                    int at = -1;
                    for (int i = 0; i < mine.Count; i++)
                    {
                        if (string.Equals(mine[i].Id, t.Id, StringComparison.OrdinalIgnoreCase)) { at = i; break; }
                    }
                    if (at >= 0) { mine[at] = t; replaced++; }
                    else { mine.Add(t); added++; }
                }
                Logger.Write("导入按钮", "新增 " + added + " 个、覆盖 " + replaced + " 个，来自 " + source);
                return "";
            }
            catch (Exception ex) { return "读不了这个文件：" + ex.Message; }
        }

        // ---------------------------------------------------------------- 置顶

        /// <summary>置顶的按钮 id，一行一个。单独放一个文件而不是塞进 settings.ini：置顶是一条一条
        /// 改的，ini 每次都要整份重写，写坏了会连主题设置一起丢。</summary>
        public static string PinnedFile { get { return Path.Combine(AppPaths.BaseDir, "pinned.txt"); } }

        public static List<string> LoadPinned()
        {
            List<string> list = new List<string>();
            try
            {
                if (!File.Exists(PinnedFile)) { return list; }
                foreach (string line in File.ReadAllLines(PinnedFile, Encoding.UTF8))
                {
                    string id = line.Trim();
                    if (id.Length > 0 && !id.StartsWith("#")) { list.Add(id); }
                }
            }
            catch { }
            return list;
        }

        public static bool IsPinned(string id, List<string> pinned)
        {
            return pinned.Exists(delegate(string s) { return string.Equals(s, id, StringComparison.OrdinalIgnoreCase); });
        }

        public static string SetPinned(string id, bool pinned)
        {
            if (id.Length == 0) { return "没有指定按钮"; }
            try
            {
                List<string> list = LoadPinned();
                list.RemoveAll(delegate(string s) { return string.Equals(s, id, StringComparison.OrdinalIgnoreCase); });
                if (pinned) { list.Insert(0, id); }
                AppPaths.EnsureBase();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 置顶的按钮 id（一行一个）。界面里右键按钮可以置顶 / 取消置顶。");
                foreach (string s in list) { sb.AppendLine(s); }
                File.WriteAllText(PinnedFile, sb.ToString(), new UTF8Encoding(false));
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ---------------------------------------------------------------- 最近用过

        /// <summary>最近点过的按钮 id，最新的在最前面（「常用」页签用它）。和 pinned.txt 一样
        /// 独立成一个文件：每次点按钮都会重写它，塞进 settings.ini 会连主题设置一起赔进去。
        /// 上限 2026-10-04 用户定成 30（原来是 12）—— 它同时决定右键「常用功能」子菜单里
        /// 「最近用过」那一段最多列几个（见 docs\DESIGN.md §14.3）。</summary>
        public const int RecentLimit = 30;

        public static string RecentFile { get { return Path.Combine(AppPaths.BaseDir, "recent.txt"); } }

        public static List<string> LoadRecent()
        {
            List<string> list = new List<string>();
            try
            {
                if (!File.Exists(RecentFile)) { return list; }
                foreach (string line in File.ReadAllLines(RecentFile, Encoding.UTF8))
                {
                    string id = line.Trim();
                    if (id.Length > 0 && !id.StartsWith("#") && !list.Contains(id)) { list.Add(id); }
                    if (list.Count >= RecentLimit) { break; }
                }
            }
            catch { }
            return list;
        }

        /// <summary>把一个按钮挪到最近使用的最前面。返回 "" 表示写成功。</summary>
        public static string PushRecent(string id)
        {
            if (id.Length == 0) { return ""; }
            try
            {
                List<string> list = LoadRecent();
                list.RemoveAll(delegate(string s) { return string.Equals(s, id, StringComparison.OrdinalIgnoreCase); });
                list.Insert(0, id);
                while (list.Count > RecentLimit) { list.RemoveAt(list.Count - 1); }
                AppPaths.EnsureBase();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 最近点过的按钮 id（一行一个，最新在最上）。「常用」页签用它，删掉这个文件就清空了。");
                foreach (string s in list) { sb.AppendLine(s); }
                File.WriteAllText(RecentFile, sb.ToString(), new UTF8Encoding(false));
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static string ClearRecent()
        {
            try
            {
                if (File.Exists(RecentFile)) { File.Delete(RecentFile); }
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Writes the whole user layer. Returns "" on success, otherwise the reason.</summary>
        public static string Save(List<ToolItem> mine)
        {
            try
            {
                AppPaths.EnsureBase();
                if (File.Exists(AppPaths.UserToolsJson))
                {
                    try { File.Copy(AppPaths.UserToolsJson, AppPaths.UserToolsJson + ".bak", true); }
                    catch { }
                }
                File.WriteAllText(AppPaths.UserToolsJson, ToJson(mine), new UTF8Encoding(false));
                Logger.Write("用户按钮", "已写入 " + AppPaths.UserToolsJson + "（" + mine.Count + " 个按钮）");
                return "";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Minimal writer for the manifest format (no dependency on a JSON library:
        /// the toolbox ships a parser only). Only meaningful fields are written.</summary>
        public static string ToJson(List<ToolItem> mine)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"_comment\": \"" + Escape(Comment) + "\",");
            sb.AppendLine("  \"tools\": [");
            for (int i = 0; i < mine.Count; i++)
            {
                ToolItem t = mine[i];
                StringBuilder line = new StringBuilder();
                line.Append("    {");
                // The first field must NOT carry the ", " separator, otherwise the file starts with
                // "{ , \"id\": ..." and the toolbox's own parser rejects it ("JSON 对象缺少键").
                // A live report from the user, 2026-10-04: their first graphically created button
                // was written with that stray comma and could not be loaded back.
                First(line, "id", t.Id);
                Field(line, "tab", t.Tab, true);
                Num(line, "segment", t.Segment, 1);
                Num(line, "order", t.Order, 0);
                Field(line, "name", t.Name, true);
                Field(line, "icon", t.Icon, false);
                Field(line, "kind", t.Kind, true);
                Field(line, "module", t.Module, false);
                Field(line, "action", t.Action, false);
                Field(line, "options", t.Options, false);
                Field(line, "path", t.Path, false);
                Field(line, "args", t.Args, false);
                Field(line, "workdir", t.WorkDir, false);
                Field(line, "target", t.Target, false);
                if (t.Kind == "script")
                {
                    Field(line, "shell", t.Shell, true);
                    Field(line, "inline", t.Inline, false);
                }
                Field(line, "hint", t.Hint, false);
                if (t.Wait) { line.Append(", \"wait\": true"); }
                if (t.RunAsAdmin) { line.Append(", \"runAsAdmin\": true"); }
                if (t.TimeoutSec > 0) { Num(line, "timeoutSec", t.TimeoutSec, 0); }
                if (t.Danger) { line.Append(", \"danger\": true"); }
                if (t.Confirm) { line.Append(", \"confirm\": true"); }
                if (t.Placeholder) { line.Append(", \"placeholder\": true"); }
                line.Append(" }");
                if (i < mine.Count - 1) { line.Append(","); }
                sb.AppendLine(line.ToString());
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void First(StringBuilder sb, string key, string value)
        {
            sb.Append(" \"").Append(key).Append("\": \"").Append(Escape(value)).Append("\"");
        }

        private static void Field(StringBuilder sb, string key, string value, bool always)
        {
            if (!always && string.IsNullOrEmpty(value)) { return; }
            sb.Append(", \"").Append(key).Append("\": \"").Append(Escape(value)).Append("\"");
        }

        private static void Num(StringBuilder sb, string key, int value, int def)
        {
            if (value == def) { return; }
            sb.Append(", \"").Append(key).Append("\": ").Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static string Escape(string s)
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
                        if (c < ' ') { sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); }
                        else { sb.Append(c); }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
