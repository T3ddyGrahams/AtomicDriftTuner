using System.Collections.Concurrent;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Debounce safety layer for interactive AZOM editing.
///
/// Interactive requests remain debounce-controlled, but their physical writes
/// share AzomLiveController's process-wide AZOM write gate with explicit
/// Apply/Revert batches.
///
/// A request must remain unchanged for a minimum quiet period before it may
/// enter the guarded bridge write path. A newer value for the same scoped
/// property supersedes the older request. An explicit Apply/Revert batch that
/// starts after the request was queued also supersedes that pending request.
///
/// Once an interactive request has acquired the shared write gate and passed
/// its final supersession checks, it is considered an active guarded write
/// attempt and is allowed to complete normally.
/// </summary>
public sealed class AzomInteractiveWriteService
{
    public const int DefaultDebounceMs = 500;

    private const int MinimumDebounceMs = 250;
    private const int MaximumDebounceMs = 2000;

    private const int MaxPropertyNameLength = 256;
    private const int MaxScopeKeyLength = 128;

    // Shared across service instances so two ADT windows/components using
    // the same scope/property still supersede each other's pending values.
    private static readonly ConcurrentDictionary<string, long> Versions =
        new(StringComparer.Ordinal);

    // Globally unique request IDs prevent version reuse/ABA issues if the
    // implementation later removes completed dictionary entries.
    private static long _nextVersion;

    private readonly AzomBridgeClient _bridge;
    private readonly string _scopeKey;

    public AzomInteractiveWriteService(
        AzomBridgeClient bridge,
        string scopeKey = "default")
    {
        _bridge =
            bridge ??
            throw new ArgumentNullException(
                nameof(bridge));

        _scopeKey =
            NormalizeScopeKey(
                scopeKey);
    }

    public async Task<AzomInteractiveWriteResult> QueueAsync(
        string propertyName,
        int? targetInt = null,
        bool? targetBool = null,
        int debounceMs = DefaultDebounceMs,
        CancellationToken cancellationToken = default)
    {
        var normalizedProperty =
            ValidatePropertyName(
                propertyName);

        ValidateTarget(
            targetInt,
            targetBool);

        debounceMs =
            Math.Clamp(
                debounceMs,
                MinimumDebounceMs,
                MaximumDebounceMs);

        // Capture the explicit-batch generation before registering this
        // interactive request. If an Apply/Revert batch begins after this
        // point, the pending request must not run afterward and overwrite it.
        var batchGeneration =
            AzomLiveController
                .CaptureExplicitBatchGeneration();

        var key =
            CreateVersionKey(
                _scopeKey,
                normalizedProperty);

        var version =
            Interlocked.Increment(
                ref _nextVersion);

        Versions.AddOrUpdate(
            key,
            version,
            (_, _) => version);

        await Task.Delay(
            debounceMs,
            cancellationToken);

        if (!IsCurrentRequest(
                key,
                version))
        {
            return Superseded(
                normalizedProperty,
                $"ADT {debounceMs} ms debounce: superseded by a newer target");
        }

        if (!IsCurrentBatchGeneration(
                batchGeneration))
        {
            return Superseded(
                normalizedProperty,
                "ADT debounce: superseded by a newer explicit Apply/Revert operation");
        }

        // Yield once before the first handoff check. This gives a value
        // arriving on the same synchronization turn an opportunity to
        // supersede this request before it waits for the physical write gate.
        await Task.Yield();

        cancellationToken.ThrowIfCancellationRequested();

        if (!IsCurrentRequest(
                key,
                version))
        {
            return Superseded(
                normalizedProperty,
                "ADT debounce: superseded immediately before write-gate handoff");
        }

        if (!IsCurrentBatchGeneration(
                batchGeneration))
        {
            return Superseded(
                normalizedProperty,
                "ADT debounce: explicit Apply/Revert took priority before write-gate handoff");
        }

        var writeGate =
            await AzomLiveController
                .AcquireLiveWriteGateAsync(
                    cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Re-check after acquiring the shared gate. This closes the race
            // where this request was current when it started waiting, but a
            // newer interactive value or explicit batch won the gate first.
            if (!IsCurrentRequest(
                    key,
                    version))
            {
                return Superseded(
                    normalizedProperty,
                    "ADT debounce: superseded while waiting for the live AZOM write gate");
            }

            if (!IsCurrentBatchGeneration(
                    batchGeneration))
            {
                return Superseded(
                    normalizedProperty,
                    "ADT debounce: explicit Apply/Revert took priority while this request waited");
            }

            // From this point onward the request owns the process-wide live
            // write gate. A later request may supersede future pending work,
            // but it cannot interrupt this in-flight wheelbase operation.
            var method =
                await _bridge.SetSettingDirectAsync(
                    normalizedProperty,
                    targetInt,
                    targetBool,
                    cancellationToken:
                        cancellationToken);

            var wasWritten =
                !IndicatesNoPhysicalWrite(
                    method);

            return new AzomInteractiveWriteResult
            {
                PropertyName =
                    normalizedProperty,

                WasSuperseded =
                    false,

                WasWritten =
                    wasWritten,

                Method =
                    string.IsNullOrWhiteSpace(method)
                        ? "AZOM guarded direct write"
                        : method
            };
        }
        finally
        {
            writeGate.Dispose();
        }
    }

