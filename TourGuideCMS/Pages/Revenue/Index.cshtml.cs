using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Revenue;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly CustomerAccountRepository _payments;
    private readonly PlaceRepository _places;

    public IndexModel(CustomerAccountRepository payments, PlaceRepository places)
    {
        _payments = payments;
        _places = places;
    }

    public double GrandTotalVnd { get; private set; }
    public int TotalPaymentRows { get; private set; }
    public IReadOnlyList<PremiumRevenueLine> Lines { get; private set; } = Array.Empty<PremiumRevenueLine>();

    public async Task OnGetAsync()
    {
        (GrandTotalVnd, TotalPaymentRows) = await _payments.GetPremiumGrandTotalsAsync();
        var byPlace = await _payments.GetPremiumRevenueByPlaceAsync();
        var placeList = await _places.ListAsync();
        var nameById = placeList.ToDictionary(p => p.Id, p => p.Name);

        Lines = byPlace
            .Select(r => new PremiumRevenueLine(
                r.PlaceId,
                nameById.TryGetValue(r.PlaceId, out var n) ? n : $"POI #{r.PlaceId}",
                r.TotalVnd,
                r.PaymentCount))
            .ToList();
    }
}

public sealed record PremiumRevenueLine(int PlaceId, string PlaceName, double TotalVnd, int PaymentCount);
