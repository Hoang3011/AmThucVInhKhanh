using Microsoft.Extensions.DependencyInjection;

namespace TourGuideApp2
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        protected override void OnSleep()
        {
            // GPS không dừng ở đây: MAUI vẫn nhận cập nhật vị trí qua foreground listener (Android: thông báo hệ thống).
            base.OnSleep();
        }
    }
}