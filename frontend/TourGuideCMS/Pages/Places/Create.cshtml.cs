using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Models;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Places;

[Authorize(Roles = "Admin")]
public class CreateModel : PageModel
{
    private readonly PlaceRepository _db;
    private readonly CmsIdentityRepository _identity;

    public CreateModel(PlaceRepository db, CmsIdentityRepository identity)
    {
        _db = db;
        _identity = identity;
    }

    [BindProperty]
    public PlaceFormViewModel Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var row = new PlaceRow
        {
            Name = Input.Name,
            Address = Input.Address,
            Specialty = Input.Specialty,
            ImageUrl = Input.ImageUrl,
            Latitude = Input.Latitude,
            Longitude = Input.Longitude,
            ActivationRadiusMeters = Input.ActivationRadiusMeters,
            Priority = Input.Priority,
            Description = Input.Description,
            VietnameseAudioText = Input.VietnameseAudioText,
            EnglishAudioText = Input.EnglishAudioText,
            ChineseAudioText = Input.ChineseAudioText,
            JapaneseAudioText = Input.JapaneseAudioText,
            MapUrl = Input.MapUrl,
            PremiumPriceDemo = Input.PremiumPriceDemo,
            PremiumVietnameseAudioText = ""
        };

        await _db.InsertAsync(row);
        await _identity.SyncOwnersForPlacesAsync(await _db.ListAsync());
        return RedirectToPage("Index");
    }
}
