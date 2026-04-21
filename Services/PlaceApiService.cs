using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Linq;
using Microsoft.Maui.Networking;
using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

public static class PlaceApiService
{
    public const string PoiApiUrlPreferenceKey = "PoiApiUrl";
    public const string PoiApiKeyPreferenceKey = "PoiApiKey";
    /// <summary>URL gốc CMS cho QR / Zalo (vd. <c>http://192.168.1.5:5095</c>) khi URL API đang trỏ localhost/emulator.</summary>
    public const string CmsListenPayPublicBaseUrlKey = "CmsListenPayPublicBaseUrl";
    /// <summary>Trùng <c>App:MobileApiKey</c> trên CMS — cần khi CMS bật khóa (lượt phát, tuyến, lịch sử).</summary>
    public const string CmsMobileApiKeyPreferenceKey = "CmsMobileApiKey";

    /// <summary>Gốc tunnel/public vừa gọi API thành công — ưu tiên đầu danh sách lần sau (đa thiết bị / 4G).</summary>
    private const string CmsLastSuccessfulNonLanOriginKey = "CmsLastSuccessfulNonLanOrigin";

    /// <summary>Ưu tiên khóa đã lưu trong Cài đặt, sau đó <see cref="AppConfig.MobileApiKey"/>.</summary>
    public static string GetMobileApiKeyForSync()
    {
        var fromPrefs = (Preferences.Default.Get(CmsMobileApiKeyPreferenceKey, string.Empty) ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(fromPrefs))
            return fromPrefs;
        return (AppConfig.MobileApiKey ?? string.Empty).Trim();
    }

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>Timeout ngắn khi tải danh sách POI — tránh tab Bản đồ “đơ” hàng chục giây khi API/CMS không tới được (mạng khác).</summary>
    private static readonly HttpClient PlacesFetchHttp = CreatePlacesFetchHttp();

    private static HttpClient CreatePlacesFetchHttp()
    {
        // Tunnel + 4G máy yếu (A125…): timeout ngắn hay fail; ConnectTimeout dài xử lý trong SocketsHttpHandler.
        return CmsTunnelHttp.CreateReliableHttpClient(TimeSpan.FromSeconds(34));
    }

    private static readonly JsonSerializerOptions JsonDefault = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Supabase/PostgREST thường trả cột snake_case (<c>vietnamese_audio_text</c>, …).</summary>
    private static readonly JsonSerializerOptions JsonSnake = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Trả về <c>null</c> khi chưa cấu hình URL, lỗi mạng, hoặc payload rỗng — caller nên đọc SQLite cục bộ (<c>VinhKhanh.db</c>).
    /// </summary>
    public static string GetEffectiveApiUrl()
    {
        return GetApiUrlCandidatesForPlaces().FirstOrDefault() ?? string.Empty;
    }

    public static bool HasRemoteApiConfigured()
        => !string.IsNullOrWhiteSpace(GetEffectiveApiUrl());

