using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using QRCoder;
using TourGuideCMS;
using TourGuideCMS.Services;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    // Tunnel (ngrok, Cloudflare) — tin header reverse proxy trong môi trường dev/demo.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
    });

builder.Services.AddAuthorization();
builder.Services.AddRazorPages();
builder.Services.AddSingleton<PlaceRepository>();
builder.Services.AddSingleton<CustomerAccountRepository>();
builder.Services.AddSingleton<CmsIdentityRepository>();

var app = builder.Build();

app.UseForwardedHeaders();

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en-US"),
    SupportedCultures = new[] { "en-US" }.Select(c => new System.Globalization.CultureInfo(c)).ToList(),
    SupportedUICultures = new[] { "en-US" }.Select(c => new System.Globalization.CultureInfo(c)).ToList()
};
localizationOptions.RequestCultureProviders.Clear();

app.UseRequestLocalization(localizationOptions);
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Ping đơn giản để app tự kiểm tra đúng host/port trong LAN.
app.MapGet("/api/ping", () => Results.Json(new
{
    ok = true,
    utc = DateTime.UtcNow.ToString("O")
}));

static string? FindApkPath(IWebHostEnvironment env)
{
    var www = env.WebRootPath ?? string.Empty;
    var canonicalDownloads = string.IsNullOrWhiteSpace(www)
        ? null
        : Path.Combine(www, "downloads", "AmThucVinhKhanh.apk");

    // Luôn ưu tiên file upload chuẩn — tránh QR tải nhầm APK debug trong bin (ABI/khác build → máy khác văng).
    if (!string.IsNullOrWhiteSpace(canonicalDownloads) && System.IO.File.Exists(canonicalDownloads))
        return canonicalDownloads;

    var candidates = new List<string>();
    if (!string.IsNullOrWhiteSpace(www))
    {
        var downloadsDir = Path.Combine(www, "downloads");
        if (Directory.Exists(downloadsDir))
            candidates.AddRange(Directory.GetFiles(downloadsDir, "*.apk", SearchOption.TopDirectoryOnly));
    }

    // Fallback: nếu chưa upload file chuẩn, vẫn cho phép lấy APK build mới nhất để QR tải được ngay.
    var root = env.ContentRootPath ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(root))
    {
        var releaseDir = Path.GetFullPath(Path.Combine(root, "..", "bin", "Release", "net10.0-android"));
        if (Directory.Exists(releaseDir))
            candidates.AddRange(Directory.GetFiles(releaseDir, "*.apk", SearchOption.AllDirectories));
    }

    var existing = candidates
        .Where(System.IO.File.Exists)
        .Select(p => new FileInfo(p))
        .OrderByDescending(fi => fi.LastWriteTimeUtc)
        .ThenByDescending(fi => fi.Length)
        .Select(fi => fi.FullName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    return existing.FirstOrDefault();
}

static string? BuildLocalApkDownloadUrl(HttpContext http, IConfiguration config, IWebHostEnvironment env)
{
    var apkPath = FindApkPath(env);
    if (string.IsNullOrWhiteSpace(apkPath) || !System.IO.File.Exists(apkPath))
        return null;

    var version = System.IO.File.GetLastWriteTimeUtc(apkPath).Ticks;
    return $"{PublicSiteUrls.SiteRootForLinks(http, config)}/downloads/AmThucVinhKhanh.apk?v={version}";
}

