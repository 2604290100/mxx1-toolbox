// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Mxx1Toolbox
{
    internal enum UpdateState
    {
        Disabled,    // 用户用环境变量关掉了（不联网）
        Unknown,     // 还没查过
        Checking,    // 正在查
        Latest,      // 已是最新
        Available,   // 有更新的版本
        NoRelease,   // 仓库还没有任何 Release / tag，没法比对
        Failed       // 网络或接口不可用
    }

    /// <summary>一次更新检查的结果。全是纯数据，可以跨线程传。</summary>
    internal sealed class UpdateResult
    {
        internal UpdateState State = UpdateState.Unknown;
        internal string Current = "";
        internal string Latest = "";
        internal string Url = "";
        /// <summary>机器可读的失败原因（纯 ASCII，命令行 / 日志用）。</summary>
        internal string Detail = "";

        /// <summary>命令行与日志里的稳定标识（纯 ASCII，测试断言用这个）。</summary>
        internal string StateId
        {
            get
            {
                switch (State)
                {
                    case UpdateState.Disabled: return "disabled";
                    case UpdateState.Checking: return "checking";
                    case UpdateState.Latest: return "latest";
                    case UpdateState.Available: return "available";
                    case UpdateState.NoRelease: return "norerelease";
                    case UpdateState.Failed: return "error";
                    default: return "unknown";
                }
            }
        }

        /// <summary>界面上显示的一句话（中文）。</summary>
        internal string UiText
        {
            get
            {
                switch (State)
                {
                    case UpdateState.Disabled:
                        return "更新检查已关闭（MXX1_NO_UPDATE=1）";
                    case UpdateState.Checking:
                        return "正在检查…";
                    case UpdateState.Latest:
                        return "已是最新版本（v" + Current + "）";
                    case UpdateState.Available:
                        return "发现新版本 v" + Latest + "（当前 v" + Current + "）";
                    case UpdateState.NoRelease:
                        return "仓库还没有发布版本，暂时无法比对";
                    case UpdateState.Failed:
                        return "检查失败：" + (Detail.Length == 0 ? "网络不可达" : Detail);
                    default:
                        return "尚未检查";
                }
            }
        }

        /// <summary>按顺序写进日志 / 命令行的那些行（key=value，和别的命令一个风格）。</summary>
        internal string[] Lines()
        {
            List<string> l = new List<string>();
            l.Add("update=" + StateId);
            l.Add("current=" + Current);
            l.Add("latest=" + Latest);
            l.Add("url=" + Url);
            l.Add("detail=" + Detail);
            return l.ToArray();
        }
    }

    /// <summary>
    /// 仓库更新检查：读 GitHub 的 Release（没有 Release 就退一步读 tag），和本机 exe 的版本号比一下，
    /// 只报告"有没有更新"，**不下载、不自动更新**。
    ///
    /// 这是工具箱**唯一**的联网动作，隐私约定（见 docs/DISCLAIMER.md 第 4 节）：
    ///   * 只在用户打开界面 / 手动跑 checkupdate 时发起一次 HTTPS 请求，不带任何本机信息
    ///     （只有 User-Agent 里的版本号）；
    ///   * 设 MXX1_NO_UPDATE=1 可以完全关掉，关掉后一个字节都不发；
    ///   * 失败一律静默降级，不弹窗、不阻塞界面；
    ///   * 命令行与界面走同一条路径，测试可以拿 MXX1_UPDATE_URL 指到本机假接口上（不碰外网）。
    /// </summary>
    internal static class UpdateCheck
    {
        internal const string RepoUrl = AboutForm.RepoUrl;
        internal const string ReleasesApi = AboutForm.RepoUrl + "/releases/latest";
        internal const string TagsApi = AboutForm.RepoUrl + "/tags";
        internal const string ReleasesPage = AboutForm.RepoUrl + "/releases";

        private static readonly object Gate = new object();
        private static UpdateResult _last = new UpdateResult();
        private static int _checking;
        /// <summary>检查正在跑的时候又来登记的回调（见 CheckAsync）。</summary>
        private static readonly List<Action<UpdateResult>> _waiting = new List<Action<UpdateResult>>();

        /// <summary>最近一次结果（没有就是 Unknown）。</summary>
        internal static UpdateResult Last
        {
            get { lock (Gate) { return _last; } }
        }

        /// <summary>本机 exe 的版本号，形如 1.5.2（唯一来源是 AboutForm）。</summary>
        internal static string CurrentVersion { get { return AboutForm.VersionText; } }

        /// <summary>MXX1_NO_UPDATE=1/true/yes/on 时完全不联网。</summary>
        internal static bool Disabled
        {
            get
            {
                string v = Env("MXX1_NO_UPDATE");
                if (v == null) { return false; }
                v = v.Trim().ToLowerInvariant();
                return v == "1" || v == "true" || v == "yes" || v == "on";
            }
        }

        private static int TimeoutMs
        {
            get
            {
                int ms;
                string v = Env("MXX1_UPDATE_TIMEOUT_MS");
                if (v != null && int.TryParse(v.Trim(), out ms) && ms >= 500 && ms <= 60000) { return ms; }
                return 6000;
            }
        }

        /// <summary>允许被覆盖，方便离线 / 镜像 / 测试（默认打 GitHub 官方接口）。</summary>
        internal static string ReleasesApiUrl { get { return Env("MXX1_UPDATE_URL") ?? ReleasesApi; } }
        internal static string TagsApiUrl { get { return Env("MXX1_UPDATE_TAGS_URL") ?? TagsApi; } }

        private static string Env(string name)
        {
            try { return Environment.GetEnvironmentVariable(name); }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// 后台查一次，查完回调（回调在线程池线程上，界面要自己 BeginInvoke 回 UI 线程）。
        /// 同一时刻只允许一个检查在跑；**在跑期间来的回调不能丢**：挂到它上面，等出结果一起通知。
        /// （丢掉回调的后果在隔壁工程实测过：关于窗口会永远停在"正在检查…"，因为那次检查的结果
        /// 没人转告它 —— 工具箱的关于窗口同样会再登记一次，所以这条必须保留。）
        /// </summary>
        internal static void CheckAsync(Action<UpdateResult> done)
        {
            CheckAsync(CurrentVersion, done);
        }

        internal static void CheckAsync(string current, Action<UpdateResult> done)
        {
            if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            {
                if (done != null) { lock (Gate) { _waiting.Add(done); } }
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateResult r;
                try { r = Run(current); }
                catch (Exception ex)
                {
                    r = new UpdateResult();
                    r.Current = current;
                    r.State = UpdateState.Failed;
                    r.Detail = "exception";
                    Logger.Write("检查更新", "出错：" + ex.Message);
                }
                Action<UpdateResult>[] pending;
                lock (Gate)
                {
                    _last = r;
                    pending = _waiting.ToArray();
                    _waiting.Clear();
                }
                Interlocked.Exchange(ref _checking, 0);
                Notify(done, r);
                for (int i = 0; i < pending.Length; i++) { Notify(pending[i], r); }
            });
        }

        private static void Notify(Action<UpdateResult> cb, UpdateResult r)
        {
            if (cb == null) { return; }
            try { cb(r); }
            catch (Exception) { }
        }

        /// <summary>同步查一次（命令行用）。绝不抛异常。</summary>
        internal static UpdateResult Run(string current)
        {
            UpdateResult r = new UpdateResult();
            r.Current = current;

            if (Disabled)
            {
                r.State = UpdateState.Disabled;
                r.Detail = "env-disabled";
                Logger.Write("检查更新", "已关闭（MXX1_NO_UPDATE）");
                return r;
            }

            int status;
            string body = HttpGet(ReleasesApiUrl, out status);
            string tag = null;
            string html = null;

            if (body != null && status == 200)
            {
                tag = Group(body, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                html = Group(body, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
            }
            else
            {
                // 404 = 还没有任何 Release；403 = 匿名接口被限流；0 = 网络不通。
                // 前两种都退一步问 tags（仓库只要打过 tag 就还能比对）。
                int s2;
                string b2 = HttpGet(TagsApiUrl, out s2);
                if (b2 != null && s2 == 200)
                {
                    tag = Group(b2, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                    html = ReleasesPage;
                    r.Detail = "via-tags";
                }
                else
                {
                    r.State = UpdateState.Failed;
                    r.Detail = status == 0 ? "network-error" : ("http-" + status);
                    Logger.Write("检查更新", "失败 detail=" + r.Detail);
                    return r;
                }
            }

            tag = tag == null ? "" : tag.Trim();
            if (tag.Length == 0)
            {
                r.State = UpdateState.NoRelease;
                r.Detail = r.Detail.Length == 0 ? "no-release" : r.Detail;
                Logger.Write("检查更新", "仓库暂无发布版本");
                return r;
            }

            r.Latest = TrimTag(tag);
            r.Url = string.IsNullOrEmpty(html) ? ReleasesPage : html;

            int cmp = Compare(r.Latest, current);
            if (cmp > 0)
            {
                r.State = UpdateState.Available;
            }
            else
            {
                r.State = UpdateState.Latest;
                if (r.Detail.Length == 0) { r.Detail = cmp == 0 ? "up-to-date" : "local-newer"; }
            }
            Logger.Write("检查更新", "state=" + r.StateId + " current=" + current
                + " latest=" + r.Latest + " detail=" + r.Detail);
            return r;
        }

        /// <summary>把 v1.5.2 / V1.5 之类统一成 1.5.2（去掉前导 v 和 -pre 后缀）。</summary>
        internal static string TrimTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) { return ""; }
            string t = tag.Trim();
            if (t.Length > 0 && (t[0] == 'v' || t[0] == 'V')) { t = t.Substring(1); }
            int dash = t.IndexOf('-');
            if (dash > 0) { t = t.Substring(0, dash); }
            return t.Trim();
        }

        /// <summary>按点分段比大小；任一段解析不了就当作"不更新"（宁可漏报也不乱报）。</summary>
        internal static int Compare(string a, string b)
        {
            int[] x = Parts(a);
            int[] y = Parts(b);
            if (x == null || y == null) { return 0; }
            int n = Math.Max(x.Length, y.Length);
            for (int i = 0; i < n; i++)
            {
                int l = i < x.Length ? x[i] : 0;
                int rr = i < y.Length ? y[i] : 0;
                if (l != rr) { return l > rr ? 1 : -1; }
            }
            return 0;
        }

        private static int[] Parts(string s)
        {
            if (string.IsNullOrEmpty(s)) { return null; }
            string[] p = s.Split('.');
            if (p.Length == 0 || p.Length > 5) { return null; }
            int[] r = new int[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                int n;
                if (!int.TryParse(p[i], out n) || n < 0) { return null; }
                r[i] = n;
            }
            return r;
        }

        private static string Group(string body, string pattern)
        {
            Match m = Regex.Match(body, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>同步 HTTPS GET。任何失败都返回 null，status 里带 HTTP 状态码（0 = 网络层失败）。</summary>
        private static string HttpGet(string url, out int status)
        {
            status = 0;
            try
            {
                // GitHub 只接受 TLS 1.2+；老 .NET Framework 的默认值不含它，必须显式设
                // （Windows 7 上不设就是"网络不通"，这条是给老系统留的）。
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
                catch (Exception) { }

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                // GitHub 接口要求带 User-Agent，不带直接 403。
                req.UserAgent = "Mxx1Toolbox/" + CurrentVersion;
                req.Accept = "application/vnd.github+json";
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = TimeoutMs;
                req.AllowAutoRedirect = true;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                try { req.Proxy = WebRequest.DefaultWebProxy; } catch (Exception) { }

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    status = (int)resp.StatusCode;
                    using (Stream s = resp.GetResponseStream())
                    using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
            catch (WebException wex)
            {
                if (wex.Response != null)
                {
                    try { status = (int)((HttpWebResponse)wex.Response).StatusCode; }
                    catch (Exception) { }
                    try { wex.Response.Close(); } catch (Exception) { }
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
