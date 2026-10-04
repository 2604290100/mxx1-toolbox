// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>Loads the button manifests: the embedded tools\*.json that ship inside the exe,
    /// then the user layer (%LOCALAPPDATA%\mxx1-toolbox\tools.json) which overrides by id.</summary>
    internal static class ToolRegistry
    {
        public static List<ToolItem> LoadAll(List<string> warnings)
        {
            Dictionary<string, ToolItem> map = new Dictionary<string, ToolItem>(StringComparer.OrdinalIgnoreCase);

            Assembly asm = Assembly.GetExecutingAssembly();
            List<string> names = new List<string>(asm.GetManifestResourceNames());
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                if (!name.StartsWith("tools.", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { continue; }
                try
                {
                    using (Stream s = asm.GetManifestResourceStream(name))
                    {
                        if (s == null) { continue; }
                        using (StreamReader sr = new StreamReader(s, Encoding.UTF8, true))
                        {
                            AddJson(map, sr.ReadToEnd(), name, warnings);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (warnings != null) { warnings.Add(name + ": " + ex.Message); }
                }
            }

            try
            {
                if (File.Exists(AppPaths.UserToolsJson))
                {
                    AddJson(map, File.ReadAllText(AppPaths.UserToolsJson, Encoding.UTF8),
                        "tools.json（用户）", warnings, true);
                }
            }
            catch (Exception ex)
            {
                if (warnings != null) { warnings.Add("用户 tools.json: " + ex.Message); }
            }

            // Tool folders last: a button a folder asks for never replaces one that already exists
            // (embedded manifest or the user layer), so dropping a folder in can only ever *add*
            // buttons. See ToolFolders for the whole rule set.
            try
            {
                foreach (ToolItem auto in ToolFolders.Scan(warnings))
                {
                    if (map.ContainsKey(auto.Id))
                    {
                        if (warnings != null)
                        {
                            warnings.Add(auto.Source + ": 按钮 id「" + auto.Id
                                + "」已经存在（内置清单或用户 tools.json），这一条自动按钮被跳过");
                        }
                        continue;
                    }
                    map[auto.Id] = auto;
                }
            }
            catch (Exception ex)
            {
                if (warnings != null) { warnings.Add("bin-tools 自动按钮: " + ex.Message); }
            }

            List<ToolItem> list = new List<ToolItem>(map.Values);
            list.Sort(Compare);
            return list;
        }

        /// <summary>Tab order, then segment, then the manifest order, then the name.</summary>
        internal static int Compare(ToolItem a, ToolItem b)
        {
            int c = Tabs.Index(a.Tab).CompareTo(Tabs.Index(b.Tab));
            if (c != 0) { return c; }
            c = a.Segment.CompareTo(b.Segment);
            if (c != 0) { return c; }
            c = a.Order.CompareTo(b.Order);
            if (c != 0) { return c; }
            c = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            if (c != 0) { return c; }
            return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        }

        /// <summary>Accepts a bare array, an object with a "tools" array, or a single object.</summary>
        private static void AddJson(Dictionary<string, ToolItem> map, string text, string source,
            List<string> warnings) { AddJson(map, text, source, warnings, false); }

        private static void AddJson(Dictionary<string, ToolItem> map, string text, string source,
            List<string> warnings, bool userLayer)
        {
            object root = Json.Parse(text);
            Dictionary<string, object> obj = Json.AsObject(root);
            List<object> items = null;
            if (obj != null) { items = Json.AsArray(Json.GetObject(obj, "tools")); }
            if (items == null) { items = Json.AsArray(root); }
            if (items == null && obj != null) { items = new List<object>(); items.Add(obj); }
            if (items == null)
            {
                if (warnings != null) { warnings.Add(source + ": 顶层既不是数组也不是对象"); }
                return;
            }
            foreach (object entry in items)
            {
                Dictionary<string, object> eo = Json.AsObject(entry);
                if (eo == null)
                {
                    if (warnings != null) { warnings.Add(source + ": 数组里有一项不是对象"); }
                    continue;
                }
                try
                {
                    ToolItem item = ToolItem.FromJson(eo, source);
                    item.UserLayer = userLayer;
                    map[item.Id] = item;
                }
                catch (Exception ex)
                {
                    if (warnings != null) { warnings.Add(source + ": " + ex.Message); }
                }
            }
        }
    }
}
