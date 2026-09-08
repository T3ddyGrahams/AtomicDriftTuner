using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class TelemetryAnalyzer
{
    public TelemetryAnalysis Analyze(TelemetrySession session) => new DriftDiagnosisEngine().Analyze(session);
}
