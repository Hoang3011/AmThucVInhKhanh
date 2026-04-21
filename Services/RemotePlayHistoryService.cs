using System.Diagnostics;
using System.Net;
using System.Text.Json;
using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

public enum RemoteHistoryFetchStatus
{
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

/// <summary>Tải lịch sử lượt phát từ CMS theo từng thiết bị (web vẫn thấy toàn bộ).</summary>
public static class RemotePlayHistoryService
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
        => CmsTunnelHttp.CreateReliableHttpClient(TimeSpan.FromSeconds(32));

    public static Task<RemoteHistoryFetchResult> FetchForCurrentCustomerAsync()
        => FetchForCurrentCustomerAsync(CancellationToken.None);

    public static async Task<RemoteHistoryFetchResult> FetchForCurrentCustomerAsync(CancellationToken ct)
    {
        var deviceInstallId = DeviceInstallIdService.GetOrCreate();
        if (string.IsNullOrWhiteSpace(deviceInstallId))
            return new RemoteHistoryFetchResult(RemoteHistoryFetchStatus.Failed, [], "Không lấy được mã thiết bị.");

        var origins = PlaceApiService.GetCmsBaseUrlCandidatesForSync();
        if (origins.Count == 0)
        {
            return new RemoteHistoryFetchResult(
                RemoteHistoryFetchStatus.SkippedNoCmsUrl,
                [],
                "Chưa cấu hình URL API POI (Cài đặt hoặc bản build).");
        }

        string? lastFailure = null;

        foreach (var origin in origins)
        {
            var url = $"{origin.TrimEnd('/')}/api/plays/history?deviceInstallId={Uri.EscapeDataString(deviceInstallId)}";

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                CmsTunnelHttp.ApplyTo(req);
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
                    lastFailure = $"HTTP {(int)res.StatusCode}: {body}";
                    continue;
                }

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var rows = JsonSerializer.Deserialize<List<RemotePlayRow>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (rows is null)
                {
                    lastFailure = "Phản hồi JSON không đọc được.";
                    continue;
                }

                var items = rows.Select(Map).OrderByDescending(x => x.Timestamp).ToList();
                PlaceApiService.TryLearnPublicSyncOriginFromRawUrl(origin);
                PlaceApiService.RememberSuccessfulCmsOrigin(origin);
                return new RemoteHistoryFetchResult(RemoteHistoryFetchStatus.Ok, items, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemotePlayHistory] {ex}");
                lastFailure = ex.Message;
            }
        }

        return new RemoteHistoryFetchResult(
            RemoteHistoryFetchStatus.Failed,
            [],
            string.IsNullOrWhiteSpace(lastFailure) ? "Không kết nối được CMS." : lastFailure);
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
