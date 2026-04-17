using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Networking;
using TourGuideApp2.Services;

namespace TourGuideApp2
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
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
        }

        protected override void OnSleep()
        {
            // GPS không dừng ở đây: MAUI vẫn nhận cập nhật vị trí qua foreground listener (Android: thông báo hệ thống).
            base.OnSleep();
        }
    }
}