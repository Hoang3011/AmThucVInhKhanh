using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly PlaceRepository _db;
    private readonly IConfiguration _config;

    public IndexModel(PlaceRepository db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public int PlaceCount { get; private set; }
    public string DatabasePath { get; private set; } = "";

    /// <summary>URL ảnh QR (đầy đủ + cache-bust) — khớp nội dung mã với <c>/install/launch</c>.</summary>
    public string QrAppImageSrc { get; private set; } = "";

    public async Task OnGetAsync()
    {
        var list = await _db.ListAsync();
        PlaceCount = list.Count;
        DatabasePath = _db.DatabasePath;
        QrAppImageSrc = PublicSiteUrls.QrAppImageSrc(HttpContext, _config);
    }
}
