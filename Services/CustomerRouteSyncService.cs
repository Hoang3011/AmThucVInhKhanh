using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Maui.Devices;

namespace TourGuideApp2.Services;

/// <summary>Đẩy tuyến cục bộ lên CMS theo thiết bị (có/không có đăng nhập đều đồng bộ).</summary>
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
    private static readonly SemaphoreSlim UploadGate = new(1, 1);

    public static void ScheduleUploadAfterLocalSave()
    {
        lock (DebounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;
            _ = UploadDebouncedAsync(token);
        }
    }

    /// <summary>Gọi khi vừa có Internet (4G/Wi‑Fi) — đẩy tuyến gần như ngay, không phụ thuộc debounce 1.2s (một số máy đổi mạng xong không mở lại tab Bản đồ).</summary>
    public static void TryFlushOnNetworkAvailable()
    {
        _ = FlushSoonAfterNetworkAsync();
    }

    private static async Task FlushSoonAfterNetworkAsync()
    {
        try
        {
            await Task.Delay(400).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        await UploadRouteSnapshotCoreAsync(CancellationToken.None).ConfigureAwait(false);
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

        await UploadRouteSnapshotCoreAsync(token).ConfigureAwait(false);
    }

    private static async Task UploadRouteSnapshotCoreAsync(CancellationToken cancellationToken)
    {
        var gateEntered = false;
        try
        {
            await UploadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateEntered = true;

            var uid = AuthService.GetCustomerIdForServerSync();
            var deviceInstallId = DeviceInstallIdService.GetOrCreate();
            var deviceName = $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim();
            if (string.IsNullOrWhiteSpace(deviceInstallId))
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
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var url = $"{origin.TrimEnd('/')}/api/customers/route-sync";
                    var body = new RouteSyncUploadDto
                    {
                        CustomerUserId = uid,
                        DeviceInstallId = deviceInstallId,
                        DeviceName = deviceName,
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

                    using var res = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
                    _ = await res.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    if (res.IsSuccessStatusCode)
                    {
                        PlaceApiService.TryLearnPublicSyncOriginFromRawUrl(origin);
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // thử origin kế tiếp
                }
            }
        }
        finally
        {
            if (gateEntered)
            {
                try
                {
                    UploadGate.Release();
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private sealed class RouteSyncUploadDto
    {
        public int? CustomerUserId { get; set; }
        public string? DeviceInstallId { get; set; }
        public string? DeviceName { get; set; }
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
