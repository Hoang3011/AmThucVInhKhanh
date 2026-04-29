using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Models;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Places;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly PlaceRepository _db;
    private readonly CustomerAccountRepository _accounts;

    public IndexModel(PlaceRepository db, CustomerAccountRepository accounts)
    {
        _db = db;
        _accounts = accounts;
    }

    public IReadOnlyList<PlaceRow> Places { get; private set; } = [];
    public Dictionary<int, int> VisitCountByPlaceId { get; private set; } = [];
    public int HighestVisitCount { get; private set; }

    public async Task OnGetAsync()
    {
        Places = await _db.ListAsync();
        var agg = await _accounts.GetAggregatesByPlaceAsync();
        var byName = agg
            .GroupBy(x => x.PlaceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count), StringComparer.OrdinalIgnoreCase);

        VisitCountByPlaceId = Places.ToDictionary(
            p => p.Id,
            p => byName.TryGetValue(p.Name, out var total) ? total : 0);

        HighestVisitCount = VisitCountByPlaceId.Count == 0 ? 0 : VisitCountByPlaceId.Values.Max();
    }
}
