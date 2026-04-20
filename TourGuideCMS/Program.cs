using System.Net;
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
    var apkPath = ApkLocator.FindPreferredApkPath(env, http.RequestServices.GetRequiredService<IConfiguration>());
    if (string.IsNullOrWhiteSpace(apkPath) || !System.IO.File.Exists(apkPath))
        return Results.NotFound("Thiếu file APK. Đặt file .apk vào TourGuideCMS/wwwroot/downloads/ hoặc build Android APK.");

    // Bắt buộc no-cache để máy quét QR luôn tải đúng APK mới vừa upload/build.
    http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
    http.Response.Headers.Pragma = "no-cache";
    http.Response.Headers.Expires = "0";

    var v = ApkLocator.CacheBusterForPath(apkPath);
    var safeName = $"AmThucVinhKhanh_{v}.apk".Replace(':', '_');

    // Dùng octet-stream + filename duy nhất theo build để Download Manager Android ít gộp nhầm bản cũ.
    return Results.File(apkPath, "application/octet-stream", safeName);
});

// Trang HTML «Đang mở cài đặt…» — quét QR mở đây rồi bấm tải (Zalo/WebView không nên 302 thẳng file .apk).
app.MapGet("/install/launch", (HttpContext http, IConfiguration config, IWebHostEnvironment env) =>
{
    http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
    http.Response.Headers.Pragma = "no-cache";
    http.Response.Headers.Expires = "0";

    var target = InstallTargetUrlResolver.Resolve(http, config, env);
    if (string.IsNullOrWhiteSpace(target))
        return Results.Redirect("/Install");

    var siteRoot = PublicSiteUrls.SiteRootForLinks(http, config);
    var setupDeepLink = $"amthucvinhkhanh://setup?base={Uri.EscapeDataString(siteRoot)}";
    var installPage = $"{siteRoot}/Install";

    var href = WebUtility.HtmlEncode(target);
    var chromeIntent = WebUtility.HtmlEncode(InstallLaunchLinks.ChromeViewIntentOrFallback(target));
    var setupEsc = WebUtility.HtmlEncode(setupDeepLink);
    var installEsc = WebUtility.HtmlEncode(installPage);

    var apkPathResolved = ApkLocator.FindPreferredApkPath(env, config);
    string sizeLine = "";
    if (!string.IsNullOrWhiteSpace(apkPathResolved) && System.IO.File.Exists(apkPathResolved))
    {
        var mb = new System.IO.FileInfo(apkPathResolved).Length / (1024.0 * 1024.0);
        sizeLine = $@"<p class=""dim"">Gói cài đặt hiện tại: ~{mb:0.#} MB (cùng file CMS phục vụ QR).</p>";
    }

    var expectedVer = (config["App:ExpectedAppVersion"] ?? "").Trim();
    var versionLine = string.IsNullOrEmpty(expectedVer)
        ? ""
        : $@"<p class=""dim"">Phiên bản build mục tiêu: <strong>{WebUtility.HtmlEncode(expectedVer)}</strong></p>";

    var html = $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
  <meta charset=""utf-8""/>
  <meta name=""viewport"" content=""width=device-width, initial-scale=1""/>
  <title>Đang mở cài đặt ứng dụng…</title>
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; padding: 20px; max-width: 480px; margin: 0 auto; background: #fff; color: #212121; }}
    h1 {{ font-size: 1.2rem; font-weight: 600; margin: 0 0 12px 0; }}
    p {{ margin: 10px 0; line-height: 1.5; }}
    .dim {{ color: #616161; font-size: 0.9rem; }}
    a.primary {{ color: #1565c0; font-size: 1.05rem; }}
    a.secondary {{ color: #1565c0; font-size: 1.05rem; }}
    .more {{ margin-top: 22px; padding-top: 16px; border-top: 1px solid #e0e0e0; font-size: 0.88rem; color: #546e7a; }}
    .btn {{ display: inline-block; margin-top: 8px; padding: 12px 16px; background: #00897b; color: #fff !important; text-decoration: none; border-radius: 10px; font-weight: 600; }}
  </style>
</head>
<body>
  <h1>Đang mở cài đặt ứng dụng…</h1>
  <p>Nếu không tự chuyển, dùng các nút bên dưới:</p>
  <p><a class=""primary"" href=""{href}"">Tải / cài đặt app</a></p>
  <p><a class=""secondary"" href=""{chromeIntent}"">Mở bằng Chrome</a> <span class=""dim"">(nếu đang mở trong Zalo / Facebook)</span></p>
  {sizeLine}
  {versionLine}
  <p class=""dim"">Không có màn đăng nhập email trong app khách — tab <strong>Khám phá</strong> &amp; <strong>Dùng app trực tiếp</strong>. Nếu vẫn thấy form đăng nhập: gỡ app cũ và tải lại bản này.</p>
  <div class=""more"">
    <p>Sau khi cài: <a class=""btn"" href=""{setupEsc}"">Mở app &amp; gán URL CMS</a></p>
    <p><a href=""{installEsc}"">Trang Install đầy đủ</a> · trong app: Cài đặt → URL API nếu cần.</p>
  </div>
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

// QR PNG tải app: nội dung = /install/launch?v=… — quét = trang «Đang mở cài đặt…» (hình 2), nút tải dùng Resolve (~130MB).
app.MapGet("/qr/app", (HttpContext http, IConfiguration config, IWebHostEnvironment env) =>
{
    http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
    http.Response.Headers.Pragma = "no-cache";

    var content = PublicSiteUrls.QrAppInstallLaunchPayload(http, config, env);

    using var gen = new QRCodeGenerator();
    using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
    var png = new PngByteQRCode(data);
    var bytes = png.GetGraphic(8);
    return Results.File(bytes, "image/png");
});

// Chẩn đoán nhanh: xem CMS đang phục vụ APK nào cho QR.
app.MapGet("/api/install/apk-info", (IWebHostEnvironment env, IConfiguration cfg) =>
{
    var apkPath = ApkLocator.FindPreferredApkPath(env, cfg);
    if (string.IsNullOrWhiteSpace(apkPath) || !System.IO.File.Exists(apkPath))
        return Results.NotFound(new { message = "Không tìm thấy APK." });

    var fi = new FileInfo(apkPath);
    return Results.Json(new
    {
        path = fi.FullName,
        bytes = fi.Length,
        lastWriteUtc = fi.LastWriteTimeUtc.ToString("O"),
        preferUploadedCanonical = cfg.GetValue("App:QrApkPreferUploadedCanonical", false),
        excludeSolutionBin = cfg.GetValue("App:QrApkExcludeSolutionBin", true),
        minimumBytes = cfg.GetValue<long?>("App:QrApkMinimumBytes") ?? 0
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
        body.DeviceInstallId,
        body.DeviceName,
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

    var deviceInstallId = (req.Query["deviceInstallId"].ToString() ?? string.Empty).Trim();
    IReadOnlyList<NarrationPlayRow> rows;
    if (deviceInstallId.Length >= 8)
    {
        rows = await repo.ListPlaysForDeviceAsync(deviceInstallId, 500);
    }
    else
    {
        var q = req.Query["customerUserId"].ToString();
        if (string.IsNullOrWhiteSpace(q))
            q = req.Query["customer_user_id"].ToString();
        if (!int.TryParse(q, out var customerUserId) || customerUserId <= 0)
            return Results.BadRequest(new { message = "Thiếu customerUserId hoặc deviceInstallId (query)." });
        rows = await repo.ListPlaysForCustomerAsync(customerUserId, 500);
    }

    var payload = rows.Select(p => new
    {
        id = p.Id,
        deviceInstallId = p.DeviceInstallId,
        deviceName = p.DeviceName,
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
    if (body is null)
        return Results.BadRequest(new { message = "Body không hợp lệ." });
    var deviceInstallId = (body.DeviceInstallId ?? string.Empty).Trim();
    if ((body.CustomerUserId ?? 0) <= 0 && deviceInstallId.Length < 8)
        return Results.BadRequest(new { message = "Thiếu customerUserId hoặc deviceInstallId." });

    var json = System.Text.Json.JsonSerializer.Serialize(body.Points ?? []);
    if ((body.CustomerUserId ?? 0) > 0)
        await repo.UpsertRouteSnapshotAsync(body.CustomerUserId!.Value, json);
    if (deviceInstallId.Length >= 8)
        await repo.UpsertDeviceRouteSnapshotAsync(deviceInstallId, body.DeviceName, (body.CustomerUserId ?? 0) > 0 ? body.CustomerUserId : null, json);
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

// Heartbeat thiết bị app (Admin xem trang /Devices/Online).
app.MapPost("/api/devices/heartbeat", async (HttpRequest req, CustomerAccountRepository repo, IConfiguration config) =>
{
    if (!MobileKeyOk(req, config))
        return Results.Unauthorized();

    var body = await System.Text.Json.JsonSerializer.DeserializeAsync<DeviceHeartbeatBody>(
        req.Body,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (body is null || string.IsNullOrWhiteSpace(body.DeviceInstallId))
        return Results.BadRequest(new { message = "Thiếu deviceInstallId." });

    var onMapTab = body.IsOnMapTab == true;
    await repo.UpsertDeviceHeartbeatAsync(body.DeviceInstallId, body.Platform, body.AppVersion, onMapTab);
    return Results.Ok(new { ok = true });
});

// Danh sách thiết bị + trạng thái online (app MAUI đồng bộ với /Devices/Online).
app.MapGet("/api/devices/presence", async (HttpRequest req, CustomerAccountRepository repo, IConfiguration config) =>
{
    if (!MobileKeyOk(req, config))
        return Results.Unauthorized();

    var onlineWindow = TimeSpan.FromMinutes(2);
    var cutoffUtc = DateTime.UtcNow - onlineWindow;
    var rows = await repo.ListDevicePresenceAsync(500);
    var devices = rows.Select(d => new
    {
        deviceInstallId = d.DeviceInstallId,
        lastSeenUtc = d.LastSeenUtc.ToString("O"),
        isOnMapTab = d.IsOnMapTab,
        platform = d.Platform,
        appVersion = d.AppVersion,
        isOnlineOnMap = d.IsOnMapTab && d.LastSeenUtc >= cutoffUtc
    }).ToList();

    var onlineCount = devices.Count(x => x.isOnlineOnMap);
    return Results.Json(new
    {
        serverUtc = DateTime.UtcNow.ToString("O"),
        onlineWindowSeconds = (int)onlineWindow.TotalSeconds,
        onlineCount,
        offlineCount = devices.Count - onlineCount,
        devices
    });
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
    public string? DeviceInstallId { get; set; }
    public string? DeviceName { get; set; }
    public string? PlaceName { get; set; }
    public string? Source { get; set; }
    public string? Language { get; set; }
    public double? DurationSeconds { get; set; }
    public string? PlayedAtUtc { get; set; }
}

internal sealed class RouteSyncBody
{
    public int? CustomerUserId { get; set; }
    public string? DeviceInstallId { get; set; }
    public string? DeviceName { get; set; }
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

internal sealed class DeviceHeartbeatBody
{
    public string? DeviceInstallId { get; set; }
    public string? Platform { get; set; }
    public string? AppVersion { get; set; }
    /// <summary>Chỉ khi <c>true</c> mới coi là đang mở tab Bản đồ trên app.</summary>
    public bool? IsOnMapTab { get; set; }
}
