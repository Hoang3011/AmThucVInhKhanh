using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TourGuideCMS.Services;

public sealed class CmsIdentityRepository
{
    private readonly string _dbPath;
    private bool _schemaReady;

    public CmsIdentityRepository(IWebHostEnvironment env)
    {
        var appData = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, "VinhKhanh.db");
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        return c;
    }

    public async Task EnsureSchemaAsync()
    {
        await using var c = Open();
        if (_schemaReady) return;

        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS CmsAdminAccount (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                PasswordSalt TEXT NOT NULL,
                PasswordPlain TEXT,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PoiOwnerAccount (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlaceId INTEGER NOT NULL UNIQUE,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                PasswordSalt TEXT NOT NULL,
                PasswordPlain TEXT,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_PoiOwnerAccount_PlaceId ON PoiOwnerAccount(PlaceId);
            CREATE INDEX IF NOT EXISTS IX_PoiOwnerAccount_Username ON PoiOwnerAccount(Username);
            """;
        await cmd.ExecuteNonQueryAsync();
        _schemaReady = true;
    }

    public async Task EnsureAdminSeedAsync(string? adminPasswordFromConfig)
    {
        await EnsureSchemaAsync();
        await using var c = Open();

        await using var count = c.CreateCommand();
        count.CommandText = "SELECT COUNT(1) FROM CmsAdminAccount WHERE IsActive = 1";
        var n = Convert.ToInt32(await count.ExecuteScalarAsync());
        if (n > 0) return;

        var password = string.IsNullOrWhiteSpace(adminPasswordFromConfig) ? "admin123" : adminPasswordFromConfig.Trim();
        var salt = GenerateSalt();
        var hash = ComputeHash(password, salt);
        var now = DateTime.UtcNow.ToString("O");

        await using var ins = c.CreateCommand();
        ins.CommandText = """
            INSERT INTO CmsAdminAccount (Username, PasswordHash, PasswordSalt, PasswordPlain, IsActive, CreatedAtUtc)
            VALUES ('admin', @h, @s, @p, 1, @c)
            """;
        ins.Parameters.AddWithValue("@h", hash);
        ins.Parameters.AddWithValue("@s", salt);
        ins.Parameters.AddWithValue("@p", password);
        ins.Parameters.AddWithValue("@c", now);
        await ins.ExecuteNonQueryAsync();
    }

    public async Task SyncOwnersForPlacesAsync(IReadOnlyList<Models.PlaceRow> places)
    {
        await EnsureSchemaAsync();
        await using var c = Open();
        var now = DateTime.UtcNow.ToString("O");

        foreach (var p in places)
        {
            await using var check = c.CreateCommand();
            check.CommandText = "SELECT COUNT(1) FROM PoiOwnerAccount WHERE PlaceId = @pid";
            check.Parameters.AddWithValue("@pid", p.Id);
            var exists = Convert.ToInt32(await check.ExecuteScalarAsync()) > 0;
            if (exists) continue;

            var username = $"owner{p.Id}";
            var password = $"poi{p.Id}@123";
            var salt = GenerateSalt();
            var hash = ComputeHash(password, salt);

            await using var ins = c.CreateCommand();
            ins.CommandText = """
                INSERT INTO PoiOwnerAccount (PlaceId, Username, PasswordHash, PasswordSalt, PasswordPlain, IsActive, CreatedAtUtc, UpdatedAtUtc)
                VALUES (@pid, @u, @h, @s, @p, 1, @c, @u2)
                """;
            ins.Parameters.AddWithValue("@pid", p.Id);
            ins.Parameters.AddWithValue("@u", username);
            ins.Parameters.AddWithValue("@h", hash);
            ins.Parameters.AddWithValue("@s", salt);
            ins.Parameters.AddWithValue("@p", password);
            ins.Parameters.AddWithValue("@c", now);
            ins.Parameters.AddWithValue("@u2", now);
            await ins.ExecuteNonQueryAsync();
        }

        var placeIds = places.Select(x => x.Id).ToHashSet();
        await using var list = c.CreateCommand();
        list.CommandText = "SELECT PlaceId FROM PoiOwnerAccount";
        await using var r = await list.ExecuteReaderAsync();
        var toDisable = new List<int>();
        while (await r.ReadAsync())
        {
            var pid = r.GetInt32(0);
            if (!placeIds.Contains(pid))
                toDisable.Add(pid);
        }
        await r.DisposeAsync();

        foreach (var pid in toDisable)
            await DisableOwnerByPlaceAsync(pid);
    }

    public async Task DisableOwnerByPlaceAsync(int placeId)
    {
        await EnsureSchemaAsync();
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE PoiOwnerAccount
            SET IsActive = 0, UpdatedAtUtc = @t
            WHERE PlaceId = @pid
            """;
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@pid", placeId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(bool Ok, string Message)> ValidateAdminAsync(string username, string password)
    {
        await EnsureSchemaAsync();
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT PasswordHash, PasswordSalt, IsActive
            FROM CmsAdminAccount
            WHERE Username = @u
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@u", (username ?? "").Trim().ToLowerInvariant());
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return (false, "Sai tài khoản hoặc mật khẩu.");

        var hash = r.GetString(0);
        var salt = r.GetString(1);
        var active = r.GetInt32(2) == 1;
        if (!active)
            return (false, "Tài khoản admin đã bị khóa.");
        if (!string.Equals(ComputeHash(password, salt), hash, StringComparison.Ordinal))
            return (false, "Sai tài khoản hoặc mật khẩu.");
        return (true, "OK");
    }

    public async Task<(bool Ok, string Message, OwnerAccountInfo? Owner)> ValidateOwnerAsync(string username, string password)
    {
        await EnsureSchemaAsync();
        await using var c = Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT Id, PlaceId, Username, PasswordHash, PasswordSalt, PasswordPlain, IsActive
            FROM PoiOwnerAccount
            WHERE Username = @u
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@u", (username ?? "").Trim().ToLowerInvariant());
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return (false, "Sai tài khoản hoặc mật khẩu.", null);

        var id = r.GetInt32(0);
        var placeId = r.GetInt32(1);
        var uname = r.GetString(2);
        var hash = r.GetString(3);
        var salt = r.GetString(4);
        var plain = r.IsDBNull(5) ? null : r.GetString(5);
        var active = r.GetInt32(6) == 1;
        if (!active)
            return (false, "Tài khoản chủ quán đã bị vô hiệu hóa.", null);
        if (!string.Equals(ComputeHash(password, salt), hash, StringComparison.Ordinal))
            return (false, "Sai tài khoản hoặc mật khẩu.", null);

        if (!string.Equals(plain, password, StringComparison.Ordinal))
        {
            await using var up = c.CreateCommand();
            up.CommandText = "UPDATE PoiOwnerAccount SET PasswordPlain = @p, UpdatedAtUtc = @t WHERE Id = @id";
            up.Parameters.AddWithValue("@p", password);
            up.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
            up.Parameters.AddWithValue("@id", id);
            await up.ExecuteNonQueryAsync();
        }

        return (true, "OK", new OwnerAccountInfo(id, placeId, uname));
    }

    public async Task<IReadOnlyList<OwnerAccountRow>> ListOwnersAsync()
    {
        await EnsureSchemaAsync();
        await using var c = Open();
        var list = new List<OwnerAccountRow>();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT Id, PlaceId, Username, PasswordPlain, IsActive, CreatedAtUtc, UpdatedAtUtc
            FROM PoiOwnerAccount
            ORDER BY PlaceId ASC
            """;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new OwnerAccountRow(
                r.GetInt32(0),
                r.GetInt32(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetInt32(4) == 1,
                DateTime.TryParse(r.GetString(5), out var cAt) ? cAt : DateTime.MinValue,
                DateTime.TryParse(r.GetString(6), out var uAt) ? uAt : DateTime.MinValue
            ));
        }
        return list;
    }

    public async Task<bool> ToggleOwnerActiveAsync(int placeId)
    {
        await EnsureSchemaAsync();
        await using var c = Open();
        await using var q = c.CreateCommand();
        q.CommandText = "SELECT IsActive FROM PoiOwnerAccount WHERE PlaceId = @pid LIMIT 1";
        q.Parameters.AddWithValue("@pid", placeId);
        var o = await q.ExecuteScalarAsync();
        if (o is null || o is DBNull) return false;
        var current = Convert.ToInt32(o) == 1;

        await using var up = c.CreateCommand();
        up.CommandText = "UPDATE PoiOwnerAccount SET IsActive = @a, UpdatedAtUtc = @t WHERE PlaceId = @pid";
        up.Parameters.AddWithValue("@a", current ? 0 : 1);
        up.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        up.Parameters.AddWithValue("@pid", placeId);
        await up.ExecuteNonQueryAsync();
        return !current;
    }

    public async Task<string?> ResetOwnerPasswordAsync(int placeId)
    {
        await EnsureSchemaAsync();
        var newPassword = $"poi{placeId}@123";
        await using var c = Open();
        var salt = GenerateSalt();
        var hash = ComputeHash(newPassword, salt);
        await using var up = c.CreateCommand();
        up.CommandText = """
            UPDATE PoiOwnerAccount
            SET PasswordHash = @h, PasswordSalt = @s, PasswordPlain = @p, IsActive = 1, UpdatedAtUtc = @t
            WHERE PlaceId = @pid
            """;
        up.Parameters.AddWithValue("@h", hash);
        up.Parameters.AddWithValue("@s", salt);
        up.Parameters.AddWithValue("@p", newPassword);
        up.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        up.Parameters.AddWithValue("@pid", placeId);
        var affected = await up.ExecuteNonQueryAsync();
        return affected > 0 ? newPassword : null;
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

public sealed record OwnerAccountInfo(int Id, int PlaceId, string Username);

public sealed record OwnerAccountRow(
    int Id,
    int PlaceId,
    string Username,
    string? PasswordPlain,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
