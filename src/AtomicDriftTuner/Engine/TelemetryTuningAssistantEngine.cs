using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class TelemetryTuningAssistantEngine
{
    public TuningAssistantReport Build(TuneInput input, CarBehaviorTarget behavior,
        SavedTelemetrySession selected, SavedTelemetrySession? previous = null)
        => new DriftAssistantReportBuilder().Build(input, behavior, selected, previous);
}
