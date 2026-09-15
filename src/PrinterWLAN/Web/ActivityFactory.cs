using PrinterWLAN.Authentication;
using PrinterWLAN.Models;

namespace PrinterWLAN.Web;

public static class ActivityFactory
{
    public static ActivityRecord From(HttpContext context, string type, string? detailJson = null, long? userId = null, string? username = null)
    {
        var now = DateTimeOffset.Now;
        if (context.User.Identity?.IsAuthenticated == true && userId is null)
        {
            var idClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (long.TryParse(idClaim, out var parsed)) userId = parsed;
            username ??= context.User.Identity.Name;
        }
        return new ActivityRecord(now.ToUniversalTime(), now, type, userId, username,
            context.User.FindFirst("session_id")?.Value ?? context.Request.Headers["X-Session-Id"].FirstOrDefault(),
            context.Request.Headers["X-Device-Id"].FirstOrDefault(),
            context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.FirstOrDefault(),
            context.Request.Headers.AcceptLanguage.FirstOrDefault(), context.Request.Headers["X-Platform"].FirstOrDefault(),
            context.Request.Headers["X-Viewport"].FirstOrDefault(), context.Request.Headers["X-Screen"].FirstOrDefault(),
            context.Request.Headers["X-Timezone"].FirstOrDefault(), context.Request.Headers.Referer.FirstOrDefault(), detailJson);
    }
}
