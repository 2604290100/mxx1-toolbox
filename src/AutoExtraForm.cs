// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「计划任务 + 服务 自启」窗口（2026-10-28 用户提的第 5 件事）：
    /// 「启动项管理」只管注册表 `Run` 键和启动文件夹；这一页补上另外两条最常被忽略的路 ——
    /// **计划任务**（开机 / 登录 / 定时触发）和**系统服务**（自动启动）。
    ///
    /// 三条口径（和「此电脑多余图标」窗口一致）：
    /// ① 点开只读（扫任务 XML + 服务注册表），所以这里不弹确认框；
    /// ② 「禁用」「还原」每次过条款门 + 确认框；要管理员（服务在 HKLM）；不是管理员弹 UAC 重开窗口；
    /// ③ **系统自带的一律只报告**：`\Microsoft\` 开头的任务、ImagePath 在 `%SystemRoot%` 下的服务、
    ///    驱动 —— 选中了也只会说"不给动"。禁用前记原值、写完读回核对，随时能还原。
    ///
    /// ⚠️ 这一页**只禁用、不删除**：任务还在、服务还在，禁用了随时能点回来。</summary>
    internal sealed class AutoExtraForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _filter = new TextBox();
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly Button _disableOne = new Button();
        private readonly Button _disableAll = new Button();
        private readonly Button _restore = new Button();

        private volatile bool _busy = false;
        private List<AutoExtraItem> _all = new List<AutoExtraItem>();

        public AutoExtraForm(Theme theme)
        {
            _theme = theme;
            Text = "计划任务 + 服务自启（关了自启它还是自己跑）";
            ClientSize = new Size(1080, 600);
            MinimumSize = new Size(900, 470);
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
            intro.MaximumSize = new Size(1030, 0);
            intro.Text = "「启动项管理」只管注册表 Run 键和启动文件夹；可软件还有两条路能自己跑起来 ——"
                + "计划任务（开机 / 登录 / 定时触发，很多国产软件走这条）和系统服务（「自动」启动）。"
                + "这一页把它们扫成一张列表：名称 / 种类 / 现在什么状态 / 触发方式 / 到底跑的是什么程序。"
                + "选中第三方的点「禁用选中…」（只禁用、不删除，随时能还原）；"
                + "系统自带的（\\Microsoft\\ 开头的任务、Windows 目录下的服务、驱动）只列出、一个都不会动。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel filterRow = new TableLayoutPanel();
            filterRow.Dock = DockStyle.Fill;
            filterRow.AutoSize = true;
            filterRow.ColumnCount = 3;
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label cap = new Label();
            cap.AutoSize = true;
            cap.Text = "过滤（名称 / 命令 / 触发）：";
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
            root.Controls.Add(filterRow, 0, 1);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = true;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("名称", 260);
            _list.Columns.Add("种类", 90);
            _list.Columns.Add("现在", 130);
            _list.Columns.Add("触发", 130);
            _list.Columns.Add("执行的是什么", 330);
            _list.Columns.Add("登记位置", 260);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.MouseDown += ListMouseDown;
            root.Controls.Add(_list, 0, 2);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(1030, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _disableOne.Text = "禁用选中…";
            StyleButton(_disableOne, 120);
            _disableOne.Click += delegate { DoDisable(false); };
            _disableAll.Text = "禁用全部第三方…";
            StyleButton(_disableAll, 150);
            _disableAll.Click += delegate { DoDisable(true); };
            _restore.Text = "还原成禁用前";
            StyleButton(_restore, 132);
            _restore.Click += delegate { DoRestore(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_disableOne, 1, 0);
            buttons.Controls.Add(_disableAll, 2, 0);
            buttons.Controls.Add(_restore, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 4);

            Controls.Add(root);
            CancelButton = close;
            KeyPreview = true;
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { RefreshState(); e.Handled = true; }
            };

            ApplyTheme(theme);
            // ⚠️ 构造函数里**不许**先 SetBusyUi(true)（坑 78/79）。
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
            _status.Text = "正在读计划任务和服务（任务多的时候要一两秒）…";
            Application.DoEvents();
            List<AutoExtraItem> list;
            try
            {
                list = AutoExtra.Scan();
            }
            catch (Exception ex)
            {
                SetBusyUi(false);
                _status.ForeColor = _theme.Danger;
                _status.Text = "读不了这台机器的计划任务 / 服务：" + ex.Message
                    + "（任务目录：" + AutoExtra.TasksRoot() + "；服务：" + AutoExtra.ServicesRootText() + "）";
                Logger.Write("计划任务与服务自启", "读失败：" + ex.Message);
                return;
            }
            _all = list;
            Fill();

            int tasks = 0, svcs = 0, movable = 0;
            foreach (AutoExtraItem it in list)
            {
                if (it.Kind == "task") { tasks++; } else { svcs++; }
                if (it.CanChange) { movable++; }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("共 ").Append(list.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" 项：计划任务 ").Append(tasks.ToString(CultureInfo.InvariantCulture))
                .Append(" 个、服务 ").Append(svcs.ToString(CultureInfo.InvariantCulture))
                .Append(" 个；第三方可禁用 ").Append(movable.ToString(CultureInfo.InvariantCulture)).Append(" 个。");
            sb.Append("系统自带的（带红色）只列出、不给动。");
            int pending = 0;
            try { pending = AutoExtra.PendingRecords(); } catch { }
            if (pending > 0)
            {
                sb.Append("有 ").Append(pending.ToString(CultureInfo.InvariantCulture)).Append(" 条改动可以点「还原成禁用前」。");
            }
            SetBusyUi(false);
            _status.Text = sb.ToString();
        }

        private void Fill()
        {
            string needle = _filter.Text.Trim();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (AutoExtraItem it in _all)
                {
                    if (needle.Length > 0)
                    {
                        string hay = it.Name + " " + it.Command + " " + it.Trigger + " " + it.StateText()
                            + " " + it.KindText() + " " + it.Id;
                        if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                    }
                    ListViewItem row = new ListViewItem(it.Name);
                    row.SubItems.Add(it.KindText());
                    row.SubItems.Add(it.StateText());
                    row.SubItems.Add(it.Kind == "task" ? it.Trigger : "—");
                    row.SubItems.Add(it.Command.Length > 0 ? it.Command : "（没读到执行路径）");
                    row.SubItems.Add(it.Where);
                    row.Tag = it;
                    if (!it.CanChange) { row.ForeColor = _theme.Danger; }
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            UpdateButtons();
        }

        private void SetBusyUi(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            int sel = 0;
            foreach (ListViewItem row in _list.SelectedItems)
            {
                AutoExtraItem it = row.Tag as AutoExtraItem;
                if (it != null && it.CanChange && !it.Disabled) { sel++; }
            }
            _disableOne.Enabled = !_busy && sel > 0;
            _disableOne.Text = sel > 0
                ? ("禁用 " + sel.ToString(CultureInfo.InvariantCulture) + " 项…")
                : "禁用选中…";
            int all = 0;
            foreach (AutoExtraItem it in _all) { if (it.CanChange && !it.Disabled) { all++; } }
            _disableAll.Enabled = !_busy && all > 0;
            int pending = 0;
            try { pending = AutoExtra.PendingRecords(); } catch { }
            _restore.Enabled = !_busy && pending > 0;
        }

        private List<AutoExtraItem> SelectedChangeable()
        {
            List<AutoExtraItem> list = new List<AutoExtraItem>();
            foreach (ListViewItem row in _list.SelectedItems)
            {
                AutoExtraItem it = row.Tag as AutoExtraItem;
                if (it != null && it.CanChange && !it.Disabled) { list.Add(it); }
            }
            return list;
        }

        private void ListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) { return; }
            ListViewItem row = _list.GetItemAt(e.X, e.Y);
            if (row != null) { row.Selected = true; }
        }

        private void Flash(string text)
        {
            _status.Text = text;
            _status.ForeColor = _theme.Danger;
        }

        // ------------------------------------------------------------------ 禁用

        private void DoDisable(bool all)
        {
            if (_busy) { return; }
            List<AutoExtraItem> targets = new List<AutoExtraItem>();
            if (all)
            {
                foreach (AutoExtraItem it in _all) { if (it.CanChange && !it.Disabled) { targets.Add(it); } }
            }
            else
            {
                targets = SelectedChangeable();
            }
            if (targets.Count == 0)
            {
                Flash(all ? "没有可以禁用的第三方项（系统自带的都不给动）。"
                    : "先选中第三方的行 —— 系统自带的（红色）不给禁用。");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要禁用这些计划任务 / 服务"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            StringBuilder body = new StringBuilder();
            body.Append("要禁用 ").Append(targets.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 项吗？");
            body.AppendLine();
            int shown = 0;
            foreach (AutoExtraItem it in targets)
            {
                if (shown >= 8) { body.Append("  … 还有 ").AppendLine((targets.Count - shown).ToString(CultureInfo.InvariantCulture) + " 项"); break; }
                body.Append("  · [").Append(it.KindText()).Append("] ").Append(it.Name).AppendLine("　" + it.StateText());
                shown++;
            }
            body.AppendLine();
            body.AppendLine("只禁用、不删除：任务还在、服务还在，随时能点「还原成禁用前」。");
            body.AppendLine("系统自带的（\\Microsoft\\ 开头的任务、Windows 目录下的服务、驱动）一个都不会动。");
            body.AppendLine("禁用前把原值记到：" + AutoExtra.RecordFile() + "；写完每一项都读回来核对。");
            body.AppendLine("服务在 HKLM 里，要管理员权限。");
            using (ConfirmForm f = new ConfirmForm("要禁用这些计划任务 / 服务吗？", body.ToString(), "禁用", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (!Launcher.IsAdmin())
            {
                OfferElevation("禁用");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在禁用 " + targets.Count.ToString(CultureInfo.InvariantCulture) + " 项…";
            Application.DoEvents();
            Logger.Write("计划任务与服务自启", "禁用 " + targets.Count + " 项");
            AutoExtraReport rep = AutoExtra.Disable(targets);
            SetBusyUi(false);
            Logger.Write("计划任务与服务自启", "禁用" + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            RefreshState();
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = "禁用" + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
        }

        private void DoRestore()
        {
            if (_busy) { return; }
            int pending = 0;
            try { pending = AutoExtra.PendingRecords(); } catch { }
            if (pending == 0)
            {
                Flash("没有待还原的记录（这台机器还没用这个窗口禁用过计划任务 / 服务）。");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要把这些计划任务 / 服务恢复成禁用之前的样子"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            string body = "会把之前禁用的 " + pending.ToString(CultureInfo.InvariantCulture) + " 项按记录恢复："
                + Environment.NewLine + Environment.NewLine
                + "  · 计划任务 → 重新启用；" + Environment.NewLine
                + "  · 服务 → 把启动类型写回原来的值（自动 / 手动 / …）。" + Environment.NewLine + Environment.NewLine
                + "记录文件：" + AutoExtra.RecordFile();
            using (ConfirmForm f = new ConfirmForm("要还原成禁用前吗？", body, "还原", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (!Launcher.IsAdmin())
            {
                OfferElevation("还原");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在还原…";
            Application.DoEvents();
            Logger.Write("计划任务与服务自启", "还原：" + pending + " 条记录");
            AutoExtraReport rep = AutoExtra.Restore();
            SetBusyUi(false);
            Logger.Write("计划任务与服务自启", "还原" + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            RefreshState();
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = "还原" + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
        }

        // ------------------------------------------------------------------ 不是管理员

        private void OfferElevation(string what)
        {
            string body = "禁用计划任务 / 服务要用管理员权限（服务写在 " + AutoExtra.ServicesRootText() + "）。"
                + Environment.NewLine + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开。"
                + "因为写注册表只能由你在窗口里点出来，所以重开之后要再点一次「" + what + "」才会真的生效。";
            using (ConfirmForm f = new ConfirmForm("需要管理员权限", body, "确定", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath, "autoextra");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("计划任务与服务自启", "不是管理员：已请求以管理员身份重开窗口");
                Close();
            }
            catch (Exception ex)
            {
                Flash("请求管理员权限失败：" + ex.Message);
                Logger.Write("计划任务与服务自启", "请求管理员权限失败：" + ex.Message);
            }
        }
    }
}
