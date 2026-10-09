namespace TourGuideCMS;

/// <summary>
/// Cùng một luồng với <c>/install/launch</c>: ưu tiên APK trong CMS (tùy cấu hình), link ngoài có cache-buster, cuối cùng là APK local nếu có.
/// </summary>
public static class InstallTargetUrlResolver
{
    public static string? BuildLocalApkDownloadUrl(HttpContext http, IConfiguration config, IWebHostEnvironment env)
    {
        var apkPath = ApkLocator.FindPreferredApkPath(env, config);
        if (string.IsNullOrWhiteSpace(apkPath) || !File.Exists(apkPath))
            return null;

        var version = ApkLocator.CacheBusterForPath(apkPath);
        return $"{PublicSiteUrls.SiteRootForLinks(http, config)}/downloads/AmThucVinhKhanh.apk?v={version}";
    }

    public static string AppendQueryCacheBuster(string url, string busterValue)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(busterValue))
            return url;
        var sep = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{sep}cb={Uri.EscapeDataString(busterValue)}";
    }

    /// <summary>Ưu tiên <c>App:AppDownloadCacheBuster</c>, không có thì <c>App:ExpectedAppVersion</c>.</summary>
    public static string? GetDirectUrlCacheBuster(IConfiguration config)
    {
        var manual = (config["App:AppDownloadCacheBuster"] ?? "").Trim();
        if (!string.IsNullOrEmpty(manual))
            return manual;
        var ver = (config["App:ExpectedAppVersion"] ?? "").Trim();
        return string.IsNullOrEmpty(ver) ? null : ver;
    }

    public static string? Resolve(HttpContext http, IConfiguration config, IWebHostEnvironment env)
    {
        var preferLocal = config.GetValue("App:PreferLocalApkOverDirectUrl", false);
        var localApkUrl = BuildLocalApkDownloadUrl(http, config, env);

        if (preferLocal && !string.IsNullOrWhiteSpace(localApkUrl))
            return localApkUrl;

        var direct = (config["App:AppDownloadUrl"] ?? "").Trim();
        if (!string.IsNullOrEmpty(direct)
            && Uri.TryCreate(direct, UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
        {
            var buster = GetDirectUrlCacheBuster(config);
            return string.IsNullOrEmpty(buster) ? direct : AppendQueryCacheBuster(direct, buster);
        }

        if (!string.IsNullOrWhiteSpace(localApkUrl))
            return localApkUrl;

        return null;
    }
}
