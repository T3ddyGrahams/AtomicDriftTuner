using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Local-LAN companion server used by ADT Remote.
///
/// The Windows ADT process remains authoritative. Telemetry is read through
/// the desktop telemetry service and all supported AZOM writes still pass
/// through AzomLiveController's guarded, verified write path.
/// </summary>
public sealed class RemoteServerService : IAsyncDisposable
{
    public const int DefaultPort = 5190;

    private const int MaxRequestBodyBytes = 16 * 1024;
    private const int MaxFailedPairAttempts = 5;
    private const int PairBlockSeconds = 30;

    private const string AdtTokenHeader = "X-ADT-Token";

    // Kept temporarily for compatibility with existing ADT Remote clients.
    // New clients should use X-ADT-Token.
    private const string LegacyTokenHeader = "X-Atomic-Token";

    private readonly AppSettingsStore _settingsStore = new();
    private readonly CarBehaviorProfileStore _behaviorStore = new();
    private readonly TelemetryHubService _telemetryHub;

    private readonly object _stateGate = new();
    private readonly object _pairGate = new();
    private readonly object _remoteRevertGate = new();

    private readonly SemaphoreSlim _lifecycleGate =
        new(1, 1);

    private readonly SemaphoreSlim _remoteMutationGate =
        new(1, 1);

    private WebApplication? _app;

    private volatile bool _remoteWritesEnabled;

    private RemoteTuneContext _tune = new();
    private TuneInput? _currentInput;

    private string _lastActivity = "Remote server is stopped.";

    private RemoteRevertState? _remoteRevertState;

    private int _failedPairAttempts;
    private DateTime _pairBlockedUntilUtc;

    private string _pairingCode = "000000";
    private string _pairToken = "";

    private sealed class RemoteRevertState
    {
        public required AzomLiveSnapshot BeforeSnapshot { get; init; }

        public required string PropertyName { get; init; }

        // The live value ADT expects to still be present before a remote revert.
        // This prevents a stale mobile undo from overwriting a newer desktop or
        // interactive change to the same setting.
        public required int ExpectedPostValue { get; init; }
    }

    public event EventHandler? StateChanged;
    public event EventHandler<RemoteAzomChangedEventArgs>? AzomChanged;

    // These handlers are supplied by MainWindow so browser requests always
    // mutate the authoritative Windows UI/tuning state on the WPF dispatcher.
    public Func<string, CancellationToken, Task<RemoteActionResponse>>?
        SetIntentHandler { get; set; }

    public Func<CancellationToken, Task<RemoteActionResponse>>?
        GenerateTuneHandler { get; set; }

    public bool IsRunning =>
        Volatile.Read(
            ref _app) is not null;

    public bool RemoteWritesEnabled =>
        _remoteWritesEnabled;

    public int Port { get; private set; } =
        DefaultPort;

    public string PairingCode
    {
        get
        {
            lock (_pairGate)
            {
                return _pairingCode;
            }
        }
    }

    public string PairToken
    {
        get
        {
            lock (_pairGate)
            {
                return _pairToken;
            }
        }
    }

    public string LastActivity
    {
        get
        {
            lock (_stateGate)
            {
                return _lastActivity;
            }
        }
    }

    public RemoteServerService(
        TelemetryHubService telemetryHub)
    {
        _telemetryHub =
            telemetryHub ??
            throw new ArgumentNullException(
                nameof(telemetryHub));

        RegeneratePairing();
    }

    public void UpdateTuneContext(
        TuneInput input,
        TuneResult? result)
    {
        ArgumentNullException.ThrowIfNull(input);

        lock (_stateGate)
        {
            _currentInput = input;

            _tune = new RemoteTuneContext
            {
                Wheelbase =
                    input.Hardware.ToString(),

                SteeringWheel =
                    input.Wheel.ToString(),

                DriftPack =
                    input.DriftPack.Name,

                Car =
                    input.Car.DisplayName,

                Intent =
                    input.Intent.Name,

                HasGeneratedTune =
                    result is not null,

                RecommendedAzom =
                    result?.Azom,

                RecommendedAc =
                    result?.Ac,

                SelfSteerScore =
                    result?.SelfSteerScore ?? 0,

                StabilityScore =
                    result?.StabilityScore ?? 0,

                DetailScore =
                    result?.DetailScore ?? 0,

                EstimatedPeakWheelTorqueNm =
                    result?.EstimatedPeakWheelTorqueNm ?? 0,

                Notes =
                    result?.Notes?.ToList() ?? []
            };
        }

        RaiseStateChanged();
    }

    public void SetRemoteWritesEnabled(
        bool enabled)
    {
        _remoteWritesEnabled =
            enabled &&
            IsRunning;

        SetActivity(
            RemoteWritesEnabled
                ? "Remote AZOM writes enabled for this ADT run."
                : "Remote AZOM writes disabled.");
    }

    public void RegeneratePairing()
    {
        var newCode =
            RandomNumberGenerator
                .GetInt32(
                    100000,
                    1000000)
                .ToString();

        var newToken =
            Convert.ToHexString(
                RandomNumberGenerator.GetBytes(32));

        lock (_pairGate)
        {
            _pairingCode = newCode;
            _pairToken = newToken;

            _failedPairAttempts = 0;
            _pairBlockedUntilUtc =
                DateTime.MinValue;
        }

        SetActivity(
            IsRunning
                ? "Pairing credentials regenerated. Previously paired browsers must pair again."
                : "Pairing credentials ready. Start ADT Remote to connect.");
    }

