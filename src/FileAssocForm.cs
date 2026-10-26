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
    /// <summary>「文件关联体检」窗口：一张只读的体检报告 + 三颗按钮（修复缺失项 / 还原成修复前 / 刷新）。
    ///
    /// 治的是用户 2026-10-24 报的那个错：装完程序点「运行」（或双击桌面快捷方式）弹出
    /// 「该文件没有与之关联的应用来执行该操作。请安装应用，若已经安装应用，请在「默认应用设置」页面中
    /// 创建关联。」—— 量出来是 `HKLM\SOFTWARE\Classes\lnkfile` 的 `IsShortcut` 值不见了。
    ///
    /// 三条口径（和「合盖与电源键动作」窗口一致）：
    /// ① 点开这个窗口**什么都不改**，只读一遍注册表给你看，所以这里不弹确认框；
    /// ② 「修复」/「还原」每一次都过条款门 + 确认框；不是管理员时弹一次 UAC 带管理员重开窗口，
    ///    重开之后要再点一次；
    /// ③ 修之前先把原值记到磁盘（记不下来就一个字都不改），所以「还原成修复前」永远点得动。</summary>
    internal sealed class FileAssocForm : Mxx1Form
    {
        private readonly Theme _theme;

        private readonly TextBox _report = new TextBox();
        private readonly Label _status = new Label();
        private readonly CheckBox _alsoWrong = new CheckBox();
        private readonly Button _fix = new Button();
        private readonly Button _restore = new Button();
        private readonly Button _refresh = new Button();
        private readonly Button _copy = new Button();

        private volatile bool _busy = false;
        private int _problems = 0;

        public FileAssocForm(Theme theme)
        {
            _theme = theme;
            Text = "文件关联体检（快捷方式打不开 / 双击没反应）";
            ClientSize = new Size(880, 640);
            MinimumSize = new Size(760, 520);
            MinimizeBox = true;   // 最小化按钮：点它把主窗口一起收进任务栏（2026-10-26 主副窗口关联）
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 7;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) { root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 报告区吃掉剩下的高度
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(830, 0);
            intro.Margin = new Padding(2, 0, 2, 6);
            intro.Text = "双击快捷方式 / 程序，弹出「该文件没有与之关联的应用来执行该操作」的时候用这一页。"
                + "它读一遍注册表里那几条「哪个文件该用什么打开」的登记，缺的就点「修复缺失项」补回来 —— "
                + "最常见的元凶是快捷方式标记（lnkfile\\IsShortcut）被某个「优化」工具删掉了。"
                + "补之前会把原值记下来，所以随时能点「还原成修复前」退回原样。";
            root.Controls.Add(intro, 0, 0);

            _alsoWrong.AutoSize = true;
            _alsoWrong.MaximumSize = new Size(830, 0);
            _alsoWrong.Margin = new Padding(2, 0, 2, 6);
            _alsoWrong.Text = "连「和默认不一样」的也改回默认（默认只补缺的；自己改过、或别的软件设过的那几项，"
                + "勾了才会被覆盖成系统默认值）";
            root.Controls.Add(_alsoWrong, 0, 1);

            Label cap = new Label();
            cap.AutoSize = true;
            cap.Margin = new Padding(2, 2, 2, 2);
            cap.Text = "体检报告（√ 正常 / × 缺 / ! 和默认不一样 / · 只报告）：";
            root.Controls.Add(cap, 0, 2);

            _report.Multiline = true;
            _report.ReadOnly = true;
            _report.ScrollBars = ScrollBars.Both;
            _report.WordWrap = false;
            _report.Dock = DockStyle.Fill;
            _report.Height = 320;
            _report.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_report, 0, 3);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(830, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 4);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 6;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _fix.Text = "修复缺失项…";
            StyleButton(_fix, 132);
            _fix.Click += delegate { DoFix(); };
            _restore.Text = "还原成修复前";
            StyleButton(_restore, 132);
            _restore.Click += delegate { DoRestore(); };
            _refresh.Text = "刷新";
            StyleButton(_refresh, 84);
            _refresh.Click += delegate { RefreshState(); };
            _copy.Text = "复制报告";
            StyleButton(_copy, 104);
            _copy.Click += delegate { CopyReport(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_fix, 1, 0);
            buttons.Controls.Add(_restore, 2, 0);
            buttons.Controls.Add(_refresh, 3, 0);
            buttons.Controls.Add(_copy, 4, 0);
            buttons.Controls.Add(close, 5, 0);
            root.Controls.Add(buttons, 0, 5);

            Controls.Add(root);
            CancelButton = close;
            KeyPreview = true;
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { RefreshState(); e.Handled = true; }
            };

            ApplyTheme(theme);
            // ⚠️ 构造函数里**不许**先 SetBusyUi(true)：RefreshState() 开头就是 `if (_busy) return;`，
            // 会把自己挡掉、窗口定格在"忙"上（坑 78/79 那次「鼠标移上去一直转圈」就是这么来的）。
            Load += delegate { RefreshState(); };
        }

        private void StyleButton(Button b, int width)
        {
            b.AutoSize = false;
            b.Width = width;
            b.Height = 30;
            b.Margin = new Padding(4, 2, 0, 2);
        }

        private void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            foreach (Control c in Controls) { c.BackColor = theme.FormBack; }
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
                    t.BackColor = t.ReadOnly ? theme.LogBack : theme.InputBack;
                    t.ForeColor = t.ReadOnly ? theme.LogText : theme.InputText;
                }
                CheckBox ck = c as CheckBox;
                if (ck != null) { ck.ForeColor = theme.BarText; }
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
            _status.Text = "正在读注册表里的文件关联…";
            Application.DoEvents();

            List<AssocItem> items;
            try
            {
                items = FileAssoc.Check();
            }
            catch (Exception ex)
            {
                SetBusyUi(false);
                _report.Text = "读不了这台机器的文件关联登记。" + Environment.NewLine
                    + "  " + ex.Message + Environment.NewLine + Environment.NewLine
                    + "  （这一页读的是注册表：" + FileAssoc.RootText() + "；读不到多半是系统被精简 / 权限被限制。）";
                _status.Text = "读不到文件关联登记，这一页修不了。";
                Logger.Write("文件关联体检", "读失败：" + ex.Message);
                UpdateButtons();
                return;
            }

            _problems = 0;
            foreach (AssocItem it in items)
            {
                if (it.Fixable && it.State != "ok") { _problems++; }
            }

            _report.Text = FileAssoc.ReportText(items);
            int pending = FileAssoc.PendingRecords();
            if (_problems == 0)
            {
                _status.Text = "没发现缺失的项。"
                    + (pending > 0 ? ("有 " + pending + " 条修复记录可以点「还原成修复前」退回。") : "双击还是打不开的话，看报告里「用户区」那几条。");
            }
            else
            {
                _status.Text = "发现 " + _problems + " 项要补（缺的 / 你勾了「也改回默认」的都会补）；"
                    + (pending > 0 ? ("另外有 " + pending + " 条修复记录还没还原。") : "补之前会先把原值记下来。");
            }
            SetBusyUi(false);
            UpdateButtons();
        }

        private void SetBusyUi(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _fix.Enabled = !busy;
            _restore.Enabled = !busy;
            _refresh.Enabled = !busy;
            _copy.Enabled = !busy;
            _alsoWrong.Enabled = !busy;
        }

        private void UpdateButtons()
        {
            int pending = 0;
            try { pending = FileAssoc.PendingRecords(); } catch { }
            _restore.Enabled = !_busy && pending > 0;
        }

        private void Flash(string text)
        {
            _status.Text = text;
            _status.ForeColor = _theme.Danger;
        }

        private void CopyReport()
        {
            try
            {
                if (_report.Text.Length == 0) { return; }
                Clipboard.SetText(_report.Text);
                _status.ForeColor = _theme.BarText;
                _status.Text = "报告已复制到剪贴板（" + _report.Text.Length.ToString(CultureInfo.InvariantCulture) + " 个字符）。";
            }
            catch (Exception ex)
            {
                Flash("复制失败：" + ex.Message);
            }
        }

        // ------------------------------------------------------------------ 修复

        private void DoFix()
        {
            if (_busy) { return; }
            if (!Consent.EnsureAccepted(this, _theme, "要往注册表里补写系统的文件关联"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            bool alsoWrong = _alsoWrong.Checked;
            List<AssocItem> items = FileAssoc.Check();
            int missing = 0, wrong = 0;
            StringBuilder list = new StringBuilder();
            foreach (AssocItem it in items)
            {
                if (!it.Fixable || it.State == "ok") { continue; }
                if (it.State == "missing")
                {
                    missing++;
                    if (list.Length < 900) { list.AppendLine("  · " + it.Title + "（补成 " + Show(it.Want) + "）"); }
                }
                else
                {
                    wrong++;
                    if (alsoWrong && list.Length < 900)
                    {
                        list.AppendLine("  · " + it.Title + "（现在是 " + Show(it.Actual) + " → 改回 " + Show(it.Want) + "）");
                    }
                }
            }

            StringBuilder body = new StringBuilder();
            body.Append("会给这台机器补 ").Append((missing + (alsoWrong ? wrong : 0)).ToString(CultureInfo.InvariantCulture))
                .AppendLine(" 项文件关联登记（写在 " + FileAssoc.RootText() + "，所有用户共用这一层）：");
            body.AppendLine();
            body.Append(list.ToString());
            body.AppendLine();
            body.AppendLine("要动的是注册表，需要管理员权限；补之前会先把原值记到：" + FileAssoc.RecordFile());
            body.AppendLine("写完之后会一项一项读回来核对；想退回原样，点「还原成修复前」。");
            if (wrong > 0 && !alsoWrong)
            {
                body.AppendLine();
                body.AppendLine("另外有 " + wrong.ToString(CultureInfo.InvariantCulture) + " 项「和默认不一样」——"
                    + "它们是系统默认值之外的值（你自己选过、或别的软件设过），这次不动它们。");
            }

            using (ConfirmForm f = new ConfirmForm("要补这些文件关联吗？", body.ToString(), "修复", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (!Launcher.IsAdmin())
            {
                OfferElevation("修复");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在补…";
            Application.DoEvents();
            Logger.Write("文件关联体检", "修复：缺失 " + missing + " 项，勾了「也改回默认」=" + (alsoWrong ? "是" : "否") + "，值不对 " + wrong + " 项");
            AssocFixReport rep = FileAssoc.Fix(alsoWrong);
            Finish(rep, "修复");
        }

        private void DoRestore()
        {
            if (_busy) { return; }
            if (!Consent.EnsureAccepted(this, _theme, "要把文件关联退回修复之前的样子"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            int pending = FileAssoc.PendingRecords();
            if (pending == 0)
            {
                Flash("没有待还原的记录（这台机器还没用这个窗口补过关联）。");
                return;
            }
            string body = "会把之前补进去的 " + pending.ToString(CultureInfo.InvariantCulture) + " 项按记录退回原样："
                + Environment.NewLine + Environment.NewLine
                + "  · 工具箱加进去的值 → 删掉；" + Environment.NewLine
                + "  · 工具箱覆盖掉的值 → 写回原来的数据。" + Environment.NewLine + Environment.NewLine
                + "如果有哪一项后来被你自己 / 别的软件改过，那一项会跳过并在报告里写出来（不会冲掉你的改动）。"
                + Environment.NewLine + Environment.NewLine
                + "记录文件：" + FileAssoc.RecordFile();
            using (ConfirmForm f = new ConfirmForm("要还原成修复前吗？", body, "还原", _theme))
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
            Logger.Write("文件关联体检", "还原：" + pending + " 条记录");
            AssocFixReport rep = FileAssoc.Restore();
            Finish(rep, "还原");
        }

        private void Finish(AssocFixReport rep, string what)
        {
            if (IsDisposed) { return; }
            List<AssocItem> after;
            try { after = FileAssoc.Check(); }
            catch { after = new List<AssocItem>(); }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("== " + what + "结果 ==");
            sb.Append(rep.Body());
            sb.AppendLine();
            sb.Append(FileAssoc.ReportText(after));
            _report.Text = sb.ToString();

            _problems = 0;
            foreach (AssocItem it in after)
            {
                if (it.Fixable && it.State != "ok") { _problems++; }
            }

            SetBusyUi(false);
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = what + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
            Logger.Write("文件关联体检", what + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            UpdateButtons();
            if (rep.NeedAdmin && !Launcher.IsAdmin())
            {
                Flash(what + "时有几项因为权限没写成 —— 点它们会弹 UAC，用管理员重开窗口之后再点一次。");
            }
        }

        // ------------------------------------------------------------------ 不是管理员

        /// <summary>不是管理员时的两条路（同「合盖与电源键动作」/「右键菜单管理」）：说清楚 → 弹一次 UAC
        /// 带管理员重开这个窗口 → 重开之后**要再点一次**。命令行那条 `assoc` 只开窗口，不带任何"要改什么"。</summary>
        private void OfferElevation(string what)
        {
            string body = "改文件关联（写 " + FileAssoc.RootText() + "）要用管理员权限才行。" + Environment.NewLine
                + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开。"
                + "因为写注册表只能由你在窗口里点出来，所以重开之后要再点一次「" + what + "」才会真的生效。";
            using (ConfirmForm f = new ConfirmForm("需要管理员权限", body, "确定", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath, "assoc");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("文件关联体检", "不是管理员：已请求以管理员身份重开窗口");
                Close();
            }
            catch (Exception ex)
            {
                Flash("请求管理员权限失败：" + ex.Message);
                Logger.Write("文件关联体检", "请求管理员权限失败：" + ex.Message);
            }
        }

        private static string Show(string value)
        {
            if (value == null) { return "(缺)"; }
            if (value.Length == 0) { return "(空)"; }
            return value;
        }
    }
}
