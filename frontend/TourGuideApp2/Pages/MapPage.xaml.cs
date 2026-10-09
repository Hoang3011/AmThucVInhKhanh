using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.ApplicationModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using TourGuideApp2.Models;
using TourGuideApp2.Services;
using QRCoder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
namespace TourGuideApp2;
using TourGuideApp2.Services;
public partial class MapPage : ContentPage
{
    private List<Place> _pois = [];
    private Dictionary<int, int> _poiIndexById = new();
    private bool _suppressQrPickerEvent;
    private bool _suppressSimulatePoiPickerEvent;
    private readonly Dictionary<int, DateTime> _autoGeoLastLogByPoi = [];
    private const int AutoGeoLogCooldownSeconds = 60;
    /// <summary>Chống phát lặp cùng POI quá sớm (ra/vào vùng hoặc GPS giật) — khác với cooldown chỉ ghi log.</summary>
    private readonly Dictionary<int, DateTime> _autoGeoNextAllowedPlayUtcByPoi = [];
    private const int AutoGeoSpeechDebounceSeconds = 45;
    /// <summary>Fallback nếu POI chưa có bán kính riêng.</summary>
    private const double DefaultAutoGeoEnterMeters = 35;
    /// <summary>Tỷ lệ bán kính thoát so với bán kính vào để tạo hysteresis (đủ nhỏ để ra khỏi quán là dừng đọc).</summary>
    private const double AutoGeoExitMultiplier = 1.22;
    private const double MinAutoGeoExitMeters = 38;
    private const double AutoGeoMinGapMeters = 8;       // khoảng cách tối thiểu giữa quán gần nhất và quán thứ 2 (tránh nhầm khi hai quán sát nhau)

    /// <summary>POI đang “ở trong vùng” thuyết minh tự động (-1 = không có).</summary>
    private int _activeProximityPoiIndex = -1;
    private CancellationTokenSource? _proximityTtsCts;
    private readonly SemaphoreSlim _proximityCheckGate = new(1, 1);
    private CancellationTokenSource? _busStopTtsCts;
    private string? _activeBusStopToken;
    private const double BusStopEnterMeters = 28;
    private const double BusStopExitMeters = 38;
    /// <summary>GPS foreground: cập nhật tọa độ khi đang mở tab Bản đồ (không chạy nền khi thoát tab).</summary>
    private bool _isForegroundGpsListening;
#if ANDROID
    /// <summary>Một số máy (Samsung + Fake GPS) không bắn <see cref="Geolocation.LocationChanged"/>; poll bổ sung.</summary>
    private CancellationTokenSource? _androidGpsPollCts;
#endif
    /// <summary>Tránh xử lý trùng khi vừa có event vừa có poll.</summary>
    private double _lastQueuedGpsLat = double.NaN;
    private double _lastQueuedGpsLng = double.NaN;
    private const double GpsDuplicateEpsilonDegrees = 1e-7;
    /// <summary>Chỉ nhận điểm GPS có độ chính xác chấp nhận được (m). Null/0 = không có metadata, vẫn cho qua.</summary>
    private const double MaxGpsAccuracyMeters = 40;
    /// <summary>Loại bỏ điểm nhảy bất thường trong khoảng thời gian ngắn (m/s). Đi bộ nhanh ~2–3 m/s; để dư địa khi demo.</summary>
    private const double MaxGpsSpeedMetersPerSecond = 12;
    /// <summary>Chỉ áp speed-filter khi quãng nhảy đủ lớn, tránh false-positive do nhiễu nhỏ.</summary>
    private const double MinDistanceForSpeedFilterMeters = 45;
    /// <summary>Cho phép "teleport" lớn khi test Fake GPS để không bị kẹt tại điểm cũ.</summary>
    private const double AllowLargeJumpMeters = 420;
    /// <summary>Throttle để tránh spam UI + route/geofence khi GPS bắn quá dày.</summary>
    private static readonly TimeSpan MinGpsProcessGap = TimeSpan.FromMilliseconds(700);
    /// <summary>Làm mượt nhẹ để marker/route ổn định (0..1). Alpha nhỏ => mượt hơn nhưng trễ hơn.</summary>
    private const double GpsEmaAlpha = 0.28;
    private DateTime? _lastAcceptedGpsUtc;
    private DateTime? _lastProcessedGpsUtc;
    private double _smoothedGpsLat = double.NaN;
    private double _smoothedGpsLng = double.NaN;
    private string _currentZoneStatus = "Đang xác định vùng...";
    /// <summary>Sau khi bấm mũi tên / xe buýt, tạm không áp GPS để không đè vị trí giả lập.</summary>
    private DateTime? _gpsManualOverrideUntilUtc;
    private const int GpsManualOverrideSeconds = 60;
    private EventHandler<WebNavigatedEventArgs>? _mapNavigatedHandler;
    // Biến cho chế độ giả lập di chuyển
    private double _simulatedLat;
    private double _simulatedLng;
    private bool _hasSimulationPosition;
    private const double MOVE_STEP_METERS = 15; // mét mỗi lần nhấn
    /// <summary>Tọa độ mặc định khu phố ẩm thực Vĩnh Khánh (Q4) — nút &quot;Về khu demo&quot;.</summary>
    private const double DemoVinhKhanhLat = 10.7590;
    private const double DemoVinhKhanhLng = 106.7041;
    private string _selectedLanguage = "vi";
    private int _isRefreshingPois;

    /// <summary>OSRM: điểm nhắc rẽ / tên đường; null khi không có lộ trình chi tiết.</summary>
    private List<OsrmRoutingService.NavCue>? _footNavCues;
    private int _nextFootNavCueIndex;
    private DateTime? _lastFootNavTtsUtc;
    private const double FootNavCueTriggerMeters = 40;
    private const double FootNavMinTtsGapSeconds = 9;
    private const double FootNavSkipCueIfCloserThanMeters = 22;
    private DateTime? _manualNarrationOverrideUntilUtc;
    private const int ManualNarrationOverrideSeconds = 25;

    /// <summary>Ưu tiên POI từ API khi đã cấu hình <c>PoiApiUrl</c> (+ <c>PoiApiKey</c> cho Supabase); không thì đọc <c>VinhKhanh.db</c> cục bộ.</summary>
    private async Task<List<Place>> LoadPlacesAsync()
    {
        // Có URL remote + đã từng cache: hiển thị ngay (A536E + Wi‑Fi/4G khi CMS tắt — tránh chờ HTTP lâu), đồng thời refresh nền.
        if (PlaceApiService.HasRemoteApiConfigured())
        {
            try
            {
                var early = await PlaceRemoteCacheService.TryLoadAsync();
                if (early.Count > 0)
                {
                    var filtered = await DeletedPlacesTracker.FilterPlacesAsync(early);
                    await FillMissingNarrationFromLocalAsync(filtered);
                    _ = SoftRefreshRemotePlacesInBackgroundAsync();
                    return SanitizePlaces(filtered);
                }
            }
            catch
            {
                // ignore
            }
        }

        var remote = await PlaceApiService.TryGetRemotePlacesAsync();
        if (remote is { Count: > 0 })
        {
            // Giữ dữ liệu từ API để đồng bộ CMS, nhưng không làm mất phần thuyết minh:
            // nếu API thiếu text audio thì bù từ DB cục bộ theo tên + tọa độ gần đúng.
            await FillMissingNarrationFromLocalAsync(remote);
            return SanitizePlaces(remote);
        }

        if (PlaceApiService.HasRemoteApiConfigured())
        {
            try
            {
                var cached = await PlaceRemoteCacheService.TryLoadAsync();
                if (cached.Count > 0)
                {
                    var filtered = await DeletedPlacesTracker.FilterPlacesAsync(cached);
                    await FillMissingNarrationFromLocalAsync(filtered);
                    return SanitizePlaces(filtered);
                }
            }
            catch
            {
                // ignore
            }

            UpdateGeoStatusLabel("POI trên máy — đồng bộ nền vẫn chạy; tự cập nhật khi tới được máy chủ.");
        }

        var bundle = await LoadPlacesFromLocalDatabaseAsync();
        var fb = await DeletedPlacesTracker.FilterPlacesAsync(bundle);
        return SanitizePlaces(fb);
    }

    /// <summary>Khi đã có cache để mở map nhanh — thử sync lại POI ở nền, cập nhật picker nếu danh sách đổi.</summary>
    private async Task SoftRefreshRemotePlacesInBackgroundAsync()
    {
        try
        {
            var remote = await PlaceApiService.TryGetRemotePlacesAsync();
            if (remote is null || remote.Count == 0)
                return;

            await FillMissingNarrationFromLocalAsync(remote);
            var next = SanitizePlaces(remote);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _pois = next;
                RebuildPoiIndexFromCurrentPois();
                PopulateQrPicker();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SoftRefreshRemotePlacesInBackgroundAsync: {ex.Message}");
        }
    }

    private void RebuildPoiIndexFromCurrentPois()
    {
        _poiIndexById = _pois
            .Select((p, idx) => new { p, idx })
            .Where(x => x.p is not null)
            .GroupBy(x => x.p.Id)
            .ToDictionary(g => g.Key, g => g.First().idx);
    }

    /// <summary>POI mới trên CMS (vd. id 11) chưa có trong danh sách đang mở — thử GET /api/places lại.</summary>
    private async Task<bool> TryEnsurePoiLoadedFromApiAsync(int poiId)
    {
        if (poiId <= 0 || !PlaceApiService.HasRemoteApiConfigured())
            return false;

        try
        {
            var remote = await PlaceApiService.TryGetRemotePlacesAsync();
            if (remote is null || remote.Count == 0 || !remote.Any(p => p.Id == poiId))
                return false;

            await FillMissingNarrationFromLocalAsync(remote);
            _pois = SanitizePlaces(remote);
            RebuildPoiIndexFromCurrentPois();
            PopulateQrPicker();
            return _poiIndexById.ContainsKey(poiId);
        }
        catch
        {
            return false;
        }
    }

    private static List<Place> SanitizePlaces(List<Place> places)
    {
        foreach (var p in places)
        {
            if (p is null) continue;
            p.VietnameseAudioText = CleanupNarrationNoise(p.VietnameseAudioText);
            p.EnglishAudioText = CleanupNarrationNoise(p.EnglishAudioText);
            p.ChineseAudioText = CleanupNarrationNoise(p.ChineseAudioText);
            p.JapaneseAudioText = CleanupNarrationNoise(p.JapaneseAudioText);
        }

        return places;
    }

    private static bool HasAnyNarration(Place? p)
    {
        if (p is null) return false;
        return !string.IsNullOrWhiteSpace(p.VietnameseAudioText)
               || !string.IsNullOrWhiteSpace(p.EnglishAudioText)
               || !string.IsNullOrWhiteSpace(p.ChineseAudioText)
               || !string.IsNullOrWhiteSpace(p.JapaneseAudioText);
    }

