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
