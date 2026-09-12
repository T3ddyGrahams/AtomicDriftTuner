using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class TouchscreenChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run(Action<string, Action> run, string root)
    {
        run("touchscreen HTTP route serves touch controls and preserves paired recorder guards", () => Http().GetAwaiter().GetResult());
        run("SimHub dashboard uses the selected loopback port and remains visible in pits", () =>
        {
            using var d = JsonDocument.Parse(TouchscreenDashboardService.Render(5191));
            var screen = d.RootElement.GetProperty("Screens")[0];
            var item = screen.GetProperty("Items")[0];
            Check(item.GetProperty("StartAddress").GetString() == "http://127.0.0.1:5191/dash", "Dashboard used a stale LAN address or port");
            Check(screen.GetProperty("InGameScreen").GetBoolean() && screen.GetProperty("IdleScreen").GetBoolean() && screen.GetProperty("PitScreen").GetBoolean(), "Dashboard disappears when needed");
            Check(!item.GetProperty("ClickThrough").GetBoolean() && item.GetProperty("$type").GetString()!.Contains("WebPageItem"), "Dashboard does not receive touch input");
            Check(d.RootElement.GetProperty("Metadata").GetProperty("PitScreensIndexs")[0].GetInt32() == 0, "Pit metadata disagrees with the screen");
            try { TouchscreenDashboardService.Render(0); throw new Exception("Invalid port accepted"); } catch (ArgumentOutOfRangeException) { }
        });
        run("dashboard installation preserves existing dashboards and backs up changed generated files", () =>
        {
            var folder = Path.Combine(root, "simhub-touch"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SimHub.Plugins.dll"), "isolated path fixture");
            var original = Path.Combine(folder, "DashTemplates", "ADT", "ADT.djson"); Directory.CreateDirectory(Path.GetDirectoryName(original)!); File.WriteAllText(original, "Keep my dashboard");
            var installed = TouchscreenDashboardService.Install(folder, 5190);
            var initial = File.ReadAllText(installed);
            TouchscreenDashboardService.Install(folder, 5191);
            Check(File.ReadAllText(original) == "Keep my dashboard", "An existing user dashboard was overwritten");
            var backups = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(installed)!, "_Backups"));
            Check(backups.Any(p => File.ReadAllText(p) == initial), "Previous generated dashboard was not backed up");
            Check(File.ReadAllText(installed).Contains("127.0.0.1:5191/dash") && File.Exists(installed + ".metadata"), "Install did not update dashboard and metadata");
            var count = backups.Length; TouchscreenDashboardService.Install(folder, 5191);
            Check(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(installed)!, "_Backups")).Length == count, "Unchanged installation created unnecessary backups");
            try { TouchscreenDashboardService.Install(Path.Combine(root, "not-simhub"), 5190); throw new Exception("Unknown destination accepted"); } catch (InvalidOperationException) { }
        });
    }

    private static async Task Http()
    {
        using var hub = new TelemetryHubService();
        await using var server = new RemoteServerService(hub);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var calls = 0;
        var state = new CompanionRecorderState { WindowId = new string('a', 32), SessionId = "same-run", ControlVersion = new string('b', 64), CanStart = true, Message = "Not ready" };
        server.CompanionStatusHandler = _ => Task.FromResult(new CompanionStatus { NextStep = "Record baseline", Details = "Full explanation", Completion = "Ready when saved", Recorder = state });
        server.CompanionCommandHandler = (request, _) =>
        {
            var reject = CompanionCommandRules.Reject(request, state);
            if (reject is not null) return Task.FromResult(new RemoteActionResponse { Message = reject });
            calls++; state.CanStart = false; state.CanStop = true;
            return Task.FromResult(new RemoteActionResponse { Ok = true, Message = "Started" });
        };
        await server.StartAsync(port);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(6) };
        foreach (var route in new[] { "/dash", "/dash/" })
        {
            var page = await http.GetAsync(route);
            Check(page.IsSuccessStatusCode, "Missing or ambiguous touchscreen route: " + route);
            var html = await page.Content.ReadAsStringAsync();
            Check(html.Contains("class=\"touchscreen\"") && html.Contains("id=\"recordStart\"") && html.Contains("function renderSettings()") && !html.Contains("/* ADT_TOUCH_SCRIPT */"), "Touchscreen page is incomplete");
        }
        Check((await http.GetAsync("/api/control/status")).StatusCode == HttpStatusCode.Unauthorized, "Unpaired browser read recorder status");
        var command = new CompanionCommand { Action = "start", WindowId = state.WindowId, SessionId = state.SessionId, ControlVersion = state.ControlVersion };
        Check((await http.PostAsJsonAsync("/api/control/recording", command)).StatusCode == HttpStatusCode.Unauthorized, "Unpaired browser controlled the recorder");
        using var pair = await http.PostAsJsonAsync("/api/pair", new { code = server.PairingCode });
        using var json = JsonDocument.Parse(await pair.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("token").GetString(); http.DefaultRequestHeaders.Add("X-ADT-Token", token);
        Check((await http.GetFromJsonAsync<CompanionStatus>("/api/control/status"))?.Details == "Full explanation", "Touchscreen lost detailed guidance");
        Check((await http.PostAsync("/api/control/recording", new StringContent("not JSON"))).StatusCode == HttpStatusCode.UnsupportedMediaType, "Non-JSON command admitted");
        Check((await http.PostAsync("/api/control/recording", new StringContent("{", Encoding.UTF8, "application/json"))).StatusCode == HttpStatusCode.BadRequest, "Broken JSON command admitted");
        Check((await http.PostAsJsonAsync("/api/control/recording", new { action = "apply" })).StatusCode == HttpStatusCode.BadRequest, "Touchscreen recorder API admitted hardware command");
        command.ControlVersion = new string('c', 64);
        Check((await http.PostAsJsonAsync("/api/control/recording", command)).StatusCode == HttpStatusCode.Conflict && calls == 0, "Stale touchscreen controlled a changed plan");
        command.ControlVersion = state.ControlVersion;
        Check((await http.PostAsJsonAsync("/api/control/recording", command)).IsSuccessStatusCode && calls == 1, "Touchscreen did not dispatch to the recorder");
        Check((await http.PostAsJsonAsync("/api/control/recording", command)).StatusCode == HttpStatusCode.Conflict && calls == 1, "Duplicate start dispatched twice");
        Check(!server.RemoteWritesEnabled, "Recording enabled wheelbase writes");
        var privateIp = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).FirstOrDefault(ip =>
            {
                var b = ip.GetAddressBytes(); return b.Length == 4 && (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31);
            });
        if (privateIp is not null)
        {
            using var lan = new HttpClient { BaseAddress = new Uri($"http://{privateIp}:{port}"), Timeout = TimeSpan.FromSeconds(6) };
            lan.DefaultRequestHeaders.Add("X-ADT-Token", token);
            Check((await lan.GetAsync("/api/control/status")).IsSuccessStatusCode, "Paired private-network touchscreen was rejected");
            Check((await lan.GetAsync("/api/companion/status")).StatusCode == HttpStatusCode.Forbidden, "In-game-only API leaked onto the LAN");
            Console.WriteLine("PASS private-network touch transport; in-game transport remains loopback-only");
        }
        server.RegeneratePairing();
        Check((await http.PostAsJsonAsync("/api/control/recording", command)).StatusCode == HttpStatusCode.Unauthorized && calls == 1, "Revoked browser controlled the recorder");
        await server.StopAsync();
    }
}
