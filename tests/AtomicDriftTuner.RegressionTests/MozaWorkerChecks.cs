using System.Diagnostics;
using System.IO;
using AtomicDriftTuner.Services;

internal static class MozaWorkerChecks
{
    private sealed class Fixture(string mode) : IMozaMotorApi
    {
        private readonly Dictionary<string, int> _values = PitHouseCatalog.Settings.ToDictionary(s => s.Key, s => s.Min);
        public string DeviceName() => "Isolated fixture base";
        public int Read(string key)
        {
            if (mode == "read-exit") Environment.Exit(37);
            if (mode == "hang") Thread.Sleep(60_000);
            if (mode == "read-error") throw new InvalidOperationException("Fixture device unavailable.");
            return _values[key];
        }
        public void Write(string key, int value)
        {
            _values[key] = value;
            if (mode == "write-exit") Environment.Exit(38); // May act before a failed response.
        }
        public void Dispose()
        {
            if (mode == "dispose-exit") Environment.Exit(39);
            if (mode == "dispose-hang") Thread.Sleep(60_000);
        }
    }

    public static int RunFixture(string pipe, string mode) => MozaWorkerApi.RunWorker(pipe, _ =>
    {
        if (mode == "open-exit") Environment.Exit(36);
        if (mode == "open-error") throw new InvalidOperationException("Fixture SDK missing.");
        return new Fixture(mode);
    });

    private static ProcessStartInfo Start(string pipe, string mode)
    {
        var info = new ProcessStartInfo(Path.ChangeExtension(typeof(MozaWorkerChecks).Assembly.Location, ".exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("--moza-fixture"); info.ArgumentList.Add(pipe); info.ArgumentList.Add(mode);
        return info;
    }
    private static MozaWorkerApi Open(string mode) => new("unused-fixture",
        pipe => Start(pipe, mode), TimeSpan.FromSeconds(3));
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Fails(Action action, string text)
    {
        try { action(); } catch (Exception ex) when (ex.Message.Contains(text, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Expected contained failure: " + text);
    }
    public static void Run(Action<string, Action> test, string root)
    {
        test("production SDK helper handles missing folder without desktop startup or vendor loading", () =>
            Fails(() => { using var api = new MozaWorkerApi(Path.Combine(root, "missing-sdk")); }, "MOZA_API_C.dll was not found"));
        test("SDK helper exits cleanly when the desktop closes its pipe", () =>
        {
            var name = "adt-clean-close-" + Guid.NewGuid().ToString("N");
            using var pipe = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
                System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
            using var process = Process.Start(Start(name, "ok"))!;
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                pipe.WaitForConnectionAsync(deadline.Token).GetAwaiter().GetResult();
                using (var writer = new StreamWriter(pipe, new System.Text.UTF8Encoding(false), 1024, true) { AutoFlush = true })
                using (var reader = new StreamReader(pipe, System.Text.Encoding.UTF8, false, 1024, true))
                {
                    writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new MozaWorkerApi.Request("open", "fixture")));
                    var reply = reader.ReadLineAsync(deadline.Token).AsTask().GetAwaiter().GetResult();
                    Check(System.Text.Json.JsonSerializer.Deserialize<MozaWorkerApi.Response>(reply!)!.Success, "Fixture did not open");
                }
                pipe.Dispose();
                Check(process.WaitForExit(3000) && process.ExitCode == 0, "Normal pipe closure produced a helper error");
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        });
        test("SDK process preserves read/write/readback and does not load vendor DLLs", () =>
        {
            using var api = Open("ok");
            Check(api.DeviceName() == "Isolated fixture base", "Device name lost");
            api.Write("FfbStrength", 25);
            Check(api.Read("FfbStrength") == 25, "Write/readback lost");
            Fails(() => api.Write("FfbStrength", 99999), "value");
            Check(api.Read("FfbStrength") == 25, "Invalid write was sent");
        });
        test("SDK initialization exit is contained", () => Fails(() => { using var api = Open("open-exit"); }, "ADT is still running"));
        test("SDK initialization error remains actionable", () => Fails(() => { using var api = Open("open-error"); }, "Fixture SDK missing"));
        test("SDK read process exit aborts the whole reading", () =>
        {
            var service = new PitHouseService(() => Open("read-exit"), Path.Combine(root, "isolated-read"), () => { });
            Fails(() => service.ReadAsync().GetAwaiter().GetResult(), "ADT is still running");
        });
        test("SDK timeout terminates helper and never retries", () =>
        {
            using var api = Open("hang"); var timer = Stopwatch.StartNew();
            Fails(() => api.Read("FfbStrength"), "ADT is still running");
            Check(timer.Elapsed < TimeSpan.FromSeconds(9), "Timed out SDK held the caller indefinitely");
            Fails(() => api.Read("FfbStrength"), "disposed");
        });
        test("SDK ordinary errors preserve the helper for readable controls", () =>
        {
            using var api = Open("read-error");
            Fails(() => api.Read("FfbStrength"), "Fixture device unavailable");
            Check(api.DeviceName() == "Isolated fixture base", "Ordinary SDK error killed connection");
        });
        foreach (var mode in new[] { "dispose-exit", "dispose-hang" })
            test("SDK teardown is contained: " + mode, () =>
            {
                var timer = Stopwatch.StartNew(); using (var api = Open(mode)) { api.Read("FfbStrength"); }
                Check(timer.Elapsed < TimeSpan.FromSeconds(9), "Teardown held desktop indefinitely");
            });
        test("SDK write process exit preserves durable originals and uncertain outcome", () =>
        {
            var backups = Path.Combine(root, "isolated-apply");
            var service = new PitHouseService(() => Open("write-exit"), backups, () => { });
            var reading = service.ReadAsync().GetAwaiter().GetResult();
            var plan = PitHouseService.Plan(reading, [KeyValuePair.Create("FfbStrength", 25)]);
            var result = service.ApplyAsync(plan).GetAwaiter().GetResult();
            Check(!result.Verified && result.Message.Contains("Some settings may have changed"), "Uncertain write reported success");
            var backup = PitHouseService.LoadBackup(result.BackupPath);
            Check(backup.Changes.Single().Before == reading.Values["FfbStrength"] && backup.Changes.Single().Target == 25,
                "Original values were lost after helper crash");
            Check(Directory.GetFiles(backups).Length == 1, "Apply was retried");
        });
    }
}
