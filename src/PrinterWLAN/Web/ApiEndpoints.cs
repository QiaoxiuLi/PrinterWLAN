using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PrinterWLAN.Authentication;
using PrinterWLAN.Documents;
using PrinterWLAN.Logging;
using PrinterWLAN.Models;
using PrinterWLAN.Printing;
using PrinterWLAN.Storage;
using PrinterWLAN.Users;

namespace PrinterWLAN.Web;

public static class ApiEndpoints
{
    public static void MapPrinterWlanApi(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        var api = app.MapGroup("/api");
        MapPublic(api);
        MapUser(api.MapGroup("/user"));
        MapAdmin(api.MapGroup("/admin"));
    }

    private static void MapPublic(RouteGroupBuilder api)
    {
        api.MapGet("/bootstrap", async (HttpContext context, AppDatabase database, AdminService admin, CancellationToken token) =>
        {
            var userAuth = await context.AuthenticateAsync("UserCookie");
            var adminAuth = await context.AuthenticateAsync("AdminCookie");
            return Results.Ok(new
            {
                siteName = await database.GetSettingAsync("site_name", "PrinterWLAN", token),
                adminConfigured = await admin.IsConfiguredAsync(token),
                userAuthenticated = userAuth.Succeeded,
                adminAuthenticated = adminAuth.Succeeded,
                version = "1.0.0"
            });
        });

        api.MapPost("/events", async ([FromBody] BrowserEvent request, HttpContext context, ActivityLogService logs, CancellationToken token) =>
        {
            var allowed = new[] { "page_view", "preview_opened" };
            if (!allowed.Contains(request.Type, StringComparer.Ordinal)) return Results.BadRequest(new { message = "无效的行为类型。" });
            await logs.WriteAsync(ActivityFactory.From(context, request.Type, JsonSerializer.Serialize(new { page = request.Page })), token);
            return Results.NoContent();
        });

        api.MapPost("/user-login", async ([FromBody] UserLogin request, HttpContext context, UserService users,
            PasswordService passwords, LoginThrottle throttle, ActivityLogService logs, CancellationToken token) =>
        {
            var key = $"user:{context.Connection.RemoteIpAddress}:{UserService.NormalizeUsername(request.Username ?? string.Empty)}";
            if (!throttle.IsAllowed(key, out var retry)) return Results.Json(new { message = $"尝试次数过多，请约 {retry} 秒后重试。" }, statusCode: 429);
            var user = string.IsNullOrWhiteSpace(request.Username) ? null : await users.FindByUsernameAsync(request.Username, token);
            if (user is null || string.IsNullOrEmpty(request.Password) || !passwords.Verify(user.PasswordHash, request.Password))
            {
                throttle.Failed(key);
                await logs.WriteAsync(ActivityFactory.From(context, "login_failed", null, user?.Id, user?.Username), token);
                return Results.Json(new { message = "用户名或密码不正确。" }, statusCode: 401);
            }
            throttle.Succeeded(key);
            var sessionId = Guid.NewGuid().ToString("N");
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, user.Username), new Claim("session_id", sessionId)
            ], "UserCookie"));
            await context.SignInAsync("UserCookie", principal);
            await logs.WriteAsync(ActivityFactory.From(context, "login_success", null, user.Id, user.Username) with { SessionId = sessionId }, token);
            return Results.Ok(new { username = user.Username });
        });

        api.MapPost("/admin-login", async ([FromBody] AdminLogin request, HttpContext context, AdminService admin,
            LoginThrottle throttle, ActivityLogService logs, CancellationToken token) =>
        {
            if (!await admin.IsConfiguredAsync(token)) return Results.BadRequest(new { message = "管理员密码尚未设置，请在服务器管理窗口中设置。" });
            var key = $"admin:{context.Connection.RemoteIpAddress}";
            if (!throttle.IsAllowed(key, out var retry)) return Results.Json(new { message = $"尝试次数过多，请约 {retry} 秒后重试。" }, statusCode: 429);
            if (string.IsNullOrEmpty(request.Password) || !await admin.VerifyAsync(request.Password, token))
            {
                throttle.Failed(key);
                await logs.WriteAsync(ActivityFactory.From(context, "admin_login_failed"), token);
                return Results.Json(new { message = "管理员密码不正确。" }, statusCode: 401);
            }
            throttle.Succeeded(key);
            var sessionId = Guid.NewGuid().ToString("N");
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Administrator"), new Claim("session_id", sessionId)], "AdminCookie"));
            await context.SignInAsync("AdminCookie", principal);
            await logs.WriteAsync(ActivityFactory.From(context, "admin_login_success") with { SessionId = sessionId }, token);
            return Results.Ok(new { ok = true });
        });
    }

    private static void MapUser(RouteGroupBuilder group)
    {
        group.RequireAuthorization("UserOnly");
        group.MapPost("/logout", async (HttpContext context, ActivityLogService logs, CancellationToken token) =>
        {
            await logs.WriteAsync(ActivityFactory.From(context, "logout"), token);
            await context.SignOutAsync("UserCookie");
            return Results.NoContent();
        });
        group.MapGet("/printers", async (IPrinterService printers, CancellationToken token) => Results.Ok(await printers.GetPrintersAsync(token)));
        group.MapPost("/documents", async (HttpRequest request, HttpContext context, AppDatabase database,
            DocumentService documents, ActivityLogService logs, CancellationToken token) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { message = "请选择文件。" });
            var form = await request.ReadFormAsync(token);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { message = "请选择文件。" });
            DateTimeOffset? clientModified = DateTimeOffset.TryParse(form["clientLastModified"], out var parsed) ? parsed : null;
            try
            {
                var document = await documents.ReceiveAsync(context.User.UserId(), file, clientModified, token);
                await logs.WriteAsync(ActivityFactory.From(context, "upload", JsonSerializer.Serialize(new { document.Id, document.OriginalFilename, document.FileSize, document.Extension, document.TotalPages })), token);
                return Results.Ok(new { document.Id, document.OriginalFilename, document.FileSize, document.Extension, document.TotalPages,
                    document.ClientLastModified, document.DocumentCreatedAt, document.DocumentModifiedAt, document.ServerReceivedAt });
            }
            catch (DocumentException exception) { return Results.BadRequest(new { message = exception.FriendlyMessage }); }
        }).DisableAntiforgery();
        group.MapGet("/documents/{id}/preview", async (string id, HttpContext context, AppDatabase database, CancellationToken token) =>
        {
            var document = await database.GetDocumentAsync(id, context.User.UserId(), token);
            if (document is null || !File.Exists(document.PdfPath)) return Results.NotFound(new { message = "预览文件已过期，请重新上传。" });
            await database.MarkDocumentPreviewedAsync(id, context.User.UserId(), DateTimeOffset.UtcNow, token);
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(document.PdfPath, "application/pdf", enableRangeProcessing: true);
        });
        group.MapDelete("/documents/{id}", async (string id, HttpContext context, AppDatabase database, DocumentService documents, CancellationToken token) =>
        {
            var document = await database.GetDocumentAsync(id, context.User.UserId(), token);
            if (document is not null) await documents.DeleteAsync(document, token);
            return Results.NoContent();
        });
        group.MapPost("/jobs", async ([FromBody] PrintRequest request, HttpContext context, AppDatabase database,
            UserService users, PrintJobService jobs, ActivityLogService logs, CancellationToken token) =>
        {
            var user = await users.FindByIdAsync(context.User.UserId(), token);
            var document = await database.GetDocumentAsync(request.DocumentId, context.User.UserId(), token);
            if (user is null || document is null) return Results.NotFound(new { message = "文件已过期，请重新上传。" });
            try
            {
                var result = await jobs.CreateAsync(user, document, request, context.User.SessionId(), context.Request.Headers["X-Device-Id"].FirstOrDefault(), context.Connection.RemoteIpAddress?.ToString(), token);
                if (result.Job is null) return Results.Json(new { message = $"请稍后再创建新的打印任务，约需等待 {result.RetryAfter} 秒。", retryAfter = result.RetryAfter }, statusCode: 429);
                await logs.WriteAsync(ActivityFactory.From(context, "print_submitted", JsonSerializer.Serialize(new { jobId = result.Job.Id, documentId = document.Id, request.PrinterName, request.PageRange, request.Copies })), token);
                return Results.Accepted($"/api/user/jobs/{result.Job.Id}", new { jobId = result.Job.Id, status = result.Job.Status });
            }
            catch (Exception exception) when (exception is FormatException or PrintValidationException)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });
        group.MapGet("/jobs/{id}", async (string id, HttpContext context, AppDatabase database, CancellationToken token) =>
        {
            var job = await database.GetJobAsync(id, context.User.UserId(), token);
            return job is null ? Results.NotFound() : Results.Ok(new { job.Id, job.Status, job.FriendlyError, job.SubmittedAt, job.SentAt, job.FailedAt });
        });
    }

    private static void MapAdmin(RouteGroupBuilder group)
    {
        group.RequireAuthorization("AdminOnly");
        group.MapPost("/logout", async (HttpContext context, ActivityLogService logs, CancellationToken token) =>
        {
            await logs.WriteAsync(ActivityFactory.From(context, "admin_logout"), token);
            await context.SignOutAsync("AdminCookie");
            return Results.NoContent();
        });
        group.MapGet("/settings", async (AppDatabase database, CancellationToken token) => Results.Ok(new { siteName = await database.GetSettingAsync("site_name", "PrinterWLAN", token), version = "1.0.0" }));
        group.MapPut("/settings", async ([FromBody] SiteSettings request, AppDatabase database, CancellationToken token) =>
        {
            var name = request.SiteName?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 80) return Results.BadRequest(new { message = "网站名称需要 1 到 80 个字符。" });
            await database.SetSettingAsync("site_name", name, token);
            return Results.Ok(new { siteName = name });
        });
        group.MapPost("/users/import", async (HttpRequest request, UserService users, CancellationToken token) =>
        {
            var form = await request.ReadFormAsync(token);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { message = "请选择 CSV 文件。" });
            if (file.Length > 10 * 1024 * 1024) return Results.BadRequest(new { message = "CSV 文件太大。" });
            try { await using var stream = file.OpenReadStream(); return Results.Ok(await users.ImportAsync(stream, token)); }
            catch (Exception exception) when (exception is InvalidDataException or CsvHelper.CsvHelperException or DecoderFallbackException)
            { return Results.BadRequest(new { message = "CSV 格式不正确，请确认文件只有一列用户名并使用 UTF-8 编码。" }); }
        }).DisableAntiforgery();
        group.MapGet("/users", async (string? query, int? page, int? pageSize, UserService users, CancellationToken token) =>
        {
            var actualPage = page ?? 1; var size = pageSize ?? 50;
            var values = await users.ListAsync(query, actualPage, size, token);
            return Results.Ok(new { items = values.Select(u => new { u.Id, u.Sequence, u.Username, u.CreatedAt, u.UpdatedAt }), total = await users.CountAsync(query, token), page = actualPage, pageSize = size });
        });
        group.MapGet("/users/{id:long}/password", async (long id, UserService users, CancellationToken token) =>
        {
            var user = await users.FindByIdAsync(id, token);
            return user is null ? Results.NotFound() : Results.Ok(new { password = users.RevealPassword(user) });
        });
        group.MapGet("/users/export", async (UserService users, CancellationToken token) =>
        {
            var stream = new MemoryStream(); await users.WriteCredentialsCsvAsync(stream, token); stream.Position = 0;
            return Results.File(stream, "text/csv; charset=utf-8", "PrinterWLAN_Users.csv");
        });
        group.MapGet("/records", async (string? username, DateTimeOffset? start, DateTimeOffset? end, string? type, string? ip,
            int? page, int? pageSize, ActivityLogService logs, CancellationToken token) =>
        {
            var result = await logs.QueryAsync(username, start, end, type, ip, page ?? 1, pageSize ?? 50, token);
            return Results.Ok(new { items = result.Records, total = result.Total, page = page ?? 1, pageSize = pageSize ?? 50,
                storageBytes = logs.GetTotalBytes(), maxBytes = 40L * 1024 * 1024 * 1024 });
        });
        group.MapGet("/log-slices", async (ActivityLogService logs, CancellationToken token) => Results.Ok(await logs.ListSlicesAsync(token)));
        group.MapPost("/logs/export", async ([FromBody] ExportRequest request, ActivityLogService logs, CancellationToken token) =>
        {
            try
            {
                var path = await logs.ExportAsync(request.Slices ?? [], token);
                return Results.File(path, "application/zip", Path.GetFileName(path), enableRangeProcessing: false);
            }
            catch (InvalidOperationException exception) { return Results.BadRequest(new { message = exception.Message }); }
        });
        group.MapPost("/logs/clear", async ([FromBody] AdminLogin request, HttpContext context, AdminService admin, ActivityLogService logs, CancellationToken token) =>
        {
            if (string.IsNullOrEmpty(request.Password) || !await admin.VerifyAsync(request.Password, token)) return Results.Json(new { message = "管理员密码不正确。" }, statusCode: 401);
            await logs.ClearAsync(token);
            await logs.WriteAsync(ActivityFactory.From(context, "logs_cleared"), token);
            return Results.Ok(new { ok = true });
        });
    }

    public sealed record UserLogin(string? Username, string? Password);
    public sealed record AdminLogin(string? Password);
    public sealed record BrowserEvent(string Type, string? Page);
    public sealed record SiteSettings(string? SiteName);
    public sealed record ExportRequest(string[]? Slices);
}
