using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TourGuideApp2.Services;

/// <summary>Đẩy tuyến cục bộ lên CMS (chỉ khi <see cref="AuthService.GetCustomerIdForServerSync"/> có giá trị).</summary>
public static class CustomerRouteSyncService
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(28) };
        CmsTunnelHttp.ApplyTo(h);
        return h;
    }
    private static readonly object DebounceLock = new();
    private static CancellationTokenSource? _debounceCts;

    public static void ScheduleUploadAfterLocalSave()
    {
        if (AuthService.GetCustomerIdForServerSync() is null)
            return;

        lock (DebounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;
            _ = UploadDebouncedAsync(token);
        }
    }

    private static async Task UploadDebouncedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(1200, token).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (AuthService.GetCustomerIdForServerSync() is not { } uid)
            return;

        var origins = PlaceApiService.GetCmsBaseUrlCandidatesForSync();
        if (origins.Count == 0)
            return;

        List<RouteTrackPoint> points;
        try
        {
            points = await RouteTrackService.GetPointsAsync().ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        foreach (var origin in origins)
        {
            try
            {
                var url = $"{origin.TrimEnd('/')}/api/customers/route-sync";
                var body = new RouteSyncUploadDto
                {
                    CustomerUserId = uid,
                    Points = points.Select(p => new RoutePointUploadDto
                    {
                        Latitude = p.Latitude,
                        Longitude = p.Longitude,
                        TimestampUtc = p.TimestampUtc.ToUniversalTime().ToString("O"),
                        Source = p.Source ?? string.Empty
                    }).ToList()
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                CmsTunnelHttp.ApplyTo(req);
                req.Content = JsonContent.Create(
                    body,
                    mediaType: null,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });
                var mobileKey = PlaceApiService.GetMobileApiKeyForSync();
                if (!string.IsNullOrWhiteSpace(mobileKey))
                    req.Headers.TryAddWithoutValidation("X-Mobile-Key", mobileKey);

                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                _ = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // thử origin kế tiếp
            }
        }

        // Máy chủ tắt / mạng — giữ bản cục bộ.
    }

    private sealed class RouteSyncUploadDto
    {
        public int CustomerUserId { get; set; }
        public List<RoutePointUploadDto>? Points { get; set; }
    }

    private sealed class RoutePointUploadDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? TimestampUtc { get; set; }
        public string? Source { get; set; }
    }
}
