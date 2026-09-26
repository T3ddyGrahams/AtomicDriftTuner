namespace AtomicDriftTuner.Models;

/// <summary>A recorded hypothesis, never evidence that a setting was applied or helped.</summary>
public sealed class RecommendationTest
{
    public string Schema { get; set; } = "adt/recommendation-test/1";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Origin { get; set; } = "";
    public string Description { get; set; } = "";
    public string MetricKey { get; set; } = "";
    public string BaselineSessionId { get; set; } = "";
    public string BaselineTuneId { get; set; } = "";
    public string BaselineSettingsFingerprint { get; set; } = "";
    public string DriverId { get; set; } = "";
    public string ContextKey { get; set; } = "";
    public string GoalSignature { get; set; } = "";
    public List<ExpectedSettingChange> Changes { get; set; } = [];
}

public sealed record ExpectedSettingChange(string Key, double Before, double After);
