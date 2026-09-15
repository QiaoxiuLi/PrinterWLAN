using Microsoft.Data.Sqlite;
using PrinterWLAN.Models;

namespace PrinterWLAN.Storage;

public sealed class AppDatabase(AppPaths paths)
{
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = paths.AppDatabase,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = true
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS settings (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS admin_credential (
              singleton INTEGER PRIMARY KEY CHECK(singleton=1),
              password_hash TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS users (
              user_id INTEGER PRIMARY KEY AUTOINCREMENT,
              username TEXT NOT NULL,
              normalized_username TEXT NOT NULL UNIQUE,
              password_hash TEXT NOT NULL,
              encrypted_exportable_password BLOB NOT NULL,
              sequence INTEGER NOT NULL UNIQUE,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_users_sequence ON users(sequence);
            CREATE TABLE IF NOT EXISTS documents (
              document_id TEXT PRIMARY KEY,
              user_id INTEGER NOT NULL REFERENCES users(user_id),
              original_filename TEXT NOT NULL,
              extension TEXT NOT NULL,
              detected_mime TEXT NOT NULL,
              file_size INTEGER NOT NULL,
              client_last_modified TEXT NULL,
              document_created_at TEXT NULL,
              document_modified_at TEXT NULL,
              server_received_at TEXT NOT NULL,
              preview_at TEXT NULL,
              conversion_duration_ms INTEGER NOT NULL DEFAULT 0,
              total_pages INTEGER NOT NULL,
              source_path TEXT NOT NULL,
              pdf_path TEXT NOT NULL,
              status TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_documents_received ON documents(server_received_at);
            CREATE TABLE IF NOT EXISTS print_jobs (
              job_id TEXT PRIMARY KEY,
              user_id INTEGER NOT NULL REFERENCES users(user_id),
              username TEXT NOT NULL,
              document_id TEXT NOT NULL,
              status TEXT NOT NULL,
              settings_json TEXT NOT NULL,
              submitted_at TEXT NOT NULL,
              processing_started_at TEXT NULL,
              sent_at TEXT NULL,
              failed_at TEXT NULL,
              friendly_error TEXT NULL,
              internal_error TEXT NULL,
              session_id TEXT NOT NULL,
              device_id TEXT NULL,
              ip_address TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_jobs_user_submitted ON print_jobs(user_id,submitted_at DESC);
            CREATE TABLE IF NOT EXISTS user_rate_limits (
              user_id INTEGER PRIMARY KEY REFERENCES users(user_id),
              last_job_at TEXT NOT NULL
            );
            INSERT OR IGNORE INTO settings(key,value,updated_at) VALUES('site_name','PrinterWLAN',CURRENT_TIMESTAMP);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnAsync(connection, "documents", "preview_at", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "documents", "conversion_duration_ms", "INTEGER NOT NULL DEFAULT 0", cancellationToken);

        // An interrupted service cannot safely resume an in-process PrintDocument call.
        await using var repair = connection.CreateCommand();
        repair.CommandText = """
            UPDATE print_jobs SET status='interrupted', failed_at=$now,
              friendly_error='服务重启中断了此任务，请重新提交。'
            WHERE status IN ('queued','processing');
            UPDATE documents SET status='ready' WHERE status='processing';
            """;
        repair.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await repair.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    public async Task<string> GetSettingAsync(string key, string fallback, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        return (string?)await command.ExecuteScalarAsync(cancellationToken) ?? fallback;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key,value,updated_at) VALUES($key,$value,$now)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveDocumentAsync(DocumentRecord document, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents(document_id,user_id,original_filename,extension,detected_mime,file_size,
              client_last_modified,document_created_at,document_modified_at,server_received_at,preview_at,
              conversion_duration_ms,total_pages,source_path,pdf_path,status)
            VALUES($id,$uid,$name,$ext,$mime,$size,$client,$created,$modified,$received,$preview,$conversion,$pages,$source,$pdf,$status);
            """;
        command.Parameters.AddWithValue("$id", document.Id);
        command.Parameters.AddWithValue("$uid", document.UserId);
        command.Parameters.AddWithValue("$name", document.OriginalFilename);
        command.Parameters.AddWithValue("$ext", document.Extension);
        command.Parameters.AddWithValue("$mime", document.DetectedMime);
        command.Parameters.AddWithValue("$size", document.FileSize);
        command.Parameters.AddWithValue("$client", Db(document.ClientLastModified));
        command.Parameters.AddWithValue("$created", Db(document.DocumentCreatedAt));
        command.Parameters.AddWithValue("$modified", Db(document.DocumentModifiedAt));
        command.Parameters.AddWithValue("$received", document.ServerReceivedAt.ToString("O"));
        command.Parameters.AddWithValue("$preview", Db(document.PreviewAt));
        command.Parameters.AddWithValue("$conversion", document.ConversionDurationMs);
        command.Parameters.AddWithValue("$pages", document.TotalPages);
        command.Parameters.AddWithValue("$source", document.SourcePath);
        command.Parameters.AddWithValue("$pdf", document.PdfPath);
        command.Parameters.AddWithValue("$status", document.Status);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DocumentRecord?> GetDocumentAsync(string id, long? userId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document_id,user_id,original_filename,extension,detected_mime,file_size,client_last_modified,
              document_created_at,document_modified_at,server_received_at,preview_at,conversion_duration_ms,
              total_pages,source_path,pdf_path,status
            FROM documents WHERE document_id=$id AND ($uid IS NULL OR user_id=$uid)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$uid", userId is null ? DBNull.Value : userId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new DocumentRecord(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetInt64(5), ParseNullable(reader, 6), ParseNullable(reader, 7),
            ParseNullable(reader, 8), DateTimeOffset.Parse(reader.GetString(9)), ParseNullable(reader, 10), reader.GetInt64(11),
            reader.GetInt32(12), reader.GetString(13), reader.GetString(14), reader.GetString(15));
    }

    public async Task MarkDocumentPreviewedAsync(string id, long userId, DateTimeOffset previewedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE documents SET preview_at=COALESCE(preview_at,$now) WHERE document_id=$id AND user_id=$uid";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$uid", userId);
        command.Parameters.AddWithValue("$now", previewedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteDocumentRowAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM documents WHERE document_id=$id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentRecord>> GetExpiredDocumentsAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        var result = new List<DocumentRecord>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT document_id FROM documents WHERE server_received_at<$before AND status!='processing'";
        command.Parameters.AddWithValue("$before", before.ToString("O"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = await GetDocumentAsync(reader.GetString(0), cancellationToken: cancellationToken);
            if (item is not null) result.Add(item);
        }
        return result;
    }

    public async Task<PrintJobRecord?> GetJobAsync(string id, long? userId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job_id,user_id,username,document_id,status,settings_json,submitted_at,processing_started_at,
              sent_at,failed_at,friendly_error,internal_error,session_id,device_id,ip_address
            FROM print_jobs WHERE job_id=$id AND ($uid IS NULL OR user_id=$uid)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$uid", userId is null ? DBNull.Value : userId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new PrintJobRecord(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), DateTimeOffset.Parse(reader.GetString(6)), ParseNullable(reader, 7),
            ParseNullable(reader, 8), ParseNullable(reader, 9), GetNullable(reader, 10), GetNullable(reader, 11),
            reader.GetString(12), GetNullable(reader, 13), GetNullable(reader, 14));
    }

    public async Task UpdateJobAsync(string id, string status, string? friendlyError = null, string? internalError = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var timestampColumn = status switch { "processing" => "processing_started_at", "sent" => "sent_at", _ => "failed_at" };
        command.CommandText = $"UPDATE print_jobs SET status=$status,{timestampColumn}=$now,friendly_error=$friendly,internal_error=$internal WHERE job_id=$id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$friendly", friendlyError is null ? DBNull.Value : friendlyError);
        command.Parameters.AddWithValue("$internal", internalError is null ? DBNull.Value : internalError);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object Db(DateTimeOffset? value) => value is null ? DBNull.Value : value.Value.ToString("O");
    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column,
        string definition, CancellationToken cancellationToken)
    {
        await using var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }
    private static DateTimeOffset? ParseNullable(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
    private static string? GetNullable(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
