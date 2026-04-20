using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS;
using System.IO;

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

    public IActionResult OnGet()
    {
        FromQr = string.Equals(Request.Query["fromQr"], "1", StringComparison.Ordinal);
        Uploaded = string.Equals(Request.Query["uploaded"], "1", StringComparison.Ordinal);
        UploadError = string.Equals(Request.Query["uploadError"], "1", StringComparison.Ordinal);

        AppDownloadDirectUrl = string.IsNullOrWhiteSpace(_config["App:AppDownloadUrl"])
            ? null
            : _config["App:AppDownloadUrl"]!.Trim();
        LocalApkAvailable = !string.IsNullOrEmpty(ApkLocator.FindPreferredApkPath(_env, _config));
        EffectiveDownloadUrl = InstallTargetUrlResolver.Resolve(HttpContext, _config, _env);

        // ?fromQr=1: cùng trang tải tối giản như QR (không dừng ở /Install đầy chữ).
        if (FromQr && !string.IsNullOrEmpty(EffectiveDownloadUrl))
            return Redirect("/install/launch");

        QrPayloadUrl = PublicSiteUrls.AppDownloadQrContent(HttpContext, _config, _env);
        var siteRoot = PublicSiteUrls.SiteRootForLinks(HttpContext, _config);
        ApiPlacesUrl = $"{siteRoot}/api/places";
        AppSetupDeepLink = $"amthucvinhkhanh://setup?base={Uri.EscapeDataString(siteRoot)}";
        QrAppImageSrc = PublicSiteUrls.QrAppImageSrc(HttpContext, _config);
        return Page();
    }

}