    public IReadOnlyList<string> GetLanUrls()
    {
        var urls =
            new List<string>();

        foreach (
            var nic in
            NetworkInterface.GetAllNetworkInterfaces())
        {
            if (
                nic.OperationalStatus !=
                    OperationalStatus.Up ||
                nic.NetworkInterfaceType ==
                    NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (
                var unicast in
                nic.GetIPProperties()
                    .UnicastAddresses)
            {
                var address =
                    unicast.Address;

                if (
                    address.AddressFamily !=
                        AddressFamily.InterNetwork ||
                    !IsPrivateOrLoopback(address))
                {
                    continue;
                }

                var url =
                    $"http://{address}:{Port}/";

                if (
                    !urls.Contains(
                        url,
                        StringComparer.OrdinalIgnoreCase))
                {
                    urls.Add(url);
                }
            }
        }

        if (urls.Count == 0)
        {
            urls.Add(
                $"http://localhost:{Port}/");
        }

        return urls;
    }

    public async Task StartAsync(
        int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(
            cancellationToken);

        try
        {
            if (IsRunning)
            {
                return;
            }

        if (port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(port),
                "Remote port must be between 1024 and 65535.");
        }

        Port = port;

        _remoteWritesEnabled = false;

        ClearRemoteRevertState();

        RegeneratePairing();

        var builder =
            WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    Args = Array.Empty<string>(),

                    ApplicationName =
                        typeof(RemoteServerService)
                            .Assembly
                            .FullName,

                    ContentRootPath =
                        AppContext.BaseDirectory
                });

        builder.Logging.ClearProviders();

        builder.Services.ConfigureHttpJsonOptions(
            options =>
            {
                options.SerializerOptions.PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase;

                options.SerializerOptions.DictionaryKeyPolicy =
                    JsonNamingPolicy.CamelCase;
            });

        builder.WebHost.ConfigureKestrel(
            options =>
            {
                options.Limits.MaxRequestBodySize =
                    MaxRequestBodyBytes;

                options.Limits.RequestHeadersTimeout =
                    TimeSpan.FromSeconds(10);

                options.Limits.KeepAliveTimeout =
                    TimeSpan.FromMinutes(2);

                options.ListenAnyIP(
                    port,
                    listenOptions =>
                    {
                        listenOptions.Protocols =
                            HttpProtocols.Http1;
                    });
            });

        var app =
            builder.Build();

        app.Use(
            async (context, next) =>
            {
                // ADT Remote represents live application state.
                // Browser/proxy caching would produce misleading stale data.
                context.Response.Headers.CacheControl =
                    "no-store, no-cache, must-revalidate";

                context.Response.Headers.Pragma =
                    "no-cache";

                context.Response.Headers.Expires =
                    "0";

                context.Response.Headers[
                    "X-Content-Type-Options"] =
                    "nosniff";

                context.Response.Headers[
                    "X-Frame-Options"] =
                    "DENY";

                context.Response.Headers[
                    "Referrer-Policy"] =
                    "no-referrer";

                context.Response.Headers[
                    "Permissions-Policy"] =
                    "camera=(), microphone=(), geolocation=()";

                var remoteAddress =
                    context.Connection.RemoteIpAddress;

                if (
                    remoteAddress is null ||
                    !IsPrivateOrLoopback(remoteAddress))
                {
                    context.Response.StatusCode =
                        StatusCodes.Status403Forbidden;

                    await context.Response.WriteAsync(
                        "ADT Remote accepts local/private network clients only.");

                    return;
                }

                if (
                    context.Request.Path
                        .StartsWithSegments("/api") &&
                    !context.Request.Path
                        .StartsWithSegments("/api/pair"))
                {
                    var supplied =
                        GetSuppliedToken(context);

                    if (!TokenMatches(supplied))
                    {
                        context.Response.StatusCode =
                            StatusCodes.Status401Unauthorized;

                        await context.Response.WriteAsJsonAsync(
                            new
                            {
                                error =
                                    "Pairing required."
                            });

                        return;
                    }
                }

                await next();
            });

        app.MapGet(
            "/",
            () =>
                Results.Content(
                    RemoteWebApp.Html,
                    "text/html; charset=utf-8"));

        app.MapGet(
            "/apple-touch-icon.png",
            () =>
                Results.NotFound());

        app.MapPost(
            "/api/pair",
            async (HttpContext context) =>
            {
                var blockedSeconds =
                    GetRemainingPairBlockSeconds();

                if (blockedSeconds > 0)
                {
                    return Results.Json(
                        new
                        {
                            ok = false,
                            error =
                                $"Too many pairing attempts. Wait {blockedSeconds} seconds and try again."
                        },
                        statusCode:
                            StatusCodes
                                .Status429TooManyRequests);
                }

                RemotePairRequest? request;

                try
                {
                    request =
                        await context.Request
                            .ReadFromJsonAsync<RemotePairRequest>(
                                cancellationToken:
                                    context.RequestAborted);
                }
                catch (Exception ex)
                    when (
                        ex is JsonException ||
                        ex is Microsoft.AspNetCore.Http.BadHttpRequestException ||
                        ex is IOException)
                {
                    return Results.BadRequest(
                        new
                        {
                            error =
                                "Invalid pairing request."
                        });
                }

                if (
                    request is null ||
                    !TryAcceptPairingCode(
                        request.Code))
                {
                    return Results.Json(
                        new
                        {
                            ok = false,
                            error =
                                "Incorrect pairing code."
                        },
                        statusCode:
                            StatusCodes
                                .Status401Unauthorized);
                }

                SetActivity(
                    "A local browser paired with ADT Remote.");

                return Results.Json(
                    new
                    {
                        ok = true,
                        token = PairToken
                    });
            });

        app.MapGet(
            "/api/status",
            () =>
                Results.Json(
                    BuildStatus()));

        app.MapGet(
            "/api/intents",
            () =>
            {
                string selected;

                lock (_stateGate)
                {
                    selected =
                        _tune.Intent;
                }

                var intents =
                    BuiltInProfiles
                        .Intents()
                        .Select(
                            x =>
                                new RemoteIntentOption
                                {
                                    Name =
                                        x.Name,

                                    Selected =
                                        string.Equals(
                                            x.Name,
                                            selected,
                                            StringComparison
                                                .OrdinalIgnoreCase)
                                })
                        .ToList();

                return Results.Json(
                    intents);
            });