    private static bool IsLikelySamePoi(Place a, Place b)
    {
        var sameName = string.Equals(
            (a.Name ?? string.Empty).Trim(),
            (b.Name ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);

        if (!sameName) return false;

        var dLat = Math.Abs(a.Latitude - b.Latitude);
        var dLng = Math.Abs(a.Longitude - b.Longitude);
        return dLat <= 0.0008 && dLng <= 0.0008;
    }

    private static void CopyNarrationIfMissing(Place target, Place source)
    {
        if (string.IsNullOrWhiteSpace(target.VietnameseAudioText))
            target.VietnameseAudioText = source.VietnameseAudioText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(target.EnglishAudioText))
            target.EnglishAudioText = source.EnglishAudioText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(target.ChineseAudioText))
            target.ChineseAudioText = source.ChineseAudioText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(target.JapaneseAudioText))
            target.JapaneseAudioText = source.JapaneseAudioText ?? string.Empty;
    }

    private static async Task FillMissingNarrationFromLocalAsync(List<Place> remote)
    {
        if (remote.Count == 0)
            return;

        var local = await PlaceLocalRepository.TryLoadAsync();
        if (local.Places.Count == 0)
            return;

        var localById = local.Places
            .Where(p => p is not null && p.Id > 0)
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var r in remote)
        {
            Place? candidate = null;
            if (r.Id > 0)
                localById.TryGetValue(r.Id, out candidate);
            candidate ??= local.Places.FirstOrDefault(l => IsLikelySamePoi(r, l));
            if (candidate is null)
                continue;

            CopyNarrationIfMissing(r, candidate);
        }
    }

    /// <summary>Loại tiền tố số rác kiểu "7272727..." trước nội dung thuyết minh.</summary>
    private static string CleanupNarrationNoise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = Regex.Replace(
            text.Trim(),
            @"^\s*(?:\d[\d\s\-_.,;:\|]*){3,}",
            string.Empty,
            RegexOptions.CultureInvariant);

        return cleaned.TrimStart();
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }

        return string.Empty;
    }

    /// <summary>Rút gọn text đưa vào HTML/WebView — tránh OOM/crash WebView trên máy RAM thấp khi POI có đoạn thuyết minh rất dài.</summary>
    private static string TruncateForMapEmbed(string? text, int maxChars)
    {
        var s = (text ?? string.Empty).Trim();
        if (s.Length <= maxChars)
            return s;
        return s[..maxChars] + "…";
    }

    private static string PickNarrationText(Place place, string? lang)
    {
        var normalizedLang = (lang ?? "vi").Trim().ToLowerInvariant();
        return normalizedLang switch
        {
            "en" => FirstNonEmpty(place.EnglishAudioText),
            "zh" => FirstNonEmpty(place.ChineseAudioText),
            "ja" => FirstNonEmpty(place.JapaneseAudioText),
            _ => FirstNonEmpty(place.VietnameseAudioText)
        };
    }

    /// <summary>
    /// Khi API chỉ có bản tiếng Việt: máy chọn EN/ZH/JA vẫn nghe được — đọc bản VI với TTS/locale <c>vi</c> (khớp file <c>poi_*.mp3</c> trong gói).
    /// </summary>
    private static (string Text, string TtsLang) ResolveNarrationForPlayback(Place place, string? requestedLang)
    {
        var ui = (requestedLang ?? "vi").Trim().ToLowerInvariant();
        var text = PickNarrationText(place, ui);
        if (!string.IsNullOrWhiteSpace(text))
            return (text, ui);

        if (ui is "en" or "zh" or "ja")
        {
            var vi = FirstNonEmpty(place.VietnameseAudioText);
            if (!string.IsNullOrWhiteSpace(vi))
                return (vi, "vi");
        }

        return (string.Empty, ui);
    }

    /// <summary>TTS ngắn khi bấm Chỉ đường — không phát file thuyết minh dài của POI.</summary>
    private static string BuildDirectionsTtsText(string? destinationDisplayName, string lang, bool osrmDetailedRoute = false)
    {
        var l = (lang ?? "vi").Trim().ToLowerInvariant();
        var name = string.IsNullOrWhiteSpace(destinationDisplayName) ? null : destinationDisplayName.Trim();
        if (osrmDetailedRoute)
        {
            return l switch
            {
                "en" => name is null
                    ? "Walking directions to the bus stop. Follow the orange route on the map; at junctions you will hear turn prompts."
                    : $"Walking directions to {name}. Follow the orange route on the map; at junctions you will hear turn prompts.",
                "zh" => name is null
                    ? "步行前往公交站。请沿地图上橙色路线行走，路口处会有转弯提示。"
                    : $"步行前往「{name}」。请沿地图上橙色路线行走，路口处会有转弯提示。",
                "ja" => name is null
                    ? "バス停までの徒歩ルートです。地図のオレンジの線に沿ってください。交差点では曲がる方向を案内します。"
                    : $"「{name}」までの徒歩ルートです。地図のオレンジの線に沿ってください。交差点では曲がる方向を案内します。",
                _ => name is null
                    ? "Đang chỉ đường đi bộ tới điểm dừng xe buýt. Hãy theo đường màu cam trên bản đồ; tới gần ngã rẽ ứng dụng sẽ báo hướng và tên đường."
                    : $"Đang chỉ đường đi bộ tới {name}. Hãy theo đường màu cam trên bản đồ; tới gần ngã rẽ ứng dụng sẽ báo hướng và tên đường."
            };
        }

        return l switch
        {
            "en" => name is null
                ? "Directions to the bus stop. Follow the orange dashed line on the map."
                : $"Directions to {name}. Follow the orange dashed line on the map.",
            "zh" => name is null
                ? "正在为您指引前往公交站。请沿地图上的橙色虚线前往。"
                : $"正在为您指引前往「{name}」。请沿地图上的橙色虚线前往。",
            "ja" => name is null
                ? "バス停への道順です。地図上のオレンジ色の破線に沿ってください。"
                : $"「{name}」への道順です。地図上のオレンジ色の破線に沿ってください。",
            _ => name is null
                ? "Đang chỉ đường tới điểm dừng xe buýt. Hãy theo đường màu cam nét đứt trên bản đồ."
                : $"Đang chỉ đường tới {name}. Hãy theo đường màu cam nét đứt trên bản đồ."
        };
    }

    void ClearFootNavTurnState()
    {
        _footNavCues = null;
        _nextFootNavCueIndex = 0;
        _lastFootNavTtsUtc = null;
    }

    void AdvanceFootNavPastNearbyCues()
    {
        if (_footNavCues is null)
            return;
        while (_nextFootNavCueIndex < _footNavCues.Count)
        {
            var c = _footNavCues[_nextFootNavCueIndex];
            if (CalculateDistance(_simulatedLat, _simulatedLng, c.Lat, c.Lng) < FootNavSkipCueIfCloserThanMeters)
                _nextFootNavCueIndex++;
            else
                break;
        }
    }

    /// <summary>Thử vẽ lộ trình OSRM; nếu thất bại thì nét thẳng như cũ. Item1 = đã vẽ polyline OSRM; Item2 = có bước rẽ TTS.</summary>
    async Task<(bool usedOsrmPolyline, bool hasTurnCues)> TryApplyOsrmFootRouteOnMapAsync(double destLat, double destLng, string? destName)
    {
        ClearFootNavTurnState();
        var route = await OsrmRoutingService.TryGetFootRouteAsync(_simulatedLat, _simulatedLng, destLat, destLng, destName);
        if (route is null)
        {
            var d1 = destLat.ToString(CultureInfo.InvariantCulture);
            var d2 = destLng.ToString(CultureInfo.InvariantCulture);
            await mapView.EvaluateJavaScriptAsync($"window.showNavigationGuideTo && window.showNavigationGuideTo({d1},{d2});");
            return (false, false);
        }

        var b64 = OsrmRoutingService.PolylineToJsonBase64(route.Polyline);
        await mapView.EvaluateJavaScriptAsync(
            $"window.showNavigationPolylineFromBase64 && window.showNavigationPolylineFromBase64('{b64}');");

        var hasCues = route.Cues.Count > 0;
        _footNavCues = hasCues ? [.. route.Cues] : null;
        _nextFootNavCueIndex = 0;
        AdvanceFootNavPastNearbyCues();
        return (true, hasCues);
    }

    async Task MaybeAnnounceFootNavCueAsync()
    {
        if (_footNavCues is null || _nextFootNavCueIndex >= _footNavCues.Count)
            return;

        var now = DateTime.UtcNow;
        if (_lastFootNavTtsUtc is DateTime t && (now - t).TotalSeconds < FootNavMinTtsGapSeconds)
            return;

        var cue = _footNavCues[_nextFootNavCueIndex];
        if (CalculateDistance(_simulatedLat, _simulatedLng, cue.Lat, cue.Lng) > FootNavCueTriggerMeters)
            return;

        var lang = string.IsNullOrWhiteSpace(_selectedLanguage) ? "vi" : _selectedLanguage;
        var text = OsrmRoutingService.PickCueText(cue, lang);
        if (string.IsNullOrWhiteSpace(text))
        {
            _nextFootNavCueIndex++;
            return;
        }

        CancelProximitySpeech();
        CancelBusStopSpeech();
        var durationSeconds = await NarrationQueueService.EnqueuePoiOrTtsAsync(-1, lang, text);
        UpdateLastPlayedLabel("Chỉ đường (rẽ)", "Chỉ đường");
        await HistoryLogService.AddAsync("Chỉ đường (rẽ)", "Chỉ đường", lang, durationSeconds);
        _nextFootNavCueIndex++;
        _lastFootNavTtsUtc = now;
    }

    private async Task<List<Place>> LoadPlacesFromLocalDatabaseAsync()
    {
        // false: không xóa DB trên máy mỗi lần mở map. true khi cần ép copy lại VinhKhanh.db từ bản cài.
        // false: giữ VinhKhanh.db đã copy; true mỗi lần xóa DB cục bộ — dễ mất POI mới trên CMS khi API tạm lỗi.
        const bool forceUpdate = false;
        var result = await PlaceLocalRepository.TryLoadAsync(forceRecopyFromPackage: forceUpdate);

        switch (result.Error)
        {
            case PlaceLocalRepository.LoadError.DbEmptyNoTables:
                await DisplayAlertAsync("Lỗi Database - DB rỗng",
                    "File VinhKhanh.db đã copy nhưng không có bảng nào.\n\n" +
                    "Hãy mở file VinhKhanh.db bằng DB Browser for SQLite để kiểm tra lại.",
                    "OK");
                break;
            case PlaceLocalRepository.LoadError.NoPlaceTable:
                await DisplayAlertAsync("Lỗi Database - Không tìm thấy bảng",
                    "Không có bảng 'Place' hoặc 'Places' trong VinhKhanh.db.",
                    "OK");
                break;
            case PlaceLocalRepository.LoadError.Exception when !string.IsNullOrEmpty(result.Message):
                await DisplayAlertAsync("Lỗi Database",
                    $"Không đọc được VinhKhanh.db:\n{result.Message}", "OK");
                break;
        }

        return result.Places;
    }
    public MapPage()
    {
        InitializeComponent();
        btnCurrentLang.Text = "🇻🇳 VI";
        langOptions.IsVisible = false;
        lblGeoStatus.Text = "📍 Trạng thái: Đang xác định vùng...";
        lblCooldownStatus.Text = "⏳ Cooldown: -";
        lblLastPlayedStatus.Text = "🔊 Đã phát gần nhất: -";
    }
    // ── Mở/đóng thanh chọn ngôn ngữ ──
    private void OnLanguageButtonClicked(object? sender, EventArgs e)
    {
        langOptions.IsVisible = !langOptions.IsVisible;
    }

    // ── Chọn Tiếng Việt ──
    private void OnSelectVietnameseClicked(object? sender, EventArgs e)
    {
        _selectedLanguage = "vi";
        btnCurrentLang.Text = "🇻🇳 VI";
        langOptions.IsVisible = false;
    }

    // ── Chọn English ──
    private void OnSelectEnglishClicked(object? sender, EventArgs e)
    {
        _selectedLanguage = "en";
        btnCurrentLang.Text = "🇬🇧 EN";
        langOptions.IsVisible = false;
    }
    // ── Chọn Tiếng Trung (mới) ──
    private void OnSelectChineseClicked(object? sender, EventArgs e)
    {
        _selectedLanguage = "zh";
        btnCurrentLang.Text = "🇨🇳 ZH";
        langOptions.IsVisible = false;
    }

    // ── Chọn Tiếng Nhật (mới) ──
    private void OnSelectJapaneseClicked(object? sender, EventArgs e)
    {
        _selectedLanguage = "ja";
        btnCurrentLang.Text = "🇯🇵 JA";
        langOptions.IsVisible = false;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            // Không await — POI + lượt phát/tuyến chờ (cùng pipeline với tab Khám phá / OnResume).
            CustomerAppWarmSyncService.Schedule();

            if (OperatingSystem.IsAndroid())
                await Task.Delay(280);

            _ = SafeLoadMapAsync();
            _ = TryStartForegroundGpsListeningAsync();
            _ = PlayWelcomeMessageAsync();

            _ = DelayedStartMapHeartbeatAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MapPage.OnAppearing: {ex}");
        }
    }

    /// <summary>Trì ping CMS một chút sau khi Activity/WebView khởi tạo — giảm crash khi thiết bị yếu hoặc vừa cài APK.</summary>
    private static async Task DelayedStartMapHeartbeatAsync()
    {
        try
        {
            await Task.Delay(650);
            DeviceHeartbeatService.StartMapTabSession();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DelayedStartMapHeartbeatAsync: {ex.Message}");
        }
    }

    private async Task SafeLoadMapAsync()
    {
        try
        {
            await LoadMapAsync();
        }
        catch (Exception ex)
        {
            try
            {
                if (mapView is null)
                    return;
                mapView.Source = new HtmlWebViewSource
                {
                    Html = $"""
                    <html>
                      <body style="font-family:Arial,Helvetica,sans-serif;background:#fafafa;color:#222;padding:16px;">
                        <h3>Khong tai duoc ban do</h3>
                        <p>Vui long mo lai tab Ban do hoac khoi dong lai app.</p>
                        <pre style="white-space:pre-wrap;background:#fff;border:1px solid #ddd;padding:8px;border-radius:8px;">{System.Net.WebUtility.HtmlEncode(ex.Message)}</pre>
                      </body>
                    </html>
                    """
                };
            }
            catch (Exception inner)
            {
                Debug.WriteLine($"SafeLoadMapAsync fallback UI: {inner.Message}");
            }
        }
    }

    protected override void OnDisappearing()
    {
        // Không dừng GPS khi chuyển tab — vẫn theo dõi vị trí (kể cả khi gập app, nếu đã cấp quyền nền).
        base.OnDisappearing();
        _ = DeviceHeartbeatService.NotifyMapTabLeftAsync();
    }

    private async Task PlayWelcomeMessageAsync()
    {
        // Đồng bộ MainPage: không TTS chào tự động trên Android — tránh crash khi vào tab bản đồ.
        if (OperatingSystem.IsAndroid())
            return;

        try
        {
            // Đợi UI/map ổn định một chút để TTS không bị hụt câu khi vừa mở trang.
            await Task.Delay(900);
            _ = await NarrationQueueService.EnqueuePoiOrTtsAsync(-1, "vi",
                "Xin chào, chào mừng bạn đến với phố ẩm thực Vĩnh Khánh.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PlayWelcomeMessageAsync: {ex.Message}");
        }
    }

    private async Task LoadMapAsync()
    {
        var loadPlacesTask = LoadPlacesAsync();
        var gpsTask = TryGetCurrentLocationAsync();
        await Task.WhenAll(loadPlacesTask, gpsTask);

        _pois = await loadPlacesTask;
        var gpsFix = await gpsTask;

        RebuildPoiIndexFromCurrentPois();
        PopulateQrPicker();

        // Vị trí mặc định - khu Vĩnh Khánh, Q4
        double centerLat = DemoVinhKhanhLat;
        double centerLng = DemoVinhKhanhLng;
        if (gpsFix is not null)
        {
            centerLat = gpsFix.Latitude;
            centerLng = gpsFix.Longitude;
        }

        // Luôn hiện điểm xe buýt demo trên map (không phụ thuộc GPS).
        const bool hasCurrentLocation = true;

        // Chuẩn bị dữ liệu POI
        var poiDtos = new List<object>();
        for (int i = 0; i < _pois.Count; i++)
        {
            var p = _pois[i];
            if (p == null) continue;
            poiDtos.Add(new
            {
                id = p.Id,
                idx = i,
                name = TruncateForMapEmbed(p.Name, 200),
                lat = p.Latitude,
                lng = p.Longitude,
                img = TruncateForMapEmbed(p.ImageUrl, 2000),
                mapUrl = TruncateForMapEmbed(p.MapUrl, 2000),
                description = TruncateForMapEmbed(p.Description, 900),
                viText = TruncateForMapEmbed(p.VietnameseAudioText, 1600),
                enText = TruncateForMapEmbed(p.EnglishAudioText, 1600),
                zhText = TruncateForMapEmbed(p.ChineseAudioText, 1600),
                jaText = TruncateForMapEmbed(p.JapaneseAudioText, 1600),
                premiumPrice = p.PremiumPriceDemo
            });
        }

        // Tạo mảng JSON thủ công (tránh vấn đề escape)
        var poiJsArray = string.Join(",", poiDtos.Select(x => JsonSerializer.Serialize(x)));
        var hasCurrentLocationJs = hasCurrentLocation ? "true" : "false";
        var busStopDtos = new List<object>
        {
            new { code = "KHANH_HOI", name = "Điểm dừng xe buýt Khánh Hội", lat = 10.7597, lng = 106.7050, poiId = _pois.Count > 4 ? _pois[4].Id : 4 },
            new { code = "VINH_HOI",  name = "Điểm dừng xe buýt Vĩnh Hội",  lat = 10.7586, lng = 106.7036, poiId = _pois.Count > 0 ? _pois[0].Id : 0 },
            new { code = "XOM_CHIEU", name = "Điểm dừng xe buýt Xóm Chiếu", lat = 10.7603, lng = 106.7026, poiId = _pois.Count > 1 ? _pois[1].Id : 1 }
        };
        var busStopJsArray = string.Join(",", busStopDtos.Select(x => JsonSerializer.Serialize(x)));
        var routePoints = await RouteTrackService.GetPointsAsync();
        const int maxRoutePointsForMap = 900;
        if (routePoints.Count > maxRoutePointsForMap)
            routePoints = routePoints.Skip(routePoints.Count - maxRoutePointsForMap).ToList();

        var routeJsArray = string.Join(",", routePoints.Select(x => JsonSerializer.Serialize(new
        {
            lat = x.Latitude,
            lng = x.Longitude
        })));

        bool hasInternet;
        try
        {
            hasInternet = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        }
        catch
        {
            hasInternet = true;
        }
        // Chỉ dùng danh sách HTML khi *không* có Internet (CDN Leaflet không tải được).
        // Có 4G/Wi‑Fi nhưng API CMS không tới (mạng khác): vẫn bản đồ Leaflet + POI đã load từ SQLite — geofence / bấm nghe giống hình 2.
        var useOfflineEmbeddedMap = !hasInternet;

        if (useOfflineEmbeddedMap)
        {
            var hint =
                "Thiết bị đang không có Internet nên bản đồ nền (Leaflet/CDN) không tải được. Bạn vẫn có thể bấm nghe thuyết minh và các nút chức năng POI bên dưới.";
            var offlineHtml = BuildOfflineMapHtml(poiJsArray, hint);
            mapView.Source = new HtmlWebViewSource { Html = offlineHtml };
            AttachMapNavigatedHandler(hasCurrentLocation, centerLat, centerLng);
            return;
        }

        string html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta name='viewport' content='width=device-width, initial-scale=1.0'>
  <style>
    html, body {{
      margin:0;
      padding:0;
      height:100%;
      width:100%;
      font-family: Arial, Helvetica, sans-serif;
    }}
    #map {{
      height: 100vh;
      width: 100%;
    }}
    .poi-icon {{
      width: 44px;
      height: 44px;
      border-radius: 50%;
      background: rgba(255, 107, 0, 0.9);
      border: 3px solid #ffffff;
      box-shadow: 0 2px 6px rgba(0,0,0,0.25);
      display: flex;
      align-items: center;
      justify-content: center;
      overflow: hidden;
    }}
    .poi-icon img {{
      width: 28px;
      height: 28px;
      border-radius: 7px;
      object-fit: cover;
      background: #fff;
    }}
    .leaflet-popup-content-wrapper {{
      border-radius: 16px;
      padding: 0;
      overflow: hidden;
    }}
    .leaflet-popup-content {{
      margin: 0;
      width: 220px;
    }}
    .poi-popup {{
      background: rgba(255,255,255,0.98);
      padding: 12px;
    }}
    .poi-title {{
      font-size: 16px;
      font-weight: 700;
      margin-bottom: 8px;
      color: #FF6B00;
    }}
    .poi-photo {{
      width: 100%;
      height: 120px;
      object-fit: cover;
      border-radius: 10px;
      margin-bottom: 10px;
      background: #f3f3f3;
    }}
    .poi-desc {{
      margin-bottom: 8px;
      font-size: 12px;
      line-height: 1.4;
      color: #2c2c2c;
      max-height: 74px;
      overflow-y: auto;
      padding-right: 2px;
    }}
    .poi-label {{
      display: inline-block;
      font-size: 11px;
      font-weight: 700;
      color: #2456d1;
      margin-right: 6px;
    }}
    .poi-actions {{
      display:flex;
      gap:10px;
      justify-content: flex-start;
    }}
    .poi-btn {{
      background: #ff9999;
      color: #ffffff;
      padding: 10px 12px;
      border-radius: 999px;
      text-decoration: none;
      font-weight: 700;
      font-size: 13px;
      display: inline-block;
      user-select: none;
      white-space: nowrap;
      box-shadow: 0 2px 8px rgba(255, 140, 140, 0.4);
      border: 0;
      cursor: pointer;
    }}
    .poi-btn.secondary {{
      background: #d88aff;
    }}
  </style>
  <link rel=""stylesheet"" href=""https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"" />
  <script src=""https://unpkg.com/leaflet-color-markers@latest/leaflet-color-markers.min.js""></script>
