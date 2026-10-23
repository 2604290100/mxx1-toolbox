// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「合盖与电源键动作」窗口：三个动作 × 接电源/用电池 两组下拉框 + 一个
    /// 「让控制面板能看见这一页」的勾 + 一块只读的报告区（体检 / 上一次的结果都写在这儿）。
    ///
    /// 三条口径（和 docs\DISCLAIMER.md 第 2 节、DESIGN §12.71 一致）：
    /// ① 改的是**所有**电源方案 —— 第三方工具会自己建方案并切过去，只改当前那个会被它换掉；
    /// ② 改之前先把原值记到磁盘，记不下来就一个字都不动；
    /// ③ 写注册表只能由用户在这个窗口里点出来（命令行那条 `power` 只负责"带管理员重开窗口"，
    ///    它不接受任何"要改什么"的参数 —— 同「右键菜单管理」的做法）。
    ///
    /// 窗口里所有会改动系统的动作都先过条款同意门 + 确认框；不是管理员时弹一次 UAC 重开窗口，
    /// 重开之后要再点一次（`powercfg` 改这些值必须要管理员）。</summary>
    internal sealed class PowerForm : Mxx1Form
    {
        private readonly Theme _theme;

        private readonly ComboBox _lidAc = new ComboBox();
        private readonly ComboBox _lidDc = new ComboBox();
        private readonly ComboBox _pwrAc = new ComboBox();
        private readonly ComboBox _pwrDc = new ComboBox();
        private readonly ComboBox _slpAc = new ComboBox();
        private readonly ComboBox _slpDc = new ComboBox();
        private readonly CheckBox _visible = new CheckBox();
        private readonly TextBox _report = new TextBox();
        private readonly Label _now = new Label();
        private readonly Label _status = new Label();
        private readonly Button _apply = new Button();
        private readonly Button _restore = new Button();
        private readonly Button _refresh = new Button();

        private volatile bool _busy = false;
        private int _schemeCount = 0;

        public PowerForm(Theme theme)
        {
            _theme = theme;
            Text = "合盖与电源键动作（改的是所有电源方案）";
            ClientSize = new Size(800, 620);
            MinimumSize = new Size(720, 520);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 10;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 8; i++) { root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 报告区吃掉剩下的高度
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 顶部这一行回答"现在到底会怎样"（2026-10-24 UX 复查第 3 条）：
            // 下面那六个下拉框是"改成什么"，很多人数不清当前值，就先给一句人话。
            _now.AutoSize = true;
            _now.MaximumSize = new Size(750, 0);
            _now.Margin = new Padding(2, 0, 2, 6);
            _now.Text = "现在：正在读本机的电源方案…";
            root.Controls.Add(_now, 0, 0);

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(750, 0);
            intro.Text = "这一页管三个动作：合上盖子、按电源按钮、按睡眠按钮（接电源、用电池分开设）。"
                + "改的时候会把这台机器上所有电源方案一起改成一样 —— 有的软件会自己建一个电源方案、"
                + "用着用着自己切过去，只改当前那个的话，一被切走就又不管用了。"
                + "改之前会把每个方案的原值记下来，随时能点「还原成改前的值」写回去。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 1);

            // ---- 三个动作 × 两组 ----
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.AutoSize = true;
            grid.ColumnCount = 3;
            grid.RowCount = 4;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Margin = new Padding(2, 2, 2, 6);
            grid.Controls.Add(HeadLabel("  什么动作"), 0, 0);
            grid.Controls.Add(HeadLabel("接电源时"), 1, 0);
            grid.Controls.Add(HeadLabel("用电池时"), 2, 0);
            AddActionRow(grid, 1, "合上盖子", _lidAc, _lidDc);
            AddActionRow(grid, 2, "按电源按钮", _pwrAc, _pwrDc);
            AddActionRow(grid, 3, "按睡眠按钮", _slpAc, _slpDc);
            root.Controls.Add(grid, 0, 2);

            _visible.AutoSize = true;
            // ⚠️ CheckBox 的 AutoSize **不折行**（只有 Label 会）——一整段长句子塞进去会被窗口右边缘
            // **切掉**（用户 2026-10-19 那张截图里就是这样）。所以这里自己断成两行：第二行要短到放得下。
            _visible.Text = "让控制面板里能看见「选择关闭盖子的功能」这一页" + Environment.NewLine
                + "（系统默认是藏着的；勾上、点「应用」之后，控制面板-电源选项-选择电源按钮的功能 里就会出现这三项）";
            _visible.MaximumSize = new Size(750, 0);
            _visible.Margin = new Padding(2, 2, 2, 8);
            root.Controls.Add(_visible, 0, 3);

            Label reportCap = new Label();
            reportCap.AutoSize = true;
            reportCap.Text = "体检 / 上一次的结果（只读）：";
            reportCap.Margin = new Padding(2, 2, 2, 2);
            root.Controls.Add(reportCap, 0, 4);

            _report.Multiline = true;
            _report.ReadOnly = true;
            _report.ScrollBars = ScrollBars.Both;
            _report.WordWrap = false;
            _report.Dock = DockStyle.Fill;
            _report.Height = 240;
            _report.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_report, 0, 5);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(750, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 6);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _apply.Text = "应用（改所有方案）";
            StyleButton(_apply, 168);
            _apply.Click += delegate { DoApply(); };
            _restore.Text = "还原成改前的值";
            StyleButton(_restore, 148);
            _restore.Click += delegate { DoRestore(); };
            _refresh.Text = "刷新";
            StyleButton(_refresh, 84);
            _refresh.Click += delegate { RefreshState(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_apply, 1, 0);
            buttons.Controls.Add(_restore, 2, 0);
            buttons.Controls.Add(_refresh, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 7);

            foreach (ComboBox b in new ComboBox[] { _lidAc, _lidDc, _pwrAc, _pwrDc, _slpAc, _slpDc }) { Prepare(b, 150); }
            Controls.Add(root);
            CancelButton = close;

            ApplyTheme(theme);
            // ⚠️ 这里**不能**先 SetBusyUi(true)：它会把 _busy 打开，而 Load 里那句 RefreshState()
            // 开头就是 `if (_busy) { return; }` —— 于是第一次读状态被自己挡掉，窗口就永远停在
            // 「忙」上：光标一直转圈（Cursors.WaitCursor）、下拉框 / 按钮全是灰的、报告区空白。
            // 用户 2026-10-19 报的就是它（「这个按钮打开的窗口没有做好。鼠标移上去一直转圈圈。」）。
            // 初始的"正在读"状态由 RefreshState() 自己设（它也排除了重入），读完自己 SetBusyUi(false)。
            Load += delegate { RefreshState(); };
        }

        private static Label HeadLabel(string text)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = text;
            l.Margin = new Padding(2, 2, 10, 2);
            return l;
        }

        private static Label RowLabel(string text)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = text;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(2, 6, 10, 4);
            return l;
        }

        private static void AddActionRow(TableLayoutPanel grid, int row, string caption, ComboBox ac, ComboBox dc)
        {
            grid.Controls.Add(RowLabel(caption), 0, row);
            grid.Controls.Add(ac, 1, row);
            ac.Margin = new Padding(2, 3, 12, 3);
            dc.Margin = new Padding(2, 3, 2, 3);
            grid.Controls.Add(dc, 2, row);
        }

        private void Prepare(ComboBox box, int width)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Width = width;
            box.Font = Font;
            box.TabStop = false;
            foreach (string s in Power.ActionLabels) { box.Items.Add(s); }
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
                ComboBox cb = c as ComboBox;
                if (cb != null)
                {
                    cb.BackColor = theme.InputBack;
                    cb.ForeColor = theme.InputText;
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

        // ------------------------------------------------------------------ 读状态

        /// <summary>读一遍当前状态：下拉框填成"当前生效那个方案"的值（读不到就用 Windows 的常规默认），
        /// 下面那块写体检报告。</summary>
        private void RefreshState()
        {
            if (_busy) { return; }
            SetBusyUi(true);
            _status.Text = "正在读电源方案…";
            Application.DoEvents();
            string error;
            List<PowerScheme> list = Power.Schemes(out error);
            _schemeCount = list.Count;
            if (list.Count == 0)
            {
                _report.Text = "读不了这台机器的电源方案。" + Environment.NewLine
                    + "  " + error + Environment.NewLine + Environment.NewLine
                    + "  （电源方案要用系统自带的 powercfg.exe 读，精简版系统里可能被删掉了。）";
                _status.Text = "读不到电源方案，这一页改不了。";
                SetBusyUi(false);
                UpdateButtons();
                return;
            }

            PowerScheme cur = null;
            foreach (PowerScheme s in list)
            {
                if (s.Active) { cur = s; break; }
            }
            if (cur == null) { cur = list[0]; }
            // 当前方案没单独设过的项：拿"大多数方案用的值"兜底（都没有就用系统常见默认），
            // 不然下拉框会停在「不采取任何操作」上，看着像"我本来是关机的，它给我改成不操作了"。
            int ml = Power.Majority(list, 0), mp = Power.Majority(list, 1), ms = Power.Majority(list, 2);
            Pick(_lidAc, Known(cur.LidAc, ml, 3)); Pick(_lidDc, Known(cur.LidDc, ml, 3));
            Pick(_pwrAc, Known(cur.PwrAc, mp, 3)); Pick(_pwrDc, Known(cur.PwrDc, mp, 3));
            Pick(_slpAc, Known(cur.SlpAc, ms, 1)); Pick(_slpDc, Known(cur.SlpDc, ms, 1));
            _visible.Checked = !Power.Hidden();
            // 顶部那行"现在会怎样"：写的是**当前方案自己的值**（没单独设过就直说），
            // 不拿下面下拉框的兜底值冒充 —— 那是两回事。
            _now.Text = "现在（当前方案「" + cur.Label + "」）：合上盖子 → 接电源「" + NowText(cur.LidAc)
                + "」，用电池「" + NowText(cur.LidDc) + "」；按电源按钮 → 接电源「" + NowText(cur.PwrAc)
                + "」，用电池「" + NowText(cur.PwrDc) + "」。";

            int thirdParty = 0;
            foreach (PowerScheme s in list)
            {
                if (s.ThirdParty) { thirdParty++; }
            }
            _status.Text = "共 " + list.Count.ToString(CultureInfo.InvariantCulture) + " 个电源方案；当前生效的是「"
                + cur.Label + "」" + (cur.ThirdParty ? "（不是 Windows 自带的）" : "")
                + "；上面填的是它现在的值（它没单独设过的项，用大多数方案的值兜底）。"
                + (thirdParty > 0 ? "（有 " + thirdParty.ToString(CultureInfo.InvariantCulture) + " 个不是 Windows 自带的方案，它们也会一起改。）" : "");
            _report.Text = Power.Fit();
            SetBusyUi(false);
            UpdateButtons();
        }

        private static void Pick(ComboBox box, int value)
        {
            if (value < 0) { value = 0; }
            if (value >= box.Items.Count) { value = box.Items.Count - 1; }
            box.SelectedIndex = value;
        }

        /// <summary>「现在」那一行用：没单独设过就直说，不拿兜底值冒充当前值。</summary>
        private static string NowText(int value)
        {
            return value < 0 ? "没单独设" : Power.ActionLabel(value);
        }

        /// <summary>下拉框先填什么：当前方案的值 → 大多数方案的值 → 系统常见默认。</summary>
        private static int Known(int value, int majority, int fallback)
        {
            if (value >= 0) { return value; }
            if (majority >= 0) { return majority; }
            return fallback;
        }

        private static int Picked(ComboBox box)
        {
            return box.SelectedIndex < 0 ? 0 : box.SelectedIndex;
        }

        private void SetBusyUi(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _apply.Enabled = !busy;
            _restore.Enabled = !busy;
            _refresh.Enabled = !busy;
            _visible.Enabled = !busy;
            foreach (ComboBox b in new ComboBox[] { _lidAc, _lidDc, _pwrAc, _pwrDc, _slpAc, _slpDc })
            {
                b.Enabled = !busy;
            }
        }

        private void UpdateButtons()
        {
            _apply.Enabled = !_busy && _schemeCount > 0;
        }

        private void Flash(string text)
        {
            _status.Text = text;
            _status.ForeColor = _theme.Danger;
        }

        // ------------------------------------------------------------------ 应用

        private void DoApply()
        {
            if (_busy) { return; }
            if (!Consent.EnsureAccepted(this, _theme, "要改动系统的「合盖与电源键动作」"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            int lidAc = Picked(_lidAc), lidDc = Picked(_lidDc);
            int pwrAc = Picked(_pwrAc), pwrDc = Picked(_pwrDc);
            int slpAc = Picked(_slpAc), slpDc = Picked(_slpDc);

            StringBuilder body = new StringBuilder();
            body.Append("会把全部 ").Append(_schemeCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" 个电源方案都改成：");
            body.Append("  合上盖子：接电源「").Append(Power.ActionLabel(lidAc)).Append("」，用电池「")
                .Append(Power.ActionLabel(lidDc)).AppendLine("」");
            body.Append("  按电源按钮：接电源「").Append(Power.ActionLabel(pwrAc)).Append("」，用电池「")
                .Append(Power.ActionLabel(pwrDc)).AppendLine("」");
            body.Append("  按睡眠按钮：接电源「").Append(Power.ActionLabel(slpAc)).Append("」，用电池「")
                .Append(Power.ActionLabel(slpDc)).AppendLine("」");
            body.AppendLine();
            body.AppendLine("改之前会先把每个方案现在的值记下来（记在：" + Power.BackupPath
                + "），随时可以点「还原成改前的值」写回去。");
            body.AppendLine("写的是系统自带的 powercfg，写完会一个一个读回来核对。");
            if (_visible.Checked != !Power.Hidden())
            {
                body.AppendLine();
                body.AppendLine(_visible.Checked
                    ? "另外：会让「选择关闭盖子的功能」这一页出现在控制面板里。"
                    : "另外：会把「选择关闭盖子的功能」这一页重新藏起来（和系统默认一样）。");
            }
            if ((lidAc == 2 || lidDc == 2 || pwrAc == 2 || pwrDc == 2 || slpAc == 2 || slpDc == 2)
                && !Power.HibernateEnabled())
            {
                body.AppendLine();
                body.AppendLine("注意：这台机器的休眠是关着的，选「休眠」的地方等于什么都不做"
                    + "（想用休眠要先打开休眠）。");
            }

            using (ConfirmForm f = new ConfirmForm("要改电源方案吗？", body.ToString(), "应用", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (!Launcher.IsAdmin())
            {
                OfferElevation("应用");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在改（所有方案都要写一遍，要几秒钟）…";
            Logger.Write("合盖与电源键动作", "应用：合盖 " + lidAc + "/" + lidDc + "，电源按钮 " + pwrAc + "/" + pwrDc
                + "，睡眠按钮 " + slpAc + "/" + slpDc + "（所有方案）");
            bool wantVisible = _visible.Checked;
            Thread t = new Thread(delegate ()
            {
                bool okValue = false, okVisible = true;
                string rep = Power.ApplyValues(lidAc, lidDc, pwrAc, pwrDc, slpAc, slpDc, out okValue);
                string vis = "";
                bool hidden = Power.Hidden();
                if (wantVisible == hidden)
                {
                    vis = Environment.NewLine + Power.SetHidden(!wantVisible, out okVisible);
                }
                bool ok = okValue && okVisible;
                string text = rep + vis;
                try { BeginInvoke((MethodInvoker)delegate { FinishBusy(text, ok, "应用"); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ------------------------------------------------------------------ 还原

        private void DoRestore()
        {
            if (_busy) { return; }
            if (!Consent.EnsureAccepted(this, _theme, "要还原系统的「合盖与电源键动作」"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            int n = Power.RecordCount();
            if (n == 0)
            {
                MessageBox.Show(this, "还没有要还原的东西：这一页的电源设置还没改过。",
                    "还原成改前的值", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StringBuilder body = new StringBuilder();
            body.Append("会把 ").Append(n.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" 个电源方案的合盖 / 电源按钮 / 睡眠按钮，按改之前的原值写回去。");
            body.AppendLine();
            body.AppendLine("原值记录：" + Power.BackupPath);
            body.AppendLine("（还原完这份记录就清空了 —— 下次再改会重新记当时的原值。）");
            using (ConfirmForm f = new ConfirmForm("要把电源方案还原成改前的值吗？", body.ToString(), "还原", _theme))
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
            _status.Text = "正在按原值写回去…";
            Logger.Write("合盖与电源键动作", "还原成改前的值（" + n.ToString(CultureInfo.InvariantCulture) + " 个方案）");
            Thread t = new Thread(delegate ()
            {
                bool ok = false;
                string rep = Power.Restore(out ok);
                try { BeginInvoke((MethodInvoker)delegate { FinishBusy(rep, ok, "还原"); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void FinishBusy(string report, bool ok, string what)
        {
            if (IsDisposed) { return; }
            SetBusyUi(false);
            _report.Text = report;
            _status.ForeColor = ok ? _theme.BarText : _theme.Danger;
            _status.Text = ok
                ? (what + "完成 —— 结果在上面的报告里；点「刷新」可以再看一遍体检。")
                : (what + "没全部成功 —— 上面带 × 的就是没成的那些（多半是缺管理员权限）。");
            Logger.Write("合盖与电源键动作", (ok ? "" : "部分失败 · ") + what + "：" + report.Trim());
            UpdateButtons();
        }

        // ------------------------------------------------------------------ 不是管理员

        /// <summary>不是管理员时的两条路（同「右键菜单管理」）：说清楚 → 弹一次 UAC 带管理员重开这个窗口
        /// → 重开之后**要再点一次**。命令行那条 `power` 只开窗口，不带任何"要改什么"的参数。</summary>
        private void OfferElevation(string what)
        {
            string body = "改电源方案（合盖 / 电源按钮 / 睡眠按钮）要用管理员权限才行。" + Environment.NewLine
                + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开。"
                + "因为写系统设置只能由你在窗口里点出来，所以重开之后要再点一次「" + what + "」才会真的生效。";
            using (ConfirmForm f = new ConfirmForm("需要管理员权限", body, "确定", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath, "power");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("合盖与电源键动作", "不是管理员：已请求以管理员身份重开窗口");
                Close();
            }
            catch (Exception ex)
            {
                Flash("请求管理员权限失败：" + ex.Message);
                Logger.Write("合盖与电源键动作", "请求管理员权限失败：" + ex.Message);
            }
        }
    }
}