    public static IReadOnlyList<string> GetCmsSyncOrigins()
    {
        var list = new List<string>();
        AddOrigin(list, GetConfiguredPublicCmsOrigin());
        AddOrigin(list, GetCmsBaseUrl());
        AddOrigin(list, AppConfig.GetCmsOrigin());

        foreach (var raw in new[]
                 {
                     (Preferences.Default.Get(PoiApiUrlPreferenceKey, string.Empty) ?? string.Empty).Trim(),
                     AppConfig.DefaultPoiApiUrl.Trim()
                 })
        {
            if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var u))
                continue;
            AddOrigin(list, $"{u.Scheme}://{u.Authority}");
        }

        return list;
    }

    /// <summary>Gốc CMS (scheme + host) trùng với API POI — đăng nhập từ xa, đồng bộ lượt phát và lịch sử.</summary>
    public static string GetCmsBaseUrl()
    {
        var apiUrl = GetEffectiveApiUrl();
        if (!string.IsNullOrWhiteSpace(apiUrl) && Uri.TryCreate(apiUrl.Trim(), UriKind.Absolute, out var u))
            return $"{u.Scheme}://{u.Authority}";
        return AppConfig.GetCmsOrigin();
    }

    private static bool IsHostUnusableForPhoneQr(string? host)
    {
        if (string.IsNullOrEmpty(host)) return true;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("::1", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("[::1]", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("10.0.2.2", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsLikelyLocalOnlyHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return true;
        if (IsHostUnusableForPhoneQr(host))
            return true;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(host, out var ip))
            return false;

        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        // RFC1918 + link-local IPv4
        if (bytes[0] == 10)
            return true;
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            return true;
        if (bytes[0] == 192 && bytes[1] == 168)
            return true;
        if (bytes[0] == 169 && bytes[1] == 254)
            return true;
        return false;
    }

    /// <summary>4G / Bluetooth: không tin URL LAN — tránh treo hàng chục giây trên từng request (vd. Samsung A125).</summary>
    private static bool PreferReachablePublicCmsEndpointsOnly()
    {
        try
        {
            var access = Connectivity.Current.NetworkAccess;
            if (access is not NetworkAccess.Internet and not NetworkAccess.ConstrainedInternet)
                return false;

            foreach (var p in Connectivity.Current.ConnectionProfiles)
            {
                if (p == ConnectionProfile.WiFi || p == ConnectionProfile.Ethernet)
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Chỉ khi **không** có Wi‑Fi/Ethernet (4G thuần / Bluetooth…) — tránh bỏ LAN khi nhà vẫn Wi‑Fi + SIM bật (CMS chỉ LAN).</summary>
    private static bool TreatAsOffHomeLanForCms()
        => PreferReachablePublicCmsEndpointsOnly();

    /// <summary>Ghi nhớ gốc tunnel/public sau bất kỳ API CMS thành công — dùng sắp xếp ưu tiên lần sau.</summary>
    public static void RememberSuccessfulCmsOrigin(string? rawUrlOrOrigin)
    {
        try
        {
            var raw = (rawUrlOrOrigin ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var u))
                return;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
                return;
            if (IsHostUnusableForPhoneQr(u.Host) || IsLikelyLocalOnlyHost(u.Host))
                return;

            var origin = $"{u.Scheme}://{u.Authority}".TrimEnd('/');
            Preferences.Default.Set(CmsLastSuccessfulNonLanOriginKey, origin);
        }
        catch
        {
            // bỏ qua
        }
    }

    private static string? GetLastSuccessfulNonLanOrigin()
    {
        var s = (Preferences.Default.Get(CmsLastSuccessfulNonLanOriginKey, string.Empty) ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(s) || !Uri.TryCreate(s, UriKind.Absolute, out var u))
            return null;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
            return null;
        if (IsHostUnusableForPhoneQr(u.Host) || IsLikelyLocalOnlyHost(u.Host))
            return null;
        return $"{u.Scheme}://{u.Authority}".TrimEnd('/');
    }

    private static void PrependLastSuccessfulNonLanOrigin(List<string> list)
    {
        var last = GetLastSuccessfulNonLanOrigin();
        if (string.IsNullOrEmpty(last))
            return;

        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (string.Equals(list[i], last, StringComparison.OrdinalIgnoreCase))
                list.RemoveAt(i);
        }

        list.Insert(0, last);
    }

    /// <summary>Bỏ host LAN khỏi danh sách khi đang cellular và vẫn còn ít nhất một host public — tránh fail multi-device.</summary>
    private static void ApplyCrossNetworkOriginPolicy(List<string> list)
    {
        if (list.Count == 0 || !TreatAsOffHomeLanForCms())
            return;

        static bool IsNonLocal(string o) =>
            Uri.TryCreate(o, UriKind.Absolute, out var u) && !IsLikelyLocalOnlyHost(u.Host);

        if (!list.Any(IsNonLocal))
            return;

        list.RemoveAll(o => Uri.TryCreate(o, UriKind.Absolute, out var u) && IsLikelyLocalOnlyHost(u.Host));
    }

    private static bool HasAnyUsablePublicCmsEndpointHint()
    {
        if (!string.IsNullOrEmpty(GetConfiguredPublicCmsOrigin()))
            return true;
        return GetLastSuccessfulNonLanOrigin() is not null;
    }

    private static string GetConfiguredPublicCmsOrigin()
    {
        var explicitBase = (Preferences.Default.Get(CmsListenPayPublicBaseUrlKey, "") ?? "").Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(explicitBase)
            && Uri.TryCreate(explicitBase, UriKind.Absolute, out var ex)
            && !IsHostUnusableForPhoneQr(ex.Host))
            return $"{ex.Scheme}://{ex.Authority}";

        var cfgPublic = (AppConfig.DefaultPublicCmsBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(cfgPublic)
            && Uri.TryCreate(cfgPublic, UriKind.Absolute, out var cfgU)
            && !IsHostUnusableForPhoneQr(cfgU.Host))
            return $"{cfgU.Scheme}://{cfgU.Authority}";

        return string.Empty;
    }

    /// <summary>
    /// Gốc CMS cho QR/Zalo, **đồng bộ lượt phát**, entitlement, đăng nhập từ xa khi cần host **4G** tới được.
    /// Thứ tự: Cài đặt → <see cref="AppConfig.DefaultPublicCmsBaseUrl"/> → API POI nếu không phải localhost.
    /// </summary>
    public static string GetCmsBaseUrlForListenPayLinks()
    {
        var configuredPublic = GetConfiguredPublicCmsOrigin();
        if (!string.IsNullOrEmpty(configuredPublic))
            return configuredPublic;

        var cms = GetCmsBaseUrl().TrimEnd('/');
        if (!string.IsNullOrEmpty(cms)
            && Uri.TryCreate(cms, UriKind.Absolute, out var u)
            && !IsHostUnusableForPhoneQr(u.Host))
            return $"{u.Scheme}://{u.Authority}";

        foreach (var raw in new[] { (Preferences.Default.Get(PoiApiUrlPreferenceKey, "") ?? "").Trim(), AppConfig.DefaultPoiApiUrl.Trim() })
        {
            if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var c))
                continue;
            if (IsHostUnusableForPhoneQr(c.Host))
                continue;
            return $"{c.Scheme}://{c.Authority}";
        }

        return cms;
    }

    /// <summary>
    /// Danh sách gốc CMS để đồng bộ (ưu tiên public/tunnel, rồi LAN). App sẽ thử tuần tự đến khi thành công.
    /// </summary>
    public static IReadOnlyList<string> GetCmsBaseUrlCandidatesForSync()
        => GetCmsBaseUrlCandidatesForSync(applyCrossNetworkOriginPolicy: true);

    /// <param name="applyCrossNetworkOriginPolicy">false = vẫn sort public trước nhưng không loại LAN (fallback sau khi presence thất bại trên 4G).</param>
    public static IReadOnlyList<string> GetCmsBaseUrlCandidatesForSync(bool applyCrossNetworkOriginPolicy)
    {
        var list = new List<string>();
        AddOriginCandidate(list, GetConfiguredPublicCmsOrigin());
        AddOriginCandidate(list, GetCmsBaseUrl());
        AddOriginCandidate(list, AppConfig.GetCmsOrigin());
        AddOriginCandidate(list, (Preferences.Default.Get(CmsListenPayPublicBaseUrlKey, "") ?? "").Trim().TrimEnd('/'));
        AddOriginCandidate(list, ParseOriginFromApiUrl(Preferences.Default.Get(PoiApiUrlPreferenceKey, string.Empty) ?? string.Empty));
        AddOriginCandidate(list, ParseOriginFromApiUrl(AppConfig.DefaultPoiApiUrl));

        // Thiết bị chỉ có URL LAN trong build: trên 4G LAN treo ~timeout — phải thử host public trước trong cùng một lần gọi.
        list.Sort(static (a, b) =>
        {
            static int LocalRank(string o) =>
                Uri.TryCreate(o, UriKind.Absolute, out var u) && IsLikelyLocalOnlyHost(u.Host) ? 1 : 0;
            var cmp = LocalRank(a).CompareTo(LocalRank(b));
            return cmp != 0 ? cmp : string.CompareOrdinal(a, b);
        });

        if (applyCrossNetworkOriginPolicy)
            ApplyCrossNetworkOriginPolicy(list);
        PrependLastSuccessfulNonLanOrigin(list);
        return list;
    }

    /// <summary>Gộp mọi gốc có thể gọi <c>/api/devices/presence</c>: sync + authority từ từng URL <c>/api/places</c> (một số máy chỉ cấu hình places, chưa có prefs public).</summary>
    public static IReadOnlyList<string> GetMergedCmsOriginCandidatesForPresence()
        => GetMergedCmsOriginCandidatesForPresence(applyCrossNetworkOriginPolicy: true);

    /// <summary>Giống overload không tham số; <paramref name="applyCrossNetworkOriginPolicy"/> = false thì vẫn giữ URL LAN trong danh sách.</summary>
    public static IReadOnlyList<string> GetMergedCmsOriginCandidatesForPresence(bool applyCrossNetworkOriginPolicy)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in GetCmsBaseUrlCandidatesForSync(applyCrossNetworkOriginPolicy))
        {
            var t = (o ?? string.Empty).Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(t))
                set.Add(t);
        }

        foreach (var api in GetApiUrlCandidatesForPlaces(applyCrossNetworkOriginPolicy))
        {
            var origin = ParseOriginFromApiUrl(api);
            var t = (origin ?? string.Empty).Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(t))
                set.Add(t);
        }

        var list = set.ToList();
        list.Sort(static (a, b) =>
        {
            static int LocalRank(string o) =>
                Uri.TryCreate(o, UriKind.Absolute, out var u) && IsLikelyLocalOnlyHost(u.Host) ? 1 : 0;
            var cmp = LocalRank(a).CompareTo(LocalRank(b));
            return cmp != 0 ? cmp : string.CompareOrdinal(a, b);
        });

        if (applyCrossNetworkOriginPolicy)
            ApplyCrossNetworkOriginPolicy(list);
        PrependLastSuccessfulNonLanOrigin(list);
        return list;
    }

    public static IReadOnlyList<string> GetApiUrlCandidatesForPlaces()
        => GetApiUrlCandidatesForPlaces(applyCrossNetworkOriginPolicy: true);

    /// <param name="applyCrossNetworkOriginPolicy">false = không gỡ URL LAN (dùng sau khi đã thử bản lọc 4G).</param>
    public static IReadOnlyList<string> GetApiUrlCandidatesForPlaces(bool applyCrossNetworkOriginPolicy)
    {
        var list = new List<string>();

        var fromPrefs = (Preferences.Default.Get(PoiApiUrlPreferenceKey, string.Empty) ?? string.Empty).Trim();
        AddApiUrlCandidate(list, fromPrefs);

        var configuredPublic = GetConfiguredPublicCmsOrigin();
        if (!string.IsNullOrWhiteSpace(configuredPublic))
            AddApiUrlCandidate(list, $"{configuredPublic}/api/places");

        AddApiUrlCandidate(list, AppConfig.DefaultPoiApiUrl);

        // Giống sync: thử host public trước — tránh máy 2 chỉ còn URL LAN trong prefs thì 4G fail liên tục.
        list.Sort(static (a, b) =>
        {
            static int LocalRank(string url)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
                    return 1;
                return IsLikelyLocalOnlyHost(u.Host) ? 1 : 0;
            }

            var cmp = LocalRank(a).CompareTo(LocalRank(b));
            return cmp != 0 ? cmp : string.CompareOrdinal(a, b);
        });

        if (applyCrossNetworkOriginPolicy && TreatAsOffHomeLanForCms())
        {
            static bool ApiIsNonLocal(string url) =>
                Uri.TryCreate(url, UriKind.Absolute, out var u) && !IsLikelyLocalOnlyHost(u.Host);

            if (list.Any(ApiIsNonLocal))
                list.RemoveAll(url => Uri.TryCreate(url, UriKind.Absolute, out var u) && IsLikelyLocalOnlyHost(u.Host));
        }

        var last = GetLastSuccessfulNonLanOrigin();
        if (!string.IsNullOrEmpty(last))
        {
            var apiFirst = $"{last}/api/places";
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (string.Equals(list[i], apiFirst, StringComparison.OrdinalIgnoreCase))
                    list.RemoveAt(i);
            }

            list.Insert(0, apiFirst);
        }

        return list;
    }

    private static void AddApiUrlCandidate(List<string> list, string? rawApiUrl)
    {
        var raw = (rawApiUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u))
            return;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
            return;
        if (IsHostUnusableForPhoneQr(u.Host))
            return;

        var normalized = u.ToString();
        if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            list.Add(normalized);
    }

    private static string ParseOriginFromApiUrl(string? rawApiUrl)
    {
        var raw = (rawApiUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var u))
            return string.Empty;
        return $"{u.Scheme}://{u.Authority}";
    }

    private static void AddOriginCandidate(List<string> list, string? rawOrigin)
    {
        var raw = (rawOrigin ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(raw))
            return;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u))
            return;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
            return;
        if (IsHostUnusableForPhoneQr(u.Host))
            return;
        var origin = $"{u.Scheme}://{u.Authority}";
        if (!list.Contains(origin, StringComparer.OrdinalIgnoreCase))
            list.Add(origin);
    }

    private static void AddOrigin(List<string> list, string? raw)
    {
        var s = (raw ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(s))
            return;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u))
            return;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
            return;

        var normalized = $"{u.Scheme}://{u.Authority}";
        if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            list.Add(normalized);
    }

    /// <summary>
    /// Demo không cùng WiFi: một URL tunnel/public (vd. <c>https://xxx.ngrok-free.app</c>) hoặc URL đầy đủ …/api/places
    /// → ghi Preferences API POI + gốc đồng bộ (Listen/Pay, lượt phát, entitlement…).
    /// </summary>
    public static bool TryApplyRemoteDemoBaseUrl(string? rawInput, out string message)
    {
        message = string.Empty;
        var trimmed = (rawInput ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(trimmed))
        {
            message = "Dán URL gốc tunnel (https://…) hoặc …/api/places.";
            return false;
        }

        const string placesSuffix = "/api/places";
        string apiPlaces;
        string origin;

        if (trimmed.EndsWith(placesSuffix, StringComparison.OrdinalIgnoreCase))
        {
            apiPlaces = trimmed;
            var without = trimmed[..^placesSuffix.Length].TrimEnd('/');
            if (!Uri.TryCreate(without, UriKind.Absolute, out var u0)
                || (u0.Scheme != Uri.UriSchemeHttp && u0.Scheme != Uri.UriSchemeHttps)
                || IsHostUnusableForPhoneQr(u0.Host))
            {
                message = "URL gốc (trước /api/places) không hợp lệ.";
                return false;
            }

            origin = $"{u0.Scheme}://{u0.Authority}".TrimEnd('/');
        }
        else
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var u)
                || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            {
                message = "URL phải bắt đầu bằng http:// hoặc https://.";
                return false;
            }

            if (IsHostUnusableForPhoneQr(u.Host))
            {
                message = "Không dùng localhost — dùng URL tunnel (ngrok, Cloudflare Tunnel…).";
                return false;
            }

            origin = $"{u.Scheme}://{u.Authority}".TrimEnd('/');
            apiPlaces = $"{origin}/api/places";
        }

        Preferences.Default.Set(PoiApiUrlPreferenceKey, apiPlaces);
        Preferences.Default.Set(CmsListenPayPublicBaseUrlKey, origin);
        message = "Đã gán API POI và gốc đồng bộ (tunnel / public).";
        return true;
    }

    /// <summary>URL trang trả phí demo (mở được trong Zalo/trình duyệt). Rỗng nếu chưa có gốc CMS.</summary>
    public static string GetListenPayUrlForPlace(int placeId)
    {
        var b = GetCmsBaseUrlForListenPayLinks().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(b) || placeId <= 0)
            return string.Empty;
        return $"{b}/Listen/Pay?placeId={placeId}";
    }

    public static Task<List<Place>?> TryGetRemotePlacesAsync()
        => TryGetRemotePlacesAsync(CancellationToken.None);

    public static async Task<List<Place>?> TryGetRemotePlacesAsync(CancellationToken cancellationToken)
    {
        var apiUrls = GetApiUrlCandidatesForPlaces();
        if (apiUrls.Count == 0)
            return null;

        foreach (var apiUrl in apiUrls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                CmsTunnelHttp.ApplyTo(request);
                var apiKey = Preferences.Default.Get(PoiApiKeyPreferenceKey, string.Empty)?.Trim();
                if (string.IsNullOrWhiteSpace(apiKey))
                    apiKey = AppConfig.DefaultPoiApiKey.Trim();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.TryAddWithoutValidation("apikey", apiKey);
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
                }

                using var perUrlCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var isLanHost = Uri.TryCreate(apiUrl, UriKind.Absolute, out var hostUri) && IsLikelyLocalOnlyHost(hostUri.Host);
                perUrlCts.CancelAfter(isLanHost ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(32));

                using var response = await PlacesFetchHttp.SendAsync(request, perUrlCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    continue;

                var media = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrEmpty(media) && media.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    continue;

                var json = await response.Content.ReadAsStringAsync(perUrlCts.Token).ConfigureAwait(false);
                var trimmedJson = (json ?? string.Empty).TrimStart();
                if (trimmedJson.Length == 0 || trimmedJson[0] == '<')
                    continue;

                var apiItems = ParseApiItems(json ?? string.Empty);
                if (apiItems.Count == 0)
                    continue;
                TryLearnPublicSyncOriginFromApiItems(apiItems);

                var mapped = apiItems
                    .Select(MapApiPoiToPlace)
                    .Where(x => x is not null)
                    .Cast<Place>()
                    .ToList();

                if (mapped.Count > 0)
                {
                    // Chỉ học prefs sau khi chắc chắn JSON hợp lệ — tránh 200 HTML (ngrok/WAF) ghi đè tunnel.
                    TryLearnPublicSyncOriginFromRawUrl(apiUrl);
                    RememberSuccessfulCmsOrigin(apiUrl);
                    return mapped;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // timeout / hủy nội bộ — thử URL kế
            }
            catch
            {
                // thử URL kế tiếp
            }
        }

        return null;
    }

    public static async Task<List<Place>> GetPlacesAsync()
    {
        var remote = await TryGetRemotePlacesAsync();
        if (remote is { Count: > 0 })
            return remote;

        var local = await PlaceLocalRepository.TryLoadAsync();
        return local.Places.Count > 0 ? local.Places : [];
    }

    private static List<PoiApiItem> ParseApiItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
                return ParsePoiArray(root);

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "places", "Places", "data", "Data" })
                {
                    if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                        return ParsePoiArray(arr);
                }
            }

            return [];
        }
        catch
        {
            return [];
        }
    }

    private static void TryLearnPublicSyncOriginFromApiItems(List<PoiApiItem> apiItems)
    {
        try
        {
            foreach (var item in apiItems)
            {
                var payload = (item.QrPayload ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(payload))
                    continue;
                if (!Uri.TryCreate(payload, UriKind.Absolute, out var u))
                    continue;
                if ((u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) || IsHostUnusableForPhoneQr(u.Host))
                    continue;
                if (!u.AbsolutePath.Contains("/Listen/Pay", StringComparison.OrdinalIgnoreCase))
                    continue;

                var origin = $"{u.Scheme}://{u.Authority}";
                Preferences.Default.Set(CmsListenPayPublicBaseUrlKey, origin);

                var savedPoiApi = (Preferences.Default.Get(PoiApiUrlPreferenceKey, string.Empty) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(savedPoiApi)
                    || !Uri.TryCreate(savedPoiApi, UriKind.Absolute, out var savedApiUri)
                    || IsLikelyLocalOnlyHost(savedApiUri.Host))
                {
                    Preferences.Default.Set(PoiApiUrlPreferenceKey, $"{origin}/api/places");
                }

                return;
            }
        }
        catch
        {
            // học URL nền thất bại thì bỏ qua, không ảnh hưởng tải POI.
        }
    }

    /// <summary>
    /// Học gốc CMS public từ bất kỳ URL quét được (ví dụ QR Listen/Pay, install, API tunnel).
    /// Không ghi đè bằng host LAN/private để tránh làm hỏng cấu hình chạy khác mạng.
    /// </summary>
    public static void TryLearnPublicSyncOriginFromRawUrl(string? rawUrl)
    {
        var raw = (rawUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u))
            return;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
            return;
        if (IsHostUnusableForPhoneQr(u.Host) || IsLikelyLocalOnlyHost(u.Host))
            return;

        var origin = $"{u.Scheme}://{u.Authority}";
        Preferences.Default.Set(CmsListenPayPublicBaseUrlKey, origin);
        RememberSuccessfulCmsOrigin(rawUrl);

        var savedPoiApi = (Preferences.Default.Get(PoiApiUrlPreferenceKey, string.Empty) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(savedPoiApi)
            || !Uri.TryCreate(savedPoiApi, UriKind.Absolute, out var savedApiUri)
            || IsLikelyLocalOnlyHost(savedApiUri.Host))
        {
            Preferences.Default.Set(PoiApiUrlPreferenceKey, $"{origin}/api/places");
        }
    }

    private static List<PoiApiItem> ParsePoiArray(JsonElement arrayElement)
    {
        var list = new List<PoiApiItem>();
        foreach (var el in arrayElement.EnumerateArray())
        {
            var item = DeserializePoiItem(el);
            if (item is not null)
                list.Add(item);
        }
        return list;
    }

    private static PoiApiItem? DeserializePoiItem(JsonElement el)
    {
        var raw = el.GetRawText();
        var fallback = JsonSerializer.Deserialize<PoiApiItem>(raw, JsonDefault);
        var snake = JsonSerializer.Deserialize<PoiApiItem>(raw, JsonSnake);

        // API nội bộ CMS trả camelCase; một số nguồn ngoài trả snake_case.
        // Chọn bản parse có dữ liệu "giàu" hơn để tránh mất text đa ngôn ngữ.
        var fallbackScore = ScorePoiItem(fallback);
        var snakeScore = ScorePoiItem(snake);

        if (fallbackScore >= snakeScore)
            return IsUsablePoiItem(fallback) ? fallback : snake ?? fallback;

        return IsUsablePoiItem(snake) ? snake : fallback ?? snake;
    }

    private static bool IsUsablePoiItem(PoiApiItem? i)
    {
        if (i is null) return false;
        if (!string.IsNullOrWhiteSpace(i.Name))
            return true;
        return i.Latitude != 0 || i.Longitude != 0;
    }

    private static int ScorePoiItem(PoiApiItem? i)
    {
        if (i is null) return -1;

        var score = 0;
        if (!string.IsNullOrWhiteSpace(i.Name)) score += 4;
        if (i.Latitude != 0 || i.Longitude != 0) score += 3;
        if (!string.IsNullOrWhiteSpace(i.Description)) score += 2;
        if (!string.IsNullOrWhiteSpace(i.VietnameseAudioText)) score += 3;
        if (!string.IsNullOrWhiteSpace(i.EnglishAudioText)) score += 3;
        if (!string.IsNullOrWhiteSpace(i.ChineseAudioText)) score += 3;
        if (!string.IsNullOrWhiteSpace(i.JapaneseAudioText)) score += 3;
        if (!string.IsNullOrWhiteSpace(i.Specialty)) score += 1;
        if (!string.IsNullOrWhiteSpace(i.MapUrl)) score += 1;
        return score;
    }

    private static Place? MapApiPoiToPlace(PoiApiItem dto)
    {
        if (dto is null) return null;

        return new Place
        {
            Id = dto.Id,
            Name = dto.Name ?? "POI",
            Address = dto.Address ?? string.Empty,
            Specialty = dto.Specialty ?? string.Empty,
            ImageUrl = dto.ImageUrl ?? string.Empty,
            MapUrl = dto.MapUrl ?? string.Empty,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            Description = dto.Description ?? string.Empty,
            VietnameseAudioText = dto.VietnameseAudioText ?? string.Empty,
            EnglishAudioText = dto.EnglishAudioText ?? string.Empty,
            ChineseAudioText = dto.ChineseAudioText ?? string.Empty,
            JapaneseAudioText = dto.JapaneseAudioText ?? string.Empty,
            KoreanAudioText = dto.KoreanAudioText ?? string.Empty,
            ActivationRadiusMeters = dto.ActivationRadiusMeters > 0 ? dto.ActivationRadiusMeters : 35,
            Priority = dto.Priority,
            PremiumPriceDemo = dto.PremiumPriceDemo,
            PremiumVietnameseAudioText = dto.PremiumVietnameseAudioText ?? string.Empty
        };
    }

    private static readonly HttpClient CmsPresenceHttp = CreatePresenceHttp();

    private static HttpClient CreatePresenceHttp()
        => CmsTunnelHttp.CreateReliableHttpClient(TimeSpan.FromSeconds(36));

    private static async Task<DevicePresenceResponse?> TryGetDevicePresenceOnOriginsAsync(
        IReadOnlyList<string> origins,
        CancellationToken cancellationToken)
    {
        foreach (var origin in origins)
        {
            if (string.IsNullOrWhiteSpace(origin))
                continue;
            try
            {
                var url = $"{origin.TrimEnd('/')}/api/devices/presence";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                CmsTunnelHttp.ApplyTo(req);
                var mobileKey = GetMobileApiKeyForSync();
                if (!string.IsNullOrWhiteSpace(mobileKey))
                    req.Headers.TryAddWithoutValidation("X-Mobile-Key", mobileKey);

                using var perUrlCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var isLanOrigin = Uri.TryCreate(origin, UriKind.Absolute, out var ou) && IsLikelyLocalOnlyHost(ou.Host);
                perUrlCts.CancelAfter(isLanOrigin ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(34));

                using var res = await CmsPresenceHttp.SendAsync(req, perUrlCts.Token).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                    continue;

                var presenceMedia = res.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrEmpty(presenceMedia) && presenceMedia.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    continue;

                var body = await res.Content.ReadAsStringAsync(perUrlCts.Token).ConfigureAwait(false);
                var trimmed = (body ?? string.Empty).TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith('<') || trimmed.StartsWith("<!"))
                    continue;

                var payload = JsonSerializer.Deserialize<DevicePresenceResponse>(body ?? string.Empty, JsonDefault);
                if (payload is null)
                    continue;
                payload.Devices ??= [];
                RememberSuccessfulCmsOrigin(origin);
                return payload;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Timeout / hủy nội bộ — origin kế.
            }
            catch
            {
                // thử gốc CMS kế tiếp
            }
        }

        return null;
    }

    /// <summary>Đồng bộ danh sách thiết bị với trang CMS «Thiết bị online» (cửa sổ ping ~2 phút, tab Bản đồ).</summary>
    public static async Task<DevicePresenceResponse?> TryGetDevicePresenceAsync(CancellationToken cancellationToken = default)
    {
        // Chỉ prime places khi chưa có tunnel/public trong prefs — tránh gấp đôi thời gian mỗi lần mở tab Bản đồ.
        if (!HasAnyUsablePublicCmsEndpointHint())
        {
            try
            {
                _ = await TryGetRemotePlacesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Vẫn thử presence.
            }
        }

        var primary = await TryGetDevicePresenceOnOriginsAsync(
            GetMergedCmsOriginCandidatesForPresence(applyCrossNetworkOriginPolicy: true),
            cancellationToken).ConfigureAwait(false);
        if (primary is not null)
            return primary;

        // 4G đã bỏ LAN mà tunnel vẫn lỗi: thử lại cả LAN (một số máy tunnel Dev/ngrok chập chờn).
        if (TreatAsOffHomeLanForCms())
        {
            return await TryGetDevicePresenceOnOriginsAsync(
                GetMergedCmsOriginCandidatesForPresence(applyCrossNetworkOriginPolicy: false),
                cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private sealed class PoiApiItem
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? Specialty { get; set; }
        public string? ImageUrl { get; set; }
        public string? MapUrl { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Description { get; set; }
        public string? VietnameseAudioText { get; set; }
        public string? EnglishAudioText { get; set; }
        public string? ChineseAudioText { get; set; }
        public string? JapaneseAudioText { get; set; }
        public string? KoreanAudioText { get; set; }
        public double ActivationRadiusMeters { get; set; }
        public int Priority { get; set; }
        public string? QrPayload { get; set; }
        public double PremiumPriceDemo { get; set; }
        public string? PremiumVietnameseAudioText { get; set; }
    }
}