    private static bool IsCurrentRequest(
        string key,
        long version)
    {
        return
            Versions.TryGetValue(
                key,
                out var latest) &&
            latest == version;
    }

    private static bool IsCurrentBatchGeneration(
        long generation)
    {
        return
            AzomLiveController
                .CaptureExplicitBatchGeneration() ==
            generation;
    }

    private static AzomInteractiveWriteResult Superseded(
        string propertyName,
        string method)
    {
        return new AzomInteractiveWriteResult
        {
            PropertyName =
                propertyName,

            WasSuperseded =
                true,

            WasWritten =
                false,

            Method =
                method
        };
    }

    private static bool IndicatesNoPhysicalWrite(
        string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            return false;
        }

        return
            method.Contains(
                "suppressed",
                StringComparison.OrdinalIgnoreCase) ||
            method.Contains(
                "already",
                StringComparison.OrdinalIgnoreCase) ||
            method.Contains(
                "unchanged",
                StringComparison.OrdinalIgnoreCase) ||
            method.Contains(
                "no write",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateTarget(
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
                "An interactive AZOM write must specify either a numeric target or a boolean target, not both.");
        }
    }

    private static string ValidatePropertyName(
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException(
                "AZOM property name is required.",
                nameof(propertyName));
        }

        var normalized =
            propertyName.Trim();

        if (normalized.Length > MaxPropertyNameLength)
        {
            throw new ArgumentException(
                $"AZOM property name exceeds the supported {MaxPropertyNameLength}-character limit.",
                nameof(propertyName));
        }

        if (!normalized.StartsWith(
                "AZOM.",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Only AZOM.* properties can use interactive write debounce.",
                nameof(propertyName));
        }

        if (
            normalized.Contains('\r') ||
            normalized.Contains('\n') ||
            normalized.Contains('\0'))
        {
            throw new ArgumentException(
                "AZOM property name contains invalid characters.",
                nameof(propertyName));
        }

        return normalized;
    }

    private static string NormalizeScopeKey(
        string? scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            return "default";
        }

        var normalized =
            scopeKey.Trim();

        if (normalized.Length > MaxScopeKeyLength)
        {
            throw new ArgumentException(
                $"Interactive AZOM scope key exceeds the supported {MaxScopeKeyLength}-character limit.",
                nameof(scopeKey));
        }

        if (
            normalized.Contains('\r') ||
            normalized.Contains('\n') ||
            normalized.Contains('\0') ||
            normalized.Contains('|'))
        {
            throw new ArgumentException(
                "Interactive AZOM scope key contains invalid characters.",
                nameof(scopeKey));
        }

        return normalized;
    }

    private static string CreateVersionKey(
        string scopeKey,
        string propertyName)
    {
        return
            scopeKey +
            "|" +
            propertyName;
    }
}
