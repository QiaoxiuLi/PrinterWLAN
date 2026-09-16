using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using PrinterWLAN.Authentication;
using PrinterWLAN.Background;
using PrinterWLAN.Documents;
using PrinterWLAN.Logging;
using PrinterWLAN.Models;
using PrinterWLAN.Printing;
using PrinterWLAN.Services;
using PrinterWLAN.Storage;
using PrinterWLAN.Users;
using PrinterWLAN.Web;
using Serilog;

var cliExitCode = await CliRunner.TryRunAsync(args);
if (cliExitCode is not null) return cliExitCode.Value;

var paths = new AppPaths();
paths.EnsureCreated();
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.File(Path.Combine(paths.Diagnostics, "PrinterWLAN-.log"), rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14, shared: true)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
    builder.Host.UseWindowsService(options => options.ServiceName = "PrinterWLAN").UseSerilog();
    builder.Services.Configure<PrinterWlanOptions>(builder.Configuration.GetSection("PrinterWLAN"));
    var configured = builder.Configuration.GetSection("PrinterWLAN").Get<PrinterWlanOptions>() ?? new PrinterWlanOptions();
    builder.WebHost.ConfigureKestrel(server =>
    {
        server.ListenAnyIP(configured.Port);
        server.Limits.MaxRequestBodySize = configured.MaxUploadBytes + 1024 * 1024;
    });
    builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = configured.MaxUploadBytes + 1024 * 1024);
    builder.Services.AddSingleton(paths);
    builder.Services.AddSingleton<AppDatabase>();
    builder.Services.AddSingleton<PasswordService>();
    builder.Services.AddSingleton<CredentialProtector>();
    builder.Services.AddSingleton<AdminService>();
    builder.Services.AddSingleton<UserService>();
    builder.Services.AddSingleton<LoginThrottle>();
    builder.Services.AddSingleton<IWordConverter, LibreOfficeConverter>();
    builder.Services.AddSingleton<DocumentService>();
    builder.Services.AddSingleton<ActivityLogService>();
    builder.Services.AddSingleton<PrintJobQueue>();
    builder.Services.AddSingleton<PrinterSelectionService>();
    builder.Services.AddSingleton<PrintJobService>();
    if (configured.UseFakePrinter) builder.Services.AddSingleton<IPrinterService, FakePrinterService>();
    else builder.Services.AddSingleton<IPrinterService, WindowsPrinterService>();
    builder.Services.AddHostedService<PrintWorker>();
    builder.Services.AddHostedService<MaintenanceWorker>();
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "UserCookie";
        options.DefaultChallengeScheme = "UserCookie";
    })
    .AddCookie("UserCookie", options => ConfigureCookie(options, "/"))
    .AddCookie("AdminCookie", options => ConfigureCookie(options, "/"));
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy("UserOnly", policy => { policy.AddAuthenticationSchemes("UserCookie"); policy.RequireAuthenticatedUser(); })
        .AddPolicy("AdminOnly", policy => { policy.AddAuthenticationSchemes("AdminCookie"); policy.RequireAuthenticatedUser(); });

    var app = builder.Build();
    await app.Services.GetRequiredService<AppDatabase>().InitializeAsync();
    await app.Services.GetRequiredService<ActivityLogService>().InitializeAsync();
    app.UseExceptionHandler(error => error.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        context.RequestServices.GetRequiredService<ILogger<Program>>()
            .LogError(exception, "Unhandled request error for {Method} {Path}", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { message = "操作未能完成，请稍后重试。" });
    }));
    app.UseMiddleware<CsrfMiddleware>();
    app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = static response =>
    {
        if (response.Context.Request.Path.StartsWithSegments("/vendor")) response.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    }});
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapPrinterWlanApi();
    app.MapFallbackToFile("index.html");
    Log.Information("PrinterWLAN 1.1.0 starting on HTTP port {Port}", configured.Port);
    await app.RunAsync();
    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "PrinterWLAN terminated unexpectedly. The HTTP port may be unavailable.");
    return 1;
}
finally { await Log.CloseAndFlushAsync(); }

static void ConfigureCookie(CookieAuthenticationOptions options, string loginPath)
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.None;
    options.Cookie.IsEssential = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = loginPath;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 401; return Task.CompletedTask; }
        context.Response.Redirect(context.RedirectUri); return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
}

public partial class Program;
