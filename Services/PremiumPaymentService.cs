using System.Collections.Generic;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace TourGuideApp2.Services;

/// <summary>Gọi API CMS: kiểm tra đã trả phí demo chưa + ghi nhận thanh toán demo (một lần / POI / thiết bị).</summary>
public static class PremiumPaymentService
{
    private static readonly HttpClient Http = CreateHttp(15);

    /// <summary>GET entitlement nhẹ hơn (timeout ngắn) — dùng khi bấm nghe POI liên tục.</summary>
    private static readonly HttpClient EntitlementHttp = CreateHttp(12);

    private static HttpClient CreateHttp(int timeoutSeconds)
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        CmsTunnelHttp.ApplyTo(h);
        return h;
    }

    private static readonly object EntitlementTrueCacheLock = new();
    private static readonly Dictionary<string, DateTime> EntitlementTrueUntilUtc = new();

    private static readonly object SilentRefreshLock = new();
    private static readonly Dictionary<int, DateTime> LastSilentRefreshUtc = new();

    private const string PersistentUnlockPrefPrefix = "TourGuidePremiumOk_v1_";

    private static string BuildEntitlementCacheKey(int placeId)
    {
        var device = DeviceInstallIdService.GetOrCreate();
        var cust = AuthService.GetCustomerIdForServerSync();
        return $"{placeId}\u001f{device}\u001f{cust?.ToString() ?? "0"}";
    }

    /// <summary>
    /// Lưu <c>device\u001fcustomerId</c> — khớp cách CMS tính unlock: (DeviceInstallId = d OR CustomerUserId = c).
    /// Không bắt buộc trùng “cả chuỗi” với session hiện tại: đổi trạng thái đăng nhập vẫn nghe offline nếu cùng máy hoặc cùng tài khoản remote đã trả.
    /// </summary>
    private static string PackPersistentPayload()
    {
        var d = DeviceInstallIdService.GetOrCreate();
        var c = AuthService.GetCustomerIdForServerSync();
        var custPart = c.HasValue && c.Value > 0
            ? c.Value.ToString(CultureInfo.InvariantCulture)
            : "0";
        return $"{d}\u001f{custPart}";
    }

    private static bool TryParsePersistentPayload(string raw, out string storedDevice, out int storedCustomerId)
    {
        storedDevice = string.Empty;
        storedCustomerId = 0;
        if (string.IsNullOrEmpty(raw))
            return false;
        var idx = raw.IndexOf('\u001f', StringComparison.Ordinal);
        if (idx < 0)
        {
            storedDevice = raw.Trim();
            return !string.IsNullOrEmpty(storedDevice);
        }

        storedDevice = raw[..idx].Trim();
        var tail = raw[(idx + 1)..].Trim();
        if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid) && cid > 0)
            storedCustomerId = cid;
        return !string.IsNullOrEmpty(storedDevice) || storedCustomerId > 0;
    }

    private static bool HasPersistentUnlock(int placeId)
    {
        var raw = Preferences.Default.Get(PersistentUnlockPrefPrefix + placeId, string.Empty) ?? string.Empty;
        if (!TryParsePersistentPayload(raw, out var storedDevice, out var storedCustomerId))
            return false;

        var appDevice = DeviceInstallIdService.GetOrCreate();
        if (!string.IsNullOrEmpty(storedDevice)
            && string.Equals(storedDevice, appDevice, StringComparison.Ordinal))
            return true;

        if (storedCustomerId > 0
            && AuthService.GetCustomerIdForServerSync() is int appCust
            && appCust == storedCustomerId)
            return true;

        return false;
    }

    private static void SavePersistentUnlock(int placeId)
    {
        Preferences.Default.Set(PersistentUnlockPrefPrefix + placeId, PackPersistentPayload());
    }

    private static void ClearPersistentUnlock(int placeId)
    {
        Preferences.Default.Remove(PersistentUnlockPrefPrefix + placeId);
    }

    private static void TouchMemoryTrueCache(string cacheKey)
    {
        lock (EntitlementTrueCacheLock)
        {
            var now = DateTime.UtcNow;
            List<string>? expired = null;
            foreach (var kv in EntitlementTrueUntilUtc)
            {
                if (kv.Value <= now)
                    (expired ??= new List<string>()).Add(kv.Key);
            }

            if (expired != null)
            {
                foreach (var k in expired)
                    EntitlementTrueUntilUtc.Remove(k);
            }

            EntitlementTrueUntilUtc[cacheKey] = now.AddSeconds(45);
        }
    }

    private static bool TryMemoryTrueCache(string cacheKey)
    {
        lock (EntitlementTrueCacheLock)
        {
            return EntitlementTrueUntilUtc.TryGetValue(cacheKey, out var until) && DateTime.UtcNow < until;
        }
    }

    /// <summary>
    /// Xóa cache RAM 45s — dùng khi quay lại app sau thanh toán web / cập nhật POI để không kẹt trạng thái “chưa mở khóa”.
    /// Không xóa <see cref="HasPersistentUnlock"/> (vẫn offline được sau khi đã mở khóa).
    /// </summary>
    public static void ClearShortLivedEntitlementMemory()
    {
        lock (EntitlementTrueCacheLock)
        {
            EntitlementTrueUntilUtc.Clear();
        }
    }

    /// <summary>
    /// null = không kết nối được hoặc HTTP lỗi; true/false = server trả JSON rõ ràng.
    /// </summary>
    private static async Task<bool?> QueryServerEntitlementAsync(int placeId, CancellationToken cancellationToken)
    {
        var origins = PlaceApiService.GetCmsBaseUrlCandidatesForSync();
        if (origins.Count == 0)
            return null;

        var device = Uri.EscapeDataString(DeviceInstallIdService.GetOrCreate());
        var cust = AuthService.GetCustomerIdForServerSync();
        var custPart = cust.HasValue && cust.Value > 0 ? $"&customerUserId={cust.Value}" : "";
        foreach (var origin in origins)
        {
            var url = $"{origin.TrimEnd('/')}/api/premium/entitlement?placeId={placeId}&deviceInstallId={device}{custPart}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            CmsTunnelHttp.ApplyTo(req);
            var key = PlaceApiService.GetMobileApiKeyForSync();
            if (!string.IsNullOrWhiteSpace(key))
                req.Headers.TryAddWithoutValidation("X-Mobile-Key", key);

            try
            {
                using var res = await EntitlementHttp.SendAsync(req, cancellationToken).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                    continue;
                await using var stream = await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
                var unlocked = doc.RootElement.TryGetProperty("unlocked", out var u) &&
                               u.ValueKind is JsonValueKind.True;
                return unlocked;
            }
            catch
            {
                // thử origin kế tiếp
            }
        }

        return null;
    }

    private static void MaybeScheduleSilentEntitlementRefresh(int placeId)
    {
        lock (SilentRefreshLock)
        {
            if (LastSilentRefreshUtc.TryGetValue(placeId, out var t) && (DateTime.UtcNow - t).TotalSeconds < 90)
                return;
            LastSilentRefreshUtc[placeId] = DateTime.UtcNow;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var r = await QueryServerEntitlementAsync(placeId, CancellationToken.None).ConfigureAwait(false);
                if (r == false)
                    ClearPersistentUnlock(placeId);
                else if (r == true)
                {
                    SavePersistentUnlock(placeId);
                    TouchMemoryTrueCache(BuildEntitlementCacheKey(placeId));
                }
            }
            catch
            {
                // Bỏ qua — chỉ là đồng bộ nền.
            }
        });
    }

    public static async Task<bool> CheckEntitlementAsync(int placeId, CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildEntitlementCacheKey(placeId);
        if (TryMemoryTrueCache(cacheKey))
            return true;

        if (HasPersistentUnlock(placeId))
        {
            MaybeScheduleSilentEntitlementRefresh(placeId);
            return true;
        }

        var server = await QueryServerEntitlementAsync(placeId, cancellationToken).ConfigureAwait(false);
        if (server == true)
        {
            SavePersistentUnlock(placeId);
            TouchMemoryTrueCache(cacheKey);
            return true;
        }

        if (server == false)
        {
            ClearPersistentUnlock(placeId);
            return false;
        }

        return false;
    }

    public static async Task<(bool Ok, string Message, bool AlreadyUnlocked)> PayDemoAsync(
        int placeId,
        CancellationToken cancellationToken = default)
    {
        var origins = PlaceApiService.GetCmsBaseUrlCandidatesForSync();
        if (origins.Count == 0)
            return (false, "Chưa cấu hình máy chủ CMS.", false);

        var body = new
        {
            placeId,
            deviceInstallId = DeviceInstallIdService.GetOrCreate(),
            customerUserId = AuthService.GetCustomerIdForServerSync()
        };
        string? lastError = null;

        foreach (var origin in origins)
        {
            try
            {
                var url = $"{origin.TrimEnd('/')}/api/premium/pay-demo";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                CmsTunnelHttp.ApplyTo(req);
                var key = PlaceApiService.GetMobileApiKeyForSync();
                if (!string.IsNullOrWhiteSpace(key))
                    req.Headers.TryAddWithoutValidation("X-Mobile-Key", key);
                req.Content = JsonContent.Create(body);

                using var res = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
                var txt = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(txt) ? "{}" : txt);
                var root = doc.RootElement;
                var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                var msg = root.TryGetProperty("message", out var mEl) && mEl.ValueKind == JsonValueKind.String
                    ? mEl.GetString() ?? ""
                    : (res.IsSuccessStatusCode ? "Xong." : $"HTTP {(int)res.StatusCode}");
                var already = root.TryGetProperty("alreadyUnlocked", out var aEl) && aEl.ValueKind == JsonValueKind.True;
                if (ok || already)
                {
                    SavePersistentUnlock(placeId);
                    TouchMemoryTrueCache(BuildEntitlementCacheKey(placeId));
                }

                if (res.IsSuccessStatusCode || already)
                    return (ok || already, msg, already);

                lastError = msg;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        return (false, lastError ?? "Không kết nối được CMS.", false);
    }
}