        app.MapPost(
            "/api/intent",
            async (HttpContext context) =>
            {
                RemoteIntentRequest? request;

                try
                {
                    request =
                        await context.Request
                            .ReadFromJsonAsync<RemoteIntentRequest>(
                                cancellationToken:
                                    context.RequestAborted);
                }
                catch (Exception ex)
                    when (
                        ex is JsonException ||
                        ex is Microsoft.AspNetCore.Http.BadHttpRequestException ||
                        ex is IOException)
                {
                    return Results.BadRequest(
                        new
                        {
                            error =
                                "Invalid drift-target request."
                        });
                }

                var intent =
                    BuiltInProfiles
                        .Intents()
                        .FirstOrDefault(
                            x =>
                                request is not null &&
                                string.Equals(
                                    x.Name,
                                    request.Name,
                                    StringComparison
                                        .OrdinalIgnoreCase));

                if (intent is null)
                {
                    return Results.BadRequest(
                        new
                        {
                            error =
                                "Unknown drift target."
                        });
                }

                if (SetIntentHandler is null)
                {
                    return Results.Json(
                        new
                        {
                            error =
                                "Windows intent control is unavailable."
                        },
                        statusCode:
                            StatusCodes
                                .Status503ServiceUnavailable);
                }

                var response =
                    await SetIntentHandler(
                        intent.Name,
                        context.RequestAborted);

                if (response.Ok)
                {
                    SetActivity(
                        $"Remote selected drift target: {intent.Name}.");
                }

                return response.Ok
                    ? Results.Json(response)
                    : Results.Json(
                        response,
                        statusCode:
                            StatusCodes.Status400BadRequest);
            });

        app.MapPost(
            "/api/tune/generate",
            async (HttpContext context) =>
            {
                if (GenerateTuneHandler is null)
                {
                    return Results.Json(
                        new
                        {
                            error =
                                "Windows tune generation is unavailable."
                        },
                        statusCode:
                            StatusCodes
                                .Status503ServiceUnavailable);
                }

                var response =
                    await GenerateTuneHandler(
                        context.RequestAborted);

                if (response.Ok)
                {
                    SetActivity(
                        "Remote requested tune generation. Windows ADT generated and displayed the result.");
                }

                return response.Ok
                    ? Results.Json(response)
                    : Results.Json(
                        response,
                        statusCode:
                            StatusCodes.Status400BadRequest);
            });

        app.MapGet(
            "/api/behavior",
            () =>
                Results.Json(
                    ReadBehaviorView()));

        app.MapPost(
            "/api/behavior",
            async (HttpContext context) =>
            {
                RemoteBehaviorUpdateRequest? request;

                try
                {
                    request =
                        await context.Request
                            .ReadFromJsonAsync<RemoteBehaviorUpdateRequest>(
                                cancellationToken:
                                    context.RequestAborted);
                }
                catch (Exception ex)
                    when (
                        ex is JsonException ||
                        ex is Microsoft.AspNetCore.Http.BadHttpRequestException ||
                        ex is IOException)
                {
                    return Results.BadRequest(
                        new
                        {
                            error =
                                "Invalid Desired Behavior request."
                        });
                }

                if (request is null)
                {
                    return Results.BadRequest(
                        new
                        {
                            error =
                                "Invalid Desired Behavior request."
                        });
                }

                var response =
                    SaveBehaviorFromRemote(
                        request);

                return response.Ok
                    ? Results.Json(response)
                    : Results.Json(
                        response,
                        statusCode:
                            StatusCodes.Status400BadRequest);
            });

        app.MapGet(
            "/api/telemetry",
            (HttpContext context) =>
            {
                try
                {
                    context.RequestAborted
                        .ThrowIfCancellationRequested();

                    var view =
                        BuildTelemetryView();

                    // Keep this response deliberately primitive so
                    // mobile telemetry transport does not depend on
                    // desktop model serialization.
                    return Results.Json(
                        new
                        {
                            connected =
                                view.Connected,

                            error =
                                view.Error,

                            sample =
                                view.Sample is null
                                    ? null
                                    : new
                                    {
                                        packetId =
                                            view.Sample.PacketId,

                                        speedKmh =
                                            view.Sample.SpeedKmh,

                                        slipAngleDeg =
                                            view.Sample.SlipAngleDeg,

                                        steeringAngleDeg =
                                            view.Sample.SteeringAngleDeg,

                                        finalFfb =
                                            view.Sample.FinalFfb
                                    },

                            isDrifting =
                                view.IsDrifting,

                            serverTimeUtc =
                                view.ServerTimeUtc
                        });
                }
                catch (
                    OperationCanceledException)
                {
                    return Results.StatusCode(
                        StatusCodes
                            .Status499ClientClosedRequest);
                }
                catch (Exception ex)
                {
                    SetActivity(
                        "Remote telemetry endpoint failed: " +
                        ex.GetType().Name);

                    return Results.Json(
                        new
                        {
                            connected = false,

                            error =
                                "ADT Remote could not read telemetry.",

                            sample =
                                (object?)null,

                            isDrifting =
                                false,

                            serverTimeUtc =
                                DateTimeOffset.UtcNow
                        });
                }
            });

        app.MapGet(
            "/api/azom",
            async (HttpContext context) =>
                Results.Json(
                    await ReadAzomViewAsync(
                        context.RequestAborted)));

        app.MapPost(
            "/api/azom/apply",
            async (HttpContext context) =>
            {
                RemoteAzomWriteRequest? request;

                try
                {
                    request =
                        await context.Request
                            .ReadFromJsonAsync<RemoteAzomWriteRequest>(
                                cancellationToken:
                                    context.RequestAborted);
                }
                catch (Exception ex)
                    when (
                        ex is JsonException ||
                        ex is Microsoft.AspNetCore.Http.BadHttpRequestException ||
                        ex is IOException)
                {
                    return Results.BadRequest(
                        new
                        {
                            error =
                                "Invalid AZOM write request."
                        });
                }

                if (request is null)
                {
                    return Results.BadRequest(
                        new
                        {
                            error =
                                "Invalid AZOM write request."
                        });
                }

                var response =
                    await ApplyRemoteSettingAsync(
                        request,
                        context.RequestAborted);

                return response.Ok
                    ? Results.Json(response)
                    : Results.Json(
                        response,
                        statusCode:
                            StatusCodes.Status400BadRequest);
            });

