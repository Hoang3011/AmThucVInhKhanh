using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TourGuideApp2.Services;

/// <summary>Đồng bộ lượt phát thuyết minh lên CMS (khi có URL và tài khoản đăng nhập từ máy chủ).</summary>
public static class PlaySyncService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(18) };

    public static void Enqueue(string placeName, string source, string language, double? durationSeconds, DateTime timestampLocal)
    {
        _ = SendAsync(placeName, source, language, durationSeconds, timestampLocal);
    }

    private static async Task SendAsync(string placeName, string source, string language, double? durationSeconds,
        DateTime timestampLocal)
    {
        // Dùng cùng origin với API POI để tránh “log lên server khác”.
        var origin = PlaceApiService.GetCmsBaseUrl();
        if (string.IsNullOrEmpty(origin))
            return;

        try
        {
            var url = $"{origin.TrimEnd('/')}/api/plays/log";
            var body = new PlayLogDto
            {
                CustomerUserId = AuthService.GetCustomerIdForServerSync(),
                PlaceName = placeName,
                Source = source,
                Language = language,
                DurationSeconds = durationSeconds,
                PlayedAtUtc = timestampLocal.ToUniversalTime().ToString("O")
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = JsonContent.Create(body);
            if (!string.IsNullOrWhiteSpace(AppConfig.MobileApiKey))
                req.Headers.TryAddWithoutValidation("X-Mobile-Key", AppConfig.MobileApiKey);

            await Http.SendAsync(req).ConfigureAwait(false);
        }
        catch
        {
            // Không chặn UI nếu máy chủ tắt hoặc mạng lỗi.
        }
    }

    private sealed class PlayLogDto
    {
        [JsonPropertyName("customerUserId")]
        public int? CustomerUserId { get; set; }

        [JsonPropertyName("placeName")]
        public string? PlaceName { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("durationSeconds")]
        public double? DurationSeconds { get; set; }

        [JsonPropertyName("playedAtUtc")]
        public string? PlayedAtUtc { get; set; }
    }
}
