using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CsvHelper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PrinterWLAN.Models;
using PrinterWLAN.Storage;

namespace PrinterWLAN.Logging;

public sealed class ActivityLogService(AppPaths paths, AppDatabase appDatabase, IOptions<PrinterWlanOptions> options,
    ILogger<ActivityLogService> logger)
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        _ = await GetAnchorAsync(cancellationToken);
        await MaintainAsync(cancellationToken);
    }

    public async Task WriteAsync(ActivityRecord record, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var slice = await GetSliceForAsync(DateOnly.FromDateTime(record.LocalTime.DateTime), cancellationToken);
            var databasePath = SliceDatabasePath(slice);
            await EnsureDatabaseAsync(databasePath, cancellationToken);
            await using var connection = await OpenAsync(databasePath, readOnly: false, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO records(utc_time,local_time,utc_offset,type,user_id,username,session_id,device_id,ip_address,
                  user_agent,language,platform,viewport,screen,timezone,referrer,detail_json)
                VALUES($utc,$local,$offset,$type,$uid,$username,$session,$device,$ip,$ua,$language,$platform,$viewport,$screen,$timezone,$referrer,$detail)
                """;
            command.Parameters.AddWithValue("$utc", record.UtcTime.ToString("O"));
            command.Parameters.AddWithValue("$local", record.LocalTime.ToString("O"));
            command.Parameters.AddWithValue("$offset", record.LocalTime.Offset.ToString());
            command.Parameters.AddWithValue("$type", record.Type);
            AddNullable(command, "$uid", record.UserId);
            AddNullable(command, "$username", record.Username);
            AddNullable(command, "$session", record.SessionId);
            AddNullable(command, "$device", record.DeviceId);
            AddNullable(command, "$ip", record.IpAddress);
            AddNullable(command, "$ua", record.UserAgent);
            AddNullable(command, "$language", record.Language);
            AddNullable(command, "$platform", record.Platform);
            AddNullable(command, "$viewport", record.Viewport);
            AddNullable(command, "$screen", record.Screen);
            AddNullable(command, "$timezone", record.Timezone);
            AddNullable(command, "$referrer", record.Referrer);
            AddNullable(command, "$detail", record.DetailJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _writeLock.Release(); }
    }

    public async Task<(IReadOnlyList<Dictionary<string, object?>> Records, int Total)> QueryAsync(
        string? username, DateTimeOffset? start, DateTimeOffset? end, string? type, string? ip, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var all = new List<Dictionary<string, object?>>();
        foreach (var slice in (await ListSlicesAsync(cancellationToken)).OrderByDescending(x => x.Start))
        {
            if (start is not null && slice.End.ToDateTime(TimeOnly.MaxValue) < start.Value.LocalDateTime) continue;
            if (end is not null && slice.Start.ToDateTime(TimeOnly.MinValue) > end.Value.LocalDateTime) continue;
            var path = await ObtainReadableDatabaseAsync(slice, cancellationToken);
            if (!File.Exists(path)) continue;
            await using var connection = await OpenAsync(path, readOnly: true, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id,utc_time,local_time,type,user_id,username,session_id,device_id,ip_address,user_agent,
                  language,platform,viewport,screen,timezone,referrer,detail_json
                FROM records WHERE ($username='' OR username LIKE $usernameLike ESCAPE '\')
                  AND ($start='' OR utc_time >= $start) AND ($end='' OR utc_time <= $end)
                  AND ($type='' OR type=$type) AND ($ip='' OR ip_address=$ip)
                ORDER BY utc_time DESC
                """;
            AddQueryParameters(command, username, start, end, type, ip);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) all.Add(ReadRecord(reader));
        }
        all.Sort((left, right) => string.CompareOrdinal((string?)right["utcTime"], (string?)left["utcTime"]));
        return (all.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), all.Count);
    }

    public async Task<IReadOnlyList<SliceInfo>> ListSlicesAsync(CancellationToken cancellationToken = default)
    {
        var anchor = await GetAnchorAsync(cancellationToken);
        var byName = new Dictionary<string, SliceInfo>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(paths.Logs, "PrinterWLAN_*"))
        {
            var extension = Path.GetExtension(file);
            if (!extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            var stem = Path.GetFileNameWithoutExtension(file);
            if (!TryParseSlice(stem, out var start, out var end)) continue;
            var name = SliceName(start, end);
            var state = extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ? "archived" : "active";
            var info = new SliceInfo(name, start, end, state, new FileInfo(file).Length);
            if (!byName.TryGetValue(name, out var old) || old.State != "active") byName[name] = info;
        }
        var current = SliceFor(anchor, DateOnly.FromDateTime(DateTime.Now));
        var currentName = SliceName(current.Start, current.End);
        if (!byName.ContainsKey(currentName)) byName[currentName] = new SliceInfo(currentName, current.Start, current.End, "active", 0);
        return byName.Values.OrderBy(x => x.Start).ToArray();
    }

    public async Task MaintainAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            foreach (var slice in await ListSlicesAsync(cancellationToken))
            {
                if (slice.State == "active" && slice.End < today && File.Exists(SliceDatabasePath(slice)))
                    await ArchiveAsync(slice, cancellationToken);
            }
            await EnforceRetentionAsync(cancellationToken);
            CleanupCache();
        }
        finally { _writeLock.Release(); }
    }

    public async Task<string> ExportAsync(IReadOnlyCollection<string> selectedNames, CancellationToken cancellationToken = default)
    {
        var slices = (await ListSlicesAsync(cancellationToken)).Where(s => selectedNames.Contains(s.Name, StringComparer.Ordinal)).OrderBy(s => s.Start).ToArray();
        if (slices.Length == 0) throw new InvalidOperationException("请至少选择一个日志时间段。");
        var token = Guid.NewGuid().ToString("N");
        var exportDirectory = Path.Combine(paths.Exports, token);
        Directory.CreateDirectory(exportDirectory);
        try
        {
            var accessPath = Path.Combine(exportDirectory, "access_records.csv");
            var printPath = Path.Combine(exportDirectory, "print_records.csv");
            var count = 0;
            await using (var access = CreateBomWriter(accessPath))
            await using (var print = CreateBomWriter(printPath))
            {
                await using var accessCsv = new CsvWriter(access, CultureInfo.InvariantCulture);
                await using var printCsv = new CsvWriter(print, CultureInfo.InvariantCulture);
                WriteCsvHeader(accessCsv); await accessCsv.NextRecordAsync();
                WriteCsvHeader(printCsv); await printCsv.NextRecordAsync();
                foreach (var slice in slices)
                {
                    var path = await ObtainReadableDatabaseAsync(slice, cancellationToken);
                    await using var connection = await OpenAsync(path, true, cancellationToken);
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT utc_time,local_time,type,user_id,username,session_id,device_id,ip_address,user_agent,detail_json FROM records ORDER BY utc_time";
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var csv = reader.GetString(2).StartsWith("print_", StringComparison.Ordinal) ? printCsv : accessCsv;
                        for (var i = 0; i < reader.FieldCount; i++) csv.WriteField(reader.IsDBNull(i) ? null : reader.GetValue(i));
                        await csv.NextRecordAsync();
                        count++;
                    }
                }
            }
            var manifest = new
            {
                sliceStart = slices[0].Start.ToString("yyyy-MM-dd"),
                sliceEnd = slices[^1].End.ToString("yyyy-MM-dd"),
                slices = slices.Select(s => s.Name),
                recordCount = count,
                exportedAt = DateTimeOffset.UtcNow,
                version = "1.0.0"
            };
            await File.WriteAllTextAsync(Path.Combine(exportDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            var filename = slices.Length == 1 ? $"PrinterWLAN_Logs_{slices[0].Name}.zip" : $"PrinterWLAN_Logs_{slices[0].Start:yyyy-MM-dd}_to_{slices[^1].End:yyyy-MM-dd}.zip";
            var output = Path.Combine(paths.Exports, filename);
            if (File.Exists(output)) File.Delete(output);
            ZipFile.CreateFromDirectory(exportDirectory, output, CompressionLevel.Optimal, false);
            return output;
        }
        finally { if (Directory.Exists(exportDirectory)) Directory.Delete(exportDirectory, true); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var file in Directory.EnumerateFiles(paths.Logs, "PrinterWLAN_*")) File.Delete(file);
            if (Directory.Exists(paths.Cache))
            {
                Directory.Delete(paths.Cache, true);
                Directory.CreateDirectory(paths.Cache);
            }
            await appDatabase.SetSettingAsync("log_anchor_date", DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"), cancellationToken);
            var current = await GetSliceForAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken);
            await EnsureDatabaseAsync(SliceDatabasePath(current), cancellationToken);
        }
        finally { _writeLock.Release(); }
    }

    public long GetTotalBytes() => Directory.EnumerateFiles(paths.Logs).Sum(file => new FileInfo(file).Length);

    public static (DateOnly Start, DateOnly End) SliceFor(DateOnly anchor, DateOnly date)
    {
        var days = date.DayNumber - anchor.DayNumber;
        var index = days >= 0 ? days / 10 : (days - 9) / 10;
        var start = anchor.AddDays(index * 10);
        return (start, start.AddDays(9));
    }

    private async Task<(DateOnly Start, DateOnly End)> GetSliceForAsync(DateOnly date, CancellationToken cancellationToken) =>
        SliceFor(await GetAnchorAsync(cancellationToken), date);

    private async Task<DateOnly> GetAnchorAsync(CancellationToken cancellationToken)
    {
        var fallback = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        var value = await appDatabase.GetSettingAsync("log_anchor_date", fallback, cancellationToken);
        if (value == fallback) await appDatabase.SetSettingAsync("log_anchor_date", value, cancellationToken);
        return DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private async Task EnsureDatabaseAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(path, false, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS records(
              id INTEGER PRIMARY KEY AUTOINCREMENT, utc_time TEXT NOT NULL, local_time TEXT NOT NULL, utc_offset TEXT NOT NULL,
              type TEXT NOT NULL, user_id INTEGER NULL, username TEXT NULL, session_id TEXT NULL, device_id TEXT NULL,
              ip_address TEXT NULL, user_agent TEXT NULL, language TEXT NULL, platform TEXT NULL, viewport TEXT NULL,
              screen TEXT NULL, timezone TEXT NULL, referrer TEXT NULL, detail_json TEXT NULL);
            CREATE INDEX IF NOT EXISTS idx_records_utc ON records(utc_time DESC);
            CREATE INDEX IF NOT EXISTS idx_records_user ON records(user_id,utc_time DESC);
            CREATE INDEX IF NOT EXISTS idx_records_type ON records(type,utc_time DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ArchiveAsync(SliceInfo slice, CancellationToken cancellationToken)
    {
        var databasePath = SliceDatabasePath(slice);
        await using (var connection = await OpenAsync(databasePath, false, cancellationToken))
        {
            foreach (var sql in new[] { "PRAGMA wal_checkpoint(TRUNCATE)", "VACUUM", "PRAGMA integrity_check" })
            {
                await using var command = connection.CreateCommand(); command.CommandText = sql;
                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (sql.Contains("integrity", StringComparison.Ordinal) && !string.Equals(result?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Log database integrity check failed for {slice.Name}.");
            }
        }
        SqliteConnection.ClearAllPools();
        var zipPath = SliceArchivePath(slice);
        var temporary = zipPath + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create)) archive.CreateEntryFromFile(databasePath, Path.GetFileName(databasePath), CompressionLevel.Optimal);
        using (var archive = ZipFile.OpenRead(temporary))
        {
            var entry = archive.Entries.SingleOrDefault();
            if (entry is null || entry.Length != new FileInfo(databasePath).Length) throw new InvalidDataException("Archived log verification failed.");
        }
        File.Move(temporary, zipPath, true);
        File.Delete(databasePath);
        DeleteSidecars(databasePath);
    }

    private async Task EnforceRetentionAsync(CancellationToken cancellationToken)
    {
        var total = GetTotalBytes();
        if (total <= options.Value.LogMaxBytes) return;
        foreach (var archive in (await ListSlicesAsync(cancellationToken)).Where(s => s.State == "archived").OrderBy(s => s.Start))
        {
            var path = SliceArchivePath(archive);
            if (!File.Exists(path)) continue;
            var length = new FileInfo(path).Length;
            File.Delete(path);
            total -= length;
            logger.LogWarning("Deleted oldest archived activity slice {SliceName} to enforce storage limit", archive.Name);
            if (total <= options.Value.LogCleanupTargetBytes) break;
        }
    }

    private async Task<string> ObtainReadableDatabaseAsync(SliceInfo slice, CancellationToken cancellationToken)
    {
        var active = SliceDatabasePath(slice);
        if (File.Exists(active)) return active;
        var archive = SliceArchivePath(slice);
        if (!File.Exists(archive)) return active;
        var cacheDirectory = Path.Combine(paths.Cache, slice.Name);
        Directory.CreateDirectory(cacheDirectory);
        var cached = Path.Combine(cacheDirectory, Path.GetFileName(active));
        if (!File.Exists(cached))
        {
            var temporary = cached + ".tmp";
            await using (var input = ZipFile.OpenRead(archive).Entries.Single().Open())
            await using (var output = File.Create(temporary)) await input.CopyToAsync(output, cancellationToken);
            File.Move(temporary, cached, true);
        }
        File.SetLastWriteTimeUtc(cached, DateTime.UtcNow);
        Directory.SetLastWriteTimeUtc(cacheDirectory, DateTime.UtcNow);
        return cached;
    }

    private void CleanupCache()
    {
        if (!Directory.Exists(paths.Cache)) return;
        foreach (var directory in Directory.EnumerateDirectories(paths.Cache))
        {
            try { if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddHours(-1)) Directory.Delete(directory, true); }
            catch (Exception exception) { logger.LogDebug(exception, "Could not clean log query cache."); }
        }
    }

    private static Dictionary<string, object?> ReadRecord(SqliteDataReader reader) => new()
    {
        ["id"] = reader.GetInt64(0), ["utcTime"] = reader.GetString(1), ["localTime"] = reader.GetString(2),
        ["type"] = reader.GetString(3), ["userId"] = Value(reader, 4), ["username"] = Value(reader, 5),
        ["sessionId"] = Value(reader, 6), ["deviceId"] = Value(reader, 7), ["ipAddress"] = Value(reader, 8),
        ["userAgent"] = Value(reader, 9), ["language"] = Value(reader, 10), ["platform"] = Value(reader, 11),
        ["viewport"] = Value(reader, 12), ["screen"] = Value(reader, 13), ["timezone"] = Value(reader, 14),
        ["referrer"] = Value(reader, 15), ["detail"] = Value(reader, 16)
    };

    private static object? Value(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
    private static void AddNullable(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static void AddQueryParameters(SqliteCommand command, string? username, DateTimeOffset? start, DateTimeOffset? end, string? type, string? ip)
    {
        var user = username?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$username", user);
        command.Parameters.AddWithValue("$usernameLike", $"%{user.Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%");
        command.Parameters.AddWithValue("$start", start?.ToUniversalTime().ToString("O") ?? string.Empty);
        command.Parameters.AddWithValue("$end", end?.ToUniversalTime().ToString("O") ?? string.Empty);
        command.Parameters.AddWithValue("$type", type ?? string.Empty);
        command.Parameters.AddWithValue("$ip", ip ?? string.Empty);
    }
    private static void WriteCsvHeader(CsvWriter csv)
    {
        foreach (var value in new[] { "utc_time", "local_time", "type", "user_id", "username", "session_id", "device_id", "ip_address", "user_agent", "detail_json" }) csv.WriteField(value);
    }
    private static StreamWriter CreateBomWriter(string path)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(Encoding.UTF8.GetPreamble());
        return new StreamWriter(stream, new UTF8Encoding(false));
    }
    private string SliceDatabasePath((DateOnly Start, DateOnly End) slice) => Path.Combine(paths.Logs, $"PrinterWLAN_{SliceName(slice.Start, slice.End)}.sqlite");
    private string SliceDatabasePath(SliceInfo slice) => Path.Combine(paths.Logs, $"PrinterWLAN_{slice.Name}.sqlite");
    private string SliceArchivePath(SliceInfo slice) => Path.Combine(paths.Logs, $"PrinterWLAN_{slice.Name}.zip");
    private static string SliceName(DateOnly start, DateOnly end) => $"{start:yyyy-MM-dd}_to_{end:yyyy-MM-dd}";
    private static bool TryParseSlice(string stem, out DateOnly start, out DateOnly end)
    {
        start = default; end = default;
        var value = stem.StartsWith("PrinterWLAN_", StringComparison.Ordinal) ? stem[12..] : stem;
        return value.Length == 24 && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start)
            && DateOnly.TryParseExact(value[14..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end);
    }
    private static async Task<SqliteConnection> OpenAsync(string path, bool readOnly, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = true }.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
    private static void DeleteSidecars(string path)
    {
        foreach (var suffix in new[] { "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }
}
