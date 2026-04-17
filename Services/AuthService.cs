using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TourGuideApp2.Data;
using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

public static class AuthService
{
    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        CmsTunnelHttp.ApplyTo(c);
        return c;
    }

    private const string SessionUserIdKey = "SessionUserId";
    private const string SessionUserNameKey = "SessionUserName";
    private const string SessionUserAccountKey = "SessionUserAccount";
    private const string SessionUserCreatedAtKey = "SessionUserCreatedAt";
    private const string SessionIsRemoteKey = "SessionIsRemote";
    private static bool _initialized;

    private static async Task InitAsync()
    {
        if (_initialized) return;

        await using var connection = new SqliteConnection(Constants.DatabasePath);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS UserAccount (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FullName TEXT NOT NULL,
    PhoneOrEmail TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    PasswordSalt TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);";
        await cmd.ExecuteNonQueryAsync();
        _initialized = true;
    }

    public static async Task<(bool Success, string Message)> RegisterAsync(string fullName, string phoneOrEmail, string password)
    {
        await InitAsync();
        fullName = (fullName ?? string.Empty).Trim();
        phoneOrEmail = (phoneOrEmail ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phoneOrEmail) || string.IsNullOrWhiteSpace(password))
            return (false, "Vui lòng nhập đầy đủ thông tin.");
        if (password.Length < 6)
            return (false, "Mật khẩu tối thiểu 6 ký tự.");

        if (GetAuthBaseUrls().Count > 0)
        {
            var remote = await RegisterRemoteAsync(fullName, phoneOrEmail, password);
            if (remote.Success || remote.ServerReachable)
                return (remote.Success, remote.Message);

            var local = await RegisterLocalAsync(fullName, phoneOrEmail, password);
            return local.Success
                ? (true, "Máy chủ tạm thời không kết nối, đã đăng ký cục bộ thành công.")
                : (false, $"Máy chủ tạm thời không kết nối. {local.Message}");
        }

        return await RegisterLocalAsync(fullName, phoneOrEmail, password);
    }

    public static async Task<(bool Success, string Message, UserAccount? User)> LoginAsync(string phoneOrEmail, string password)
    {
        await InitAsync();
        phoneOrEmail = (phoneOrEmail ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(phoneOrEmail) || string.IsNullOrWhiteSpace(password))
            return (false, "Vui lòng nhập tài khoản và mật khẩu.", null);

        if (GetAuthBaseUrls().Count > 0)
        {
            var remote = await LoginRemoteAsync(phoneOrEmail, password);
            if (remote.Success)
                return (true, remote.Message, remote.User);

            if (!remote.ServerReachable)
            {
                var local = await LoginLocalAsync(phoneOrEmail, password);
                if (!local.Success)
                    return (false,
                        "Không kết nối máy chủ hoặc URL sai. Kiểm tra Wi-Fi cùng mạng PC chạy CMS và URL API trong Cài đặt (4G không vào được IP LAN). Tài khoản chỉ có trên web nên chưa đăng nhập cục bộ được.",
                        null);

                var remote2 = await LoginRemoteAsync(phoneOrEmail, password);
                if (remote2.Success)
                    return (true, "Đã đăng nhập và liên kết máy chủ.", remote2.User);

                return (true, "Máy chủ tạm thời không kết nối, đã đăng nhập cục bộ.", local.User);
            }

            var localOnly = await LoginLocalAsync(phoneOrEmail, password);
            if (localOnly.Success)
                return (true, $"{remote.Message} Đã đăng nhập cục bộ.", localOnly.User);

            return (false, remote.Message, null);
        }

        return await LoginLocalAsync(phoneOrEmail, password);
    }

    private static async Task<(bool Success, string Message)> RegisterLocalAsync(string fullName, string phoneOrEmail, string password)
    {
        await using var connection = new SqliteConnection(Constants.DatabasePath);
        await connection.OpenAsync();

        var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(1) FROM UserAccount WHERE PhoneOrEmail = @phoneOrEmail";
        checkCmd.Parameters.AddWithValue("@phoneOrEmail", phoneOrEmail);
        var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
        if (exists)
            return (false, "Tài khoản đã tồn tại.");

        var salt = GenerateSalt();
        var hash = ComputeHash(password, salt);

        var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
INSERT INTO UserAccount (FullName, PhoneOrEmail, PasswordHash, PasswordSalt, CreatedAt)
VALUES (@fullName, @phoneOrEmail, @passwordHash, @passwordSalt, @createdAt);";
        insertCmd.Parameters.AddWithValue("@fullName", fullName);
        insertCmd.Parameters.AddWithValue("@phoneOrEmail", phoneOrEmail);
        insertCmd.Parameters.AddWithValue("@passwordHash", hash);
        insertCmd.Parameters.AddWithValue("@passwordSalt", salt);
        insertCmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("O"));
        await insertCmd.ExecuteNonQueryAsync();

        return (true, "Đăng ký thành công.");
    }

    private static async Task<(bool Success, string Message, UserAccount? User)> LoginLocalAsync(string phoneOrEmail, string password)
    {
        await using var connection = new SqliteConnection(Constants.DatabasePath);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT Id, FullName, PhoneOrEmail, PasswordHash, PasswordSalt, CreatedAt
FROM UserAccount
WHERE PhoneOrEmail = @phoneOrEmail
LIMIT 1;";
        cmd.Parameters.AddWithValue("@phoneOrEmail", phoneOrEmail);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (false, "Không tìm thấy tài khoản.", null);

        var user = new UserAccount
        {
            Id = reader.GetInt32(0),
            FullName = reader.GetString(1),
            PhoneOrEmail = reader.GetString(2),
            PasswordHash = reader.GetString(3),
            PasswordSalt = reader.GetString(4),
            CreatedAt = DateTime.TryParse(reader.GetString(5), out var dt) ? dt : DateTime.Now
        };

        var computed = ComputeHash(password, user.PasswordSalt);
        if (!string.Equals(computed, user.PasswordHash, StringComparison.Ordinal))
            return (false, "Sai mật khẩu.", null);

        SetSession(user, false);
        return (true, "Đăng nhập thành công.", user);
    }

    public static void Logout()
    {
        Preferences.Default.Remove(SessionUserIdKey);
        Preferences.Default.Remove(SessionUserNameKey);
        Preferences.Default.Remove(SessionUserAccountKey);
        Preferences.Default.Remove(SessionUserCreatedAtKey);
        Preferences.Default.Remove(SessionIsRemoteKey);
    }

    /// <summary>Chỉ khi đăng nhập qua máy chủ — dùng để đồng bộ lượt phát.</summary>
    public static int? GetCustomerIdForServerSync()
    {
        if (!IsLoggedIn)
            return null;
        if (!Preferences.Default.Get(SessionIsRemoteKey, false))
            return null;
        return Preferences.Default.Get(SessionUserIdKey, 0);
    }

    /// <summary>
    /// Khóa tệp tuyến: <c>null</c> = khách; <c>r_id</c> = đăng nhập qua máy chủ; <c>l_id</c> = chỉ cục bộ SQLite.
    /// </summary>
    public static string? GetRouteOwnerKey()
    {
        if (!IsLoggedIn)
            return null;
        var id = Preferences.Default.Get(SessionUserIdKey, 0);
        if (id <= 0)
            return null;
        return Preferences.Default.Get(SessionIsRemoteKey, false) ? $"r_{id}" : $"l_{id}";
    }

    public static bool IsLoggedIn => Preferences.Default.ContainsKey(SessionUserIdKey);
    public static string CurrentUserName => Preferences.Default.Get(SessionUserNameKey, string.Empty);
    public static string CurrentUserAccount => Preferences.Default.Get(SessionUserAccountKey, string.Empty);
    public static DateTime? CurrentUserCreatedAt
    {
        get
        {
            var raw = Preferences.Default.Get(SessionUserCreatedAtKey, string.Empty);
            if (DateTime.TryParse(raw, out var createdAt))
                return createdAt;
            return null;
        }
    }

    private static void SetSession(UserAccount user, bool isRemoteSession)
    {
        Preferences.Default.Set(SessionUserIdKey, user.Id);
        Preferences.Default.Set(SessionUserNameKey, user.FullName);
        Preferences.Default.Set(SessionUserAccountKey, user.PhoneOrEmail ?? string.Empty);
        Preferences.Default.Set(SessionUserCreatedAtKey, user.CreatedAt.ToString("O"));
        Preferences.Default.Set(SessionIsRemoteKey, isRemoteSession);
    }

    private static async Task<(bool Success, bool ServerReachable, string Message)> RegisterRemoteAsync(
        string fullName, string phoneOrEmail, string password)
    {
        var endpointUrls = GetAuthEndpointUrls("register");
        if (endpointUrls.Count == 0)
            return (false, false, "Chưa cấu hình URL máy chủ.");

        string lastConnectError = "Không kết nối máy chủ.";
        foreach (var url in endpointUrls)
        {
            try
            {
                if (!await IsServerReachableForAuthAsync(url))
                    continue;

                using var client = CreateHttpClient(TimeSpan.FromSeconds(15));
                var res = await client.PostAsJsonAsync(url, new { fullName, phoneOrEmail, password });
                if (res.IsSuccessStatusCode)
                {
                    await UpsertLocalAccountAsync(fullName, phoneOrEmail, password);
                    return (true, true, "Đăng ký thành công.");
                }

                // URL này có server nhưng sai endpoint, thử URL kế tiếp.
                if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                    continue;

                var msg = await TryReadApiMessageAsync(res);
                return (false, true, string.IsNullOrWhiteSpace(msg) ? "Đăng ký thất bại." : msg);
            }
            catch (Exception ex)
            {
                lastConnectError = $"Không kết nối máy chủ ({ex.Message}).";
            }
        }

        return (false, false, lastConnectError);
    }

    private static async Task<(bool Success, bool ServerReachable, string Message, UserAccount? User)> LoginRemoteAsync(
        string phoneOrEmail, string password)
    {
        var endpointUrls = GetAuthEndpointUrls("login");
        if (endpointUrls.Count == 0)
            return (false, false, "Chưa cấu hình URL máy chủ.", null);

        string lastConnectError = "Không kết nối máy chủ.";
        foreach (var url in endpointUrls)
        {
            try
            {
                if (!await IsServerReachableForAuthAsync(url))
                    continue;

                using var client = CreateHttpClient(TimeSpan.FromSeconds(15));
                var res = await client.PostAsJsonAsync(url, new { phoneOrEmail, password });
                if (!res.IsSuccessStatusCode)
                {
                    // URL này có server nhưng sai endpoint, thử URL kế tiếp.
                    if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                        continue;

                    var msg = await TryReadApiMessageAsync(res);
                    return (false, true, string.IsNullOrWhiteSpace(msg) ? "Đăng nhập thất bại." : msg, null);
                }

                var dto = await res.Content.ReadFromJsonAsync<RemoteAuthDto>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (dto is null)
                    return (false, true, "Phản hồi máy chủ không hợp lệ.", null);

                var created = DateTime.TryParse(dto.CreatedAt, out var c) ? c : DateTime.Now;
                var user = new UserAccount
                {
                    Id = dto.Id,
                    FullName = dto.FullName ?? string.Empty,
                    PhoneOrEmail = dto.PhoneOrEmail ?? string.Empty,
                    CreatedAt = created
                };
                await UpsertLocalAccountAsync(user.FullName, user.PhoneOrEmail, password, created);
                SetSession(user, true);
                CustomerRouteSyncService.ScheduleUploadAfterLocalSave();
                return (true, true, "Đăng nhập thành công.", user);
            }
            catch (Exception ex)
            {
                lastConnectError = $"Không kết nối máy chủ ({ex.Message}).";
            }
        }

        return (false, false, lastConnectError, null);
    }

    private static async Task<bool> IsServerReachableForAuthAsync(string endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var u))
            return false;

        var origin = $"{u.Scheme}://{u.Authority}".TrimEnd('/');
        if (string.IsNullOrWhiteSpace(origin))
            return false;

        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(3));
            // Ưu tiên /api/ping (bản mới), fallback /api/places (bản cũ).
            var ping = await client.GetAsync($"{origin}/api/ping");
            if (ping.IsSuccessStatusCode)
                return true;
        }
        catch
        {
            // ignore
        }

        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(3));
            var places = await client.GetAsync($"{origin}/api/places");
            return places.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> GetAuthEndpointUrls(string action)
    {
        var list = new List<string>();
        var actionPart = action.Trim().ToLowerInvariant();
        if (actionPart != "login" && actionPart != "register")
            return list;

        foreach (var b in GetAuthBaseUrls())
        {
            AddEndpointCandidate(list, $"{b}/api/customers/{actionPart}");
            AddEndpointCandidate(list, $"{b}/customers/{actionPart}");
        }

        // Nếu API POI nằm sau một prefix (vd /vk/api/places), giữ prefix đó cho auth.
        var effectiveApi = (PlaceApiService.GetEffectiveApiUrl() ?? string.Empty).Trim();
        if (Uri.TryCreate(effectiveApi, UriKind.Absolute, out var u))
        {
            var path = u.AbsolutePath.TrimEnd('/');
            var marker = "/api/places";
            var idx = path.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var prefix = path[..idx].TrimEnd('/');
                AddEndpointCandidate(list, $"{u.Scheme}://{u.Authority}{prefix}/api/customers/{actionPart}");
            }
        }

        return list;
    }

    private static void AddEndpointCandidate(List<string> list, string? raw)
    {
        var s = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(s))
            return;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u))
            return;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
            return;
        var normalized = u.ToString().TrimEnd('/');
        if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            list.Add(normalized);
    }

    private static List<string> GetAuthBaseUrls()
    {
        var list = new List<string>();
        foreach (var origin in PlaceApiService.GetCmsBaseUrlCandidatesForSync())
            AddBaseUrlCandidate(list, origin);
        AddBaseUrlCandidate(list, PlaceApiService.GetCmsBaseUrl());
        AddBaseUrlCandidate(list, PlaceApiService.GetCmsBaseUrlForListenPayLinks());
        AddBaseUrlCandidate(list, AppConfig.GetCmsOrigin());

        var apiUrl = PlaceApiService.GetEffectiveApiUrl();
        if (Uri.TryCreate(apiUrl, UriKind.Absolute, out var api))
            AddHostPortFallbacks(list, api.Host);

        return list;
    }

    private static void AddBaseUrlCandidate(List<string> list, string? raw)
    {
        var s = (raw ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(s))
            return;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u))
            return;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
            return;
        var normalized = $"{u.Scheme}://{u.Authority}";
        if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            list.Add(normalized);

        AddHostPortFallbacks(list, u.Host);
    }

    private static void AddHostPortFallbacks(List<string> list, string? host)
    {
        var h = (host ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(h))
            return;

        // Fallback cổng phổ biến của ASP.NET trong môi trường dev/demo.
        foreach (var c in new[]
                 {
                     $"http://{h}:5095",
                     $"http://{h}:5000",
                     $"http://{h}:5001",
                     $"https://{h}:5001",
                     $"https://{h}:5096"
                 })
        {
            if (!Uri.TryCreate(c, UriKind.Absolute, out _))
                continue;
            if (!list.Contains(c, StringComparer.OrdinalIgnoreCase))
                list.Add(c);
        }
    }

    private static async Task<string?> TryReadApiMessageAsync(HttpResponseMessage res)
    {
        try
        {
            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var m))
                return m.GetString();
        }
        catch
        {
            // bỏ qua
        }

        return null;
    }

    private sealed class RemoteAuthDto
    {
        public int Id { get; set; }
        public string? FullName { get; set; }
        public string? PhoneOrEmail { get; set; }
        public string? CreatedAt { get; set; }
    }

    private static string GenerateSalt()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes);
    }

    private static string ComputeHash(string password, string salt)
    {
        var input = $"{password}:{salt}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Mirror tài khoản remote về SQLite cục bộ để login được cả khi mất mạng.</summary>
    private static async Task UpsertLocalAccountAsync(
        string fullName,
        string phoneOrEmail,
        string password,
        DateTime? createdAtUtc = null)
    {
        fullName = (fullName ?? string.Empty).Trim();
        phoneOrEmail = (phoneOrEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phoneOrEmail) || string.IsNullOrWhiteSpace(password))
            return;

        await using var connection = new SqliteConnection(Constants.DatabasePath);
        await connection.OpenAsync();

        var createdAt = (createdAtUtc ?? DateTime.UtcNow).ToUniversalTime().ToString("O");
        var salt = GenerateSalt();
        var hash = ComputeHash(password, salt);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO UserAccount (FullName, PhoneOrEmail, PasswordHash, PasswordSalt, CreatedAt)
VALUES (@fullName, @phoneOrEmail, @passwordHash, @passwordSalt, @createdAt)
ON CONFLICT(PhoneOrEmail) DO UPDATE SET
    FullName = excluded.FullName,
    PasswordHash = excluded.PasswordHash,
    PasswordSalt = excluded.PasswordSalt,
    CreatedAt = excluded.CreatedAt;";
        cmd.Parameters.AddWithValue("@fullName", fullName);
        cmd.Parameters.AddWithValue("@phoneOrEmail", phoneOrEmail);
        cmd.Parameters.AddWithValue("@passwordHash", hash);
        cmd.Parameters.AddWithValue("@passwordSalt", salt);
        cmd.Parameters.AddWithValue("@createdAt", createdAt);
        await cmd.ExecuteNonQueryAsync();
    }
}
