using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Customers;

[Authorize(Roles = "Admin")]
public class RouteModel : PageModel
{
    private readonly CustomerAccountRepository _repo;

    public RouteModel(CustomerAccountRepository repo) => _repo = repo;

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public CustomerUserRow? Customer { get; private set; }

    public string RoutePointsJson { get; private set; } = "[]";

    public int PointCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id <= 0)
            return RedirectToPage("Index");

        Customer = await _repo.GetUserByIdAsync(Id);
        if (Customer is null)
            return NotFound();

        var raw = await _repo.GetRouteSnapshotJsonAsync(Id);
        RoutePointsJson = string.IsNullOrWhiteSpace(raw) ? "[]" : raw;
        try
        {
            using var doc = JsonDocument.Parse(RoutePointsJson);
            PointCount = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch
        {
            PointCount = 0;
            RoutePointsJson = "[]";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteRouteAsync()
    {
        if (Id <= 0)
            return RedirectToPage("Index");

        await _repo.DeleteRouteSnapshotAsync(Id);
        return RedirectToPage(new { Id });
    }
}
