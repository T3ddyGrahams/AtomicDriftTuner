using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// A bounded, per-recording mailbox between the background sampler and the UI.
/// Times are original acquisition times; draining never interpolates missing data.
/// </summary>
public sealed class TelemetryRecordingCapture
{
    // One minute at the requested 50 Hz. On overflow keep the captured prefix and
    // fail closed, rather than silently overwriting evidence or growing forever.
    internal const int Capacity = 3000;
    private readonly object _gate = new();
    private readonly Queue<TelemetrySample> _pending = new();
    private bool _overflowed;

    internal TelemetryRecordingCapture() { }

    internal void Append(TelemetrySample sample)
    {
        lock (_gate)
        {
            if (_overflowed) return;
            if (_pending.Count >= Capacity) { _overflowed = true; return; }
            // GetSnapshot consumers must not be able to mutate captured evidence.
            _pending.Enqueue(sample.Copy());
        }
    }

    public TelemetryRecordingBatch Drain()
    {
        lock (_gate)
        {
            var samples = _pending.ToArray();
            _pending.Clear();
            return new(samples, _overflowed);
        }
    }
}

public sealed record TelemetryRecordingBatch(IReadOnlyList<TelemetrySample> Samples, bool Overflowed);