</head>
<body>
  <div id='map'></div>
  <div id='debug' style='position:absolute; top:8px; left:8px; z-index:9999; background:rgba(0,0,0,0.55); color:#fff; padding:8px 10px; border-radius:10px; font-size:12px; max-width:85%;'>
    Loading...
  </div>

  <script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
  <link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css' />
  <script>
    (function () {{
      var dbg = document.getElementById('debug');
      function setDbg(text) {{
        try {{ if (dbg) dbg.innerText = text; }} catch (_) {{}}
      }}

      function esc(value) {{
        if (value === null || value === undefined) return '';
        return String(value)
          .replace(/&/g, '&amp;')
          .replace(/</g, '&lt;')
          .replace(/>/g, '&gt;')
          .replace(/""/g, '&quot;')
          .replace(/'/g, '&#39;');
      }}

      try {{
        setDbg('JS init...');

        var pois = [{poiJsArray}];
        if (!Array.isArray(pois)) pois = [];
        pois = pois.filter(function(p) {{
          return p && typeof p.lat === 'number' && typeof p.lng === 'number';
        }});

        var map = L.map('map', {{ scrollWheelZoom: true, zoomControl: true }})
          .setView([{centerLat.ToString(CultureInfo.InvariantCulture)}, {centerLng.ToString(CultureInfo.InvariantCulture)}], 16);
        window.appMap = map;
        // ==================== CHẤM ĐEN CỐ ĐỊNH KÍCH THƯỚC (pixel) ====================
        var blackDotIcon = L.divIcon({{
            className: 'custom-black-dot',
            html: `<div style='width:15px;height:15px;background:#000;border-radius:50%;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.4);'></div>`,
            iconSize: [24, 24],
            iconAnchor: [12, 12]
        }});

        window.userMarker = L.marker([{centerLat.ToString(CultureInfo.InvariantCulture)}, {centerLng.ToString(CultureInfo.InvariantCulture)}], {{
            icon: blackDotIcon,
            zIndexOffset: 1000   // luôn nằm trên cùng
        }}).addTo(map);

        window.userMarker.bindPopup('<b>Vị trí của bạn</b><br/><small>GPS khi bật quyền; trong phòng có thể chọn quán (mô phỏng gần quán).</small>');
        var hasCurrentLocation = {hasCurrentLocationJs};
        var currentLat = {centerLat.ToString(CultureInfo.InvariantCulture)};
        var currentLng = {centerLng.ToString(CultureInfo.InvariantCulture)};
        var busStops = [{busStopJsArray}];
        var routePoints = [{routeJsArray}];
        var routePolyline = null;

        window.renderRoutePath = function(points) {{
          if (!Array.isArray(points) || points.length === 0) return false;
          var latlngs = points
            .filter(function(p) {{ return p && typeof p.lat === 'number' && typeof p.lng === 'number'; }})
            .map(function(p) {{ return [p.lat, p.lng]; }});
          if (latlngs.length === 0) return false;

          if (!routePolyline) {{
            routePolyline = L.polyline(latlngs, {{
              color: '#0057D9',
              weight: 4,
              opacity: 0.9
            }}).addTo(map);
          }} else {{
            routePolyline.setLatLngs(latlngs);
          }}
          return true;
        }};

        window.appendRoutePoint = function(lat, lng) {{
          if (typeof lat !== 'number' || typeof lng !== 'number') return false;
          routePoints.push({{ lat: lat, lng: lng }});
          return window.renderRoutePath(routePoints);
        }};

        window.clearRoutePath = function() {{
          routePoints = [];
          if (routePolyline && map) {{
            map.removeLayer(routePolyline);
            routePolyline = null;
          }}
          return true;
        }};

        var navGuidePolyline = null;
        window.clearNavigationGuide = function() {{
          if (navGuidePolyline && map) {{
            map.removeLayer(navGuidePolyline);
            navGuidePolyline = null;
          }}
          return true;
        }};
        window.showNavigationGuideTo = function(destLat, destLng) {{
          if (typeof destLat !== 'number' || typeof destLng !== 'number' || !map) return false;
          var lat = currentLat, lng = currentLng;
          if (window.userMarker && typeof window.userMarker.getLatLng === 'function') {{
            var ull = window.userMarker.getLatLng();
            if (ull && typeof ull.lat === 'number' && typeof ull.lng === 'number') {{
              lat = ull.lat;
              lng = ull.lng;
            }}
          }}
          if (typeof lat !== 'number' || typeof lng !== 'number') return false;
          if (window.clearNavigationGuide) window.clearNavigationGuide();
          navGuidePolyline = L.polyline([[lat, lng], [destLat, destLng]], {{
            color: '#FF6D00',
            weight: 4,
            dashArray: '10,12',
            opacity: 0.95
          }}).addTo(map);
          try {{
            map.fitBounds(L.latLngBounds([lat, lng], [destLat, destLng]), {{ padding: [40, 40], maxZoom: 17 }});
          }} catch (err) {{}}
          if (typeof setDbg === 'function') setDbg('Chỉ đường: ' + lat.toFixed(5) + ',' + lng.toFixed(5) + ' → ' + destLat.toFixed(5) + ',' + destLng.toFixed(5));
          return true;
        }};

        window.showNavigationPolylineFromBase64 = function(b64) {{
          try {{
            if (typeof b64 !== 'string' || !b64 || !map) return false;
            var json = atob(b64);
            var latlngs = JSON.parse(json);
            if (!Array.isArray(latlngs) || latlngs.length < 2) return false;
            for (var j = 0; j < latlngs.length; j++) {{
              var p = latlngs[j];
              if (!p || typeof p[0] !== 'number' || typeof p[1] !== 'number') return false;
            }}
            if (window.clearNavigationGuide) window.clearNavigationGuide();
            navGuidePolyline = L.polyline(latlngs, {{
              color: '#FF6D00',
              weight: 5,
              opacity: 0.92
            }}).addTo(map);
            try {{
              map.fitBounds(navGuidePolyline.getBounds(), {{ padding: [50, 50], maxZoom: 17 }});
            }} catch (err2) {{}}
            if (typeof setDbg === 'function') setDbg('Chỉ đường OSRM: ' + latlngs.length + ' điểm');
            return true;
          }} catch (err) {{ return false; }}
        }};

        // Tránh tile.openstreetmap.org vì WebView mobile thường không gửi Referer => bị chặn.
        var tileProviders = [
          {{
            name: 'CartoDB Voyager',
            url: 'https://{{s}}.basemaps.cartocdn.com/rastertiles/voyager/{{z}}/{{x}}/{{y}}{{r}}.png',
            options: {{
              subdomains: 'abcd',
              maxZoom: 20,
              attribution: '&copy; OpenStreetMap contributors &copy; CARTO'
            }}
          }},
          {{
            name: 'OpenStreetMap',
            url: 'https://{{s}}.tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png',
            options: {{
              maxZoom: 19,
              attribution: '&copy; OpenStreetMap contributors'
            }}
          }}
        ];
        var providerIndex = 0;
        var layer = null;
        var tileErrorCount = 0;

        function switchTileProvider(index) {{
          if (layer) {{
            map.removeLayer(layer);
          }}

          var provider = tileProviders[index];
          layer = L.tileLayer(provider.url, provider.options);
          layer.addTo(map);
          tileErrorCount = 0;
          setDbg('Tile: ' + provider.name + ' | POIs: ' + pois.length);
        }}

        switchTileProvider(providerIndex);

        map.on('tileload', function() {{
          setDbg('POIs: ' + pois.length + ' | map loaded');
        }});
        map.on('tileerror', function() {{
          tileErrorCount++;
          if (tileErrorCount >= 2 && providerIndex < tileProviders.length - 1) {{
            providerIndex++;
            switchTileProvider(providerIndex);
            return;
          }}
          setDbg('Tile error (' + tileErrorCount + ')');
        }});

        // Mục 8: bật GPS thì hiển thị 3 điểm dừng xe buýt (Khánh Hội, Vĩnh Hội, Xóm Chiếu).
        if (hasCurrentLocation && Array.isArray(busStops)) {{
          var busIcon = L.divIcon({{
            className: 'bus-stop-icon',
            html: `<div style='width:30px;height:30px;background:#1976D2;color:#fff;border-radius:50%;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:16px;border:2px solid #fff;box-shadow:0 2px 6px rgba(0,0,0,0.3);'>🚌</div>`,
            iconSize: [30, 30],
            iconAnchor: [15, 15]
          }});

          busStops.forEach(function(s) {{
            if (!s || typeof s.lat !== 'number' || typeof s.lng !== 'number') return;
            var marker = L.marker([s.lat, s.lng], {{ icon: busIcon, zIndexOffset: 800 }}).addTo(map);
            var popup = `<div style='font-size:13px;line-height:1.4'>`
              + `<b>${{esc(s.name || 'Điểm dừng xe buýt')}}</b><br/>`
              + `Mã QR: <b>${{esc(s.code || '')}}</b><br/>`
              + `<a class='poi-btn secondary' href='app://poi?id=${{s.poiId}}&lang=vi'>Nghe ngay</a>`
              + ` <a class='poi-btn' href='app://directions?lat=${{s.lat}}&lng=${{s.lng}}'>🧭 Chỉ đường</a>`
              + `</div>`;
            marker.bindPopup(popup);
          }});
        }}
// Biến để lưu circle/marker theo POI id
var circles = [];
window.poiMarkers = {{}};
window.poiCircles = {{}};
window._highlightPoiId = -1;
window.openPoiById = function(id) {{
    var marker = window.poiMarkers[id];
    if (marker && typeof marker.openPopup === 'function') {{
        marker.openPopup();
        if (marker.getLatLng && map && map.panTo) {{
            map.panTo(marker.getLatLng());
        }}
        return true;
    }}
    var circle = window.poiCircles[id];
    if (circle && typeof circle.openPopup === 'function') {{
        circle.openPopup();
        if (circle.getLatLng && map && map.panTo) {{
            map.panTo(circle.getLatLng());
        }}
        return true;
    }}
    return false;
}};

window.setNearestPoiHighlight = function(id) {{
    var previousId = window._highlightPoiId;
    if (typeof previousId === 'number' && previousId >= 0) {{
        var previousMarker = window.poiMarkers[previousId];
        if (previousMarker && window.redPinIcon && previousMarker.setIcon) {{
            previousMarker.setIcon(window.redPinIcon);
        }}
        var previousCircle = window.poiCircles[previousId];
        if (previousCircle && previousCircle.setStyle) {{
            previousCircle.setStyle({{
                color: '#3388ff',
                fillColor: '#3388ff',
                fillOpacity: 0.2,
                weight: 2,
                opacity: 0.7
            }});
        }}
    }}

    if (typeof id !== 'number' || id < 0) {{
        window._highlightPoiId = -1;
        return false;
    }}

    var marker = window.poiMarkers[id];
    if (marker && window.nearestPinIcon && marker.setIcon) {{
        marker.setIcon(window.nearestPinIcon);
    }}

    var circle = window.poiCircles[id];
    if (circle && circle.setStyle) {{
        circle.setStyle({{
            color: '#ff6b00',
            fillColor: '#ffa43a',
            fillOpacity: 0.35,
            weight: 3,
            opacity: 0.95
        }});
    }}

    window._highlightPoiId = id;
    return true;
}};

var redPinIcon = L.icon({{
    iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-red.png',
    iconRetinaUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-red.png',
    shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-shadow.png',
    iconSize: [25, 41],
    iconAnchor: [12, 41],
    popupAnchor: [1, -34],
    shadowSize: [41, 41]
}});
var nearestPinIcon = L.icon({{
    iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-orange.png',
    iconRetinaUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-orange.png',
    shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/images/marker-shadow.png',
    iconSize: [25, 41],
    iconAnchor: [12, 41],
    popupAnchor: [1, -34],
    shadowSize: [41, 41]
}});
window.redPinIcon = redPinIcon;
window.nearestPinIcon = nearestPinIcon;

for (var i = 0; i < pois.length; i++) {{
    var p = pois[i];

    var marker = L.marker([p.lat, p.lng], {{
        icon: redPinIcon
    }}).addTo(map);
    window.poiMarkers[p.id] = marker;

    // 2. Tạo vòng tròn (circle) xung quanh marker
    var circle = L.circle([p.lat, p.lng], {{
        radius: 50,               
        color: '#3388ff',          // viền xanh giống Google Maps
        fillColor: '#3388ff',      // màu nền xanh nhạt
        fillOpacity: 0.2,          // độ mờ thấp để không che khuất quá nhiều
        weight: 2,                 // độ dày viền
        opacity: 0.7
    }}).addTo(map);
    window.poiCircles[p.id] = circle;

    circles.push(circle);  // lưu lại nếu sau này muốn remove/update
// 3. Popup
    var name = (p.name && String(p.name).length > 0) ? String(p.name) : 'POI';
          var viText = (p.viText && String(p.viText).length > 0)
            ? String(p.viText)
            : ((p.description && String(p.description).length > 0) ? String(p.description) : 'Đang cập nhật nội dung thuyết minh.');
          var enText = (p.enText && String(p.enText).length > 0) ? String(p.enText) : viText;
          var zhText = (p.zhText && String(p.zhText).length > 0) ? String(p.zhText) : viText;
          var jaText = (p.jaText && String(p.jaText).length > 0) ? String(p.jaText) : viText;
          var imgFile = (p.img && String(p.img).length > 0) ? String(p.img) : '';
          var photoSrc = '';
          if (imgFile) {{
            var u = imgFile.trim().toLowerCase();
            if (u.indexOf('http://') === 0 || u.indexOf('https://') === 0 || u.indexOf('//') === 0)
              photoSrc = imgFile;
            else
              photoSrc = 'file:///android_asset/' + esc(imgFile);
          }}
          var mapHref = (p.mapUrl && String(p.mapUrl).length > 0) ? String(p.mapUrl) : ('https://www.google.com/maps?q=' + p.lat + ',' + p.lng);
          var payLine = '';
          if (typeof p.premiumPrice === 'number' && p.premiumPrice > 0) {{
            payLine = '<div class=""poi-desc""><span class=""poi-label"">Trả phí</span>Thuyết minh (demo): <b>'
              + String(Math.round(p.premiumPrice))
              + ' đ</b>. Nghe trong app sẽ hỏi trả phí; hoặc bấm dưới để trả bằng Zalo/trình duyệt.</div>'
              + '<div class=""poi-desc""><a class=""poi-btn secondary"" href=""app://open-listen-pay?id=' + p.id + '"">Trả phí (Zalo / trình duyệt)</a></div>';
          }}
          
          var popupHtml = 
              `<div class='poi-popup'>`
              + `<div class='poi-title'>${{name}}</div>`
              + `${{photoSrc ? `<img class='poi-photo' src='${{photoSrc}}' onerror=""this.style.display='none'"" />` : ''}}`
              + `<div class='poi-desc'><a class='poi-btn secondary' href='`
              + ('app://map?u=' + encodeURIComponent(mapHref))
              + `'>🗺 Bản đồ ngoài</a></div>`
              + `<div class='poi-desc'><span class='poi-label'>VI</span>${{esc(viText)}}</div>`
              + `<div class='poi-desc'><span class='poi-label'>EN</span>${{esc(enText)}}</div>`
              + `<div class='poi-desc'><span class='poi-label'>ZH</span>${{esc(zhText)}}</div>`
              + `<div class='poi-desc'><span class='poi-label'>JA</span>${{esc(jaText)}}</div>`
              + payLine
              + `<div class='poi-actions'>`
              + `<a class='poi-btn' href='app://directions?id=${{p.id}}&idx=${{p.idx}}' style='background:#0D47A1;color:#fff'>🧭 Chỉ đường</a>`
              + `<a class='poi-btn' href='app://speak-vi?id=${{p.id}}&idx=${{p.idx}}'>Nghe VN</a>`
              + `<a class='poi-btn' href='app://speak-zh?id=${{p.id}}&idx=${{p.idx}}'>听 ZH</a>`
              + `<a class='poi-btn secondary' href='app://speak-en?id=${{p.id}}&idx=${{p.idx}}'>Listen EN</a>`
              + `<a class='poi-btn secondary' href='app://speak-ja?id=${{p.id}}&idx=${{p.idx}}'>聞く JA</a>`
              + `</div>`
              + `</div>`;

    // Bind popup vào marker
    marker.bindPopup(popupHtml);
    circle.bindPopup(popupHtml);
}}

        if (pois.length === 0) {{
          setDbg('No POIs');
        }} else {{
          window.setNearestPoiHighlight(0);
        }}

        if (Array.isArray(routePoints) && routePoints.length > 0) {{
          window.renderRoutePath(routePoints);
        }}
      }} catch (e) {{
        var msg = (e && e.message) ? e.message : String(e);
        setDbg('JS crash: ' + msg);
      }}
    }})();
  </script>
</body>
</html>";

        mapView.Source = new HtmlWebViewSource { Html = html };
        AttachMapNavigatedHandler(hasCurrentLocation, centerLat, centerLng);
    }

    private void AttachMapNavigatedHandler(bool hasCurrentLocation, double centerLat, double centerLng)
    {
        if (_mapNavigatedHandler is not null)
        {
            mapView.Navigated -= _mapNavigatedHandler;
            _mapNavigatedHandler = null;
        }

        _mapNavigatedHandler = async (_, _) =>
        {
            await Task.Delay(450); // đợi map ổn định (ngắn hơn để test / bấm POI cảm giác nhanh hơn)

            if (!_hasSimulationPosition)
            {
                _simulatedLat = hasCurrentLocation ? centerLat : DemoVinhKhanhLat;
                _simulatedLng = hasCurrentLocation ? centerLng : DemoVinhKhanhLng;
                _hasSimulationPosition = true;
            }

            try
            {
                await SyncUserMarkerPositionOnMapAsync(panToMarker: true);
                await TrackRoutePointAsync("init");
                await CheckProximityAndSpeakAsync();
            }
            catch
            {
                // Không để lỗi sync marker làm văng app.
            }
        };
        mapView.Navigated += _mapNavigatedHandler;
    }

    private static string BuildOfflineMapHtml(string poiJsArray, string hintBody)
    {
        var safeHint = System.Net.WebUtility.HtmlEncode(hintBody ?? string.Empty);
        return $@"
<!DOCTYPE html>
<html>
<head>
  <meta name='viewport' content='width=device-width, initial-scale=1.0'>
  <style>
    html, body {{ margin:0; padding:0; background:#f6f8fb; font-family:Arial,Helvetica,sans-serif; color:#1f2937; }}
    .wrap {{ padding:14px; }}
    .title {{ font-size:18px; font-weight:700; color:#0d47a1; margin-bottom:6px; }}
    .hint {{ font-size:13px; color:#4b5563; margin-bottom:10px; }}
    .card {{ background:#fff; border:1px solid #dbe3ef; border-radius:10px; padding:10px; margin-bottom:10px; }}
    .name {{ font-size:15px; font-weight:700; margin-bottom:6px; }}
    .desc {{ font-size:12px; color:#4b5563; margin-bottom:8px; }}
    .row a {{ display:inline-block; margin:2px 6px 2px 0; padding:6px 10px; border-radius:8px; text-decoration:none; font-size:12px; background:#1976d2; color:#fff; }}
    .row a.secondary {{ background:#6b7280; }}
  </style>
</head>
<body>
  <div class='wrap'>
    <div class='title'>Bản đồ offline tạm thời</div>
    <div class='hint'>{safeHint}</div>
    <div id='list'></div>
  </div>
  <script>
    (function(){{
      var pois = [{poiJsArray}];
      var host = document.getElementById('list');
      function esc(t) {{
        return String(t ?? '')
          .split('&').join('&amp;')
          .split('<').join('&lt;')
          .split('>').join('&gt;');
      }}
      var html = '';
      for (var i = 0; i < pois.length; i++) {{
        var p = pois[i];
        if (!p || !p.id) continue;
        html += ""<div class='card'>""
          + ""<div class='name'>"" + esc(p.name || ('POI #' + p.id)) + ""</div>""
          + ""<div class='desc'>Mở mạng lại để thấy nền bản đồ chi tiết.</div>""
          + ""<div class='row'>""
          + ""<a href='app://speak-vi?id="" + p.id + ""&idx="" + p.idx + ""'>Nghe VI</a>""
          + ""<a href='app://speak-en?id="" + p.id + ""&idx="" + p.idx + ""' class='secondary'>EN</a>""
          + ""<a href='app://speak-zh?id="" + p.id + ""&idx="" + p.idx + ""' class='secondary'>ZH</a>""
          + ""<a href='app://speak-ja?id="" + p.id + ""&idx="" + p.idx + ""' class='secondary'>JA</a>""
          + ""</div></div>"";
      }}
      host.innerHTML = html || ""<div class='card'>Chưa có POI để hiển thị.</div>"";

      window.openPoiById = function(){{}};
      window.setUserMarkerPosition = function(){{}};
      window.setNearestPoiHighlight = function(){{}};
      window.updateGpsDebug = function(){{}};
      window.showNavigationGuideTo = function(){{}};
      window.clearNavigationGuide = function(){{}};
      window.clearRoutePath = function(){{}};
      window.showRoutePathFromBase64 = function(){{}};
      window.showNavigationPolylineFromBase64 = function(){{}};
    }})();
  </script>
</body>
</html>";
    }

    // ── Event handlers cho nút di chuyển ──
    private async void OnMoveUpClicked(object? sender, EventArgs e)
        => await MoveSimulation(MOVE_STEP_METERS, 0);

    private async void OnMoveDownClicked(object? sender, EventArgs e)
        => await MoveSimulation(-MOVE_STEP_METERS, 0);

    private async void OnMoveLeftClicked(object? sender, EventArgs e)
        => await MoveSimulation(0, -MOVE_STEP_METERS);

    private async void OnMoveRightClicked(object? sender, EventArgs e)
        => await MoveSimulation(0, MOVE_STEP_METERS);

    /// <summary>Nhảy nhanh về tọa độ demo Vĩnh Khánh (không cần mở QR / xe buýt).</summary>
    private async void OnJumpToVinhKhanhDemoClicked(object? sender, EventArgs e)
    {
        // Không tạm chặn GPS: nếu không, 60s sau nút này Fake GPS / GPS thật sẽ không cập nhật marker.
        await MoveSimulatedMarkerToAsync(DemoVinhKhanhLat, DemoVinhKhanhLng, runProximityCheck: true, pauseGpsAfterMove: false);
    }

    private async void OnClearRouteClicked(object? sender, EventArgs e)
    {
        try
        {
            await RouteTrackService.ClearAsync();
            ClearFootNavTurnState();
            await mapView.EvaluateJavaScriptAsync("(window.clearNavigationGuide&&window.clearNavigationGuide());(window.clearRoutePath&&window.clearRoutePath());");
            await DisplayAlertAsync("Đã xóa tuyến", "Đã xóa toàn bộ dữ liệu tuyến di chuyển.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi", $"Không thể xóa tuyến.\n{ex.Message}", "OK");
        }
    }

    private async void OnResetDemoStateClicked(object? sender, EventArgs e)
    {
        try
        {
            CancelProximitySpeech();
            CancelBusStopSpeech();

            _activeProximityPoiIndex = -1;
            _activeBusStopToken = null;
            _autoGeoNextAllowedPlayUtcByPoi.Clear();
            _autoGeoLastLogByPoi.Clear();
            _lastQueuedGpsLat = double.NaN;
            _lastQueuedGpsLng = double.NaN;
            _lastAcceptedGpsUtc = null;

            UpdateGeoStatusLabel("Đã reset demo - ngoài vùng POI");
            UpdateCooldownLabel(-1);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                lblLastPlayedStatus.Text = "🔊 Đã phát gần nhất: -";
            });

            await mapView.EvaluateJavaScriptAsync("window.setNearestPoiHighlight && window.setNearestPoiHighlight(-1);");
            await DisplayAlertAsync("Reset demo", "Đã reset trạng thái geofence/cooldown để test lại từ đầu.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi reset", $"Không thể reset trạng thái demo.\n{ex.Message}", "OK");
        }
    }

    private async void OnRefreshPoisClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _isRefreshingPois, 1) == 1)
            return;

        try
        {
            UpdateGeoStatusLabel("Đang cập nhật POI từ server...");
            await SafeLoadMapAsync();
            PremiumPaymentService.ClearShortLivedEntitlementMemory();
            UpdateGeoStatusLabel("Đã cập nhật POI.");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi cập nhật POI", ex.Message, "OK");
        }
        finally
        {
            Interlocked.Exchange(ref _isRefreshingPois, 0);
        }
    }

    private void PopulateQrPicker()
    {
        pickerPoiForQr.Items.Clear();
        foreach (var p in _pois)
        {
            pickerPoiForQr.Items.Add(p.Name);
        }

        PopulateSimulatePoiPicker();
    }

    /// <summary>
    /// Trong phòng không có GPS/mock ổn định: đặt marker đúng tọa độ quán trong DB (không phải mũi tên lưới ô).
    /// </summary>
    void PopulateSimulatePoiPicker()
    {
        _suppressSimulatePoiPickerEvent = true;
        try
        {
            pickerSimulateNearPoi.Items.Clear();
            pickerSimulateNearPoi.Items.Add("-- Chọn quán --");
            foreach (var p in _pois)
            {
                if (p is not null)
                    pickerSimulateNearPoi.Items.Add(p.Name);
            }

            pickerSimulateNearPoi.SelectedIndex = 0;
        }
        finally
        {
            _suppressSimulatePoiPickerEvent = false;
        }
    }

    void OnSimulateNearPoiPickerChanged(object? sender, EventArgs e)
    {
        if (_suppressSimulatePoiPickerEvent)
            return;
        if (pickerSimulateNearPoi.SelectedIndex <= 0)
            return;

        var poiIndex = pickerSimulateNearPoi.SelectedIndex - 1;
        if (poiIndex < 0 || poiIndex >= _pois.Count)
            return;

        var place = _pois[poiIndex];
        if (place is null)
            return;

        _ = MoveSimulatedMarkerToAsync(place.Latitude, place.Longitude, runProximityCheck: true, pauseGpsAfterMove: false);
    }

    /// <summary>Bấm nút QR xanh: mở/đóng khối QR; nội dung luôn là mã QR từng điểm (chọn được trong Picker).</summary>
    private void OnQrToggleClicked(object? sender, EventArgs e)
    {
        if (qrNearbyPanel.IsVisible)
        {
            qrNearbyPanel.IsVisible = false;
            btnQrToggle.Text = "QR ▼";
            return;
        }

        qrNearbyPanel.IsVisible = true;
        btnQrToggle.Text = "QR ▲";
        ApplyDefaultSelectionForQrPanel();
    }

    private void ApplyDefaultSelectionForQrPanel()
    {
        if (_pois.Count == 0)
        {
            lblQrHint.Text = "Chưa có điểm thuyết minh.";
            return;
        }

        // QR hoạt động độc lập GPS: luôn chọn theo danh sách POI hoặc id trong mã QR.
        var index = pickerPoiForQr.SelectedIndex >= 0 ? pickerPoiForQr.SelectedIndex : 0;
        lblQrHint.Text = "QR này không cần GPS: khách quét mã sẽ mở đúng điểm theo poiId trong QR.";

        _suppressQrPickerEvent = true;
        try
        {
            pickerPoiForQr.SelectedIndex = index;
        }
        finally
        {
            _suppressQrPickerEvent = false;
        }

        UpdateQrPanelContent(index);
    }

    private void OnPoiQrPickerChanged(object? sender, EventArgs e)
    {
        if (_suppressQrPickerEvent) return;
        if (pickerPoiForQr.SelectedIndex < 0) return;
        UpdateQrPanelContent(pickerPoiForQr.SelectedIndex);
    }

    private void OnSelectKhanhHoiQrClicked(object? sender, EventArgs e) => _ = RunBusStopSelectionAsync("KHANH_HOI");
    private void OnSelectVinhHoiQrClicked(object? sender, EventArgs e) => _ = RunBusStopSelectionAsync("VINH_HOI");
    private void OnSelectXomChieuQrClicked(object? sender, EventArgs e) => _ = RunBusStopSelectionAsync("XOM_CHIEU");

    /// <summary>
    /// Xử lý tuần tự: nhảy marker → (không kích proximity trùng) → hủy TTS cũ → phát đúng tuyến mới.
    /// Trước đây Move + Speak chạy song song nên proximity/TTS cũ làm nghe nhầm “tuyến cũ”.
    /// </summary>
    private async Task RunBusStopSelectionAsync(string token)
    {
        if (!TryResolveBusStopPoiIndex(token, out var poiIndex))
        {
            if (!TryResolvePoiIdFromQr(token, out var poiId))
                return;
            if (!_poiIndexById.TryGetValue(poiId, out poiIndex))
                return;
        }

        _suppressQrPickerEvent = true;
        try
        {
            pickerPoiForQr.SelectedIndex = poiIndex;
        }
        finally
        {
            _suppressQrPickerEvent = false;
        }

        UpdateQrPanelContent(poiIndex);

        if (TryGetBusStopCoordinates(token, out var lat, out var lng))
            await MoveSimulatedMarkerToAsync(lat, lng, runProximityCheck: false);

        await SpeakPoiImmediatelyFromBusStopAsync(token, poiIndex);
    }

    /// <summary>Chỉ định POI theo đúng 3 mã điểm dừng (không dùng Contains để tránh nhầm).</summary>
    private static bool TryResolveBusStopPoiIndex(string token, out int poiIndex)
    {
        poiIndex = -1;
        var n = NormalizeQrToken(token);
        switch (n)
        {
            case "KHANHHOI":
                poiIndex = 4;
                return true;
            case "VINHHOI":
                poiIndex = 0;
                return true;
            case "XOMCHIEU":
                poiIndex = 1;
                return true;
            default:
                return false;
        }
    }

    private static string GetBusStopDisplayName(string token)
    {
        var normalized = NormalizeQrToken(token);
        return normalized switch
        {
            "KHANHHOI" => "Khánh Hội",
            "VINHHOI" => "Vĩnh Hội",
            "XOMCHIEU" => "Xóm Chiếu",
            _ => "gần đây"
        };
    }

    /// <summary>
    /// Trên tuyến xe buýt chỉ cần giới thiệu món/đặc điểm quán; câu dạng "Bạn đang ở khu vực…" / "You are now…"
    /// dành cho khi khách thật sự vào vùng POI (AutoGeo), không lặp khi đang ở trạm.
    /// </summary>
    private static string StripArrivalFramingForBusRouteContext(string? text, string lang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        var t = text.Trim();
        var startsArrival = false;
        switch (lang)
        {
            case "en":
                startsArrival = t.StartsWith("You are now ", StringComparison.OrdinalIgnoreCase)
                    || t.StartsWith("You are currently", StringComparison.OrdinalIgnoreCase);
                break;
            case "zh":
                startsArrival = t.StartsWith("您正在");
                break;
            case "ja":
                startsArrival = t.StartsWith("あなたは今、");
                break;
            default:
                startsArrival = t.StartsWith("Bạn đang ở", StringComparison.OrdinalIgnoreCase);
                break;
        }

        if (!startsArrival) return text;

        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] == '.' || t[i] == '。')
            {
                var rest = t[(i + 1)..].TrimStart();
                return string.IsNullOrWhiteSpace(rest) ? text : rest;
            }
        }

        return text;
    }

    private async Task SpeakPoiImmediatelyFromBusStopAsync(string token, int poiIndex)
    {
        if (poiIndex < 0 || poiIndex >= _pois.Count) return;
        var place = _pois[poiIndex];
        if (place is null) return;
        var busStopName = GetBusStopDisplayName(token);
        var lang = string.IsNullOrEmpty(_selectedLanguage) ? "vi" : _selectedLanguage;

        var viMain = PickNarrationText(place, "vi");
        var enMain = PickNarrationText(place, "en");
        var zhMain = PickNarrationText(place, "zh");
        var jaMain = PickNarrationText(place, "ja");

        var text = lang switch
        {
            "en" =>
                $"Along the {busStopName} bus route, near {place.Name}. {StripArrivalFramingForBusRouteContext(enMain, "en").TrimStart()}",
            "zh" =>
                $"途经{busStopName}公交线，靠近{place.Name}。{StripArrivalFramingForBusRouteContext(zhMain, "zh").TrimStart()}",
            "ja" =>
                $"{busStopName}のバス路線沿い、{place.Name}の近く。{StripArrivalFramingForBusRouteContext(jaMain, "ja").TrimStart()}",
            _ =>
                $"Trên tuyến xe buýt {busStopName}, gần khu ẩm thực {place.Name}. {StripArrivalFramingForBusRouteContext(viMain, "vi").TrimStart()}"
        };
        if (string.IsNullOrWhiteSpace(text)) return;

        if (!await EnsurePoiListenPaidAsync(place))
            return;

        CancelProximitySpeech();
        CancelBusStopSpeech();
        _busStopTtsCts?.Dispose();
        _busStopTtsCts = new CancellationTokenSource();
        var cts = _busStopTtsCts;
        var ct = cts.Token;

        try
        {
            var durationSeconds = await NarrationQueueService.EnqueuePoiOrTtsAsync(
                poiIndex, _selectedLanguage, text, ct, place.Id > 0 ? place.Id : null);
            UpdateLastPlayedLabel(place.Name, "BusStop");
            await HistoryLogService.AddAsync(place.Name, "BusStop", _selectedLanguage, durationSeconds);
        }
        catch (OperationCanceledException)
        {
            // Đã bấm điểm dừng khác — bỏ log.
        }
        finally
        {
            if (ReferenceEquals(_busStopTtsCts, cts))
            {
                _busStopTtsCts?.Dispose();
                _busStopTtsCts = null;
            }
            else
            {
                cts.Dispose();
            }
        }
    }

    void CancelBusStopSpeech()
    {
        try
        {
            _busStopTtsCts?.Cancel();
            NarrationQueueService.StopActivePlayer();
        }
        catch
        {
            // Bỏ qua.
        }
    }

    private static bool TryGetBusStopCoordinates(string token, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        var normalized = NormalizeQrToken(token);
        switch (normalized)
        {
            case "KHANHHOI":
                lat = 10.7597; lng = 106.7050; return true;
            case "VINHHOI":
                lat = 10.7586; lng = 106.7036; return true;
            case "XOMCHIEU":
                lat = 10.7603; lng = 106.7026; return true;
            default:
                return false;
        }
    }

    private static bool TryGetNearestBusStopInRange(double lat, double lng, double enterMeters, out string token, out int poiIndex)
    {
        token = string.Empty;
        poiIndex = -1;
        var nearestDistance = double.MaxValue;

        var stops = new[]
        {
            "KHANH_HOI",
            "VINH_HOI",
            "XOM_CHIEU"
        };

        foreach (var stop in stops)
        {
            if (!TryGetBusStopCoordinates(stop, out var sLat, out var sLng))
                continue;

            var d = CalculateDistanceStatic(lat, lng, sLat, sLng);
            if (d <= enterMeters && d < nearestDistance && TryResolveBusStopPoiIndex(stop, out var mappedPoi))
            {
                nearestDistance = d;
                token = stop;
                poiIndex = mappedPoi;
            }
        }

        return poiIndex >= 0;
    }

    private static double CalculateDistanceStatic(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000;
        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }

    private async Task MoveSimulatedMarkerToAsync(double lat, double lng, bool runProximityCheck = true, bool pauseGpsAfterMove = true)
    {
        _simulatedLat = lat;
        _simulatedLng = lng;
        if (pauseGpsAfterMove)
            PauseGpsForManualDemo();

        var js = $@"
        if (window.userMarker && typeof window.userMarker.setLatLng === 'function') {{
            window.userMarker.setLatLng([{lat.ToString(CultureInfo.InvariantCulture)}, {lng.ToString(CultureInfo.InvariantCulture)}]);
            if (window.userMarker.openPopup) {{
                window.userMarker.openPopup();
            }}
        }}
        if (window.appMap && typeof window.appMap.panTo === 'function') {{
            window.appMap.panTo([{lat.ToString(CultureInfo.InvariantCulture)}, {lng.ToString(CultureInfo.InvariantCulture)}]);
        }}
        ";

        for (var i = 0; i < 4; i++)
        {
            try
            {
                await mapView.EvaluateJavaScriptAsync(js);
                await TrackRoutePointAsync("jump");
                if (runProximityCheck)
                {
                    await CheckProximityAndSpeakAsync();
                    await MaybeAnnounceFootNavCueAsync();
                }

                return;
            }
            catch
            {
                await Task.Delay(250);
            }
        }
    }

    private void UpdateQrPanelContent(int poiIndex)
    {
        if (poiIndex < 0 || poiIndex >= _pois.Count) return;

        var place = _pois[poiIndex];
        var payload = PlaceApiService.GetListenPayUrlForPlace(place.Id);
        if (string.IsNullOrWhiteSpace(payload))
            payload = $"app://poi?id={place.Id}";

        lblQrNearbyTitle.Text = place.Name;
        lblNearbyPayload.Text = payload;
        imgNearbyQr.Source = GenerateQrImage(payload);
    }

    private async Task OpenScannerWithPermissionAsync()
    {
        var cameraPermission = await Permissions.RequestAsync<Permissions.Camera>();
        if (cameraPermission != PermissionStatus.Granted)
        {
            await DisplayAlertAsync("Không thể quét QR", "Bạn cần cấp quyền camera để quét mã QR.", "OK");
            return;
        }

        try
        {
            await Navigation.PushModalAsync(new QrScannerPage(OnQrScanned));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi mở QR", $"Không thể mở camera quét QR trên thiết bị này.\n{ex.Message}", "OK");
        }
    }

    private ImageSource GenerateQrImage(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        var bytes = qr.GetGraphic(8);
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    /// <summary>Mở QR phóng to nền trắng — khách dùng điện thoại khác quét màn hình này.</summary>
    private async void OnOpenGuestQrFullscreenClicked(object? sender, EventArgs e)
    {
        if (pickerPoiForQr.SelectedIndex < 0 || pickerPoiForQr.SelectedIndex >= _pois.Count)
        {
            await DisplayAlertAsync("Chưa chọn điểm", "Chọn quán trong danh sách trước khi hiển thị QR cho khách.", "OK");
            return;
        }

        var idx = pickerPoiForQr.SelectedIndex;
        var place = _pois[idx];
        var payload = PlaceApiService.GetListenPayUrlForPlace(place.Id);
        if (string.IsNullOrWhiteSpace(payload))
            payload = $"app://poi?id={place.Id}";

        try
        {
            await Navigation.PushModalAsync(new QrGuestFullscreenPage(place.Name, payload));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi", $"Không mở được màn hình QR.\n{ex.Message}", "OK");
        }
    }

    private async void OnOpenScannerFromPanelClicked(object? sender, EventArgs e)
    {
        await OpenScannerWithPermissionAsync();
    }

    private void OnCloseNearbyQrPanelClicked(object? sender, EventArgs e)
    {
        qrNearbyPanel.IsVisible = false;
        btnQrToggle.Text = "QR ▼";
    }

    private void OnQrScanned(string rawValue)
    {
        _ = ProcessQrScanSafeAsync(rawValue);
    }

    private async Task ProcessQrScanSafeAsync(string rawValue)
    {
        try
        {
            await HandleQrScanAsync(rawValue);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ProcessQrScanSafeAsync: {ex}");
            try
            {
                await DisplayAlertAsync("Lỗi xử lý mã QR", ex.Message, "OK");
            }
            catch
            {
                // Bỏ qua nếu UI không còn hợp lệ.
            }
        }
    }

    private async Task HandleQrScanAsync(string rawValue)
    {
        PlaceApiService.TryLearnPublicSyncOriginFromRawUrl(rawValue);

        if (!TryResolvePoiIdFromQr(rawValue, out var poiId))
        {
            await DisplayAlertAsync("QR không hợp lệ", "Không tìm thấy POI từ mã QR này.", "OK");
            return;
        }

        if (!_poiIndexById.TryGetValue(poiId, out var poiIndex) || poiIndex < 0 || poiIndex >= _pois.Count)
        {
            if (!await TryEnsurePoiLoadedFromApiAsync(poiId))
            {
                await DisplayAlertAsync(
                    "POI không tồn tại",
                    "Mã QR đã quét không có trong dữ liệu hiện tại. Hãy có mạng tới CMS, bấm Cập nhật POI, rồi quét lại.",
                    "OK");
                return;
            }

            poiIndex = _poiIndexById[poiId];
        }

        var place = _pois[poiIndex];
        if (!await EnsurePoiListenPaidAsync(place))
            return;

        var (text, ttsLang) = ResolveNarrationForPlayback(place, _selectedLanguage);

        if (!string.IsNullOrWhiteSpace(text))
        {
            BeginManualNarrationOverride();
            CancelProximitySpeech();
            CancelBusStopSpeech();
            try
            {
                var durationSeconds = await NarrationQueueService.EnqueuePoiOrTtsAsync(
                    poiIndex, ttsLang, text, default, place.Id > 0 ? place.Id : null);
                UpdateLastPlayedLabel(place.Name, "QR");
                await HistoryLogService.AddAsync(place.Name, "QR", ttsLang, durationSeconds);
            }
            catch (OperationCanceledException)
            {
                // Đã hủy thuyết minh — bỏ ghi log.
            }
        }
        else
        {
            await HistoryLogService.AddAsync(place.Name, "QR", _selectedLanguage);
        }

        try
        {
            await mapView.EvaluateJavaScriptAsync($"window.openPoiById && window.openPoiById({poiId});");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"openPoiById JS: {ex}");
        }
        // Theo yêu cầu "quét là nghe ngay", không chặn luồng bằng popup success.
    }

    private bool TryResolvePoiIdFromQr(string rawValue, out int poiId)
    {
        poiId = -1;
        if (string.IsNullOrWhiteSpace(rawValue)) return false;

        var value = rawValue.Trim();
        if (value.Contains("Listen/Pay", StringComparison.OrdinalIgnoreCase))
        {
            var lm = Regex.Match(value, @"[?&]placeId=(\d+)", RegexOptions.IgnoreCase);
            if (lm.Success && int.TryParse(lm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var listenPid))
            {
                poiId = listenPid;
                return true;
            }
        }

        var normalized = NormalizeQrToken(value);

        // ===== Mục 8 trong yêu cầu: QR tại điểm dừng xe buýt, không cần GPS =====
        // Khớp chính xác trước (tránh nhầm khi chuỗi dài chứa nhiều từ khóa).
        if (normalized is "KHANHHOI" or "VINHHOI" or "XOMCHIEU")
        {
            poiId = normalized switch
            {
                "KHANHHOI" => _pois.Count > 4 ? _pois[4].Id : 4,
                "VINHHOI" => _pois.Count > 0 ? _pois[0].Id : 0,
                "XOMCHIEU" => _pois.Count > 1 ? _pois[1].Id : 1,
                _ => -1,
            };
            return true;
        }

        // Chuỗi QR có tiền tố/hậu tố (vd. STOP_KHANHHOI_V1)
        if (normalized.Contains("KHANHHOI"))
        {
            poiId = _pois.Count > 4 ? _pois[4].Id : 4;
            return true;
        }
        if (normalized.Contains("VINHHOI"))
        {
            poiId = _pois.Count > 0 ? _pois[0].Id : 0;
            return true;
        }
        if (normalized.Contains("XOMCHIEU"))
        {
            poiId = _pois.Count > 1 ? _pois[1].Id : 1;
            return true;
        }

        // Hỗ trợ trường hợp mã chỉ chứa số thứ tự POI.
        if (int.TryParse(value, out var directIndex))
        {
            poiId = directIndex;
            return true;
        }

        // Hỗ trợ deep link: tourguide://poi?id=2 hoặc app://poi?id=2
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var query = uri.Query.TrimStart('?');
            if (!string.IsNullOrWhiteSpace(query))
            {
                var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length != 2) continue;
                    var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
                    var val = Uri.UnescapeDataString(kv[1]);
                    if ((key == "id" || key == "poiid" || key == "poi") && int.TryParse(val, out var parsed))
                    {
                        poiId = parsed;
                        return true;
                    }
                }
            }
        }

        // Một số thiết bị/scanner trả về app://poi?id=n mà Uri.Query rỗng — bắt bằng regex
        var idMatch = Regex.Match(value, @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
        if (idMatch.Success && int.TryParse(idMatch.Groups[1].Value, out var idFromRegex))
        {
            poiId = idFromRegex;
            return true;
        }

        // Hỗ trợ JSON: {"poiId":2} hoặc {"id":2}
        try
        {
            using var doc = JsonDocument.Parse(value);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("stop", out var stopProp))
                {
                    var stopValue = NormalizeQrToken(stopProp.GetString() ?? string.Empty);
                    if (stopValue.Contains("KHANHHOI")) { poiId = _pois.Count > 4 ? _pois[4].Id : 4; return true; }
                    if (stopValue.Contains("VINHHOI")) { poiId = _pois.Count > 0 ? _pois[0].Id : 0; return true; }
                    if (stopValue.Contains("XOMCHIEU")) { poiId = _pois.Count > 1 ? _pois[1].Id : 1; return true; }
                }

                if (root.TryGetProperty("ward", out var wardProp))
                {
                    var wardValue = NormalizeQrToken(wardProp.GetString() ?? string.Empty);
                    if (wardValue.Contains("KHANHHOI")) { poiId = _pois.Count > 4 ? _pois[4].Id : 4; return true; }
                    if (wardValue.Contains("VINHHOI")) { poiId = _pois.Count > 0 ? _pois[0].Id : 0; return true; }
                    if (wardValue.Contains("XOMCHIEU")) { poiId = _pois.Count > 1 ? _pois[1].Id : 1; return true; }
                }

                if (root.TryGetProperty("poiId", out var poiIdProp) && poiIdProp.TryGetInt32(out var jsonPoiId))
                {
                    poiId = jsonPoiId;
                    return true;
                }

                if (root.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var jsonId))
                {
                    poiId = jsonId;
                    return true;
                }
            }
        }
        catch
        {
            // Bỏ qua nếu không phải JSON.
        }

        // Fallback: tìm số đầu tiên trong chuỗi kiểu "POI-3"
        var match = Regex.Match(value, @"\d+");
        if (match.Success && int.TryParse(match.Value, out var extracted))
        {
            poiId = extracted;
            return true;
        }

        return false;
    }

    private static string NormalizeQrToken(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb
            .ToString()
            .ToUpperInvariant()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty);
    }

    private async Task MoveSimulation(double deltaLatM, double deltaLngM)
    {
        const double metersPerLatDeg = 111000;
        double metersPerLngDeg = 111000 * Math.Cos(_simulatedLat * Math.PI / 180);

        double deltaLat = deltaLatM / metersPerLatDeg;
        double deltaLng = deltaLngM / metersPerLngDeg;

        _simulatedLat += deltaLat;
        _simulatedLng += deltaLng;

        try
        {
            // Pan theo chấm đen khi demo bằng mũi tên — trước đây pan=false nên map đứng yên,
            // phải bật QR/chọn điểm mới thấy “bay” tới quán.
            await SyncUserMarkerPositionOnMapAsync(panToMarker: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MoveSimulation JS error: {ex.Message}");
        }

        PauseGpsForManualDemo();
        await TrackRoutePointAsync("arrow");
        await CheckProximityAndSpeakAsync();
        await MaybeAnnounceFootNavCueAsync();
    }

    void PauseGpsForManualDemo()
    {
        _gpsManualOverrideUntilUtc = DateTime.UtcNow.AddSeconds(GpsManualOverrideSeconds);
        _lastQueuedGpsLat = double.NaN;
        _lastQueuedGpsLng = double.NaN;
    }

    private async Task CheckProximityAndSpeakAsync()
    {
        if (_manualNarrationOverrideUntilUtc is DateTime manualOverrideUntil
            && DateTime.UtcNow < manualOverrideUntil)
            return;

        string? textToSpeak = null;
        var speakTtsLang = _selectedLanguage;
        int speakPoiIndex = -1;
        Place? speakPlace = null;
        CancellationTokenSource? speakCts = null;
        int nearestPoiForHighlight = -1;
        string? busStopToSpeak = null;
        int busStopPoiIndex = -1;
        var inBusStopZone = false;
        var nearStopPoiForHighlight = -1;

        await _proximityCheckGate.WaitAsync();
        try
        {
            if (TryGetNearestBusStopInRange(_simulatedLat, _simulatedLng, BusStopEnterMeters, out var nearStopToken, out var nearStopPoi))
            {
                inBusStopZone = true;
                nearStopPoiForHighlight = nearStopPoi;
                UpdateGeoStatusLabel($"Trong vùng trạm {GetBusStopDisplayName(nearStopToken)}");
                if (!string.Equals(_activeBusStopToken, nearStopToken, StringComparison.Ordinal))
                {
                    _activeBusStopToken = nearStopToken;
                    busStopToSpeak = nearStopToken;
                    busStopPoiIndex = nearStopPoi;
                }
            }
            else if (!string.IsNullOrWhiteSpace(_activeBusStopToken))
            {
                if (TryGetBusStopCoordinates(_activeBusStopToken!, out var activeStopLat, out var activeStopLng))
                {
                    var distToActiveStop = CalculateDistance(_simulatedLat, _simulatedLng, activeStopLat, activeStopLng);
                    if (distToActiveStop > BusStopExitMeters)
                        _activeBusStopToken = null;
                }
                else
                {
                    _activeBusStopToken = null;
                }
            }

            if (_activeProximityPoiIndex >= 0 && _activeProximityPoiIndex < _pois.Count)
            {
                var activePlace = _pois[_activeProximityPoiIndex];
                if (activePlace is not null)
                {
                var distToActive = CalculateDistance(_simulatedLat, _simulatedLng, activePlace.Latitude, activePlace.Longitude);
                if (distToActive > GetExitRadiusMeters(activePlace))
                    {
                        CancelProximitySpeech();
                        _activeProximityPoiIndex = -1;
                        UpdateGeoStatusLabel("Ngoài vùng POI");
                        UpdateCooldownLabel(-1);
                    }
                    else
                    {
                        UpdateGeoStatusLabel($"Đang trong vùng: {activePlace.Name}");
                    }
                }
                else
                {
                    _activeProximityPoiIndex = -1;
                    UpdateGeoStatusLabel("Ngoài vùng POI");
                }
            }

            // Trong vùng trạm xe buýt: ưu tiên thuyết minh trạm, không để AutoGeo POI (vd. quán ốc) chặn hoặc trùng.
            if (inBusStopZone)
            {
                nearestPoiForHighlight = nearStopPoiForHighlight >= 0 ? nearStopPoiForHighlight : -1;
                CancelProximitySpeech();
                _activeProximityPoiIndex = -1;
                if (string.IsNullOrWhiteSpace(busStopToSpeak))
                {
                    _ = UpdateNearestPoiHighlightAsync(nearestPoiForHighlight);
                    return;
                }
            }
            else if (_activeProximityPoiIndex >= 0)
            {
                nearestPoiForHighlight = _activeProximityPoiIndex;
                _ = UpdateNearestPoiHighlightAsync(nearestPoiForHighlight);
                return;
            }

            if (!inBusStopZone)
            {
            var nearestIndex = -1;
            var nearestDistance = double.MaxValue;
            var secondNearestDistance = double.MaxValue;
            var candidateIndex = -1;
            var candidateDistance = double.MaxValue;
            var candidatePriority = int.MinValue;
            var secondCandidateDistance = double.MaxValue;
            var secondCandidatePriority = int.MinValue;

            for (var i = 0; i < _pois.Count; i++)
            {
                var place = _pois[i];
                if (place == null) continue;

                var distance = CalculateDistance(_simulatedLat, _simulatedLng, place.Latitude, place.Longitude);
                if (distance < nearestDistance)
                {
                    secondNearestDistance = nearestDistance;
                    nearestDistance = distance;
                    nearestIndex = i;
                }
                else if (distance < secondNearestDistance)
                {
                    secondNearestDistance = distance;
                }

                if (distance <= GetEnterRadiusMeters(place))
                {
                    if (place.Priority > candidatePriority || (place.Priority == candidatePriority && distance < candidateDistance))
                    {
                        secondCandidateDistance = candidateDistance;
                        secondCandidatePriority = candidatePriority;
                        candidatePriority = place.Priority;
                        candidateDistance = distance;
                        candidateIndex = i;
                    }
                    else if (place.Priority == candidatePriority && distance < secondCandidateDistance)
                    {
                        secondCandidateDistance = distance;
                        secondCandidatePriority = place.Priority;
                    }
                }
            }

            nearestPoiForHighlight = nearestIndex;
            if (nearestIndex < 0)
            {
                _ = UpdateNearestPoiHighlightAsync(nearestPoiForHighlight);
                return;
            }
            if (candidateIndex < 0)
            {
                UpdateGeoStatusLabel("Ngoài vùng POI");
                UpdateCooldownLabel(-1);
                _ = UpdateNearestPoiHighlightAsync(nearestPoiForHighlight);
                return;
            }

            var distanceGap = secondCandidateDistance - candidateDistance;
            var isAmbiguousSamePriority = secondCandidatePriority == candidatePriority
                                          && secondCandidateDistance < double.MaxValue
                                          && distanceGap < AutoGeoMinGapMeters;
            if (isAmbiguousSamePriority)
            {
                _ = UpdateNearestPoiHighlightAsync(nearestPoiForHighlight);
                return;
            }

            var nearestPlace = _pois[candidateIndex];
            if (nearestPlace == null)
            {
                _ = UpdateNearestPoiHighlightAsync(nearestPoiForHighlight);
                return;
            }

            var (text, ttsLang) = ResolveNarrationForPlayback(nearestPlace, _selectedLanguage);

            if (string.IsNullOrWhiteSpace(text))
            {
                UpdateGeoStatusLabel($"Đang trong vùng: {nearestPlace.Name}");
                _ = UpdateNearestPoiHighlightAsync(nearestPoiForHighlight);
                return;
            }

            if (IsAutoGeoPlaybackDebounced(candidateIndex))
            {
                UpdateGeoStatusLabel($"Đang trong vùng: {nearestPlace.Name}");
                UpdateCooldownLabel(candidateIndex);
                nearestPoiForHighlight = nearestIndex;
                _ = UpdateNearestPoiHighlightAsync(nearestPoiForHighlight);
                return;
            }

            _activeProximityPoiIndex = candidateIndex;
            CancelProximitySpeech();
            speakCts = new CancellationTokenSource();
            _proximityTtsCts = speakCts;
            textToSpeak = text;
            speakTtsLang = ttsLang;
            speakPoiIndex = candidateIndex;
            speakPlace = nearestPlace;
            UpdateGeoStatusLabel($"Đang trong vùng: {nearestPlace.Name}");
            }
        }
        finally
        {
            _proximityCheckGate.Release();
        }

        await UpdateNearestPoiHighlightAsync(nearestPoiForHighlight);

        if (!string.IsNullOrWhiteSpace(busStopToSpeak) && busStopPoiIndex >= 0)
        {
            await SpeakPoiImmediatelyFromBusStopAsync(busStopToSpeak, busStopPoiIndex);
            return;
        }

        if (string.IsNullOrWhiteSpace(textToSpeak) || speakCts is null || speakPlace is null)
            return;

        if (!await EnsurePoiListenPaidAsync(speakPlace))
            return;

        var token = speakCts.Token;
        try
        {
            var durationSeconds = await NarrationQueueService.EnqueuePoiOrTtsAsync(
                speakPoiIndex, speakTtsLang, textToSpeak, token, speakPlace.Id > 0 ? speakPlace.Id : null);
            RegisterAutoGeoPlaybackCompleted(speakPoiIndex);
            UpdateLastPlayedLabel(speakPlace.Name, "AutoGeo");
            UpdateCooldownLabel(speakPoiIndex);
            var pushRemote = ShouldLogAutoGeo(speakPoiIndex);
            await HistoryLogService.AddAsync(speakPlace.Name, "AutoGeo", speakTtsLang, durationSeconds, pushRemote);
        }
        catch (OperationCanceledException)
        {
            // Đã rời vùng — hủy TTS là đúng ý.
        }
        finally
        {
            if (ReferenceEquals(_proximityTtsCts, speakCts))
            {
                _proximityTtsCts?.Dispose();
                _proximityTtsCts = null;
            }
            else
            {
                speakCts.Dispose();
            }
        }
    }

    private void CancelProximitySpeech()
    {
        try
        {
            _proximityTtsCts?.Cancel();
            NarrationQueueService.StopActivePlayer();
        }
        catch
        {
            // Bỏ qua.
        }
    }

    private bool ShouldLogAutoGeo(int poiIndex)
    {
        var now = DateTime.Now;
        if (_autoGeoLastLogByPoi.TryGetValue(poiIndex, out var lastTime))
        {
            if ((now - lastTime).TotalSeconds < AutoGeoLogCooldownSeconds)
                return false;
        }

        _autoGeoLastLogByPoi[poiIndex] = now;
        return true;
    }

    private bool IsAutoGeoPlaybackDebounced(int poiIndex)
    {
        if (poiIndex < 0) return false;
        return _autoGeoNextAllowedPlayUtcByPoi.TryGetValue(poiIndex, out var notBefore)
               && DateTime.UtcNow < notBefore;
    }

    private void RegisterAutoGeoPlaybackCompleted(int poiIndex)
    {
        if (poiIndex < 0) return;
        _autoGeoNextAllowedPlayUtcByPoi[poiIndex] = DateTime.UtcNow.AddSeconds(AutoGeoSpeechDebounceSeconds);
    }

    private void UpdateGeoStatusLabel(string status)
    {
        _currentZoneStatus = status;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            lblGeoStatus.Text = $"📍 Trạng thái: {_currentZoneStatus}";
        });
    }

    private void UpdateLastPlayedLabel(string placeName, string source)
    {
        var now = DateTime.Now;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            lblLastPlayedStatus.Text = $"🔊 Đã phát gần nhất: {placeName} ({source}) lúc {now:HH:mm:ss}";
        });
    }

    private void UpdateCooldownLabel(int poiIndex)
    {
        var text = "⏳ Cooldown: -";
        if (poiIndex >= 0 && _autoGeoNextAllowedPlayUtcByPoi.TryGetValue(poiIndex, out var notBeforeUtc))
        {
            var remaining = notBeforeUtc - DateTime.UtcNow;
            if (remaining.TotalSeconds > 0)
                text = $"⏳ Cooldown: còn {Math.Ceiling(remaining.TotalSeconds)}s";
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            lblCooldownStatus.Text = text;
        });
    }

    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double deg) => deg * Math.PI / 180;

    private static double GetEnterRadiusMeters(Place place)
    {
        if (place.ActivationRadiusMeters > 1)
            return place.ActivationRadiusMeters;
        return DefaultAutoGeoEnterMeters;
    }

    private static double GetExitRadiusMeters(Place place)
    {
        var enter = GetEnterRadiusMeters(place);
        return Math.Max(MinAutoGeoExitMeters, enter * AutoGeoExitMultiplier);
    }

    private async Task UpdateNearestPoiHighlightAsync(int nearestPoiIndex)
    {
        var js = $"window.setNearestPoiHighlight && window.setNearestPoiHighlight({nearestPoiIndex});";
        try
        {
            await mapView.EvaluateJavaScriptAsync(js);
        }
        catch
        {
            // WebView chưa sẵn sàng hoặc đang reload.
        }
    }

    async Task<Location?> TryGetCurrentLocationAsync()
    {
        try
        {
            if (!await EnsureLocationPermissionsForContinuousTrackingAsync())
                return null;

            var last = await Geolocation.Default.GetLastKnownLocationAsync();
            // Timeout ngắn hơn để mở tab Bản đồ không chờ lâu khi GPS chậm / mạng yếu.
            var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5));
            var location = await Geolocation.Default.GetLocationAsync(request);
            return location ?? last;
        }
        catch
        {
            try
            {
                return await Geolocation.Default.GetLastKnownLocationAsync();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Lắng nghe GPS liên tục (MAUI dùng foreground service trên Android — có thông báo hệ thống).
    /// Giữ chạy sau khi rời tab Bản đồ; cần quyền vị trí "Luôn" để cập nhật khi app ở nền.
    /// </summary>
    async Task TryStartForegroundGpsListeningAsync()
    {
        if (_isForegroundGpsListening)
            return;

        if (!await EnsureLocationPermissionsForContinuousTrackingAsync())
            return;

#if ANDROID
        // Mock / Samsung: fused listener không bắn sự kiện; poll GetLocationAsync vẫn lấy được tọa độ giả.
        StartAndroidGpsPolling();
#endif

        try
        {
            Geolocation.Default.LocationChanged += OnForegroundLocationChanged;
            Geolocation.Default.ListeningFailed += OnForegroundListeningFailed;

            await Geolocation.Default.StartListeningForegroundAsync(
                new GeolocationListeningRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(3)));

            _isForegroundGpsListening = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"StartListeningForeground: {ex}");
            Geolocation.Default.LocationChanged -= OnForegroundLocationChanged;
            Geolocation.Default.ListeningFailed -= OnForegroundListeningFailed;
        }
    }

    /// <summary>Gộp đường dẫn cập nhật từ event GPS và từ poll Android (Fake GPS).</summary>
    void QueueGpsLocationFromReading(Location? location)
    {
        if (location is null)
            return;

        if (_gpsManualOverrideUntilUtc is DateTime until && DateTime.UtcNow < until)
            return;

        // Throttle: tránh xử lý quá dày (đặc biệt khi vừa có event vừa poll Android).
        var nowUtc = DateTime.UtcNow;
        if (_lastProcessedGpsUtc.HasValue && (nowUtc - _lastProcessedGpsUtc.Value) < MinGpsProcessGap)
            return;

        if (!double.IsNaN(_lastQueuedGpsLat) &&
            Math.Abs(location.Latitude - _lastQueuedGpsLat) < GpsDuplicateEpsilonDegrees &&
            Math.Abs(location.Longitude - _lastQueuedGpsLng) < GpsDuplicateEpsilonDegrees)
            return;

        if (!TryAcceptAndSmoothGps(location, out var acceptedLat, out var acceptedLng, out var qualityText))
            return;

        _lastQueuedGpsLat = acceptedLat;
        _lastQueuedGpsLng = acceptedLng;
        _lastAcceptedGpsUtc = nowUtc;
        _lastProcessedGpsUtc = nowUtc;

        var lat = acceptedLat;
        var lng = acceptedLng;

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            _simulatedLat = lat;
            _simulatedLng = lng;
            _hasSimulationPosition = true;
            if (!string.IsNullOrWhiteSpace(qualityText))
                UpdateGeoStatusLabel($"{_currentZoneStatus} | {qualityText}");
            await SyncUserMarkerPositionOnMapAsync(panToMarker: false);
            await TrackRoutePointAsync("gps");
            await CheckProximityAndSpeakAsync();
            await MaybeAnnounceFootNavCueAsync();
        });
    }

    private static bool IsGpsAccuracyAcceptable(Location location)
    {
        var accuracy = location.Accuracy;
        if (!accuracy.HasValue || accuracy.Value <= 0)
            return true;
        return accuracy.Value <= MaxGpsAccuracyMeters;
    }

    private bool IsGpsJumpLikelyInvalid(Location location)
    {
        if (double.IsNaN(_lastQueuedGpsLat) || double.IsNaN(_lastQueuedGpsLng))
            return false;

        if (!_lastAcceptedGpsUtc.HasValue)
            return false;

        var now = DateTime.UtcNow;
        var dtSeconds = (now - _lastAcceptedGpsUtc.Value).TotalSeconds;
        if (dtSeconds <= 0.35)
            return false;

        var distanceMeters = CalculateDistance(_lastQueuedGpsLat, _lastQueuedGpsLng, location.Latitude, location.Longitude);
        if (distanceMeters < MinDistanceForSpeedFilterMeters)
            return false;

        // Fake GPS thường đổi vị trí theo kiểu "nhảy cụm"; nếu chặn sẽ bị kéo ngược/đứng yên sai.
        if (distanceMeters >= AllowLargeJumpMeters)
            return false;

        // Điểm có accuracy tốt thì ưu tiên tin cậy hơn speed check.
        if (location.Accuracy.HasValue && location.Accuracy.Value > 0 && location.Accuracy.Value <= 18)
            return false;

        var speedMps = distanceMeters / dtSeconds;
        return speedMps > MaxGpsSpeedMetersPerSecond;
    }

    private bool TryAcceptAndSmoothGps(Location location, out double acceptedLat, out double acceptedLng, out string qualityText)
    {
        acceptedLat = 0;
        acceptedLng = 0;
        qualityText = string.Empty;

        var accuracy = location.Accuracy;
        if (!IsGpsAccuracyAcceptable(location))
        {
            if (accuracy.HasValue && accuracy.Value > 0)
                qualityText = $"GPS yếu (±{Math.Round(accuracy.Value)}m)";
            else
                qualityText = "GPS yếu";
            return false;
        }

        if (IsGpsJumpLikelyInvalid(location))
        {
            // Không “kêu” quá nhiều lên UI, chỉ báo nhẹ để hiểu vì sao không nhảy marker.
            qualityText = "GPS nhiễu (lọc jump)";
            return false;
        }

        // EMA smoothing: ổn định marker/route khi GPS hơi rung.
        var lat = location.Latitude;
        var lng = location.Longitude;
        if (double.IsNaN(_smoothedGpsLat) || double.IsNaN(_smoothedGpsLng))
        {
            _smoothedGpsLat = lat;
            _smoothedGpsLng = lng;
        }
        else
        {
            _smoothedGpsLat = (GpsEmaAlpha * lat) + ((1 - GpsEmaAlpha) * _smoothedGpsLat);
            _smoothedGpsLng = (GpsEmaAlpha * lng) + ((1 - GpsEmaAlpha) * _smoothedGpsLng);
        }

        acceptedLat = _smoothedGpsLat;
        acceptedLng = _smoothedGpsLng;

        if (accuracy.HasValue && accuracy.Value > 0)
        {
            var a = accuracy.Value;
            qualityText = a <= 25 ? $"GPS ổn (±{Math.Round(a)}m)" : $"GPS tạm (±{Math.Round(a)}m)";
        }
        else
        {
            qualityText = "GPS OK";
        }

        return true;
    }

