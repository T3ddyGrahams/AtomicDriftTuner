namespace AtomicDriftTuner.Models;

public sealed class CompanionStatus
{
    public int ProtocolVersion { get; set; } = 1;
    public string AppVersion { get; set; } = Services.DistributionInfo.Version;
    public bool TelemetryConnected { get; set; }
    public bool TelemetryStale { get; set; }
    public string NextStep { get; set; } = "Prepare your workflow in desktop ADT.";
    public string Instructions { get; set; } = "";
    public string Details { get; set; } = "";
    public string Completion { get; set; } = "";
    public string Progress { get; set; } = "";
    public CompanionRecorderState Recorder { get; set; } = new();
}

public sealed class CompanionRecorderState
{
    public string WindowId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string ControlVersion { get; set; } = "";
    public string Car { get; set; } = "";
    public string Driver { get; set; } = "";
    public string State { get; set; } = "unprepared";
    public string Message { get; set; } = "In desktop ADT, open Telemetry Recorder and prepare your driver and recording details.";
    public int Samples { get; set; }
    public double ElapsedSeconds { get; set; }
    public bool CanStart { get; set; }
    public bool CanStop { get; set; }
    public bool CanSave { get; set; }
}

public sealed class CompanionCommand
{
    public string Action { get; set; } = "";
    public string WindowId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string ControlVersion { get; set; } = "";
}

public static class CompanionCommandRules
{
    public static string? Reject(CompanionCommand request, CompanionRecorderState state)
    {
        if (request.Action is not ("start" or "stop" or "save")) return "Unknown recording command.";
        if (state.WindowId.Length == 0) return state.Message;
        if (request.WindowId != state.WindowId || request.SessionId != state.SessionId || request.ControlVersion != state.ControlVersion)
            return "The recorder changed. Refresh its status before trying again.";
        if (request.Action == "start" && !state.CanStart || request.Action == "stop" && !state.CanStop || request.Action == "save" && !state.CanSave)
            return state.Message;
        return null;
    }
}
