using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TourGuideApp2.Services;

/// <summary>
/// Lộ trình đi bộ qua OSRM demo (router.project-osrm.org). Chỉ dùng demo — có giới hạn tốc độ, không đảm bảo SLA.
/// </summary>
public static class OsrmRoutingService
{
    public sealed class NavCue
    {
        public double Lat { get; init; }
        public double Lng { get; init; }
        public string Vi { get; init; } = "";
        public string En { get; init; } = "";
        public string Zh { get; init; } = "";
        public string Ja { get; init; } = "";
    }

    public sealed class FootRouteResult
    {
        public IReadOnlyList<(double Lat, double Lng)> Polyline { get; init; } = [];
        public IReadOnlyList<NavCue> Cues { get; init; } = [];
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(28) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AmThucVinhKhanh/1.0 (MAUI; foot routing)");
        return c;
    }

    /// <summary>Gọi OSRM foot profile; trả null nếu lỗi mạng / không có tuyến.</summary>
    public static async Task<FootRouteResult?> TryGetFootRouteAsync(
        double fromLat,
        double fromLng,
        double toLat,
        double toLng,
        string? destinationDisplayName,
        CancellationToken cancellationToken = default)
    {
        var url =
            "https://router.project-osrm.org/route/v1/foot/" +
            $"{fromLng.ToString(CultureInfo.InvariantCulture)},{fromLat.ToString(CultureInfo.InvariantCulture)};" +
            $"{toLng.ToString(CultureInfo.InvariantCulture)},{toLat.ToString(CultureInfo.InvariantCulture)}" +
            "?overview=full&geometries=geojson&steps=true&alternatives=false";

        string body;
        try
        {
            using var resp = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (resp.StatusCode != HttpStatusCode.OK)
                return null;
            body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array || routes.GetArrayLength() == 0)
                return null;

            var route0 = routes[0];
            if (!route0.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Object)
                return null;
            if (!geom.TryGetProperty("coordinates", out var coords) || coords.ValueKind != JsonValueKind.Array)
                return null;

            var poly = new List<(double Lat, double Lng)>(coords.GetArrayLength());
            foreach (var c in coords.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Array || c.GetArrayLength() < 2)
                    continue;
                var lon = c[0].GetDouble();
                var la = c[1].GetDouble();
                poly.Add((la, lon));
            }

            if (poly.Count < 2)
                return null;

            poly = ThinPolyline(poly, maxPoints: 420);

            if (!route0.TryGetProperty("legs", out var legs) || legs.ValueKind != JsonValueKind.Array || legs.GetArrayLength() == 0)
                return null;

