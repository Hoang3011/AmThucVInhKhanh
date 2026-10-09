namespace TourGuideCMS.Services;

/// <summary>
/// Vị trí file SQLite (<c>VinhKhanh.db</c>) nằm ở thư mục <c>backend/</c> của repo.
/// Có thể ghi đè bằng cấu hình <c>Database:Directory</c>; không thấy thư mục backend (vd. khi deploy riêng) thì dùng <c>App_Data</c> cạnh CMS.
/// </summary>
public static class DbPaths
{
    public static string ResolveDirectory(IWebHostEnvironment env, IConfiguration? config = null)
    {
        var configured = config?["Database:Directory"];
        string dir;
        if (!string.IsNullOrWhiteSpace(configured))
            dir = Path.GetFullPath(configured, env.ContentRootPath);
        else
        {
            var backend = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "backend"));
            dir = Directory.Exists(backend)
                ? Path.Combine(backend, "App_Data")
                : Path.Combine(env.ContentRootPath, "App_Data");
        }
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string SeedDatabasePath(IWebHostEnvironment env) =>
        Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "backend", "VinhKhanh.db"));
}
