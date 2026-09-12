using System.Net;
using System.Text.Json;
using AtomicDriftTuner.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AtomicDriftTuner.Services;

public sealed partial class RemoteServerService
{
    public Func<CancellationToken, Task<CompanionStatus>>? CompanionStatusHandler { get; set; }
    public Func<CompanionCommand, CancellationToken, Task<RemoteActionResponse>>? CompanionCommandHandler { get; set; }
    private readonly SemaphoreSlim _companionCommandGate = new(1, 1);

    private void MapCompanionEndpoints(WebApplication app)
    {
        // Both transports use the same recorder, validation and admission gate.
        // Browser access is still restricted to paired private-network clients by middleware.
        MapRecorderEndpoints(app, "/api/companion", localOnly: true, "In-game companion");
        MapRecorderEndpoints(app, "/api/control", localOnly: false, "Touchscreen");
    }

    private void MapRecorderEndpoints(WebApplication app, string prefix, bool localOnly, string source)
    {
        static bool Local(HttpContext context) => context.Connection.RemoteIpAddress is { } ip &&
            IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);
        app.MapGet(prefix + "/status", async (HttpContext context) =>
        {
            if (localOnly && !Local(context)) return Results.StatusCode(403);
            if (CompanionStatusHandler is null) return Results.Json(new { error = "Companion requires an updated desktop ADT." }, statusCode: 503);
            return Results.Json(await CompanionStatusHandler(context.RequestAborted));
        });
        app.MapPost(prefix + "/recording", async (HttpContext context) =>
        {
            if (localOnly && !Local(context)) return Results.StatusCode(403);
            if (!context.Request.HasJsonContentType()) return Results.Json(new { error = "Recording commands require JSON." }, statusCode: 415);
            CompanionCommand? request;
            try { request = await context.Request.ReadFromJsonAsync<CompanionCommand>(context.RequestAborted); }
            catch (Exception ex) when (ex is JsonException or BadHttpRequestException or IOException)
            { return Results.BadRequest(new { error = "Invalid recording command." }); }
            if (request is null || request.Action is not ("start" or "stop" or "save") ||
                request.WindowId?.Length != 32 || string.IsNullOrWhiteSpace(request.SessionId) || request.SessionId.Length > 128 || request.ControlVersion?.Length != 64)
                return Results.BadRequest(new { error = "Invalid recording command." });
            if (CompanionCommandHandler is null) return Results.Json(new { error = "Desktop recorder is unavailable." }, statusCode: 503);
            if (!await _companionCommandGate.WaitAsync(0, context.RequestAborted))
                return Results.Json(new { error = "A recording command is already running. Refresh status before trying again." }, statusCode: 409);
            try
            {
                // Recheck pairing after admission; queued old credentials cannot control a new run.
                if (!TokenMatches(GetSuppliedToken(context))) return Results.StatusCode(401);
                var response = await CompanionCommandHandler(request, context.RequestAborted);
                if (response.Ok) SetActivity(source + ": " + request.Action + " recording.");
                return Results.Json(response, statusCode: response.Ok ? 200 : 409);
            }
            finally { _companionCommandGate.Release(); }
        });
    }
}
