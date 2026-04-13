using System.Diagnostics;
using System.Net;
using System.Text.Json;
using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

public enum RemoteHistoryFetchStatus
{
    SkippedNotRemoteSession,
    SkippedLocalSession,
    /// <summary>Chưa có URL API POI / gốc CMS.</summary>
    SkippedNoCmsUrl,
    Unauthorized,
    Failed,
    Ok
}

/// <summary>Kết quả tải lịch sử từ <c>/api/plays/history</c>.</summary>
public readonly record struct RemoteHistoryFetchResult(
    RemoteHistoryFetchStatus Status,
    IReadOnlyList<HistoryEntry> Items,
    string? Message);

/// <summary>Tải lịch sử lượt phát từ CMS (cùng nguồn với trang /Plays) khi khách đăng nhập từ xa.</summary>
public static class RemotePlayHistoryService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };

    public static Task<RemoteHistoryFetchResult> FetchForCurrentCustomerAsync()
        => FetchForCurrentCustomerAsync(CancellationToken.None);

    public static async Task<RemoteHistoryFetchResult> FetchForCurrentCustomerAsync(CancellationToken ct)
    {
        if (AuthService.GetCustomerIdForServerSync() is null)
        {
            if (AuthService.IsLoggedIn)
            {
                return new RemoteHistoryFetchResult(
                    RemoteHistoryFetchStatus.SkippedLocalSession,
                    [],
                    "Phiên cục bộ: đăng xuất rồi đăng nhập lại khi CMS và mạng ổn để lấy Id khách từ máy chủ (đồng bộ Lượt phát / tuyến).");
            }

            return new RemoteHistoryFetchResult(
                RemoteHistoryFetchStatus.SkippedNotRemoteSession,
                [],
                "Đăng nhập tài khoản ở tab Chính để gộp lịch sử với trang Lượt phát CMS.");
        }

        // Cùng gốc với đồng bộ lượt phát: ưu tiên URL công khai (Cài đặt) để 4G tới được CMS.
        var origin = PlaceApiService.GetCmsBaseUrlForListenPayLinks();
        if (string.IsNullOrWhiteSpace(origin))
            origin = PlaceApiService.GetCmsBaseUrl();
        if (string.IsNullOrWhiteSpace(origin))
        {
            return new RemoteHistoryFetchResult(
                RemoteHistoryFetchStatus.SkippedNoCmsUrl,
                [],
                "Chưa cấu hình URL API POI (Cài đặt hoặc bản build).");
        }

        var id = AuthService.GetCustomerIdForServerSync()!.Value;
        var url = $"{origin.TrimEnd('/')}/api/plays/history?customerUserId={id}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var mobileKey = PlaceApiService.GetMobileApiKeyForSync();
            if (!string.IsNullOrWhiteSpace(mobileKey))
                req.Headers.TryAddWithoutValidation("X-Mobile-Key", mobileKey);

            var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new RemoteHistoryFetchResult(
                    RemoteHistoryFetchStatus.Unauthorized,
                    [],
                    "401: trong Cài đặt nhập Khóa đồng bộ CMS trùng App:MobileApiKey (hoặc để trống khóa trên CMS).");
            }

            if (!res.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(res).ConfigureAwait(false);
                return new RemoteHistoryFetchResult(
                    RemoteHistoryFetchStatus.Failed,
                    [],
                    $"HTTP {(int)res.StatusCode}: {body}");
            }

            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var rows = JsonSerializer.Deserialize<List<RemotePlayRow>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (rows is null)
            {
                return new RemoteHistoryFetchResult(
                    RemoteHistoryFetchStatus.Failed,
                    [],
                    "Phản hồi JSON không đọc được.");
            }

            var items = rows.Select(Map).OrderByDescending(x => x.Timestamp).ToList();
            return new RemoteHistoryFetchResult(RemoteHistoryFetchStatus.Ok, items, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RemotePlayHistory] {ex}");
            return new RemoteHistoryFetchResult(
                RemoteHistoryFetchStatus.Failed,
                [],
                ex.Message);
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage res)
    {
        try
        {
            return (await res.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
        }
        catch
        {
            return "";
        }
    }

    private static HistoryEntry Map(RemotePlayRow r)
    {
        var played = DateTime.TryParse(r.PlayedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : DateTime.UtcNow;
        if (played.Kind == DateTimeKind.Unspecified)
            played = DateTime.SpecifyKind(played, DateTimeKind.Utc);

        return new HistoryEntry
        {
            PlaceName = r.PlaceName ?? string.Empty,
            Source = r.Source ?? string.Empty,
            Language = string.IsNullOrWhiteSpace(r.Language) ? "vi" : r.Language!,
            Timestamp = played.ToLocalTime(),
            DurationSeconds = r.DurationSeconds
        };
    }

    private sealed class RemotePlayRow
    {
        public int Id { get; set; }
        public string? PlaceName { get; set; }
        public string? Source { get; set; }
        public string? Language { get; set; }
        public double? DurationSeconds { get; set; }
        public string? PlayedAtUtc { get; set; }
    }
}
