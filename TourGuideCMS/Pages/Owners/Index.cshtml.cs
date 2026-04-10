using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Models;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Owners;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly CmsIdentityRepository _identity;
    private readonly PlaceRepository _places;

    public IndexModel(CmsIdentityRepository identity, PlaceRepository places)
    {
        _identity = identity;
        _places = places;
    }

    public IReadOnlyList<OwnerAccountViewRow> Rows { get; private set; } = Array.Empty<OwnerAccountViewRow>();
    public string? Toast { get; private set; }

    public async Task OnGetAsync(string? toast = null)
    {
        Toast = toast;
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostToggleAsync(int placeId)
    {
        await _identity.ToggleOwnerActiveAsync(placeId);
        return RedirectToPage(new { toast = "Đã cập nhật trạng thái tài khoản chủ quán." });
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(int placeId)
    {
        var pwd = await _identity.ResetOwnerPasswordAsync(placeId);
        if (pwd is null)
            return RedirectToPage(new { toast = "Không tìm thấy tài khoản chủ quán để reset." });
        return RedirectToPage(new { toast = $"Đã reset mật khẩu mặc định: {pwd}" });
    }

    private async Task LoadAsync()
    {
        var places = await _places.ListAsync();
        await _identity.SyncOwnersForPlacesAsync(places);
        var owners = await _identity.ListOwnersAsync();

        var byPlace = owners.ToDictionary(x => x.PlaceId, x => x);
        Rows = places
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p =>
            {
                byPlace.TryGetValue(p.Id, out var o);
                return new OwnerAccountViewRow(
                    p.Id,
                    p.Name,
                    o?.Username ?? $"owner{p.Id}",
                    o?.PasswordPlain ?? "(chưa có)",
                    o?.IsActive ?? false,
                    o?.UpdatedAtUtc);
            })
            .ToList();
    }
}

public sealed record OwnerAccountViewRow(
    int PlaceId,
    string PlaceName,
    string Username,
    string Password,
    bool IsActive,
    DateTime? UpdatedAtUtc);
