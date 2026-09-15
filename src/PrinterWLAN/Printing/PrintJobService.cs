using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PrinterWLAN.Models;
using PrinterWLAN.Storage;

namespace PrinterWLAN.Printing;

public sealed class PrintJobService(AppDatabase database, IPrinterService printers, PrintJobQueue queue)
{
    private readonly SemaphoreSlim _createLock = new(1, 1);

    public async Task<(PrintJobRecord? Job, int RetryAfter)> CreateAsync(UserRecord user, DocumentRecord document,
        PrintRequest request, string sessionId, string? deviceId, string? ipAddress, CancellationToken cancellationToken)
    {
        request.SelectedPages = PageRangeParser.Parse(request.PageRange, document.TotalPages);
        PageRangeParser.ValidateJobLimit(request.SelectedPages.Count, request.Copies);
        await printers.ValidateAsync(request, cancellationToken);
        await _createLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using var rate = connection.CreateCommand();
            rate.Transaction = transaction;
            rate.CommandText = "SELECT last_job_at FROM user_rate_limits WHERE user_id=$uid";
            rate.Parameters.AddWithValue("$uid", user.Id);
            var lastRaw = (string?)await rate.ExecuteScalarAsync(cancellationToken);
            if (lastRaw is not null)
            {
                var elapsed = DateTimeOffset.UtcNow - DateTimeOffset.Parse(lastRaw, CultureInfo.InvariantCulture);
                if (elapsed < TimeSpan.FromSeconds(60))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (null, Math.Max(1, 60 - (int)elapsed.TotalSeconds));
                }
            }
            var now = DateTimeOffset.UtcNow;
            await using var upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO user_rate_limits(user_id,last_job_at) VALUES($uid,$now)
                ON CONFLICT(user_id) DO UPDATE SET last_job_at=excluded.last_job_at
                """;
            upsert.Parameters.AddWithValue("$uid", user.Id);
            upsert.Parameters.AddWithValue("$now", now.ToString("O"));
            await upsert.ExecuteNonQueryAsync(cancellationToken);
            var id = Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(request);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO print_jobs(job_id,user_id,username,document_id,status,settings_json,submitted_at,session_id,device_id,ip_address)
                VALUES($id,$uid,$username,$document,'queued',$settings,$now,$session,$device,$ip)
                """;
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$uid", user.Id);
            insert.Parameters.AddWithValue("$username", user.Username);
            insert.Parameters.AddWithValue("$document", document.Id);
            insert.Parameters.AddWithValue("$settings", json);
            insert.Parameters.AddWithValue("$now", now.ToString("O"));
            insert.Parameters.AddWithValue("$session", sessionId);
            insert.Parameters.AddWithValue("$device", deviceId is null ? DBNull.Value : deviceId);
            insert.Parameters.AddWithValue("$ip", ipAddress is null ? DBNull.Value : ipAddress);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await using var reserve = connection.CreateCommand();
            reserve.Transaction = transaction;
            reserve.CommandText = "UPDATE documents SET status='processing' WHERE document_id=$document AND user_id=$uid";
            reserve.Parameters.AddWithValue("$document", document.Id);
            reserve.Parameters.AddWithValue("$uid", user.Id);
            await reserve.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var job = new PrintJobRecord(id, user.Id, user.Username, document.Id, "queued", json, now, null, null, null, null, null, sessionId, deviceId, ipAddress);
            await queue.EnqueueAsync(id, cancellationToken);
            return (job, 0);
        }
        finally { _createLock.Release(); }
    }
}
