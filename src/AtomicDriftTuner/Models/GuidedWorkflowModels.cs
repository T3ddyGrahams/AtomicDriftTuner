namespace AtomicDriftTuner.Models;

public sealed class GuidedPreferences
{
    public string Schema { get; set; } = "adt/guided-preferences/1";
    public bool Completed { get; set; }
    public string SimHub { get; set; } = "Not sure";
    public string Azom { get; set; } = "Not sure";
    public bool WantLiveConnection { get; set; }
    public string DriverName { get; set; } = "Local driver";
}

public sealed class GuidedJourney
{
    public string Schema { get; set; } = "adt/guided-journey/1";
    public string ContextKey { get; set; } = "";
    public string DriverId { get; set; } = "";
    public bool CarConfirmed { get; set; }
    public string GoalSignature { get; set; } = "";
    public bool TuneGenerated { get; set; }
    public string GeneratedSignature { get; set; } = "";
    public bool TuneReady { get; set; }
    public string BaselineId { get; set; } = "";
    public string AfterId { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public bool Reviewed { get; set; }
    public string SetupPath { get; set; } = "";
    public string Conditions { get; set; } = "";
}

public enum GuidedStage { Welcome, Car, Goals, Prepare, Baseline, Findings, Test, Compare, Complete, GoalsChanged }
public sealed record GuidedStep(GuidedStage Stage, string Title, string Instructions, string Action);
public sealed record IntegrationState(bool SimHubInstalled, bool SimHubRunning, bool BridgeInstalled, bool BridgeConnected, bool AzomDetected, bool SettingsReadable);
public sealed record RecordingPlan(string DriverId, string DriverName, string BaselineId, string Recommendation, string SetupPath, string Conditions);
