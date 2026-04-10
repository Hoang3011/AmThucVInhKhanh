using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using QRCoder;
using TourGuideCMS.Services;

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

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// API JSON cho app MAUI (PostgREST-style): cấu hình URL trong app trỏ tới https://.../api/places
app.MapGet("/api/places", async (PlaceRepository repo) =>
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
        qrPayload = $"app://poi?id={r.Id}"
    });
    return Results.Json(payload, new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
});

// QR PNG cho từng POI: dùng payload ổn định theo Id (không phụ thuộc index).
app.MapGet("/qr/places/{id:int}", async (int id, PlaceRepository repo) =>
{
    var place = await repo.GetAsync(id);
    if (place is null)
        return Results.NotFound();

    var content = $"app://poi?id={place.Id}";
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
