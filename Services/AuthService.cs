using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TourGuideApp2.Data;
using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

public static class AuthService
{
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

        if (!string.IsNullOrWhiteSpace(PlaceApiService.GetCmsBaseUrl()))
            return await RegisterRemoteAsync(fullName, phoneOrEmail, password);

        return await RegisterLocalAsync(fullName, phoneOrEmail, password);
    }

    public static async Task<(bool Success, string Message, UserAccount? User)> LoginAsync(string phoneOrEmail, string password)
    {
        await InitAsync();
        phoneOrEmail = (phoneOrEmail ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(phoneOrEmail) || string.IsNullOrWhiteSpace(password))
            return (false, "Vui lòng nhập tài khoản và mật khẩu.", null);

        if (!string.IsNullOrWhiteSpace(PlaceApiService.GetCmsBaseUrl()))
            return await LoginRemoteAsync(phoneOrEmail, password);

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

    private static async Task<(bool Success, string Message)> RegisterRemoteAsync(
        string fullName, string phoneOrEmail, string password)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            var url = $"{PlaceApiService.GetCmsBaseUrl().TrimEnd('/')}/api/customers/register";
            var res = await client.PostAsJsonAsync(url, new { fullName, phoneOrEmail, password });
            if (res.IsSuccessStatusCode)
                return (true, "Đăng ký thành công.");

            var msg = await TryReadApiMessageAsync(res);
            return (false, string.IsNullOrWhiteSpace(msg) ? "Đăng ký thất bại." : msg);
        }
        catch (Exception ex)
        {
            return (false, $"Không kết nối máy chủ ({ex.Message}).");
        }
    }

    private static async Task<(bool Success, string Message, UserAccount? User)> LoginRemoteAsync(
        string phoneOrEmail, string password)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            var url = $"{PlaceApiService.GetCmsBaseUrl().TrimEnd('/')}/api/customers/login";
            var res = await client.PostAsJsonAsync(url, new { phoneOrEmail, password });
            if (!res.IsSuccessStatusCode)
            {
                var msg = await TryReadApiMessageAsync(res);
                return (false, string.IsNullOrWhiteSpace(msg) ? "Đăng nhập thất bại." : msg, null);
            }

            var dto = await res.Content.ReadFromJsonAsync<RemoteAuthDto>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto is null)
                return (false, "Phản hồi máy chủ không hợp lệ.", null);

            var created = DateTime.TryParse(dto.CreatedAt, out var c) ? c : DateTime.Now;
            var user = new UserAccount
            {
                Id = dto.Id,
                FullName = dto.FullName ?? string.Empty,
                PhoneOrEmail = dto.PhoneOrEmail ?? string.Empty,
                CreatedAt = created
            };
            SetSession(user, true);
            return (true, "Đăng nhập thành công.", user);
        }
        catch (Exception ex)
        {
            return (false, $"Không kết nối máy chủ ({ex.Message}).", null);
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
}
