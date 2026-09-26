using System.Diagnostics;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Process-wide Assetto Corsa telemetry source.
///
/// Only this service owns the AC shared-memory reader. The desktop telemetry
/// window and ADT Remote share its live snapshot. Recordings additionally retain
/// each acquired frame in a bounded mailbox, independent of display refreshes.
/// </summary>
public sealed class TelemetryHubService : IDisposable
{
    private const int PollIntervalMs =
        20;

    private const int ReconnectDelayMs =
        1000;

    private static readonly TimeSpan StaleAfter =
        TimeSpan.FromMilliseconds(500);

    private readonly object _gate =
        new();

    private AssettoCorsaTelemetryReader _reader =
        new();

    private readonly TrackPositionReader _trackPosition = new();

    private readonly Stopwatch _clock =
        new();

    private readonly System.Threading.Timer _pollTimer;

    private TelemetrySample? _latest;
    private readonly HashSet<TelemetryRecordingCapture> _recordings = new();
    private int? _lastObservedPacketId;
    private string? _error;
    private DateTimeOffset? _updatedUtc;

    private long _lastConnectAttemptMs =
        long.MinValue;

    private int _pollActive;
    private bool _disposed;

    public TelemetryHubService()
    {
        _pollTimer =
            new System.Threading.Timer(
                Poll,
                null,
                Timeout.Infinite,
                Timeout.Infinite);
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return
                    !_disposed &&
                    IsSnapshotHealthyLocked();
            }
        }
    }

    public bool TryConnect()
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            return EnsureConnectedLocked(
                force: true);
        }
    }

    public TelemetryHubSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return new TelemetryHubSnapshot
                {
                    Connected =
                        false,

                    Stale =
                        false,

                    Error =
                        "ADT telemetry service is stopped."
                };
            }

            if (!_reader.IsConnected)
            {
                EnsureConnectedLocked(
                    force: false);
            }

            // Prime immediately so a newly connected consumer does not have
            // to wait for the first timer callback.
            if (
                _reader.IsConnected &&
                _latest is null)
            {
                ReadOnceLocked();
            }

            var stale =
                IsLatestSampleStaleLocked();

            return new TelemetryHubSnapshot
            {
                Connected =
                    IsSnapshotHealthyLocked(),

                Stale =
                    stale,

                Error =
                    _error,

                Sample =
                    _latest,

                UpdatedUtc =
                    _updatedUtc
            };
        }
    }

    public void ResetDerivativeState()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _reader.ResetDerivativeState();
        }
    }

    public TelemetryRecordingCapture StartRecordingCapture()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!IsSnapshotHealthyLocked())
                throw new InvalidOperationException("Live AC telemetry is unavailable. Enter an on-track session and try again.");
            _reader.ResetDerivativeState();
            var capture = new TelemetryRecordingCapture();
            _recordings.Add(capture);
            // No cached frame from before the recording belongs to the new run.
            return capture;
        }
    }

    public TelemetryRecordingBatch StopRecordingCapture(TelemetryRecordingCapture capture)
    {
        lock (_gate)
        {
            _recordings.Remove(capture);
            // Detach and take the final batch atomically with respect to sampling.
            return capture.Drain();
        }
    }

    private bool EnsureConnectedLocked(
        bool force)
    {
        if (_reader.IsConnected)
        {
            return true;
        }

        var now =
            Environment.TickCount64;

        if (
            !force &&
            _lastConnectAttemptMs != long.MinValue &&
            now - _lastConnectAttemptMs < ReconnectDelayMs)
        {
            return false;
        }

        _lastConnectAttemptMs =
            now;

        bool connected;

        try
        {
            connected =
                _reader.TryConnect();
        }
        catch
        {
            connected =
                false;
        }

        if (!connected)
        {
            ClearCurrentSampleLocked();

            _error =
                "Assetto Corsa shared memory was not found. Start Assetto Corsa and enter an on-track session.";

            return false;
        }

        if (!_clock.IsRunning)
        {
            _clock.Start();
        }

        // A newly opened AC shared-memory mapping represents a new continuity
        // boundary. Do not calculate steering/yaw derivatives from values that
        // belonged to the previous session.
        _reader.ResetDerivativeState();

        _latest =
            null;

        _updatedUtc =
            null;

        _error =
            null;

        _pollTimer.Change(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(
                PollIntervalMs));

        return ReadOnceLocked();
    }

    private void Poll(
        object? _)
    {
        if (
            Interlocked.Exchange(
                ref _pollActive,
                1) != 0)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (!_reader.IsConnected)
                {
                    EnsureConnectedLocked(
                        force: false);

                    return;
                }

                ReadOnceLocked();
            }
        }
        finally
        {
            Volatile.Write(
                ref _pollActive,
                0);
        }
    }

    private bool ReadOnceLocked()
    {
        try
        {
            var sample =
                _reader.Read(
                    _clock.Elapsed.TotalSeconds);

            if (sample is null)
            {
                HandleReadFailureLocked(
                    "Assetto Corsa telemetry returned no sample.");

                return false;
            }

            // Freshness follows the producer, not how often ADT polls the map.
            if (_lastObservedPacketId == sample.PacketId)
            {
                if (_latest is null || IsLatestSampleStaleLocked())
                {
                    HandleReadFailureLocked("Assetto Corsa stopped updating telemetry. ADT will reconnect automatically.");
                    return false;
                }
                return true;
            }

            sample.Position = _trackPosition.Read(sample.TimeSeconds);
            PublishSampleLocked(sample);

            return true;
        }
        catch
        {
            HandleReadFailureLocked(
                "Assetto Corsa telemetry became unavailable. ADT will retry automatically.");

            return false;
        }
    }

    private void PublishSampleLocked(TelemetrySample sample)
    {
        if (_lastObservedPacketId == sample.PacketId) return;
        _lastObservedPacketId = sample.PacketId;
        _latest = sample;
        _updatedUtc = DateTimeOffset.UtcNow;
        _error = null;
        foreach (var capture in _recordings) capture.Append(sample);
    }

    private void HandleReadFailureLocked(
        string message)
    {
        ClearCurrentSampleLocked();

        _error =
            message;

        try
        {
            // Drop the stale mapping so the next retry can reopen AC shared
            // memory after a session change, game restart, or mapping reset.
            _reader.Dispose();
        }
        catch
        {
            // Reader cleanup failure must not replace the telemetry-state
            // error. The next reconnect attempt remains authoritative.
        }
        _reader = new AssettoCorsaTelemetryReader();
    }

    private bool IsSnapshotHealthyLocked()
    {
        return
            _reader.IsConnected &&
            _latest is not null &&
            _error is null &&
            !IsLatestSampleStaleLocked();
    }

    private bool IsLatestSampleStaleLocked()
    {
        if (
            _latest is null ||
            _updatedUtc is null)
        {
            return false;
        }

        return
            DateTimeOffset.UtcNow -
            _updatedUtc.Value >
            StaleAfter;
    }

    private void ClearCurrentSampleLocked()
    {
        _latest =
            null;

        _updatedUtc =
            null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(TelemetryHubService));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed =
                true;

            try
            {
                _pollTimer.Change(
                    Timeout.Infinite,
                    Timeout.Infinite);
            }
            catch
            {
                // Timer shutdown should not prevent the telemetry reader from
                // being released.
            }

            try
            {
                _reader.Dispose();
            }
            catch
            {
                // Dispose is best-effort at application shutdown.
            }

            ClearCurrentSampleLocked();

            _recordings.Clear();

            _error =
                "ADT telemetry service is stopped.";
        }

        _trackPosition.Dispose();
        _pollTimer.Dispose();

        _clock.Stop();
    }
}

public sealed class TelemetryHubSnapshot
{
    public bool Connected { get; init; }

    public bool Stale { get; init; }

    public string? Error { get; init; }

    public TelemetrySample? Sample { get; init; }

    public DateTimeOffset? UpdatedUtc { get; init; }
}
