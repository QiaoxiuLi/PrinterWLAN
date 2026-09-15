using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.Sqlite;
using PrinterWLAN.Authentication;
using PrinterWLAN.Models;
using PrinterWLAN.Storage;

namespace PrinterWLAN.Users;

public sealed class UserService(AppDatabase database, PasswordService passwords, CredentialProtector protector)
{
    private readonly SemaphoreSlim _importLock = new(1, 1);
    public static string NormalizeUsername(string value) => value.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();

    public async Task<ImportResult> ImportAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        await _importLock.WaitAsync(cancellationToken);
        try
        {
        var entries = ParseCsv(csvStream, out var ignored);
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            long nextSequence;
            await using (var max = connection.CreateCommand())
            {
                max.Transaction = transaction;
                max.CommandText = "SELECT COALESCE(MAX(sequence),0)+1 FROM users";
                nextSequence = Convert.ToInt64(await max.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }

            var added = 0;
            var updated = 0;
            foreach (var displayName in entries)
            {
                var normalized = NormalizeUsername(displayName);
                var password = passwords.GenerateUserPassword();
                var hash = passwords.Hash(password);
                var encrypted = protector.Protect(password);
                var now = DateTimeOffset.UtcNow.ToString("O");

                await using var find = connection.CreateCommand();
                find.Transaction = transaction;
                find.CommandText = "SELECT user_id FROM users WHERE normalized_username=$normalized";
                find.Parameters.AddWithValue("$normalized", normalized);
                var existing = await find.ExecuteScalarAsync(cancellationToken);
                if (existing is not null)
                {
                    await using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = """
                        UPDATE users SET username=$username,password_hash=$hash,
                          encrypted_exportable_password=$encrypted,updated_at=$now
                        WHERE normalized_username=$normalized
                        """;
                    update.Parameters.AddWithValue("$username", displayName);
                    update.Parameters.AddWithValue("$hash", hash);
                    update.Parameters.AddWithValue("$encrypted", encrypted);
                    update.Parameters.AddWithValue("$now", now);
                    update.Parameters.AddWithValue("$normalized", normalized);
                    await update.ExecuteNonQueryAsync(cancellationToken);
                    updated++;
                }
                else
                {
                    await using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = """
                        INSERT INTO users(username,normalized_username,password_hash,encrypted_exportable_password,sequence,created_at,updated_at)
                        VALUES($username,$normalized,$hash,$encrypted,$sequence,$now,$now)
                        """;
                    insert.Parameters.AddWithValue("$username", displayName);
                    insert.Parameters.AddWithValue("$normalized", normalized);
                    insert.Parameters.AddWithValue("$hash", hash);
                    insert.Parameters.AddWithValue("$encrypted", encrypted);
                    insert.Parameters.AddWithValue("$sequence", nextSequence++);
                    insert.Parameters.AddWithValue("$now", now);
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                    added++;
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new ImportResult(added, updated, ignored);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        }
        finally { _importLock.Release(); }
    }

    public async Task<UserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeUsername(username);
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT user_id,username,normalized_username,password_hash,encrypted_exportable_password,sequence,created_at,updated_at
            FROM users WHERE normalized_username=$normalized
            """;
        command.Parameters.AddWithValue("$normalized", normalized);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<UserRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT user_id,username,normalized_username,password_hash,encrypted_exportable_password,sequence,created_at,updated_at
            FROM users WHERE user_id=$id
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<IReadOnlyList<UserRecord>> ListAsync(string? query, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var result = new List<UserRecord>();
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT user_id,username,normalized_username,password_hash,encrypted_exportable_password,sequence,created_at,updated_at
            FROM users WHERE $query='' OR username LIKE $like ESCAPE '\'
            ORDER BY sequence ASC LIMIT $limit OFFSET $offset
            """;
        var safeQuery = query?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$query", safeQuery);
        command.Parameters.AddWithValue("$like", $"%{EscapeLike(safeQuery)}%");
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadUser(reader));
        return result;
    }

    public async Task<int> CountAsync(string? query, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var safeQuery = query?.Trim() ?? string.Empty;
        command.CommandText = "SELECT COUNT(*) FROM users WHERE $query='' OR username LIKE $like ESCAPE '\'";
        command.Parameters.AddWithValue("$query", safeQuery);
        command.Parameters.AddWithValue("$like", $"%{EscapeLike(safeQuery)}%");
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public string RevealPassword(UserRecord user) => protector.Unprotect(user.EncryptedPassword);

    public async Task WriteCredentialsCsvAsync(Stream output, CancellationToken cancellationToken = default)
    {
        await output.WriteAsync(Encoding.UTF8.GetPreamble(), cancellationToken);
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteField("用户名");
        csv.WriteField("密码");
        await csv.NextRecordAsync();
        var page = 1;
        while (true)
        {
            var users = await ListAsync(null, page++, 200, cancellationToken);
            foreach (var user in users)
            {
                csv.WriteField(user.Username);
                csv.WriteField(RevealPassword(user));
                await csv.NextRecordAsync();
            }
            if (users.Count < 200) break;
        }
        await writer.FlushAsync(cancellationToken);
    }

    public static IReadOnlyList<string> ParseCsv(Stream input, out int ignored)
    {
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            IgnoreBlankLines = false,
            TrimOptions = TrimOptions.None,
            BadDataFound = null,
            MissingFieldFound = null
        };
        using var csv = new CsvReader(reader, configuration);
        var result = new List<string>();
        ignored = 0;
        var firstNonEmpty = true;
        while (csv.Read())
        {
            var raw = csv.Parser.Count > 0 ? csv.GetField(0) ?? string.Empty : string.Empty;
            var value = raw.Trim().Normalize(NormalizationForm.FormC);
            if (value.Length == 0)
            {
                ignored++;
                continue;
            }
            if (csv.Parser.Count > 1) throw new InvalidDataException("CSV 只能包含一列用户名。");
            if (firstNonEmpty && (value.Equals("username", StringComparison.OrdinalIgnoreCase) || value == "用户名"))
            {
                firstNonEmpty = false;
                continue;
            }
            firstNonEmpty = false;
            result.Add(value);
        }
        return result;
    }

    private static UserRecord ReadUser(SqliteDataReader reader) => new(reader.GetInt64(0), reader.GetString(1),
        reader.GetString(2), reader.GetString(3), (byte[])reader[4], reader.GetInt64(5),
        DateTimeOffset.Parse(reader.GetString(6)), DateTimeOffset.Parse(reader.GetString(7)));

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
