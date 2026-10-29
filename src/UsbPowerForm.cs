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
    /// <summary>「USB 省电（断连）」窗口（2026-10-28 用户提的第 2 件事）：把 USB 设备扫成一张列表，
    /// 每行念出它在「设备管理器 → 电源管理」那一页上的设置，选中就能关掉省电。
    ///
    /// 治的是什么：Windows 默认允许"为了省电把这个 USB 设备关掉"，空闲一会儿真把它断了 ——
    /// 鼠标卡一下、U 盘突然掉、移动硬盘认不到、外置网卡断线。
    ///
    /// 三条口径（和「此电脑多余图标」窗口一致）：
    /// ① 点开这个窗口**什么都不改**（只读注册表），所以这里不弹确认框；
    /// ② 「关掉省电」「还原成关闭前」每一次都过条款门 + 确认框；不是管理员时弹一次 UAC 带管理员重开窗口；
    /// ③ 写前把每个值原本有没有 / 什么数据记进 TSV（记不下来就一个设备都不动），写完逐个读回来核对。
    ///
    /// ⚠️ 改完要**拔一下设备再插上**（或者重启）才生效 —— 驱动是在设备枚举时读这几个值的。</summary>
    internal sealed class UsbPowerForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _filter = new TextBox();
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly Button _offOne = new Button();
        private readonly Button _offAll = new Button();
        private readonly Button _restore = new Button();

        private volatile bool _busy = false;
        private List<UsbPowerDevice> _all = new List<UsbPowerDevice>();

        public UsbPowerForm(Theme theme)
        {
            _theme = theme;
            Text = "USB 省电（设备自己断连 / U 盘突然掉）";
            ClientSize = new Size(980, 580);
            MinimumSize = new Size(840, 460);
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
            intro.MaximumSize = new Size(930, 0);
            intro.Text = "U 盘突然掉、鼠标卡一下、移动硬盘认不到、外置网卡断线 —— 常见原因之一是 Windows"
                + "「允许计算机关闭此设备以节约电源」：空闲一会儿它真把这个 USB 设备断了。这一页把 USB 设备"
                + "扫成一张列表（每行念出它现在的设置），选中点「关掉省电」就改成不允许；"
                + "系统驱动（集线器 / 根集线器）也在列表里，一般不用动它们。改完要拔一下再插上（或重启）才生效。";
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
            cap.Text = "过滤（名称 / VID / 驱动）：";
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
            _list.Columns.Add("设备", 300);
            _list.Columns.Add("电源管理", 170);
            _list.Columns.Add("驱动", 110);
            _list.Columns.Add("VID / PID", 190);
            _list.Columns.Add("实例", 190);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.MouseDown += ListMouseDown;
            root.Controls.Add(_list, 0, 2);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(930, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _offOne.Text = "关掉省电（选中）…";
            StyleButton(_offOne, 160);
            _offOne.Click += delegate { DoOff(false); };
            _offAll.Text = "关掉全部…";
            StyleButton(_offAll, 120);
            _offAll.Click += delegate { DoOff(true); };
            _restore.Text = "还原成关闭前";
            StyleButton(_restore, 132);
            _restore.Click += delegate { DoRestore(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_offOne, 1, 0);
            buttons.Controls.Add(_offAll, 2, 0);
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
            _status.Text = "正在读 USB 设备的电源管理设置…";
            Application.DoEvents();

            List<UsbPowerDevice> list;
            try
            {
                list = UsbPower.Scan();
            }
            catch (Exception ex)
            {
                SetBusyUi(false);
                _status.ForeColor = _theme.Danger;
                _status.Text = "读不了这台机器的 USB 设备列表：" + ex.Message
                    + "（这一页读的是注册表：" + UsbPower.RootText() + "）";
                Logger.Write("USB 省电", "读失败：" + ex.Message);
                return;
            }
            _all = list;
            Fill();

            int on = 0, off = 0;
            foreach (UsbPowerDevice d in list)
            {
                if (d.State == "on") { on++; }
                else if (d.State == "off") { off++; }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("共 ").Append(list.Count.ToString(CultureInfo.InvariantCulture)).Append(" 个 USB 设备：允许省电 ")
                .Append(on.ToString(CultureInfo.InvariantCulture)).Append(" 个、已禁止省电 ")
                .Append(off.ToString(CultureInfo.InvariantCulture)).Append(" 个、其余跟随系统默认。");
            if (on > 0) { sb.Append("会自己断连的那些，选中它们点「关掉省电（选中）…」。"); }
            int pending = 0;
            try { pending = UsbPower.PendingDevices(); } catch { }
            if (pending > 0)
            {
                sb.Append("有 ").Append(pending.ToString(CultureInfo.InvariantCulture)).Append(" 个设备的改动可以点「还原成关闭前」写回去。");
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
                foreach (UsbPowerDevice d in _all)
                {
                    if (needle.Length > 0)
                    {
                        string hay = d.Name + " " + d.Instance + " " + d.Service + " " + d.StateText();
                        if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                    }
                    ListViewItem row = new ListViewItem(d.Name);
                    row.SubItems.Add(d.StateText());
                    row.SubItems.Add(d.Service.Length > 0 ? d.Service : "（没写驱动）");
                    row.SubItems.Add(d.Vendor);
                    row.SubItems.Add(d.Instance);
                    row.Tag = d;
                    if (d.State == "on") { row.ForeColor = _theme.Danger; }      // 会被关掉的标红
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
                UsbPowerDevice d = row.Tag as UsbPowerDevice;
                if (d != null && d.HasParams) { sel++; }
            }
            int all = 0;
            foreach (UsbPowerDevice d in _all) { if (d.HasParams) { all++; } }
            _offOne.Enabled = !_busy && sel > 0;
            _offOne.Text = sel > 0
                ? ("关掉省电 " + sel.ToString(CultureInfo.InvariantCulture) + " 个…")
                : "关掉省电（选中）…";
            _offAll.Enabled = !_busy && all > 0;
            int pending = 0;
            try { pending = UsbPower.PendingDevices(); } catch { }
            _restore.Enabled = !_busy && pending > 0;
        }

        private List<UsbPowerDevice> SelectedDevices()
        {
            List<UsbPowerDevice> list = new List<UsbPowerDevice>();
            foreach (ListViewItem row in _list.SelectedItems)
            {
                UsbPowerDevice d = row.Tag as UsbPowerDevice;
                if (d != null && d.HasParams) { list.Add(d); }
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

        // ------------------------------------------------------------------ 关省电

        private void DoOff(bool all)
        {
            if (_busy) { return; }
            List<UsbPowerDevice> targets = new List<UsbPowerDevice>();
            if (all)
            {
                foreach (UsbPowerDevice d in _all) { if (d.HasParams) { targets.Add(d); } }
            }
            else
            {
                targets = SelectedDevices();
            }
            if (targets.Count == 0)
            {
                Flash(all ? "这台机器上没读到带电源管理键的 USB 设备。" : "先选中设备（可以按住 Ctrl 多选）。");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要关闭这些 USB 设备的省电"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            StringBuilder body = new StringBuilder();
            body.Append("要关掉 ").Append(targets.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个 USB 设备的省电吗？");
            body.AppendLine();
            int shown = 0;
            foreach (UsbPowerDevice d in targets)
            {
                if (shown >= 8) { body.Append("  … 还有 ").AppendLine((targets.Count - shown).ToString(CultureInfo.InvariantCulture) + " 个"); break; }
                body.Append("  · ").Append(d.Name).Append("（").Append(d.Instance).AppendLine("）");
                shown++;
            }
            body.AppendLine();
            body.AppendLine("做的事：把它们「允许计算机关闭此设备以节约电源」的两个值写成 0（不允许）；");
            body.AppendLine("系统驱动（集线器 / 根集线器）也在里面 —— 不想动它们就只选你那个设备。");
            body.AppendLine("写完逐个读回来核对；原值记到：" + UsbPower.RecordFile());
            body.AppendLine("改完要拔一下设备再插上（或者重启）才生效。想退回原样点「还原成关闭前」。");
            using (ConfirmForm f = new ConfirmForm("要关掉这些 USB 设备的省电吗？", body.ToString(), "关掉省电", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (!Launcher.IsAdmin())
            {
                OfferElevation("关掉省电");
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在关掉 " + targets.Count.ToString(CultureInfo.InvariantCulture) + " 个设备的省电…";
            Application.DoEvents();
            Logger.Write("USB 省电", "关掉省电：" + targets.Count + " 个设备");
            UsbPowerReport rep = UsbPower.DisablePowerSave(targets);
            SetBusyUi(false);
            Logger.Write("USB 省电", "关掉省电" + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            RefreshState();
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = "关掉省电" + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
        }

        private void DoRestore()
        {
            if (_busy) { return; }
            int pending = 0;
            try { pending = UsbPower.PendingDevices(); } catch { }
            if (pending == 0)
            {
                Flash("没有待还原的记录（这台机器还没用这个窗口关过 USB 省电）。");
                return;
            }
            if (!Consent.EnsureAccepted(this, _theme, "要把 USB 省电设置恢复成关闭之前的样子"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }
            string body = "会把之前改过的 " + pending.ToString(CultureInfo.InvariantCulture) + " 个设备的电源管理值按记录写回去："
                + Environment.NewLine + Environment.NewLine
                + "  · 原来有的值 → 写回原来的数据；" + Environment.NewLine
                + "  · 原来没有的值 → 删掉（回到系统默认）。" + Environment.NewLine + Environment.NewLine
                + "记录文件：" + UsbPower.RecordFile();
            using (ConfirmForm f = new ConfirmForm("要还原成关闭前吗？", body, "还原", _theme))
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
            Logger.Write("USB 省电", "还原：" + pending + " 个设备");
            UsbPowerReport rep = UsbPower.Restore();
            SetBusyUi(false);
            Logger.Write("USB 省电", "还原" + (rep.Ok ? "完成：" : "部分失败：") + rep.Message);
            RefreshState();
            _status.ForeColor = rep.Ok ? _theme.BarText : _theme.Danger;
            _status.Text = "还原" + (rep.Ok ? "完成：" : "没全部成功：") + rep.Message;
        }

        // ------------------------------------------------------------------ 不是管理员

        private void OfferElevation(string what)
        {
            string body = "改 USB 设备的省电设置（写 " + UsbPower.RootText() + "）要用管理员权限才行。"
                + Environment.NewLine + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开。"
                + "因为写注册表只能由你在窗口里点出来，所以重开之后要再点一次「" + what + "」才会真的生效。";
            using (ConfirmForm f = new ConfirmForm("需要管理员权限", body, "确定", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath, "usbpower");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("USB 省电", "不是管理员：已请求以管理员身份重开窗口");
                Close();
            }
            catch (Exception ex)
            {
                Flash("请求管理员权限失败：" + ex.Message);
                Logger.Write("USB 省电", "请求管理员权限失败：" + ex.Message);
            }
        }
    }
}
