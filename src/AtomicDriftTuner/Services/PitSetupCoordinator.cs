using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Dispatcher-owned admission ledger. A missing acknowledgement never expires into permission to record.</summary>
public sealed class PitSetupCoordinator
{
    private PitSetupPlan? _plan;
    private string _context = "";
    private bool _consumed;
    private long _revision;
    private string _message = "Generate and stage a car setup in desktop ADT, then use the companion Pit setup tab.";
    private readonly Dictionary<string, PitSetupPlan> _authorized = new(StringComparer.Ordinal);
    private readonly HashSet<string> _commands = new(StringComparer.Ordinal);
    private PitSetupCommand? _pending;
    private string _lease = "";
    private PitSetupCommand? _completed;
    private PitSetupResponse? _completion;
    public bool Busy => _pending is not null;
    public string? RecordingBlockReason => Busy
        ? "A pit setup action needs its result confirmed. Use Sync result with ADT in the companion Pit setup tab. If AC has closed, clear the pending action in ADT's car setup screen."
        : null;

    public void Stage(PitSetupPlan plan, string context)
    {
        if (Busy) throw new InvalidOperationException(RecordingBlockReason);
        _plan = plan; _context = context; _consumed = false; _revision++;
        _message = $"{plan.Changes.Count} change(s) staged. Open AC's pit setup menu, then choose Save & Apply Tune in ADT Companion → Pit setup. Nothing has been applied yet.";
    }

    public PitSetupStatus Status(string context, string carId, bool recording)
    {
        var matches = _plan is not null && _context == context && string.Equals(_plan.CarId, carId, StringComparison.OrdinalIgnoreCase);
        return new()
        {
            Plan = matches ? _plan : null, Busy = Busy,
            CanApply = matches && !_consumed && !Busy && !recording,
            ControlVersion = CompanionWorkflowPresentation.Version(new { _revision, context, carId, recording, Busy }),
            Message = Busy ? RecordingBlockReason! : recording ? "Stop recording before applying or restoring a setup."
                : _plan is not null && !matches ? "The selected car, driver, goals or workflow changed. Generate and stage a setup for the current selection." : _message
        };
    }

    public PitSetupResponse Execute(PitSetupCommand request, string context, string carId, bool recording)
    {
        if (!Valid(request)) return Reject("Invalid pit setup command.");
        if (request.Action == "complete") return Complete(request);
        if (Busy) return Reject(RecordingBlockReason!);
        var status = Status(context, carId, recording);
        if (request.ControlVersion != status.ControlVersion || recording)
            return Reject("The recording or setup context changed. Refresh status before trying again.");
        if (_commands.Contains(request.CommandId)) return Reject("This operation was already admitted. Refresh status; it will not be applied twice.");
        PitSetupPlan? plan;
        if (request.Operation == "apply")
        {
            if (!status.CanApply || _plan?.PlanId != request.PlanId) return Reject(status.Message);
            plan = _plan;
        }
        else
        {
            if (!_authorized.TryGetValue(request.PlanId, out plan) || !string.Equals(plan.CarId, carId, StringComparison.OrdinalIgnoreCase))
                return Reject("The previous setup is not authorized for this car in this desktop session.");
        }
        if (_commands.Count >= 2048) return Reject("Restart desktop ADT before another pit action; this session has reached its operation limit.");
        _commands.Add(request.CommandId);
        _authorized[plan!.PlanId] = plan;
        // Keep only the recent restore targets. The Lua app retains the latest local backup.
        while (_authorized.Count > 64) _authorized.Remove(_authorized.Keys.First());
        _pending = new() { Operation = request.Operation, PlanId = request.PlanId, CommandId = request.CommandId };
        _lease = Guid.NewGuid().ToString("N"); _consumed = true; _revision++;
        return new() { Ok = true, Message = "Pit operation admitted. The companion must verify the result.", LeaseId = _lease, CommandId = request.CommandId, Plan = plan };
    }

    private PitSetupResponse Complete(PitSetupCommand request)
    {
        if (_completed is not null && SameCompletion(_completed, request)) return _completion!;
        if (_pending is null || request.LeaseId != _lease || request.CommandId != _pending.CommandId ||
            request.PlanId != _pending.PlanId || request.Operation != _pending.Operation)
            return Reject("This result does not match the pending pit operation. Do not repeat the setup change.");
        if (request.Success && request.State != (request.Operation == "apply" ? "applied" : "restored") ||
            !request.Success && request.State is not ("failed" or "verification-failed"))
            return Reject("The pit operation result is inconsistent. Check the companion result.");
        _message = request.Success ? request.Operation == "apply"
            ? "The companion reports the new setup saved, loaded and verified. Confirm it for your next recording."
            : "The companion reports the previous setup restored and verified. Confirm it for your next recording."
            : "The pit action did not complete successfully. Check the companion result and current setup before recording. A saved file alone does not confirm application.";
        _completed = new() { Operation = request.Operation, PlanId = request.PlanId, CommandId = request.CommandId,
            LeaseId = request.LeaseId, Success = request.Success, State = request.State };
        _completion = new() { Ok = true, Message = _message, LeaseId = _lease, CommandId = request.CommandId };
        _pending = null; _lease = ""; _revision++;
        return _completion;
    }

    public void ClearAfterGameExit()
    {
        _pending = null; _lease = ""; _plan = null; _authorized.Clear(); _completed = null; _completion = null;
        _consumed = true; _revision++; _message = "Pending pit state cleared after AC closed. Start a fresh session, then generate and stage the setup again.";
    }

    private static bool SameCompletion(PitSetupCommand a, PitSetupCommand b) => a.CommandId == b.CommandId && a.LeaseId == b.LeaseId &&
        a.PlanId == b.PlanId && a.Operation == b.Operation && a.Success == b.Success && a.State == b.State;
    private static PitSetupResponse Reject(string message) => new() { Message = message };
    public static bool Valid(PitSetupCommand? r) => r is not null && r.ProtocolVersion == 1 &&
        r.Action is "prepare" or "complete" && r.Operation is "apply" or "restore" &&
        Guid.TryParseExact(r.PlanId, "N", out _) && r.CommandId is { Length: > 0 and <= 64 } &&
        r.CommandId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') &&
        r.ControlVersion is { Length: 64 } && r.ControlVersion.All(Uri.IsHexDigit) &&
        r.Message is { Length: <= 1000 } && r.State is { Length: <= 32 } &&
        (r.Action == "prepare" || Guid.TryParseExact(r.LeaseId, "N", out _));
}
