namespace AtomicDriftTuner.Models;

/// <summary>A source-backed interpretation of a saved setup value; never a replacement for that value.</summary>
public sealed record DecodedSetupSetting(
    string Section,
    string SavedValue,
    string Status,
    string Value,
    string Source,
    string Explanation,
    double? NumericValue = null)
{
    public const string Verified = "Verified mapping";
    public const string Partial = "Partial understanding";
    public const string Unsupported = "Unsupported";

    public string Display => $"[{Section}] saved {SavedValue} — {Status}: {Value}" +
        (string.IsNullOrWhiteSpace(Source) ? "" : $" — {Source}") + $". {Explanation}";
}
