using System.Globalization;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class LiveSetupChecks
{
    public static void Run(Action<string, Action> test)
    {
        test("live setup captures numeric AC values and server receipt identity without files", () =>
        {
            var request = Request();
            var before = DateTime.UtcNow;
            var captured = Parse(request);
            Check(captured.ReceivedUtc >= before && captured.ReceivedUtc <= DateTime.UtcNow && captured.ReceivedUtc.Kind == DateTimeKind.Utc,
                "Receipt time did not come from the server clock");
            Check(captured.Values.Count == 3 && captured.Values["ACSetup.PRESSURE_LF"] == 24 &&
                captured.Values["ACSetup.CAMBER_LF"] == -3.5 && captured.Values["ACSetup.INTERNAL_GEAR_4"] == 2,
                "Numeric setup values or indexed gear semantics changed");
            Check(captured.CarId == request.CarId && captured.TrackId == request.TrackId && captured.TrackLayout == request.TrackLayout &&
                captured.SessionIndex == 0 && captured.SessionType == 6 && captured.SessionGeneration == 1 &&
                captured.SetupRevision == 2 && captured.CaptureSequence == 3 && captured.Frame == 100 && captured.SimTimeMs == 1234.5,
                "Capture identity/counters were lost");
            Check(captured.Source == "csp-current-setup" && captured.Sha256.Length == 64 &&
                captured.Sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Invalid source/fingerprint");
        });

        test("live setup canonical hash ignores formatting order metadata and current culture", () =>
        {
            var a = Request();
            a.SetupIni = "[PRESSURE_LF]\nVALUE=24.00\n[CAMBER_LF]\nVALUE=-0\n";
            var b = Request();
            b.SetupIni = "\uFEFF; unsaved current setup\r\n[car]\r\nMODEL=TEST_CAR\r\n[camber_lf]\r\nvalue = +0.0\r\n[pressure_lf]\r\nVALUE=2.4e1\r\nNAME=Different label\r\n";
            var prior = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var first = Parse(a); var second = Parse(b);
                Check(first.Sha256 == second.Sha256, "Equivalent unsaved values had different fingerprints");
                Check(first.Values.Keys.SequenceEqual(second.Values.Keys), "Canonical section names/order changed");
                Check(BitConverter.DoubleToInt64Bits(second.Values["ACSetup.CAMBER_LF"]) == 0, "Negative zero was not normalized");
            }
            finally { CultureInfo.CurrentCulture = prior; }
        });

        test("live setup value change changes hash but capture time and labels do not", () =>
        {
            var baseline = Request(); var first = Parse(baseline);
            var metadata = Request(); metadata.CaptureSequence++; metadata.SetupRevision++; metadata.Frame += 10; metadata.SimTimeMs += 100;
            metadata.SetupIni += "\n[METADATA]\nNAME=Unsaved edits\nAUTHOR=Driver\n";
            Check(Parse(metadata).Sha256 == first.Sha256, "Capture metadata affected numeric fingerprint");
            metadata.SetupIni = metadata.SetupIni.Replace("VALUE=24", "VALUE=24.125", StringComparison.Ordinal);
            Check(Parse(metadata).Sha256 != first.Sha256, "Unsaved numeric change was lost");
        });

        test("live setup snapshot omits raw metadata paths transport tokens and remains read only", () =>
        {
            var request = Request();
            request.Nonce = new string('b', 64); request.WindowId = new string('c', 32);
            request.SetupIni += "\n[METADATA]\nNAME=PRIVATE_SETUP_LABEL\nPATH=C:\\Users\\PrivateOwner\\Secret.ini\nAUTHOR=PRIVATE_AUTHOR\n";
            var snapshot = Parse(request);
            request.SetupIni = "[FUEL]\nVALUE=99";
            request.CarId = "changed_car";
            var json = JsonSerializer.Serialize(snapshot);
            Check(!json.Contains("PRIVATE", StringComparison.Ordinal) && !json.Contains("Secret.ini", StringComparison.Ordinal) &&
                !json.Contains("SetupIni", StringComparison.Ordinal) && !json.Contains("Nonce", StringComparison.Ordinal) &&
                !json.Contains("WindowId", StringComparison.Ordinal), "Snapshot retained private metadata or transport envelope");
            Check(snapshot.CarId == "test_car" && snapshot.Values["ACSetup.PRESSURE_LF"] == 24, "Request mutation changed captured evidence");
            bool refused = false;
            try { ((IDictionary<string, double>)snapshot.Values)["ACSetup.PRESSURE_LF"] = 99; }
            catch (NotSupportedException) { refused = true; }
            Check(refused && snapshot.Values["ACSetup.PRESSURE_LF"] == 24, "Captured numeric dictionary is mutable");
        });

        test("live setup rejects duplicate and malformed INI rather than guessing", () =>
        {
            foreach (var ini in new[]
            {
                "[FUEL]\nVALUE=10\n[fuel]\nVALUE=10", "[FUEL]\nVALUE=10\nvalue =10",
                "[FUEL]\nVALUE=10\n[FUEL]\nNOTE=duplicate", "[CAR]\nMODEL=test_car\nMODEL=test_car\n[FUEL]\nVALUE=10",
                "VALUE=10", "[FUEL\nVALUE=10", "[]\nVALUE=10", "[FUEL] trailing\nVALUE=10",
                "[FUEL]\nVALUE 10", "[FUEL]\n=10", "[../FUEL]\nVALUE=10", "[FUEL]\nVA LUE=10",
                "[FUEL]\nVALUE=10\0", "[FUEL]\nVALUE=10\u0001"
            }) Reject(Request(ini));
        });

        test("live setup rejects nonfinite ambiguous unbounded and underflowed numeric values", () =>
        {
            foreach (var value in new[] { "", "NaN", "Infinity", "-Infinity", "1e999", "1e-999", "1,25", "1,000", "12 units", "10 ; comment", "1=2", "1000000000001" })
                Reject(Request("[FUEL]\nVALUE=" + value));
            Check(Parse(Request("[FUEL]\nVALUE=1e12")).Values["ACSetup.FUEL"] == 1e12, "Maximum finite value rejected");
            Check(Parse(Request("[FUEL]\nVALUE=0e-999")).Values["ACSetup.FUEL"] == 0, "An actual zero was treated as underflow");
            Reject(Request("[CAR]\nMODEL=test_car\n[METADATA]\nNAME=No numeric settings"));
        });

        test("live setup enforces UTF8 byte size and line limits", () =>
        {
            var maximum = PadWithComments("[FUEL]\nVALUE=10\n", LiveSetupCaptureService.MaximumIniBytes);
            Check(Encoding.UTF8.GetByteCount(maximum) == LiveSetupCaptureService.MaximumIniBytes, "Invalid limit fixture");
            Parse(Request(maximum));
            Reject(Request(maximum + "\n"));
            Reject(Request("[FUEL]\nVALUE=10\n;" + new string('é', 40_000)));
            Reject(Request("[FUEL]\nVALUE=10\nNOTE=" + new string('n', 2048)));
            Reject(Request("[FUEL]\nVALUE=10\nNOTE=\ud800"));
        });

        test("live setup accepts 512 numeric sections and refuses expansion beyond bounds", () =>
        {
            var ini = string.Join("\n", Enumerable.Range(0, 512).Select(i => $"[ITEM_{i}]\nVALUE={i}"));
            Check(Parse(Request(ini)).Values.Count == 512, "Valid 512-section boundary rejected");
            Reject(Request(ini + "\n[ITEM_512]\nVALUE=512"));
            var metadata = string.Join("\n", Enumerable.Range(0, 1024).Select(i => $"[META_{i}]\nNAME=x"));
            Reject(Request("[FUEL]\nVALUE=10\n" + metadata));
            Reject(Request("[" + new string('X', 129) + "]\nVALUE=1"));
        });

        test("live setup refuses unknown mismatched or path-like car and track identity", () =>
        {
            foreach (var identity in new[] { "", "unknown", "unknown-car", "UNKNOWN_TRACK", "none", "..", "../car", "C:\\car", "car/part", "car\n", " car", "car ", new string('c', 129) })
            {
                var car = Request(); car.CarId = identity; Reject(car);
                var track = Request(); track.TrackId = identity; Reject(track);
            }
            foreach (var model in new[] { "other_car", "unknown", "", "C:\\private\\car" })
                Reject(Request("[CAR]\nMODEL=" + model + "\n[FUEL]\nVALUE=10"));
            var layout = Request(); layout.TrackLayout = "../layout"; Reject(layout);
            var noLayout = Request(); noLayout.TrackLayout = ""; Parse(noLayout);
            Parse(Request("[FUEL]\nVALUE=10")); // Captured/root-verified CarId is authoritative if CAR metadata is absent.
        });

        test("live setup validates session revision sequence frame and simulator time", () =>
        {
            var invalid = new Action<LiveSetupCaptureRequest>[]
            {
                r => r.SessionIndex = -1, r => r.SessionIndex = 1024, r => r.SessionType = 0, r => r.SessionType = 8,
                r => r.SessionGeneration = -1, r => r.SessionGeneration = long.MaxValue,
                r => r.SetupRevision = -1, r => r.SetupRevision = long.MaxValue,
                r => r.CaptureSequence = 0, r => r.CaptureSequence = long.MaxValue,
                r => r.Frame = -1, r => r.Frame = long.MaxValue,
                r => r.SimTimeMs = -1, r => r.SimTimeMs = double.NaN,
                r => r.SimTimeMs = double.PositiveInfinity, r => r.SimTimeMs = double.MaxValue
            };
            foreach (var change in invalid) { var request = Request(); change(request); Reject(request); }
            var zero = Request(); zero.SessionGeneration = zero.SetupRevision = zero.Frame = 0; zero.SimTimeMs = 0; Parse(zero);
            var edge = Request(); edge.CaptureSequence = LiveSetupCaptureService.MaximumSafeCounter; Parse(edge);
        });

        test("live setup unsupported unavailable and null input return bounded nonsecret errors", () =>
        {
            Reject(null);
            var unsupported = Request(); unsupported.ProtocolVersion = 2; Reject(unsupported);
            var wrongSource = Request(); wrongSource.Source = "saved-file"; Reject(wrongSource);
            var unavailable = Request(); unavailable.Available = false; unavailable.Code = "not-supported"; unavailable.Message = "PRIVATE C:\\users\\owner";
            Check(!LiveSetupCaptureService.TryParse(unavailable, out var snapshot, out var error) && snapshot is null &&
                !error.Contains("PRIVATE", StringComparison.Ordinal), "Unavailable response echoed client metadata");
            foreach (var change in new Action<LiveSetupCaptureRequest>[]
            {
                r => r.SetupIni = null!, r => r.CarId = null!, r => r.TrackId = null!, r => r.TrackLayout = null!,
                r => r.Code = null!, r => r.Message = null!, r => r.Nonce = null!, r => r.WindowId = null!,
                r => r.Code = new string('c', 65), r => r.Message = new string('m', 501),
                r => r.Nonce = new string('n', 65), r => r.WindowId = new string('w', 33)
            }) { var request = Request(); change(request); Reject(request); }
        });
    }

    private static LiveSetupCaptureRequest Request(string? ini = null) => new()
    {
        Available = true, ProtocolVersion = 1, Source = "csp-current-setup", CarId = "test_car", TrackId = "test_track", TrackLayout = "drift",
        SessionIndex = 0, SessionType = 6, SessionGeneration = 1, SetupRevision = 2, CaptureSequence = 3, SimTimeMs = 1234.5, Frame = 100,
        SetupIni = ini ?? "[CAR]\nMODEL=test_car\n[PRESSURE_LF]\nVALUE=24\n[CAMBER_LF]\nVALUE=-3.5\n[INTERNAL_GEAR_4]\nVALUE=2\n"
    };

    private static CapturedCarSetup Parse(LiveSetupCaptureRequest request)
    {
        Check(LiveSetupCaptureService.TryParse(request, out var captured, out var error), error);
        return captured!;
    }

    private static void Reject(LiveSetupCaptureRequest? request)
    {
        Check(!LiveSetupCaptureService.TryParse(request, out var captured, out var error) && captured is null && error.Length is > 0 and < 240,
            "Invalid capture was accepted or did not return a useful bounded error");
    }

    private static string PadWithComments(string ini, int bytes)
    {
        var builder = new StringBuilder(ini);
        while (builder.Length < bytes)
        {
            var remaining = bytes - builder.Length;
            if (remaining == 1) { builder.Append('\n'); break; }
            var length = Math.Min(1000, remaining - 2);
            builder.Append(';').Append('x', length).Append('\n');
        }
        return builder.ToString();
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
