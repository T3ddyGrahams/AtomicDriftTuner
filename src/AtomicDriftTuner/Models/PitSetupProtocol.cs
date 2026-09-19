namespace AtomicDriftTuner.Models;

public sealed class PitSetupStatus
{
    public int ProtocolVersion { get; set; } = 1;
    public PitSetupPlan? Plan { get; set; }
    public string ControlVersion { get; set; } = "";
    public bool CanApply { get; set; }
    public bool Busy { get; set; }
    public string Message { get; set; } = "Generate and stage a car setup in desktop ADT first.";
}

public sealed class PitSetupCommand
{
    public int ProtocolVersion { get; set; }
    public string Action { get; set; } = "";
    public string Operation { get; set; } = "";
    public string PlanId { get; set; } = "";
    public string CommandId { get; set; } = "";
    public string ControlVersion { get; set; } = "";
    public string LeaseId { get; set; } = "";
    public bool Success { get; set; }
    public string State { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class PitSetupResponse
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public string LeaseId { get; set; } = "";
    public string CommandId { get; set; } = "";
    public PitSetupPlan? Plan { get; set; }
}
