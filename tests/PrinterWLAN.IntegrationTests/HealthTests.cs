using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PrinterWLAN.Authentication;
using PrinterWLAN.Printing;
using PrinterWLAN.Users;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
    [Fact] public async Task AnonymousCannotAccessPrinterApi() => Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/user/print-capabilities")).StatusCode);

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

    [Fact]
    public async Task AdminSelectsPrinterWhileUserGetsNoPrinterIdentityAndCannotInjectOne()
    {
        var csrf = await BootstrapAndGetCsrfAsync();
        var admin = _factory.Services.GetRequiredService<AdminService>();
        var adminPassword = "Integration-" + Guid.NewGuid().ToString("N");
        await admin.SetPasswordAsync(adminPassword);
        using var adminLogin = JsonRequest(HttpMethod.Post, "/api/admin-login", csrf, new { password = adminPassword });
        Assert.True((await _client.SendAsync(adminLogin)).IsSuccessStatusCode);

        var before = await _client.GetFromJsonAsync<JsonElement>("/api/admin/printers");
        var printers = before.GetProperty("printers");
        Assert.Equal(2, printers.GetArrayLength());
        var selectedId = printers[0].GetProperty("id").GetString();
        using var select = JsonRequest(HttpMethod.Put, "/api/admin/printer", csrf, new { printerId = selectedId });
        Assert.True((await _client.SendAsync(select)).IsSuccessStatusCode);
        var after = await _client.GetFromJsonAsync<JsonElement>("/api/admin/printers");
        Assert.Equal(selectedId, after.GetProperty("selectedPrinterId").GetString());

        var users = _factory.Services.GetRequiredService<UserService>();
        var username = "api-user-" + Guid.NewGuid().ToString("N")[..8];
        await using var csv = new MemoryStream(Encoding.UTF8.GetBytes(username + "\n"));
        await users.ImportAsync(csv);
        var user = await users.FindByUsernameAsync(username);
        Assert.NotNull(user);
        using var userLogin = JsonRequest(HttpMethod.Post, "/api/user-login", csrf,
            new { username, password = users.RevealPassword(user) });
        Assert.True((await _client.SendAsync(userLogin)).IsSuccessStatusCode);

        var capabilitiesJson = await _client.GetStringAsync("/api/user/print-capabilities");
        Assert.DoesNotContain("printerName", capabilitiesJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakePrinterService.Capability.Name, capabilitiesJson, StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await _client.GetAsync("/api/user/printers")).StatusCode);

        foreach (var field in new[] { "printer", "printerId", "printerName", "targetPrinter" })
        {
            using var injected = JsonRequest(HttpMethod.Post, "/api/user/jobs", csrf,
                new Dictionary<string, object?>
                {
                    ["documentId"] = "missing",
                    ["paperSize"] = "A4",
                    [field] = FakePrinterService.SecondaryCapability.Name
                });
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await _client.SendAsync(injected)).StatusCode);
        }
    }

    private async Task<string> BootstrapAndGetCsrfAsync()
    {
        var bootstrap = await _client.GetAsync("/api/bootstrap");
        var setCookie = bootstrap.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("PrinterWLAN-CSRF=", StringComparison.Ordinal));
        return setCookie.Split(';')[0].Split('=')[1];
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, string csrf, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-Token", csrf);
        return request;
    }
}

public sealed class PrinterWlanFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PrinterWLAN-tests-" + Guid.NewGuid().ToString("N"));
    private HttpClient? _warmupClient;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("PRINTERWLAN_DATA_DIR", _directory);
        builder.UseSetting("PrinterWLAN:UseFakePrinter", "true");
    }
    public Task InitializeAsync()
    {
        // WebApplicationFactory's first CreateClient call is not safe when several xUnit test instances
        // are constructed concurrently. Start the shared server before any test constructor runs.
        _warmupClient = CreateClient();
        return Task.CompletedTask;
    }
    public new async Task DisposeAsync()
    {
        _warmupClient?.Dispose();
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
