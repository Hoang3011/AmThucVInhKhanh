using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
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
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
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
            // Khi activity không còn hiển thị (về Home / vuốt tắt app). Bỏ qua xoay màn hình.
            try
            {
                if (!IsChangingConfigurations)
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
                if (!string.IsNullOrWhiteSpace(rawBase))
                    PlaceApiService.TryApplyRemoteDemoBaseUrl(rawBase, out _);
            }
            catch
            {
                // Không để deeplink setup làm văng app.
            }
        }
    }
}
