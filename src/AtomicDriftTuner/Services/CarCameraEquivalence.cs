using System.Globalization;
using System.Text;

namespace AtomicDriftTuner.Services;

/// <summary>Permits only finite seat-position/pitch value differences in car.ini.
/// Everything outside those value spans, including layout/comments, stays byte-equivalent.</summary>
internal static class CarCameraEquivalence
{
    internal static bool Matches(byte[] packed, byte[] unpacked) =>
        TryMask(packed, out var first) && TryMask(unpacked, out var second) && first == second;

    private static bool TryMask(byte[] bytes, out string masked)
    {
        masked = "";
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return false; }
        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) return false;
        var lines = text.Split('\n'); var section = ""; var cameraValues = 0;
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i]; var trimmed = raw.Trim();
            if (i == 0) trimmed = trimmed.TrimStart('\uFEFF');
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith('['))
            {
                var close = trimmed.IndexOf(']');
                if (close < 2 || !Name(trimmed[1..close])) return false;
                var suffix = trimmed[(close + 1)..].TrimStart();
                if (suffix.Length > 0 && !suffix.StartsWith(';')) return false;
                section = trimmed[1..close];
                if (!sections.Add(section)) return false;
                continue;
            }
            var equals = raw.IndexOf('=');
            if (section.Length == 0 || equals < 1) return false;
            var key = raw[..equals].Trim();
            if (!Name(key) || !keys.Add(section + "." + key)) return false;
            if (!section.Equals("GRAPHICS", StringComparison.OrdinalIgnoreCase)) continue;
            var count = key.Equals("DRIVEREYES", StringComparison.OrdinalIgnoreCase) ? 3 :
                key.Equals("ON_BOARD_PITCH_ANGLE", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (count == 0) continue;
            var start = equals + 1; var comment = raw.IndexOf(';', start); var end = comment < 0 ? raw.Length : comment;
            while (start < end && char.IsWhiteSpace(raw[start])) start++;
            while (end > start && char.IsWhiteSpace(raw[end - 1])) end--;
            var numbers = raw[start..end].Split(',');
            if (numbers.Length != count || numbers.Any(n => !double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))) return false;
            lines[i] = raw[..start] + "<ADT_CAMERA_VALUE>" + raw[end..];
            cameraValues++;
        }
        if (cameraValues == 0) return false;
        masked = string.Join('\n', lines);
        return true;
    }
    private static bool Name(string value) => value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
