using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Listen;

[AllowAnonymous]
public class PayModel : PageModel
{
    private readonly PlaceRepository _places;
    private readonly CustomerAccountRepository _customers;

    public PayModel(PlaceRepository places, CustomerAccountRepository customers)
    {
        _places = places;
        _customers = customers;
    }

    [BindProperty(SupportsGet = true)]
    public int PlaceId { get; set; }

    public string PlaceName { get; private set; } = "";
    public double PriceVnd { get; private set; }

    [BindProperty]
    public string WebDeviceId { get; set; } = "";

    [BindProperty]
    public string? CustomerPhone { get; set; }

    [BindProperty]
    public string? CustomerPassword { get; set; }

    public string? ResultMessage { get; set; }
    public bool ResultOk { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (PlaceId <= 0)
        {
            ResultOk = false;
            ResultMessage = "Thiếu tham số placeId trên đường dẫn.";
            return Page();
        }

        var place = await _places.GetAsync(PlaceId);
        if (place is null)
            return NotFound();

        PlaceName = place.Name;
        PriceVnd = place.PremiumPriceDemo;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (PlaceId <= 0)
        {
            ResultOk = false;
            ResultMessage = "Thiếu placeId.";
            return Page();
        }

        var place = await _places.GetAsync(PlaceId);
        if (place is null)
            return NotFound();

        PlaceName = place.Name;
        PriceVnd = place.PremiumPriceDemo;

        if (PriceVnd <= 0)
        {
            ResultOk = true;
            ResultMessage = "Địa điểm này miễn phí thuyết minh.";
            return Page();
        }

        WebDeviceId = (WebDeviceId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(WebDeviceId) || WebDeviceId.Length < 8)
        {
            ResultOk = false;
            ResultMessage = "Thiếu mã thiết bị trình duyệt — bật JavaScript và tải lại trang.";
            return Page();
        }

        var phone = (CustomerPhone ?? string.Empty).Trim();
        var pwd = CustomerPassword ?? "";
        if (string.IsNullOrWhiteSpace(phone) || pwd.Length == 0)
        {
            ResultOk = false;
            ResultMessage = "Vui lòng quét QR tải app và tạo tài khoản trước, sau đó đăng nhập tại đây để trả phí.";
            return Page();
        }

        var (ok, _, user) = await _customers.LoginAsync(phone, pwd);
        if (!ok || user is null)
        {
            ResultOk = false;
            ResultMessage = "Không tìm thấy tài khoản hợp lệ. Vui lòng quét QR tải app, tạo tài khoản rồi đăng nhập lại để trả phí.";
            return Page();
        }

        var (payOk, msg, _) = await _customers.RecordPremiumPaymentDemoAsync(
            PlaceId,
            WebDeviceId,
            place.PremiumPriceDemo,
            user.Id);

        ResultOk = payOk;
        ResultMessage = msg;
        return Page();
    }
}
