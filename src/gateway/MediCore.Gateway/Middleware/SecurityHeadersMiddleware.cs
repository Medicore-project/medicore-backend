

namespace MediCore.Gateway.Middleware;

/// <summary>
/// Injects security headers on every HTTP response.
/// Must be registered early in the pipeline (before UseRouting / MapReverseProxy)
/// so that headers are present even on proxied responses.
///
/// Headers applied:
///   X-Content-Type-Options  — prevent MIME sniffing
///   X-Frame-Options         — prevent clickjacking
///   Content-Security-Policy — restrict resource origins
///   Strict-Transport-Security (HSTS) — force HTTPS in production
///   Referrer-Policy         — reduce referrer leakage
///   Permissions-Policy      — disable unused browser features
///
/// The Server header is stripped to avoid version disclosure.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _env  = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Register a callback that fires just before headers are sent to the client.
        // This is the correct hook — response headers cannot be set after the body starts.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Prevent MIME type sniffing
            headers["X-Content-Type-Options"] = "nosniff";

            // Block the page from being embedded in frames (clickjacking defence)
            headers["X-Frame-Options"] = "DENY";

            // Content Security Policy — tightened further by each service team as needed
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; " +
                "connect-src 'self'; " +
                "font-src 'self'; " +
                "object-src 'none'; " +
                "frame-ancestors 'none'";

            // HSTS — 2 years, applied in all environments so browsers remember it
            // (Dev certs are trusted locally; the gateway is HTTP-only in dev anyway)
            if (!_env.IsDevelopment())
            {
                headers["Strict-Transport-Security"] =
                    "max-age=63072000; includeSubDomains; preload";
            }

            // Reduce referrer leakage
            headers["Referrer-Policy"] = "no-referrer";

            // Disable browser features not needed by a REST API
            headers["Permissions-Policy"] =
                "camera=(), microphone=(), geolocation=(), payment=()";

            // Remove the Server header to avoid version disclosure
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
