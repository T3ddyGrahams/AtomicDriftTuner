using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>One readiness edge per recording; presentation only, never controls recording.</summary>
public sealed class RecordingReadyNotification
{
    private string _observedSession = "";
    private bool _announced;

    public bool Observe(string sessionId, bool recording, RecordingEvidenceProgress progress, bool soundEnabled)
    {
        if (sessionId != _observedSession) { _observedSession = sessionId; _announced = false; }
        if (string.IsNullOrEmpty(sessionId) || !recording || progress.State != "ready" || !progress.ReadyToReview || _announced)
            return false;
        _announced = true; // Enabling sound later must not replay an already observed readiness event.
        return soundEnabled;
    }
}
