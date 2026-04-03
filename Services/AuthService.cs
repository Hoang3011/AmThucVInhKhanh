using System.Security.Cryptography;
using System.Text;
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

        return await RegisterLocalAsync(fullName, phoneOrEmail, password);
    }

    public static async Task<(bool Success, string Message, UserAccount? User)> LoginAsync(string phoneOrEmail, string password)
    {
        await InitAsync();
        phoneOrEmail = (phoneOrEmail ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(phoneOrEmail) || string.IsNullOrWhiteSpace(password))
            return (false, "Vui lòng nhập tài khoản và mật khẩu.", null);

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

        SetSession(user);
        return (true, "Đăng nhập thành công.", user);
    }

    public static void Logout()
    {
        Preferences.Default.Remove(SessionUserIdKey);
        Preferences.Default.Remove(SessionUserNameKey);
        Preferences.Default.Remove(SessionUserAccountKey);
        Preferences.Default.Remove(SessionUserCreatedAtKey);
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

    private static void SetSession(UserAccount user)
    {
        Preferences.Default.Set(SessionUserIdKey, user.Id);
        Preferences.Default.Set(SessionUserNameKey, user.FullName);
        Preferences.Default.Set(SessionUserAccountKey, user.PhoneOrEmail ?? string.Empty);
        Preferences.Default.Set(SessionUserCreatedAtKey, user.CreatedAt.ToString("O"));
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
