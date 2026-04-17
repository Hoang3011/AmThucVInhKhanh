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

    /// <summary>Chuỗi gắn trong PNG <c>/qr/app</c> — trùng với <see cref="AppDownloadQrContent"/> khi không dùng link trực tiếp.</summary>
    public static string QrAppInstallLaunchUrl(HttpContext ctx, IConfiguration config)
        => $"{SiteRootForLinks(ctx, config)}/install/launch";

    /// <summary>URL ảnh QR (cache-bust) để trình duyệt không giữ PNG cũ.</summary>
    public static string QrAppImageSrc(HttpContext ctx, IConfiguration config)
        => $"{SiteRootForLinks(ctx, config)}/qr/app?cb={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

    /// <summary>
    /// Nội dung mã QR "tải app": <c>App:AppDownloadUrl</c> nếu có; không thì <c>/install/launch</c> (cùng PNG <c>/qr/app</c>).
    /// </summary>
    public static string AppDownloadQrContent(HttpContext ctx, IConfiguration config)
    {
        var direct = (config["App:AppDownloadUrl"] ?? "").Trim();
        if (!string.IsNullOrEmpty(direct)
            && Uri.TryCreate(direct, UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            return direct.Trim();

        return QrAppInstallLaunchUrl(ctx, config);
    }
}
