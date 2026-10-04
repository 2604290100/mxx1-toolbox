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
    /// <summary>「解除文件占用」的结果窗口。
    ///
    /// 从资源管理器右键点进来的（`Mxx1Toolbox.exe rightmenu unlock "&lt;路径&gt;"`），所以它自己就是
    /// 一个**独立的窗口进程**：不打开主界面，看完关掉就走。
    ///
    /// 界面上只做一件事：把"谁占着它"列清楚，让你勾要结束的程序。所以
    /// ① 系统关键进程列出来但**勾不动**（灰的 + 写明原因），explorer.exe 默认不勾；
    /// ② 结束前过一遍自家的确认窗口（右键菜单是误点高发区），并且把"会连带结束哪些子进程"写在确认框里；
    /// ③ **查不到就分三种情况说清楚**（这是 2026-10-04 用户报「右键文件夹没扫描到占用」之后改的）：
    ///    真的没人在用 / 确实被占着但名字报不出来 / 拦住你的是权限不是占用 —— 绝不混成一句
    ///    「没查到」让用户以为工具坏了；
    /// ④ **列出来的行不只是"占用"**（2026-10-04 又加）：真占着的（lock）、它自己在运行的（run）、
    ///    窗口里开着它的（open）—— 后两类不是占用，但恰好是用户最想问的两种情况
    ///    （"我明明开着它"、"文件夹说被占着却报不出是谁"）。</summary>
    internal sealed class UnlockForm : Form
    {
        /// <summary>列表里的一行：哪个程序，占着哪个文件。</summary>
        private sealed class Row
        {
            public FileLocker Locker;
            public string File = "";

            public Row(FileLocker locker, string file)
            {
                Locker = locker;
                File = file;
            }
        }

        private readonly string[] _paths;
        private readonly Theme _theme;
        private readonly List<Row> _rows = new List<Row>();
        private LockReport _report;
        private bool _busy;              // 正在后台扫句柄（这时别让按钮重复触发）

        private ListView _list;
        private Label _head;
        private Label _status;
        private Label _empty;
        private Label _hint;
        private Button _killBtn;
        private Button _forceBtn;

        public UnlockForm(string[] paths)
        {
            _paths = (paths == null) ? new string[0] : paths;
            _theme = Theme.Resolve(Settings.Load().Theme);

            Text = "解除文件占用";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(620, 300);
            BackColor = _theme.FormBack;
            try { Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point); }
            catch { }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.Padding = new Padding(14, 12, 14, 12);
            root.BackColor = _theme.FormBack;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _head = new Label();
            _head.AutoSize = true;
            _head.MaximumSize = new Size(570, 0);
            _head.ForeColor = _theme.InputText;
            _head.Margin = new Padding(2, 0, 2, 8);
            _head.Text = PathsText();
            root.Controls.Add(_head, 0, 0);

            _status = new Label();
            _status.AutoSize = true;
            _status.MaximumSize = new Size(570, 0);
            _status.ForeColor = _theme.BarText;
            _status.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(_status, 0, 1);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.BackColor = _theme.InputBack;
            _list.ForeColor = _theme.InputText;
            _list.Margin = new Padding(2, 0, 2, 8);
            _list.Columns.Add("程序", 140, HorizontalAlignment.Left);
            _list.Columns.Add("PID", 55, HorizontalAlignment.Left);
            _list.Columns.Add("占着的文件", 185, HorizontalAlignment.Left);
            _list.Columns.Add("说明", 190, HorizontalAlignment.Left);
            _list.ItemCheck += OnItemCheck;
            root.Controls.Add(_list, 0, 2);

            _empty = new Label();
            _empty.Dock = DockStyle.Fill;
            _empty.AutoSize = false;
            _empty.ForeColor = _theme.BarText;
            _empty.Margin = new Padding(2, 0, 2, 8);
            _empty.Visible = false;
            root.Controls.Add(_empty, 0, 2);

            _hint = new Label();
            _hint.AutoSize = true;
            _hint.MaximumSize = new Size(570, 0);
            _hint.ForeColor = _theme.BarText;
            _hint.Margin = new Padding(2, 0, 2, 10);
            _hint.Text = "勾上要结束的程序，再点「结束选中的进程」（它启动的子进程会一起结束 —— 安装包、启动器"
                + "都是父进程拉个子进程干活，只结束父进程的话窗口会留着）。系统关键程序是灰的，勾不动；"
                + "没锁住它、只是「它自己在运行」或者「窗口里开着它」的也会列出来，那是线索，不一定要结束。";
            root.Controls.Add(_hint, 0, 3);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.WrapContents = false;
            bar.Margin = new Padding(0);
            bar.BackColor = _theme.FormBack;
            _killBtn = MakeButton("结束选中的进程");
            _killBtn.Click += delegate { KillChecked(); };
            bar.Controls.Add(_killBtn);
            _forceBtn = MakeButton("强制解锁（不关程序）");
            _forceBtn.Click += delegate { ForceUnlock(); };
            bar.Controls.Add(_forceBtn);
            Button refresh = MakeButton("重新检查");
            refresh.Click += delegate { RefreshLockers(); };
            bar.Controls.Add(refresh);
            Button copy = MakeButton("复制路径");
            copy.Click += delegate { CopyPaths(); };
            bar.Controls.Add(copy);
            Button close = MakeButton("关闭");
            close.Click += delegate { Close(); };
            bar.Controls.Add(close);
            root.Controls.Add(bar, 0, 4);

            Controls.Add(root);
            // 故意不设 AcceptButton：这个窗口里按 Enter 应该是"什么都不做"，
            // 结束进程必须真的去点那个按钮（和 ConfirmForm 里"默认按钮是取消"同一个理由）。
            CancelButton = close;
            Native.ApplyDarkTitleBar(Handle, _theme.DarkMode);

            Shown += delegate
            {
                RefreshLockers();
                Activate();
            };
        }

        private Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(10, 4, 10, 4);
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.BackColor = _theme.ButtonBack;
            b.ForeColor = _theme.ButtonText;
            b.FlatAppearance.BorderColor = _theme.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = _theme.ButtonHover;
            b.FlatAppearance.MouseDownBackColor = _theme.ButtonPressed;
            b.Margin = new Padding(0, 0, 8, 0);
            return b;
        }

        private string PathsText()
        {
            if (_paths.Length == 0) { return "（没有给出路径）"; }
            if (_paths.Length == 1) { return _paths[0]; }
            StringBuilder sb = new StringBuilder();
            sb.Append(_paths.Length.ToString(CultureInfo.InvariantCulture)).Append(" 个路径：");
            int shown = 0;
            foreach (string p in _paths)
            {
                if (shown >= 4) { sb.Append(" 等").Append(_paths.Length.ToString(CultureInfo.InvariantCulture)).Append(" 个"); break; }
                if (shown > 0) { sb.Append("；"); }
                sb.Append(p);
                shown++;
            }
            return sb.ToString();
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            // 系统关键进程勾不动：把这次改动拨回去（ListView 允许在事件里改回去）。
            if (e.Index >= 0 && e.Index < _rows.Count && _rows[e.Index].Locker.Protected)
            {
                e.NewValue = e.CurrentValue;
            }
        }

        private bool HasRow(int pid, string file, string source)
        {
            foreach (Row r in _rows)
            {
                if (r.Locker.Pid == pid && r.Locker.Source == source
                    && string.Equals(r.File, file, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private void RefreshLockers()
        {
            _rows.Clear();
            _report = FileLock.Scan(_paths);
            foreach (LockHit h in _report.Hits)
            {
                foreach (FileLocker f in h.Lockers)
                {
                    // 去重键里带"线索种类"：同一个进程既占着它、又是"它自己在运行"时要列两行
                    // （后一句才是"为什么删不掉"的答案，不能被前一行吞掉）
                    if (HasRow(f.Pid, h.File, f.Source)) { continue; }
                    _rows.Add(new Row(f, h.File));
                }
            }

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (Row r in _rows)
            {
                ListViewItem it = new ListViewItem(r.Locker.Exe);
                it.SubItems.Add(r.Locker.Pid.ToString(CultureInfo.InvariantCulture));
                it.SubItems.Add(FileText(r.File));
                it.SubItems.Add(Explain(r.Locker));
                it.Checked = r.Locker.Checked && !r.Locker.Protected;
                if (r.Locker.Protected) { it.ForeColor = _theme.ButtonDisabledText; }
                _list.Items.Add(it);
            }
            _list.EndUpdate();

            bool any = _rows.Count > 0;
            _list.Visible = any;
            _empty.Visible = !any;
            if (any)
            {
                _status.Text = FoundHeadline();
                _empty.Text = "";
            }
            else
            {
                _status.Text = EmptyHeadline();
                _empty.Text = EmptyBody();
            }
            _killBtn.Enabled = true;
            UpdateSize();
        }

        /// <summary>这一批行里"真占着"的有几个（run / open 两类不是占用）。</summary>
        private int LockRowCount()
        {
            int n = 0;
            List<int> seen = new List<int>();
            foreach (Row r in _rows)
            {
                if (!r.Locker.IsLock || seen.Contains(r.Locker.Pid)) { continue; }
                seen.Add(r.Locker.Pid);
                n++;
            }
            return n;
        }

        private static string FileText(string file)
        {
            if (file.Length == 0) { return "（没定位到具体文件）"; }
            try
            {
                string name = System.IO.Path.GetFileName(file);
                return (name.Length > 0) ? name : file;
            }
            catch { return file; }
        }

        private string FoundHeadline()
        {
            StringBuilder sb = new StringBuilder();
            int locks = LockRowCount();
            int run = _report.RunCount;
            int open = _report.OpenCount;

            if (locks > 0)
            {
                sb.Append("查到 ").Append(locks.ToString(CultureInfo.InvariantCulture)).Append(" 个程序占着它");
                if (_report.FolderScanned)
                {
                    sb.Append("（文件夹里扫了 ").Append(_report.Scanned.ToString(CultureInfo.InvariantCulture)).Append(" 个文件");
                    int files = _report.LockedFiles().Count;
                    if (files > 1) { sb.Append("，命中在 ").Append(files.ToString(CultureInfo.InvariantCulture)).Append(" 个文件上"); }
                    sb.Append("）");
                }
                sb.Append("：");
                // 有"真占用"的时候也别忘了那两条线索：用户右键的多半就是个正在跑的安装包
                // （"删不掉"的真正原因就是它），只写在列表的「说明」列里容易被忽略（2026-10-04 实测）。
                if (run > 0 || open > 0)
                {
                    sb.Append(Environment.NewLine).Append("  另有 ");
                    if (run > 0)
                    {
                        sb.Append(run.ToString(CultureInfo.InvariantCulture)).Append(" 个程序是「它自己在运行」");
                    }
                    if (run > 0 && open > 0) { sb.Append("、"); }
                    if (open > 0)
                    {
                        sb.Append(open.ToString(CultureInfo.InvariantCulture)).Append(" 个窗口里开着它");
                    }
                    sb.Append("（哪几个看列表「说明」那一列）");
                }
                if (_report.DeleteNote.Length > 0) { sb.Append(Environment.NewLine).Append("  ").Append(_report.DeleteNote); }
            }
            else
            {
                // 一条"真占用"都没有，但列出来的每一行都是有用的话 —— 别把用户吓一跳。
                sb.Append("没有程序锁着它");
                if (run > 0 && open > 0) { sb.Append("；下面几行是「它自己在运行」和「窗口里开着它」的线索"); }
                else if (run > 0) { sb.Append("；下面那行是「它自己在运行」的线索"); }
                else if (open > 0) { sb.Append("；下面那行是「窗口里开着它」，它并没有锁住文件"); }
                sb.Append("：");
                if (_report.Verdict.Length > 0) { sb.Append(Environment.NewLine).Append("  ").Append(_report.Verdict); }
                // 「能不能删 / 改名」直接在结论里说清楚（用户真正要问的就是这句）
                if (_report.DeleteNote.Length > 0) { sb.Append(Environment.NewLine).Append("  ").Append(_report.DeleteNote); }
            }
            AppendNotes(sb);
            return sb.ToString();
        }

        private string EmptyHeadline()
        {
            if (_report == null) { return "还没查。"; }
            if (_report.Error.Length > 0) { return "这次查询本身没成功（不是「没人占用」）："; }
            if (!_report.VerdictExists) { return "路径没传过来："; }
            if (_report.VerdictDenied) { return "拦住它的不是占用，是权限："; }
            if (_report.VerdictLocked) { return "确实有程序占着它，但报不出是哪个程序："; }
            return "没查到占用它的程序：";
        }

        private string EmptyBody()
        {
            StringBuilder sb = new StringBuilder();
            if (_report == null) { return "点「重新检查」查一次。"; }

            if (_report.Error.Length > 0) { sb.Append("  ").Append(_report.Error).AppendLine().AppendLine(); }
            if (_report.Verdict.Length > 0) { sb.Append("  ").Append(_report.Verdict).AppendLine(); }
            if (_report.DeleteNote.Length > 0) { sb.Append("  ").Append(_report.DeleteNote).AppendLine(); }
            if (_report.FolderScanned)
            {
                sb.Append("  文件夹里扫了 ").Append(_report.Scanned.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个文件（往下 ").Append(FileLock.MaxScanDepth.ToString(CultureInfo.InvariantCulture))
                  .Append(" 层）");
                if (_report.Truncated) { sb.Append("，没扫完（文件夹太大 / 里面有软链接）"); }
                sb.AppendLine("。");
            }
            if (_report.BadFiles > 0)
            {
                sb.Append("  另有 ").Append(_report.BadFiles.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个路径系统不肯查（已经跳过了）。").AppendLine();
            }
            if (_report.Note.Length > 0) { sb.Append("  ").Append(_report.Note).AppendLine(); }
            if (_paths.Length > 1)
            {
                sb.Append("  多个路径时，上面那句自查结论只针对第一个路径。").AppendLine();
            }
            sb.AppendLine();

            if (_report.VerdictLocked)
            {
                sb.AppendLine("  为什么会报不出名字：Windows 这个接口只报当前用户看得见的进程；而且文件正被「运行中的程序」");
                sb.AppendLine("  用着的时候（.exe 自己、正在播放的媒体），那个程序压根不持有文件句柄，谁问都问不出来。可以试：");
                sb.AppendLine("  · 看看这个文件夹里有没有程序正开着（任务栏 / 任务管理器里的活动程序），先把它关掉再点「重新检查」；");
                sb.AppendLine("  · 关掉最近动过它的程序（Office / PDF 阅读器 / 播放器 / 压缩软件）再点「重新检查」；");
                sb.AppendLine("  · 用管理员身份打开工具箱（右键 exe，选「以管理员身份运行」），再从资源管理器右键一次；");
                sb.AppendLine("  · 实在找不到：注销一次（占用它的进程会跟着退出）。");
            }
            else if (_report.VerdictDenied)
            {
                sb.AppendLine("  这不是「哪个程序开着它」的问题：去文件的「属性」里看看只读、或者安全里的权限。");
            }
            else if (!_report.VerdictExists)
            {
                sb.AppendLine("  资源管理器没把真实路径传过来 —— 常见于「文件夹里的空白处」和「桌面空白处」");
                sb.AppendLine("  这两个位置（要用 %V）。点一次工具箱「右键增强」页的「装上…」会重写成正确写法。");
            }
            else
            {
                sb.AppendLine("  这条结论不是猜的：我刚刚自己试着独占打开它，成功了 —— 也就是说现在真的");
                sb.AppendLine("  没有程序占着它。如果它还是删不掉 / 改不了 / 改名不了，那多半是：");
                sb.AppendLine("  · 权限（ACL）或只读属性；");
                sb.AppendLine("  · 占用它的是内核态的东西（杀毒软件实时扫描、驱动），它不属于任何进程；");
                sb.AppendLine("  · 它自己是个正在运行的程序（可执行文件是内存映射，不算文件锁）；");
                sb.AppendLine("  · 你删的是文件夹，而拦住你的是它「里面」更深的文件（上面写了扫了几层）。");
            }
            return sb.ToString().TrimEnd();
        }

        private void AppendNotes(StringBuilder sb)
        {
            List<string> parts = new List<string>();
            if (_report.Note.Length > 0) { parts.Add(_report.Note); }
            if (_report.Truncated) { parts.Add("文件夹太大，没扫完（只扫了 " + _report.Scanned.ToString(CultureInfo.InvariantCulture) + " 个文件）"); }
            if (_report.BadFiles > 0) { parts.Add(_report.BadFiles.ToString(CultureInfo.InvariantCulture) + " 个路径系统不肯查，跳过了"); }
            if (_report.Error.Length > 0) { parts.Add(_report.Error); }
            if (parts.Count == 0) { return; }
            sb.Append(Environment.NewLine).Append("（注意：").Append(string.Join("；", parts.ToArray())).Append("）");
        }

        private static string Explain(FileLocker f)
        {
            // 系统给的是"友好显示名"（Windows PowerShell / Windows 资源管理器 / Windows Event Log），
            // 不是路径；进程名（pwsh.exe）是我们自己按 PID 反查的。两个都放，但别写成
            // 「资源管理器 · Windows 资源管理器」这种重复。
            List<string> parts = new List<string>();
            string bare = f.Exe;
            if (bare.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { bare = bare.Substring(0, bare.Length - 4); }
            bool dup = (f.Kind.Length > 0 && f.AppName.IndexOf(f.Kind, StringComparison.Ordinal) >= 0)
                || string.Equals(f.AppName, bare, StringComparison.OrdinalIgnoreCase);
            if (f.AppName.Length > 0 && !dup) { parts.Add(f.AppName); }
            if (f.Kind.Length > 0) { parts.Add(f.Kind); }
            if (f.Why.Length > 0) { parts.Add(f.Why); }
            if (f.Extra.Length > 0)
            {
                // run 带的是镜像路径（太长了，只留文件名）；open 带的是那个窗口的标题。
                parts.Add((f.Source == FileLocker.SourceOpen) ? ("窗口：" + Clip(f.Extra, 40)) : FileName(f.Extra));
            }
            return string.Join(" · ", parts.ToArray());
        }

        private static string Clip(string s, int max)
        {
            if (s == null) { return ""; }
            return (s.Length <= max) ? s : (s.Substring(0, max) + "…");
        }

        private static string FileName(string path)
        {
            try
            {
                string n = System.IO.Path.GetFileName(path);
                return (n == null) ? path : n;
            }
            catch { return path; }
        }

        private void UpdateSize()
        {
            int rows = _rows.Count;
            if (rows > 8) { rows = 8; }
            int h = 210 + rows * 20;
            if (h > 470) { h = 470; }
            ClientSize = new Size(620, h);
        }

        private void CopyPaths()
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, _paths));
                _status.Text = "路径已经复制到剪贴板。";
            }
            catch (Exception ex)
            {
                _status.Text = "复制不了：" + ex.Message;
            }
        }

        /// <summary>强制解锁：像火绒那样**不结束进程**，把对方手里的文件句柄直接抽掉。
        ///
        /// 两步：① 全系统扫句柄（几秒，放后台线程，别把窗口冻住）；② 结果拿去过一遍确认框
        /// （这是危险动作：从别人脚下抽走句柄，那个程序可能出错 / 丢数据），确认了才真动手。</summary>
        private void ForceUnlock()
        {
            if (_busy) { return; }
            _busy = true;
            _killBtn.Enabled = false;
            _forceBtn.Enabled = false;
            _status.Text = "正在扫全系统句柄（像火绒那样，几秒钟）…… 这不是卡死，是在干活。";

            List<string> targets = new List<string>();
            foreach (string p in _paths) { if (!Contains(targets, p)) { targets.Add(p); } }
            if (_report != null)
            {
                foreach (string f in _report.LockedFiles())
                {
                    if (!Contains(targets, f)) { targets.Add(f); }
                }
            }

            Thread t = new Thread(delegate()
            {
                List<HandleHit> hits = null;
                string note = "";
                try { hits = HandleUnlock.Find(targets.ToArray(), out note); }
                catch (Exception ex) { note = "扫句柄出错：" + ex.Message; hits = new List<HandleHit>(); }
                try { BeginInvoke((MethodInvoker)delegate { ForceUnlockReady(hits, note); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ForceUnlockReady(List<HandleHit> hits, string note)
        {
            _busy = false;
            _killBtn.Enabled = true;
            _forceBtn.Enabled = true;
            if (IsDisposed) { return; }

            if (hits == null) { hits = new List<HandleHit>(); }
            if (hits.Count == 0)
            {
                _status.Text = "没找到攥着它的句柄。" + Environment.NewLine + "  " + note
                    + Environment.NewLine + "  （Windows 里「谁开着它」有时就是查不出来：内核驱动、杀软实时扫描"
                    + "这类不属于任何进程；那种只能注销一次。）";
                return;
            }

            StringBuilder who = new StringBuilder();
            int protectedCount = 0;
            foreach (HandleHit h in hits)
            {
                if (h.Protected) { protectedCount++; }
                if (who.Length > 0) { who.Append("、"); }
                who.Append(h.Exe).Append("（PID ").Append(h.Pid.ToString(CultureInfo.InvariantCulture)).Append("）");
            }

            ToolItem item = new ToolItem();
            item.Name = "强制解锁（抽掉句柄）";
            item.Id = "rightmenu.unlock.force";
            item.Danger = true;
            item.Hint = "会从下面这些程序手里把它「抢」过来：" + who.ToString()
                + Environment.NewLine + note
                + Environment.NewLine + "做法跟火绒的「解锁占用」一样：不结束进程，只把那几个句柄关掉。"
                + Environment.NewLine + "风险：程序手里的句柄被突然抽走，它可能报错 / 存不上盘。"
                + Environment.NewLine + "没保存的东西先存一下；能接受再点「执行」。"
                + ((protectedCount > 0)
                    ? (Environment.NewLine + "（其中有 " + protectedCount.ToString(CultureInfo.InvariantCulture)
                        + " 个是系统关键进程，不会动它们。）")
                    : "");

            DialogResult answer;
            using (ConfirmForm f = new ConfirmForm(item, "", false, false, _theme))
            {
                answer = f.ShowDialog(this);
            }
            if (answer != DialogResult.OK)
            {
                _status.Text = "已取消（一个句柄都没动）。找到的这些句柄：" + who.ToString();
                return;
            }

            string report = HandleUnlock.Release(hits);
            Logger.Write("解除文件占用", "强制解锁（抽句柄）：" + who.ToString() + Environment.NewLine + report);
            RefreshLockers();
            _status.Text = "强制解锁的结果：" + Environment.NewLine + report;
        }

        private static bool Contains(List<string> list, string s)
        {
            foreach (string x in list) { if (string.Equals(x, s, StringComparison.OrdinalIgnoreCase)) { return true; } }
            return false;
        }

        private void KillChecked()
        {
            List<FileLocker> chosen = new List<FileLocker>();
            foreach (int i in _list.CheckedIndices)
            {
                if (i >= 0 && i < _rows.Count) { chosen.Add(_rows[i].Locker); }
            }
            if (chosen.Count == 0)
            {
                _status.Text = "没勾选任何程序 —— 在上面的列表里勾一个再点这个按钮。";
                return;
            }

            StringBuilder names = new StringBuilder();
            List<int> seen = new List<int>();
            foreach (FileLocker f in chosen)
            {
                if (seen.Contains(f.Pid)) { continue; }
                seen.Add(f.Pid);
                if (names.Length > 0) { names.Append("、"); }
                names.Append(f.Exe).Append("（PID ").Append(f.Pid.ToString(CultureInfo.InvariantCulture)).Append("）");
            }

            // 会连带结束的子进程先说清楚（用户 2026-10-04 报的"文件占用解除了、窗口还在"就是它）
            List<FileLocker> kids = FileLock.ChildrenOf(chosen);
            StringBuilder kidText = new StringBuilder();
            foreach (FileLocker k in kids)
            {
                if (kidText.Length > 0) { kidText.Append("、"); }
                kidText.Append(k.Exe).Append("（PID ").Append(k.Pid.ToString(CultureInfo.InvariantCulture)).Append("）");
            }

            ToolItem t = new ToolItem();
            t.Name = "结束选中的进程";
            t.Id = "rightmenu.unlock.kill";
            t.Danger = true;
            t.Hint = "结束 " + names.ToString() + " 之后，文件就不再被它们占着了。"
                + Environment.NewLine + "没保存的东西会丢 —— 结束之前先回那个程序里存一下。"
                + ((kids.Count > 0)
                    ? (Environment.NewLine + "会连带结束它启动的 " + kids.Count.ToString(CultureInfo.InvariantCulture)
                        + " 个子进程：" + kidText.ToString())
                    : "");
            DialogResult answer;
            using (ConfirmForm f = new ConfirmForm(t, "", false, false, _theme))
            {
                answer = f.ShowDialog(this);
            }
            if (answer != DialogResult.OK)
            {
                _status.Text = "已取消（没有结束任何程序）。";
                return;
            }

            string report = FileLock.Kill(chosen);
            Logger.Write("解除文件占用", "结束进程：" + names.ToString() + Environment.NewLine + report);
            RefreshLockers();
            _status.Text = "结束的结果：" + Environment.NewLine + report;
        }
    }
}
