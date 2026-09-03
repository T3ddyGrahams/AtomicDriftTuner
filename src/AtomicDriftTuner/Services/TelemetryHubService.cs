using System.Diagnostics;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Process-wide Assetto Corsa telemetry source.
///
/// Only this service owns the AC shared-memory reader. The desktop telemetry
/// window and Atomic Remote consume the same cached physics frames so they
/// cannot disagree about whether telemetry is available.
/// </summary>
public sealed class TelemetryHubService : IDisposable
{
    private readonly object _gate = new();
    private readonly AssettoCorsaTelemetryReader _reader = new();
    private readonly Stopwatch _clock = new();
    private readonly System.Threading.Timer _pollTimer;

    private TelemetrySample? _latest;
    private string? _error;
    private DateTimeOffset? _updatedUtc;
    private long _lastConnectAttemptMs = long.MinValue;
    private int _pollActive;
    private bool _disposed;

    public TelemetryHubService()
    {
        _pollTimer = new System.Threading.Timer(
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
                return !_disposed && _reader.IsConnected && _error is null;
        }
    }

    public bool TryConnect()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return EnsureConnectedLocked(force: true);
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
                    Connected = false,
                    Error = "Atomic telemetry service is stopped."
                };
            }

            if (!_reader.IsConnected)
                EnsureConnectedLocked(force: false);

            // Prime immediately so a newly connected remote does not have to
            // wait for the first timer callback.
            if (_reader.IsConnected && _latest is null)
                ReadOnceLocked();

            return new TelemetryHubSnapshot
            {
                Connected = _reader.IsConnected && _latest is not null && _error is null,
                Error = _error,
                Sample = _latest,
                UpdatedUtc = _updatedUtc
            };
        }
    }

    public void ResetDerivativeState()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _reader.ResetDerivativeState();
        }
    }

    private bool EnsureConnectedLocked(bool force)
    {
        if (_reader.IsConnected)
            return true;

        var now = Environment.TickCount64;
        if (!force &&
            _lastConnectAttemptMs != long.MinValue &&
            now - _lastConnectAttemptMs < 1000)
        {
            return false;
        }

        _lastConnectAttemptMs = now;

        if (!_reader.TryConnect())
        {
            _error =
                "Assetto Corsa shared memory was not found. " +
                "Start Assetto Corsa and enter an on-track session.";
            return false;
        }

        if (!_clock.IsRunning)
            _clock.Start();

        _error = null;
        _pollTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
        ReadOnceLocked();
        return _reader.IsConnected;
    }

    private void Poll(object? _)
    {
        if (Interlocked.Exchange(ref _pollActive, 1) != 0)
            return;

        try
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                if (!_reader.IsConnected)
                {
                    EnsureConnectedLocked(force: false);
                    return;
                }

                ReadOnceLocked();
            }
        }
        finally
        {
            Volatile.Write(ref _pollActive, 0);
        }
    }

    private bool ReadOnceLocked()
    {
        try
        {
            _latest = _reader.Read(_clock.Elapsed.TotalSeconds);
            _updatedUtc = DateTimeOffset.UtcNow;
            _error = null;
            return true;
        }
        catch (Exception ex)
        {
            _latest = null;
            _updatedUtc = null;
            _error = ex.Message;

            // Drop the stale mapping so the next retry can reopen AC shared
            // memory after a session/game restart.
            _reader.Dispose();
            return false;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TelemetryHubService));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _reader.Dispose();
            _latest = null;
        }

        _pollTimer.Dispose();
    }
}

public sealed class TelemetryHubSnapshot
{
    public bool Connected { get; init; }
    public string? Error { get; init; }
    public TelemetrySample? Sample { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
}
