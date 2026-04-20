using Microsoft.Extensions.Configuration;

namespace TourGuideCMS;

/// <summary>
/// Chọn file APK phục vụ QR / trang Install — cùng logic ở mọi nơi.
/// Bản Release MAUI đầy đủ thường ~100–150MB; bản ~40MB thường là debug/split trong <c>bin</c> — không dùng cho QR.
/// </summary>
public static class ApkLocator
{
    /// <param name="config">
    /// <c>App:QrApkPreferUploadedCanonical</c> — ưu tiên <c>wwwroot/downloads/AmThucVinhKhanh.apk</c>.
    /// <c>App:QrApkExcludeSolutionBin</c> (mặc định true) — không quét <c>…/bin</c>, tránh nhầm APK nhỏ mới build trên máy chủ.
    /// <c>App:QrApkMinimumBytes</c> — bỏ qua file nhỏ hơn (vd. 100000000 = 100MB).
    /// </param>
    public static string? FindPreferredApkPath(IWebHostEnvironment env, IConfiguration? config = null)
    {
        var minBytes = config?.GetValue<long?>("App:QrApkMinimumBytes") ?? 0;
        if (minBytes < 0)
            minBytes = 0;

        var www = env.WebRootPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(www))
        {
            var canonicalDownloads = Path.Combine(www, "downloads", "AmThucVinhKhanh.apk");
            if (config?.GetValue("App:QrApkPreferUploadedCanonical", false) == true
                && File.Exists(canonicalDownloads))
            {
                var fiCanon = new FileInfo(canonicalDownloads);
                if (minBytes == 0 || fiCanon.Length >= minBytes)
                    return canonicalDownloads;
                // File upload quá nhỏ (nhầm bản debug) — không trả về, để admin thấy thiếu APK hợp lệ.
            }
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(www))
        {
            var canonicalDownloads = Path.Combine(www, "downloads", "AmThucVinhKhanh.apk");
            if (File.Exists(canonicalDownloads))
                candidates.Add(canonicalDownloads);

            var downloadsDir = Path.Combine(www, "downloads");
            if (Directory.Exists(downloadsDir))
            {
                foreach (var f in Directory.GetFiles(downloadsDir, "*.apk", SearchOption.TopDirectoryOnly))
                    candidates.Add(f);
            }
        }

        var excludeBin = config?.GetValue("App:QrApkExcludeSolutionBin", true) != false;
        if (!excludeBin)
        {
            var root = env.ContentRootPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(root))
            {
                var solutionBin = Path.GetFullPath(Path.Combine(root, "..", "bin"));
                if (Directory.Exists(solutionBin))
                {
                    try
                    {
                        foreach (var f in Directory.GetFiles(solutionBin, "*.apk", SearchOption.AllDirectories))
                            candidates.Add(f);
                    }
                    catch
                    {
                        // Bỏ qua lỗi quyền / đường dẫn.
                    }
                }
            }
        }

        var existing = candidates
            .Where(File.Exists)
            .Select(p => new FileInfo(p))
            .Where(fi => minBytes == 0 || fi.Length >= minBytes)
            // Ưu tiên bản lớn (Release đầy đủ), sau đó mới nhất.
            .OrderByDescending(fi => fi.Length)
            .ThenByDescending(fi => fi.LastWriteTimeUtc)
            .ToList();

        return existing.FirstOrDefault()?.FullName;
    }

    /// <summary>Đổi mỗi khi file APK thay đổi (dung lượng hoặc thời gian sửa) — tránh trình duyệt/CDN giữ bản cũ.</summary>
    public static string CacheBusterForPath(string apkPath)
    {
        var fi = new FileInfo(apkPath);
        return $"{fi.Length}-{fi.LastWriteTimeUtc.Ticks}";
    }
}