            var leg0 = legs[0];
            if (!leg0.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
                return new FootRouteResult { Polyline = poly, Cues = [] };

            var cues = new List<NavCue>();
            var stepList = steps.EnumerateArray().ToList();
            for (var i = 0; i < stepList.Count; i++)
            {
                var step = stepList[i];
                if (!step.TryGetProperty("maneuver", out var maneuver))
                    continue;
                if (!TryReadLocation(maneuver, out var cueLat, out var cueLng))
                    continue;

                var type = maneuver.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String
                    ? tEl.GetString() ?? ""
                    : "";
                type = type.Trim().ToLowerInvariant();

                if (i == 0 && type == "depart")
                    continue;

                var modifier = maneuver.TryGetProperty("modifier", out var mEl) && mEl.ValueKind == JsonValueKind.String
                    ? mEl.GetString()?.Trim().ToLowerInvariant() ?? ""
                    : "";

                var road = step.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String
                    ? nEl.GetString()?.Trim() ?? ""
                    : "";

                var cue = BuildCue(cueLat, cueLng, type, modifier, road, destinationDisplayName);
                if (cue is null)
                    continue;

                if (cues.Count > 0)
                {
                    var last = cues[^1];
                    if (HaversineMeters(last.Lat, last.Lng, cue.Lat, cue.Lng) < 9)
                        continue;
                }

                cues.Add(cue);
            }

            return new FootRouteResult { Polyline = poly, Cues = cues };
        }
        catch
        {
            return null;
        }
    }

    private static List<(double Lat, double Lng)> ThinPolyline(List<(double Lat, double Lng)> ring, int maxPoints)
    {
        if (ring.Count <= maxPoints)
            return ring;

        var n = ring.Count;
        var step = (double)(n - 1) / (maxPoints - 1);
        var list = new List<(double Lat, double Lng)>(maxPoints);
        for (var i = 0; i < maxPoints; i++)
        {
            var idx = (int)(i * step);
            if (idx >= n)
                idx = n - 1;
            list.Add(ring[idx]);
        }

        list[^1] = ring[^1];
        return list;
    }

    private static bool TryReadLocation(JsonElement maneuver, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        if (!maneuver.TryGetProperty("location", out var loc) || loc.ValueKind != JsonValueKind.Array || loc.GetArrayLength() < 2)
            return false;
        lng = loc[0].GetDouble();
        lat = loc[1].GetDouble();
        return true;
    }

    private static NavCue? BuildCue(
        double lat,
        double lng,
        string type,
        string modifier,
        string road,
        string? destName)
    {
        var r = string.IsNullOrWhiteSpace(road) ? null : road.Trim();
        var d = string.IsNullOrWhiteSpace(destName) ? null : destName.Trim();

        if (type == "arrive")
        {
            return new NavCue
            {
                Lat = lat,
                Lng = lng,
                Vi = d is null ? "Bạn đã gần đích." : $"Bạn đã gần đích: {d}.",
                En = d is null ? "You are almost at the destination." : $"You are almost at {d}.",
                Zh = d is null ? "您已接近目的地。" : $"您已接近「{d}」。",
                Ja = d is null ? "もうすぐ目的地です。" : $"「{d}」に近づいています。"
            };
        }

        var (vi, en, zh, ja) = type switch
        {
            "turn" => TurnAll(modifier, r),
            "new name" => (
                $"Đi tiếp theo {RoadVi(r)}.",
                $"Continue onto {RoadEn(r)}.",
                $"沿{RoadZh(r)}继续前进。",
                $"{RoadJa(r)}に進みます。"),
            "continue" => (
                $"Tiếp tục đi trên {RoadVi(r)}.",
                $"Continue on {RoadEn(r)}.",
                $"继续沿{RoadZh(r)}前进。",
                $"{RoadJa(r)}を直進します。"),
            "merge" => (
                $"Nhập làn / nhập đường {RoadVi(r)}.",
                $"Merge toward {RoadEn(r)}.",
                $"并入{RoadZh(r)}。",
                $"{RoadJa(r)}に合流します。"),
            "fork" => (
                $"Ở ngã ba, chọn hướng tới {RoadVi(r)}.",
                $"At the fork, take the way toward {RoadEn(r)}.",
                $"在岔路选择前往{RoadZh(r)}的方向。",
                $"分岐点では{RoadJa(r)}方面へ。"),
            "end of road" => EndOfRoadAll(modifier, r),
            "roundabout" or "rotary" => (
                "Đi vào vòng xuyến.",
                "Enter the roundabout.",
                "进入环岛。",
                "ロータリーに入ります。"),
            "exit roundabout" or "exit rotary" => (
                $"Ra khỏi vòng xuyến, đi theo {RoadVi(r)}.",
                $"Exit the roundabout onto {RoadEn(r)}.",
                $"驶出环岛，进入{RoadZh(r)}。",
                $"ロータリーを出て{RoadJa(r)}へ。"),
            "notification" => (
                $"Chú ý: {RoadVi(r)}.",
                $"Note: {RoadEn(r)}.",
                $"提示：{RoadZh(r)}。",
                $"案内: {RoadJa(r)}。"),
            _ => (
                $"Đi tiếp theo {RoadVi(r)}.",
                $"Continue toward {RoadEn(r)}.",
                $"沿{RoadZh(r)}前进。",
                $"{RoadJa(r)}へ進みます。")
        };

        if (string.IsNullOrWhiteSpace(vi) && string.IsNullOrWhiteSpace(en))
            return null;

        return new NavCue
        {
            Lat = lat,
            Lng = lng,
            Vi = vi,
            En = string.IsNullOrWhiteSpace(en) ? vi : en,
            Zh = string.IsNullOrWhiteSpace(zh) ? vi : zh,
            Ja = string.IsNullOrWhiteSpace(ja) ? en : ja
        };
    }

    private static (string vi, string en, string zh, string ja) TurnAll(string modifier, string? r)
    {
        return modifier switch
        {
            "left" or "sharp left" or "slight left" => (
                $"Rẽ trái vào {RoadVi(r)}.",
                $"Turn left onto {RoadEn(r)}.",
                $"左转进入{RoadZh(r)}。",
                $"{RoadJa(r)}の方へ左折します。"),
            "right" or "sharp right" or "slight right" => (
                $"Rẽ phải vào {RoadVi(r)}.",
                $"Turn right onto {RoadEn(r)}.",
                $"右转进入{RoadZh(r)}。",
                $"{RoadJa(r)}の方へ右折します。"),
            "straight" => (
                $"Đi thẳng theo {RoadVi(r)}.",
                $"Go straight on {RoadEn(r)}.",
                $"沿{RoadZh(r)}直行。",
                $"{RoadJa(r)}を直進します。"),
            "uturn" => (
                $"Quay đầu, sau đó vào {RoadVi(r)}.",
                $"Make a U-turn, then take {RoadEn(r)}.",
                $"掉头后进入{RoadZh(r)}。",
                $"Uターンして{RoadJa(r)}へ。"),
            _ => (
                $"Đổi hướng và đi vào {RoadVi(r)}.",
                $"Turn, then continue onto {RoadEn(r)}.",
                $"转弯后进入{RoadZh(r)}。",
                $"曲がって{RoadJa(r)}へ。")
        };
    }

    private static (string vi, string en, string zh, string ja) EndOfRoadAll(string modifier, string? r)
    {
        var sideVi = modifier.Contains("left", StringComparison.Ordinal) ? "trái" : modifier.Contains("right", StringComparison.Ordinal) ? "phải" : "";
        var sideEn = modifier.Contains("left", StringComparison.Ordinal) ? "left" : modifier.Contains("right", StringComparison.Ordinal) ? "right" : "";

        if (sideVi.Length > 0)
        {
            return (
                $"Hết đường, rẽ {sideVi} vào {RoadVi(r)}.",
                $"At the end of the road, turn {sideEn} onto {RoadEn(r)}.",
                $"道路尽头向{MapZhSide(sideEn)}转，进入{RoadZh(r)}。",
                $"行き止まりで{MapJaSide(sideEn)}、{RoadJa(r)}へ。");
        }

        return (
            $"Hết đường, đi vào {RoadVi(r)}.",
            $"At the end of the road, continue onto {RoadEn(r)}.",
            $"道路尽头进入{RoadZh(r)}。",
            $"行き止まりから{RoadJa(r)}へ。");
    }

    private static string MapZhSide(string sideEn) => sideEn == "left" ? "左" : "右";
    private static string MapJaSide(string sideEn) => sideEn == "left" ? "左" : "右";

    private static string RoadVi(string? r) => r ?? "đường không tên";
    private static string RoadEn(string? r) => r ?? "the unnamed street";
    private static string RoadZh(string? r) => r ?? "未命名道路";
    private static string RoadJa(string? r) => r ?? "名称のない道";

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        var dLat = (lat2 - lat1) * (Math.PI / 180);
        var dLon = (lon2 - lon1) * (Math.PI / 180);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * (Math.PI / 180)) * Math.Cos(lat2 * (Math.PI / 180)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    public static string PickCueText(NavCue cue, string lang)
    {
        var l = (lang ?? "vi").Trim().ToLowerInvariant();
        return l switch
        {
            "en" => cue.En,
            "zh" => cue.Zh,
            "ja" => cue.Ja,
            _ => cue.Vi
        };
    }

    /// <summary>Polyline dạng JSON [[lat,lng],...] — ASCII an toàn cho base64 trong WebView.</summary>
    public static string PolylineToJsonBase64(IReadOnlyList<(double Lat, double Lng)> poly)
    {
        var sb = new StringBuilder(poly.Count * 24);
        sb.Append('[');
        for (var i = 0; i < poly.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var p = poly[i];
            sb.Append('[')
                .Append(p.Lat.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(p.Lng.ToString(CultureInfo.InvariantCulture))
                .Append(']');
        }

        sb.Append(']');
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
    }
}
