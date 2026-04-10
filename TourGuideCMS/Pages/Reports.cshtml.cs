using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TourGuideCMS.Pages;

[Authorize(Roles = "Admin")]
public class ReportsModel : PageModel
{
    public void OnGet() { }
}
