using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;
using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

/// <summary>
/// Đọc <c>VinhKhanh.db</c> trong AppData (và copy từ package nếu chưa có).
/// Dùng chung cho <see cref="MapPage"/> và <see cref="PlaceApiService.GetPlacesAsync"/> (fallback khi không có API).
/// </summary>
public static class PlaceLocalRepository
{
    public enum LoadError
    {
        None,
        DbEmptyNoTables,
        NoPlaceTable,
        Exception
    }

    public sealed record LoadResult(List<Place> Places, LoadError Error, string? Message);

    /// <param name="forceRecopyFromPackage">true: xóa DB cũ và copy lại từ bản cài (chỉ khi debug).</param>
    public static async Task<LoadResult> TryLoadAsync(bool forceRecopyFromPackage = false)
    {
        var places = new List<Place>();
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "VinhKhanh.db");

        try
        {
            if (forceRecopyFromPackage || !File.Exists(dbPath))
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);

                await using var inputStream = await FileSystem.OpenAppPackageFileAsync("VinhKhanh.db");
                await using var fileStream = File.Create(dbPath);
                await inputStream.CopyToAsync(fileStream);

                if (new FileInfo(dbPath).Length == 0)
                    return new LoadResult(places, LoadError.Exception, "File VinhKhanh.db rỗng sau khi copy.");
            }

            await using var connection = new SqliteConnection($"Filename={dbPath}");
            await connection.OpenAsync();

            await using (var tableCommand = connection.CreateCommand())
            {
                tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
                var tables = new List<string>();
                await using (var reader = await tableCommand.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        tables.Add(reader.GetString(0));
                }

                if (tables.Count == 0)
                    return new LoadResult(places, LoadError.DbEmptyNoTables, null);

                var tableName = tables.Contains("Place", StringComparer.OrdinalIgnoreCase) ? "Place" :
                    tables.Contains("Places", StringComparer.OrdinalIgnoreCase) ? "Places" : null;

                if (string.IsNullOrEmpty(tableName))
                    return new LoadResult(places, LoadError.NoPlaceTable, null);

                var hasMapUrl = await TableHasColumnAsync(connection, tableName, "MapUrl");

                await using var command = connection.CreateCommand();
                command.CommandText = hasMapUrl
                    ? $"""
                       SELECT Id, Name, Address, Specialty, ImageUrl,
                              Latitude, Longitude, ActivationRadiusMeters, Priority,
                              Description,
                              VietnameseAudioText, EnglishAudioText,
                              ChineseAudioText, JapaneseAudioText, MapUrl
                       FROM {tableName}
                       """
                    : $"""
                       SELECT Id, Name, Address, Specialty, ImageUrl,
                              Latitude, Longitude, ActivationRadiusMeters, Priority,
                              Description,
                              VietnameseAudioText, EnglishAudioText,
                              ChineseAudioText, JapaneseAudioText
                       FROM {tableName}
                       """;

                await using var readerData = await command.ExecuteReaderAsync();
                while (await readerData.ReadAsync())
                {
                    var place = new Place
                    {
                        Id = readerData.GetInt32(0),
                        Name = readerData.GetString(1),
                        Address = readerData.GetString(2),
                        Specialty = readerData.GetString(3),
                        ImageUrl = readerData.GetString(4),
                        Latitude = readerData.GetDouble(5),
                        Longitude = readerData.GetDouble(6),
                        ActivationRadiusMeters = readerData.GetDouble(7),
                        Priority = readerData.GetInt32(8),
                        Description = readerData.GetString(9),
                        VietnameseAudioText = readerData.GetString(10),
                        EnglishAudioText = readerData.GetString(11),
                        ChineseAudioText = readerData.GetString(12),
                        JapaneseAudioText = readerData.GetString(13),
                        MapUrl = hasMapUrl && !readerData.IsDBNull(14) ? readerData.GetString(14) : string.Empty
                    };
                    places.Add(place);
                }
            }

            return new LoadResult(places, LoadError.None, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PlaceLocalRepository: {ex}");
            return new LoadResult(places, LoadError.Exception, ex.Message);
        }
    }

    static async Task<bool> TableHasColumnAsync(SqliteConnection connection, string tableName, string columnName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var name = r.GetString(1);
            if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
