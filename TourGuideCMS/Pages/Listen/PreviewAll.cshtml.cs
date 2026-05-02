using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS;
using TourGuideCMS.Models;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Listen;

[AllowAnonymous]
public class PreviewAllModel : PageModel
{
    private readonly PlaceRepository _places;
    private readonly IConfiguration _config;

    public PreviewAllModel(PlaceRepository places, IConfiguration config)
    {
        _places = places;
        _config = config;
    }

    public IReadOnlyList<PlaceRow> Places { get; private set; } = [];
    public string QrAppImageSrc { get; private set; } = "";
    public string AppInstallUrl { get; private set; } = "";
    /// <summary>JSON mảng POI (nội dung thuyết minh) để trình duyệt phát khi không còn kết nối tới máy chủ.</summary>
    public string PlacesJson { get; private set; } = "[]";

    public async Task OnGetAsync()
    {
        Places = await _places.ListAsync();
        QrAppImageSrc = PublicSiteUrls.QrAppImageSrc(HttpContext, _config);
        AppInstallUrl = PublicSiteUrls.QrAppInstallLaunchUrl(HttpContext, _config);
        PlacesJson = JsonSerializer.Serialize(
            Places.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                address = p.Address,
                desc = p.Description,
                vi = p.VietnameseAudioText,
                en = p.EnglishAudioText,
                zh = p.ChineseAudioText,
                ja = p.JapaneseAudioText
            }));
    }
}
