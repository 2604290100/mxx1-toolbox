// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「高 DPI 兼容批量设置」窗口（2026-10-28 用户提的第 3 件事）：
    /// 开了 125% / 150% 缩放之后界面糊成一团的程序，在这里一键设成「高 DPI 感知」。
    ///
    /// 三条口径（和「此电脑多余图标」窗口一致）：
    /// ① 点开这个窗口只读一遍 HKCU 里的兼容性标记，什么都不改（所以不弹确认框）；
    /// ② 「设为高 DPI 感知」「还原」每次过条款门 + 确认框；**只写 HKCU，不用管理员**；
    /// ③ 写前记原值、写完逐个读回核对，`RUNASADMIN` 这类别的标记一个不动。
    ///
    /// ⚠️ 关掉那个程序、重新打开才生效。</summary>
    internal sealed class DpiFlagForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _filter = new TextBox();
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly Button _add = new Button();
        private readonly Button _set = new Button();
        private readonly Button _restore = new Button();

        private volatile bool _busy = false;
        private List<DpiFlagItem> _all = new List<DpiFlagItem>();
        private List<DpiFlagItem> _added = new List<DpiFlagItem>();

        public DpiFlagForm(Theme theme)
        {
            _theme = theme;
            Text = "高 DPI 兼容（界面模糊 / 字太小）";
            ClientSize = new Size(1040, 580);
            MinimumSize = new Size(880, 460);
            MinimizeBox = true;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(990, 0);
            intro.Text = "笔记本开 125% / 150% 缩放之后，老程序的界面会糊成一片、或者字小得看不清 ——"
                + "那是它没声明自己「懂 DPI」，Windows 只好把整张画面拉伸。这一页列出已经有兼容性标记的程序，"
                + "点「添加程序…」把糊的那几个加进来 → 选中 → 点「设为高 DPI 感知…」"
                + "（等于右键 exe → 属性 → 兼容性 → 更改高 DPI 设置 →「高 DPI 缩放替代」）。"
                + "只写当前用户（不用管理员），随时能「还原成设置前」。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel filterRow = new TableLayoutPanel();
            filterRow.Dock = DockStyle.Fill;
            filterRow.AutoSize = true;
            filterRow.ColumnCount = 4;
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label cap = new Label();
            cap.AutoSize = true;
            cap.Text = "过滤（程序名 / 路径）：";
            cap.Margin = new Padding(2, 7, 4, 0);
            filterRow.Controls.Add(cap, 0, 0);
            _filter.Dock = DockStyle.Fill;
            _filter.Margin = new Padding(2, 3, 6, 3);
            _filter.TextChanged += delegate { Fill(); };
            filterRow.Controls.Add(_filter, 1, 0);
            Button refresh = new Button();
            refresh.Text = "刷新";
            StyleButton(refresh, 84);
            refresh.Click += delegate { RefreshState(); };
            filterRow.Controls.Add(refresh, 2, 0);
            _add.Text = "添加程序…";
            StyleButton(_add, 116);
            _add.Click += delegate { AddPrograms(); };
            filterRow.Controls.Add(_add, 3, 0);
            root.Controls.Add(filterRow, 0, 1);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = true;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("程序", 300);
            _list.Columns.Add("现在的设置", 210);
            _list.Columns.Add("别的兼容性标记", 190);
            _list.Columns.Add("完整路径", 330);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            root.Controls.Add(_list, 0, 2);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(990, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 4;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 3; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _set.Text = "设为高 DPI 感知…";
            StyleButton(_set, 150);
            _set.Click += delegate { DoSet(); };
            _restore.Text = "还原成设置前";
            StyleButton(_restore, 132);
            _restore.Click += delegate { DoRestore(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_set, 1, 0);
            buttons.Controls.Add(_restore, 2, 0);
            buttons.Controls.Add(close, 3, 0);
            root.Controls.Add(buttons, 0, 4);

            Controls.Add(root);
            CancelButton = close;
            KeyPreview = true;
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { RefreshState(); e.Handled = true; }
                if (e.KeyCode == Keys.Delete) { RemoveAdded(); e.Handled = true; }
            };

            ApplyTheme(theme);
            // ⚠️ 构造函数里**不许**先 SetBusyUi(true)：RefreshState() 开头就是 `if (_busy) return;`（坑 78/79）。
            Load += delegate { RefreshState(); };
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

        private void ApplyTheme(Theme theme)
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
                TextBox t = c as TextBox;
                if (t != null)
                {
                    t.BackColor = theme.InputBack;
                    t.ForeColor = theme.InputText;
                }
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

        // ------------------------------------------------------------------ 读

        private void RefreshState()
        {
            if (_busy) { return; }
            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在读兼容性标记…";
            Application.DoEvents();
            List<DpiFlagItem> list;
            try
            {
                list = DpiFlag.Scan();
            }
            catch (Exception ex)
            {
                SetBusyUi(false);
                _status.ForeColor = _theme.Danger;
                _status.Text = "读不了兼容性标记：" + ex.Message + "（位置：" + DpiFlag.RootText() + "）";
                Logger.Write("高 DPI 兼容", "读失败：" + ex.Message);
                return;
            }
            _all = list;
            MergeAdded();
            Fill();

            int high = 0;
            foreach (DpiFlagItem it in _all) { if (it.Has("HIGHDPIAWARE")) { high++; } }
            StringBuilder sb = new StringBuilder();
            sb.Append("共 ").Append(_all.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个程序有兼容性标记，其中 ")
                .Append(high.ToString(CultureInfo.InvariantCulture)).Append(" 个已经是「高 DPI 感知」。");
            if (_added.Count > 0)
            {
                sb.Append("另外有 ").Append(_added.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(" 个是刚加进来的（还没写进注册表）。");
            }
            sb.Append("选中要设的执行「设为高 DPI 感知…」；设完关掉那个程序重新打开才生效。");
            int pending = 0;
            try { pending = DpiFlag.PendingRecords(); } catch { }
            if (pending > 0)
            {
                sb.Append("有 ").Append(pending.ToString(CultureInfo.InvariantCulture)).Append(" 条改动可以点「还原成设置前」。");
            }
            SetBusyUi(false);
            _status.Text = sb.ToString();
        }

        /// <summary>刚添加、还没写进注册表的那些，合并进列表（已存在的就不重复加）。</summary>
        private void MergeAdded()
        {
            if (_added.Count == 0) { return; }
            List<DpiFlagItem> keep = new List<DpiFlagItem>();
            foreach (DpiFlagItem a in _added)
            {
                bool found = false;
                foreach (DpiFlagItem it in _all)
                {
                    if (string.Equals(it.Path, a.Path, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                }
                if (found) { continue; }
                keep.Add(a);
                _all.Insert(0, a);
            }
            _added = keep;
        }

        private void Fill()
        {
            string needle = _filter.Text.Trim();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (DpiFlagItem it in _all)
                {
                    if (needle.Length > 0)
                    {
                        string hay = it.Path + " " + it.Product + " " + it.Data;
                        if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                    }
                    string name = it.Product.Length > 0 ? it.Product : SafeName(it.Path);
                    ListViewItem row = new ListViewItem(name);
                    row.SubItems.Add(it.JustAdded ? "（刚加进来，还没设）" : it.StateText());
                    string other = it.OtherTokens();
                    row.SubItems.Add(other.Length > 0 ? other : "—");
                    row.SubItems.Add(it.Path);
                    row.Tag = it;
                    if (it.Has("HIGHDPIAWARE")) { row.ForeColor = _theme.BarText; }
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            UpdateButtons();
        }

        private static string SafeName(string path)
        {
            try { return Path.GetFileName(path); }
            catch { return path; }
        }

        private void SetBusyUi(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _add.Enabled = !busy;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            int sel = _list.SelectedItems.Count;
            _set.Enabled = !_busy && sel > 0;
            _set.Text = sel > 0
                ? ("设为高 DPI 感知 " + sel.ToString(CultureInfo.InvariantCulture) + " 个…")
                : "设为高 DPI 感知…";
            int pending = 0;
            try { pending = DpiFlag.PendingRecords(); } catch { }
            _restore.Enabled = !_busy && pending > 0;
        }

        private List<DpiFlagItem> SelectedItems()
        {
            List<DpiFlagItem> list = new List<DpiFlagItem>();
            foreach (ListViewItem row in _list.SelectedItems)
            {
                DpiFlagItem it = row.Tag as DpiFlagItem;
                if (it != null) { list.Add(it); }
            }
            return list;
        }

        private void Flash(string text)
        {
            _status.Text = text;
            _status.ForeColor = _theme.Danger;
        }

        // ------------------------------------------------------------------ 添加

        private void AddPrograms()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选要设成高 DPI 感知的程序（可以多选）";
                dlg.Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dlg.Multiselect = true;
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                int added = 0;
                foreach (string f in dlg.FileNames)
                {
                    bool exists = false;
                    foreach (DpiFlagItem it in _all)
                    {
                        if (string.Equals(it.Path, f, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                    }
                    if (exists) { continue; }
                    DpiFlagItem it2 = new DpiFlagItem();
                    it2.Path = f;
                    it2.JustAdded = true;
                    it2.Present = false;
                    it2.Product = DpiFlag.ProductOf(f);
                    _added.Add(it2);
                    _all.Insert(0, it2);
                    added++;
                }
                Fill();
                if (added == 0)
                {
                    Flash("这些程序已经在列表里了（不用重复加）。");
                }
                else
                {
                    _status.ForeColor = _theme.BarText;
                    _status.Text = "已加进来 " + added.ToString(CultureInfo.InvariantCulture)
                        + " 个（选中它们点「设为高 DPI 感知…」才会写进注册表）。";
                }
            }
        }

        /// <summary>Delete 键：只把"刚加进来还没设"的行去掉（已写进注册表的不在这儿删）。</summary>
        private void RemoveAdded()
        {
            List<DpiFlagItem> sel = SelectedItems();
            if (sel.Count == 0) { return; }
            bool any = false;
            foreach (DpiFlagItem it in sel)
            {
                if (!it.JustAdded) { continue; }
                _added.Remove(it);
                _all.Remove(it);
                any = true;
            }
            if (any)
            {
                Fill();
                _status.ForeColor = _theme.BarText;
                _status.Text = "已把刚加进来、还没写注册表的那几个从列表里去掉（注册表没动）。";
            }
            else
            {
                Flash("选中的都是已经写进注册表的 —— 想撤掉请点「还原成设置前」。");
            }
        }

        // ------------------------------------------------------------------ 设为高 DPI

        private void DoSet()
        {
            if (_busy) { return; }
            List<DpiFlagItem> targets = SelectedItems();
            if (targets.Count == 0) { Flash("先选中要设的程序（可以按住 Ctrl 多选）。"); return; }
            if (!Consent.EnsureAccepted(this, _theme, "要把这些程序设成高 DPI 感知"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            StringBuilder body = new StringBuilder();
            body.Append("要把 ").Append(targets.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个程序设成「高 DPI 感知」吗？");
            body.AppendLine();
            int shown = 0;
            foreach (DpiFlagItem it in targets)
            {
                if (shown >= 8) { body.Append("  … 还有 ").AppendLine((targets.Count - shown).ToString(CultureInfo.InvariantCulture) + " 个"); break; }
                body.Append("  · ").AppendLine(it.Path);
                shown++;
            }
            body.AppendLine();
            body.AppendLine("写的是当前用户（HKCU）的兼容性标记：「~ HIGHDPIAWARE」。不用管理员。");
            body.AppendLine("只替换 DPI 相关的三个词，RUNASADMIN 这类别的标记一个不动；");
            body.AppendLine("写完逐个读回来核对；原值记到：" + DpiFlag.RecordFile());
            body.AppendLine("关掉那个程序、重新打开才生效。想退回原样点「还原成设置前」。");
            using (ConfirmForm f = new ConfirmForm("要把这些程序设成高 DPI 感知吗？", body.ToString(), "设为高 DPI 感知", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在写兼容性标记…";
            Application.DoEvents();
            Logger.Write("高 DPI 兼容", "设为高 DPI 感知：" + targets.Count + " 个");
            DpiFlagReport rep = DpiFlag.SetHighDpi(targets);
            SetBusyUi(false);
            Logger.Write("高 DPI 兼容", "设为高 DPI 感知" + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            _added.Clear();
            RefreshState();
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = "设为高 DPI 感知" + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
        }

        private void DoRestore()
        {
            if (_busy) { return; }
            int pending = 0;
            try { pending = DpiFlag.PendingRecords(); } catch { }
            if (pending == 0)
            {
                Flash("没有待还原的记录（这台机器还没用这个窗口设过高 DPI 兼容）。");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要把兼容性标记恢复成设置之前的样子"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            string body = "会把之前设过的 " + pending.ToString(CultureInfo.InvariantCulture) + " 个程序按记录写回去："
                + Environment.NewLine + Environment.NewLine
                + "  · 原来有标记的 → 写回原来那一整串；" + Environment.NewLine
                + "  · 原来没有的 → 删掉这一条（等于没设过）。" + Environment.NewLine + Environment.NewLine
                + "记录文件：" + DpiFlag.RecordFile();
            using (ConfirmForm f = new ConfirmForm("要还原成设置前吗？", body, "还原", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在还原…";
            Application.DoEvents();
            Logger.Write("高 DPI 兼容", "还原：" + pending + " 条记录");
            DpiFlagReport rep = DpiFlag.Restore();
            SetBusyUi(false);
            Logger.Write("高 DPI 兼容", "还原" + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            RefreshState();
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = "还原" + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
        }
    }
}
