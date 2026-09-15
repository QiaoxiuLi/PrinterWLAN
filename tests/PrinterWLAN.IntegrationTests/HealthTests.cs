using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PrinterWLAN.Authentication;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Serilog;

namespace PrinterWLAN.IntegrationTests;

public sealed class HealthTests : IClassFixture<PrinterWlanFactory>
{
    private readonly HttpClient _client;
    private readonly PrinterWlanFactory _factory;
    public HealthTests(PrinterWlanFactory factory) { _factory = factory; _client = factory.CreateClient(); }
    [Fact]
    public async Task HealthDoesNotLeakInternals()
    {
        var body = await _client.GetStringAsync("/health");
        Assert.Equal("{\"status\":\"ok\"}", body);
        Assert.DoesNotContain("database", body, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task AnonymousCannotAccessAdminApi() => Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/admin/users")).StatusCode);
    [Fact] public async Task AnonymousCannotAccessPrinterApi() => Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/user/printers")).StatusCode);

    [Fact]
    public async Task AdminCannotClearLogsWithoutPasswordReentry()
    {
        var bootstrap = await _client.GetAsync("/api/bootstrap");
        var setCookie = bootstrap.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("PrinterWLAN-CSRF=", StringComparison.Ordinal));
        var csrf = setCookie.Split(';')[0].Split('=')[1];
        var service = _factory.Services.GetRequiredService<AdminService>();
        var password = "Integration-" + Guid.NewGuid().ToString("N");
        await service.SetPasswordAsync(password);
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/admin-login")
        { Content = JsonContent.Create(new { password }) };
        login.Headers.Add("X-CSRF-Token", csrf);
        Assert.True((await _client.SendAsync(login)).IsSuccessStatusCode);
        using var clear = new HttpRequestMessage(HttpMethod.Post, "/api/admin/logs/clear")
        { Content = JsonContent.Create(new { password = "wrong" }) };
        clear.Headers.Add("X-CSRF-Token", csrf);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await _client.SendAsync(clear)).StatusCode);
    }
}

public sealed class PrinterWlanFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PrinterWLAN-tests-" + Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("PRINTERWLAN_DATA_DIR", _directory);
        builder.UseSetting("PrinterWLAN:UseFakePrinter", "true");
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Log.CloseAndFlushAsync();
        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("PRINTERWLAN_DATA_DIR", null);
        for (var attempt = 1; attempt <= 10 && Directory.Exists(_directory); attempt++)
        {
            try
            {
                Directory.Delete(_directory, true);
            }
            catch (IOException) when (attempt < 10)
            {
                await Task.Delay(100);
            }
        }
    }
}
