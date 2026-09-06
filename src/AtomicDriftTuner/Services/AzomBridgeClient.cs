using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Raised when ADT has already handed a mutating request to the SimHub bridge
/// but loses certainty about whether that request executed.
///
/// Callers must treat this as a fail-closed condition: do not immediately send
/// another fallback write/action for the same operation.
/// </summary>
public sealed class AzomBridgeUncertainOperationException : IOException
{
    public string Operation { get; }

    public string AzomName { get; }

    public AzomBridgeUncertainOperationException(
        string operation,
        string azomName,
        string message,
        Exception? innerException = null)
        : base(
            message,
            innerException)
    {
        Operation =
            operation;

        AzomName =
            azomName;
    }
}

public sealed class AzomBridgeClient
{
    private const int DefaultSnapshotTimeoutMs =
        2500;

    private const int DefaultActionTimeoutMs =
        4000;

    private const int DefaultDirectWriteTimeoutMs =
        4500;

    private const int MaxSnapshotResponseChars =
        256_000;

    private const int MaxActionResponseChars =
        64_000;

    private const int MaxDirectWriteResponseChars =
        64_000;

    private const int MaxPipeNameLength =
        128;

    private const int MaxAzomNameLength =
        256;

    private const int MaxBridgeVersionLength =
        64;

    private const int DirectWriteMinGapMs =
        120;

    private const string AllowedPipePrefix =
        "AtomicDriftTuner.";

    private static readonly TimeSpan MaximumSnapshotAge =
        TimeSpan.FromSeconds(
            5);

    private static readonly TimeSpan MaximumSnapshotFutureSkew =
        TimeSpan.FromSeconds(
            5);

