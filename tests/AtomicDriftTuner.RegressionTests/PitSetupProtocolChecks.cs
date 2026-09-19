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

internal static class PitSetupProtocolChecks
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("pit coordinator admits one explicit apply and blocks recording until completion", () =>
        {
            var plan = Plan(root); var coordinator = Staged(plan);
            var first = Prepare(coordinator, plan);
            var admission = coordinator.Execute(first, "context", "test_car", false);
            Check(admission.Ok && admission.Plan == plan && admission.CommandId == first.CommandId &&
                Guid.TryParseExact(admission.LeaseId, "N", out _), "Valid explicit apply was not admitted");
            Check(coordinator.Busy && coordinator.RecordingBlockReason is not null &&
                !coordinator.Status("context", "test_car", false).CanApply, "Pending operation did not block recording/apply");
            Check(!coordinator.Execute(first, "context", "test_car", false).Ok, "Duplicate prepare returned a second admission");
            Check(!coordinator.Execute(Prepare(coordinator, plan), "context", "test_car", false).Ok, "New command bypassed pending apply");
            var refused = false;
            try { coordinator.Stage(Plan(root), "context"); } catch (InvalidOperationException) { refused = true; }
            Check(refused && coordinator.Busy, "Staging erased unresolved application state");
            Check(coordinator.Execute(Complete(first, admission), "context", "test_car", false).Ok, "Matching completion refused");
            Check(!coordinator.Busy && coordinator.RecordingBlockReason is null &&
                !coordinator.Status("context", "test_car", false).CanApply, "Completed plan became applicable twice");
            Check(!coordinator.Execute(Prepare(coordinator, plan), "context", "test_car", false).Ok, "Consumed plan admitted twice");
        });

        test("pit coordinator rejects stale context car control version and recording", () =>
        {
            var plan = Plan(root); var coordinator = Staged(plan);
            var prepared = Prepare(coordinator, plan);
            Check(!coordinator.Execute(prepared, "changed-goal", "test_car", false).Ok &&
                coordinator.Status("changed-goal", "test_car", false).Plan is null, "Changed workflow context admitted old plan");
            Check(!coordinator.Execute(prepared, "context", "other_car", false).Ok &&
                coordinator.Status("context", "other_car", false).Plan is null, "Different car admitted old plan");
            Check(!coordinator.Execute(prepared, "context", "test_car", true).Ok, "Recording permitted application");
            var currentRecording = Prepare(coordinator, plan, recording: true);
            Check(!coordinator.Execute(currentRecording, "context", "test_car", true).Ok, "Fresh recording version permitted application");
            coordinator.Stage(Plan(root), "context");
            Check(!coordinator.Execute(prepared, "context", "test_car", false).Ok && !coordinator.Busy, "Restaging failed to invalidate an old command");
        });

        test("pit coordinator rejects mismatched or inconsistent completion without losing its lease", () =>
        {
            foreach (var mutate in new Action<PitSetupCommand>[]
            {
                c => c.LeaseId = Guid.NewGuid().ToString("N"),
                c => c.PlanId = Guid.NewGuid().ToString("N"),
                c => c.CommandId = "another_command",
                c => c.Operation = "restore",
                c => c.State = "restored",
                c => c.Success = false,
                c => { c.Success = true; c.State = "failed"; },
                c => { c.Success = false; c.State = "unknown"; }
            })
            {
                var plan = Plan(root); var coordinator = Staged(plan); var prepared = Prepare(coordinator, plan);
                var admission = coordinator.Execute(prepared, "context", "test_car", false);
                var completion = Complete(prepared, admission); mutate(completion);
                Check(!coordinator.Execute(completion, "context", "test_car", false).Ok && coordinator.Busy &&
                    coordinator.RecordingBlockReason is not null, "Invalid completion released an unresolved lease");
                Check(coordinator.Execute(Complete(prepared, admission), "changed-context", "other_car", true).Ok && !coordinator.Busy,
                    "A matching result could not be acknowledged after context changed");
            }
        });

        test("pit completion is idempotent and an old receipt cannot release a newer operation", () =>
        {
            var plan = Plan(root); var coordinator = Staged(plan); var prepared = Prepare(coordinator, plan);
            var admission = coordinator.Execute(prepared, "context", "test_car", false);
            var completion = Complete(prepared, admission);
            var receipt = coordinator.Execute(completion, "context", "test_car", false);
            var version = coordinator.Status("context", "test_car", false).ControlVersion;
            var duplicate = coordinator.Execute(completion, "context", "test_car", false);
            Check(receipt.Ok && duplicate.Ok && receipt.LeaseId == duplicate.LeaseId &&
                coordinator.Status("context", "test_car", false).ControlVersion == version, "Completion retry repeated its state change");
            var restore = Prepare(coordinator, plan, operation: "restore");
            var restoreAdmission = coordinator.Execute(restore, "context", "test_car", false);
            Check(restoreAdmission.Ok, "Authorized restore refused");
            Check(coordinator.Execute(completion, "context", "test_car", false).Ok && coordinator.Busy,
                "An old successful receipt released the pending restore");
            var result = Complete(restore, restoreAdmission); result.State = "restored";
            Check(coordinator.Execute(result, "context", "test_car", false).Ok && !coordinator.Busy, "Restore completion refused");
        });

        test("pit restore requires an admitted plan for the same car and unique command", () =>
        {
            var plan = Plan(root); var coordinator = Staged(plan);
            Check(!coordinator.Execute(Prepare(coordinator, plan, operation: "restore"), "context", "test_car", false).Ok,
                "An unapplied plan authorized restoration");
            var prepared = Prepare(coordinator, plan); var admission = coordinator.Execute(prepared, "context", "test_car", false);
            var failed = Complete(prepared, admission); failed.Success = false; failed.State = "verification-failed";
            Check(coordinator.Execute(failed, "context", "test_car", false).Ok && !coordinator.Busy,
                "Explicit failed verification could not complete its operation");
            Check(!coordinator.Execute(Prepare(coordinator, plan, carId: "other_car", operation: "restore"), "context", "other_car", false).Ok,
                "Another car authorized the saved restore target");
            var duplicate = Prepare(coordinator, plan, operation: "restore"); duplicate.CommandId = prepared.CommandId;
            Check(!coordinator.Execute(duplicate, "context", "test_car", false).Ok, "An admitted command ID was reused for restore");
            Check(coordinator.Execute(Prepare(coordinator, plan, operation: "restore"), "context", "test_car", false).Ok,
                "A failed application could not restore its authorized backup");
        });

        test("pit recovery clears pending and restore authorization without reviving an old plan", () =>
        {
            var plan = Plan(root); var coordinator = Staged(plan); var prepared = Prepare(coordinator, plan);
            var admission = coordinator.Execute(prepared, "context", "test_car", false);
            for (var i = 0; i < 5; i++)
                Check(coordinator.Status("other-context", "other_car", false).Busy && coordinator.RecordingBlockReason is not null,
                    "Unacknowledged operation expired through a status change");
            coordinator.ClearAfterGameExit();
            var status = coordinator.Status("context", "test_car", false);
            Check(!status.Busy && !status.CanApply && status.Plan is null && coordinator.RecordingBlockReason is null, "Recovery retained pending plan state");
            Check(!coordinator.Execute(Complete(prepared, admission), "context", "test_car", false).Ok &&
                !coordinator.Execute(Prepare(coordinator, plan, operation: "restore"), "context", "test_car", false).Ok,
                "Recovery retained old lease or restore authority");
            var fresh = Plan(root); coordinator.Stage(fresh, "context");
            Check(coordinator.Execute(Prepare(coordinator, fresh), "context", "test_car", false).Ok, "Recovery prevented a fresh staged plan");
        });

        test("pit HTTP authenticates validates bounds serializes and shares recorder admission", () =>
            CheckHttp(Plan(root)).GetAwaiter().GetResult());
    }

    private static async Task CheckHttp(PitSetupPlan plan)
    {
        using var hub = new TelemetryHubService();
        await using var server = new RemoteServerService(hub);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        await server.StartAsync(port);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        const string route = "/api/companion/pit-setup";
        var coordinator = Staged(plan); var command = Prepare(coordinator, plan);
        var body = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var calls = 0;
        Check((await Post(body)).StatusCode == HttpStatusCode.Unauthorized, "Unpaired pit command admitted");
        var pair = await http.PostAsJsonAsync("/api/pair", new { code = server.PairingCode });
        using var pairing = JsonDocument.Parse(await pair.Content.ReadAsStringAsync());
        http.DefaultRequestHeaders.Add("X-ADT-Token", pairing.RootElement.GetProperty("token").GetString());
        Check((await Post(body)).StatusCode == HttpStatusCode.ServiceUnavailable, "Missing pit handler not reported");
        server.PitSetupHandler = (request, _) =>
        {
            Interlocked.Increment(ref calls);
            Check(request.PlanId == plan.PlanId && request.CommandId == command.CommandId, "Pit command identity changed in transport");
            return Task.FromResult(new PitSetupResponse { Ok = true, Plan = plan, CommandId = request.CommandId, LeaseId = new string('b', 32) });
        };
        server.CompanionStatusHandler = _ => Task.FromResult(new CompanionStatus { PitSetup = coordinator.Status("context", "test_car", false) });
        using (var status = JsonDocument.Parse(await http.GetStringAsync("/api/companion/status")))
            Check(status.RootElement.GetProperty("pitSetup").GetProperty("plan").GetProperty("baselineValues").GetProperty("PRESSURE_LF").GetDouble() == 24,
                "Status serialization changed raw INI section names");
        Check((await http.PostAsync(route, new StringContent(body))).StatusCode == HttpStatusCode.UnsupportedMediaType, "Non-JSON pit command admitted");
        foreach (var malformed in new[] { "{", "null", "[]", "{}", body.Replace("\"protocolVersion\":1", "\"protocolVersion\":2", StringComparison.Ordinal),
            body.Replace("\"prepare\"", "\"execute\"", StringComparison.Ordinal), body.Replace("\"apply\"", "\"write-file\"", StringComparison.Ordinal),
            body.Replace(plan.PlanId, "../setup.ini", StringComparison.Ordinal), body.Replace(command.CommandId, "bad/command", StringComparison.Ordinal),
            body.Replace(command.ControlVersion, "bad-version", StringComparison.Ordinal),
            body.Replace("\"message\":\"\"", "\"message\":null", StringComparison.Ordinal),
            body.Replace("\"success\":false", "\"success\":\"yes\"", StringComparison.Ordinal) })
            Check((await Post(malformed)).StatusCode == HttpStatusCode.BadRequest, "Malformed pit command reached the handler");
        Check(calls == 0, "Invalid HTTP input had side effects");
        Check((await http.PostAsync("/api/control/pit-setup", Json(body))).StatusCode == HttpStatusCode.NotFound,
            "Touchscreen exposed setup application");
        Check((await http.GetAsync(route)).StatusCode == HttpStatusCode.MethodNotAllowed, "GET could apply a setup");
        var maximum = body + new string(' ', 16 * 1024 - Encoding.UTF8.GetByteCount(body));
        var response = await Post(maximum);
        Check(response.StatusCode == HttpStatusCode.OK && calls == 1, "Valid 16 KiB command boundary refused");
        using (var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            Check(accepted.RootElement.GetProperty("plan").GetProperty("baselineValues").GetProperty("PRESSURE_LF").GetDouble() == 24,
                "Prepare response serialization changed raw INI section names");
        Check((await Post(maximum + " ")).StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            "Oversized pit command admitted");
        using (var chunked = new HttpRequestMessage(HttpMethod.Post, route) { Content = new ChunkedJsonContent(maximum + " ") })
        {
            chunked.Headers.TransferEncodingChunked = true;
            try
            {
                Check((await http.SendAsync(chunked)).StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
                    "Chunked pit command bypassed body limit");
            }
            catch (HttpRequestException ex) when (ex.InnerException is IOException { InnerException: SocketException socket } &&
                socket.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted) { }
        }
        Check(calls == 1, "Oversized commands reached the handler");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.PitSetupHandler = async (_, cancellation) =>
        {
            Interlocked.Increment(ref calls); entered.TrySetResult(); await finish.Task.WaitAsync(cancellation);
            return new() { Ok = true };
        };
        server.CompanionCommandHandler = (_, _) => throw new Exception("Recording bypassed pending pit operation");
        server.CompanionWorkflowCommandHandler = (_, _) => throw new Exception("Workflow bypassed pending pit operation");
        server.CompanionSetupHandler = (_, _) => throw new Exception("Setup capture bypassed pending pit operation");
        var pending = Post(body);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check((await Post(body)).StatusCode == HttpStatusCode.Conflict, "Concurrent pit request admitted");
            foreach (var prefix in new[] { "/api/companion", "/api/control" })
            {
                var record = new CompanionCommand { Action = "start", WindowId = new string('a', 32), SessionId = "test", ControlVersion = new string('b', 64) };
                Check((await http.PostAsJsonAsync(prefix + "/recording", record)).StatusCode == HttpStatusCode.Conflict, "Recording bypassed shared pit admission");
                Check((await http.PostAsJsonAsync(prefix + "/workflow", new CompanionWorkflowCommand { Action = "prepare", ControlVersion = new string('b', 64) })).StatusCode == HttpStatusCode.Conflict,
                    "Workflow bypassed shared pit admission");
            }
            Check((await http.PostAsJsonAsync("/api/companion/setup", new { nonce = new string('a', 64), windowId = new string('b', 32) })).StatusCode == HttpStatusCode.Conflict,
                "Live capture bypassed shared pit admission");
        }
        finally { finish.TrySetResult(); }
        Check((await pending).StatusCode == HttpStatusCode.OK && calls == 2, "Pending command dispatched multiple times");

        var recorderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorderFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.CompanionCommandHandler = async (_, cancellation) =>
        {
            recorderEntered.TrySetResult(); await recorderFinish.Task.WaitAsync(cancellation);
            return new() { Ok = true };
        };
        var recording = http.PostAsJsonAsync("/api/control/recording", new CompanionCommand
        { Action = "start", WindowId = new string('a', 32), SessionId = "test", ControlVersion = new string('b', 64) });
        try
        {
            await recorderEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check((await Post(body)).StatusCode == HttpStatusCode.Conflict && calls == 2, "Pit admission bypassed pending recording");
        }
        finally { recorderFinish.TrySetResult(); }
        Check((await recording).StatusCode == HttpStatusCode.OK, "Recorder fixture did not finish");
        server.PitSetupHandler = (_, _) => Task.FromResult(new PitSetupResponse { Ok = false, Message = "Stale plan" });
        Check((await Post(body)).StatusCode == HttpStatusCode.Conflict, "Desktop rejection became HTTP success");
        server.RegeneratePairing();
        Check((await Post(body)).StatusCode == HttpStatusCode.Unauthorized && calls == 2 && !server.RemoteWritesEnabled,
            "Revoked pairing admitted pit action or hardware writes were enabled");
        await server.StopAsync();
        Task<HttpResponseMessage> Post(string content) => http.PostAsync(route, Json(content));
    }

    private static PitSetupPlan Plan(string root)
    {
        var path = Path.Combine(root, "pit-protocol-" + Guid.NewGuid().ToString("N") + ".ini");
        File.WriteAllText(path, "[CAR]\nMODEL=test_car\n[PRESSURE_LF]\nVALUE=24\n");
        var analysis = new AssettoCorsaSetupService().LoadBaseline(path, new CarProfile { SourceFolderName = "test_car" });
        analysis.Parameters[0].RecommendedValue = 25;
        analysis.Parameters[0].Range = new() { Section = "PRESSURE_LF", Min = 20, Max = 40, Step = 1 };
        return new PitSetupPlanService().Create(analysis, "test_car", "Protocol fixture");
    }

    private static PitSetupCoordinator Staged(PitSetupPlan plan)
    {
        var coordinator = new PitSetupCoordinator(); coordinator.Stage(plan, "context"); return coordinator;
    }

    private static PitSetupCommand Prepare(PitSetupCoordinator coordinator, PitSetupPlan plan, string carId = "test_car", bool recording = false, string operation = "apply") => new()
    {
        ProtocolVersion = 1, Action = "prepare", Operation = operation, PlanId = plan.PlanId,
        CommandId = Guid.NewGuid().ToString("N"), ControlVersion = coordinator.Status("context", carId, recording).ControlVersion
    };

    private static PitSetupCommand Complete(PitSetupCommand prepared, PitSetupResponse admitted) => new()
    {
        ProtocolVersion = 1, Action = "complete", Operation = prepared.Operation, PlanId = prepared.PlanId,
        CommandId = prepared.CommandId, ControlVersion = prepared.ControlVersion, LeaseId = admitted.LeaseId, Success = true, State = "applied"
    };

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class ChunkedJsonContent : HttpContent
    {
        private readonly byte[] _bytes;
        public ChunkedJsonContent(string value)
        {
            _bytes = Encoding.UTF8.GetBytes(value); Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_bytes).AsTask();
    }
}
