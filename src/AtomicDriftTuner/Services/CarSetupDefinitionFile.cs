namespace AtomicDriftTuner.Services;

/// <summary>Reads car setup definitions independently. Never used to relax saved/live setup validation.</summary>
internal sealed class CarSetupDefinitionFile
{
    public Dictionary<string, Dictionary<string, string>> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> InvalidSections { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static CarSetupDefinitionFile Parse(string text)
    {
        var result = new CarSetupDefinitionFile();
        string? current = null;
        var headers = 0;
        foreach (var raw in text.TrimStart('\uFEFF').Split('\n'))
        {
            var line = raw.Split(';')[0].Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;
            if (line.StartsWith('['))
            {
                // An unidentifiable boundary could hide a gearing override or limiter.
                // Such files remain unsupported, rather than treating a control as absent.
                if (++headers > 2048 || !line.EndsWith(']') || line.Length is < 3 or > 256 ||
                    line[1..^1].IndexOfAny(['[', ']']) >= 0 || string.IsNullOrWhiteSpace(line[1..^1]))
                    throw new InvalidDataException("Malformed section boundary in setup.ini; its controls cannot be identified safely.");
                current = line[1..^1].Trim().ToUpperInvariant();
                if (!result.Sections.TryAdd(current, new(StringComparer.OrdinalIgnoreCase)))
                    result.InvalidSections.TryAdd(current, "duplicate section");
                continue;
            }
            if (current is null)
                throw new InvalidDataException("Unassigned fields in setup.ini; its controls cannot be identified safely.");
            if (result.InvalidSections.ContainsKey(current)) continue;
            var equals = line.IndexOf('=');
            if (equals <= 0 || line.Length > 4096 || string.IsNullOrWhiteSpace(line[..equals]))
            { result.InvalidSections.TryAdd(current, "malformed field"); continue; }
            var section = result.Sections[current];
            if (section.Count >= 2048) throw new InvalidDataException("Too many setup.ini fields.");
            if (!section.TryAdd(line[..equals].Trim(), line[(equals + 1)..].Trim()))
                result.InvalidSections.TryAdd(current, "duplicate field");
        }
        // Keep names for presence checks, but never expose either conflicting definition's values.
        foreach (var name in result.InvalidSections.Keys) result.Sections[name].Clear();
        return result;
    }

    public static string Warning(string section, string reason) =>
        $"setup.ini [{section}] has a {reason}; this control is left unchanged. Other valid controls remain available.";
}
