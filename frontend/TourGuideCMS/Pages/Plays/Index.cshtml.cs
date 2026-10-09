using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Plays;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    /// <summary>Dữ liệu test/JMeter lỗi (chưa thay biến) hoặc rỗng — không hiển thị nguyên chuỗi trên CMS.</summary>
    public static bool IsInvalidDisplayField(string? s) =>
        string.IsNullOrWhiteSpace(s)
        || s.Contains("${", StringComparison.Ordinal)
        || s.Contains("{{", StringComparison.Ordinal);

    /// <summary>Mã thiết bị kiểu load test (không phải mã cài app).</summary>
    public static bool IsJmeterLikeDevice(string? deviceInstallId, string? deviceName)
    {
        var id = deviceInstallId ?? "";
        var name = deviceName ?? "";
        if (IsInvalidDisplayField(id) || IsInvalidDisplayField(name))
            return true;
        if (id.StartsWith("jmeter-", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("JMeter-", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static string SanitizePlaceOrLanguage(string? s) =>
        IsInvalidDisplayField(s) ? "—" : s!;

    /// <summary>Tên địa điểm giả trong DB — không đưa vào dropdown lọc.</summary>
    public static bool IsPlaceholderAggregatePlace(string? placeName)
    {
        var t = (placeName ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            return true;
        if (t is "—" or "---")
            return true;
        return string.Equals(t, "TÊN_QUÁN_CỐ_ĐỊNH", StringComparison.Ordinal);
    }

    private readonly CustomerAccountRepository _repo;

    public IndexModel(CustomerAccountRepository repo) => _repo = repo;

    [BindProperty(SupportsGet = true)]
    public string? Place { get; set; }

    public IReadOnlyList<string> PlaceOptions { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<PlayAggregateRow> Aggregates { get; private set; } = Array.Empty<PlayAggregateRow>();
    public IReadOnlyList<NarrationPlayRow> Recent { get; private set; } = Array.Empty<NarrationPlayRow>();

    public async Task OnGetAsync()
    {
        // Thuyết minh / yêu cầu GV: hệ số hiển thị cột "Số lượt" (bảng tổng hợp). 1 = đúng DB; đổi thành 2 nếu cần demo nhân đôi.
        const int PlayCountDisplayMultiplier = 1;

        var allAggregates = await _repo.GetAggregatesByPlaceAsync();
        var allRecent = await _repo.ListRecentPlaysAsync(200);

        PlaceOptions = allAggregates
            .Select(x => x.PlaceName)
            .Where(x => !IsInvalidDisplayField(x))
            .Where(x => !IsPlaceholderAggregatePlace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(Place))
        {
            var summaryRows = await _repo.GetPlayAggregateSummaryRowsAsync();
            var detailRows = allAggregates
                .Select(x => new PlayAggregateRow(x.PlaceName, x.Source, x.Count * PlayCountDisplayMultiplier, IsSummaryRow: false));
            Aggregates = summaryRows
                .Select(x => new PlayAggregateRow(x.PlaceName, x.Source, x.Count * PlayCountDisplayMultiplier, x.IsSummaryRow))
                .Concat(detailRows)
                .ToList();
            Recent = allRecent;
            return;
        }

        var selected = Place.Trim();
        Aggregates = allAggregates
            .Where(x => string.Equals(x.PlaceName, selected, StringComparison.OrdinalIgnoreCase))
            .Select(x => new PlayAggregateRow(x.PlaceName, x.Source, x.Count * PlayCountDisplayMultiplier, IsSummaryRow: false))
            .ToList();
        Recent = allRecent
            .Where(x => string.Equals(x.PlaceName, selected, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
