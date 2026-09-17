using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Find one exact saved run without analyzing every earlier recording.</summary>
public static class CompanionSavedRunLookup
{
    public static SavedTelemetrySession? Find(TelemetrySessionStore store, string sessionId)
    {
        if (!Guid.TryParseExact(sessionId, "N", out _) || !Directory.Exists(store.RootDirectory)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var path in Directory.EnumerateFiles(store.RootDirectory, "session.json", options))
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                // ADT writes the session identity before samples. Read only the metadata prefix;
                // unrelated large recordings must not be reanalyzed just to locate a baseline.
                var prefix = new byte[Math.Min(65536L, stream.Length)];
                stream.ReadExactly(prefix);
                if (ReadSessionId(prefix) != sessionId) continue;
                var run = store.TryLoad(path);
                if (run?.Session.Id == sessionId) return run;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return null;
    }

    private static string? ReadSessionId(ReadOnlySpan<byte> prefix)
    {
        var reader = new Utf8JsonReader(prefix, isFinalBlock: false, state: default);
        bool inSession = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
                inSession = string.Equals(reader.GetString(), "session", StringComparison.OrdinalIgnoreCase);
            if (inSession && reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 2 &&
                string.Equals(reader.GetString(), "id", StringComparison.OrdinalIgnoreCase) && reader.Read() && reader.TokenType == JsonTokenType.String)
                return reader.GetString();
        }
        return null;
    }
}
