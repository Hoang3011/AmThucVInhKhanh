using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using TourGuideApp2.Services;

namespace TourGuideApp2
{
    public partial class App : Application
    {
        private static readonly DateTime StartedUtc = DateTime.UtcNow;

        public App()
        {
            InitializeComponent();

            try
            {
                // App khách: không giữ phiên đăng nhập — mỗi lần mở app là trạng thái khách (tránh màn/form đăng nhập cũ).
                try
                {
                    AuthService.Logout();
                }
                catch
                {
                    // bỏ qua
                }

                Connectivity.ConnectivityChanged += (_, _) =>
                {
                    try
                    {
                        if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                            CustomerAppWarmSyncService.Schedule();
                    }
                    catch
                    {
                        // Không để sự kiện mạng làm văng app.
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App ctor: {ex}");
            }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        protected override void OnStart()
        {
            base.OnStart();
            try
            {
                CustomerAppWarmSyncService.Schedule();
            }
            catch
            {
                // bỏ qua
            }
        }

        protected override void OnResume()
        {
            base.OnResume();
            PremiumPaymentService.ClearShortLivedEntitlementMemory();
            try
            {
                CustomerAppWarmSyncService.Schedule();
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
            // Vài thiết bị gọi OnSleep cực sớm khi vừa mở app — gọi HTTP lúc đó dễ race → văng process.
            try
            {
                if ((DateTime.UtcNow - StartedUtc).TotalSeconds >= 3.0)
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