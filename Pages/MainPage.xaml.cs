using Microsoft.Maui.ApplicationModel;
using TourGuideApp2.Services;

namespace TourGuideApp2;

public partial class MainPage : ContentPage
{
    private static bool _hasPlayedAppWelcome;

    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _ = PlayAppWelcomeOnceAsync();
            CustomerAppWarmSyncService.Schedule();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainPage.OnAppearing: {ex}");
        }
    }

    private static async Task PlayAppWelcomeOnceAsync()
    {
        if (_hasPlayedAppWelcome)
            return;

        _hasPlayedAppWelcome = true;

        if (OperatingSystem.IsAndroid())
            return;

        try
        {
            await Task.Delay(350);
            _ = await NarrationQueueService.EnqueuePoiOrTtsAsync(
                -1,
                "vi",
                "Xin chào, chào mừng bạn đến với phố ẩm thực Vĩnh Khánh.");
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PlayAppWelcomeOnceAsync: {ex.Message}");
        }
    }
}
