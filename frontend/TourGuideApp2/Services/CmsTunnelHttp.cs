using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace TourGuideApp2.Services;

/// <summary>
/// Ngrok / Microsoft Dev Tunnels có thể chèn trang HTML cảnh báo trước API — thêm header để app nhận JSON.
/// Server LAN thường bỏ qua các header lạ; không đổi URL hay logic nghiệp vụ.
/// </summary>
public static class CmsTunnelHttp
{
    private const string NgrokSkipBrowserWarning = "ngrok-skip-browser-warning";
    /// <summary>Microsoft Learn — bỏ trang anti-phishing cho client không phải trình duyệt (GET /api/places, …).</summary>
    private const string DevTunnelSkipAntiPhishing = "X-Tunnel-Skip-AntiPhishing-Page";

    /// <summary>User-Agent giống Chrome — một số tunnel/WAF/4G (vd. Samsung A125) chặn UA mặc định của HttpClient.</summary>
    private const string BrowserLikeUserAgent =
        "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36 AmThucVinhKhanh/1";

    /// <summary>Connect dài + pool ngắn — tránh treo DNS/TLS trên 4G máy yếu.</summary>
    public static HttpClient CreateReliableHttpClient(TimeSpan requestTimeout)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(40),
            AutomaticDecompression = DecompressionMethods.All
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = requestTimeout
        };
        ApplyTo(client);
        return client;
    }

    public static void ApplyTo(HttpClient client)
    {
        try
        {
            if (!client.DefaultRequestHeaders.Contains(NgrokSkipBrowserWarning))
                client.DefaultRequestHeaders.TryAddWithoutValidation(NgrokSkipBrowserWarning, "true");
            if (!client.DefaultRequestHeaders.Contains(DevTunnelSkipAntiPhishing))
                client.DefaultRequestHeaders.TryAddWithoutValidation(DevTunnelSkipAntiPhishing, "true");
            if (client.DefaultRequestHeaders.Accept.Count == 0)
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!client.DefaultRequestHeaders.UserAgent.Any())
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserLikeUserAgent);
        }
        catch
        {
            // Không chặn gọi API nếu header lỗi thiết bị hiếm.
        }
    }

    public static void ApplyTo(HttpRequestMessage request)
    {
        try
        {
            request.Headers.TryAddWithoutValidation(NgrokSkipBrowserWarning, "true");
            request.Headers.TryAddWithoutValidation(DevTunnelSkipAntiPhishing, "true");
            if (!request.Headers.Accept.Any())
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Tunnel / WAF / 4G (Samsung): UA mặc định hoặc quá “lạ” dễ bị chặn — dùng UA giống Chrome.
            if (!request.Headers.UserAgent.Any())
                request.Headers.TryAddWithoutValidation("User-Agent", BrowserLikeUserAgent);
        }
        catch
        {
            // ignore
        }
    }
}
