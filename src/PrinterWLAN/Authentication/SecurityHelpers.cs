using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;

namespace PrinterWLAN.Authentication;

public static class ClaimsExtensions
{
    public static long UserId(this ClaimsPrincipal principal) =>
        long.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);
    public static string SessionId(this ClaimsPrincipal principal) => principal.FindFirstValue("session_id") ?? string.Empty;
}

public sealed class LoginThrottle
{
    private readonly ConcurrentDictionary<string, Attempt> _attempts = new(StringComparer.Ordinal);
    public bool IsAllowed(string key, out int retryAfterSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_attempts.TryGetValue(key, out var attempt) || now - attempt.WindowStart >= TimeSpan.FromMinutes(5))
        {
            retryAfterSeconds = 0;
            return true;
        }
        if (attempt.Count < 10) { retryAfterSeconds = 0; return true; }
        retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((attempt.WindowStart.AddMinutes(5) - now).TotalSeconds));
        return false;
    }
    public void Failed(string key) => _attempts.AddOrUpdate(key, _ => new(1, DateTimeOffset.UtcNow), (_, old) =>
        DateTimeOffset.UtcNow - old.WindowStart >= TimeSpan.FromMinutes(5) ? new(1, DateTimeOffset.UtcNow) : old with { Count = old.Count + 1 });
    public void Succeeded(string key) => _attempts.TryRemove(key, out _);
    private sealed record Attempt(int Count, DateTimeOffset WindowStart);
}

public sealed class CsrfMiddleware(RequestDelegate next)
{
    public const string CookieName = "PrinterWLAN-CSRF";
    public const string HeaderName = "X-CSRF-Token";
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie) || string.IsNullOrEmpty(cookie))
        {
            cookie = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            context.Response.Cookies.Append(CookieName, cookie, new CookieOptions
            {
                HttpOnly = false, SameSite = SameSiteMode.Strict, Secure = false, IsEssential = true, Path = "/"
            });
        }
        if (!SafeMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase) &&
            context.Request.Path.StartsWithSegments("/api") &&
            (!context.Request.Headers.TryGetValue(HeaderName, out var header) || !FixedEquals(cookie, header.ToString())))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "页面验证信息已过期，请刷新后重试。" });
            return;
        }
        await next(context);
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(left);
        var b = System.Text.Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
