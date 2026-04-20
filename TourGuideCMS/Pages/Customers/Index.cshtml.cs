using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Customers;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly CustomerAccountRepository _repo;

    public IndexModel(CustomerAccountRepository repo) => _repo = repo;

    public IReadOnlyList<CustomerUserRow> Users { get; private set; } = Array.Empty<CustomerUserRow>();
    public IReadOnlyList<CustomerDeviceRow> Devices { get; private set; } = Array.Empty<CustomerDeviceRow>();

    public async Task OnGetAsync()
    {
        Users = await _repo.ListUsersAsync();
        Devices = await _repo.ListCustomerDevicesAsync();
    }
}
