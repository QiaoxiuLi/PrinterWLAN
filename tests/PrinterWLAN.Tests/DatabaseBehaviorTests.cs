using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using PrinterWLAN.Authentication;
using PrinterWLAN.Logging;
using PrinterWLAN.Models;
using PrinterWLAN.Printing;
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
        Assert.Equal(1, await users.CountAsync("张"));
        Assert.Equal(0, await users.CountAsync("%"));
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
        var record = new ActivityRecord(local.ToUniversalTime(), local, "page_view", null, null, "s", "d", "127.0.0.1", "ua", "zh", "test", "320x568", "320x568", "UTC", null, null);
        await logs.WriteAsync(record);
        await logs.WriteAsync(record with { UtcTime = record.UtcTime.AddSeconds(1), LocalTime = record.LocalTime.AddSeconds(1), Type = "upload" });
        await logs.WriteAsync(record with { UtcTime = record.UtcTime.AddSeconds(2), LocalTime = record.LocalTime.AddSeconds(2), Type = "preview" });
        await logs.MaintainAsync();
        var archived = (await logs.ListSlicesAsync()).Single(s => s.Start == anchor);
        Assert.Equal("archived", archived.State);
        var firstPage = await logs.QueryAsync(null, null, null, null, null, 1, 2);
        Assert.Equal(3, firstPage.Total);
        Assert.Equal(["preview", "upload"], firstPage.Records.Select(x => x["type"]));
        var secondPage = await logs.QueryAsync(null, null, null, null, null, 2, 2);
        Assert.Equal(3, secondPage.Total);
        Assert.Single(secondPage.Records);
        Assert.Equal("page_view", secondPage.Records[0]["type"]);
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

    [Fact]
    public async Task UpgradePreservesExistingDataAndRequiresExplicitPrinterSelection()
    {
        Environment.SetEnvironmentVariable("PRINTERWLAN_DATA_DIR", _directory);
        var paths = new AppPaths();
        var database = new AppDatabase(paths);
        await database.InitializeAsync();
        await database.SetSettingAsync("site_name", "Existing Printer Site");

        await new AppDatabase(paths).InitializeAsync();

        Assert.Equal("Existing Printer Site", await database.GetSettingAsync("site_name", "missing"));
        Assert.Equal(string.Empty, await database.GetSettingAsync(PrinterSelectionService.SelectedPrinterIdKey, string.Empty));
    }

    [Fact]
    public async Task AdminSelectionPersistsAndSwitchOnlyChangesNewSubmissions()
    {
        Environment.SetEnvironmentVariable("PRINTERWLAN_DATA_DIR", _directory);
        var paths = new AppPaths();
        var database = new AppDatabase(paths);
        await database.InitializeAsync();
        var firstService = new PrinterSelectionService(database, new FakePrinterService());

        var unconfigured = await Assert.ThrowsAsync<PrintValidationException>(() => firstService.GetSelectedAsync());
        Assert.Equal("当前暂未配置打印机，请联系管理员。", unconfigured.Message);

        await firstService.SelectAsync(FakePrinterService.Capability.Id);
        var queuedForA = await firstService.CreateRequestAsync(NewSubmission());
        var restartedService = new PrinterSelectionService(new AppDatabase(paths), new FakePrinterService());
        Assert.Equal(FakePrinterService.Capability.Id, (await restartedService.GetStateAsync()).SelectedPrinterId);

        await restartedService.SelectAsync(FakePrinterService.SecondaryCapability.Id);
        var queuedForB = await restartedService.CreateRequestAsync(NewSubmission() with { ColorMode = "monochrome" });

        Assert.Equal(FakePrinterService.Capability.Id, queuedForA.PrinterId);
        Assert.Equal(FakePrinterService.Capability.Name, queuedForA.PrinterName);
        Assert.Equal(FakePrinterService.SecondaryCapability.Id, queuedForB.PrinterId);
        Assert.NotEqual(queuedForA.PrinterId, queuedForB.PrinterId);
    }

    private static PrintSubmissionRequest NewSubmission() => new()
    {
        DocumentId = "document",
        PaperSize = "A4",
        ColorMode = "color"
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("PRINTERWLAN_DATA_DIR", null);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
