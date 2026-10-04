// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>Buttons that come from the tool folder itself.
    ///
    /// Every direct sub folder of bin-tools\ is scanned, in this order of preference:
    ///   1. bin-tools\&lt;工具&gt;\tool.json -- the manifest this folder ships with. It takes the same
    ///      fields as tools\*.json (a single object, an array, or {"tools":[...]}), so a tool vendor
    ///      can hand out "one folder = one button" without anyone editing the toolbox.
    ///   2. no manifest, exactly one exe in the folder -- a button is made anyway, pointing at it.
    ///      A folder whose name matches the exe ("memreduct\memreduct.exe") wins over a folder with
    ///      several exes; with several and no match the folder is **skipped with a warning** rather
    ///      than guessing which of them the user meant.
    ///
    /// Deliberate limits (2026-10-04, when the user asked "bin-tools 里面的工具是不是应该自动加载
    /// 一个按钮？"):
    ///   * auto buttons never override anything: an id that already exists (embedded manifest or the
    ///     user layer) wins and the folder's button is skipped with a warning -- dropping a folder in
    ///     must not be able to silently replace 「关闭实时防护」 with something else;
    ///   * they are not editable through the right-click menu (there is nothing in the user layer to
    ///     edit) -- change the folder's tool.json instead;
    ///   * a broken tool.json is reported and skipped, it never takes the other buttons down with it;
    ///   * the folder is only read, never written to.
    /// </summary>
    internal static class ToolFolders
    {
        /// <summary>Name of the per-tool manifest inside a tool folder.</summary>
        public const string ManifestName = "tool.json";

        /// <summary>Where an auto button lands when its manifest does not say: the 「我的工具」 tab,
        /// below the built-in buttons, in its own labelled segment so it is obvious why it is there.</summary>
        private const string DefaultSegmentName = "bin-tools 里的工具（自动加载）";

        /// <summary>Scans the tool folders next to the exe and in the per user fallback folder.</summary>
        public static List<ToolItem> Scan(List<string> warnings)
        {
            List<ToolItem> found = new List<ToolItem>();
            List<string> visited = new List<string>();
            ScanRoot(AppPaths.PayloadDir, AppPaths.PayloadDirName, found, warnings, visited);
            ScanRoot(AppPaths.UserPayloadDir, "用户目录 " + AppPaths.PayloadDirName, found, warnings, visited);
            return found;
        }

        /// <summary>Human readable origin, used as ToolItem.Source (shown by 「查看定义」).</summary>
        private static string Label(string folder)
        {
            return AppPaths.PayloadDirName + "\\" + Path.GetFileName(folder);
        }

        private static void Warn(List<string> warnings, string message)
        {
            if (warnings != null) { warnings.Add(message); }
        }

        private static void ScanRoot(string dir, string label, List<ToolItem> found,
            List<string> warnings, List<string> visited)
        {
            string[] subs;
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { return; }
                string full = Path.GetFullPath(dir);
                foreach (string v in visited)
                {
                    if (string.Equals(v, full, StringComparison.OrdinalIgnoreCase)) { return; }
                }
                visited.Add(full);
                subs = Directory.GetDirectories(dir);
            }
            catch (Exception ex)
            {
                Warn(warnings, label + ": " + ex.Message);
                return;
            }
            Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
            foreach (string sub in subs)
            {
                try { ScanFolder(sub, found, warnings); }
                catch (Exception ex)
                {
                    // One bad folder must never cost the user the other buttons.
                    Warn(warnings, Label(sub) + ": " + ex.Message);
                }
            }
        }

        private static void ScanFolder(string folder, List<ToolItem> found, List<string> warnings)
        {
            string name = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(name) || name.StartsWith(".")) { return; }

            string manifest = Path.Combine(folder, ManifestName);
            string origin;
            if (File.Exists(manifest))
            {
                origin = Label(folder) + "\\" + ManifestName;
                string text;
                try { text = File.ReadAllText(manifest, Encoding.UTF8); }
                catch (Exception ex)
                {
                    Warn(warnings, origin + ": " + ex.Message);
                    return;
                }
                List<object> entries = Entries(text, origin, warnings);
                if (entries == null) { return; }
                foreach (object entry in entries)
                {
                    Dictionary<string, object> eo = Json.AsObject(entry);
                    if (eo == null)
                    {
                        Warn(warnings, origin + ": 数组里有一项不是对象");
                        continue;
                    }
                    ToolItem t = Make(eo, folder, origin, warnings);
                    if (t != null) { found.Add(t); }
                }
                return;
            }

            // No manifest: a folder holding exactly one exe still deserves a button.
            origin = Label(folder) + "（没有 " + ManifestName + "，按文件夹里的 exe 自动生成）";
            string rel = FindExe(folder, name);
            if (rel == null) { return; }
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["name"] = name;
            o["kind"] = "exe";
            o["path"] = name + "\\" + rel;
            o["workdir"] = name;
            ToolItem auto = Make(o, folder, origin, warnings);
            if (auto != null) { found.Add(auto); }
        }

        /// <summary>Accepts a bare array, an object with a "tools" array, or a single object --
        /// the same three shapes tools\*.json accepts.</summary>
        private static List<object> Entries(string text, string origin, List<string> warnings)
        {
            object root = Json.Parse(text);
            Dictionary<string, object> obj = Json.AsObject(root);
            List<object> items = null;
            if (obj != null) { items = Json.AsArray(Json.GetObject(obj, "tools")); }
            if (items == null) { items = Json.AsArray(root); }
            if (items == null && obj != null) { items = new List<object>(); items.Add(obj); }
            if (items == null) { Warn(warnings, origin + ": 顶层既不是数组也不是对象"); }
            return items;
        }

        /// <summary>Turns one manifest entry into a button, filling in what a tool folder can know
        /// about itself. Returns null (with a warning) when the entry cannot be used.</summary>
        private static ToolItem Make(Dictionary<string, object> o, string folder, string origin,
            List<string> warnings)
        {
            string name = Path.GetFileName(folder);
            if (Json.GetString(o, "id", "").Trim().Length == 0) { o["id"] = "auto." + name; }
            if (!o.ContainsKey("tab")) { o["tab"] = Tabs.Mine; }
            if (Json.GetString(o, "name", "").Trim().Length == 0) { o["name"] = name; }
            if (!o.ContainsKey("segment")) { o["segment"] = 2; }
            if (!o.ContainsKey("segmentName")) { o["segmentName"] = DefaultSegmentName; }
            if (!o.ContainsKey("order")) { o["order"] = 40; }

            string kind = Json.GetString(o, "kind", "").Trim().ToLowerInvariant();
            if (kind.Length == 0)
            {
                // A manifest that only names the tool means "run the exe in this folder".
                kind = "exe";
                o["kind"] = kind;
            }
            if (kind == "exe")
            {
                string path = Json.GetString(o, "path", "").Trim();
                if (path.Length == 0)
                {
                    string rel = FindExe(folder, name);
                    if (rel == null)
                    {
                        Warn(warnings, origin + ": kind=exe 但没写 path，而且这个文件夹里没有唯一的一个 exe");
                        return null;
                    }
                    o["path"] = name + "\\" + rel;
                    path = (string)o["path"];
                }
                if (Json.GetString(o, "workdir", "").Trim().Length == 0) { o["workdir"] = name; }
                if (Json.GetString(o, "icon", "").Trim().Length == 0)
                {
                    string png = FindIcon(folder, Path.GetFileNameWithoutExtension(path));
                    if (png != null) { o["icon"] = name + "\\" + png; }
                }
            }

            ToolItem item = ToolItem.FromJson(o, origin);
            item.AutoLayer = true;
            return item;
        }

        /// <summary>The exe a folder wants to run, as a path relative to the tool folder.
        /// &lt;文件夹&gt;.exe wins; a single candidate wins by default; anything else is ambiguous
        /// and returns null (the caller reports it instead of picking one at random).</summary>
        private static string FindExe(string folder, string folderName)
        {
            List<string> hits = new List<string>();
            CollectExe(folder, "", hits, 1);
            if (hits.Count == 0) { return null; }
            foreach (string h in hits)
            {
                if (string.Equals(Path.GetFileName(h), folderName + ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return h;
                }
            }
            return hits.Count == 1 ? hits[0] : null;
        }

        /// <summary>Note files are skipped: a folder holding the portable package plus a 说明.txt /
        /// Readme.txt is the normal case, not an ambiguity.</summary>
        private static void CollectExe(string dir, string prefix, List<string> hits, int depth)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.exe"))
                {
                    hits.Add(prefix + Path.GetFileName(f));
                }
            }
            catch (Exception) { }
            if (depth <= 0) { return; }
            try
            {
                // Portable packages often keep the real binary one level down (32\ / 64\ / bin\).
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    string leaf = Path.GetFileName(sub);
                    if (leaf.StartsWith(".")) { continue; }
                    CollectExe(sub, prefix + leaf + "\\", hits, depth - 1);
                }
            }
            catch (Exception) { }
        }

        /// <summary>A PNG next to the exe is used as the button icon (a portable package usually
        /// ships one). Only the exact names that mean "this program" are accepted -- the first PNG
        /// in the folder would otherwise be picked up (a screenshot, a banner...).</summary>
        private static string FindIcon(string folder, string exeBase)
        {
            string[] wanted = new string[] { exeBase + ".png", "icon.png", Path.GetFileName(folder) + ".png" };
            foreach (string w in wanted)
            {
                try
                {
                    if (File.Exists(Path.Combine(folder, w))) { return w; }
                }
                catch (Exception) { }
            }
            try
            {
                foreach (string sub in Directory.GetDirectories(folder))
                {
                    string leaf = Path.GetFileName(sub);
                    if (leaf.StartsWith(".")) { continue; }
                    foreach (string w in wanted)
                    {
                        try
                        {
                            if (File.Exists(Path.Combine(sub, w))) { return leaf + "\\" + w; }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}
