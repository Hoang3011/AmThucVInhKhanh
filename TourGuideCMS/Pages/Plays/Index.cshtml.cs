using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Plays;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly CustomerAccountRepository _repo;

    public IndexModel(CustomerAccountRepository repo) => _repo = repo;

    [BindProperty(SupportsGet = true)]
    public string? Place { get; set; }

    public IReadOnlyList<string> PlaceOptions { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<PlayAggregateRow> Aggregates { get; private set; } = Array.Empty<PlayAggregateRow>();
    public IReadOnlyList<NarrationPlayRow> Recent { get; private set; } = Array.Empty<NarrationPlayRow>();

    public async Task OnGetAsync()
    {
        var allAggregates = await _repo.GetAggregatesByPlaceAsync();
        var allRecent = await _repo.ListRecentPlaysAsync(200);

        PlaceOptions = allAggregates
            .Select(x => x.PlaceName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(Place))
        {
            Aggregates = allAggregates;
            Recent = allRecent;
            return;
        }

        var selected = Place.Trim();
        Aggregates = allAggregates
            .Where(x => string.Equals(x.PlaceName, selected, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Recent = allRecent
            .Where(x => string.Equals(x.PlaceName, selected, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