        app.MapPost(
            "/api/azom/revert",
            async (HttpContext context) =>
            {
                var response =
                    await RevertLastRemoteSettingAsync(
                        context.RequestAborted);

                return response.Ok
                    ? Results.Json(response)
                    : Results.Json(
                        response,
                        statusCode:
                            StatusCodes.Status400BadRequest);
            });

        try
        {
            await app.StartAsync(
                cancellationToken);

            Volatile.Write(
                ref _app,
                app);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        // Best-effort connection. If AC is not running yet,
        // TelemetryHubService retries when clients request data.
        _telemetryHub.GetSnapshot();

            SetActivity(
                $"ADT Remote started on port {Port}. Remote writes are OFF.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(
            cancellationToken);

        try
        {
            var app =
                Interlocked.Exchange(
                    ref _app,
                    null);

            if (app is null)
            {
                return;
            }

        _remoteWritesEnabled = false;

        ClearRemoteRevertState();

        try
        {
            await app.StopAsync(
                cancellationToken);
        }
        finally
        {
            await app.DisposeAsync();

                SetActivity(
                    "ADT Remote stopped. Remote writes are OFF.");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private RemoteStatusView BuildStatus()
    {
        lock (_stateGate)
        {
            return new RemoteStatusView
            {
                AtomicVersion =
                    DistributionInfo.Version,

                RemoteWritesEnabled =
                    RemoteWritesEnabled,

                LastActivity =
                    _lastActivity,

                Tune =
                    _tune
            };
        }
    }

    private RemoteBehaviorView ReadBehaviorView()
    {
        TuneInput? input;

        lock (_stateGate)
        {
            input =
                _currentInput;
        }

        if (input is null)
        {
            return new RemoteBehaviorView
            {
                Ok = false,

                Error =
                    "Select a wheelbase, wheel, drift pack and car in Windows ADT first."
            };
        }

        try
        {
            var target =
                _behaviorStore.Load(input);

            target.Normalize();

            return new RemoteBehaviorView
            {
                Ok = true,

                DisplayName =
                    target.DisplayName,

                UpdatedUtc =
                    target.UpdatedUtc,

                FrontEndBite =
                    target.FrontEndBite,

                RearGrip =
                    target.RearGrip,

                SelfSteerSpeed =
                    target.SelfSteerSpeed,

                TransitionSpeed =
                    target.TransitionSpeed,

                AngleStability =
                    target.AngleStability,

                ThrottleSteering =
                    target.ThrottleSteering,

                InitiationSharpness =
                    target.InitiationSharpness
            };
        }
        catch (Exception ex)
        {
            SetActivity(
                "Remote Desired Behavior read failed: " +
                ex.GetType().Name);

            return new RemoteBehaviorView
            {
                Ok = false,

                Error =
                    "ADT could not load Desired Behavior for the current car."
            };
        }
    }

    private RemoteActionResponse SaveBehaviorFromRemote(
        RemoteBehaviorUpdateRequest request)
    {
        TuneInput? input;

        lock (_stateGate)
        {
            input =
                _currentInput;
        }

        if (input is null)
        {
            return new RemoteActionResponse
            {
                Ok = false,

                Message =
                    "Select a current car in Windows ADT first."
            };
        }

        try
        {
            var target =
                new CarBehaviorTarget
                {
                    FrontEndBite =
                        request.FrontEndBite,

                    RearGrip =
                        request.RearGrip,

                    SelfSteerSpeed =
                        request.SelfSteerSpeed,

                    TransitionSpeed =
                        request.TransitionSpeed,

                    AngleStability =
                        request.AngleStability,

                    ThrottleSteering =
                        request.ThrottleSteering,

                    InitiationSharpness =
                        request.InitiationSharpness
                };

            target.Normalize();

            _behaviorStore.Save(
                input,
                target);

            SetActivity(
                $"Remote saved Desired Behavior for {input.DriftPack.Name} • {input.Car.DisplayName}. " +
                "This changes ADT's per-car setup target only; it does not write the wheelbase.");

            return new RemoteActionResponse
            {
                Ok = true,

                Message =
                    $"Saved Desired Behavior for {input.Car.DisplayName}."
            };
        }
        catch (Exception ex)
        {
            SetActivity(
                "Remote Desired Behavior save failed: " +
                ex.GetType().Name);

            return new RemoteActionResponse
            {
                Ok = false,

                Message =
                    "ADT could not save Desired Behavior."
            };
        }
    }

    private RemoteTelemetryView BuildTelemetryView()
    {
        var snapshot =
            _telemetryHub.GetSnapshot();

        var sample =
            snapshot.Sample;

        if (
            !snapshot.Connected ||
            sample is null)
        {
            return new RemoteTelemetryView
            {
                Connected = false,

                Error =
                    snapshot.Error ??
                    "Assetto Corsa telemetry unavailable.",

                ServerTimeUtc =
                    DateTimeOffset.UtcNow
            };
        }

        var speed =
            FiniteOrNull(
                sample.SpeedKmh);

        var slip =
            FiniteOrNull(
                sample.SlipAngleDeg);

        var steering =
            FiniteOrNull(
                sample.SteeringAngleDeg);

        var ffb =
            FiniteOrNull(
                sample.FinalFfb);

        return new RemoteTelemetryView
        {
            Connected = true,

            Sample =
                new RemoteTelemetrySampleView
                {
                    PacketId =
                        sample.PacketId,

                    SpeedKmh =
                        speed,

                    SlipAngleDeg =
                        slip,

                    SteeringAngleDeg =
                        steering,

                    FinalFfb =
                        ffb
                },

            IsDrifting =
                speed.HasValue &&
                slip.HasValue &&
                speed.Value >= 20 &&
                Math.Abs(slip.Value) >= 10,

            ServerTimeUtc =
                DateTimeOffset.UtcNow
        };
    }

    public string GetTelemetryDiagnosticText()
    {
        try
        {
            var snapshot =
                _telemetryHub.GetSnapshot();

            if (
                !snapshot.Connected ||
                snapshot.Sample is null)
            {
                return
                    "OFFLINE • " +
                    (
                        snapshot.Error ??
                        "No shared telemetry sample is available."
                    );
            }

            var ageMs =
                snapshot.UpdatedUtc.HasValue
                    ? Math.Max(
                        0,
                        (
                            DateTimeOffset.UtcNow -
                            snapshot.UpdatedUtc.Value
                        ).TotalMilliseconds)
                    : -1;

            return
                $"LIVE • packet {snapshot.Sample.PacketId} • " +
                $"sample age {(ageMs < 0 ? "?" : ageMs.ToString("0"))} ms";
        }
        catch (Exception ex)
        {
            return
                "ERROR • " +
                ex.GetType().Name +
                ": " +
                ex.Message;
        }
    }

    private static double? FiniteOrNull(
        double value)
    {
        return double.IsFinite(value)
            ? value
            : null;
    }

    private async Task<object> ReadAzomViewAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot =
                await CreateLiveController()
                    .ReadAsync(
                        cancellationToken);

            var settings =
                SettingDefinitions
                    .Select(
                        def =>
                            new RemoteAzomSettingView
                            {
                                PropertyName =
                                    def.PropertyName,

                                DisplayName =
                                    def.DisplayName,

                                Current =
                                    def.Getter(snapshot),

                                Min =
                                    def.Range.Min,

                                Max =
                                    def.Range.Max,

                                Unit =
                                    def.Range.Unit,

                                Writable =
                                    RemoteWritesEnabled &&
                                    snapshot.SettingsReadable &&
                                    string.Equals(
                                        snapshot.PropertyNamespace,
                                        "AZOM",
                                        StringComparison
                                            .OrdinalIgnoreCase) &&
                                    def.Getter(snapshot)
                                        .HasValue
                            })
                    .ToList();

            return new
            {
                ok = true,

                bridgeVersion =
                    snapshot.BridgeVersion,

                propertyNamespace =
                    snapshot.PropertyNamespace,

                settingsReadable =
                    snapshot.SettingsReadable,

                baseConnected =
                    snapshot.BaseConnected,

                remoteWritesEnabled =
                    RemoteWritesEnabled,

                settings
            };
        }
        catch (Exception ex)
        {
            SetActivity(
                "Remote AZOM read failed: " +
                ex.GetType().Name);

            return new
            {
                ok = false,

                error =
                    "ADT Remote could not read live AZOM settings.",

                remoteWritesEnabled =
                    RemoteWritesEnabled,

                settings =
                    Array.Empty<RemoteAzomSettingView>()
            };
        }
    }

    private async Task<RemoteAzomWriteResponse>
        ApplyRemoteSettingAsync(
            RemoteAzomWriteRequest request,
            CancellationToken cancellationToken)
    {
        if (!RemoteWritesEnabled)
        {
            return Failure(
                request,
                "Remote AZOM writes are disabled on the Windows PC.");
        }

        var definition =
            SettingDefinitions.FirstOrDefault(
                x =>
                    string.Equals(
                        x.PropertyName,
                        request.PropertyName,
                        StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            return Failure(
                request,
                "That setting is not in ADT Remote's explicit write allow-list.");
        }

        if (
            request.Value <
                definition.Range.Min ||
            request.Value >
                definition.Range.Max)
        {
            return Failure(
                request,
                $"Requested value is outside ADT's known range {definition.Range.Display}.");
        }

        // Enforce the controller's centralized property/type/range contract too.
        // The remote surface is intentionally a smaller subset of that contract.
        try
        {
            AzomLiveController.ValidateDirectWriteTarget(
                definition.PropertyName,
                request.Value,
                null);
        }
        catch (Exception)
        {
            return Failure(
                request,
                "ADT refused the requested AZOM property/value at the live-write safety boundary.");
        }

        await _remoteMutationGate.WaitAsync(
            cancellationToken);

        try
        {
            // A user may disable remote writes while this request is waiting
            // behind another remote mutation. Re-check only after serialization.
            if (!RemoteWritesEnabled)
            {
                return Failure(
                    request,
                    "Remote AZOM writes were disabled before this request could run.");
            }

            var controller =
                CreateLiveController();

            var before =
                await controller.ReadAsync(
                    cancellationToken);

            if (
                !before.SettingsReadable ||
                !string.Equals(
                    before.PropertyNamespace,
                    "AZOM",
                    StringComparison.OrdinalIgnoreCase) ||
                before.BaseConnected ==
                    false)
            {
                return Failure(
                    request,
                    "Current AZOM Base settings are not safely writable through the ADT bridge.");
            }

            var current =
                definition.Getter(
                    before);

            if (
                !current.HasValue ||
                current.Value <
                    definition.Range.Min ||
                current.Value >
                    definition.Range.Max)
            {
                return Failure(
                    request,
                    "The requested live AZOM setting is not safely readable on this AZOM/base combination.");
            }

            if (
                current.Value ==
                request.Value)
            {
                SetActivity(
                    $"Remote request for {definition.DisplayName} already matched live value " +
                    $"{request.Value}{definition.Range.Unit}.");

                return new RemoteAzomWriteResponse
                {
                    Ok = true,
                    Verified = true,

                    PropertyName =
                        definition.PropertyName,

                    RequestedValue =
                        request.Value,

                    LiveValue =
                        current.Value,

                    Message =
                        "Already matched; no write was sent."
                };
            }

            // Install the recovery record before handing the request to the
            // controller. If cancellation/transport ambiguity happens after
            // handoff, a later remote revert can only proceed when the live
            // value still matches this exact requested value.
            SetRemoteRevertState(
                new RemoteRevertState
                {
                    BeforeSnapshot =
                        before,

                    PropertyName =
                        definition.PropertyName,

                    ExpectedPostValue =
                        request.Value
                });

            var plan =
                new List<AzomApplyPlanItem>
                {
                    new()
                    {
                        Group =
                            "Remote",

                        DisplayName =
                            definition.DisplayName,

                        PropertyName =
                            definition.PropertyName,

                        Kind =
                            AzomApplyItemKind.Numeric,

                        CurrentInt =
                            current.Value,

                        TargetInt =
                            request.Value,

                        CurrentDisplay =
                            current.Value +
                            definition.Range.Unit,

                        TargetDisplay =
                            request.Value +
                            definition.Range.Unit,

                        ActionBase =
                            definition.PropertyName,

                        FineStep =
                            definition.FineStep,

                        CoarseStep =
                            definition.CoarseStep,

                        CanApply =
                            true,

                        IsSelectedForApply =
                            true
                    }
                };

            var result =
                await controller.ApplyAsync(
                    plan,
                    before,
                    cancellationToken);

            var after =
                result.After ??
                await controller.ReadAsync(
                    cancellationToken);

            var live =
                definition.Getter(
                    after);

            var verified =
                live.HasValue &&
                live.Value ==
                    request.Value &&
                result.VerifiedSettingsChanged ==
                    1;

            if (
                !verified &&
                live.HasValue &&
                live.Value !=
                    current.Value)
            {
                // The guarded batch observed a changed but non-target value.
                // Bind recovery to the exact last observed post-write state so
                // remote undo still cannot overwrite an unrelated later change.
                SetRemoteRevertState(
                    new RemoteRevertState
                    {
                        BeforeSnapshot =
                            before,

                        PropertyName =
                            definition.PropertyName,

                        ExpectedPostValue =
                            live.Value
                    });
            }
            else if (
                !verified &&
                live.HasValue &&
                live.Value ==
                    current.Value)
            {
                // Nothing changed; preserve no stale "undo" for this failed
                // request.
                ClearRemoteRevertState();
            }

            var response =
                new RemoteAzomWriteResponse
                {
                    Ok =
                        verified,

                    Verified =
                        verified,

                    PropertyName =
                        definition.PropertyName,

                    RequestedValue =
                        request.Value,

                    LiveValue =
                        live,

                    Message =
                        verified
                            ? $"Verified live readback at {live}{definition.Range.Unit}."
                            : "ADT did not verify the requested live value. The guarded batch stopped."
                };

            SetActivity(
                verified
                    ? $"REMOTE VERIFIED: {definition.DisplayName} " +
                      $"{current}{definition.Range.Unit} → " +
                      $"{live}{definition.Range.Unit}."
                    : $"REMOTE FAILED VERIFY: {definition.DisplayName} requested " +
                      $"{request.Value}{definition.Range.Unit}; live " +
                      $"{live?.ToString() ?? "N/A"}{definition.Range.Unit}.");

            AzomChanged?.Invoke(
                this,
                new RemoteAzomChangedEventArgs
                {
                    PropertyName =
                        definition.PropertyName,

                    Value =
                        live,

                    Verified =
                        verified
                });

            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetActivity(
                "Remote AZOM write failed: " +
                ex.GetType().Name);

            return Failure(
                request,
                "ADT could not complete the requested AZOM write.");
        }
        finally
        {
            _remoteMutationGate.Release();
        }
    }

    private async Task<RemoteAzomWriteResponse>
        RevertLastRemoteSettingAsync(
            CancellationToken cancellationToken)
    {
        if (!RemoteWritesEnabled)
        {
            return new RemoteAzomWriteResponse
            {
                Ok = false,

                Message =
                    "Remote AZOM writes are disabled on the Windows PC."
            };
        }

        await _remoteMutationGate.WaitAsync(
            cancellationToken);

        try
        {
            if (!RemoteWritesEnabled)
            {
                return new RemoteAzomWriteResponse
                {
                    Ok = false,

                    Message =
                        "Remote AZOM writes were disabled before this revert could run."
                };
            }

            var revertState =
                GetRemoteRevertState();

            if (revertState is null)
            {
                return new RemoteAzomWriteResponse
                {
                    Ok = false,

                    Message =
                        "No remote-change snapshot is available to revert in this ADT run."
                };
            }

            var definition =
                SettingDefinitions.FirstOrDefault(
                    x =>
                        string.Equals(
                            x.PropertyName,
                            revertState.PropertyName,
                            StringComparison.Ordinal));

            if (definition is null)
            {
                ClearRemoteRevertState();

                return new RemoteAzomWriteResponse
                {
                    Ok = false,

                    PropertyName =
                        revertState.PropertyName,

                    Message =
                        "The saved remote revert property is no longer supported by this ADT build."
                };
            }

            var desired =
                definition.Getter(
                    revertState.BeforeSnapshot);

            if (
                !desired.HasValue ||
                desired.Value <
                    definition.Range.Min ||
                desired.Value >
                    definition.Range.Max)
            {
                ClearRemoteRevertState();

                return new RemoteAzomWriteResponse
                {
                    Ok = false,

                    PropertyName =
                        revertState.PropertyName,

                    Message =
                        "The saved pre-change AZOM value is no longer a safe revert target."
                };
            }

            var controller =
                CreateLiveController();

            var current =
                await controller.ReadAsync(
                    cancellationToken);

            if (
                !current.SettingsReadable ||
                !string.Equals(
                    current.PropertyNamespace,
                    "AZOM",
                    StringComparison.OrdinalIgnoreCase) ||
                current.BaseConnected ==
                    false)
            {
                return new RemoteAzomWriteResponse
                {
                    Ok = false,

                    PropertyName =
                        revertState.PropertyName,

                    Message =
                        "Current AZOM Base settings are not safely readable for remote revert."
                };
            }

            var currentValue =
                definition.Getter(
                    current);

            if (!currentValue.HasValue)
            {
                return new RemoteAzomWriteResponse
                {
                    Ok = false,

                    PropertyName =
                        revertState.PropertyName,

                    Message =
                        "The live AZOM value required for remote revert is not readable."
                };
            }

            if (currentValue.Value ==
                desired.Value)
            {
                ClearRemoteRevertStateIfSame(
                    revertState);

                SetActivity(
                    "Remote Revert: the live setting already matches the saved pre-change snapshot.");

                return new RemoteAzomWriteResponse
                {
                    Ok = true,
                    Verified = true,

                    PropertyName =
                        revertState.PropertyName,

                    RequestedValue =
                        desired.Value,

                    LiveValue =
                        currentValue.Value,

                    Message =
                        "Already reverted."
                };
            }

            if (currentValue.Value !=
                revertState.ExpectedPostValue)
            {
                ClearRemoteRevertStateIfSame(
                    revertState);

                SetActivity(
                    $"Remote Revert refused for {definition.DisplayName}: live value changed after the remote operation.");

                return new RemoteAzomWriteResponse
                {
                    Ok = false,
                    Verified = false,

                    PropertyName =
                        revertState.PropertyName,

                    RequestedValue =
                        desired.Value,

                    LiveValue =
                        currentValue.Value,

                    Message =
                        "ADT refused this stale remote revert because the live setting changed after the remote operation."
                };
            }

            var plan =
                controller.BuildRevertPlan(
                    revertState.BeforeSnapshot,
                    current,
                    new[]
                    {
                        revertState.PropertyName
                    });

            var changed =
                plan
                    .Where(
                        x =>
                            x.CanApply &&
                            x.IsDifferent)
                    .ToList();

            if (changed.Count !=
                1)
            {
                return new RemoteAzomWriteResponse
                {
                    Ok = false,

                    PropertyName =
                        revertState.PropertyName,

                    RequestedValue =
                        desired.Value,

                    LiveValue =
                        currentValue.Value,

                    Message =
                        "ADT could not build one safe remote revert operation for the saved setting."
                };
            }

            // AzomLiveController v3 verifies that the authoritative live source
            // still equals this plan's CurrentInt after it acquires the global
            // live-write gate. That closes the final desktop-vs-remote stale
            // revert race between the read above and the actual commit.
            var result =
                await controller.ApplyAsync(
                    plan,
                    current,
                    cancellationToken);

            var after =
                result.After ??
                await controller.ReadAsync(
                    cancellationToken);

            var live =
                definition.Getter(
                    after);

            var verified =
                live.HasValue &&
                live.Value ==
                    desired.Value &&
                result.VerifiedSettingsChanged >=
                    1;

            if (verified)
            {
                ClearRemoteRevertStateIfSame(
                    revertState);
            }

            SetActivity(
                verified
                    ? $"REMOTE REVERT VERIFIED: {definition.DisplayName} restored to " +
                      $"{live}{definition.Range.Unit}."
                    : $"REMOTE REVERT FAILED VERIFY: {definition.DisplayName} live " +
                      $"{live?.ToString() ?? "N/A"}{definition.Range.Unit}.");

            AzomChanged?.Invoke(
                this,
                new RemoteAzomChangedEventArgs
                {
                    PropertyName =
                        revertState.PropertyName,

                    Value =
                        live,

                    Verified =
                        verified
                });

            return new RemoteAzomWriteResponse
            {
                Ok =
                    verified,

                Verified =
                    verified,

                PropertyName =
                    revertState.PropertyName,

                RequestedValue =
                    desired.Value,

                LiveValue =
                    live,

                Message =
                    verified
                        ? "Remote change reverted and verified."
                        : "Revert did not verify; ADT stopped the batch."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetActivity(
                "Remote Revert failed: " +
                ex.GetType().Name);

            return new RemoteAzomWriteResponse
            {
                Ok = false,

                Message =
                    "ADT could not complete the remote revert."
            };
        }
        finally
        {
            _remoteMutationGate.Release();
        }
    }

    private AzomLiveController CreateLiveController()
    {
        var live =
            _settingsStore.Load().AzomLive ??
            new AzomLiveConnectionSettings();

        SimHubActionInvoker? cliFallback =
            null;

        var exe =
            SimHubLocator.FindSimHubExe(
                live.SimHubExePath) ??
            live.SimHubExePath;

        if (
            !string.IsNullOrWhiteSpace(exe) &&
            File.Exists(exe))
        {
            cliFallback =
                new SimHubActionInvoker(
                    exe,
                    live.ActionDelayMs);
        }

        return new AzomLiveController(
            new AzomBridgeClient(
                live.PipeName),
            live.ActionDelayMs,
            cliFallback);
    }

    private void SetRemoteRevertState(
        RemoteRevertState state)
    {
        ArgumentNullException.ThrowIfNull(
            state);

        lock (_remoteRevertGate)
        {
            _remoteRevertState =
                state;
        }
    }

    private RemoteRevertState? GetRemoteRevertState()
    {
        lock (_remoteRevertGate)
        {
            return _remoteRevertState;
        }
    }

    private void ClearRemoteRevertState()
    {
        lock (_remoteRevertGate)
        {
            _remoteRevertState =
                null;
        }
    }

    private void ClearRemoteRevertStateIfSame(
        RemoteRevertState expected)
    {
        lock (_remoteRevertGate)
        {
            if (ReferenceEquals(
                    _remoteRevertState,
                    expected))
            {
                _remoteRevertState =
                    null;
            }
        }
    }

    private static RemoteAzomWriteResponse Failure(
        RemoteAzomWriteRequest request,
        string message)
    {
        return new RemoteAzomWriteResponse
        {
            Ok = false,
            Verified = false,

            PropertyName =
                request.PropertyName,

            RequestedValue =
                request.Value,

            Message =
                message
        };
    }

    private static string? GetSuppliedToken(
        HttpContext context)
    {
        var current =
            context.Request.Headers[
                AdtTokenHeader]
                .FirstOrDefault();

        if (
            !string.IsNullOrWhiteSpace(
                current))
        {
            return current;
        }

        return context.Request.Headers[
                LegacyTokenHeader]
            .FirstOrDefault();
    }

    private bool TokenMatches(
        string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied))
        {
            return false;
        }

        string expected;

        lock (_pairGate)
        {
            expected =
                _pairToken;
        }

        if (
            supplied.Length !=
            expected.Length)
        {
            return false;
        }

        var a =
            Encoding.UTF8.GetBytes(
                supplied);

        var b =
            Encoding.UTF8.GetBytes(
                expected);

        return
            a.Length == b.Length &&
            CryptographicOperations
                .FixedTimeEquals(
                    a,
                    b);
    }

    private bool TryAcceptPairingCode(
        string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied))
        {
            RegisterPairFailure();
            return false;
        }

