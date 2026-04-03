namespace TourGuideApp2;

/// <summary>
/// Cấu hình tích hợp server đi kèm bản phát hành: điền URL REST (Supabase/PostgREST) và khóa anon trước khi build.
/// Để trống thì app chỉ dùng dữ liệu cục bộ (<c>VinhKhanh.db</c>).
/// </summary>
public static class AppConfig
{
    public const string DefaultPoiApiUrl = "";
    public const string DefaultPoiApiKey = "";
}
