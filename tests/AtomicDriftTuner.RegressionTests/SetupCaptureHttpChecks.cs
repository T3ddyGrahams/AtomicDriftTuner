using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class SetupCaptureHttpChecks
{
    internal static void Run(Action<string, Action> test) => test(
        "setup capture HTTP authenticates, bounds JSON and shares recorder admission",
        () => { try { CheckHttp().GetAwaiter().GetResult(); } catch (Exception ex) { throw new Exception(ex.ToString()); } });

    private static async Task CheckHttp()
    {
        using var hub = new TelemetryHubService();
        await using var server = new RemoteServerService(hub);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        await server.StartAsync(port);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        const string route = "/api/companion/setup";
        var nonce = new string('a', 64);
        var windowId = Guid.NewGuid().ToString("N");
        // Transport tests use a fake handler. INI provenance/content validation belongs to
        // the capture service tests; an ignored property exercises body-size limits alone.
        var body = JsonSerializer.Serialize(new { nonce, windowId });
        var calls = 0;
        Check((await Post(body)).StatusCode == HttpStatusCode.Unauthorized, "Unpaired setup capture admitted");
        var pair = await http.PostAsJsonAsync("/api/pair", new { code = server.PairingCode });
        using var pairing = JsonDocument.Parse(await pair.Content.ReadAsStringAsync());
        http.DefaultRequestHeaders.Add("X-ADT-Token", pairing.RootElement.GetProperty("token").GetString());
        Check((await Post(body)).StatusCode == HttpStatusCode.ServiceUnavailable, "Missing capture handler was not reported");
        server.CompanionSetupHandler = (request, _) =>
        {
            Interlocked.Increment(ref calls);
            Check(request.Nonce == nonce && request.WindowId == windowId, "Capture challenge changed in transport");
            return Task.FromResult(new RemoteActionResponse { Ok = true, Message = "Fixture capture accepted", RefreshStatus = true });
        };
        Check((await http.PostAsync(route, new StringContent(body))).StatusCode == HttpStatusCode.UnsupportedMediaType,
            "Non-JSON capture body accepted");
        foreach (var malformed in new[] { "{", "null", "[]", "{}",
            JsonSerializer.Serialize(new { nonce = new string('z', 64), windowId }),
            JsonSerializer.Serialize(new { nonce = new string('a', 63), windowId }),
            JsonSerializer.Serialize(new { nonce, windowId = Guid.NewGuid().ToString("D") }),
            JsonSerializer.Serialize(new { nonce = 42, windowId }),
            JsonSerializer.Serialize(new { nonce, windowId = (string?)null }) })
            Check((await Post(malformed)).StatusCode == HttpStatusCode.BadRequest, "Malformed setup capture reached the handler");
        Check(calls == 0, "Invalid capture requests had side effects");
        Check((await http.PostAsync("/api/control/setup", Json(body))).StatusCode == HttpStatusCode.NotFound,
            "Touchscreen route unexpectedly allowed setup injection");
        Check((await http.GetAsync(route)).StatusCode == HttpStatusCode.MethodNotAllowed, "Setup injection accepted GET");

        var large = JsonSerializer.Serialize(new { nonce, windowId, filler = new string('x', 24_000) });
        var largeResponse = await Post(large);
        Check(largeResponse.StatusCode == HttpStatusCode.OK && calls == 1,
            "Valid setup transport above the ordinary 16 KiB limit was rejected");
        using (var accepted = JsonDocument.Parse(await largeResponse.Content.ReadAsStringAsync()))
            Check(accepted.RootElement.GetProperty("refreshStatus").GetBoolean(), "Capture did not signal a changed authoritative control version");
        var limitBody = body + new string(' ', 400_000 - Encoding.UTF8.GetByteCount(body));
        Check((await Post(limitBody)).StatusCode == HttpStatusCode.OK && calls == 2, "400,000-byte setup boundary was rejected");
        Check((await Post(limitBody + " ")).StatusCode == HttpStatusCode.RequestEntityTooLarge && calls == 2,
            "Known-length setup body above the limit was accepted");
        using (var chunked = new HttpRequestMessage(HttpMethod.Post, route) { Content = new ChunkedJsonContent(limitBody + " ") })
        {
            chunked.Headers.TransferEncodingChunked = true;
            try
            {
                Check((await http.SendAsync(chunked)).StatusCode == HttpStatusCode.RequestEntityTooLarge,
                    "Chunked setup body bypassed the size limit");
            }
            catch (HttpRequestException ex) when (ex.InnerException is IOException { InnerException: SocketException socket } &&
                socket.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
            {
                // Kestrel can close the oversized chunked upload before Windows finishes
                // sending it. Connection rejection is also valid, but dispatch is forbidden.
            }
            Check(calls == 2, "Oversized chunked body reached the setup handler");
        }
        var workflow = JsonSerializer.Serialize(new { action = "prepare", controlVersion = new string('b', 64), notes = new string('n', 24_000) });
        Check((await http.PostAsync("/api/companion/workflow", Json(workflow))).StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            "Setup capture raised the size limit on unrelated endpoints");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.CompanionSetupHandler = async (_, cancellation) =>
        {
            Interlocked.Increment(ref calls); entered.TrySetResult(); await finish.Task.WaitAsync(cancellation);
            return new() { Ok = true, Message = "Fixture capture completed" };
        };
        server.CompanionCommandHandler = (_, _) => throw new Exception("Recording bypassed a pending setup capture");
        server.CompanionWorkflowCommandHandler = (_, _) => throw new Exception("Workflow bypassed a pending setup capture");
        var pending = Post(body);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check((await Post(body)).StatusCode == HttpStatusCode.Conflict, "Overlapping setup capture admitted");
            var record = new CompanionCommand { Action = "start", WindowId = windowId, SessionId = "fixture", ControlVersion = new string('c', 64) };
            foreach (var prefix in new[] { "/api/companion", "/api/control" })
            {
                Check((await http.PostAsJsonAsync(prefix + "/recording", record)).StatusCode == HttpStatusCode.Conflict,
                    "Recording started while setup capture was pending");
                Check((await http.PostAsJsonAsync(prefix + "/workflow", new CompanionWorkflowCommand { Action = "prepare", ControlVersion = new string('c', 64) })).StatusCode == HttpStatusCode.Conflict,
                    "Workflow mutated its plan while setup capture was pending");
            }
        }
        finally { finish.TrySetResult(); }
        Check((await pending).StatusCode == HttpStatusCode.OK && calls == 3, "Pending capture dispatched more than once");
        Check(!server.RemoteWritesEnabled, "Setup capture enabled hardware writes");
        server.CompanionSetupHandler = (_, _) => Task.FromResult(new RemoteActionResponse { Ok = false, Message = "Stale capture challenge" });
        Check((await Post(body)).StatusCode == HttpStatusCode.Conflict, "Rejected desktop capture returned success");
        server.RegeneratePairing();
        Check((await Post(body)).StatusCode == HttpStatusCode.Unauthorized && calls == 3, "Revoked pairing admitted setup capture");
        await server.StopAsync();

        Task<HttpResponseMessage> Post(string content) => http.PostAsync(route, Json(content));
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }

    private sealed class ChunkedJsonContent : HttpContent
    {
        private readonly byte[] _bytes;
        public ChunkedJsonContent(string value)
        {
            _bytes = Encoding.UTF8.GetBytes(value);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_bytes).AsTask();
    }
}