        var normalized =
            supplied.Trim();

        lock (_pairGate)
        {
            if (
                DateTime.UtcNow <
                _pairBlockedUntilUtc)
            {
                return false;
            }

            if (
                normalized.Length ==
                    _pairingCode.Length)
            {
                var a =
                    Encoding.UTF8.GetBytes(
                        normalized);

                var b =
                    Encoding.UTF8.GetBytes(
                        _pairingCode);

                if (
                    a.Length == b.Length &&
                    CryptographicOperations
                        .FixedTimeEquals(
                            a,
                            b))
                {
                    _failedPairAttempts = 0;
                    _pairBlockedUntilUtc =
                        DateTime.MinValue;

                    return true;
                }
            }

            _failedPairAttempts++;

            if (
                _failedPairAttempts >=
                MaxFailedPairAttempts)
            {
                _failedPairAttempts = 0;

                _pairBlockedUntilUtc =
                    DateTime.UtcNow.AddSeconds(
                        PairBlockSeconds);
            }

            return false;
        }
    }

    private void RegisterPairFailure()
    {
        lock (_pairGate)
        {
            if (
                DateTime.UtcNow <
                _pairBlockedUntilUtc)
            {
                return;
            }

            _failedPairAttempts++;

            if (
                _failedPairAttempts >=
                MaxFailedPairAttempts)
            {
                _failedPairAttempts = 0;

                _pairBlockedUntilUtc =
                    DateTime.UtcNow.AddSeconds(
                        PairBlockSeconds);
            }
        }
    }

    private int GetRemainingPairBlockSeconds()
    {
        lock (_pairGate)
        {
            var remaining =
                _pairBlockedUntilUtc -
                DateTime.UtcNow;

            if (remaining <= TimeSpan.Zero)
            {
                return 0;
            }

            return Math.Max(
                1,
                (int)Math.Ceiling(
                    remaining.TotalSeconds));
        }
    }

    private void SetActivity(
        string activity)
    {
        lock (_stateGate)
        {
            _lastActivity =
                activity;
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(
            this,
            EventArgs.Empty);
    }

    private static bool IsPrivateOrLoopback(
        IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address =
                address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (
            address.AddressFamily ==
            AddressFamily.InterNetwork)
        {
            var bytes =
                address.GetAddressBytes();

            return
                bytes[0] == 10 ||
                (
                    bytes[0] == 172 &&
                    bytes[1] is >= 16 and <= 31
                ) ||
                (
                    bytes[0] == 192 &&
                    bytes[1] == 168
                ) ||
                (
                    bytes[0] == 169 &&
                    bytes[1] == 254
                );
        }

        if (
            address.AddressFamily ==
            AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal)
            {
                return true;
            }

            var bytes =
                address.GetAddressBytes();

            // fc00::/7 unique-local range.
            return
                (bytes[0] & 0xFE) ==
                0xFC;
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private sealed record RemoteSettingDefinition(
        string PropertyName,
        string DisplayName,
        AzomRange Range,
        Func<AzomLiveSnapshot, int?> Getter,
        int FineStep,
        int CoarseStep);

    private sealed class RemotePairRequest
    {
        public string Code { get; set; } =
            "";
    }

    // Remote write surface: only the core/wheelbase controls ADT already
    // knows how to range-check and verify. Preferences, EQ, curve nodes
    // and undocumented controls remain read-only/not exposed remotely.
    private static readonly
        IReadOnlyList<RemoteSettingDefinition>
        SettingDefinitions =
        [
            new(
                "AZOM.FfbStrength",
                "Game FFB Strength",
                AzomSettingCatalog.GameFfbStrength,
                x => x.FfbStrength,
                5,
                10),

            new(
                "AZOM.Torque",
                "Base Torque Output",
                AzomSettingCatalog.BaseTorqueOutput,
                x => x.Torque,
                5,
                10),

            new(
                "AZOM.Rotation",
                "Wheel Rotation Angle",
                AzomSettingCatalog.WheelRotationAngle,
                x => x.Rotation,
                90,
                180),

            new(
                "AZOM.WheelSpeedLimit",
                "Maximum Wheel Speed",
                AzomSettingCatalog.MaximumWheelSpeed,
                x => x.WheelSpeedLimit,
                5,
                10),

            new(
                "AZOM.Interpolation",
                "Interpolation",
                AzomSettingCatalog.Interpolation,
                x => x.Interpolation,
                1,
                2),

            new(
                "AZOM.Damper",
                "Wheel Damper",
                AzomSettingCatalog.WheelDamper,
                x => x.Damper,
                5,
                10),

            new(
                "AZOM.Friction",
                "Wheel Friction",
                AzomSettingCatalog.WheelFriction,
                x => x.Friction,
                5,
                10),

            new(
                "AZOM.Inertia",
                "Natural Inertia",
                AzomSettingCatalog.NaturalInertia,
                x => x.Inertia,
                10,
                50),

            new(
                "AZOM.SpeedDamping",
                "High-Speed Damping",
                AzomSettingCatalog.HighSpeedDampingLevel,
                x => x.SpeedDamping,
                5,
                10),

            new(
                "AZOM.SpeedDampingPoint",
                "High-Speed Trigger",
                AzomSettingCatalog.HighSpeedTriggerSpeed,
                x => x.SpeedDampingPoint,
                10,
                50)
        ];
}
