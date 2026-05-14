using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace TourGuideApp2.Services;

/// <summary>Hiển thị kết quả giả lập kiểm tra cấu hình: thông báo (Android) + toast (các nền tảng khác).</summary>
public static class DeviceConfigUiPresenter
{
    private static bool _presented;

    public static async Task PresentOnceAsync(Label? tierLabel)
    {
        if (_presented)
            return;
        _presented = true;

        var tier = App.GetOrAssignSimulatedDeviceConfigTier();
        var line = $"Kiểm tra cấu hình (demo): {tier} — {(tier == 0 ? "cấu hình mạnh" : "cấu hình yếu")}.";

        if (tierLabel is not null)
            tierLabel.Text = line;

#if ANDROID
        DeviceTierNotificationHelper.TryNotifyInstallConfigCheck(tier);
#else
        try
        {
            await Toast.Make(line, ToastDuration.Long).Show();
        }
        catch
        {
            // bỏ qua
        }
#endif
    }
}
