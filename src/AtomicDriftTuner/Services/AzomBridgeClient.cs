using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AzomBridgeClient
{
    private const int DefaultSnapshotTimeoutMs = 2500;
    private const int DefaultActionTimeoutMs = 4000;
    private const int DefaultDirectWriteTimeoutMs = 4500;

    private const int MaxSnapshotResponseChars = 256_000;
    private const int MaxActionResponseChars = 64_000;
    private const int MaxDirectWriteResponseChars = 64_000;

    private const int MaxPipeNameLength = 256;
    private const int MaxAzomNameLength = 256;

    private const int DirectWriteMinGapMs = 120;

    private readonly string _pipeName;

    private static readonly JsonSerializerOptions Json =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    // Direct AZOM writes are process-wide single-flight.
    //
    // Even when callers already serialize an Apply batch, this prevents
    // multiple ADT windows/tasks from overlapping bridge-level write
    // requests in the same process.
    private static readonly SemaphoreSlim DirectWriteGate =
        new(1, 1);

    private static readonly object DirectWriteTimingLock =
        new();

    private static long _lastDirectWriteTick;

    public AzomBridgeClient(
        string pipeName)
    {
        _pipeName =
            NormalizePipeName(
                pipeName);
    }

    public async Task<AzomLiveSnapshot> ReadSnapshotAsync(
        int timeoutMs = DefaultSnapshotTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ValidateTimeout(
            timeoutMs);

        using var timeout =
            CreateTimeout(
                timeoutMs,
                cancellationToken);

        using var pipe =
            CreatePipe();

        await ConnectAsync(
            pipe,
            timeout.Token,
            cancellationToken,
            "connecting to the ADT SimHub Bridge for a live AZOM snapshot");

        using var writer =
            CreateWriter(
                pipe);

        using var reader =
            CreateReader(
                pipe);

        await writer.WriteLineAsync(
            "{\"command\":\"snapshot\"}");

        var line =
            await ReadRequiredResponseLineAsync(
                reader,
                MaxSnapshotResponseChars,
                timeout.Token,
                cancellationToken,
                "waiting for the ADT SimHub Bridge snapshot response");

        BridgeResponse response;

        try
        {
            response =
                JsonSerializer.Deserialize<BridgeResponse>(
                    line,
                    Json)
                ?? throw new IOException(
                    "ADT SimHub Bridge returned an empty snapshot object.");
        }
        catch (JsonException ex)
        {
            throw new IOException(
                "ADT SimHub Bridge returned invalid snapshot JSON.",
                ex);
        }

        if (!response.Ok)
        {
            throw new IOException(
                CreateBridgeErrorMessage(
                    "ADT SimHub Bridge reported a snapshot error.",
                    response.Error));
        }

        return response.Snapshot
            ?? throw new IOException(
                "ADT SimHub Bridge did not return an AZOM snapshot.");
    }

    public async Task TriggerActionAsync(
        string actionName,
        int timeoutMs = DefaultActionTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var normalizedAction =
            ValidateAzomName(
                actionName,
                nameof(actionName),
                "action");

        ValidateTimeout(
            timeoutMs);

        using var timeout =
            CreateTimeout(
                timeoutMs,
                cancellationToken);

        using var pipe =
            CreatePipe();

        await ConnectAsync(
            pipe,
            timeout.Token,
            cancellationToken,
            $"connecting to the ADT SimHub Bridge for action {normalizedAction}");

        using var writer =
            CreateWriter(
                pipe);

        using var reader =
            CreateReader(
                pipe);

        var request =
            JsonSerializer.Serialize(
                new
                {
                    command = "triggerAction",
                    actionName = normalizedAction
                });

        await writer.WriteLineAsync(
            request);

        var line =
            await ReadRequiredResponseLineAsync(
                reader,
                MaxActionResponseChars,
                timeout.Token,
                cancellationToken,
                $"waiting for SimHub to execute {normalizedAction}");

        BridgeActionResponse response;

        try
        {
            response =
                JsonSerializer.Deserialize<BridgeActionResponse>(
                    line,
                    Json)
                ?? throw new IOException(
                    "ADT SimHub Bridge returned an empty action response.");
        }
        catch (JsonException ex)
        {
            throw new IOException(
                "ADT SimHub Bridge returned invalid action JSON.",
                ex);
        }

        if (!response.Ok)
        {
            throw new IOException(
                CreateBridgeErrorMessage(
                    $"SimHub/AZOM could not trigger {normalizedAction}.",
                    response.Error));
        }

        if (
            !string.IsNullOrWhiteSpace(response.Action) &&
            !string.Equals(
                response.Action,
                normalizedAction,
                StringComparison.Ordinal))
        {
            throw new IOException(
                "ADT SimHub Bridge acknowledged a different action than the one ADT requested.");
        }
    }

    public async Task<string?> SetSettingDirectAsync(
        string propertyName,
        int? targetInt = null,
        bool? targetBool = null,
        int timeoutMs = DefaultDirectWriteTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        var normalizedProperty =
            ValidateAzomName(
                propertyName,
                nameof(propertyName),
                "property");

        ValidateDirectTarget(
            targetInt,
            targetBool);

        ValidateTimeout(
            timeoutMs);

        await DirectWriteGate.WaitAsync(
            cancellationToken);

        var attemptedWrite =
            false;

        try
        {
            var remaining =
                GetRemainingDirectWriteDelay();

            if (remaining > 0)
            {
                await Task.Delay(
                    remaining,
                    cancellationToken);
            }

            attemptedWrite =
                true;

            return await SetSettingDirectCoreAsync(
                normalizedProperty,
                targetInt,
                targetBool,
                timeoutMs,
                cancellationToken);
        }
        finally
        {
            // Space bridge write attempts, not only successful writes.
            //
            // If a write timed out or failed after reaching the bridge,
            // immediately sending another write would be the unsafe case.
            if (attemptedWrite)
            {
                lock (DirectWriteTimingLock)
                {
                    _lastDirectWriteTick =
                        Environment.TickCount64;
                }
            }

            DirectWriteGate.Release();
        }
    }

    private async Task<string?> SetSettingDirectCoreAsync(
        string propertyName,
        int? targetInt,
        bool? targetBool,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CreateTimeout(
                timeoutMs,
                cancellationToken);

        using var pipe =
            CreatePipe();

        await ConnectAsync(
            pipe,
            timeout.Token,
            cancellationToken,
            $"connecting to the ADT SimHub Bridge for direct AZOM write {propertyName}");

        using var writer =
            CreateWriter(
                pipe);

        using var reader =
            CreateReader(
                pipe);

        var request =
            JsonSerializer.Serialize(
                new
                {
                    command = "setSettingDirect",
                    propertyName,
                    targetInt,
                    targetBool
                });

        await writer.WriteLineAsync(
            request);

        var line =
            await ReadRequiredResponseLineAsync(
                reader,
                MaxDirectWriteResponseChars,
                timeout.Token,
                cancellationToken,
                $"waiting for direct AZOM write of {propertyName}");

        BridgeDirectResponse response;

        try
        {
            response =
                JsonSerializer.Deserialize<BridgeDirectResponse>(
                    line,
                    Json)
                ?? throw new IOException(
                    "ADT SimHub Bridge returned an empty direct-write response.");
        }
        catch (JsonException ex)
        {
            throw new IOException(
                "ADT SimHub Bridge returned invalid direct-write JSON.",
                ex);
        }

        if (!response.Ok)
        {
            throw new IOException(
                CreateBridgeErrorMessage(
                    $"AZOM direct compatibility write failed for {propertyName}.",
                    response.Error));
        }

        if (
            !string.IsNullOrWhiteSpace(response.PropertyName) &&
            !string.Equals(
                response.PropertyName,
                propertyName,
                StringComparison.Ordinal))
        {
            throw new IOException(
                "ADT SimHub Bridge acknowledged a different AZOM property than the one ADT requested.");
        }

        if (response.Suppressed)
        {
            throw new IOException(
                $"ADT SimHub Bridge suppressed the direct write for {propertyName}; no setting change was confirmed.");
        }

        return response.Method;
    }

    private NamedPipeClientStream CreatePipe()
    {
        return new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
    }

    private static StreamWriter CreateWriter(
        Stream stream)
    {
        return new StreamWriter(
            stream,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false),
            1024,
            leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    private static StreamReader CreateReader(
        Stream stream)
    {
        return new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
    }

    private static async Task ConnectAsync(
        NamedPipeClientStream pipe,
        CancellationToken timeoutToken,
        CancellationToken callerToken,
        string operation)
    {
        try
        {
            await pipe.ConnectAsync(
                timeoutToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                "Windows denied access to the ADT SimHub Bridge named pipe. " +
                "This can happen when SimHub and Atomic Drift Tuner are running at different Windows privilege levels. " +
                "Restart SimHub and ADT at the same elevation level, then try again.",
                ex);
        }
        catch (OperationCanceledException)
            when (!callerToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out while {operation}.");
        }
    }

    private static async Task<string> ReadRequiredResponseLineAsync(
        StreamReader reader,
        int maxChars,
        CancellationToken timeoutToken,
        CancellationToken callerToken,
        string operation)
    {
        string? line;

        try
        {
            line =
                await ReadBoundedLineAsync(
                    reader,
                    maxChars,
                    timeoutToken);
        }
        catch (OperationCanceledException)
            when (!callerToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out while {operation}.");
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            throw new IOException(
                "ADT SimHub Bridge returned an empty response.");
        }

        return line;
    }

    private static async Task<string?> ReadBoundedLineAsync(
        StreamReader reader,
        int maxChars,
        CancellationToken cancellationToken)
    {
        if (maxChars <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxChars));
        }

        var builder =
            new StringBuilder(
                Math.Min(
                    maxChars,
                    4096));

        var buffer =
            new char[1];

        while (true)
        {
            var read =
                await reader.ReadAsync(
                    buffer.AsMemory(0, 1),
                    cancellationToken);

            if (read == 0)
            {
                return builder.Length == 0
                    ? null
                    : builder.ToString();
            }

            var character =
                buffer[0];

            if (character == '\n')
            {
                if (
                    builder.Length > 0 &&
                    builder[^1] == '\r')
                {
                    builder.Length--;
                }

                return builder.ToString();
            }

            if (builder.Length >= maxChars)
            {
                throw new IOException(
                    $"ADT SimHub Bridge response exceeded the supported {maxChars:N0}-character limit.");
            }

            builder.Append(
                character);
        }
    }

    private static CancellationTokenSource CreateTimeout(
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(
            timeoutMs);

        return timeout;
    }

    private static int GetRemainingDirectWriteDelay()
    {
        lock (DirectWriteTimingLock)
        {
            if (_lastDirectWriteTick == 0)
            {
                return 0;
            }

            var now =
                Environment.TickCount64;

            var elapsed =
                now -
                _lastDirectWriteTick;

            if (elapsed >= DirectWriteMinGapMs)
            {
                return 0;
            }

            if (elapsed < 0)
            {
                // Environment.TickCount64 should not realistically wrap
                // during an ADT process lifetime, but fail safe if it does.
                return DirectWriteMinGapMs;
            }

            return
                DirectWriteMinGapMs -
                (int)elapsed;
        }
    }

    private static void ValidateDirectTarget(
        int? targetInt,
        bool? targetBool)
    {
        if (
            !targetInt.HasValue &&
            !targetBool.HasValue)
        {
            throw new ArgumentException(
                "A numeric or boolean AZOM target is required.");
        }

        if (
            targetInt.HasValue &&
            targetBool.HasValue)
        {
            throw new ArgumentException(
                "A direct AZOM write must specify either a numeric target or a boolean target, not both.");
        }
    }

    private static string ValidateAzomName(
        string value,
        string parameterName,
        string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"AZOM {kind} name is required.",
                parameterName);
        }

        var normalized =
            value.Trim();

        if (normalized.Length > MaxAzomNameLength)
        {
            throw new ArgumentException(
                $"AZOM {kind} name is too long.",
                parameterName);
        }

        if (!normalized.StartsWith(
                "AZOM.",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Only AZOM.* {kind} names are allowed.",
                parameterName);
        }

        if (
            normalized.Contains('\r') ||
            normalized.Contains('\n') ||
            normalized.Contains('\0'))
        {
            throw new ArgumentException(
                $"AZOM {kind} name contains invalid characters.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizePipeName(
        string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException(
                "ADT SimHub Bridge pipe name is required.",
                nameof(pipeName));
        }

        var normalized =
            pipeName.Trim();

        if (
            normalized.Length == 0 ||
            normalized.Length > MaxPipeNameLength)
        {
            throw new ArgumentException(
                $"ADT SimHub Bridge pipe name must be between 1 and {MaxPipeNameLength} characters.",
                nameof(pipeName));
        }

        if (
            normalized.Contains('\r') ||
            normalized.Contains('\n') ||
            normalized.Contains('\0'))
        {
            throw new ArgumentException(
                "ADT SimHub Bridge pipe name contains invalid characters.",
                nameof(pipeName));
        }

        return normalized;
    }

    private static void ValidateTimeout(
        int timeoutMs)
    {
        if (timeoutMs is < 100 or > 60_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutMs),
                "Bridge timeout must be between 100 ms and 60 seconds.");
        }
    }

    private static string CreateBridgeErrorMessage(
        string prefix,
        string? bridgeError)
    {
        if (string.IsNullOrWhiteSpace(bridgeError))
        {
            return prefix;
        }

        var cleaned =
            bridgeError
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();

        if (cleaned.Length > 1000)
        {
            cleaned =
                cleaned[..1000] +
                "…";
        }

        return
            prefix +
            " " +
            cleaned;
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
