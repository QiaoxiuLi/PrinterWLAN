using Microsoft.Data.Sqlite;
using PrinterWLAN.Storage;

namespace PrinterWLAN.Authentication;

public sealed class AdminService(AppDatabase database, PasswordService passwords)
{
    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) =>
        await GetHashAsync(cancellationToken) is not null;

    public async Task<bool> VerifyAsync(string password, CancellationToken cancellationToken = default)
    {
        var hash = await GetHashAsync(cancellationToken);
        return hash is not null && passwords.Verify(hash, password);
    }

    public async Task SetPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (password.Length < 8) throw new ArgumentException("管理员密码至少需要 8 个字符。", nameof(password));
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO admin_credential(singleton,password_hash,updated_at) VALUES(1,$hash,$now)
            ON CONFLICT(singleton) DO UPDATE SET password_hash=excluded.password_hash,updated_at=excluded.updated_at
            """;
        command.Parameters.AddWithValue("$hash", passwords.Hash(password));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string?> GetHashAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT password_hash FROM admin_credential WHERE singleton=1";
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }
}
