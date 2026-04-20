using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace TourGuideApp2.Services;

/// <summary>Đồng bộ lượt phát thuyết minh lên CMS theo từng thiết bị (và tùy chọn tài khoản).</summary>
public static class PlaySyncService
{
    /// <summary>4G / mạng chậm: timeout dài hơn; lượt vẫn lưu file chờ nếu lỗi.</summary>
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(28) };
        CmsTunnelHttp.ApplyTo(h);
        return h;
    }
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _pendingFilePath;

    /// <summary>Lazy: tránh gọi <see cref="FileSystem.AppDataDirectory"/> khi static init chạy trước MAUI platform.</summary>
    private static string PendingFilePath => _pendingFilePath ??= Path.Combine(FileSystem.AppDataDirectory, "play-sync-pending.json");

    public static void Enqueue(string placeName, string source, string language, double? durationSeconds, DateTime timestampLocal)
    {
        _ = SendQueuedAndCurrentAsync(placeName, source, language, durationSeconds, timestampLocal);
    }

    /// <summary>Gửi lại toàn bộ lượt đang chờ (sau khi có mạng tới CMS, mở app, vào Lịch sử/Bản đồ).</summary>
    public static Task FlushPendingAsync(CancellationToken cancellationToken = default)
        => FlushPendingCoreAsync(cancellationToken);

    private static async Task FlushPendingCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var pending = await ReadPendingAsync().ConfigureAwait(false);
                if (pending.Count == 0)
                    return;

                var origins = ResolveSyncOrigins();
                if (origins.Count == 0)
                    return;

                await DrainQueueAsync(origins, pending, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Bỏ qua.
        }
        catch
        {
            // Không chặn UI.
        }
    }

    private static IReadOnlyList<string> ResolveSyncOrigins()
    {
        return PlaceApiService.GetCmsBaseUrlCandidatesForSync();
    }

    /// <summary>Gửi tuần tự từ đầu; gặp lỗi thì giữ từ phần tử đó trở đi.</summary>
    private static async Task DrainQueueAsync(IReadOnlyList<string> origins, List<PlayLogDto> pending, CancellationToken cancellationToken)
    {
        for (var i = 0; i < pending.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TrySendOneWithFallbackAsync(origins, pending[i]).ConfigureAwait(false))
            {
                await WritePendingAsync(pending.Skip(i).ToList()).ConfigureAwait(false);
                return;
            }
        }

        await WritePendingAsync([]).ConfigureAwait(false);
    }

    private static async Task SendQueuedAndCurrentAsync(string placeName, string source, string language, double? durationSeconds,
    DateTime timestampLocal)
    {
        var current = new PlayLogDto
        {
            CustomerUserId = AuthService.GetCustomerIdForServerSync(),
            DeviceInstallId = DeviceInstallIdService.GetOrCreate(),
            DeviceName = $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim(),
            PlaceName = placeName,
            Source = source,
            Language = language,
            DurationSeconds = durationSeconds,
            PlayedAtUtc = timestampLocal.ToUniversalTime().ToString("O")
        };

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var pending = await ReadPendingAsync().ConfigureAwait(false);
            pending.Add(current);

            var origins = ResolveSyncOrigins();

            // === THAY ĐỔI Ở ĐÂY: Luôn lưu local trước ===
            await WritePendingAsync(pending).ConfigureAwait(false);

            // Chỉ thử gửi nếu có địa chỉ CMS
            if (origins.Count > 0)
            {
                await DrainQueueAsync(origins, pending, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("⚠️ CMS chưa cấu hình hoặc đang tắt → log đã lưu local");
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<bool> TrySendOneAsync(string origin, PlayLogDto body)
    {
        try
        {
            var url = $"{origin.TrimEnd('/')}/api/plays/log";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            CmsTunnelHttp.ApplyTo(req);
            req.Content = JsonContent.Create(body);
            var mobileKey = PlaceApiService.GetMobileApiKeyForSync();
            if (!string.IsNullOrWhiteSpace(mobileKey))
                req.Headers.TryAddWithoutValidation("X-Mobile-Key", mobileKey);

            using var res = await Http.SendAsync(req).ConfigureAwait(false);
            _ = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                PlaceApiService.TryLearnPublicSyncOriginFromRawUrl(origin);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TrySendOneWithFallbackAsync(IReadOnlyList<string> origins, PlayLogDto body)
    {
        foreach (var origin in origins)
        {
            if (await TrySendOneAsync(origin, body).ConfigureAwait(false))
                return true;
        }

        return false;
    }

    private static async Task<List<PlayLogDto>> ReadPendingAsync()
    {
        try
        {
            if (!File.Exists(PendingFilePath))
                return [];
            await using var s = File.OpenRead(PendingFilePath);
            return await System.Text.Json.JsonSerializer.DeserializeAsync<List<PlayLogDto>>(s).ConfigureAwait(false) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task WritePendingAsync(List<PlayLogDto> pending)
    {
        try
        {
            await using var s = File.Create(PendingFilePath);
            await System.Text.Json.JsonSerializer.SerializeAsync(s, pending).ConfigureAwait(false);
        }
        catch
        {
            // Bỏ qua lỗi file cục bộ để không ảnh hưởng luồng phát.
        }
    }

    private sealed class PlayLogDto
    {
        [JsonPropertyName("customerUserId")]
        public int? CustomerUserId { get; set; }

        [JsonPropertyName("deviceInstallId")]
        public string? DeviceInstallId { get; set; }

        [JsonPropertyName("deviceName")]
        public string? DeviceName { get; set; }

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
