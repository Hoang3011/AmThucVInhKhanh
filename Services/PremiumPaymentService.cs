using System.Net.Http.Json;
using System.Text.Json;

namespace TourGuideApp2.Services;

/// <summary>Gọi API CMS: kiểm tra đã trả phí demo chưa + ghi nhận thanh toán demo (một lần / POI / thiết bị).</summary>
public static class PremiumPaymentService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public static async Task<bool> CheckEntitlementAsync(int placeId, CancellationToken cancellationToken = default)
    {
        var baseUrl = PlaceApiService.GetCmsBaseUrlForListenPayLinks();
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;

        var device = Uri.EscapeDataString(DeviceInstallIdService.GetOrCreate());
        var cust = AuthService.GetCustomerIdForServerSync();
        var custPart = cust.HasValue && cust.Value > 0 ? $"&customerUserId={cust.Value}" : "";
        var url = $"{baseUrl.TrimEnd('/')}/api/premium/entitlement?placeId={placeId}&deviceInstallId={device}{custPart}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var key = PlaceApiService.GetMobileApiKeyForSync();
        if (!string.IsNullOrWhiteSpace(key))
            req.Headers.TryAddWithoutValidation("X-Mobile-Key", key);

        try
        {
            using var res = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return false;
            await using var stream = await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
            return doc.RootElement.TryGetProperty("unlocked", out var u) &&
                   u.ValueKind is JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<(bool Ok, string Message, bool AlreadyUnlocked)> PayDemoAsync(
        int placeId,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = PlaceApiService.GetCmsBaseUrlForListenPayLinks();
        if (string.IsNullOrWhiteSpace(baseUrl))
            return (false, "Chưa cấu hình máy chủ CMS.", false);

        var url = $"{baseUrl.TrimEnd('/')}/api/premium/pay-demo";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        var key = PlaceApiService.GetMobileApiKeyForSync();
        if (!string.IsNullOrWhiteSpace(key))
            req.Headers.TryAddWithoutValidation("X-Mobile-Key", key);

        var body = new
        {
            placeId,
            deviceInstallId = DeviceInstallIdService.GetOrCreate(),
            customerUserId = AuthService.GetCustomerIdForServerSync()
        };
        req.Content = JsonContent.Create(body);

        try
        {
            using var res = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var txt = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(txt) ? "{}" : txt);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var msg = root.TryGetProperty("message", out var mEl) && mEl.ValueKind == JsonValueKind.String
                ? mEl.GetString() ?? ""
                : (res.IsSuccessStatusCode ? "Xong." : $"HTTP {(int)res.StatusCode}");
            var already = root.TryGetProperty("alreadyUnlocked", out var aEl) && aEl.ValueKind == JsonValueKind.True;
            return (ok || already, msg, already);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, false);
        }
    }
}
