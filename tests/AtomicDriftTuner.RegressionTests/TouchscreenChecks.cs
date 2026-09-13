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
            Check(item.GetProperty("StartAddress").GetString() == "http://127.0.0.1:5191/dash/launch", "Dashboard did not use the adaptive launcher and selected port");
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
            Check(File.ReadAllText(installed).Contains("127.0.0.1:5191/dash/launch") && File.Exists(installed + ".metadata"), "Install did not update dashboard and metadata");
            var count = backups.Length; TouchscreenDashboardService.Install(folder, 5191);
            Check(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(installed)!, "_Backups")).Length == count, "Unchanged installation created unnecessary backups");
            try { TouchscreenDashboardService.Install(Path.Combine(root, "not-simhub"), 5190); throw new Exception("Unknown destination accepted"); } catch (InvalidOperationException) { }
        });
        run("adaptive launcher uses the user's appearance palette", () =>
        {
            var theme = new ThemeSettings { AppBackground = "#112233", PrimaryText = "#ABCDEF", Accent = "#FA8700" };
            var html = RemoteWebApp.RenderTouchLauncher(theme);
            Check(html.Contains("--bg:#112233FF;") && html.Contains("--text:#ABCDEFFF;") && html.Contains("--accent:#FA8700FF;"), "Launcher ignored the custom theme");
            Check(!html.Contains("/* ADT_LAUNCH_PALETTE */"), "Launcher did not render its palette");
        });
        run("adaptive dashboard installer carries this PC's LAN address and the current port", () =>
        {
            var folder = Path.Combine(root, "simhub-adaptive"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SimHub.Plugins.dll"), "isolated path fixture");
            var path = TouchscreenDashboardService.Install(folder, 5190, "http://192.168.2.235:5190/");
            using var first = JsonDocument.Parse(File.ReadAllText(path));
            Check(first.RootElement.GetProperty("Screens")[0].GetProperty("Items")[0].GetProperty("StartAddress").GetString() ==
                "http://192.168.2.235:5190/dash/launch", "Separate device got loopback");
            TouchscreenDashboardService.Install(folder, 5192, "http://10.2.0.7:5190/");
            Check(File.ReadAllText(path).Contains("http://10.2.0.7:5192/dash/launch"), "Reinstall retained an old machine address or port");
            foreach (var invalid in new[] { "https://192.168.2.235/", "http://example.com/", "http://8.8.8.8/", "http://192.168.2.235/?token=secret", "http://user:pass@192.168.2.235/", "javascript:alert(1)" })
            {
                try { TouchscreenDashboardService.Render(5190, invalid); throw new Exception("Invalid dashboard destination accepted"); }
                catch (ArgumentException) { }
            }
        });
        run("LAN address selection prefers a routed physical interface over host-only virtual networks", () =>
        {
            var urls = LanAddressService.SelectUrls(new[]
            {
                new LanAddressService.Candidate(IPAddress.Parse("172.18.16.1"), false, true),
                new LanAddressService.Candidate(IPAddress.Parse("192.168.124.1"), false, true),
                new LanAddressService.Candidate(IPAddress.Parse("10.10.0.1"), true, true),
                new LanAddressService.Candidate(IPAddress.Parse("192.168.2.235"), true, false),
                new LanAddressService.Candidate(IPAddress.Parse("192.168.2.235"), true, false),
                new LanAddressService.Candidate(IPAddress.Parse("169.254.2.7"), false, false),
                new LanAddressService.Candidate(IPAddress.Parse("127.0.0.1"), false, false),
                new LanAddressService.Candidate(IPAddress.Parse("8.8.8.8"), true, false)
            }, 5190);
            Check(urls[0] == "http://192.168.2.235:5190/" && urls.Count == 4, "Suggested an unsuitable or duplicate address");
            Check(LanAddressService.SelectUrls(Array.Empty<LanAddressService.Candidate>(), 5190).Single() == "http://localhost:5190/", "Offline PC fallback changed");
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
            Check(page.Headers.GetValues("X-Frame-Options").Single() == "DENY", "Authenticated dashboard lost clickjacking protection");
            Check(html.Contains("id=\"fullscreenButton\"") && html.Contains("function toggleFullscreen()") && !html.Contains("/* ADT_DISPLAY_SCRIPT */"), "Adaptive display controls are missing");
        }
        foreach (var route in new[] { "/dash/launch", "/dash/launch/", "/DASH/LAUNCH" })
        {
            var launch = await http.GetAsync(route);
            var html = await launch.Content.ReadAsStringAsync();
            Check(launch.IsSuccessStatusCode && !launch.Headers.Contains("X-Frame-Options"), "SimHub could not embed the public launcher");
            Check(launch.Headers.GetValues("Content-Security-Policy").Single().Contains("default-src 'none'"), "Launcher security policy missing");
            Check(html.Contains("target=\"_top\"") && !html.Contains("/api/") && !html.Contains("recordStart"), "Launcher must only open the top-level dashboard");
        }
        Check((await http.GetAsync("/dash/launch-extra")).Headers.GetValues("X-Frame-Options").Single() == "DENY", "Framing exception extends beyond the public launcher");
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
