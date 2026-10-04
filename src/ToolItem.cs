// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.IO;

namespace Mxx1Toolbox
{
    /// <summary>One button in the wall. Everything about it comes from a tools\*.json manifest.</summary>
    internal sealed class ToolItem
    {
        public string Id = "";
        public string Tab = Tabs.Mine;
        public int Segment = 1;
        public int Order = 0;
        public string Name = "-";
        public string Icon = "";          // optional explicit path; default = assets\icons\<id>.png

        public string Kind = "builtin";   // builtin | exe | script | open | macro
        public string Module = "";        // builtin: permdel | app
        public string Action = "";        // builtin: install / uninstall / status / verify / ...
        public string Options = "";       // builtin: extra command line (e.g. --quiet)

        public string Path = "";          // exe / script
        public string Args = "";
        public string WorkDir = "";
        public bool Wait = false;
        public bool RunAsAdmin = false;

        public string Target = "";        // open: url / folder / ms-settings:
        public string Shell = "powershell"; // script: powershell | cmd
        public string Inline = "";        // script: inline body
        public int TimeoutSec = 0;

        public bool Danger = false;
        public bool Confirm = false;
        public bool Pinned = false;
        public bool Placeholder = false;
        public bool Hidden = false;
        public string Hint = "";
        public string Source = "";        // manifest the button came from (troubleshooting)

        public string IconPath
        {
            get
            {
                if (!string.IsNullOrEmpty(Icon)) { return AppPaths.Expand(Icon); }
                return System.IO.Path.Combine(AppPaths.IconsDir, Id + ".png");
            }
        }

        /// <summary>Text shown by `list` and in the run log.</summary>
        public string Describe()
        {
            string kind = Kind;
            if (Kind == "builtin") { kind = "builtin:" + Module + "/" + Action; }
            return Id + "\t" + Tabs.Display(Tab) + "\t" + Name + "\t" + kind
                + (Placeholder ? "\tplaceholder" : "")
                + (Danger ? "\tdanger" : "");
        }

        public static ToolItem FromJson(Dictionary<string, object> o, string source)
        {
            ToolItem t = new ToolItem();
            t.Source = source;
            t.Id = Json.GetString(o, "id", "");
            t.Tab = NormalizeTab(Json.GetString(o, "tab", Tabs.Mine));
            t.Segment = Json.GetInt(o, "segment", 1);
            t.Order = Json.GetInt(o, "order", 0);
            t.Name = Json.GetString(o, "name", t.Id);
            t.Icon = Json.GetString(o, "icon", "");

            t.Kind = Json.GetString(o, "kind", "builtin").Trim().ToLowerInvariant();
            t.Module = Json.GetString(o, "module", "");
            t.Action = Json.GetString(o, "action", "");
            t.Options = Json.GetString(o, "options", "");

            t.Path = Json.GetString(o, "path", "");
            t.Args = Json.GetString(o, "args", "");
            t.WorkDir = Json.GetString(o, "workdir", "");
            t.Wait = Json.GetBool(o, "wait", false);
            t.RunAsAdmin = Json.GetBool(o, "runAsAdmin", false);

            t.Target = Json.GetString(o, "target", "");
            t.Shell = Json.GetString(o, "shell", "powershell").Trim().ToLowerInvariant();
            t.Inline = Json.GetString(o, "inline", "");
            t.TimeoutSec = Json.GetInt(o, "timeoutSec", 0);

            t.Danger = Json.GetBool(o, "danger", false);
            t.Confirm = Json.GetBool(o, "confirm", t.Danger);
            t.Pinned = Json.GetBool(o, "pinned", false);
            t.Placeholder = Json.GetBool(o, "placeholder", false);
            t.Hidden = Json.GetBool(o, "hidden", false);
            t.Hint = Json.GetString(o, "hint", "");

            if (t.Id.Length == 0) { throw new FormatException("按钮缺少 id（来自 " + source + "）"); }
            return t;
        }

        public static string NormalizeTab(string tab)
        {
            string t = (tab ?? "").Trim();
            if (t.Length == 0) { return Tabs.Mine; }
            foreach (string id in Tabs.Ids)
            {
                if (string.Equals(id, t, StringComparison.OrdinalIgnoreCase)) { return id; }
                if (string.Equals(Tabs.Display(id), t, StringComparison.OrdinalIgnoreCase)) { return id; }
            }
            return Tabs.Mine;
        }
    }

    /// <summary>The five tabs, in display order.</summary>
    internal static class Tabs
    {
        public const string Common = "common";
        public const string RightMenu = "rightmenu";
        public const string Cleanup = "cleanup";
        public const string System = "system";
        public const string Mine = "mine";

        public static readonly string[] Ids = new string[] { Common, RightMenu, Cleanup, System, Mine };

        public static string Display(string id)
        {
            switch (id)
            {
                case Common: return "常用设置";
                case RightMenu: return "右键增强";
                case Cleanup: return "清理优化";
                case System: return "系统工具";
                case Mine: return "我的工具";
            }
            return id;
        }

        public static int Index(string id)
        {
            for (int i = 0; i < Ids.Length; i++)
            {
                if (string.Equals(Ids[i], id, StringComparison.OrdinalIgnoreCase)) { return i; }
            }
            return Ids.Length;
        }
    }
}
