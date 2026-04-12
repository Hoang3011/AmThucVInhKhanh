using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using QRCoder;
using TourGuideCMS.Services;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

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

static bool IsHostUnusableForPhoneQr(string? host)
{
    if (string.IsNullOrEmpty(host)) return true;
    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
    if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;
    if (host.Equals("::1", StringComparison.OrdinalIgnoreCase)) return true;
    if (host.Equals("[::1]", StringComparison.OrdinalIgnoreCase)) return true;
    // Emulator → máy dev; điện thoại thật quét QR không mở được.
    if (host.Equals("10.0.2.2", StringComparison.OrdinalIgnoreCase)) return true;
    return false;
}

static string SiteRootForLinks(HttpContext ctx, IConfiguration config)
{
    var configured = (config["App:PublicBaseUrl"] ?? "").Trim().TrimEnd('/');
    if (!string.IsNullOrEmpty(configured))
        return configured;

    var requestHost = ctx.Request.Host.Host;
    if (!IsHostUnusableForPhoneQr(requestHost))
        return $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}{ctx.Request.PathBase}".TrimEnd('/');

    // Trình duyệt mở bằng localhost nhưng QR/Zalo cần URL LAN hoặc domain — cấu hình App:DevelopmentPublicBaseUrl (Development).
    var devPublic = (config["App:DevelopmentPublicBaseUrl"] ?? "").Trim().TrimEnd('/');
    if (!string.IsNullOrEmpty(devPublic))
        return devPublic;

    return $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}{ctx.Request.PathBase}".TrimEnd('/');
}

static string ListenPayPayload(HttpContext ctx, IConfiguration config, int placeId)
    => $"{SiteRootForLinks(ctx, config)}/Listen/Pay?placeId={placeId}";

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
        qrPayload = ListenPayPayload(http, config, r.Id)
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

    var content = ListenPayPayload(http, config, place.Id);
    using var gen = new QRCodeGenerator();
    using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
    var png = new PngByteQRCode(data);
    var bytes = png.GetGraphic(8);
    return Results.File(bytes, "image/png");
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
