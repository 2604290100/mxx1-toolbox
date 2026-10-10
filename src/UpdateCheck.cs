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
        /// <summary>版本号是从哪儿读来的：`site` = mxx1.cn 的资源接口（首选），
        /// `github` = GitHub 兜底，`none` = 两边都没读成。
        /// **发版后"以哪边为准"出问题时第一眼看这个**（见 docs/DESIGN.md §12.67）。</summary>
        internal string Source = "";
        /// <summary>站点那边标题的**解码后原文**（诊断用，让人一眼看出"站点上现在写的是什么版本"）。
        /// 站点没读成时是空串。`v1.5.44` 那次手滑就是靠这一行看出来的。</summary>
        internal string SiteTitle = "";

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
            // 版本号是从哪读来的（site = mxx1.cn 资源接口 / github = 兜底 / none = 都没成）
            l.Add("source=" + (Source.Length == 0 ? "none" : Source));
            // 站点接口这个地址是**机器可读**的那一份真话：默认值是 mxx1.cn 的资源接口，
            // 被 MXX1_RESOURCE_URL / MXX1_RESOURCE_ID 覆盖时这里跟着变。
            l.Add("site=" + UpdateCheck.ResourceApiUrlWithId);
            // 站点标题原文（解码后）。**这一行是"站点上到底写着哪个版本"的唯一出口** ——
            // 用户 2026-10-11 发现的 `v1.5.44` 那种手滑，一眼就能看出来（见 §12.67）。
            l.Add("sitetitle=" + SiteTitle);
            // 真去请求的是哪个 GitHub 地址也报出来：默认值是 GitHub 的接口（api.github.com/repos/…），
            // 被 MXX1_UPDATE_URL / MXX1_UPDATE_TAGS_URL 覆盖时这里跟着变。
            // 排障时把它贴进浏览器就能看出是接口写错了还是网络不通（2026-10-05 就是这么发现
            // 「接口被写成了网页地址 → 406」的）。
            l.Add("api=" + UpdateCheck.ReleasesApiUrl);
            l.Add("detail=" + Detail);
            return l.ToArray();
        }
    }

    /// <summary>
    /// 更新检查：**先问 mxx1.cn 的资源接口读线上版本号**，读不到再退一步读 GitHub 的 Release
    /// （没有 Release 也读不到就退一步读 tag）。和本机 exe 的版本号比一下，
    /// 只报告"有没有更新"，**不下载、不自动更新**。
    ///
    /// 这是工具箱**唯一**的联网动作，隐私约定（见 docs/DISCLAIMER.md 第 4 节）：
    ///   * 只在用户打开界面 / 手动跑 checkupdate 时发起一次 HTTPS 请求，不带任何本机信息
    ///     （只有 User-Agent 里的版本号）；
    ///   * 设 MXX1_NO_UPDATE=1 可以完全关掉，关掉后一个字节都不发；
    ///   * 失败一律静默降级，不弹窗、不阻塞界面；
    ///   * 命令行与界面走同一条路径，测试可以拿 MXX1_RESOURCE_URL / MXX1_UPDATE_URL
    ///     指到本机假接口上（不碰外网）。
    ///
    /// **2026-10-11 换来源**（用户拍的方案 A，见 docs/DESIGN.md §12.67）：原来只查 GitHub，
    /// 国内访问 `api.github.com` 又慢又会被匿名限流（403）；现在首选自己的站点接口
    /// `GET https://www.mxx1.cn/apis/resources?id=2109`（**公开只读、不需要 token**，所以 exe 里
    /// 不含任何密钥），GitHub 那条路**一个字没动**，只当兜底。
    ///
    /// ⚠️ 这个接口有两个反直觉的地方，改代码前必读（坑 53）：
    ///   ① 响应头是 `Content-Encoding: gzip` + `charset=gb2312` —— 两个都得处理，
    ///      `AutomaticDecompression` 和 GBK 解码缺一个都不行；
    ///   ② `title` / `description` 在库里存的是 **URL 编码后的 ASCII**，所以哪怕编码读错了
    ///      版本号照样能解出来 —— 这正是这个坑不容易被发现的原因。
    /// </summary>
    internal static class UpdateCheck
    {
        internal const string RepoUrl = AboutForm.RepoUrl;

        /// <summary>站点资源接口的根（默认值）。请求时在后面接上 `?id=` 和资源 id。</summary>
        internal const string ResourceApi = "https://www.mxx1.cn/apis/resources";

        /// <summary>本工具在站点上的资源 id（首页 `http://mxx1.cn/info?id=2109`）。</summary>
        internal const string DefaultResourceId = "2109";

        /// <summary>资源详情页的根（「前往更新」点开的就是它接上 `?id=` 和资源 id）。</summary>
        internal const string InfoPageBase = "https://www.mxx1.cn/info";

        /// <summary>标题里用来定位版本号那一段的锚（见 ExtractVersion：先找含这个字样的分段，
        /// 再取它**后面**那一段）。</summary>
        private const string TitleAnchor = "萌新工具箱";

        /// <summary>接口根：`https://github.com/&lt;账号&gt;/&lt;仓库&gt;` → `https://api.github.com/repos/&lt;账号&gt;/&lt;仓库&gt;`。
        ///
        /// **2026-10-05 修**：原来这里直接把**网页地址**后面接上 `/releases/latest` 当接口用了
        /// （`https://github.com/…/releases/latest` 是个 HTML 页面）。GitHub 对页面请求里那个
        /// `Accept: application/vnd.github+json` 直接回 **406 Not Acceptable**，于是真实环境下的
        /// 更新检查一直是「检查失败：http-406」—— 而测试全程用 `MXX1_UPDATE_URL` 指到本机假接口，
        /// 正好绕开了这个默认值，所以两套测试都是绿的。现在从仓库地址现推接口根，两处不会再跑偏。</summary>
        private static readonly string ApiBase = MakeApiBase();

        private static string MakeApiBase()
        {
            try
            {
                Uri u = new Uri(RepoUrl);
                string path = u.AbsolutePath.TrimEnd('/');
                if (path.Length == 0) { return ""; }
                return "https://api.github.com/repos" + path;
            }
            catch (Exception) { return ""; }
        }

        internal static string ReleasesApi
        {
            get { return ApiBase.Length > 0 ? ApiBase + "/releases/latest" : RepoUrl; }
        }

        internal static string TagsApi
        {
            get { return ApiBase.Length > 0 ? ApiBase + "/tags" : RepoUrl; }
        }

        internal const string ReleasesPage = RepoUrl + "/releases";

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

        /// <summary>站点资源接口的根，可被 MXX1_RESOURCE_URL 覆盖（测试指到本机假接口）。</summary>
        internal static string ResourceApiUrl { get { return Env("MXX1_RESOURCE_URL") ?? ResourceApi; } }

        /// <summary>资源 id，可被 MXX1_RESOURCE_ID 覆盖。</summary>
        internal static string ResourceId { get { return Env("MXX1_RESOURCE_ID") ?? DefaultResourceId; } }

        /// <summary>真正会去请求的那个地址（`…/apis/resources?id=2109`），`checkupdate` 里报出来。</summary>
        internal static string ResourceApiUrlWithId
        {
            get { return ResourceApiUrl + (ResourceApiUrl.IndexOf('?') >= 0 ? "&id=" : "?id=") + ResourceId; }
        }

        /// <summary>「前往更新」点开的那个页面：站点上的资源详情页。</summary>
        internal static string InfoPageUrl { get { return InfoPageBase + "?id=" + ResourceId; } }

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

            // ---- 第一顺位：站点资源接口（方案 A，用户 2026-10-11 拍板）----
            // 读得到版本号就以它为准（**不再去问 GitHub**，正常情况只发这一次请求）。
            string siteVersion;
            string siteTitle;
            string siteReason;
            bool siteOk = TrySiteVersion(out siteVersion, out siteTitle, out siteReason);
            // **不管成没成，站点上写的是什么标题都报出来** —— 这正是"站点标题手滑成 v1.5.44"
            // 那类问题唯一能被一眼看见的地方（`checkupdate` 的 sitetitle= 那一行）。
            r.SiteTitle = siteTitle;
            if (siteOk)
            {
                r.Source = "site";
                r.Latest = siteVersion;
                r.Url = InfoPageUrl;
                int cmpSite = Compare(r.Latest, current);
                if (cmpSite > 0)
                {
                    r.State = UpdateState.Available;
                    r.Detail = "site-newer";
                }
                else
                {
                    r.State = UpdateState.Latest;
                    r.Detail = cmpSite == 0 ? "site-up-to-date" : "site-local-newer";
                }
                Logger.Write("检查更新", "state=" + r.StateId + " source=site current=" + current
                    + " latest=" + r.Latest + " detail=" + r.Detail);
                return r;
            }

            // ---- 第二顺位：GitHub 兜底（站点读不成时才走到这里）----
            // 为什么会走这里：站点维护中 / 接口改了 / 标题里找不到版本号（`siteReason` 说清是哪种）。
            // `detail` 里把站点那一段原因带上，排障时一眼看出"是哪边出的问题"。
            r.Source = "github";
            UseGithub(current, r, "site-" + siteReason + ">");
            if (r.State == UpdateState.Failed) { r.Source = "none"; }
            return r;
        }

        /// <summary>
        /// 站点那条路：`GET https://www.mxx1.cn/apis/resources?id=2109` → 从 `data.list[0].title` 里
        /// 读出版本号。**只读、不需要 token**（公开接口），所以 exe 里不含任何密钥。
        ///
        /// 三个真实约束（坑 53，都是实测出来的）：
        ///   * 响应是 **gzip**  + **GBK**（`charset=gb2312`）—— 缺一个都会出乱码；
        ///   * `title` 是 **URL 编码**的 ASCII（`%E8%90%8C…%20%7C%20v1.5.6`），要 unquote 才看得出分段；
        ///   * `title` 在 JSON 里是**第一个**出现的字段，所以"取第一个 `"title"`"就够（不用真写 JSON 解析器）。
        ///
        /// 失败时返回 false 并把**机器可读的原因**写进 reason（纯 ASCII，进 `detail=`）：
        /// `unreachable` / `http-N` / `code(N)` / `no-code` / `no-title` / `no-version`。
        /// **注意 `title` 是 out 且失败时也可能有值**：只要响应体里读得出 title 就填上
        /// （调用方会把它报成 `sitetitle=`，让人看到"站点上现在写的到底是什么"）。
        /// </summary>
        private static bool TrySiteVersion(out string version, out string title, out string reason)
        {
            version = "";
            title = "";
            reason = "";

            string url = ResourceApiUrlWithId;
            int status;
            string body = HttpGet(url, Encoding.GetEncoding(936), out status);
            if (body == null)
            {
                reason = status == 0 ? "unreachable" : ("http-" + status);
                Logger.Write("检查更新", "站点接口没读成 detail=" + reason);
                return false;
            }

            string code = Group(body, "\"code\"\\s*:\\s*(-?\\d+)");
            if (code != "200")
            {
                // 站点自己报的失败（文档里记着失败就是 `{"code":-1}`）
                reason = code == null ? "no-code" : ("code(" + code + ")");
                Logger.Write("检查更新", "站点接口返回 " + reason);
                return false;
            }

            string raw = Group(body, "\"title\"\\s*:\\s*\"([^\"]*)\"");
            if (raw == null)
            {
                reason = "no-title";
                Logger.Write("检查更新", "站点接口里没有 title");
                return false;
            }

            title = UrlDecode(raw);
            version = ExtractVersion(title);
            if (version.Length == 0)
            {
                reason = "no-version";
                Logger.Write("检查更新", "站点标题里找不到版本号：" + title);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 从站点标题里取版本号（用户 2026-10-11 选的 T1 口径）。
        ///
        /// 站点的标题长这样（真实值）：
        ///   `035期 | 多行多列按钮墙启动器，… | 萌新工具箱 | v1.5.6 | ★0`
        /// 规则两步：
        ///   ① 按 `|` 分段，找含「萌新工具箱」的那一段，取它**后面**那一段，要求整段就是 `v1.5.6` 这种形状；
        ///   ② ① 不成立时（比如以后站点把顺序改了）退一步：从后往前找**第一个**形状合法的分段。
        /// **只认"整段都是版本号"的分段**，绝不在长句子里乱抓数字 —— 否则
        /// `112 个内置按钮`、`035期` 这类数字会被当成版本号。
        ///
        /// 取不出版本号就返回空串（调用方据此退回 GitHub）。
        /// </summary>
        internal static string ExtractVersion(string title)
        {
            if (string.IsNullOrEmpty(title)) { return ""; }
            string[] segs = title.Split('|');

            for (int i = 0; i < segs.Length - 1; i++)
            {
                if (segs[i].IndexOf(TitleAnchor) >= 0)
                {
                    string v = VersionSegment(segs[i + 1]);
                    if (v.Length > 0) { return v; }
                }
            }
            for (int i = segs.Length - 1; i >= 0; i--)
            {
                string v = VersionSegment(segs[i]);
                if (v.Length > 0) { return v; }
            }
            return "";
        }

        /// <summary>整段就是一个版本号吗？是就返回"去掉前导 v"的数字串，不是就返回空串。</summary>
        private static string VersionSegment(string seg)
        {
            string t = seg == null ? "" : seg.Trim();
            if (t.Length > 1 && (t[0] == 'v' || t[0] == 'V')) { t = t.Substring(1); }
            string[] p = t.Split('.');
            if (p.Length < 2 || p.Length > 4) { return ""; }
            for (int i = 0; i < p.Length; i++)
            {
                int n;
                if (p[i].Length == 0 || !int.TryParse(p[i], out n) || n < 0) { return ""; }
            }
            return t;
        }

        /// <summary>把接口里的百分号编码还原成能看的文本。
        ///
        /// 站点存的是 Python `quote()` 的产物（空格是 `%20`），所以理论上不需要处理 `+`；
        /// 但 `+` 在 form 编码里就是空格，顺手还原一次更保险（`%2B` 不受影响）。
        /// 解不开（非法百分号序列）就原样返回 —— 解析不出问题，因为版本号那一段是纯 ASCII。</summary>
        internal static string UrlDecode(string s)
        {
            if (string.IsNullOrEmpty(s)) { return s == null ? "" : s; }
            try { return Uri.UnescapeDataString(s.Replace("+", " ")); }
            catch (Exception) { return s; }
        }

        /// <summary>GitHub 那条路（站点读不出来时的兜底）。语义与 2026-10-05 那版**完全一致**，
        /// 只是所有 `detail` 前面加上 `note`（站点那边失败的原因），排障时能看出是哪边出的问题。</summary>
        private static void UseGithub(string current, UpdateResult r, string note)
        {
            int status;
            string body = HttpGet(ReleasesApiUrl, Encoding.UTF8, out status);
            string tag = null;
            string html = null;
            string detail = "";

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
                string b2 = HttpGet(TagsApiUrl, Encoding.UTF8, out s2);
                if (b2 != null && s2 == 200)
                {
                    tag = Group(b2, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                    html = ReleasesPage;
                    detail = "via-tags";
                }
                else
                {
                    r.State = UpdateState.Failed;
                    r.Detail = note + (status == 0 ? "network-error" : ("http-" + status));
                    Logger.Write("检查更新", "失败 detail=" + r.Detail);
                    return;
                }
            }

            tag = tag == null ? "" : tag.Trim();
            if (tag.Length == 0)
            {
                r.State = UpdateState.NoRelease;
                r.Detail = note + (detail.Length == 0 ? "no-release" : detail);
                Logger.Write("检查更新", "仓库暂无发布版本");
                return;
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
                if (detail.Length == 0) { detail = cmp == 0 ? "up-to-date" : "local-newer"; }
            }
            r.Detail = note + detail;
            Logger.Write("检查更新", "state=" + r.StateId + " source=github current=" + current
                + " latest=" + r.Latest + " detail=" + r.Detail);
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

        /// <summary>同步 HTTPS GET。任何失败都返回 null，status 里带 HTTP 状态码（0 = 网络层失败）。
        ///
        /// `enc` 是要用哪种编码读响应体：GitHub 是 UTF-8，**站点接口是 GBK（`charset=gb2312`）** ——
        /// 写死 UTF-8 的话站点那边的中文会变乱码（坑 53）。</summary>
        private static string HttpGet(string url, Encoding enc, out int status)
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
                // 站点接口**无条件**回 gzip（实测：请求里没带 Accept-Encoding 也回 gzip），
                // 所以这一行不是可选优化，少了它读到的是一堆压缩字节。
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                try { req.Proxy = WebRequest.DefaultWebProxy; } catch (Exception) { }

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    status = (int)resp.StatusCode;
                    using (Stream s = resp.GetResponseStream())
                    using (StreamReader sr = new StreamReader(s, enc))
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
