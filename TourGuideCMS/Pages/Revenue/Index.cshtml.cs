using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;
using System.Globalization;

namespace TourGuideCMS.Pages.Revenue;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly CustomerAccountRepository _payments;
    private readonly PlaceRepository _places;

    public IndexModel(CustomerAccountRepository payments, PlaceRepository places)
    {
        _payments = payments;
        _places = places;
    }

    public double GrandTotalVnd { get; private set; }
    public int TotalPaymentRows { get; private set; }
    public IReadOnlyList<PremiumRevenueLine> Lines { get; private set; } = Array.Empty<PremiumRevenueLine>();
    public IReadOnlyList<PremiumRevenuePaymentLine> PaymentHistory { get; private set; } = Array.Empty<PremiumRevenuePaymentLine>();

    public async Task OnGetAsync()
    {
        (GrandTotalVnd, TotalPaymentRows) = await _payments.GetPremiumGrandTotalsAsync();
        var byPlace = await _payments.GetPremiumRevenueByPlaceAsync();
        var payers = await _payments.GetPremiumPayersByPlaceAsync();
        var placeList = await _places.ListAsync();
        var nameById = placeList.ToDictionary(p => p.Id, p => p.Name);

        var payerTextByPlace = payers
            .GroupBy(x => x.PlaceId)
            .ToDictionary(
                g => g.Key,
                g => string.Join("<br/>", g
                    .Select(FormatPayerLine)
                    .Distinct(StringComparer.Ordinal)
                    .Take(8)));

        Lines = byPlace
            .Select(r => new PremiumRevenueLine(
                r.PlaceId,
                nameById.TryGetValue(r.PlaceId, out var n) ? n : $"POI #{r.PlaceId}",
                r.TotalVnd,
                r.PaymentCount,
                payerTextByPlace.TryGetValue(r.PlaceId, out var payerText) ? payerText : "Chưa có dữ liệu khách"))
            .ToList();

        PaymentHistory = payers
            .OrderByDescending(x => x.PaidAtUtc)
            .Take(200)
            .Select(x =>
            {
                var placeName = nameById.TryGetValue(x.PlaceId, out var n) ? n : $"POI #{x.PlaceId}";
                var customerText = x.CustomerUserId.HasValue && !string.IsNullOrWhiteSpace(x.CustomerPhoneOrEmail)
                    ? $"{(string.IsNullOrWhiteSpace(x.CustomerFullName) ? "Khách" : x.CustomerFullName.Trim())} ({x.CustomerPhoneOrEmail})"
                    : "Không gắn tài khoản";
                return new PremiumRevenuePaymentLine(
                    x.PaidAtUtc,
                    x.PlaceId,
                    placeName,
                    x.AmountVnd,
                    customerText,
                    MaskDeviceId(x.DeviceInstallId));
            })
            .ToList();
    }

    private static string FormatPayerLine(PremiumRevenuePayerRow row)
    {
        var amountText = row.AmountVnd.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        var paidAtText = row.PaidAtUtc == DateTime.MinValue
            ? "-"
            : row.PaidAtUtc.ToLocalTime().ToString("dd/MM HH:mm");

        if (row.CustomerUserId.HasValue && !string.IsNullOrWhiteSpace(row.CustomerPhoneOrEmail))
        {
            var full = string.IsNullOrWhiteSpace(row.CustomerFullName) ? "Khách" : row.CustomerFullName.Trim();
            return $"{full} ({row.CustomerPhoneOrEmail}) - {amountText} đ - {paidAtText}";
        }

        var device = (row.DeviceInstallId ?? string.Empty).Trim();
        if (device.Length > 14)
            device = $"{device[..6]}...{device[^4..]}";
        if (string.IsNullOrEmpty(device))
            device = "N/A";
        return $"Thiết bị {device} - {amountText} đ - {paidAtText}";
    }

    private static string MaskDeviceId(string? raw)
    {
        var device = (raw ?? string.Empty).Trim();
        if (device.Length > 14)
            return $"{device[..6]}...{device[^4..]}";
        return string.IsNullOrEmpty(device) ? "N/A" : device;
    }
}

public sealed record PremiumRevenueLine(int PlaceId, string PlaceName, double TotalVnd, int PaymentCount, string PayerSummaryHtml);
public sealed record PremiumRevenuePaymentLine(
    DateTime PaidAtUtc,
    int PlaceId,
    string PlaceName,
    double AmountVnd,
    string CustomerText,
    string DeviceMasked);
