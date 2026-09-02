using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AzomBridgeClient
{
    private readonly string _pipeName;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // Even though explicit Apply batches are already sequential, keep a
    // process-wide single-flight gate around direct AZOM commits so two Atomic
    // windows/tasks cannot overlap internal writes.
    private static readonly SemaphoreSlim DirectWriteGate = new(1, 1);
    private static readonly object DirectWriteTimingLock = new();
    private static long _lastDirectWriteTick;
    private const int DirectWriteMinGapMs = 120;

    public AzomBridgeClient(string pipeName) => _pipeName = pipeName;

    public async Task<AzomLiveSnapshot> ReadSnapshotAsync(int timeoutMs = 2500, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        try
        {
            await pipe.ConnectAsync(timeout.Token);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                "Windows denied access to the Atomic AZOM bridge named pipe. " +
                "This is usually caused by SimHub and Atomic Drift Tuner running at different privilege levels. " +
                "Use the v0.5.2 bridge and restart SimHub, or temporarily run both applications at the same elevation level.",
                ex);
        }

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, true, 1024, true);
        await writer.WriteLineAsync("{\"command\":\"snapshot\"}");
        string? line;
        try
        {
            line = await reader.ReadLineAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The Atomic SimHub Bridge did not answer within 2.5 seconds. " +
                "The live read was aborted; the tuner remains safe to use.");
        }

        if (string.IsNullOrWhiteSpace(line))
            throw new IOException("Atomic SimHub Bridge returned an empty response.");
        if (line.Length > 256_000)
            throw new IOException("Atomic SimHub Bridge returned an unexpectedly large response; the read was rejected.");

        var response = JsonSerializer.Deserialize<BridgeResponse>(line, Json)
            ?? throw new IOException("Atomic SimHub Bridge returned invalid JSON.");
        if (!response.Ok) throw new IOException(response.Error ?? "Atomic SimHub Bridge reported an error.");
        return response.Snapshot ?? throw new IOException("Atomic SimHub Bridge did not return an AZOM snapshot.");
    }

    public async Task TriggerActionAsync(
        string actionName,
        int timeoutMs = 4000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionName) ||
            !actionName.StartsWith("AZOM.", StringComparison.Ordinal))
            throw new ArgumentException(
                "Only AZOM.* actions can be triggered.", nameof(actionName));

        using var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        try
        {
            await pipe.ConnectAsync(timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Timed out connecting to the Atomic SimHub Bridge.");
        }

        using var writer = new StreamWriter(
            pipe, new UTF8Encoding(false), 1024, true)
        { AutoFlush = true };

        using var reader = new StreamReader(
            pipe, Encoding.UTF8, true, 1024, true);

        await writer.WriteLineAsync(JsonSerializer.Serialize(new
        {
            command = "triggerAction",
            actionName
        }));

        string? line;
        try
        {
            line = await reader.ReadLineAsync(timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out waiting for SimHub to execute {actionName}.");
        }

        if (string.IsNullOrWhiteSpace(line))
            throw new IOException(
                "Atomic SimHub Bridge returned an empty action response.");

        var response =
            JsonSerializer.Deserialize<BridgeActionResponse>(line, Json)
            ?? throw new IOException(
                "Atomic SimHub Bridge returned invalid action JSON.");

        if (!response.Ok)
            throw new IOException(
                $"SimHub/AZOM could not trigger {actionName}: " +
                (response.Error ?? "Unknown bridge error."));
    }

    public async Task<string?> SetSettingDirectAsync(
        string propertyName,
        int? targetInt = null,
        bool? targetBool = null,
        int timeoutMs = 4500,
        CancellationToken cancellationToken = default)
    {
        await DirectWriteGate.WaitAsync(cancellationToken);

        try
        {
            int remaining;

            lock (DirectWriteTimingLock)
            {
                long now = Environment.TickCount64;
                long elapsed = now - _lastDirectWriteTick;

                remaining =
                    _lastDirectWriteTick == 0
                        ? 0
                        : Math.Max(
                            0,
                            DirectWriteMinGapMs - (int)Math.Min(elapsed, int.MaxValue));
            }

            if (remaining > 0)
                await Task.Delay(remaining, cancellationToken);

            var method =
                await SetSettingDirectCoreAsync(
                    propertyName,
                    targetInt,
                    targetBool,
                    timeoutMs,
                    cancellationToken);

            lock (DirectWriteTimingLock)
                _lastDirectWriteTick = Environment.TickCount64;

            return method;
        }
        finally
        {
            DirectWriteGate.Release();
        }
    }

    private async Task<string?> SetSettingDirectCoreAsync(
        string propertyName,
        int? targetInt = null,
        bool? targetBool = null,
        int timeoutMs = 4500,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(propertyName) ||
            !propertyName.StartsWith("AZOM.", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Only AZOM.* properties can use the direct compatibility fallback.",
                nameof(propertyName));
        }

        if (!targetInt.HasValue &&
            !targetBool.HasValue)
        {
            throw new ArgumentException(
                "A numeric or boolean target is required.");
        }

        using var pipe =
            new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(timeoutMs);

        try
        {
            await pipe.ConnectAsync(timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Timed out connecting to the Atomic SimHub Bridge for a direct AZOM setting.");
        }

        using var writer =
            new StreamWriter(
                pipe,
                new UTF8Encoding(false),
                1024,
                true)
            {
                AutoFlush = true
            };

        using var reader =
            new StreamReader(
                pipe,
                Encoding.UTF8,
                true,
                1024,
                true);

        await writer.WriteLineAsync(
            JsonSerializer.Serialize(
                new
                {
                    command = "setSettingDirect",
                    propertyName,
                    targetInt,
                    targetBool
                }));

        string? line;

        try
        {
            line =
                await reader.ReadLineAsync(
                    timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out waiting for direct AZOM write of {propertyName}.");
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            throw new IOException(
                "Atomic SimHub Bridge returned an empty direct-write response.");
        }

        var response =
            JsonSerializer.Deserialize<BridgeDirectResponse>(
                line,
                Json)
            ?? throw new IOException(
                "Atomic SimHub Bridge returned invalid direct-write JSON.");

        if (!response.Ok)
        {
            throw new IOException(
                $"AZOM direct compatibility write failed for {propertyName}: " +
                (response.Error ?? "Unknown bridge error."));
        }

        return response.Method;
    }

    private sealed class BridgeResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public AzomLiveSnapshot? Snapshot { get; set; }
    }

    private sealed class BridgeActionResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Action { get; set; }
        public string? BridgeVersion { get; set; }
    }

    private sealed class BridgeDirectResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Method { get; set; }
        public string? PropertyName { get; set; }
        public bool Suppressed { get; set; }
        public string? BridgeVersion { get; set; }
    }
}
