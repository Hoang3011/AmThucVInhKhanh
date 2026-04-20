using System.Linq;
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
            // Một số nhà mạng/WAF chặn client không có User-Agent (4G hay lỗi hơn Wi‑Fi/USB debug).
            if (!request.Headers.UserAgent.Any())
            {
                try
                {
                    var v = Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;
                    request.Headers.TryAddWithoutValidation("User-Agent", $"AmThucVinhKhanh/{v} (MAUI)");
                }
                catch
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "AmThucVinhKhanh (MAUI)");
                }
            }
        }
        catch
        {
            // ignore
        }
    }
}
