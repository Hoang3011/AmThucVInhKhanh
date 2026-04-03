#if ANDROID
using Android.Content;
using Android.Locations;
using Android.OS;
using SysDiag = System.Diagnostics;

namespace TourGuideApp2;

/// <summary>
/// Đọc vị trí qua <see cref="LocationManager"/> (GPS / Network / Passive).
/// Không dùng <c>IsProviderEnabled</c> để bỏ qua provider: khi bật Fake GPS, GPS có thể báo “tắt” nhưng mock vẫn đẩy tọa độ.
/// Thêm poll <see cref="LocationManager.GetLastKnownLocation"/> vì một số máy không gọi <c>OnLocationChanged</c> đủ.
/// </summary>
internal static class AndroidLocationManagerBridge
{
    private static LocationManager? _locationManager;
    private static NativeLocationListener? _listener;
    private static Handler? _pollHandler;
    private static Java.Lang.Runnable? _pollRunnable;

    public static void Start(Action<double, double> onLatLng)
    {
        Stop();
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx is null)
                return;

            _locationManager = ctx.GetSystemService(Context.LocationService) as LocationManager;
            if (_locationManager is null)
                return;

            _listener = new NativeLocationListener(onLatLng);
            var looper = Looper.MainLooper;
            if (looper is null)
                return;

            // Luôn thử đăng ký — không kiểm tra IsProviderEnabled (mock hay bị bỏ qua).
            foreach (var provider in new[]
                     {
                         LocationManager.GpsProvider,
                         LocationManager.NetworkProvider,
                         LocationManager.PassiveProvider
                     })
            {
                try
                {
                    _locationManager.RequestLocationUpdates(provider, 1000, 0f, _listener, looper);
                }
                catch (Exception ex)
                {
                    SysDiag.Debug.WriteLine($"AndroidLocationManagerBridge RequestUpdates {provider}: {ex.Message}");
                }
            }

            TryEmitBestLastKnown(onLatLng);
            StartLastKnownPoll(onLatLng, looper);
        }
        catch (Exception ex)
        {
            SysDiag.Debug.WriteLine($"AndroidLocationManagerBridge.Start: {ex}");
        }
    }

    static void TryEmitBestLastKnown(Action<double, double> onLatLng)
    {
        if (_locationManager is null)
            return;
        try
        {
            var last = _locationManager.GetLastKnownLocation(LocationManager.PassiveProvider)
                ?? _locationManager.GetLastKnownLocation(LocationManager.GpsProvider)
                ?? _locationManager.GetLastKnownLocation(LocationManager.NetworkProvider);
            if (last is not null)
                onLatLng(last.Latitude, last.Longitude);
        }
        catch (Exception ex)
        {
            SysDiag.Debug.WriteLine($"AndroidLocationManagerBridge last known: {ex.Message}");
        }
    }

    static void StartLastKnownPoll(Action<double, double> onLatLng, Looper looper)
    {
        StopLastKnownPollOnly();
        _pollHandler = new Handler(looper);
        _pollRunnable = new Java.Lang.Runnable(() =>
        {
            try
            {
                TryEmitBestLastKnown(onLatLng);
            }
            catch (Exception ex)
            {
                SysDiag.Debug.WriteLine($"AndroidLocationManagerBridge poll: {ex.Message}");
            }

            if (_pollHandler is not null && _pollRunnable is not null)
                _pollHandler.PostDelayed(_pollRunnable, 1600);
        });
        _pollHandler.Post(_pollRunnable);
    }

    static void StopLastKnownPollOnly()
    {
        if (_pollHandler is not null && _pollRunnable is not null)
        {
            try
            {
                _pollHandler.RemoveCallbacks(_pollRunnable);
            }
            catch
            {
                // bỏ qua
            }
        }

        _pollHandler = null;
        _pollRunnable = null;
    }

    public static void Stop()
    {
        StopLastKnownPollOnly();

        try
        {
            if (_locationManager is not null && _listener is not null)
                _locationManager.RemoveUpdates(_listener);
        }
        catch (Exception ex)
        {
            SysDiag.Debug.WriteLine($"AndroidLocationManagerBridge.Stop: {ex}");
        }

        _listener = null;
        _locationManager = null;
    }

    private sealed class NativeLocationListener : Java.Lang.Object, ILocationListener
    {
        private readonly Action<double, double> _onLocation;

        public NativeLocationListener(Action<double, double> onLocation) => _onLocation = onLocation;

        public void OnLocationChanged(Android.Locations.Location location)
        {
            if (location is null)
                return;
            _onLocation(location.Latitude, location.Longitude);
        }

        public void OnProviderDisabled(string provider) { }

        public void OnProviderEnabled(string provider) { }

        public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }
    }
}
#endif
