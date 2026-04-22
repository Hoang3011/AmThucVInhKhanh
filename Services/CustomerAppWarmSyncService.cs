using Microsoft.Maui.Networking;

namespace TourGuideApp2.Services;

/// <summary>
/// Đồng bộ nền cho app khách: làm mới POI từ CMS (có giới hạn tần suất) và luôn thử đẩy
/// <see cref="PlaySyncService"/> + tuyến khi có mạng — không cần màn «Thiết bị đồng bộ CMS».
/// </summary>
public static class CustomerAppWarmSyncService
{
    private static readonly object PlacesLock = new();
    private static DateTime _lastPlacesFetchUtc = DateTime.MinValue;
    private static bool _placesWarmedOnce;
    private const int PlacesMinIntervalSeconds = 28;

    /// <summary>Gọi fire-and-forget từ OnAppearing / OnResume / đổi mạng.</summary>
    public static void Schedule()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(OperatingSystem.IsAndroid() ? 700 : 350).ConfigureAwait(false);

                var fetchPlaces = false;
                lock (PlacesLock)
                {
                    var now = DateTime.UtcNow;
                    if (!_placesWarmedOnce || (now - _lastPlacesFetchUtc).TotalSeconds >= PlacesMinIntervalSeconds)
                    {
                        fetchPlaces = true;
                        _lastPlacesFetchUtc = now;
                        _placesWarmedOnce = true;
                    }
                }

                if (fetchPlaces)
                    _ = await PlaceApiService.TryGetRemotePlacesAsync().ConfigureAwait(false);

                await PlaySyncService.FlushPendingAsync().ConfigureAwait(false);

                if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                    CustomerRouteSyncService.TryFlushOnNetworkAvailable();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CustomerAppWarmSyncService: {ex}");
            }
        });
    }
}
