// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「文件哈希校验 MD5/SHA256」的计算部分。
    ///
    /// 界面（HashForm）和命令行（`Mxx1Toolbox.exe hash <文件>`）**共用这一份实现** —— 不然
    /// "界面上算出来的值"和"命令行算出来的值"就有两份代码，改一处忘一处，而校验码这种东西
    /// 一旦不一致，用户拿它去核对下载的安装包就会得出错误结论。
    ///
    /// 三条底线（写在代码里，不是写在文档里）：
    /// ① **只读**：用 FileShare.ReadWrite 打开，不独占、不改时间戳、不写一个字节 —— 一个正在被
    ///    别的程序写着的文件也照样能算（算出来的可能是"那一瞬间的半成品"，但那是用户自己的判断）；
    /// ② **不联网**：只有本机的 System.Security.Cryptography，不上传文件名、不上传内容；
    /// ③ **不猜**：给出的校验值和哪个算法都不像时，说"看不出这是哪种校验码"，不硬说"不一致"。</summary>
    internal static class HashTool
    {
        /// <summary>一次读 1 MB：再大对速度没帮助（磁盘才是瓶颈），再小进度条会抖。</summary>
        private const int ChunkSize = 1024 * 1024;

        /// <summary>进度回调：每读一块报一次（只在整数百分比变化时真的回调，见 Compute）。</summary>
        public delegate void ProgressSink(long done, long total);

        /// <summary>一次计算的结果。Error 非空 = 没算成（Ok 为 false）。</summary>
        public sealed class HashResult
        {
            public bool Ok = false;
            public string Md5 = "";
            public string Sha256 = "";
            public long Size = -1;
            public DateTime Modified = DateTime.MinValue;
            public string Error = "";
        }

        /// <summary>算出一个文件的 MD5 + SHA256（一趟读完，两个算法同时喂）。
        /// 大文件可能跑几十秒，所以这里不做任何界面操作 —— 调用方自己放后台线程。</summary>
        public static HashResult Compute(string path, ProgressSink progress)
        {
            HashResult r = new HashResult();
            if (path == null || path.Trim().Length == 0) { r.Error = "没有指定文件"; return r; }
            try
            {
                if (Directory.Exists(path)) { r.Error = "这是文件夹，算不了哈希，请选一个文件：" + path; return r; }
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists) { r.Error = "找不到这个文件：" + path; return r; }
                r.Size = fi.Length;
                r.Modified = fi.LastWriteTime;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (MD5 md5 = MD5.Create())
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] buf = new byte[ChunkSize];
                    long done = 0;
                    int lastPct = -1;
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    {
                        md5.TransformBlock(buf, 0, n, null, 0);
                        sha.TransformBlock(buf, 0, n, null, 0);
                        done += n;
                        if (progress != null)
                        {
                            int pct = (r.Size > 0) ? (int)(done * 100 / r.Size) : 100;
                            if (pct != lastPct) { lastPct = pct; progress(done, r.Size); }
                        }
                    }
                    md5.TransformFinalBlock(new byte[0], 0, 0);
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    r.Md5 = ToHex(md5.Hash);
                    r.Sha256 = ToHex(sha.Hash);
                }
                r.Ok = true;
            }
            catch (Exception ex)
            {
                r.Error = FriendlyError(path, ex);
            }
            return r;
        }

        /// <summary>失败也要说人话：用户看到的不能是 "IOException: 另一个程序正在使用此文件"。</summary>
        private static string FriendlyError(string path, Exception ex)
        {
            if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return "找不到这个文件：" + path;
            }
            if (ex is UnauthorizedAccessException)
            {
                return "没有权限读这个文件（它在受保护的目录里，或者被系统拦着）：" + path;
            }
            if (ex is PathTooLongException)
            {
                return "这个路径太长了，Windows 读不了：" + path;
            }
            if (ex is IOException)
            {
                return "读的时候出错了（文件可能正被别的程序独占着）：" + ex.Message;
            }
            return "读取失败：" + ex.Message;
        }

        private static string ToHex(byte[] data)
        {
            if (data == null) { return ""; }
            System.Text.StringBuilder sb = new System.Text.StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++) { sb.Append(data[i].ToString("x2", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        /// <summary>把用户粘进来的校验值洗成能比较的样子：去掉空格 / 换行 / 短横线（网站经常写成
        /// "AB-CD-EF…" 或者分好几行），并且统一成小写。大小写不算差别 —— 十六进制本来就不分。</summary>
        public static string Normalize(string code)
        {
            if (code == null) { return ""; }
            System.Text.StringBuilder sb = new System.Text.StringBuilder(code.Length);
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (char.IsWhiteSpace(c) || c == '-') { continue; }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>MD5 是 32 位十六进制、SHA256 是 64 位 —— 按长度认算法，不按用户选的那个下拉框
        /// 认（少一个要选的东西，也少一种"选错了"的可能）。认不出来返回空串。</summary>
        public static string AlgorithmOf(string normalized)
        {
            if (IsHex(normalized, 32)) { return "md5"; }
            if (IsHex(normalized, 64)) { return "sha256"; }
            return "";
        }

        private static bool IsHex(string s, int len)
        {
            if (s == null || s.Length != len) { return false; }
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex) { return false; }
            }
            return true;
        }

        /// <summary>拿"网站上给的值"和"算出来的值"比一次，回一句人话。
        /// matched 只在"认出算法"时才有效；algo 为空 = 认不出（这时 matched 一律 false，
        /// 但文案要说清楚是"认不出"而不是"不一致"）。</summary>
        public static string Verdict(string expected, HashResult r, out bool matched, out string algo)
        {
            matched = false;
            algo = "";
            string norm = Normalize(expected);
            if (norm.Length == 0) { return "先在右边粘上网站上给出的校验值，再点「对比」。"; }
            if (r == null || !r.Ok) { return "还没有算出这个文件的校验值，先选一个文件。"; }
            algo = AlgorithmOf(norm);
            if (algo.Length == 0)
            {
                return "看不出这是哪种校验码：MD5 是 32 位、SHA256 是 64 位，你粘进来的是 " +
                    norm.Length.ToString(CultureInfo.InvariantCulture) + " 位。";
            }
            string mine = (algo == "md5") ? r.Md5 : r.Sha256;
            matched = (norm == mine);
            string name = (algo == "md5") ? "MD5" : "SHA256";
            if (matched) { return "一致：和这个文件算出来的 " + name + " 完全一样，文件是完好的。"; }
            return "不一致：和这个文件算出来的 " + name + " 不一样。文件可能没下载全（重新下一遍），"
                + "也可能被改过（别装它）。";
        }

        /// <summary>给人看的大小：1024 进制 + 一位小数，1 KB 以下直接说字节。</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 0) { return "未知"; }
            if (bytes < 1024) { return bytes.ToString(CultureInfo.InvariantCulture) + " 字节"; }
            double kb = bytes / 1024.0;
            if (kb < 1024) { return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB"; }
            double mb = kb / 1024.0;
            if (mb < 1024) { return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB"; }
            return (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }
    }

    /// <summary>「文件哈希校验」窗口：选一个文件 → 算出 MD5 / SHA256 → 复制 → 和网站上给的值对一下。
    ///
    /// 两个设计取舍（都是被用户报过的问题逼出来的）：
    /// ① **算的时候不卡窗口**：算 SHA256 要读完整个文件，装一个几 GB 的镜像就是几十秒 —— 上一轮的
    ///    「解除文件占用」窗口就是栽在"扫描期间窗口完全没响应"上（实测 6.5 秒假死，用户报了）。
    ///    所以计算放后台线程，进度写在窗口里，窗口一直能拖能关；
    /// ② **不猜算法**：用户粘 32 位就是 MD5、64 位就是 SHA256，认不出来就说认不出来
    ///    （见 HashTool.Verdict），不拿"不一致"去吓人。</summary>
    internal sealed class HashForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _path = new TextBox();
        private readonly TextBox _md5 = new TextBox();
        private readonly TextBox _sha256 = new TextBox();
        private readonly TextBox _expect = new TextBox();
        private readonly Label _fileInfo = new Label();
        private readonly Label _message = new Label();
        private readonly Button _pick = new Button();
        private readonly Button _again = new Button();
        private readonly Button _compare = new Button();

        private HashTool.HashResult _result;
        private string _current = "";
        private volatile bool _busy = false;

        public HashForm(Theme theme)
        {
            _theme = theme;
            Text = "文件哈希校验（MD5 / SHA256）";
            ClientSize = new Size(660, 430);
            MinimumSize = new Size(560, 380);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;
            AllowDrop = true;   // 直接把文件从资源管理器拖进来更省事
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 9;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 8; i++) { root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(620, 0);
            intro.Text = "校验文件完整性：算出 MD5 / SHA256，和下载页面给出的值对一下，"
                + "一致就说明文件下全了、没被改过。只在本机计算，不上传文件、不改动文件。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            // ① 选文件
            TableLayoutPanel fileRow = NewRow();
            _pick.Text = "选择文件…";
            StyleButton(_pick, 110);
            _pick.Click += delegate { PickFile(); };
            _path.ReadOnly = true;
            _path.Dock = DockStyle.Fill;
            fileRow.Controls.Add(RowLabel("文件"), 0, 0);
            fileRow.Controls.Add(_path, 1, 0);
            fileRow.Controls.Add(_pick, 2, 0);
            root.Controls.Add(fileRow, 0, 1);

            // ② 两个校验值 + 各自的复制按钮
            root.Controls.Add(HashRow("MD5", _md5, "复制 MD5"), 0, 2);
            root.Controls.Add(HashRow("SHA256", _sha256, "复制 SHA256"), 0, 3);

            _fileInfo.AutoSize = true;
            _fileInfo.ForeColor = theme.BarText;
            _fileInfo.Text = "还没有选文件。";
            _fileInfo.Margin = new Padding(2, 4, 2, 8);
            root.Controls.Add(_fileInfo, 0, 4);

            // ③ 和网站上的值对比
            TableLayoutPanel expectRow = NewRow();
            _compare.Text = "对比";
            StyleButton(_compare, 72);
            _compare.Click += delegate { RunCompare(); };
            _expect.Dock = DockStyle.Fill;
            _expect.TextChanged += delegate { /* 算完之后自动比，见 FinishCompute */ };
            expectRow.Controls.Add(RowLabel("对照值"), 0, 0);
            expectRow.Controls.Add(_expect, 1, 0);
            expectRow.Controls.Add(_compare, 2, 0);
            root.Controls.Add(expectRow, 0, 5);

            _message.AutoSize = true;
            _message.MaximumSize = new Size(620, 0);
            _message.Margin = new Padding(2, 6, 2, 6);
            root.Controls.Add(_message, 0, 6);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 4;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _again.Text = "重新计算";
            StyleButton(_again, 96);
            _again.Click += delegate { StartCompute(_current); };
            Button copyAll = new Button();
            copyAll.Text = "复制全部";
            StyleButton(copyAll, 88);
            copyAll.Click += delegate { CopyAll(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 72);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_again, 1, 0);
            buttons.Controls.Add(copyAll, 2, 0);
            buttons.Controls.Add(close, 3, 0);
            root.Controls.Add(buttons, 0, 7);

            root.Controls.Add(new Label(), 0, 8);
            Controls.Add(root);
            CancelButton = close;
            AcceptButton = _compare;   // 粘完对照值直接回车 = 对比

            ApplyTheme(theme);
            SetBusyUi(false);
        }

        private static TableLayoutPanel NewRow()
        {
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.AutoSize = true;
            p.ColumnCount = 3;
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.Margin = new Padding(0);
            p.RowCount = 1;
            return p;
        }

        private static Label RowLabel(string text)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = text;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(2, 6, 8, 4);
            return l;
        }

        /// <summary>一行 = 算法名 + 校验值 + 复制。</summary>
        private TableLayoutPanel HashRow(string caption, TextBox box, string copyText)
        {
            TableLayoutPanel p = NewRow();
            box.ReadOnly = true;
            box.Dock = DockStyle.Fill;
            box.Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);
            Button copy = new Button();
            copy.Text = copyText;
            StyleButton(copy, 96);
            TextBox target = box;
            string what = caption;
            copy.Click += delegate { CopyValue(target, what); };
            p.Controls.Add(RowLabel(caption), 0, 0);
            p.Controls.Add(box, 1, 0);
            p.Controls.Add(copy, 2, 0);
            return p;
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

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) { return; }
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) { return; }
            StartCompute(files[0]);
        }

        private void PickFile()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要校验的文件";
                dlg.Filter = "所有文件 (*.*)|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) { return; }
                StartCompute(dlg.FileName);
            }
        }

        /// <summary>选好文件（或拖进来）就开始算。计算在后台线程，界面照样能拖能关。</summary>
        private void StartCompute(string path)
        {
            if (_busy) { return; }
            if (path == null || path.Trim().Length == 0) { return; }
            _current = path;
            _path.Text = path;
            _result = null;
            _md5.Text = "";
            _sha256.Text = "";
            _fileInfo.Text = "正在读这个文件…";
            _message.Text = "正在计算…（大文件要等一会儿）";
            _message.ForeColor = _theme.BarText;
            SetBusyUi(true);

            string target = path;
            Thread t = new Thread(delegate ()
            {
                HashTool.HashResult r = HashTool.Compute(target, delegate (long done, long total)
                {
                    ReportProgress(done, total);
                });
                try { BeginInvoke((MethodInvoker)delegate { FinishCompute(r); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ReportProgress(long done, long total)
        {
            if (IsDisposed || !IsHandleCreated) { return; }
            string text = (total > 0)
                ? "正在计算… " + (done * 100 / total).ToString(CultureInfo.InvariantCulture) + "%（"
                    + HashTool.FormatSize(done) + " / " + HashTool.FormatSize(total) + "）"
                : "正在计算…";
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (_busy) { _message.Text = text; }
                });
            }
            catch { }
        }

        private void FinishCompute(HashTool.HashResult r)
        {
            if (IsDisposed) { return; }
            _busy = false;
            SetBusyUi(false);
            _result = r;
            if (!r.Ok)
            {
                _fileInfo.Text = "没有算出结果。";
                _message.Text = r.Error;
                _message.ForeColor = _theme.Danger;
                return;
            }
            _md5.Text = r.Md5;
            _sha256.Text = r.Sha256;
            string modified = (r.Modified == DateTime.MinValue)
                ? ""
                : " · 修改时间 " + r.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            _fileInfo.Text = "大小 " + HashTool.FormatSize(r.Size) + modified;
            _message.Text = "算完了。复制上面的值，或者把网站上给的校验值粘到「对照值」里点「对比」。";
            _message.ForeColor = _theme.BarText;
            Logger.Write("文件哈希校验", "算完：" + _current + "  md5=" + r.Md5 + "  sha256=" + r.Sha256);
            if (HashTool.Normalize(_expect.Text).Length > 0) { RunCompare(); }
        }

        private void SetBusyUi(bool busy)
        {
            _busy = busy;
            _pick.Enabled = !busy;
            _again.Enabled = !busy && _current.Length > 0;
            _compare.Enabled = !busy;
        }

        private void RunCompare()
        {
            bool matched;
            string algo;
            string text = HashTool.Verdict(_expect.Text, _result, out matched, out algo);
            _message.Text = text;
            if (algo.Length == 0) { _message.ForeColor = _theme.BarText; }
            else { _message.ForeColor = matched ? _theme.ToastOkText : _theme.Danger; }
        }

        private void CopyValue(TextBox box, string what)
        {
            if (box.Text.Length == 0) { _message.Text = "还没有算出 " + what + "，先选一个文件。"; return; }
            try
            {
                Clipboard.SetText(box.Text);
                _message.Text = "已复制 " + what + "（" + box.Text + "）";
                _message.ForeColor = _theme.BarText;
            }
            catch (Exception ex)
            {
                _message.Text = "复制失败（剪贴板被别的程序占着）：" + ex.Message;
                _message.ForeColor = _theme.Danger;
            }
        }

        private void CopyAll()
        {
            if (_result == null || !_result.Ok) { _message.Text = "还没有算出结果，先选一个文件。"; return; }
            string text = "文件：" + _current + Environment.NewLine
                + "大小：" + HashTool.FormatSize(_result.Size) + Environment.NewLine
                + "MD5：" + _result.Md5 + Environment.NewLine
                + "SHA256：" + _result.Sha256;
            try
            {
                Clipboard.SetText(text);
                _message.Text = "已复制：文件名 + 大小 + MD5 + SHA256";
                _message.ForeColor = _theme.BarText;
            }
            catch (Exception ex)
            {
                _message.Text = "复制失败（剪贴板被别的程序占着）：" + ex.Message;
                _message.ForeColor = _theme.Danger;
            }
        }
    }
}
