using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Parses CSP's current setup without reading files or retaining INI metadata and paths.</summary>
public static class LiveSetupCaptureService
{
    public const string CaptureSource = "csp-current-setup";
    public const int MaximumIniBytes = 64 * 1024;
    public const int MaximumNumericSections = 512;
    public const double MaximumAbsoluteValue = 1_000_000_000_000;
    public const long MaximumSafeCounter = 9_007_199_254_740_991;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryParse(LiveSetupCaptureRequest? request, out CapturedCarSetup? snapshot, out string error)
    {
        snapshot = null;
        error = "";
        if (request is null) return Fail("CSP did not return a setup capture.", out error);
        if (!BoundedText(request.Code, 64) || !BoundedText(request.Message, 500) ||
            !BoundedText(request.Nonce, 64) || !BoundedText(request.WindowId, 32))
            return Fail("The setup capture metadata is invalid.", out error);
        if (!request.Available) return Fail("CSP could not read the current setup. Use the desktop setup attachment until live capture is available.", out error);
        if (request.ProtocolVersion != 1 || request.Source != CaptureSource)
            return Fail("This setup capture format is not supported.", out error);
        if (!Identity(request.CarId, false) || !Identity(request.TrackId, false) || !Identity(request.TrackLayout, true))
            return Fail("The setup capture has an unknown or invalid car or track identity.", out error);
        // CSP's installed SDK defines active session types 1 (Practice) through 7 (Drag); 0 is Undefined.
        if (request.SessionIndex is < 0 or > 1023 || request.SessionType is < 1 or > 7 ||
            !Counter(request.SessionGeneration) || !Counter(request.SetupRevision) ||
            request.CaptureSequence < 1 || !Counter(request.CaptureSequence) || !Counter(request.Frame) ||
            !double.IsFinite(request.SimTimeMs) || request.SimTimeMs < 0 || request.SimTimeMs > MaximumSafeCounter)
            return Fail("The setup capture has invalid session or timing information.", out error);
        if (string.IsNullOrWhiteSpace(request.SetupIni) || request.SetupIni.Length > MaximumIniBytes)
            return Fail("The current setup is empty or exceeds the 64 KiB capture limit.", out error);
        try
        {
            if (StrictUtf8.GetByteCount(request.SetupIni) > MaximumIniBytes)
                return Fail("The current setup exceeds the 64 KiB capture limit.", out error);
        }
        catch (EncoderFallbackException)
        {
            return Fail("The current setup contains invalid text.", out error);
        }
        if (request.SetupIni.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            return Fail("The current setup contains invalid control characters.", out error);

        var values = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        var hasValue = false;
        var hasCarModel = false;
        double? unassignedValue = null;
        using var reader = new StringReader(request.SetupIni.TrimStart('\uFEFF'));
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            if (raw.Length > 2048) return Fail("A setup line exceeds the capture limit.", out error);
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#' || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line[0] == '[')
            {
                if (line.Length < 2 || line[^1] != ']' ||
                    line[1..^1].Trim() is { Length: > 0 } header && !Identifier(header))
                    return Fail("The setup contains a malformed section header.", out error);
                section = line[1..^1].Trim().ToUpperInvariant();
                if (!sections.Add(section)) return Fail("The setup contains duplicate sections.", out error);
                if (sections.Count > 1024) return Fail("The setup contains too many sections.", out error);
                hasValue = false;
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals <= 0 || !Identifier(line[..equals].Trim()))
                return Fail("The setup contains a malformed entry.", out error);
            var key = line[..equals].Trim();
            var text = line[(equals + 1)..].Trim();
            if (section == "CAR" && key.Equals("MODEL", StringComparison.OrdinalIgnoreCase))
            {
                if (hasCarModel || !Identity(text, false) || !text.Equals(request.CarId, StringComparison.OrdinalIgnoreCase))
                    return Fail("The setup's car metadata does not identify the captured car unambiguously.", out error);
                hasCarModel = true;
                continue;
            }
            if (!key.Equals("VALUE", StringComparison.OrdinalIgnoreCase)) continue;
            if (hasValue) return Fail("The setup contains duplicate VALUE entries.", out error);
            hasValue = true;
            if (text.Length is 0 or > 128 || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) ||
                !double.IsFinite(numeric) || Math.Abs(numeric) > MaximumAbsoluteValue)
                return Fail("The setup contains a nonnumeric, nonfinite or out-of-range VALUE.", out error);
            // Do not turn an underflowed nonzero value into a false zero snapshot.
            if (numeric == 0 && text.Split('e', 'E')[0].Any(c => c is >= '1' and <= '9'))
                return Fail("A setup VALUE is too small to capture reliably.", out error);
            if (section.Length == 0)
            {
                sections.Add(""); // A later explicit [] would be a duplicate root section.
                unassignedValue = numeric == 0 ? 0 : numeric;
            }
            else values.Add("ACSetup." + section, numeric == 0 ? 0 : numeric);
            if (values.Count + (unassignedValue.HasValue ? 1 : 0) > MaximumNumericSections)
                return Fail("The setup exceeds the 512 numeric-section capture limit.", out error);
        }
        if (values.Count == 0) return Fail("The current setup contains no numeric VALUE sections.", out error);

        // Fingerprint values only: names, paths, comments, section order and number formatting are irrelevant.
        var canonical = new StringBuilder("adt/captured-setup/1\n");
        foreach (var pair in values)
            canonical.Append(pair.Key).Append('=').Append(pair.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        if (unassignedValue is double unnamed)
            canonical.Append("ACUnassigned.VALUE=").Append(unnamed.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        snapshot = new CapturedCarSetup
        {
            Source = CaptureSource,
            UnassignedValue = unassignedValue,
            Values = new ReadOnlyDictionary<string, double>(values),
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant(),
            CarId = request.CarId, TrackId = request.TrackId, TrackLayout = request.TrackLayout,
            SessionIndex = request.SessionIndex, SessionType = request.SessionType,
            SessionGeneration = request.SessionGeneration, SetupRevision = request.SetupRevision,
            CaptureSequence = request.CaptureSequence, SimTimeMs = request.SimTimeMs, Frame = request.Frame,
            ReceivedUtc = DateTime.UtcNow
        };
        return true;
    }

    private static bool Counter(long value) => value is >= 0 and <= MaximumSafeCounter;

    private static bool BoundedText(string? value, int length) =>
        value is not null && value.Length <= length && !value.Any(char.IsControl);

    private static bool Identity(string? value, bool optional)
    {
        if (value is null || value.Length > 128 || value != value.Trim()) return false;
        if (value.Length == 0) return optional;
        if (value is "." or ".." || value.Any(c => char.IsControl(c) || "\\/:*?\"<>|".Contains(c))) return false;
        if (new[] { "unknown", "unknown-car", "unknown_car", "unknown-track", "unknown_track", "none", "null", "-" }
            .Contains(value, StringComparer.OrdinalIgnoreCase)) return false;
        try { StrictUtf8.GetByteCount(value); return true; }
        catch (EncoderFallbackException) { return false; }
    }

    private static bool Identifier(string value) => value.Length is > 0 and <= 128 &&
        value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private static bool Fail(string message, out string error) { error = message; return false; }
}
