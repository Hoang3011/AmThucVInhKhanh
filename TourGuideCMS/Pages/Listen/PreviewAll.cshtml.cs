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

    public async Task OnGetAsync()
    {
        Places = await _places.ListAsync();
        QrAppImageSrc = PublicSiteUrls.QrAppImageSrc(HttpContext, _config);
        AppInstallUrl = PublicSiteUrls.QrAppInstallLaunchUrl(HttpContext, _config);
    }
}
