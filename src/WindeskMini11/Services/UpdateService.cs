using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WindeskMini11.Services
{
    /// <summary>检查更新的结果。</summary>
    public sealed class UpdateInfo
    {
        public bool Succeeded { get; set; }
        public string Error { get; set; } = string.Empty;

        /// <summary>最新版本号，已去掉前缀 v，例如 1.4.1。</summary>
        public string LatestVersion { get; set; } = string.Empty;

        public string ReleaseUrl { get; set; } = string.Empty;

        /// <summary>最新版本是否比当前程序新。</summary>
        public bool IsNewer { get; set; }

        /// <summary>当前程序是否比公开的最新 Release 更新。</summary>
        public bool IsCurrentNewer { get; set; }
    }

    /// <summary>从 GitHub Releases 读取最新版本。</summary>
    public static class UpdateService
    {
        private const string LatestReleaseApi =
            "https://api.github.com/repos/mingyu0401/windesk-mini/releases/latest";

        public const string ReleasePageUrl =
            "https://github.com/mingyu0401/windesk-mini/releases/latest";

        /// <summary>当前程序版本（三段式，如 1.4.1）。</summary>
        public static string CurrentVersion
        {
            get
            {
                var version = typeof(UpdateService).Assembly.GetName().Version;
                return version == null ? "0.0.0" : version.ToString(3);
            }
        }

        public static async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
        {
            var result = new UpdateInfo();

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

                // GitHub API 必须带 User-Agent，否则直接 403
                http.DefaultRequestHeaders.UserAgent.ParseAdd("WindeskMini11/" + CurrentVersion);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                using var response = await http.GetAsync(LatestReleaseApi, cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    result.Error = "仓库还没有发布任何版本";
                    return result;
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    result.Error = "请求过于频繁，请稍后再试";
                    return result;
                }

                if (!response.IsSuccessStatusCode)
                {
                    result.Error = "服务器返回 " + (int)response.StatusCode;
                    return result;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var rawTag = GetString(root, "tag_name").Trim();
                var tag = NormalizeVersion(rawTag);
                if (string.IsNullOrEmpty(tag))
                {
                    result.Error = "无法解析版本号";
                    return result;
                }

                result.LatestVersion = tag;
                result.ReleaseUrl = GetString(root, "html_url");
                result.Succeeded = true;
                var comparison = CompareVersions(tag, CurrentVersion);
                result.IsNewer = comparison > 0;
                result.IsCurrentNewer = comparison < 0;

                Log.Write("检查更新: 当前=" + CurrentVersion + " 最新=" + tag +
                          " 有新版=" + result.IsNewer + " 当前较新=" + result.IsCurrentNewer);
            }
            catch (TaskCanceledException)
            {
                result.Error = "请求超时";
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        private static string GetString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        /// <summary>兼容 v1.5.3、1.5.3、release-1.5.3 等标签格式。</summary>
        private static string NormalizeVersion(string value)
        {
            var text = value.Trim();
            while (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(1).TrimStart();

            var marker = text.IndexOfAny(new[] { '-', '+', ' ' });
            if (marker >= 0) text = text.Substring(0, marker);

            return Version.TryParse(text, out var version) ? version.ToString(3) : string.Empty;
        }

        /// <summary>逐段比较版本号，a &gt; b 返回正数。</summary>
        private static int CompareVersions(string a, string b)
        {
            var leftText = NormalizeVersion(a);
            var rightText = NormalizeVersion(b);
            if (Version.TryParse(leftText, out var left) && Version.TryParse(rightText, out var right))
            {
                return left.CompareTo(right);
            }

            return 0;
        }
    }
}
