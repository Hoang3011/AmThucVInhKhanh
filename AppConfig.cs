namespace TourGuideApp2;

/// <summary>
/// Cấu hình tích hợp server đi kèm bản phát hành: điền URL REST (Supabase/PostgREST) và khóa anon trước khi build.
/// Để trống thì app chỉ dùng dữ liệu cục bộ (<c>VinhKhanh.db</c>).
/// </summary>
public static class AppConfig
{
    /// <summary>Mặc định khi người dùng chưa nhập URL trong Cài đặt — nhiều thiết bị nên dùng chung URL máy chạy CMS (LAN).</summary>
    public const string DefaultPoiApiUrl = "http://192.168.1.101:5095/api/places";
    public const string DefaultPoiApiKey = "";

    /// <summary>
    /// Gốc CMS mà **4G** gọi được (ngrok, Cloudflare Tunnel, domain public…). Để trống = app chỉ đồng bộ lượt phát/entitlement qua LAN như <see cref="DefaultPoiApiUrl"/>.
    /// Ví dụ sau khi chạy tunnel trỏ vào cổng CMS: <c>https://abc123.ngrok-free.app</c> (không có slash cuối).
    /// </summary>
    public const string DefaultPublicCmsBaseUrl = "";

    /// <summary>Khóa trùng <c>App:MobileApiKey</c> trên CMS (để trống = không kiểm tra).</summary>
    public const string MobileApiKey = "";

    /// <summary>Gốc URL CMS (cùng host với API POI). Trả rỗng nếu <see cref="DefaultPoiApiUrl"/> không hợp lệ — khi đó đăng nhập chỉ cục bộ.</summary>
    public static string GetCmsOrigin()
    {
        if (string.IsNullOrWhiteSpace(DefaultPoiApiUrl))
            return string.Empty;
        if (!Uri.TryCreate(DefaultPoiApiUrl.Trim(), UriKind.Absolute, out var u))
            return string.Empty;
        return $"{u.Scheme}://{u.Authority}";
    }
}
