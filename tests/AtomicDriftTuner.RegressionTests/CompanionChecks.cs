using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class CompanionChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("companion rejects stale sessions, edited plans and unavailable actions", () =>
        {
            var state = new CompanionRecorderState { WindowId = new string('a', 32), SessionId = "run-a", ControlVersion = new string('b', 64), CanStart = true, Message = "Save first" };
            var command = new CompanionCommand { Action = "start", WindowId = state.WindowId, SessionId = state.SessionId, ControlVersion = state.ControlVersion };
            Check(CompanionCommandRules.Reject(command, state) is null, "valid start rejected");
            command.Action = "save"; Check(CompanionCommandRules.Reject(command, state) is not null, "save while unready");
            command.Action = "apply"; Check(CompanionCommandRules.Reject(command, state) is not null, "unexpected action");
            command.Action = "start"; state.SessionId = "run-b"; Check(CompanionCommandRules.Reject(command, state) is not null, "stale session");
            state.SessionId = "run-a"; state.ControlVersion = new string('c', 64); Check(CompanionCommandRules.Reject(command, state) is not null, "edited plan");
            state.ControlVersion = command.ControlVersion; state.WindowId = new string('d', 32); Check(CompanionCommandRules.Reject(command, state) is not null, "replaced window");
        });
        run("companion HTTP requires pairing, validates commands and rejects overlap", () => CheckHttp().GetAwaiter().GetResult());
    }

    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }

    private static async Task CheckHttp()
    {
        using var hub = new TelemetryHubService();
        await using var server = new RemoteServerService(hub);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.CompanionStatusHandler = _ => Task.FromResult(new CompanionStatus { NextStep = "Record baseline" });
        server.CompanionCommandHandler = async (_, ct) => { Interlocked.Increment(ref calls); entered.TrySetResult(); await finish.Task.WaitAsync(ct); return new() { Ok = true, Message = "Recorded" }; };
        await server.StartAsync(port);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        Check((await http.GetAsync("/api/companion/status")).StatusCode == HttpStatusCode.Unauthorized, "unauthenticated status");
        var command = new CompanionCommand { Action = "start", WindowId = new string('a', 32), SessionId = "s1", ControlVersion = new string('b', 64) };
        Check((await http.PostAsJsonAsync("/api/companion/recording", command)).StatusCode == HttpStatusCode.Unauthorized, "unauthenticated recording");
        var pair = await http.PostAsJsonAsync("/api/pair", new { code = server.PairingCode });
        var json = JsonDocument.Parse(await pair.Content.ReadAsStringAsync());
        http.DefaultRequestHeaders.Add("X-ADT-Token", json.RootElement.GetProperty("token").GetString());
        Check((await http.GetFromJsonAsync<CompanionStatus>("/api/companion/status"))?.NextStep == "Record baseline", "next-step payload");
        Check(!server.RemoteWritesEnabled, "recording enabled hardware writes");
        Check((await http.PostAsync("/api/companion/recording", new StringContent("{", Encoding.UTF8, "application/json"))).StatusCode == HttpStatusCode.BadRequest, "bad JSON");
        Check((await http.PostAsync("/api/companion/recording", new StringContent("plain text"))).StatusCode == HttpStatusCode.UnsupportedMediaType, "non-JSON body");
        Check((await http.PostAsJsonAsync("/api/companion/recording", new { action = "apply" })).StatusCode == HttpStatusCode.BadRequest, "unknown action");
        Check(calls == 0, "invalid requests reached recorder");
        var first = http.PostAsJsonAsync("/api/companion/recording", command);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { Check((await http.PostAsJsonAsync("/api/companion/recording", command)).StatusCode == HttpStatusCode.Conflict, "overlapping mutation admitted"); }
        catch { finish.TrySetResult(); throw; }
        try { Check((await http.PostAsJsonAsync("/api/control/recording", command)).StatusCode == HttpStatusCode.Conflict, "touchscreen bypassed the companion's recording gate"); }
        finally { finish.TrySetResult(); }
        Check((await first).IsSuccessStatusCode && calls == 1, "recording dispatched more than once");
        server.RegeneratePairing();
        Check((await http.GetAsync("/api/companion/status")).StatusCode == HttpStatusCode.Unauthorized, "revoked pairing retained access");
        Check((await http.PostAsJsonAsync("/api/companion/recording", command)).StatusCode == HttpStatusCode.Unauthorized && calls == 1, "revoked token mutated recorder");
        await server.StopAsync();
    }
}
