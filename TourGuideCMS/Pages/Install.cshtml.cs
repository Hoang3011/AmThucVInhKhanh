using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS;
using System.IO;
using System.Linq;

namespace TourGuideCMS.Pages;

[AllowAnonymous]
public class InstallModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public InstallModel(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    public string QrPayloadUrl { get; private set; } = "";
    public string ApiPlacesUrl { get; private set; } = "";
    public string? AppDownloadDirectUrl { get; private set; }
    public string? EffectiveDownloadUrl { get; private set; }
    public string AppSetupDeepLink { get; private set; } = "";
    public bool LocalApkAvailable { get; private set; }
    public bool FromQr { get; private set; }
    public bool Uploaded { get; set; }
    public bool UploadError { get; set; }

    /// <summary>URL ảnh QR đầy đủ + cache-bust (khớp host/port với máy đang mở CMS).</summary>
    public string QrAppImageSrc { get; private set; } = "";

    public void OnGet()
    {
        FromQr = string.Equals(Request.Query["fromQr"], "1", StringComparison.Ordinal);
        Uploaded = string.Equals(Request.Query["uploaded"], "1", StringComparison.Ordinal);
        UploadError = string.Equals(Request.Query["uploadError"], "1", StringComparison.Ordinal);

        AppDownloadDirectUrl = string.IsNullOrWhiteSpace(_config["App:AppDownloadUrl"])
            ? null
            : _config["App:AppDownloadUrl"]!.Trim();
        LocalApkAvailable = FindAnyLocalApk(_env);
        if (!string.IsNullOrWhiteSpace(AppDownloadDirectUrl))
            EffectiveDownloadUrl = AppDownloadDirectUrl;
        else if (LocalApkAvailable)
        {
            var apkPath = ApkLocator.FindPreferredApkPath(_env);
            if (!string.IsNullOrWhiteSpace(apkPath) && System.IO.File.Exists(apkPath))
            {
                var site = PublicSiteUrls.SiteRootForLinks(HttpContext, _config);
                var v = ApkLocator.CacheBusterForPath(apkPath);
                EffectiveDownloadUrl = $"{site}/downloads/AmThucVinhKhanh.apk?v={v}";
            }
            else
                EffectiveDownloadUrl = "/downloads/AmThucVinhKhanh.apk";
        }
        else
            EffectiveDownloadUrl = null;
        QrPayloadUrl = PublicSiteUrls.AppDownloadQrContent(HttpContext, _config);
        var siteRoot = PublicSiteUrls.SiteRootForLinks(HttpContext, _config);
        ApiPlacesUrl = $"{siteRoot}/api/places";
        AppSetupDeepLink = $"amthucvinhkhanh://setup?base={Uri.EscapeDataString(siteRoot)}";
        QrAppImageSrc = PublicSiteUrls.QrAppImageSrc(HttpContext, _config);
    }

    private static bool FindAnyLocalApk(IWebHostEnvironment? env)
    {
        if (env is null) return false;

        var webRoot = env.WebRootPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(webRoot))
        {
            var localDir = Path.Combine(webRoot, "downloads");
            if (Directory.Exists(localDir) &&
                Directory.GetFiles(localDir, "*.apk", SearchOption.TopDirectoryOnly).Length > 0)
                return true;
        }

        var root = env.ContentRootPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root))
            return false;

        var releaseDir = Path.GetFullPath(Path.Combine(root, "..", "bin", "Release", "net10.0-android"));
        return Directory.Exists(releaseDir) &&
               Directory.GetFiles(releaseDir, "*.apk", SearchOption.AllDirectories).Any();
    }
}
