using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using TourGuideApp2.Services;

namespace TourGuideApp2
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Một lần sau khi cài bản không bắt đăng nhập: xóa session cũ để không kẹt trạng thái từ APK trước.
            const string guestShellMigration = "TourGuestNoLoginShell_v1";
            if (!Preferences.Default.Get(guestShellMigration, false))
            {
                try
                {
                    AuthService.Logout();
                }
                catch
                {
                    // bỏ qua
                }

                Preferences.Default.Set(guestShellMigration, true);
            }

            Connectivity.ConnectivityChanged += (_, _) =>
            {
                try
                {
                    if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                        _ = PlaySyncService.FlushPendingAsync();
                }
                catch
                {
                    // Không để sự kiện mạng làm văng app.
                }
            };
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        protected override void OnResume()
        {
            base.OnResume();
            PremiumPaymentService.ClearShortLivedEntitlementMemory();
            try
            {
                _ = PlaySyncService.FlushPendingAsync();
            }
            catch
            {
                // Bỏ qua — không chặn resume.
            }

            // Vào lại app đang đứng tab Bản đồ thì bật lại ping (OnAppearing đôi khi không chạy lại sau khi về foreground).
            try
            {
                if (Shell.Current?.CurrentPage is MapPage)
                    DeviceHeartbeatService.StartMapTabSession();
            }
            catch
            {
                // bỏ qua
            }
        }

        protected override void OnSleep()
        {
            // Ra khỏi app (Home / đa nhiệm / vuốt tắt) — báo offline ngay để CMS F5 đúng trạng thái.
            try
            {
                _ = DeviceHeartbeatService.NotifyMapTabLeftAsync();
            }
            catch
            {
                // bỏ qua
            }

            // GPS không dừng ở đây: MAUI vẫn nhận cập nhật vị trí qua foreground listener (Android: thông báo hệ thống).
            base.OnSleep();
        }
    }
}