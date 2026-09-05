using System.Collections.Concurrent;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Debounce safety layer for interactive AZOM editing.
///
/// This is intentionally separate from explicit Apply batches.
///
/// Interactive requests must remain unchanged for a minimum quiet period
/// before they are handed to the guarded bridge write path. If a newer value
/// for the same scoped property arrives during that period, the older request
/// is superseded and never reaches the bridge.
///
/// Once a request has been handed to AzomBridgeClient, it is considered an
/// active guarded write attempt and is allowed to complete normally.
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

        // Yield once before the final handoff check. This gives a value
        // arriving on the same synchronization turn an opportunity to
        // supersede this request before it enters the bridge write path.
        await Task.Yield();

        cancellationToken.ThrowIfCancellationRequested();

        if (!IsCurrentRequest(
                key,
                version))
        {
            return Superseded(
                normalizedProperty,
                "ADT debounce: superseded immediately before bridge handoff");
        }

        // From this point onward the request has entered the guarded bridge
        // write path. A later interactive value should queue behind/supersede
        // future pending work rather than trying to interrupt an in-flight
        // wheelbase operation.
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