#if ANDROID
    void StartAndroidGpsPolling()
    {
        _androidGpsPollCts?.Cancel();
        _androidGpsPollCts?.Dispose();
        _androidGpsPollCts = new CancellationTokenSource();
        var ct = _androidGpsPollCts.Token;

        // Fake GPS thường đẩy vào LocationManager (GPS/Passive), không qua Fused của MAUI.
        AndroidLocationManagerBridge.Start((lat, lng) =>
            QueueGpsLocationFromReading(new Location(lat, lng)));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(4000, ct).ConfigureAwait(false);
                    var loc = await MainThread.InvokeOnMainThreadAsync(() =>
                        Geolocation.Default.GetLocationAsync(
                            new GeolocationRequest(GeolocationAccuracy.Lowest, TimeSpan.FromSeconds(12))));
                    if (loc is not null)
                        QueueGpsLocationFromReading(loc);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"AndroidGpsPoll: {ex.Message}");
                }
            }
        }, ct);
    }

    void StopAndroidGpsPolling()
    {
        AndroidLocationManagerBridge.Stop();
        _androidGpsPollCts?.Cancel();
        _androidGpsPollCts?.Dispose();
        _androidGpsPollCts = null;
    }
#endif

    /// <summary>
    /// When-in-use bắt buộc; Always (Android/iOS) để hệ điều hành cho phép cập nhật khi không mở app.
    /// </summary>
    static async Task<bool> EnsureLocationPermissionsForContinuousTrackingAsync()
    {
        var whenInUse = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (whenInUse != PermissionStatus.Granted)
            return false;

#if ANDROID || IOS || MACCATALYST
        _ = await Permissions.RequestAsync<Permissions.LocationAlways>();
#endif
        return true;
    }

    void StopForegroundGpsListening()
    {
#if ANDROID
        StopAndroidGpsPolling();
#endif
        if (!_isForegroundGpsListening)
            return;

        try
        {
            Geolocation.Default.LocationChanged -= OnForegroundLocationChanged;
            Geolocation.Default.ListeningFailed -= OnForegroundListeningFailed;
            Geolocation.Default.StopListeningForeground();
        }
        catch
        {
            // Bỏ qua.
        }

        _isForegroundGpsListening = false;
    }

    void OnForegroundListeningFailed(object? sender, GeolocationListeningFailedEventArgs e)
    {
        Debug.WriteLine($"Geolocation listening failed: {e.Error}");
    }

    void OnForegroundLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        QueueGpsLocationFromReading(e.Location);
    }

    async Task TrackRoutePointAsync(string source)
    {
        try
        {
            await RouteTrackService.AppendPointAsync(_simulatedLat, _simulatedLng, source);
            var lat = _simulatedLat.ToString(CultureInfo.InvariantCulture);
            var lng = _simulatedLng.ToString(CultureInfo.InvariantCulture);
            await mapView.EvaluateJavaScriptAsync($"window.appendRoutePoint && window.appendRoutePoint({lat}, {lng});");
        }
        catch
        {
            // Bỏ qua lỗi route tracking để không ảnh hưởng luồng chính.
        }
    }

    /// <summary>Đồng bộ chấm đen trên Leaflet với <see cref="_simulatedLat"/> / <see cref="_simulatedLng"/>.</summary>
    async Task SyncUserMarkerPositionOnMapAsync(bool panToMarker)
    {
        var lat = _simulatedLat.ToString(CultureInfo.InvariantCulture);
        var lng = _simulatedLng.ToString(CultureInfo.InvariantCulture);
        var panJs = panToMarker
            ? $"if (window.appMap && typeof window.appMap.panTo === 'function') {{ window.appMap.panTo([{lat}, {lng}]); }}"
            : string.Empty;

        var js = $@"
        if (window.userMarker && typeof window.userMarker.setLatLng === 'function') {{
            window.userMarker.setLatLng([{lat}, {lng}]);
        }}
        {panJs}
        ";

        try
        {
            await mapView.EvaluateJavaScriptAsync(js);
        }
        catch
        {
            // WebView chưa sẵn sàng hoặc đang reload.
        }
    }

    async Task<string> GetImageDataUriAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        try
        {
            // fileName là tên file (ví dụ: pho-am-thuc-vinh-khanh-oc-dao-1707245308.jpg)
            await using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var mime = ext switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => "image/jpeg"
            };

            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return string.Empty;
        }
    }

    static Task<bool> EnsurePoiListenPaidAsync(Place place)
    {
        _ = place;
        return Task.FromResult(true);
    }

    async void MapView_Navigating(object sender, WebNavigatingEventArgs e)
    {
        if (e.Url.StartsWith("app://open-listen-pay", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            try
            {
                var m = Regex.Match(e.Url, @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
                if (m.Success
                    && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                {
                    var openUrl = PlaceApiService.GetListenPayUrlForPlace(pid);
                    if (!string.IsNullOrWhiteSpace(openUrl))
                        await Launcher.Default.OpenAsync(new Uri(openUrl));
                }
            }
            catch
            {
                // Bỏ qua.
            }

            return;
        }

        if (e.Url.Contains("/Listen/Pay", StringComparison.OrdinalIgnoreCase)
            && (e.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || e.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            e.Cancel = true;
            try
            {
                if (Uri.TryCreate(e.Url, UriKind.Absolute, out var u))
                    await Launcher.Default.OpenAsync(u);
            }
            catch
            {
                // Bỏ qua.
            }

            return;
        }

        if (e.Url.StartsWith("app://map", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            try
            {
                var uri = new Uri(e.Url);
                var query = uri.Query.TrimStart('?');
                foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length != 2)
                        continue;
                    if (!string.Equals(kv[0], "u", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var url = Uri.UnescapeDataString(kv[1]);
                    if (Uri.TryCreate(url, UriKind.Absolute, out var openUri))
                        await Launcher.Default.OpenAsync(openUri);
                    return;
                }
            }
            catch
            {
                // Bỏ qua.
            }

            return;
        }

        if (e.Url.StartsWith("app://directions", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            try
            {
                double? destLat = null;
                double? destLng = null;
                string? directionsDestinationName = null;

                var idxMatch = Regex.Match(e.Url, @"[?&]idx=(\d+)", RegexOptions.IgnoreCase);
                if (idxMatch.Success
                    && int.TryParse(idxMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var directIdx)
                    && directIdx >= 0 && directIdx < _pois.Count)
                {
                    var p = _pois[directIdx];
                    destLat = p.Latitude;
                    destLng = p.Longitude;
                    directionsDestinationName = string.IsNullOrWhiteSpace(p.Name) ? $"POI #{p.Id}" : p.Name;
                }
                else
                {
                    var idMatch = Regex.Match(e.Url, @"[?&]id=(\d+)", RegexOptions.IgnoreCase);
                    if (idMatch.Success
                        && int.TryParse(idMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
                        && _poiIndexById.TryGetValue(pid, out var idx)
                        && idx >= 0 && idx < _pois.Count)
                    {
                        var p = _pois[idx];
                        destLat = p.Latitude;
                        destLng = p.Longitude;
                        directionsDestinationName = string.IsNullOrWhiteSpace(p.Name) ? $"POI #{p.Id}" : p.Name;
                    }
                    else
                    {
                        var latM = Regex.Match(e.Url, @"[?&]lat=([^&]+)", RegexOptions.IgnoreCase);
                        var lngM = Regex.Match(e.Url, @"[?&]lng=([^&]+)", RegexOptions.IgnoreCase);
                        if (latM.Success && lngM.Success
                            && double.TryParse(Uri.UnescapeDataString(latM.Groups[1].Value), NumberStyles.Float, CultureInfo.InvariantCulture, out var la)
                            && double.TryParse(Uri.UnescapeDataString(lngM.Groups[1].Value), NumberStyles.Float, CultureInfo.InvariantCulture, out var ln))
                        {
                            destLat = la;
                            destLng = ln;
                        }
                    }
                }

                if (destLat is null || destLng is null)
                    return;

                var (usedOsrmPolyline, _) = await TryApplyOsrmFootRouteOnMapAsync(destLat.Value, destLng.Value, directionsDestinationName);

                var lang = string.IsNullOrWhiteSpace(_selectedLanguage) ? "vi" : _selectedLanguage;
                var tts = BuildDirectionsTtsText(directionsDestinationName, lang, usedOsrmPolyline);
                var label = directionsDestinationName ?? "Điểm dừng xe buýt";

                CancelProximitySpeech();
                CancelBusStopSpeech();
                var durationSeconds = await NarrationQueueService.EnqueuePoiOrTtsAsync(-1, lang, tts);
                UpdateLastPlayedLabel(label, "Chỉ đường");
                await HistoryLogService.AddAsync(label, "Chỉ đường", lang, durationSeconds);
            }
            catch
            {
                // Bỏ qua.
            }

            return;
        }

        // Nút Nghe trong popup dùng scheme riêng cho từng ngôn ngữ: app://speak-vi, speak-en, speak-zh, speak-ja
        // Không còn parse &lang= nữa — tránh mọi vấn đề encode trên WebView Android.
        string? speakLang = null;
        if (e.Url.StartsWith("app://speak-vi", StringComparison.OrdinalIgnoreCase))
            speakLang = "vi";
        else if (e.Url.StartsWith("app://speak-en", StringComparison.OrdinalIgnoreCase))
            speakLang = "en";
        else if (e.Url.StartsWith("app://speak-zh", StringComparison.OrdinalIgnoreCase))
            speakLang = "zh";
        else if (e.Url.StartsWith("app://speak-ja", StringComparison.OrdinalIgnoreCase))
            speakLang = "ja";
        else if (e.Url.StartsWith("app://poi", StringComparison.OrdinalIgnoreCase))
            speakLang = "vi"; // fallback legacy

        if (speakLang == null)
            return;

        e.Cancel = true;

        try
        {
            BeginManualNarrationOverride();
            var poiIndex = -1;
            var idxMatch = Regex.Match(e.Url, @"(?:\?|&)idx=(\d+)", RegexOptions.IgnoreCase);
            if (idxMatch.Success
                && int.TryParse(idxMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var directIdx)
                && directIdx >= 0 && directIdx < _pois.Count)
            {
                poiIndex = directIdx;
            }
            else
            {
                var idMatch = Regex.Match(e.Url, @"(?:\?|&)id=(\d+)", RegexOptions.IgnoreCase);
                if (!idMatch.Success || !int.TryParse(idMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id < 0)
                    return;

                if (!_poiIndexById.TryGetValue(id, out poiIndex) || poiIndex < 0 || poiIndex >= _pois.Count)
                    return;
            }

            var place = _pois[poiIndex];
            if (!await EnsurePoiListenPaidAsync(place))
                return;

            var (text, ttsLang) = ResolveNarrationForPlayback(place, speakLang);

            CancelProximitySpeech();
            CancelBusStopSpeech();
            var durationSeconds = await NarrationQueueService.EnqueuePoiOrTtsAsync(
                poiIndex, ttsLang, text ?? "", default, place.Id > 0 ? place.Id : null);
            UpdateLastPlayedLabel(place.Name, "Map");
            await HistoryLogService.AddAsync(place.Name, "Map", ttsLang, durationSeconds);
        }
        catch { }
    }

    private void BeginManualNarrationOverride()
    {
        _manualNarrationOverrideUntilUtc = DateTime.UtcNow.AddSeconds(ManualNarrationOverrideSeconds);
        _activeProximityPoiIndex = -1;
    }

    private static bool TryParsePoiDeepLinkFromWebView(string? rawUrl, out int poiId, out string lang)
    {
        poiId = -1;
        lang = "vi";

        var url = (rawUrl ?? string.Empty)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            var uri = new Uri(url);
            var query = uri.Query.TrimStart('?');
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;

                var key = Uri.UnescapeDataString(kv[0]);
                var value = Uri.UnescapeDataString(kv[1]);

                if (string.Equals(key, "id", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(value, out var parsedId))
                    poiId = parsedId;

                if (string.Equals(key, "lang", StringComparison.OrdinalIgnoreCase))
                    lang = NormalizeLanguageCode(value);
            }
        }
        catch
        {
            // Fallback regex xử lý các máy trả custom scheme không có Uri.Query.
        }

        if (poiId < 0)
        {
            var idMatch = Regex.Match(url, @"(?:\?|&)id=(\d+)", RegexOptions.IgnoreCase);
            if (idMatch.Success && int.TryParse(idMatch.Groups[1].Value, out var regexId))
                poiId = regexId;
        }

        var langMatch = Regex.Match(url, @"(?:\?|&)lang=([^&#]+)", RegexOptions.IgnoreCase);
        if (langMatch.Success)
        {
            lang = NormalizeLanguageCode(Uri.UnescapeDataString(langMatch.Groups[1].Value));
        }

        return poiId >= 0;
    }

    private static string NormalizeLanguageCode(string? rawLang)
    {
        var normalized = (rawLang ?? string.Empty).Trim().Trim('"', '\'').ToLowerInvariant();
        if (normalized.StartsWith("en", StringComparison.Ordinal))
            return "en";
        if (normalized.StartsWith("zh", StringComparison.Ordinal))
            return "zh";
        if (normalized.StartsWith("ja", StringComparison.Ordinal))
            return "ja";
        if (normalized.StartsWith("vi", StringComparison.Ordinal))
            return "vi";
        return "vi";
    }
}