// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「无效快捷方式清理」窗口：扫桌面 + 开始菜单里的 .lnk，列出目标已经不在的那些。
    ///
    /// 界面上的三条口径（和后端 `src\Shortcuts.cs` 一一对应）：
    /// ① **只有"坏链"能勾、能删**；网络位置 / 可移动盘 / 商店应用也在列表里，但勾选框点不动
    ///    （点了会弹回来并说明原因）—— 它们看着也像坏链，删了却可能是真丢东西；
    /// ② 删除**只进回收站**（走 `src\Recycle.cs` 的 SHFileOperation），删错了还能捞回来；
    /// ③ 删除前过《使用条款》同意门 + 确认框，一个文件都不提权、不改系统设置。
    ///
    /// 扫描是只读的、随时可以重新扫；扫描放在后台线程（开始菜单里几百个快捷方式，同步扫会让窗口假死）。</summary>
    internal sealed class BrokenLinksForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly Button _rescan = new Button();
        private readonly Button _checkAll = new Button();
        private readonly Button _uncheck = new Button();
        private readonly Button _delete = new Button();

        private volatile bool _busy = false;
        private int _broken = 0;

        public BrokenLinksForm(Theme theme)
        {
            _theme = theme;
            Text = "无效快捷方式清理（桌面 + 开始菜单）";
            ClientSize = new Size(980, 560);
            MinimumSize = new Size(820, 440);
            MinimizeBox = true;   // 最小化按钮：点它把主窗口一起收进任务栏（2026-10-26 主副窗口关联）
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(930, 0);
            intro.Text = "扫的是四个地方：当前用户桌面、所有用户桌面、当前用户开始菜单、所有用户开始菜单（子文件夹也会进去）。"
                + "只统计**目标已经不在了**的快捷方式；网络位置、没插的移动盘、商店应用也在下面列出来，"
                + "但它们不能勾（删了就真没了 / 可能只是没插）。"
                + Environment.NewLine + "删除走回收站，删错了可以从回收站还原。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.CheckBoxes = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("名称", 220);
            _list.Columns.Add("类型", 92);
            _list.Columns.Add("所在文件夹", 260);
            _list.Columns.Add("指向（目标）", 300);
            _list.Columns.Add("说明", 320);
            _list.ItemCheck += ListItemCheck;
            _list.ItemChecked += delegate { UpdateButtons(); };
            root.Controls.Add(_list, 0, 1);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(930, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 2);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 6;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 5; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _rescan.Text = "重新扫描";
            StyleButton(_rescan, 100);
            _rescan.Click += delegate { Scan(); };
            _checkAll.Text = "全选坏链";
            StyleButton(_checkAll, 100);
            _checkAll.Click += delegate { CheckBroken(true); };
            _uncheck.Text = "清除勾选";
            StyleButton(_uncheck, 92);
            _uncheck.Click += delegate { CheckBroken(false); };
            _delete.Text = "删除选中的坏链（回收站）";
            StyleButton(_delete, 210);
            _delete.Click += delegate { DeleteChecked(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_rescan, 1, 0);
            buttons.Controls.Add(_checkAll, 2, 0);
            buttons.Controls.Add(_uncheck, 3, 0);
            buttons.Controls.Add(_delete, 4, 0);
            buttons.Controls.Add(close, 5, 0);
            root.Controls.Add(buttons, 0, 3);

            Controls.Add(root);
            CancelButton = close;
            ApplyTheme(theme);
            Load += delegate { Scan(); };
        }

        private void StyleButton(Button b, int width)
        {
            b.AutoSize = false;
            b.Height = 26;
            b.Width = width;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Margin = new Padding(6, 2, 0, 2);
            b.TabStop = false;
        }

        public void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            ApplyThemeRecursive(this, theme);
        }

        private static void ApplyThemeRecursive(Control parent, Theme theme)
        {
            foreach (Control c in parent.Controls)
            {
                c.BackColor = theme.FormBack;
                Label l = c as Label;
                if (l != null) { l.ForeColor = theme.BarText; }
                ListView lv = c as ListView;
                if (lv != null)
                {
                    lv.BackColor = theme.LogBack;
                    lv.ForeColor = theme.LogText;
                }
                Button b = c as Button;
                if (b != null)
                {
                    b.BackColor = theme.ButtonBack;
                    b.ForeColor = theme.ButtonText;
                    b.FlatAppearance.BorderColor = theme.ButtonBorder;
                    b.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
                }
                if (c.Controls.Count > 0) { ApplyThemeRecursive(c, theme); }
            }
        }

        // ------------------------------------------------------------------ 扫描

        private void Scan()
        {
            if (_busy) { return; }
            SetBusy(true, "正在扫描桌面和开始菜单里的快捷方式…");
            Thread t = new Thread(delegate ()
            {
                List<string> errors;
                List<Shortcuts.LinkInfo> links = Shortcuts.Scan(Shortcuts.ScanRoots(), out errors);
                links.Sort(delegate (Shortcuts.LinkInfo a, Shortcuts.LinkInfo b)
                {
                    int ka = KindOrder(a.Kind);
                    int kb = KindOrder(b.Kind);
                    if (ka != kb) { return ka.CompareTo(kb); }
                    return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                });
                try { BeginInvoke((MethodInvoker)delegate { Fill(links, errors); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private static int KindOrder(string kind)
        {
            if (kind == "broken") { return 0; }
            if (kind == "network") { return 1; }
            if (kind == "removable") { return 2; }
            if (kind == "skip") { return 3; }
            return 4;
        }

        private void Fill(List<Shortcuts.LinkInfo> links, List<string> errors)
        {
            if (IsDisposed) { return; }
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                int broken = 0;
                foreach (Shortcuts.LinkInfo li in links)
                {
                    if (li.Kind == "ok") { continue; }
                    ListViewItem row = new ListViewItem(li.Name);
                    row.SubItems.Add(li.KindText);
                    row.SubItems.Add(li.Dir);
                    row.SubItems.Add(li.Target.Length > 0 ? li.Target : "（没有可解析的文件目标）");
                    row.SubItems.Add(li.Note);
                    row.Tag = li;
                    if (li.Kind == "broken")
                    {
                        broken++;
                        row.ForeColor = _theme.Danger;
                    }
                    _list.Items.Add(row);
                }
                _broken = broken;
            }
            finally
            {
                _list.EndUpdate();
            }
            SetBusy(false, "");
            string line = "坏链 " + _broken.ToString(CultureInfo.InvariantCulture) + " 个（能勾、能删）；"
                + "其余 " + Math.Max(0, _list.Items.Count - _broken).ToString(CultureInfo.InvariantCulture)
                + " 个看着像坏链但不能删（网络位置 / 没插的盘 / 商店应用）。";
            if (errors.Count > 0) { line += " 扫描时有 " + errors.Count.ToString(CultureInfo.InvariantCulture) + " 个目录读不进去（不影响其余结果）。"; }
            _status.Text = line;
            Logger.Write("无效快捷方式清理", "扫描完成：坏链 " + _broken.ToString(CultureInfo.InvariantCulture)
                + " 个，" + _list.Items.Count.ToString(CultureInfo.InvariantCulture) + " 行");
            foreach (string e in errors) { Logger.Write("无效快捷方式清理", "扫描提示：" + e); }
            UpdateButtons();
        }

        private void SetBusy(bool busy, string text)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _rescan.Enabled = !busy;
            _checkAll.Enabled = !busy;
            _uncheck.Enabled = !busy;
            if (text.Length > 0) { _status.Text = text; }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _delete.Enabled = !_busy && CheckedBroken() > 0;
        }

        private int CheckedBroken()
        {
            int n = 0;
            foreach (ListViewItem row in _list.Items)
            {
                Shortcuts.LinkInfo li = row.Tag as Shortcuts.LinkInfo;
                if (row.Checked && li != null && li.Kind == "broken") { n++; }
            }
            return n;
        }

        private void ListItemCheck(object sender, ItemCheckEventArgs e)
        {
            Shortcuts.LinkInfo li = _list.Items[e.Index].Tag as Shortcuts.LinkInfo;
            if (li == null || li.Kind == "broken") { return; }
            if (e.NewValue == CheckState.Checked)
            {
                e.NewValue = CheckState.Unchecked;
                _status.Text = "这一项不能删：" + li.Note + "。";
            }
        }

        private void CheckBroken(bool value)
        {
            if (_busy) { return; }
            _list.BeginUpdate();
            try
            {
                foreach (ListViewItem row in _list.Items)
                {
                    Shortcuts.LinkInfo li = row.Tag as Shortcuts.LinkInfo;
                    if (li != null && li.Kind == "broken") { row.Checked = value; }
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            UpdateButtons();
            _status.Text = value ? "坏链已全选 —— 点右下角「删除选中的坏链」才会真删（进回收站）。" : "勾选已清除。";
        }

        // ------------------------------------------------------------------ 删除

        private void DeleteChecked()
        {
            if (_busy) { return; }
            List<Shortcuts.LinkInfo> chosen = new List<Shortcuts.LinkInfo>();
            foreach (ListViewItem row in _list.Items)
            {
                Shortcuts.LinkInfo li = row.Tag as Shortcuts.LinkInfo;
                if (row.Checked && li != null && li.Kind == "broken") { chosen.Add(li); }
            }
            if (chosen.Count == 0) { return; }

            if (!Consent.EnsureAccepted(this, _theme, "要删除桌面 / 开始菜单里的无效快捷方式"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次删除被拦下了。";
                return;
            }

            System.Text.StringBuilder body = new System.Text.StringBuilder();
            body.Append("会删掉下面 ").Append(chosen.Count.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" 个快捷方式（**只删快捷方式本身，不碰它们指向的东西**），全部进回收站，删错了可以还原：");
            body.AppendLine();
            int shown = 0;
            foreach (Shortcuts.LinkInfo li in chosen)
            {
                if (shown >= 12) { body.AppendLine("… 还有 " + (chosen.Count - shown).ToString(CultureInfo.InvariantCulture) + " 个"); break; }
                body.Append("  · ").AppendLine(li.Name + "    （" + li.Dir + "）");
                shown++;
            }
            using (ConfirmForm f = new ConfirmForm("要删除这些无效快捷方式吗？", body.ToString(), "删除到回收站", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusy(true, "正在删（进回收站）…");
            Logger.Write("无效快捷方式清理", "删除 " + chosen.Count.ToString(CultureInfo.InvariantCulture) + " 个坏链（进回收站）");
            Thread t = new Thread(delegate ()
            {
                Recycle.DeleteResult dr = Shortcuts.DeleteBroken(chosen);
                try { BeginInvoke((MethodInvoker)delegate { FinishDelete(dr); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void FinishDelete(Recycle.DeleteResult dr)
        {
            if (IsDisposed) { return; }
            SetBusy(false, "");
            if (dr.Failed.Count == 0)
            {
                _status.Text = "删好了：" + dr.Deleted.ToString(CultureInfo.InvariantCulture) + " 个快捷方式进了回收站。正在重新扫描…";
                Logger.Write("无效快捷方式清理", "删除完成：" + dr.Deleted.ToString(CultureInfo.InvariantCulture) + " 个");
            }
            else
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("删了 ").Append(dr.Deleted.ToString(CultureInfo.InvariantCulture)).Append(" 个，")
                    .Append(dr.Failed.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个没删成：");
                for (int i = 0; i < dr.Failed.Count && i < 5; i++)
                {
                    sb.Append(" ").Append(Path.GetFileName(dr.Failed[i])).Append("（").Append(dr.ReasonOf(i)).Append("）");
                }
                _status.Text = sb.ToString();
                Logger.Write("无效快捷方式清理", "删除部分失败：" + sb.ToString());
            }
            Scan();
        }
    }
}
