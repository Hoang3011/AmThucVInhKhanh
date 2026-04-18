namespace TourGuideCMS;

/// <summary>Chọn file APK phục vụ QR / trang Install — cùng logic ở mọi nơi.</summary>
public static class ApkLocator
{
    public static string? FindPreferredApkPath(IWebHostEnvironment env)
    {
        var www = env.WebRootPath ?? string.Empty;
        var canonicalDownloads = string.IsNullOrWhiteSpace(www)
            ? null
            : Path.Combine(www, "downloads", "AmThucVinhKhanh.apk");

        if (!string.IsNullOrWhiteSpace(canonicalDownloads) && File.Exists(canonicalDownloads))
            return canonicalDownloads;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(www))
        {
            var downloadsDir = Path.Combine(www, "downloads");
            if (Directory.Exists(downloadsDir))
                candidates.AddRange(Directory.GetFiles(downloadsDir, "*.apk", SearchOption.TopDirectoryOnly));
        }

        var root = env.ContentRootPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(root))
        {
            var releaseDir = Path.GetFullPath(Path.Combine(root, "..", "bin", "Release", "net10.0-android"));
            if (Directory.Exists(releaseDir))
                candidates.AddRange(Directory.GetFiles(releaseDir, "*.apk", SearchOption.AllDirectories));
        }

        var existing = candidates
            .Where(File.Exists)
            .Select(p => new FileInfo(p))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .ThenByDescending(fi => fi.Length)
            .Select(fi => fi.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return existing.FirstOrDefault();
    }

    /// <summary>Đổi mỗi khi file APK thay đổi (dung lượng hoặc thời gian sửa) — tránh trình duyệt/CDN giữ bản cũ.</summary>
    public static string CacheBusterForPath(string apkPath)
    {
        var fi = new FileInfo(apkPath);
        return $"{fi.Length}-{fi.LastWriteTimeUtc.Ticks}";
    }
}
