using Microsoft.Maui.Storage;

namespace TourGuideApp2.Services;

/// <summary>Id cài đặt ổn định trên máy — dùng cho trả phí demo / mở thuyết minh nâng cao (không thay thế đăng nhập khách).</summary>
public static class DeviceInstallIdService
{
    private const string PreferenceKey = "device_install_guid_v1";

    public static string GetOrCreate()
    {
        var s = (Preferences.Default.Get(PreferenceKey, string.Empty) ?? string.Empty).Trim();
        if (s.Length < 8)
        {
            s = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(PreferenceKey, s);
        }

        return s;
    }
}
