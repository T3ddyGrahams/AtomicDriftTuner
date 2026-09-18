using System.Net;
using System.Text.Json;
using AtomicDriftTuner.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AtomicDriftTuner.Services;

public sealed partial class RemoteServerService
{
    private void MapSetupCaptureEndpoint(WebApplication app)
    {
        // Only the paired CSP app on this PC may provide game setup evidence.
        // The ordinary API's 16 KiB limit remains unchanged on every other route.
        app.MapPost("/api/companion/setup", async (HttpContext context) =>
        {
            var ip = context.Connection.RemoteIpAddress;
            if (ip is null || !IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip)) return Results.StatusCode(403);
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            const long maximumBody = 400_000; // Bounded JSON escaping of a 64 KiB INI.
            if (context.Request.ContentLength > maximumBody) return Results.StatusCode(413);
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = maximumBody;
            LiveSetupCaptureRequest? request;
            try { request = await context.Request.ReadFromJsonAsync<LiveSetupCaptureRequest>(context.RequestAborted); }
            catch (BadHttpRequestException ex) { return Results.StatusCode(ex.StatusCode); }
            catch (Exception ex) when (ex is JsonException or IOException) { return Results.BadRequest(new { error = "Invalid setup capture." }); }
            if (request is null || request.Nonce is not { Length: 64 } || !request.Nonce.All(Uri.IsHexDigit) ||
                !Guid.TryParseExact(request.WindowId, "N", out _)) return Results.BadRequest(new { error = "Invalid setup capture challenge." });
            if (CompanionSetupHandler is null) return Results.StatusCode(503);
            if (!await _companionCommandGate.WaitAsync(0, context.RequestAborted)) return Results.StatusCode(409);
            try
            {
                if (!TokenMatches(GetSuppliedToken(context))) return Results.StatusCode(401);
                var result = await CompanionSetupHandler(request, context.RequestAborted);
                return Results.Json(result, statusCode: result.Ok ? 200 : 409);
            }
            finally { _companionCommandGate.Release(); }
        });
    }
}
