namespace TourGuideCMS;

/// <summary>
/// URL gốc cho QR / Zalo / điện thoại — trùng logic với <c>Program.cs</c> (localhost → LAN qua DevelopmentPublicBaseUrl).
/// </summary>
public static class PublicSiteUrls
{
    public static bool IsHostUnusableForPhoneQr(string? host)
    {
        if (string.IsNullOrEmpty(host)) return true;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("::1", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("[::1]", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("10.0.2.2", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string SiteRootForLinks(HttpContext ctx, IConfiguration config)
    {
        var configured = (config["App:PublicBaseUrl"] ?? "").Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(configured))
            return configured;

        // Reverse proxy / tunnel (ngrok, IIS ARR): dùng host thật mà điện thoại thấy được.
        var forwardedRaw = ctx.Request.Headers["X-Forwarded-Host"].ToString();
        var forwardedHost = string.IsNullOrWhiteSpace(forwardedRaw)
            ? null
            : forwardedRaw.Split(',')[0].Trim();
        var forwardedProto = ctx.Request.Headers["X-Forwarded-Proto"].ToString().Split(',')[0].Trim();
        if (!string.IsNullOrWhiteSpace(forwardedHost))
        {
            var scheme = string.IsNullOrWhiteSpace(forwardedProto)
                ? ctx.Request.Scheme
                : forwardedProto;
            return $"{scheme}://{forwardedHost}{ctx.Request.PathBase}".TrimEnd('/');
        }

        var requestHost = ctx.Request.Host.Host;
        if (!IsHostUnusableForPhoneQr(requestHost))
            return $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}{ctx.Request.PathBase}".TrimEnd('/');

        var devPublic = (config["App:DevelopmentPublicBaseUrl"] ?? "").Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(devPublic))
            return devPublic;

        return $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}{ctx.Request.PathBase}".TrimEnd('/');
    }

    public static string ListenPayPayload(HttpContext ctx, IConfiguration config, int placeId)
        => $"{SiteRootForLinks(ctx, config)}/Listen/Pay?placeId={placeId}";

    /// <summary>Chỉ đường dẫn <c>/install/launch</c> (không tham số).</summary>
    public static string QrAppInstallLaunchUrl(HttpContext ctx, IConfiguration config)
        => $"{SiteRootForLinks(ctx, config)}/install/launch";

    /// <summary>
    /// Chuỗi trong PNG <c>/qr/app</c>: <c>/install/launch?v=…</c> khi có APK hợp lệ — quét QR mở trang «Đang mở cài đặt…» rồi bấm tải (cùng file ~130MB từ <see cref="ApkLocator"/>). Không APK: <c>/install/launch</c>.
    /// </summary>
    public static string QrAppInstallLaunchPayload(HttpContext ctx, IConfiguration config, IWebHostEnvironment env)
    {
        var site = SiteRootForLinks(ctx, config);
        var apkPath = ApkLocator.FindPreferredApkPath(env, config);
        if (!string.IsNullOrWhiteSpace(apkPath) && File.Exists(apkPath))
        {
            var v = ApkLocator.CacheBusterForPath(apkPath);
            return $"{site}/install/launch?v={Uri.EscapeDataString(v)}";
        }

        return $"{site}/install/launch";
    }

    /// <summary>URL ảnh QR (cache-bust) để trình duyệt không giữ PNG cũ.</summary>
    public static string QrAppImageSrc(HttpContext ctx, IConfiguration config)
        => $"{SiteRootForLinks(ctx, config)}/qr/app?cb={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

    /// <summary>
    /// Chuỗi hiển thị / copy — trùng nội dung mã QR (PNG <c>/qr/app</c>).
    /// </summary>
    public static string AppDownloadQrContent(HttpContext ctx, IConfiguration config, IWebHostEnvironment env)
        => QrAppInstallLaunchPayload(ctx, config, env);
}