static string? ResolveInstallTargetUrl(HttpContext http, IConfiguration config, IWebHostEnvironment env)
{
    // 1) Link public cấu hình sẵn (store/APK public)
    var direct = (config["App:AppDownloadUrl"] ?? "").Trim();
    if (!string.IsNullOrEmpty(direct)
        && Uri.TryCreate(direct, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
        return direct;

    // 2) APK local trong CMS
    var localApkUrl = BuildLocalApkDownloadUrl(http, config, env);
    if (!string.IsNullOrWhiteSpace(localApkUrl))
        return localApkUrl;

    return null;
}

// API JSON cho app MAUI (PostgREST-style): cấu hình URL trong app trỏ tới https://.../api/places
app.MapGet("/api/places", async (HttpContext http, PlaceRepository repo, IConfiguration config) =>
{
    var rows = await repo.ListAsync();
    var payload = rows.Select(r => new
    {
        id = r.Id,
        name = r.Name,
        address = r.Address,
        specialty = r.Specialty,
        imageUrl = r.ImageUrl,
        latitude = r.Latitude,
        longitude = r.Longitude,
        activationRadiusMeters = r.ActivationRadiusMeters,
        priority = r.Priority,
        description = r.Description,
        vietnameseAudioText = r.VietnameseAudioText,
        englishAudioText = r.EnglishAudioText,
        chineseAudioText = r.ChineseAudioText,
        japaneseAudioText = r.JapaneseAudioText,
        mapUrl = r.MapUrl,
        premiumPriceDemo = r.PremiumPriceDemo,
        qrPayload = PublicSiteUrls.ListenPayPayload(http, config, r.Id)
    });
    return Results.Json(payload, new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
});

// QR PNG: HTTPS mở được trong Zalo/trình duyệt (trang trả phí demo).
app.MapGet("/qr/places/{id:int}", async (HttpContext http, int id, PlaceRepository repo, IConfiguration config) =>
{
    var place = await repo.GetAsync(id);
    if (place is null)
        return Results.NotFound();

    var content = PublicSiteUrls.ListenPayPayload(http, config, place.Id);
    using var gen = new QRCodeGenerator();
    using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
    var png = new PngByteQRCode(data);
    var bytes = png.GetGraphic(8);
    return Results.File(bytes, "image/png");
});

// APK local (LAN): đặt file tại wwwroot/downloads/AmThucVinhKhanh.apk để điện thoại quét QR là bật prompt tải/cài.
app.MapGet("/downloads/AmThucVinhKhanh.apk", (HttpContext http, IWebHostEnvironment env) =>
{
    var apkPath = FindApkPath(env);
    if (string.IsNullOrWhiteSpace(apkPath) || !System.IO.File.Exists(apkPath))
        return Results.NotFound("Thiếu file APK. Đặt file .apk vào TourGuideCMS/wwwroot/downloads/ hoặc build Android APK.");

    // Bắt buộc no-cache để máy quét QR luôn tải đúng APK mới vừa upload/build.
    http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
    http.Response.Headers.Pragma = "no-cache";
    http.Response.Headers.Expires = "0";

    // Dùng octet-stream + filename để nhiều webview buộc tải file thay vì render trắng.
    return Results.File(apkPath, "application/octet-stream", "AmThucVinhKhanh.apk");
});

// Route trung gian cho QR tải app: nếu có target cài đặt thì redirect thẳng.
app.MapGet("/install/launch", (HttpContext http, IConfiguration config, IWebHostEnvironment env) =>
{
    var target = ResolveInstallTargetUrl(http, config, env);
    if (string.IsNullOrWhiteSpace(target))
        return Results.Redirect("/Install?fromQr=1");

    var encodedTarget = Uri.EscapeDataString(target);
    var fallback = Uri.EscapeDataString($"{PublicSiteUrls.SiteRootForLinks(http, config)}/Install?fromQr=1");
    var noScheme = target.Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                         .Replace("http://", "", StringComparison.OrdinalIgnoreCase);
    var intentScheme = target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
    var intentUrl = $"intent://{noScheme}#Intent;scheme={intentScheme};package=com.android.chrome;S.browser_fallback_url={fallback};end";
    var encodedIntent = Uri.EscapeDataString(intentUrl);
    var setupDeepLink = $"amthucvinhkhanh://setup?base={Uri.EscapeDataString(PublicSiteUrls.SiteRootForLinks(http, config))}";
    var encodedSetup = Uri.EscapeDataString(setupDeepLink);

    var html = $@"
<!doctype html>
<html>
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>Đang mở cài đặt ứng dụng...</title>
</head>
<body style=""font-family: Arial, sans-serif; padding: 16px;"">
  <p>Đang mở cài đặt ứng dụng...</p>
  <p>Nếu không tự chuyển, dùng các nút bên dưới:</p>
  <p><a href=""{target}"">Tải / cài đặt app</a></p>
  <p><a href=""{intentUrl}"">Mở bằng Chrome</a></p>
  <p><a href=""{setupDeepLink}"">Mở app và tự cấu hình đồng bộ</a></p>
  <script>
    (function () {{
      var ua = navigator.userAgent || '';
      var isAndroid = /Android/i.test(ua);
      if (isAndroid) {{
        try {{ window.location.href = decodeURIComponent('{encodedIntent}'); }} catch(e) {{}}
      }}
      setTimeout(function() {{
        try {{ window.location.href = decodeURIComponent('{encodedTarget}'); }} catch(e) {{}}
      }}, 500);

      // Sau khi cài xong, nhiều máy quay lại trình duyệt thay vì tự mở app.
      // Thử gọi deeplink setup định kỳ để app nhận URL CMS mà không cần màn Cài đặt.
      var tries = 0;
      var timer = setInterval(function() {{
        tries++;
        if (tries > 45) {{ clearInterval(timer); return; }}
        try {{ window.location.href = decodeURIComponent('{encodedSetup}'); }} catch(e) {{}}
      }}, 2000);
    }})();
  </script>
</body>
</html>";
    return Results.Content(html, "text/html; charset=utf-8");
});

// Upload nhanh APK từ trình duyệt để bật ngay QR cài app cho thiết bị khác.
app.MapPost("/api/install/upload-apk", async (HttpRequest req, IWebHostEnvironment env) =>
{
    if (!req.HasFormContentType)
        return Results.BadRequest("Yêu cầu multipart/form-data.");

    var form = await req.ReadFormAsync();
    var file = form.Files["apkFile"] ?? form.Files.FirstOrDefault(f => f.FileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));
    if (file is null || file.Length <= 0)
        return Results.Redirect("/Install?uploadError=1");

    var ext = Path.GetExtension(file.FileName);
    if (!".apk".Equals(ext, StringComparison.OrdinalIgnoreCase))
        return Results.Redirect("/Install?uploadError=1");

    var downloadsDir = Path.Combine(env.WebRootPath ?? "", "downloads");
    Directory.CreateDirectory(downloadsDir);
    var targetPath = Path.Combine(downloadsDir, "AmThucVinhKhanh.apk");

    await using (var fs = System.IO.File.Create(targetPath))
    {
        await file.CopyToAsync(fs);
    }

    return Results.Redirect("/Install?uploaded=1");
});

// QR PNG tải app: mở route /install/launch để nhảy thẳng luồng cài đặt nếu có APK/link.
app.MapGet("/qr/app", (HttpContext http, IConfiguration config, IWebHostEnvironment env) =>
{
    http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
    http.Response.Headers.Pragma = "no-cache";

    var content = PublicSiteUrls.QrAppInstallLaunchUrl(http, config);

    using var gen = new QRCodeGenerator();
    using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
    var png = new PngByteQRCode(data);
    var bytes = png.GetGraphic(8);
    return Results.File(bytes, "image/png");
});

// Chẩn đoán nhanh: xem CMS đang phục vụ APK nào cho QR.
app.MapGet("/api/install/apk-info", (IWebHostEnvironment env) =>
{
    var apkPath = FindApkPath(env);
    if (string.IsNullOrWhiteSpace(apkPath) || !System.IO.File.Exists(apkPath))
        return Results.NotFound(new { message = "Không tìm thấy APK." });

    var fi = new FileInfo(apkPath);
    return Results.Json(new
    {
        path = fi.FullName,
        bytes = fi.Length,
        lastWriteUtc = fi.LastWriteTimeUtc.ToString("O")
    });
});

static bool MobileKeyOk(HttpRequest req, IConfiguration config)
{
    var expected = config["App:MobileApiKey"];
    if (string.IsNullOrEmpty(expected))
        return true;
    return string.Equals(req.Headers["X-Mobile-Key"], expected, StringComparison.Ordinal);
}

// --- Tài khoản khách (app MAUI) ---
app.MapPost("/api/customers/register", async (HttpRequest req, CustomerAccountRepository repo) =>
{
    var body = await System.Text.Json.JsonSerializer.DeserializeAsync<RegisterBody>(
        req.Body,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (body is null)
        return Results.BadRequest(new { message = "Body không hợp lệ." });

    var (ok, message, user) = await repo.RegisterAsync(body.FullName, body.PhoneOrEmail, body.Password);
    if (!ok || user is null)
        return Results.BadRequest(new { message });

    return Results.Json(new
    {
        id = user.Id,
        fullName = user.FullName,
        phoneOrEmail = user.PhoneOrEmail,
        createdAt = user.CreatedAtUtc.ToString("O")
    });
});

app.MapPost("/api/customers/login", async (HttpRequest req, CustomerAccountRepository repo) =>
{
    var body = await System.Text.Json.JsonSerializer.DeserializeAsync<LoginBody>(
        req.Body,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (body is null)
        return Results.BadRequest(new { message = "Body không hợp lệ." });

    var (ok, message, user) = await repo.LoginAsync(body.PhoneOrEmail, body.Password);
    if (!ok || user is null)
        return Results.Json(new { message }, statusCode: 401);

    return Results.Json(new
    {
        id = user.Id,
        fullName = user.FullName,
        phoneOrEmail = user.PhoneOrEmail,
        createdAt = user.CreatedAtUtc.ToString("O")
    });
});

// Đồng bộ lượt phát thuyết minh (QR / Map / AutoGeo / BusStop)
app.MapPost("/api/plays/log", async (HttpRequest req, CustomerAccountRepository repo, IConfiguration config) =>
{
    if (!MobileKeyOk(req, config))
        return Results.Unauthorized();

    var body = await System.Text.Json.JsonSerializer.DeserializeAsync<PlayLogBody>(
        req.Body,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (body is null)
        return Results.BadRequest(new { message = "Body không hợp lệ." });

    DateTime played = DateTime.UtcNow;
    if (!string.IsNullOrWhiteSpace(body.PlayedAtUtc) && DateTime.TryParse(body.PlayedAtUtc, out var parsed))
        played = parsed.ToUniversalTime();

    await repo.AddPlayAsync(
        body.CustomerUserId,
        body.PlaceName ?? "",
        body.Source ?? "",
        body.Language,
        body.DurationSeconds,
        played);

    return Results.Ok(new { ok = true });
});

// Lịch sử lượt phát theo khách (app đồng bộ với trang /Plays)
app.MapGet("/api/plays/history", async (HttpRequest req, CustomerAccountRepository repo, IConfiguration config) =>
{
    if (!MobileKeyOk(req, config))
        return Results.Unauthorized();

    var q = req.Query["customerUserId"].ToString();
    if (string.IsNullOrWhiteSpace(q))
        q = req.Query["customer_user_id"].ToString();
    if (!int.TryParse(q, out var customerUserId) || customerUserId <= 0)
        return Results.BadRequest(new { message = "Thiếu customerUserId (query)." });

    var rows = await repo.ListPlaysForCustomerAsync(customerUserId, 500);
    var payload = rows.Select(p => new
    {
        id = p.Id,
        placeName = p.PlaceName,
        source = p.Source,
        language = p.Language,
        durationSeconds = p.DurationSeconds,
        playedAtUtc = p.PlayedAtUtc.ToUniversalTime().ToString("O")
    });
    return Results.Json(payload, new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
});

// Đồng bộ tuyến di chuyển từ app — mỗi CustomerUserId một snapshot JSON (admin xem Tuyến / Heatmap).
app.MapPost("/api/customers/route-sync", async (HttpRequest req, CustomerAccountRepository repo, IConfiguration config) =>
{
    if (!MobileKeyOk(req, config))
        return Results.Unauthorized();

    var body = await System.Text.Json.JsonSerializer.DeserializeAsync<RouteSyncBody>(
        req.Body,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (body is null || body.CustomerUserId <= 0)
        return Results.BadRequest(new { message = "Thiếu customerUserId hoặc body không hợp lệ." });

    var json = System.Text.Json.JsonSerializer.Serialize(body.Points ?? []);
    await repo.UpsertRouteSnapshotAsync(body.CustomerUserId, json);
    return Results.Ok(new { ok = true });
});

// Thanh toán demo mở thuyết minh (app gửi DeviceInstallId + tùy chọn CustomerUserId).
app.MapPost("/api/premium/pay-demo", async (HttpRequest req, PlaceRepository places, CustomerAccountRepository customers, IConfiguration config) =>
{
    if (!MobileKeyOk(req, config))
        return Results.Unauthorized();

    var body = await System.Text.Json.JsonSerializer.DeserializeAsync<PremiumPayDemoBody>(
        req.Body,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (body is null || body.PlaceId <= 0 || string.IsNullOrWhiteSpace(body.DeviceInstallId))
        return Results.BadRequest(new { message = "Thiếu placeId hoặc deviceInstallId." });

    var place = await places.GetAsync(body.PlaceId);
    if (place is null)
        return Results.NotFound(new { message = "Không có POI." });
    if (place.PremiumPriceDemo <= 0)
        return Results.BadRequest(new { message = "POI này không bật trả phí demo." });

    var (ok, message, already) = await customers.RecordPremiumPaymentDemoAsync(
        body.PlaceId,
        body.DeviceInstallId,
        place.PremiumPriceDemo,
        body.CustomerUserId);

    return Results.Json(new { ok, message, alreadyUnlocked = already });
});

app.MapGet("/api/premium/entitlement", async (HttpRequest req, PlaceRepository places, CustomerAccountRepository customers, IConfiguration config) =>
{
    if (!MobileKeyOk(req, config))
        return Results.Unauthorized();

    if (!int.TryParse(req.Query["placeId"], out var placeId) || placeId <= 0)
        return Results.BadRequest(new { message = "Thiếu placeId." });
    var deviceId = req.Query["deviceInstallId"].ToString();
    if (string.IsNullOrWhiteSpace(deviceId))
        return Results.BadRequest(new { message = "Thiếu deviceInstallId." });
    int? customerUserId = null;
    var cq = req.Query["customerUserId"].ToString();
    if (int.TryParse(cq, out var cid) && cid > 0)
        customerUserId = cid;

    var place = await places.GetAsync(placeId);
    if (place is null)
        return Results.NotFound(new { message = "Không có POI." });
    if (place.PremiumPriceDemo <= 0)
        return Results.Json(new { unlocked = true });

    var unlocked = await customers.HasPremiumUnlockForCurrentPriceAsync(
        placeId,
        place.PremiumPriceDemo,
        deviceId.Trim(),
        customerUserId);

    return Results.Json(new { unlocked });
});

app.MapRazorPages();

await using (var scope = app.Services.CreateAsyncScope())
{
    var places = scope.ServiceProvider.GetRequiredService<PlaceRepository>();
    var cmsIdentity = scope.ServiceProvider.GetRequiredService<CmsIdentityRepository>();
    var customers = scope.ServiceProvider.GetRequiredService<CustomerAccountRepository>();
    await customers.EnsureSchemaAsync();
    await cmsIdentity.EnsureSchemaAsync();
    await cmsIdentity.EnsureAdminSeedAsync(scope.ServiceProvider.GetRequiredService<IConfiguration>()["AdminPassword"]);
    await cmsIdentity.SyncOwnersForPlacesAsync(await places.ListAsync());
}

app.Run();

internal sealed class RegisterBody
{
    public string FullName { get; set; } = "";
    public string PhoneOrEmail { get; set; } = "";
    public string Password { get; set; } = "";
}

internal sealed class LoginBody
{
    public string PhoneOrEmail { get; set; } = "";
    public string Password { get; set; } = "";
}

internal sealed class PlayLogBody
{
    public int? CustomerUserId { get; set; }
    public string? PlaceName { get; set; }
    public string? Source { get; set; }
    public string? Language { get; set; }
    public double? DurationSeconds { get; set; }
    public string? PlayedAtUtc { get; set; }
}

internal sealed class RouteSyncBody
{
    public int CustomerUserId { get; set; }
    public List<RoutePointSyncItem>? Points { get; set; }
}

internal sealed class RoutePointSyncItem
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? TimestampUtc { get; set; }
    public string? Source { get; set; }
}

internal sealed class PremiumPayDemoBody
{
    public int PlaceId { get; set; }
    public string? DeviceInstallId { get; set; }
    public int? CustomerUserId { get; set; }
}