    private static readonly JsonSerializerOptions Json =
        new()
        {
            PropertyNameCaseInsensitive =
                true,

            MaxDepth =
                64
        };

    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier:
                false,
            throwOnInvalidBytes:
                true);

    private sealed record NumericRule(
        int Minimum,
        int Maximum)
    {
        public bool Contains(
            int value) =>
            value >=
                Minimum &&
            value <=
                Maximum;
    }

    // This is intentionally repeated at the bridge-client boundary even though
    // AzomLiveController validates the same contract. The client is a lower
    // hardware-adjacent boundary and must reject a bad direct caller on its own.
    private static readonly IReadOnlyDictionary<string, NumericRule>
        DirectNumericRules =
        new Dictionary<string, NumericRule>(
            StringComparer.Ordinal)
        {
            ["AZOM.FfbStrength"] =
                new(0, 100),

            ["AZOM.Torque"] =
                new(50, 100),

            ["AZOM.Rotation"] =
                new(60, 2700),

            ["AZOM.WheelSpeedLimit"] =
                new(0, 200),

            ["AZOM.Interpolation"] =
                new(0, 10),

            ["AZOM.GearshiftVibration"] =
                new(0, 5),

            ["AZOM.Damper"] =
                new(0, 100),

            ["AZOM.Friction"] =
                new(0, 100),

            ["AZOM.Inertia"] =
                new(100, 500),

            ["AZOM.Spring"] =
                new(0, 100),

            ["AZOM.GameDamper"] =
                new(0, 100),

            ["AZOM.GameFriction"] =
                new(0, 100),

            ["AZOM.GameInertia"] =
                new(0, 100),

            ["AZOM.GameSpring"] =
                new(0, 100),

            ["AZOM.NaturalInertia"] =
                new(100, 4000),

            ["AZOM.SoftLimitStiffness"] =
                new(1, 10),

            ["AZOM.SpeedDamping"] =
                new(0, 100),

            ["AZOM.SpeedDampingPoint"] =
                new(0, 400),

            ["AZOM.RoadSensitivity"] =
                new(0, 10),

            ["AZOM.Equalizer1"] =
                new(0, 400),

            ["AZOM.Equalizer2"] =
                new(0, 400),

            ["AZOM.Equalizer3"] =
                new(0, 400),

            ["AZOM.Equalizer4"] =
                new(0, 400),

            ["AZOM.Equalizer5"] =
                new(0, 400),

            ["AZOM.Equalizer6"] =
                new(0, 400),

            ["AZOM.Equalizer7"] =
                new(0, 400),

            ["AZOM.Equalizer8"] =
                new(0, 400),

            ["AZOM.Equalizer9"] =
                new(0, 400),

            ["AZOM.Equalizer10"] =
                new(0, 400),

            ["AZOM.FfbCurveX1"] =
                new(0, 100),

            ["AZOM.FfbCurveX2"] =
                new(0, 100),

            ["AZOM.FfbCurveX3"] =
                new(0, 100),

            ["AZOM.FfbCurveX4"] =
                new(0, 100),

            ["AZOM.FfbCurveY1"] =
                new(0, 100),

            ["AZOM.FfbCurveY2"] =
                new(0, 100),

            ["AZOM.FfbCurveY3"] =
                new(0, 100),

            ["AZOM.FfbCurveY4"] =
                new(0, 100),

            ["AZOM.FfbCurveY5"] =
                new(0, 100)
        };

    private static readonly HashSet<string>
        DirectToggleProperties =
        new(
            StringComparer.Ordinal)
        {
            "AZOM.Protection",
            "AZOM.SoftLimitRetain",
            "AZOM.FfbReverse",
            "AZOM.BaseStatusLed",
            "AZOM.Bluetooth",
            "AZOM.WorkMode"
        };

    private static readonly HashSet<string>
        ApprovedToggleActions =
        new(
            StringComparer.Ordinal)
        {
            "AZOM.ProtectionOn",
            "AZOM.ProtectionOff",
            "AZOM.SoftLimitRetainOn",
            "AZOM.SoftLimitRetainOff",
            "AZOM.FfbReverseOn",
            "AZOM.FfbReverseOff",
            "AZOM.BaseStatusLedOn",
            "AZOM.BaseStatusLedOff",
            "AZOM.BluetoothOn",
            "AZOM.BluetoothOff",
            "AZOM.WorkModeOn",
            "AZOM.WorkModeOff"
        };

    // Mutating bridge requests are single-flight across every client instance.
    // The higher-level AzomLiveController still owns the full Apply/Revert batch
    // gate; this is an additional transport-level guard for direct callers.
    private static readonly SemaphoreSlim BridgeMutationGate =
        new(
            1,
            1);

    private static readonly object DirectWriteTimingLock =
        new();

    private static long _lastDirectWriteTick;

    private readonly string _pipeName;

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

        await WriteRequestLineAsync(
            writer,
            "{\"command\":\"snapshot\"}",
            timeout.Token,
            cancellationToken,
            "sending the ADT SimHub Bridge snapshot request");

        var line =
            await ReadRequiredResponseLineAsync(
                reader,
                MaxSnapshotResponseChars,
                timeout.Token,
                cancellationToken,
                "waiting for the ADT SimHub Bridge snapshot response");

        BridgeSnapshotResponse response;

        try
        {
            response =
                JsonSerializer.Deserialize<BridgeSnapshotResponse>(
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

        if (
            response.Snapshot.ValueKind !=
            JsonValueKind.Object)
        {
            throw new IOException(
                "ADT SimHub Bridge did not return a valid AZOM snapshot object.");
        }

        var bridgeVersion =
            ReadRequiredStringProperty(
                response.Snapshot,
                "bridgeVersion",
                MaxBridgeVersionLength);

        if (string.Equals(
                bridgeVersion,
                "unknown",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "ADT SimHub Bridge snapshot did not identify a valid bridge version.");
        }

        var capturedUtc =
            ReadRequiredDateTimeProperty(
                response.Snapshot,
                "capturedUtc");

        var propertyNamespace =
            ReadRequiredStringProperty(
                response.Snapshot,
                "propertyNamespace",
                MaxAzomNameLength);

        _ =
            ReadRequiredBooleanProperty(
                response.Snapshot,
                "settingsReadable");

        AzomLiveSnapshot snapshot;

        try
        {
            snapshot =
                response.Snapshot.Deserialize<AzomLiveSnapshot>(
                    Json)
                ?? throw new IOException(
                    "ADT SimHub Bridge returned an empty AZOM snapshot.");
        }
        catch (JsonException ex)
        {
            throw new IOException(
                "ADT SimHub Bridge returned an AZOM snapshot that ADT could not deserialize.",
                ex);
        }

        // Do not allow model defaults to make omitted protocol identity fields
        // appear valid. These values came from required JSON fields above.
        snapshot.BridgeVersion =
            bridgeVersion;

        snapshot.CapturedUtc =
            capturedUtc;

        snapshot.PropertyNamespace =
            propertyNamespace;

        ValidateSnapshotFreshness(
            snapshot);

        return snapshot;
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

        ValidateApprovedAction(
            normalizedAction);

        ValidateTimeout(
            timeoutMs);

        await BridgeMutationGate.WaitAsync(
            cancellationToken);

        try
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
                        command =
                            "triggerAction",

                        actionName =
                            normalizedAction
                    });

            try
            {
                // From this point forward a transport/protocol failure may mean
                // that SimHub still has the requested action queued.
                await WriteRequestLineAsync(
                    writer,
                    request,
                    timeout.Token,
                    cancellationToken,
                    $"sending SimHub action {normalizedAction}");

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
                    if (IndicatesBridgeQueueTimeout(
                            response.Error))
                    {
                        throw CreateUncertain(
                            "action",
                            normalizedAction,
                            $"ADT lost certainty about whether SimHub will still execute {normalizedAction} after the bridge timed out waiting for it.");
                    }

                    throw new BridgeRejectedOperationException(
                        CreateBridgeErrorMessage(
                            $"SimHub/AZOM could not trigger {normalizedAction}.",
                            response.Error));
                }

                ValidateBridgeVersion(
                    response.BridgeVersion);

                if (!string.Equals(
                        response.Action,
                        normalizedAction,
                        StringComparison.Ordinal))
                {
                    throw CreateUncertain(
                        "action",
                        normalizedAction,
                        "ADT SimHub Bridge did not acknowledge the exact action ADT requested.");
                }
            }
            catch (OperationCanceledException)
                when (
                    cancellationToken.IsCancellationRequested)
            {
                // Caller cancellation after handoff is itself an uncertain
                // hardware-adjacent outcome, but preserving cancellation
                // semantics stops all higher-level fallback work immediately.
                throw;
            }
            catch (BridgeRejectedOperationException ex)
            {
                throw new IOException(
                    ex.Message,
                    ex);
            }
            catch (AzomBridgeUncertainOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateUncertain(
                    "action",
                    normalizedAction,
                    $"ADT lost certainty about whether SimHub executed {normalizedAction}. No additional fallback action should be sent from this result.",
                    ex);
            }
        }
        finally
        {
            BridgeMutationGate.Release();
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

        ValidateDirectWriteContract(
            normalizedProperty,
            targetInt,
            targetBool);

        ValidateTimeout(
            timeoutMs);

        await BridgeMutationGate.WaitAsync(
            cancellationToken);

        var attemptedWrite =
            false;

        try
        {
            var remaining =
                GetRemainingDirectWriteDelay();

            if (remaining >
                0)
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
            // Space bridge write attempts, not only successful writes. If a
            // write ended ambiguously, immediately sending another write would
            // be the unsafe case.
            if (attemptedWrite)
            {
                lock (DirectWriteTimingLock)
                {
                    _lastDirectWriteTick =
                        Environment.TickCount64;
                }
            }

            BridgeMutationGate.Release();
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
                    command =
                        "setSettingDirect",

                    propertyName,

                    targetInt,

                    targetBool
                });

        try
        {
            // From this point onward a transport/protocol failure is ambiguous:
            // the bridge may already have queued or executed this exact write.
            await WriteRequestLineAsync(
                writer,
                request,
                timeout.Token,
                cancellationToken,
                $"sending direct AZOM write of {propertyName}");

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
                if (IndicatesBridgeQueueTimeout(
                        response.Error))
                {
                    throw CreateUncertain(
                        "direct write",
                        propertyName,
                        $"ADT lost certainty about whether the bridge will still execute {propertyName} after timing out.");
                }

                // A normal bridge rejection/error is allowed to flow back to the
                // higher-level controller, which will re-read live state before
                // deciding whether a documented fallback is safe.
                throw new BridgeRejectedOperationException(
                    CreateBridgeErrorMessage(
                        $"AZOM direct compatibility write failed for {propertyName}.",
                        response.Error));
            }

            ValidateBridgeVersion(
                response.BridgeVersion);

            if (!string.Equals(
                    response.PropertyName,
                    propertyName,
                    StringComparison.Ordinal))
            {
                throw CreateUncertain(
                    "direct write",
                    propertyName,
                    "ADT SimHub Bridge did not acknowledge the exact AZOM property ADT requested.");
            }

            if (response.Suppressed)
            {
                // Bridge suppression means the bridge observed that this exact
                // target was already live and deliberately performed no write.
                return
                    "ADT bridge: duplicate target already live; write suppressed";
            }

            if (string.IsNullOrWhiteSpace(
                    response.Method))
            {
                throw CreateUncertain(
                    "direct write",
                    propertyName,
                    "ADT SimHub Bridge reported a successful direct write without identifying the commit method.");
            }

            return
                NormalizeBridgeMethod(
                    response.Method);
        }
        catch (OperationCanceledException)
            when (
                cancellationToken.IsCancellationRequested)
        {
            // Do not translate caller cancellation into a normal bridge failure;
            // higher layers use cancellation specifically to stop all fallback.
            throw;
        }
        catch (BridgeRejectedOperationException ex)
        {
            throw new IOException(
                ex.Message,
                ex);
        }
        catch (AzomBridgeUncertainOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateUncertain(
                "direct write",
                propertyName,
                $"ADT lost certainty about whether the bridge executed the direct write for {propertyName}. No additional fallback write should be sent from this result.",
                ex);
        }
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
            StrictUtf8,
            1024,
            leaveOpen:
                true)
        {
            AutoFlush =
                true
        };
    }

    private static StreamReader CreateReader(
        Stream stream)
    {
        return new StreamReader(
            stream,
            StrictUtf8,
            detectEncodingFromByteOrderMarks:
                false,
            bufferSize:
                4096,
            leaveOpen:
                true);
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
                "Restart SimHub and ADT at the same Windows privilege level, then try again.",
                ex);
        }
        catch (OperationCanceledException)
            when (!callerToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out while {operation}.");
        }
    }

    private static async Task WriteRequestLineAsync(
        StreamWriter writer,
        string request,
        CancellationToken timeoutToken,
        CancellationToken callerToken,
        string operation)
    {
        try
        {
            await writer.WriteLineAsync(
                request.AsMemory(),
                timeoutToken);
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
        catch (DecoderFallbackException ex)
        {
            throw new IOException(
                "ADT SimHub Bridge returned invalid UTF-8 data.",
                ex);
        }

        if (string.IsNullOrWhiteSpace(
                line))
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
        if (maxChars <=
            0)
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
            new char[1024];

        while (true)
        {
            var read =
                await reader.ReadAsync(
                    buffer.AsMemory(
                        0,
                        buffer.Length),
                    cancellationToken);

            if (read ==
                0)
            {
                return builder.Length ==
                    0
                    ? null
                    : builder.ToString();
            }

            var newlineIndex =
                Array.IndexOf(
                    buffer,
                    '\n',
                    0,
                    read);

            var appendCount =
                newlineIndex >=
                    0
                    ? newlineIndex
                    : read;

            if (
                builder.Length +
                appendCount >
                maxChars)
            {
                throw new IOException(
                    $"ADT SimHub Bridge response exceeded the supported {maxChars:N0}-character limit.");
            }

            builder.Append(
                buffer,
                0,
                appendCount);

            if (newlineIndex >=
                0)
            {
                if (
                    builder.Length >
                        0 &&
                    builder[^1] ==
                        '\r')
                {
                    builder.Length--;
                }

                return builder.ToString();
            }
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
            if (_lastDirectWriteTick ==
                0)
            {
                return 0;
            }

            var now =
                Environment.TickCount64;

            var elapsed =
                now -
                _lastDirectWriteTick;

            if (elapsed >=
                DirectWriteMinGapMs)
            {
                return 0;
            }

            if (elapsed <
                0)
            {
                return
                    DirectWriteMinGapMs;
            }

            return
                DirectWriteMinGapMs -
                (int)elapsed;
        }
    }

    private static void ValidateDirectWriteContract(
        string propertyName,
        int? targetInt,
        bool? targetBool)
    {
        if (DirectNumericRules.TryGetValue(
                propertyName,
                out var numericRule))
        {
            if (
                !targetInt.HasValue ||
                targetBool.HasValue)
            {
                throw new ArgumentException(
                    $"{propertyName} requires exactly one numeric target.");
            }

            if (!numericRule.Contains(
                    targetInt.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetInt),
                    targetInt.Value,
                    $"ADT refused {propertyName}={targetInt.Value}. Supported bridge-client range is {numericRule.Minimum}..{numericRule.Maximum}.");
            }

            return;
        }

        if (DirectToggleProperties.Contains(
                propertyName))
        {
            if (
                !targetBool.HasValue ||
                targetInt.HasValue)
            {
                throw new ArgumentException(
                    $"{propertyName} requires exactly one boolean target.");
            }

            return;
        }

        throw new ArgumentException(
            $"ADT does not allow direct bridge writes for AZOM property '{propertyName}'.",
            nameof(propertyName));
    }

    private static void ValidateApprovedAction(
        string actionName)
    {
        if (ApprovedToggleActions.Contains(
                actionName))
        {
            return;
        }

        string[] suffixes =
        [
            "UpCoarse",
            "DownCoarse",
            "Up",
            "Down"
        ];

        foreach (var suffix in suffixes)
        {
            if (!actionName.EndsWith(
                    suffix,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var propertyName =
                actionName[
                    ..^suffix.Length];

            if (DirectNumericRules.ContainsKey(
                    propertyName))
            {
                return;
            }
        }

        throw new ArgumentException(
            $"ADT does not allow bridge action '{actionName}'.",
            nameof(actionName));
    }

    private static string ValidateAzomName(
        string value,
        string parameterName,
        string kind)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                $"AZOM {kind} name is required.",
                parameterName);
        }

        var normalized =
            value.Trim();

        if (
            normalized.Length >
                MaxAzomNameLength ||
            !normalized.StartsWith(
                "AZOM.",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Only supported AZOM.* {kind} names are allowed.",
                parameterName);
        }

        foreach (var character in normalized)
        {
            if (
                !char.IsLetterOrDigit(
                    character) &&
                character !=
                    '.' &&
                character !=
                    '_' &&
                character !=
                    '-')
            {
                throw new ArgumentException(
                    $"AZOM {kind} name contains invalid characters.",
                    parameterName);
            }
        }

        return normalized;
    }

    private static string NormalizePipeName(
        string pipeName)
    {
        if (string.IsNullOrWhiteSpace(
                pipeName))
        {
            throw new ArgumentException(
                "ADT SimHub Bridge pipe name is required.",
                nameof(pipeName));
        }

        var normalized =
            pipeName.Trim();

        if (
            normalized.Length >
                MaxPipeNameLength ||
            !normalized.StartsWith(
                AllowedPipePrefix,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "ADT only connects to the AtomicDriftTuner.* local bridge pipe namespace.",
                nameof(pipeName));
        }

        foreach (var character in normalized)
        {
            if (
                !char.IsLetterOrDigit(
                    character) &&
                character !=
                    '.' &&
                character !=
                    '_' &&
                character !=
                    '-')
            {
                throw new ArgumentException(
                    "ADT SimHub Bridge pipe name contains invalid characters.",
                    nameof(pipeName));
            }
        }

        return normalized;
    }

    private static void ValidateTimeout(
        int timeoutMs)
    {
        if (timeoutMs is
            < 100 or
            > 60_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutMs),
                "Bridge timeout must be between 100 ms and 60 seconds.");
        }
    }

    private static void ValidateSnapshotFreshness(
        AzomLiveSnapshot snapshot)
    {
        var capturedUtc =
            snapshot.CapturedUtc.Kind ==
                DateTimeKind.Utc
                ? snapshot.CapturedUtc
                : snapshot.CapturedUtc.ToUniversalTime();

        var now =
            DateTime.UtcNow;

        if (
            capturedUtc <
                now -
                MaximumSnapshotAge)
        {
            throw new IOException(
                $"ADT SimHub Bridge returned a stale AZOM snapshot captured at {capturedUtc:O}. Wait for SimHub to refresh live data and try again.");
        }

        if (
            capturedUtc >
                now +
                MaximumSnapshotFutureSkew)
        {
            throw new IOException(
                "ADT SimHub Bridge returned an AZOM snapshot with an invalid future timestamp.");
        }
    }

    private static string ValidateBridgeVersion(
        string? version)
    {
        if (
            string.IsNullOrWhiteSpace(
                version))
        {
            throw new IOException(
                "ADT SimHub Bridge response did not identify the bridge version.");
        }

        var normalized =
            version.Trim();

        if (
            normalized.Length >
                MaxBridgeVersionLength ||
            normalized.IndexOfAny(
                ['\r', '\n', '\0']) >=
                0)
        {
            throw new IOException(
                "ADT SimHub Bridge returned an invalid bridge version identifier.");
        }

        return normalized;
    }

    private static string ReadRequiredStringProperty(
        JsonElement element,
        string name,
        int maxLength)
    {
        if (
            !TryGetPropertyIgnoreCase(
                element,
                name,
                out var property) ||
            property.ValueKind !=
                JsonValueKind.String)
        {
            throw new IOException(
                $"ADT SimHub Bridge snapshot is missing required field '{name}'.");
        }

        var value =
            property.GetString()
            ?? string.Empty;

        value =
            value.Trim();

        if (
            value.Length ==
                0 ||
            value.Length >
                maxLength ||
            value.IndexOfAny(
                ['\r', '\n', '\0']) >=
                0)
        {
            throw new IOException(
                $"ADT SimHub Bridge snapshot field '{name}' is invalid.");
        }

        return value;
    }

    private static DateTime ReadRequiredDateTimeProperty(
        JsonElement element,
        string name)
    {
        if (
            !TryGetPropertyIgnoreCase(
                element,
                name,
                out var property) ||
            property.ValueKind !=
                JsonValueKind.String ||
            !property.TryGetDateTime(
                out var value))
        {
            throw new IOException(
                $"ADT SimHub Bridge snapshot is missing or has invalid required field '{name}'.");
        }

        return value;
    }

    private static bool ReadRequiredBooleanProperty(
        JsonElement element,
        string name)
    {
        if (
            !TryGetPropertyIgnoreCase(
                element,
                name,
                out var property) ||
            property.ValueKind is not
                JsonValueKind.True and not
                JsonValueKind.False)
        {
            throw new IOException(
                $"ADT SimHub Bridge snapshot is missing or has invalid required field '{name}'.");
        }

        return property.GetBoolean();
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        foreach (var property in
                 element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                value =
                    property.Value;

                return true;
            }
        }

        value =
            default;

        return false;
    }

    private static bool IndicatesBridgeQueueTimeout(
        string? bridgeError)
    {
        if (string.IsNullOrWhiteSpace(
                bridgeError))
        {
            return false;
        }

        return bridgeError.Contains(
            "Timed out waiting",
            StringComparison.OrdinalIgnoreCase);
    }

    private static AzomBridgeUncertainOperationException CreateUncertain(
        string operation,
        string azomName,
        string message,
        Exception? innerException = null)
    {
        return new AzomBridgeUncertainOperationException(
            operation,
            azomName,
            message,
            innerException);
    }

    private static string NormalizeBridgeMethod(
        string method)
    {
        var normalized =
            method
                .Replace(
                    '\r',
                    ' ')
                .Replace(
                    '\n',
                    ' ')
                .Trim();

        if (normalized.Length >
            500)
        {
            normalized =
                normalized[..500] +
                "…";
        }

        return normalized;
    }

    private static string CreateBridgeErrorMessage(
        string prefix,
        string? bridgeError)
    {
        if (string.IsNullOrWhiteSpace(
                bridgeError))
        {
            return prefix;
        }

        var cleaned =
            bridgeError
                .Replace(
                    '\r',
                    ' ')
                .Replace(
                    '\n',
                    ' ')
                .Trim();

        if (cleaned.Length >
            1000)
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

    private sealed class BridgeRejectedOperationException : Exception
    {
        public BridgeRejectedOperationException(
            string message)
            : base(
                message)
        {
        }
    }

    private sealed class BridgeSnapshotResponse
    {
        public bool Ok { get; set; }

        public string? Error { get; set; }

        public JsonElement Snapshot { get; set; }
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
