namespace AtomicDriftTuner.Models;

public enum TuningFocus { Both, FfbOnly, CarSetupOnly }
public enum RecommendationArea { General, Ffb, CarSetup }
public enum FfbProvider { SimHubAzom, MozaPitHouse, Manual, LogitechG27 }

public sealed class GuidedPreferences
{
    public string Schema { get; set; } = "adt/guided-preferences/1";
    public bool Completed { get; set; }
    public string SimHub { get; set; } = "Not sure";
    public string Azom { get; set; } = "Not sure";
    public bool WantLiveConnection { get; set; }
    // Zero preserves the provider used by older preferences.
    public FfbProvider FfbProvider { get; set; } = FfbProvider.SimHubAzom;
    public string MozaSdkFolder { get; set; } = "";
    public string DriverName { get; set; } = "Local driver";
    public TuningFocus Focus { get; set; } = TuningFocus.Both;
    public bool FocusChoiceConfirmed { get; set; }
    public bool ShowDetailedHelp { get; set; }
}

public sealed class GuidedJourney
{
    public FfbProvider FfbProvider { get; set; } = FfbProvider.SimHubAzom;
    public string Schema { get; set; } = "adt/guided-journey/1";
    public string ContextKey { get; set; } = "";
    public string DriverId { get; set; } = "";
    public TuningFocus Focus { get; set; } = TuningFocus.Both;
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
public sealed record GuidedStep(GuidedStage Stage, string Title, string Instructions, string Action)
{
    public string Details { get; init; } = "";
    public string Completion { get; init; } = "";
}
public sealed record IntegrationState(bool SimHubInstalled, bool SimHubRunning, bool BridgeInstalled, bool BridgeConnected, bool AzomDetected, bool SettingsReadable);
public sealed record RecordingPlan(string DriverId, string DriverName, string BaselineId, string Recommendation, string SetupPath, string Conditions,
    TuningFocus Focus = TuningFocus.Both, bool ShowDetailedHelp = true, FfbProvider FfbProvider = FfbProvider.SimHubAzom);
