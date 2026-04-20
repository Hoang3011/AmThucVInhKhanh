using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using TourGuideApp2.Services;

namespace TourGuideApp2
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = "amthucvinhkhanh",
        DataHost = "setup")]
    public class MainActivity : MauiAppCompatActivity
    {
        private DateTime _activityCreatedUtc;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _activityCreatedUtc = DateTime.UtcNow;
            TryApplySetupIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            if (intent is not null)
                TryApplySetupIntent(intent);
        }

        protected override void OnStop()
        {
            // Một số máy bắn OnStop sớm sau khi mở app — gọi heartbeat lúc đó dễ race với Shell/WebView và góp phần văng process.
            try
            {
                if (!IsChangingConfigurations
                    && (DateTime.UtcNow - _activityCreatedUtc).TotalSeconds >= 2.0)
                    _ = DeviceHeartbeatService.NotifyMapTabLeftAsync();
            }
            catch
            {
                // bỏ qua
            }

            base.OnStop();
        }

        private static void TryApplySetupIntent(Intent? intent)
        {
            try
            {
                var data = intent?.Data;
                if (data is null)
                    return;

                var scheme = data.Scheme ?? string.Empty;
                var host = data.Host ?? string.Empty;
                if (!scheme.Equals("amthucvinhkhanh", StringComparison.OrdinalIgnoreCase)
                    || !host.Equals("setup", StringComparison.OrdinalIgnoreCase))
                    return;

                var rawBase = data.GetQueryParameter("base");
                if (string.IsNullOrWhiteSpace(rawBase))
                    return;

                if (!PlaceApiService.TryApplyRemoteDemoBaseUrl(rawBase, out _))
                    return;

                // Cold start sau cài APK / quét QR: Shell đôi khi chưa sẵn sau 1 lần delay — thử lại vài lần để tránh văng app.
                MainThread.BeginInvokeOnMainThread(() => _ = NavigateSetupDeeplinkToExploreAsync());
            }
            catch
            {
                // Không để deeplink setup làm văng app.
            }
        }

        private static async Task NavigateSetupDeeplinkToExploreAsync()
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    await Task.Delay(attempt == 0 ? 850 : 380);
                    if (Shell.Current is null)
                        continue;
                    await Shell.Current.GoToAsync("//explore", animate: false);
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Setup deeplink → //explore attempt {attempt}: {ex.Message}");
                }
            }
        }
    }
}
