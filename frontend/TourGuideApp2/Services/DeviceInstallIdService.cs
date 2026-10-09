using Microsoft.Maui.Storage;

namespace TourGuideApp2.Services;

/// <summary>Id cài đặt ổn định trên máy — dùng cho trả phí demo / mở thuyết minh nâng cao (không thay thế đăng nhập khách).</summary>
public static class DeviceInstallIdService
{
    private const string PreferenceKey = "device_install_guid_v1";
    private const string FileName = "device_install_guid_v1.txt";

    /// <summary>File phụ trong AppData — một số OEM xóa Preferences nhưng còn file (giảm trùng thiết bị trên CMS).</summary>
    private static string? _filePath;

    private static string FilePath => _filePath ??= Path.Combine(FileSystem.AppDataDirectory, FileName);

    public static string GetOrCreate()
    {
        var s = (Preferences.Default.Get(PreferenceKey, string.Empty) ?? string.Empty).Trim();
        if (s.Length >= 8)
        {
            TryMirrorToFile(s);
            return s;
        }

        try
        {
            if (File.Exists(FilePath))
            {
                var fromFile = (File.ReadAllText(FilePath) ?? string.Empty).Trim();
                if (fromFile.Length >= 8)
                {
                    Preferences.Default.Set(PreferenceKey, fromFile);
                    return fromFile;
                }
            }
        }
        catch
        {
            // bỏ qua — tạo id mới
        }

        s = Guid.NewGuid().ToString("N");
        Preferences.Default.Set(PreferenceKey, s);
        TryMirrorToFile(s);
        return s;
    }

    private static void TryMirrorToFile(string id)
    {
        try
        {
            File.WriteAllText(FilePath, id);
        }
        catch
        {
            // không chặn app
        }
    }
}
