using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace TourGuideApp2.Services;

/// <summary>Chỉ gửi heartbeat khi tab Bản đồ đang hiển thị — CMS đếm "online" theo đó.</summary>
public static class DeviceHeartbeatService
{
    private static readonly HttpClient Http = CreateHttp();
    private static readonly object MapGate = new();
    private static CancellationTokenSource? _mapLoopCts;

    private static HttpClient CreateHttp()
        => CmsTunnelHttp.CreateReliableHttpClient(TimeSpan.FromSeconds(28));

    /// <summary>Bắt đầu ping định kỳ — gọi từ MapPage.OnAppearing.</summary>
    public static void StartMapTabSession()
    {
        CancellationTokenSource cts;
        lock (MapGate)
        {
            try
            {
                _mapLoopCts?.Cancel();
                _mapLoopCts?.Dispose();
            }
            catch
            {
                // bỏ qua
            }

            _mapLoopCts = new CancellationTokenSource();
            cts = _mapLoopCts;
        }

        _ = MapHeartbeatLoopAsync(cts.Token);
    }

    /// <summary>Dừng ping và báo đã rời tab Bản đồ (OnDisappearing).</summary>
    public static async Task NotifyMapTabLeftAsync()
    {
        CancellationTokenSource? old;
        lock (MapGate)
        {
            old = _mapLoopCts;
            _mapLoopCts = null;
        }

        try
        {
            old?.Cancel();
            old?.Dispose();
        }
        catch
        {
            // bỏ qua
        }

        await SendHeartbeatCoreAsync(false, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task MapHeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatCoreAsync(true, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // không thoát vòng — thử lại sau delay
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task SendHeartbeatCoreAsync(bool isOnMapTab, CancellationToken ct)
    {
        var origins = PlaceApiService.GetCmsBaseUrlCandidatesForSync();
        if (origins.Count == 0)
            return;

        var body = new HeartbeatDto
        {
            DeviceInstallId = DeviceInstallIdService.GetOrCreate(),
            Platform = DeviceInfo.Current.Platform.ToString(),
            AppVersion = AppInfo.Current.VersionString,
            IsOnMapTab = isOnMapTab
        };

        foreach (var origin in origins)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryPostAsync(origin, body, ct).ConfigureAwait(false))
            {
                PlaceApiService.RememberSuccessfulCmsOrigin(origin);
                return;
            }
        }
    }

    private static async Task<bool> TryPostAsync(string origin, HeartbeatDto body, CancellationToken ct)
    {
        try
        {
            var url = $"{origin.TrimEnd('/')}/api/devices/heartbeat";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            CmsTunnelHttp.ApplyTo(req);
            req.Content = JsonContent.Create(body);
            var mobileKey = PlaceApiService.GetMobileApiKeyForSync();
            if (!string.IsNullOrWhiteSpace(mobileKey))
                req.Headers.TryAddWithoutValidation("X-Mobile-Key", mobileKey);

            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            _ = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private sealed class HeartbeatDto
    {
        [JsonPropertyName("deviceInstallId")]
        public string DeviceInstallId { get; set; } = "";

        [JsonPropertyName("platform")]
        public string? Platform { get; set; }

        [JsonPropertyName("appVersion")]
        public string? AppVersion { get; set; }

        [JsonPropertyName("isOnMapTab")]
        public bool IsOnMapTab { get; set; }
    }
}
