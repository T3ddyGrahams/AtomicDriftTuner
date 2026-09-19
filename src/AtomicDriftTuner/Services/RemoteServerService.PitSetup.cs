using System.Net;
using System.Text.Json;
using AtomicDriftTuner.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AtomicDriftTuner.Services;

public sealed partial class RemoteServerService
{
    public Func<PitSetupCommand, CancellationToken, Task<PitSetupResponse>>? PitSetupHandler { get; set; }

    private void MapPitSetupEndpoint(WebApplication app)
    {
        app.MapPost("/api/companion/pit-setup", async (HttpContext context) =>
        {
            var ip = context.Connection.RemoteIpAddress;
            if (ip is null || !IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip)) return Results.StatusCode(403);
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            PitSetupCommand? request;
            try { request = await context.Request.ReadFromJsonAsync<PitSetupCommand>(context.RequestAborted); }
            catch (Exception ex) when (ex is JsonException or BadHttpRequestException or IOException)
            { return Results.BadRequest(new { error = "Invalid pit setup command." }); }
            if (!PitSetupCoordinator.Valid(request)) return Results.BadRequest(new { error = "Invalid pit setup command." });
            if (PitSetupHandler is null) return Results.StatusCode(503);
            if (!await _companionCommandGate.WaitAsync(0, context.RequestAborted)) return Results.StatusCode(409);
            try
            {
                if (!TokenMatches(GetSuppliedToken(context))) return Results.StatusCode(401);
                var response = await PitSetupHandler(request!, context.RequestAborted);
                return Results.Json(response, statusCode: response.Ok ? 200 : 409);
            }
            finally { _companionCommandGate.Release(); }
        });
    }
}
