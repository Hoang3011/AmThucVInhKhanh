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
        if (!_schemaReady)
        {
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
                    DeviceInstallId TEXT,
                    DeviceName TEXT,
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
                CREATE TABLE IF NOT EXISTS CustomerRouteSnapshot (
                    CustomerUserId INTEGER PRIMARY KEY,
                    PointsJson TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (CustomerUserId) REFERENCES CustomerUser(Id)
                );
                CREATE TABLE IF NOT EXISTS DeviceRouteSnapshot (
                    DeviceInstallId TEXT PRIMARY KEY,
                    DeviceName TEXT,
                    CustomerUserId INTEGER,
                    PointsJson TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (CustomerUserId) REFERENCES CustomerUser(Id)
                );
                CREATE INDEX IF NOT EXISTS IX_DeviceRouteSnapshot_Customer ON DeviceRouteSnapshot(CustomerUserId);
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

        if (!await ColumnExistsAsync(connection, "NarrationPlay", "DeviceInstallId"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE NarrationPlay ADD COLUMN DeviceInstallId TEXT";
            await alter.ExecuteNonQueryAsync();
        }

        if (!await ColumnExistsAsync(connection, "NarrationPlay", "DeviceName"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE NarrationPlay ADD COLUMN DeviceName TEXT";
            await alter.ExecuteNonQueryAsync();
        }

        await using (var idx = connection.CreateCommand())
        {
            idx.CommandText = "CREATE INDEX IF NOT EXISTS IX_NarrationPlay_Device ON NarrationPlay(DeviceInstallId)";
            await idx.ExecuteNonQueryAsync();
        }

        await EnsurePoiPremiumPaymentSchemaAsync(connection);
        await EnsureDeviceHeartbeatSchemaAsync(connection);
    }

    /// <summary>DB cũ có thể chưa có bảng thanh toán — luôn gọi idempotent.</summary>
    private static async Task EnsurePoiPremiumPaymentSchemaAsync(SqliteConnection connection)
    {
        await using var prem = connection.CreateCommand();
        prem.CommandText = """
            CREATE TABLE IF NOT EXISTS PoiPremiumPayment (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlaceId INTEGER NOT NULL,
                DeviceInstallId TEXT NOT NULL,
                AmountVnd REAL NOT NULL,
                CustomerUserId INTEGER,
                PaidAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_PoiPremiumPayment_Place ON PoiPremiumPayment(PlaceId);
            CREATE INDEX IF NOT EXISTS IX_PoiPremiumPayment_Device ON PoiPremiumPayment(DeviceInstallId);
            CREATE INDEX IF NOT EXISTS IX_PoiPremiumPayment_Customer ON PoiPremiumPayment(CustomerUserId);
            """;
        await prem.ExecuteNonQueryAsync();
    }

    private static async Task EnsureDeviceHeartbeatSchemaAsync(SqliteConnection connection)
    {
        await using var hb = connection.CreateCommand();
        hb.CommandText = """
            CREATE TABLE IF NOT EXISTS AppDeviceHeartbeat (
                DeviceInstallId TEXT NOT NULL PRIMARY KEY,
                LastSeenUtc TEXT NOT NULL,
                Platform TEXT,
                AppVersion TEXT,
                IsOnMap INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_AppDeviceHeartbeat_LastSeen ON AppDeviceHeartbeat(LastSeenUtc);
            """;
        await hb.ExecuteNonQueryAsync();

        if (!await ColumnExistsAsync(connection, "AppDeviceHeartbeat", "IsOnMap"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE AppDeviceHeartbeat ADD COLUMN IsOnMap INTEGER NOT NULL DEFAULT 0";
            await alter.ExecuteNonQueryAsync();
        }
    }

    /// <summary>App MAUI gửi khi đang / không đang ở tab Bản đồ.</summary>
    public async Task UpsertDeviceHeartbeatAsync(string deviceInstallId, string? platform, string? appVersion, bool isOnMapTab)
    {
        var id = (deviceInstallId ?? string.Empty).Trim();
        if (id.Length < 8)
            return;

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var utc = DateTime.UtcNow.ToString("O");
        var mapFlag = isOnMapTab ? 1 : 0;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppDeviceHeartbeat (DeviceInstallId, LastSeenUtc, Platform, AppVersion, IsOnMap)
            VALUES (@d, @t, @p, @v, @m)
            ON CONFLICT(DeviceInstallId) DO UPDATE SET
                LastSeenUtc = excluded.LastSeenUtc,
                Platform = COALESCE(excluded.Platform, AppDeviceHeartbeat.Platform),
                AppVersion = COALESCE(excluded.AppVersion, AppDeviceHeartbeat.AppVersion),
                IsOnMap = excluded.IsOnMap;
            """;
        cmd.Parameters.AddWithValue("@d", id);
        cmd.Parameters.AddWithValue("@t", utc);
        cmd.Parameters.AddWithValue("@p", string.IsNullOrWhiteSpace(platform) ? (object)DBNull.Value : platform.Trim());
        cmd.Parameters.AddWithValue("@v", string.IsNullOrWhiteSpace(appVersion) ? (object)DBNull.Value : appVersion.Trim());
        cmd.Parameters.AddWithValue("@m", mapFlag);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Thiết bị đã từng gửi heartbeat (mới nhất trước), kèm cờ tab Bản đồ từ lần ping cuối.</summary>
    public async Task<IReadOnlyList<DevicePresenceRow>> ListDevicePresenceAsync(int maxRows = 500)
    {
        maxRows = Math.Clamp(maxRows, 1, 2000);
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<DevicePresenceRow>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT DeviceInstallId, LastSeenUtc, IsOnMap, Platform, AppVersion
            FROM AppDeviceHeartbeat
            ORDER BY datetime(LastSeenUtc) DESC
            LIMIT @lim;
            """;
        cmd.Parameters.AddWithValue("@lim", maxRows);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var devId = r.GetString(0);
            var lsRaw = r.GetString(1);
            var ls = DateTime.TryParse(lsRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUniversalTime()
                : DateTime.MinValue;
            var onMap = !r.IsDBNull(2) && r.GetInt32(2) != 0;
            list.Add(new DevicePresenceRow(
                devId,
                ls,
                onMap,
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4)));
        }

        return list;
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

    public async Task AddPlayAsync(int? customerUserId, string? deviceInstallId, string? deviceName, string placeName, string source, string? language,
        double? durationSeconds, DateTime playedAtUtc)
    {
        placeName = (placeName ?? string.Empty).Trim();
        source = (source ?? string.Empty).Trim();
        deviceInstallId = (deviceInstallId ?? string.Empty).Trim();
        deviceName = (deviceName ?? string.Empty).Trim();
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
            INSERT INTO NarrationPlay (CustomerUserId, DeviceInstallId, DeviceName, PlaceName, Source, Language, DurationSeconds, PlayedAtUtc)
            VALUES (@u, @di, @dn, @p, @s, @l, @d, @t)
            """;
        cmd.Parameters.AddWithValue("@u", uid.HasValue ? uid.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@di", string.IsNullOrWhiteSpace(deviceInstallId) ? DBNull.Value : deviceInstallId);
        cmd.Parameters.AddWithValue("@dn", string.IsNullOrWhiteSpace(deviceName) ? DBNull.Value : deviceName);
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
            SELECT p.Id, p.CustomerUserId, p.DeviceInstallId, p.DeviceName, p.PlaceName, p.Source, p.Language, p.DurationSeconds, p.PlayedAtUtc,
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
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetDouble(7),
                DateTime.TryParse(r.GetString(8), out var pt) ? pt : DateTime.MinValue,
                r.IsDBNull(9) ? null : r.GetString(9)));
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
            SELECT p.Id, p.CustomerUserId, p.DeviceInstallId, p.DeviceName, p.PlaceName, p.Source, p.Language, p.DurationSeconds, p.PlayedAtUtc,
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
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetDouble(7),
                DateTime.TryParse(r.GetString(8), out var pt) ? pt : DateTime.MinValue,
                r.IsDBNull(9) ? null : r.GetString(9)));
        }

        return list;
    }

    public async Task<IReadOnlyList<NarrationPlayRow>> ListPlaysForDeviceAsync(string deviceInstallId, int take = 500)
    {
        deviceInstallId = (deviceInstallId ?? string.Empty).Trim();
        if (deviceInstallId.Length < 8)
            return [];

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<NarrationPlayRow>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.Id, p.CustomerUserId, p.DeviceInstallId, p.DeviceName, p.PlaceName, p.Source, p.Language, p.DurationSeconds, p.PlayedAtUtc,
                   u.PhoneOrEmail
            FROM NarrationPlay p
            LEFT JOIN CustomerUser u ON u.Id = p.CustomerUserId
            WHERE p.DeviceInstallId = @d
            ORDER BY p.PlayedAtUtc DESC
            LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@d", deviceInstallId);
        cmd.Parameters.AddWithValue("@lim", take);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new NarrationPlayRow(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetDouble(7),
                DateTime.TryParse(r.GetString(8), out var pt) ? pt : DateTime.MinValue,
                r.IsDBNull(9) ? null : r.GetString(9)));
        }

        return list;
    }

    public async Task<CustomerUserRow?> GetUserByIdAsync(int id)
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FullName, PhoneOrEmail, PasswordPlain, CreatedAtUtc
            FROM CustomerUser WHERE Id = @id LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return null;

        return new CustomerUserRow(
            r.GetInt32(0),
            r.GetString(1),
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            DateTime.TryParse(r.GetString(4), out var dt) ? dt : DateTime.MinValue);
    }

    public async Task UpsertRouteSnapshotAsync(int customerUserId, string pointsJson)
    {
        pointsJson ??= "[]";
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using var ck = connection.CreateCommand();
        ck.CommandText = "SELECT COUNT(1) FROM CustomerUser WHERE Id = @id";
        ck.Parameters.AddWithValue("@id", customerUserId);
        if (Convert.ToInt32(await ck.ExecuteScalarAsync()) == 0)
            return;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CustomerRouteSnapshot (CustomerUserId, PointsJson, UpdatedAtUtc)
            VALUES (@id, @j, @u)
            ON CONFLICT(CustomerUserId) DO UPDATE SET
                PointsJson = excluded.PointsJson,
                UpdatedAtUtc = excluded.UpdatedAtUtc
            """;
        cmd.Parameters.AddWithValue("@id", customerUserId);
        cmd.Parameters.AddWithValue("@j", pointsJson);
        cmd.Parameters.AddWithValue("@u", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpsertDeviceRouteSnapshotAsync(string deviceInstallId, string? deviceName, int? customerUserId, string pointsJson)
    {
        deviceInstallId = (deviceInstallId ?? string.Empty).Trim();
        if (deviceInstallId.Length < 8)
            return;
        pointsJson ??= "[]";
        deviceName = (deviceName ?? string.Empty).Trim();

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        int? uid = customerUserId;
        if (uid.HasValue)
        {
            await using var ck = connection.CreateCommand();
            ck.CommandText = "SELECT COUNT(1) FROM CustomerUser WHERE Id = @id";
            ck.Parameters.AddWithValue("@id", uid.Value);
            if (Convert.ToInt32(await ck.ExecuteScalarAsync()) == 0)
                uid = null;
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO DeviceRouteSnapshot (DeviceInstallId, DeviceName, CustomerUserId, PointsJson, UpdatedAtUtc)
            VALUES (@d, @n, @u, @j, @t)
            ON CONFLICT(DeviceInstallId) DO UPDATE SET
                DeviceName = CASE
                    WHEN excluded.DeviceName IS NULL OR trim(excluded.DeviceName) = '' THEN DeviceRouteSnapshot.DeviceName
                    ELSE excluded.DeviceName
                END,
                CustomerUserId = excluded.CustomerUserId,
                PointsJson = excluded.PointsJson,
                UpdatedAtUtc = excluded.UpdatedAtUtc
            """;
        cmd.Parameters.AddWithValue("@d", deviceInstallId);
        cmd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(deviceName) ? DBNull.Value : deviceName);
        cmd.Parameters.AddWithValue("@u", uid.HasValue ? uid.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@j", pointsJson);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetRouteSnapshotJsonAsync(int customerUserId)
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT PointsJson FROM CustomerRouteSnapshot WHERE CustomerUserId = @id LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@id", customerUserId);
        var o = await cmd.ExecuteScalarAsync();
        return o as string;
    }

    public async Task DeleteRouteSnapshotAsync(int customerUserId)
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM CustomerRouteSnapshot WHERE CustomerUserId = @id";
        cmd.Parameters.AddWithValue("@id", customerUserId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetRouteSnapshotJsonByDeviceAsync(string deviceInstallId)
    {
        deviceInstallId = (deviceInstallId ?? string.Empty).Trim();
        if (deviceInstallId.Length < 8)
            return null;

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT PointsJson FROM DeviceRouteSnapshot WHERE DeviceInstallId = @id LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@id", deviceInstallId);
        var o = await cmd.ExecuteScalarAsync();
        return o as string;
    }

    public async Task<DeviceRouteSnapshotRow?> GetDeviceRouteSnapshotAsync(string deviceInstallId)
    {
        deviceInstallId = (deviceInstallId ?? string.Empty).Trim();
        if (deviceInstallId.Length < 8)
            return null;

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT d.DeviceInstallId, d.DeviceName, d.CustomerUserId, u.FullName, u.PhoneOrEmail, d.UpdatedAtUtc
            FROM DeviceRouteSnapshot d
            LEFT JOIN CustomerUser u ON u.Id = d.CustomerUserId
            WHERE d.DeviceInstallId = @id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@id", deviceInstallId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return null;

        return new DeviceRouteSnapshotRow(
            r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetInt32(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            DateTime.TryParse(r.GetString(5), out var dt) ? dt : DateTime.MinValue);
    }

    public async Task<IReadOnlyList<CustomerDeviceRow>> ListCustomerDevicesAsync()
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<CustomerDeviceRow>();
        await using var cmd = connection.CreateCommand();
        // Gộp nhiều snapshot cùng tên thiết bị (cài lại app → deviceInstallId mới) — giữ bản hoạt động gần nhất.
        cmd.CommandText = """
            SELECT DeviceInstallId, DeviceName, CustomerUserId, FullName, PhoneOrEmail, UpdatedAtUtc, LastSeenUtc, Platform, AppVersion
            FROM (
                SELECT d.DeviceInstallId,
                       COALESCE(NULLIF(trim(d.DeviceName), ''), NULLIF(trim(h.Platform), ''), 'Thiết bị') AS DeviceName,
                       d.CustomerUserId,
                       u.FullName,
                       u.PhoneOrEmail,
                       d.UpdatedAtUtc,
                       h.LastSeenUtc,
                       h.Platform,
                       h.AppVersion,
                       ROW_NUMBER() OVER (
                           PARTITION BY lower(trim(COALESCE(NULLIF(d.DeviceName, ''), d.DeviceInstallId)))
                           ORDER BY datetime(COALESCE(h.LastSeenUtc, d.UpdatedAtUtc)) DESC
                       ) AS rn
                FROM DeviceRouteSnapshot d
                LEFT JOIN CustomerUser u ON u.Id = d.CustomerUserId
                LEFT JOIN AppDeviceHeartbeat h ON h.DeviceInstallId = d.DeviceInstallId
            ) x
            WHERE x.rn = 1
            ORDER BY datetime(COALESCE(x.LastSeenUtc, x.UpdatedAtUtc)) DESC
            """;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new CustomerDeviceRow(
                r.GetString(0),
                r.IsDBNull(1) ? "Thiết bị" : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                DateTime.TryParse(r.GetString(5), out var up) ? up : DateTime.MinValue,
                r.IsDBNull(6) ? null : DateTime.TryParse(r.GetString(6), out var ls) ? ls : null,
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8)));
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
            SELECT p.Id, p.CustomerUserId, p.DeviceInstallId, p.DeviceName, p.PlaceName, p.Source, p.Language, p.DurationSeconds, p.PlayedAtUtc,
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
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetDouble(7),
                DateTime.TryParse(r.GetString(8), out var pt) ? pt : DateTime.MinValue,
                r.IsDBNull(9) ? null : r.GetString(9)));
        }

        return list;
    }

    public async Task<double> GetPremiumPaidSumAsync(int placeId, string deviceInstallId, int? customerUserId)
    {
        if (placeId <= 0)
            return 0;

        deviceInstallId = (deviceInstallId ?? string.Empty).Trim();

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        if (customerUserId.HasValue && customerUserId.Value > 0)
        {
            cmd.CommandText = """
                SELECT IFNULL(SUM(AmountVnd), 0) FROM PoiPremiumPayment
                WHERE PlaceId = @p
                  AND CustomerUserId = @c
                """;
            cmd.Parameters.AddWithValue("@c", customerUserId.Value);
        }
        else
        {
            cmd.CommandText = """
                SELECT IFNULL(SUM(AmountVnd), 0) FROM PoiPremiumPayment
                WHERE PlaceId = @p AND DeviceInstallId = @d
                """;
        }

        cmd.Parameters.AddWithValue("@p", placeId);
        cmd.Parameters.AddWithValue("@d", deviceInstallId);
        var o = await cmd.ExecuteScalarAsync();
        return o is null or DBNull ? 0 : Convert.ToDouble(o);
    }

    public async Task<bool> HasPremiumUnlockForCurrentPriceAsync(
        int placeId,
        double requiredVnd,
        string deviceInstallId,
        int? customerUserId)
    {
        if (placeId <= 0 || requiredVnd <= 0)
            return true;

        var sum = await GetPremiumPaidSumAsync(placeId, deviceInstallId, customerUserId);
        return sum + 0.0001 >= requiredVnd;
    }

    /// <summary>Ghi nhận thanh toán demo; nếu admin tăng giá, chỉ thu phần còn thiếu so với tổng đã trả.</summary>
    public async Task<(bool Ok, string Message, bool WasAlreadyUnlocked)> RecordPremiumPaymentDemoAsync(
        int placeId,
        string deviceInstallId,
        double requiredAmountVnd,
        int? customerUserId)
    {
        deviceInstallId = (deviceInstallId ?? string.Empty).Trim();
        if (placeId <= 0 || string.IsNullOrWhiteSpace(deviceInstallId))
            return (false, "Thiếu PlaceId hoặc mã thiết bị.", false);
        if (requiredAmountVnd <= 0)
            return (false, "POI không bật trả phí.", false);

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

        var sum = await GetPremiumPaidSumAsync(placeId, deviceInstallId, uid);
        if (sum + 0.0001 >= requiredAmountVnd)
            return (true, "Đã mở khóa thuyết minh cho mức giá hiện tại.", true);

        var delta = requiredAmountVnd - sum;
        if (delta <= 0)
            return (true, "Đã mở khóa.", true);

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO PoiPremiumPayment (PlaceId, DeviceInstallId, AmountVnd, CustomerUserId, PaidAtUtc)
                VALUES (@p, @d, @a, @u, @t)
                """;
            cmd.Parameters.AddWithValue("@p", placeId);
            cmd.Parameters.AddWithValue("@d", deviceInstallId);
            cmd.Parameters.AddWithValue("@a", delta);
            cmd.Parameters.AddWithValue("@u", uid.HasValue ? uid.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        return (true, "Đã ghi nhận thanh toán demo.", false);
    }

    public async Task<(double GrandTotalVnd, int TotalPayments)> GetPremiumGrandTotalsAsync()
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(SUM(AmountVnd), 0), COUNT(1) FROM PoiPremiumPayment
            """;
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return (0, 0);
        return (r.GetDouble(0), r.GetInt32(1));
    }

    public async Task<IReadOnlyList<PremiumRevenueByPlaceRow>> GetPremiumRevenueByPlaceAsync()
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<PremiumRevenueByPlaceRow>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT PlaceId, SUM(AmountVnd) AS Total, COUNT(*) AS Cnt
            FROM PoiPremiumPayment
            GROUP BY PlaceId
            ORDER BY Total DESC
            """;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new PremiumRevenueByPlaceRow(r.GetInt32(0), r.GetDouble(1), r.GetInt32(2)));
        }

        return list;
    }

    public async Task<(double TotalVnd, int PaymentCount)> GetPremiumRevenueForPlaceAsync(int placeId)
    {
        if (placeId <= 0)
            return (0, 0);

        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(SUM(AmountVnd), 0), COUNT(1) FROM PoiPremiumPayment WHERE PlaceId = @p
            """;
        cmd.Parameters.AddWithValue("@p", placeId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return (0, 0);
        return (r.GetDouble(0), r.GetInt32(1));
    }

    public async Task<IReadOnlyList<PremiumRevenuePayerRow>> GetPremiumPayersByPlaceAsync()
    {
        await using var connection = Open();
        await EnsureSchemaAsync(connection);

        var list = new List<PremiumRevenuePayerRow>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.PlaceId,
                   p.CustomerUserId,
                   u.FullName,
                   u.PhoneOrEmail,
                   p.DeviceInstallId,
                   p.AmountVnd,
                   p.PaidAtUtc
            FROM PoiPremiumPayment p
            LEFT JOIN CustomerUser u ON u.Id = p.CustomerUserId
            ORDER BY p.PaidAtUtc DESC
            """;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new PremiumRevenuePayerRow(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? "" : r.GetString(4),
                r.GetDouble(5),
                DateTime.TryParse(r.GetString(6), out var paidAt) ? paidAt : DateTime.MinValue));
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
    string? DeviceInstallId,
    string? DeviceName,
    string PlaceName,
    string Source,
    string? Language,
    double? DurationSeconds,
    DateTime PlayedAtUtc,
    string? CustomerAccount);

public sealed record PlayAggregateRow(string PlaceName, string Source, int Count);

public sealed record PremiumRevenueByPlaceRow(int PlaceId, double TotalVnd, int PaymentCount);

public sealed record PremiumRevenuePayerRow(
    int PlaceId,
    int? CustomerUserId,
    string? CustomerFullName,
    string? CustomerPhoneOrEmail,
    string DeviceInstallId,
    double AmountVnd,
    DateTime PaidAtUtc);

public sealed record DevicePresenceRow(
    string DeviceInstallId,
    DateTime LastSeenUtc,
    bool IsOnMapTab,
    string? Platform,
    string? AppVersion);

public sealed record DeviceRouteSnapshotRow(
    string DeviceInstallId,
    string? DeviceName,
    int? CustomerUserId,
    string? CustomerFullName,
    string? CustomerPhoneOrEmail,
    DateTime UpdatedAtUtc);

public sealed record CustomerDeviceRow(
    string DeviceInstallId,
    string DeviceName,
    int? CustomerUserId,
    string? CustomerFullName,
    string? CustomerPhoneOrEmail,
    DateTime RouteUpdatedAtUtc,
    DateTime? LastSeenUtc,
    string? Platform,
    string? AppVersion);
