using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrinterWLAN.Authentication;
using PrinterWLAN.Logging;
using PrinterWLAN.Models;
using PrinterWLAN.Storage;
using PrinterWLAN.Users;

namespace PrinterWLAN.Tests;

public sealed class DatabaseBehaviorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PrinterWLAN-unit-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DuplicateImportRegeneratesPasswordAndPreservesSequenceAndExportOrder()
    {
        if (!OperatingSystem.IsWindows()) return;
        Environment.SetEnvironmentVariable("PRINTERWLAN_DATA_DIR", _directory);
        var paths = new AppPaths(); var database = new AppDatabase(paths); await database.InitializeAsync();
        var users = new UserService(database, new PasswordService(), new CredentialProtector());
        await using var first = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("用户名\n张三\nAlice\n"));
        var firstResult = await users.ImportAsync(first);
        Assert.Equal(2, firstResult.Added);
        var original = await users.FindByUsernameAsync("张三");
        Assert.NotNull(original); var oldPassword = users.RevealPassword(original);
        await using var second = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("张三\n李四\n"));
        var secondResult = await users.ImportAsync(second);
        Assert.Equal(1, secondResult.Added); Assert.Equal(1, secondResult.Updated);
        var updated = await users.FindByUsernameAsync("张三");
        Assert.NotNull(updated); Assert.Equal(original.Sequence, updated.Sequence); Assert.NotEqual(oldPassword, users.RevealPassword(updated));
        var ordered = await users.ListAsync(null, 1, 20);
        Assert.Equal(["张三", "Alice", "李四"], ordered.Select(x => x.Username));
        await using var export = new MemoryStream(); await users.WriteCredentialsCsvAsync(export);
        Assert.True(export.ToArray().AsSpan().StartsWith(System.Text.Encoding.UTF8.GetPreamble()));
    }

    [Fact]
    public async Task LogSlicesArchiveQueryAndClearWithoutTouchingAppDatabase()
    {
        Environment.SetEnvironmentVariable("PRINTERWLAN_DATA_DIR", _directory);
        var paths = new AppPaths(); var database = new AppDatabase(paths); await database.InitializeAsync();
        var anchor = DateOnly.FromDateTime(DateTime.Now).AddDays(-30);
        await database.SetSettingAsync("log_anchor_date", anchor.ToString("yyyy-MM-dd"));
        var options = Options.Create(new PrinterWlanOptions());
        var logs = new ActivityLogService(paths, database, options, NullLogger<ActivityLogService>.Instance);
        await logs.InitializeAsync();
        var local = new DateTimeOffset(anchor.ToDateTime(new TimeOnly(12, 0)), TimeZoneInfo.Local.GetUtcOffset(anchor.ToDateTime(new TimeOnly(12, 0))));
        await logs.WriteAsync(new ActivityRecord(local.ToUniversalTime(), local, "page_view", null, null, "s", "d", "127.0.0.1", "ua", "zh", "test", "320x568", "320x568", "UTC", null, null));
        await logs.MaintainAsync();
        var archived = (await logs.ListSlicesAsync()).Single(s => s.Start == anchor);
        Assert.Equal("archived", archived.State);
        var query = await logs.QueryAsync(null, null, null, null, null, 1, 10);
        Assert.Contains(query.Records, x => Equals(x["type"], "page_view"));
        await logs.ClearAsync();
        Assert.Equal("PrinterWLAN", await database.GetSettingAsync("site_name", "missing"));
        Assert.DoesNotContain(await logs.ListSlicesAsync(), s => s.Start == anchor);
    }

    [Fact]
    public async Task RetentionDeletesOldestArchiveFirst()
    {
        Environment.SetEnvironmentVariable("PRINTERWLAN_DATA_DIR", _directory);
        var paths = new AppPaths(); var database = new AppDatabase(paths); await database.InitializeAsync();
        await database.SetSettingAsync("log_anchor_date", "2020-01-01");
        Directory.CreateDirectory(paths.Logs);
        await File.WriteAllBytesAsync(Path.Combine(paths.Logs, "PrinterWLAN_2020-01-01_to_2020-01-10.zip"), new byte[90]);
        await File.WriteAllBytesAsync(Path.Combine(paths.Logs, "PrinterWLAN_2020-01-11_to_2020-01-20.zip"), new byte[90]);
        var limits = Options.Create(new PrinterWlanOptions { LogMaxBytes = 150, LogCleanupTargetBytes = 100 });
        var logs = new ActivityLogService(paths, database, limits, NullLogger<ActivityLogService>.Instance);
        await logs.MaintainAsync();
        Assert.False(File.Exists(Path.Combine(paths.Logs, "PrinterWLAN_2020-01-01_to_2020-01-10.zip")));
        Assert.True(File.Exists(Path.Combine(paths.Logs, "PrinterWLAN_2020-01-11_to_2020-01-20.zip")));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PRINTERWLAN_DATA_DIR", null);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
