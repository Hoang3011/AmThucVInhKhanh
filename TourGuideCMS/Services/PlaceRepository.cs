using Microsoft.Data.Sqlite;
using TourGuideCMS.Models;

namespace TourGuideCMS.Services;

public sealed class PlaceRepository
{
    private readonly string _dbPath;
    private string? _tableName;
    private bool? _hasMapUrl;
    private bool _premiumColumnsReady;

    public PlaceRepository(IWebHostEnvironment env)
    {
        var appData = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, "VinhKhanh.db");

        var src = Path.Combine(env.ContentRootPath, "..", "TourGuideApp2", "VinhKhanh.db");
        if (!File.Exists(_dbPath) && File.Exists(src))
            File.Copy(src, _dbPath, overwrite: false);
    }

    public string DatabasePath => _dbPath;

    private SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        return c;
    }

    private async Task EnsureMetaAsync(SqliteConnection connection)
    {
        if (_tableName is not null)
            return;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT name FROM sqlite_master
            WHERE type='table' AND (LOWER(name)='place' OR LOWER(name)='places')
            ORDER BY CASE WHEN LOWER(name)='place' THEN 0 ELSE 1 END
            LIMIT 1
            """;
        var o = await cmd.ExecuteScalarAsync();
        _tableName = o as string ?? "Place";

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({_tableName})";
        await using var r = await pragma.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var col = r.GetString(1);
            if (string.Equals(col, "MapUrl", StringComparison.OrdinalIgnoreCase))
                _hasMapUrl = true;
        }

        _hasMapUrl ??= false;
    }

    private static async Task<bool> ColumnExistsOnTableAsync(SqliteConnection connection, string tableName, string column)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName})";
        await using var r = await pragma.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task EnsurePremiumColumnsAsync(SqliteConnection conn)
    {
        if (_premiumColumnsReady)
            return;

        await EnsureMetaAsync(conn);
        var table = _tableName ?? "Place";

        if (!await ColumnExistsOnTableAsync(conn, table, "PremiumPriceDemo"))
        {
            await using var a = conn.CreateCommand();
            a.CommandText = $"ALTER TABLE {table} ADD COLUMN PremiumPriceDemo REAL NOT NULL DEFAULT 0";
            await a.ExecuteNonQueryAsync();
        }

        if (!await ColumnExistsOnTableAsync(conn, table, "PremiumVietnameseAudioText"))
        {
            await using var a = conn.CreateCommand();
            a.CommandText = $"ALTER TABLE {table} ADD COLUMN PremiumVietnameseAudioText TEXT NOT NULL DEFAULT ''";
            await a.ExecuteNonQueryAsync();
        }

        _premiumColumnsReady = true;
    }

    public async Task<List<PlaceRow>> ListAsync()
    {
        var list = new List<PlaceRow>();
        await using var conn = Open();
        await EnsureMetaAsync(conn);
        await EnsurePremiumColumnsAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _hasMapUrl == true
            ? $"""
              SELECT Id, Name, Address, Specialty, ImageUrl,
                     Latitude, Longitude, ActivationRadiusMeters, Priority,
                     Description, VietnameseAudioText, EnglishAudioText,
                     ChineseAudioText, JapaneseAudioText,
                     PremiumPriceDemo, PremiumVietnameseAudioText, MapUrl
              FROM {_tableName}
              ORDER BY Priority DESC, Name
              """
            : $"""
              SELECT Id, Name, Address, Specialty, ImageUrl,
                     Latitude, Longitude, ActivationRadiusMeters, Priority,
                     Description, VietnameseAudioText, EnglishAudioText,
                     ChineseAudioText, JapaneseAudioText,
                     PremiumPriceDemo, PremiumVietnameseAudioText
              FROM {_tableName}
              ORDER BY Priority DESC, Name
              """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(ReadRow(reader, _hasMapUrl == true));

        return list;
    }

    public async Task<PlaceRow?> GetAsync(int id)
    {
        await using var conn = Open();
        await EnsureMetaAsync(conn);
        await EnsurePremiumColumnsAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _hasMapUrl == true
            ? $"""
              SELECT Id, Name, Address, Specialty, ImageUrl,
                     Latitude, Longitude, ActivationRadiusMeters, Priority,
                     Description, VietnameseAudioText, EnglishAudioText,
                     ChineseAudioText, JapaneseAudioText,
                     PremiumPriceDemo, PremiumVietnameseAudioText, MapUrl
              FROM {_tableName} WHERE Id = @id
              """
            : $"""
              SELECT Id, Name, Address, Specialty, ImageUrl,
                     Latitude, Longitude, ActivationRadiusMeters, Priority,
                     Description, VietnameseAudioText, EnglishAudioText,
                     ChineseAudioText, JapaneseAudioText,
                     PremiumPriceDemo, PremiumVietnameseAudioText
              FROM {_tableName} WHERE Id = @id
              """;
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return ReadRow(reader, _hasMapUrl == true);
    }

    private static PlaceRow ReadRow(SqliteDataReader reader, bool hasMapUrl)
    {
        var premPrice = reader.GetDouble(14);
        var premText = reader.IsDBNull(15) ? "" : reader.GetString(15);
        var map = "";
        if (hasMapUrl)
            map = reader.IsDBNull(16) ? "" : reader.GetString(16);

        return new PlaceRow
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Address = reader.GetString(2),
            Specialty = reader.GetString(3),
            ImageUrl = reader.GetString(4),
            Latitude = reader.GetDouble(5),
            Longitude = reader.GetDouble(6),
            ActivationRadiusMeters = reader.GetDouble(7),
            Priority = reader.GetInt32(8),
            Description = reader.GetString(9),
            VietnameseAudioText = reader.GetString(10),
            EnglishAudioText = reader.GetString(11),
            ChineseAudioText = reader.GetString(12),
            JapaneseAudioText = reader.GetString(13),
            PremiumPriceDemo = premPrice,
            PremiumVietnameseAudioText = premText,
            MapUrl = map
        };
    }

    public async Task<int> InsertAsync(PlaceRow p)
    {
        await using var conn = Open();
        await EnsureMetaAsync(conn);
        await EnsurePremiumColumnsAsync(conn);

        await using var cmd = conn.CreateCommand();
        if (_hasMapUrl == true)
        {
            cmd.CommandText = $"""
                INSERT INTO {_tableName}
                (Name, Address, Specialty, ImageUrl, Latitude, Longitude,
                 ActivationRadiusMeters, Priority, Description,
                 VietnameseAudioText, EnglishAudioText, ChineseAudioText, JapaneseAudioText,
                 PremiumPriceDemo, PremiumVietnameseAudioText, MapUrl)
                VALUES (@n,@a,@s,@i,@lat,@lng,@rad,@pri,@d,@vi,@en,@zh,@ja,@prem,@premT,@map)
                """;
            cmd.Parameters.AddWithValue("@map", p.MapUrl);
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_tableName}
                (Name, Address, Specialty, ImageUrl, Latitude, Longitude,
                 ActivationRadiusMeters, Priority, Description,
                 VietnameseAudioText, EnglishAudioText, ChineseAudioText, JapaneseAudioText,
                 PremiumPriceDemo, PremiumVietnameseAudioText)
                VALUES (@n,@a,@s,@i,@lat,@lng,@rad,@pri,@d,@vi,@en,@zh,@ja,@prem,@premT)
                """;
        }

        cmd.Parameters.AddWithValue("@n", p.Name);
        cmd.Parameters.AddWithValue("@a", p.Address);
        cmd.Parameters.AddWithValue("@s", p.Specialty);
        cmd.Parameters.AddWithValue("@i", p.ImageUrl);
        cmd.Parameters.AddWithValue("@lat", p.Latitude);
        cmd.Parameters.AddWithValue("@lng", p.Longitude);
        cmd.Parameters.AddWithValue("@rad", p.ActivationRadiusMeters);
        cmd.Parameters.AddWithValue("@pri", p.Priority);
        cmd.Parameters.AddWithValue("@d", p.Description);
        cmd.Parameters.AddWithValue("@vi", p.VietnameseAudioText);
        cmd.Parameters.AddWithValue("@en", p.EnglishAudioText);
        cmd.Parameters.AddWithValue("@zh", p.ChineseAudioText);
        cmd.Parameters.AddWithValue("@ja", p.JapaneseAudioText);
        cmd.Parameters.AddWithValue("@prem", p.PremiumPriceDemo);
        cmd.Parameters.AddWithValue("@premT", p.PremiumVietnameseAudioText ?? "");

        await cmd.ExecuteNonQueryAsync();

        await using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var newId = Convert.ToInt32(await idCmd.ExecuteScalarAsync());
        return newId;
    }

    public async Task UpdateAsync(PlaceRow p)
    {
        await using var conn = Open();
        await EnsureMetaAsync(conn);
        await EnsurePremiumColumnsAsync(conn);

        await using var cmd = conn.CreateCommand();
        if (_hasMapUrl == true)
        {
            cmd.CommandText = $"""
                UPDATE {_tableName} SET
                  Name=@n, Address=@a, Specialty=@s, ImageUrl=@i,
                  Latitude=@lat, Longitude=@lng, ActivationRadiusMeters=@rad, Priority=@pri,
                  Description=@d, VietnameseAudioText=@vi, EnglishAudioText=@en,
                  ChineseAudioText=@zh, JapaneseAudioText=@ja,
                  PremiumPriceDemo=@prem, PremiumVietnameseAudioText=@premT, MapUrl=@map
                WHERE Id=@id
                """;
            cmd.Parameters.AddWithValue("@map", p.MapUrl);
        }
        else
        {
            cmd.CommandText = $"""
                UPDATE {_tableName} SET
                  Name=@n, Address=@a, Specialty=@s, ImageUrl=@i,
                  Latitude=@lat, Longitude=@lng, ActivationRadiusMeters=@rad, Priority=@pri,
                  Description=@d, VietnameseAudioText=@vi, EnglishAudioText=@en,
                  ChineseAudioText=@zh, JapaneseAudioText=@ja,
                  PremiumPriceDemo=@prem, PremiumVietnameseAudioText=@premT
                WHERE Id=@id
                """;
        }

        cmd.Parameters.AddWithValue("@id", p.Id);
        cmd.Parameters.AddWithValue("@n", p.Name);
        cmd.Parameters.AddWithValue("@a", p.Address);
        cmd.Parameters.AddWithValue("@s", p.Specialty);
        cmd.Parameters.AddWithValue("@i", p.ImageUrl);
        cmd.Parameters.AddWithValue("@lat", p.Latitude);
        cmd.Parameters.AddWithValue("@lng", p.Longitude);
        cmd.Parameters.AddWithValue("@rad", p.ActivationRadiusMeters);
        cmd.Parameters.AddWithValue("@pri", p.Priority);
        cmd.Parameters.AddWithValue("@d", p.Description);
        cmd.Parameters.AddWithValue("@vi", p.VietnameseAudioText);
        cmd.Parameters.AddWithValue("@en", p.EnglishAudioText);
        cmd.Parameters.AddWithValue("@zh", p.ChineseAudioText);
        cmd.Parameters.AddWithValue("@ja", p.JapaneseAudioText);
        cmd.Parameters.AddWithValue("@prem", p.PremiumPriceDemo);
        cmd.Parameters.AddWithValue("@premT", p.PremiumVietnameseAudioText ?? "");

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var conn = Open();
        await EnsureMetaAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_tableName} WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
