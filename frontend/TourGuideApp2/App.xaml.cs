using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using TourGuideApp2.Services;

namespace TourGuideApp2
{
    public partial class App : Application
    {
        private static readonly DateTime StartedUtc = DateTime.UtcNow;

        /// <summary>Kết quả giả lập kiểm tra cấu hình (0 mạnh / 1 yếu), gán sau lần chạy đầu tiên trong phiên.</summary>
        public static int? SimulatedDeviceConfigTier { get; private set; }

        /// <summary>Dùng khi <see cref="OnStart"/> chưa kịp gán (edge case) — đảm bảo một giá trị cho UI/thông báo.</summary>
        public static int GetOrAssignSimulatedDeviceConfigTier()
        {
            if (SimulatedDeviceConfigTier is int t)
                return t;
            SimulatedDeviceConfigTier = DeviceConfigSimulator.SimulateRandomDeviceTier();
            return SimulatedDeviceConfigTier.Value;
        }

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
                SimulatedDeviceConfigTier = DeviceConfigSimulator.SimulateRandomDeviceTier();
                System.Diagnostics.Debug.WriteLine(
                    $"[DeviceConfigSimulator] tier={SimulatedDeviceConfigTier} — {DeviceConfigSimulator.DescribeTierVi(SimulatedDeviceConfigTier.Value)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DeviceConfigSimulator: {ex}");
            }

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