using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Owner;

[Authorize(Roles = "Owner")]
public class IndexModel : PageModel
{
    private readonly CustomerAccountRepository _plays;
    private readonly PlaceRepository _places;

    public IndexModel(CustomerAccountRepository plays, PlaceRepository places)
    {
        _plays = plays;
        _places = places;
    }

    public string PlaceName { get; private set; } = "";
    public int PlaceId { get; private set; }
    public IReadOnlyList<PlayAggregateRow> Aggregates { get; private set; } = Array.Empty<PlayAggregateRow>();
    public IReadOnlyList<NarrationPlayRow> Recent { get; private set; } = Array.Empty<NarrationPlayRow>();
    public double PremiumTotalVnd { get; private set; }
    public int PremiumPaymentCount { get; private set; }

    public async Task OnGetAsync()
    {
        var placeIdRaw = User.FindFirstValue("PlaceId");
        if (!int.TryParse(placeIdRaw, out var placeId) || placeId <= 0)
            return;

        var place = await _places.GetAsync(placeId);
        if (place is null)
            return;

        PlaceId = placeId;
        PlaceName = place.Name;
        Aggregates = await _plays.GetAggregatesForPlaceAsync(place.Name);
        Recent = await _plays.ListRecentPlaysForPlaceAsync(place.Name, 200);
        (PremiumTotalVnd, PremiumPaymentCount) = await _plays.GetPremiumRevenueForPlaceAsync(placeId);
    }
}
