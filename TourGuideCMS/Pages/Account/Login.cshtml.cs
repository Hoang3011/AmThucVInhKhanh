using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly CmsIdentityRepository _identity;
    private readonly PlaceRepository _places;

    public LoginModel(CmsIdentityRepository identity, PlaceRepository places)
    {
        _identity = identity;
        _places = places;
    }

    [BindProperty]
    public string Username { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Owner"))
                return RedirectToPage("/Owner/Index");
            return RedirectToPage("/Index");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var username = (Username ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Vui lòng nhập tên đăng nhập và mật khẩu.";
            return Page();
        }

        var admin = await _identity.ValidateAdminAsync(username, Password);
        if (admin.Ok)
        {
            var adminClaims = new List<Claim>
            {
                new(ClaimTypes.Name, "admin"),
                new(ClaimTypes.Role, "Admin")
            };
            var adminId = new ClaimsIdentity(adminClaims, CookieAuthenticationDefaults.AuthenticationScheme);
            var adminPrincipal = new ClaimsPrincipal(adminId);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                adminPrincipal,
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });

            return RedirectToPage("/Index");
        }

        var owner = await _identity.ValidateOwnerAsync(username, Password);
        if (!owner.Ok || owner.Owner is null)
        {
            ErrorMessage = "Sai tài khoản hoặc mật khẩu.";
            return Page();
        }

        var place = await _places.GetAsync(owner.Owner.PlaceId);
        if (place is null)
        {
            ErrorMessage = "POI của tài khoản chủ quán không tồn tại hoặc đã bị xóa.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, owner.Owner.Username),
            new(ClaimTypes.Role, "Owner"),
            new("PlaceId", owner.Owner.PlaceId.ToString()),
            new("PlaceName", place.Name)
        };
        var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(id);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });

        return RedirectToPage("/Owner/Index");
    }
}
