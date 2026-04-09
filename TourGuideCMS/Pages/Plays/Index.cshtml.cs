using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Plays;

[Authorize]
public class IndexModel : PageModel
{
    private readonly CustomerAccountRepository _repo;

    public IndexModel(CustomerAccountRepository repo) => _repo = repo;

    public IReadOnlyList<PlayAggregateRow> Aggregates { get; private set; } = Array.Empty<PlayAggregateRow>();
    public IReadOnlyList<NarrationPlayRow> Recent { get; private set; } = Array.Empty<NarrationPlayRow>();

    public async Task OnGetAsync()
    {
        Aggregates = await _repo.GetAggregatesByPlaceAsync();
        Recent = await _repo.ListRecentPlaysAsync(200);
    }
}
