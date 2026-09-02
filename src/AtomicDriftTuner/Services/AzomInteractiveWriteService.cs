using System.Collections.Concurrent;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Safety layer for any future "write while editing" AZOM UI.
///
/// This is intentionally separate from explicit Apply batches. Interactive
/// requests are delayed until the same setting has been quiet for 500 ms.
/// If newer values arrive during that window, older requests are superseded and
/// never reach the bridge. Only the latest value is allowed through.
/// </summary>
public sealed class AzomInteractiveWriteService
{
    public const int DefaultDebounceMs = 500;

    private static readonly ConcurrentDictionary<string, long> Versions =
        new(StringComparer.Ordinal);

    private readonly AzomBridgeClient _bridge;
    private readonly string _scopeKey;

    public AzomInteractiveWriteService(
        AzomBridgeClient bridge,
        string scopeKey = "default")
    {
        _bridge = bridge;
        _scopeKey = string.IsNullOrWhiteSpace(scopeKey)
            ? "default"
            : scopeKey.Trim();
    }

    public async Task<AzomInteractiveWriteResult> QueueAsync(
        string propertyName,
        int? targetInt = null,
        bool? targetBool = null,
        int debounceMs = DefaultDebounceMs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(propertyName) ||
            !propertyName.StartsWith("AZOM.", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Only AZOM.* properties can use interactive write debounce.",
                nameof(propertyName));
        }

        if (!targetInt.HasValue && !targetBool.HasValue)
            throw new ArgumentException("A numeric or boolean target is required.");

        debounceMs = Math.Clamp(debounceMs, 250, 2000);

        string key =
            $"{_scopeKey}|{propertyName}";

        long version =
            Versions.AddOrUpdate(
                key,
                1,
                static (_, current) => current + 1);

        await Task.Delay(
            debounceMs,
            cancellationToken);

        if (!Versions.TryGetValue(key, out var latest) ||
            latest != version)
        {
            return new AzomInteractiveWriteResult
            {
                PropertyName = propertyName,
                WasSuperseded = true,
                WasWritten = false,
                Method = "Atomic 500 ms debounce: superseded by newer target"
            };
        }

        // Check again immediately before entering the guarded direct-write path.
        // A newer slider value that arrived after the delay still wins.
        if (!Versions.TryGetValue(key, out latest) ||
            latest != version)
        {
            return new AzomInteractiveWriteResult
            {
                PropertyName = propertyName,
                WasSuperseded = true,
                WasWritten = false,
                Method = "Atomic debounce: superseded before write"
            };
        }

        string? method =
            await _bridge.SetSettingDirectAsync(
                propertyName,
                targetInt,
                targetBool,
                cancellationToken: cancellationToken);

        return new AzomInteractiveWriteResult
        {
            PropertyName = propertyName,
            WasSuperseded = false,
            WasWritten =
                method?.Contains(
                    "suppressed",
                    StringComparison.OrdinalIgnoreCase) != true &&
                method?.Contains(
                    "already",
                    StringComparison.OrdinalIgnoreCase) != true,
            Method = method ?? "AZOM direct write"
        };
    }
}
