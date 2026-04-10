using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TourGuideCMS.Services;

/// <summary>
/// Tài khoản khách (app MAUI) + log lượt thuyết minh đồng bộ từ điện thoại.
/// Cùng file SQLite với POI (<c>VinhKhanh.db</c> trong App_Data).
/// </summary>
public sealed class CustomerAccountRepository
{
    private readonly string _dbPath;
    private bool _schemaReady;

    public CustomerAccountRepository(IWebHostEnvironment env)
    {
        var appData = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, "VinhKhanh.db");

        var src = Path.Combine(env.ContentRootPath, "..", "TourGuideApp2", "VinhKhanh.db");
        if (!File.Exists(_dbPath) && File.Exists(src))
            File.Copy(src, _dbPath, overwrite: false);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        return c;
    }

    private async Task EnsureSchemaAsync(SqliteConnection connection)
    {
        if (_schemaReady) return;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS CustomerUser (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FullName TEXT NOT NULL,
                PhoneOrEmail TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                PasswordSalt TEXT NOT NULL,
                PasswordPlain TEXT,
                CreatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS NarrationPlay (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerUserId INTEGER,
                PlaceName TEXT NOT NULL,
                Source TEXT NOT NULL,
                Language TEXT,
                DurationSeconds REAL,
                PlayedAtUtc TEXT NOT NULL,
                FOREIGN KEY (CustomerUserId) REFERENCES CustomerUser(Id)
            );
            CREATE INDEX IF NOT EXISTS IX_NarrationPlay_Place ON NarrationPlay(PlaceName);
            CREATE INDEX IF NOT EXISTS IX_NarrationPlay_Source ON NarrationPlay(Source);
            CREATE INDEX IF NOT EXISTS IX_NarrationPlay_PlayedAt ON NarrationPlay(PlayedAtUtc);
            """;
        await cmd.ExecuteNonQueryAsync();

        // Migration cho DB cũ: bổ sung cột mật khẩu hiển thị trên CMS.
        if (!await ColumnExistsAsync(connection, "CustomerUser", "PasswordPlain"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE CustomerUser ADD COLUMN PasswordPlain TEXT";
            await alter.ExecuteNonQueryAsync();
        }
        _schemaReady = true;
    }

    public async Task EnsureSchemaAsync()
    {
        await using var c = Open();
        await EnsureSchemaAsync(c);
    }

    public async Task<IReadOnlyList<CustomerUserRow>> ListUsersAsync()
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<CustomerUserRow>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FullName, PhoneOrEmail, PasswordPlain, CreatedAtUtc
            FROM CustomerUser
            ORDER BY Id DESC
            """;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new CustomerUserRow(
                r.GetInt32(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                DateTime.TryParse(r.GetString(4), out var dt) ? dt : DateTime.MinValue));
        }

        return list;
    }

    public async Task<(bool Ok, string Message, CustomerUserRow? User)> RegisterAsync(
        string fullName, string phoneOrEmail, string password)
    {
        fullName = (fullName ?? string.Empty).Trim();
        phoneOrEmail = (phoneOrEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phoneOrEmail) ||
            string.IsNullOrWhiteSpace(password))
            return (false, "Thiếu thông tin.", null);
        if (password.Length < 6)
            return (false, "Mật khẩu tối thiểu 6 ký tự.", null);

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM CustomerUser WHERE PhoneOrEmail = @e";
            check.Parameters.AddWithValue("@e", phoneOrEmail);
            var n = Convert.ToInt32(await check.ExecuteScalarAsync());
            if (n > 0)
                return (false, "Tài khoản đã tồn tại.", null);
        }

        var salt = GenerateSalt();
        var hash = ComputeHash(password, salt);
        var created = DateTime.UtcNow;

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO CustomerUser (FullName, PhoneOrEmail, PasswordHash, PasswordSalt, PasswordPlain, CreatedAtUtc)
                VALUES (@n, @e, @h, @s, @p, @c)
                """;
            insert.Parameters.AddWithValue("@n", fullName);
            insert.Parameters.AddWithValue("@e", phoneOrEmail);
            insert.Parameters.AddWithValue("@h", hash);
            insert.Parameters.AddWithValue("@s", salt);
            insert.Parameters.AddWithValue("@p", password);
            insert.Parameters.AddWithValue("@c", created.ToString("O"));
            await insert.ExecuteNonQueryAsync();
        }

        await using var idCmd = connection.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var id = Convert.ToInt32(await idCmd.ExecuteScalarAsync());

        var user = new CustomerUserRow(id, fullName, phoneOrEmail, password, created);
        return (true, "Đăng ký thành công.", user);
    }

    public async Task<(bool Ok, string Message, CustomerUserRow? User)> LoginAsync(
        string phoneOrEmail, string password)
    {
        phoneOrEmail = (phoneOrEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(phoneOrEmail) || string.IsNullOrWhiteSpace(password))
            return (false, "Thiếu tài khoản hoặc mật khẩu.", null);

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FullName, PhoneOrEmail, PasswordHash, PasswordSalt, CreatedAtUtc
            FROM CustomerUser WHERE PhoneOrEmail = @e LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@e", phoneOrEmail);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return (false, "Không tìm thấy tài khoản.", null);

        var id = r.GetInt32(0);
        var fullName = r.GetString(1);
        var email = r.GetString(2);
        var hash = r.GetString(3);
        var salt = r.GetString(4);
        var created = DateTime.TryParse(r.GetString(5), out var c) ? c : DateTime.UtcNow;

        if (!string.Equals(ComputeHash(password, salt), hash, StringComparison.Ordinal))
            return (false, "Sai mật khẩu.", null);

        // Tài khoản cũ (trước khi thêm cột PasswordPlain) sẽ được backfill khi đăng nhập thành công.
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE CustomerUser
                SET PasswordPlain = @p
                WHERE Id = @id
                """;
            update.Parameters.AddWithValue("@p", password);
            update.Parameters.AddWithValue("@id", id);
            await update.ExecuteNonQueryAsync();
        }

        return (true, "Đăng nhập thành công.", new CustomerUserRow(id, fullName, email, password, created));
    }

    public async Task AddPlayAsync(int? customerUserId, string placeName, string source, string? language,
        double? durationSeconds, DateTime playedAtUtc)
    {
        placeName = (placeName ?? string.Empty).Trim();
        source = (source ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(placeName) || string.IsNullOrEmpty(source))
            return;

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        int? uid = customerUserId;
        if (uid.HasValue)
        {
            await using var ck = connection.CreateCommand();
            ck.CommandText = "SELECT COUNT(1) FROM CustomerUser WHERE Id = @id";
            ck.Parameters.AddWithValue("@id", uid.Value);
            var exists = Convert.ToInt32(await ck.ExecuteScalarAsync()) > 0;
            if (!exists)
                uid = null;
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NarrationPlay (CustomerUserId, PlaceName, Source, Language, DurationSeconds, PlayedAtUtc)
            VALUES (@u, @p, @s, @l, @d, @t)
            """;
        cmd.Parameters.AddWithValue("@u", uid.HasValue ? uid.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@p", placeName);
        cmd.Parameters.AddWithValue("@s", source);
        cmd.Parameters.AddWithValue("@l", language ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@d", durationSeconds.HasValue ? durationSeconds.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@t", playedAtUtc.ToUniversalTime().ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<NarrationPlayRow>> ListRecentPlaysAsync(int take = 300)
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<NarrationPlayRow>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.Id, p.CustomerUserId, p.PlaceName, p.Source, p.Language, p.DurationSeconds, p.PlayedAtUtc,
                   u.PhoneOrEmail
            FROM NarrationPlay p
            LEFT JOIN CustomerUser u ON u.Id = p.CustomerUserId
            ORDER BY p.PlayedAtUtc DESC
            LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@lim", take);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new NarrationPlayRow(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetInt32(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetDouble(5),
                DateTime.TryParse(r.GetString(6), out var pt) ? pt : DateTime.MinValue,
                r.IsDBNull(7) ? null : r.GetString(7)));
        }

        return list;
    }

    public async Task<IReadOnlyList<NarrationPlayRow>> ListPlaysForCustomerAsync(int customerUserId, int take = 500)
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<NarrationPlayRow>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.Id, p.CustomerUserId, p.PlaceName, p.Source, p.Language, p.DurationSeconds, p.PlayedAtUtc,
                   u.PhoneOrEmail
            FROM NarrationPlay p
            LEFT JOIN CustomerUser u ON u.Id = p.CustomerUserId
            WHERE p.CustomerUserId = @uid
            ORDER BY p.PlayedAtUtc DESC
            LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@uid", customerUserId);
        cmd.Parameters.AddWithValue("@lim", take);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new NarrationPlayRow(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetInt32(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetDouble(5),
                DateTime.TryParse(r.GetString(6), out var pt) ? pt : DateTime.MinValue,
                r.IsDBNull(7) ? null : r.GetString(7)));
        }

        return list;
    }

    /// <summary>Tổng lượt phát theo địa điểm và nguồn (QR, Map, …).</summary>
    public async Task<IReadOnlyList<PlayAggregateRow>> GetAggregatesByPlaceAsync()
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<PlayAggregateRow>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT PlaceName, Source, COUNT(*) AS Cnt
            FROM NarrationPlay
            GROUP BY PlaceName, Source
            ORDER BY Cnt DESC, PlaceName, Source
            """;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new PlayAggregateRow(r.GetString(0), r.GetString(1), r.GetInt32(2)));
        }

        return list;
    }

    public async Task<IReadOnlyList<PlayAggregateRow>> GetAggregatesForPlaceAsync(string placeName)
    {
        placeName = (placeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(placeName))
            return [];

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<PlayAggregateRow>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT PlaceName, Source, COUNT(*) AS Cnt
            FROM NarrationPlay
            WHERE PlaceName = @place
            GROUP BY PlaceName, Source
            ORDER BY Cnt DESC, Source
            """;
        cmd.Parameters.AddWithValue("@place", placeName);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new PlayAggregateRow(r.GetString(0), r.GetString(1), r.GetInt32(2)));
        }

        return list;
    }

    public async Task<IReadOnlyList<NarrationPlayRow>> ListRecentPlaysForPlaceAsync(string placeName, int take = 200)
    {
        placeName = (placeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(placeName))
            return [];

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<NarrationPlayRow>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.Id, p.CustomerUserId, p.PlaceName, p.Source, p.Language, p.DurationSeconds, p.PlayedAtUtc,
                   u.PhoneOrEmail
            FROM NarrationPlay p
            LEFT JOIN CustomerUser u ON u.Id = p.CustomerUserId
            WHERE p.PlaceName = @place
            ORDER BY p.PlayedAtUtc DESC
            LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@place", placeName);
        cmd.Parameters.AddWithValue("@lim", take);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new NarrationPlayRow(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetInt32(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetDouble(5),
                DateTime.TryParse(r.GetString(6), out var pt) ? pt : DateTime.MinValue,
                r.IsDBNull(7) ? null : r.GetString(7)));
        }

        return list;
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

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

public sealed record CustomerUserRow(int Id, string FullName, string PhoneOrEmail, string? PasswordPlain, DateTime CreatedAtUtc);

public sealed record NarrationPlayRow(
    int Id,
    int? CustomerUserId,
    string PlaceName,
    string Source,
    string? Language,
    double? DurationSeconds,
    DateTime PlayedAtUtc,
    string? CustomerAccount);

public sealed record PlayAggregateRow(string PlaceName, string Source, int Count);
