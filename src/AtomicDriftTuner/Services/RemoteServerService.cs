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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Local-LAN companion server used by the experimental iPhone/browser remote.
/// The Windows process remains authoritative: telemetry is read here and all
/// AZOM writes still go through AzomLiveController's existing guarded,
/// verified write path.
/// </summary>
public sealed class RemoteServerService : IAsyncDisposable
{
    public const int DefaultPort = 5190;

    private readonly AppSettingsStore _settingsStore = new();
    private readonly CarBehaviorProfileStore _behaviorStore = new();
    private readonly TelemetryHubService _telemetryHub;
    private readonly object _stateGate = new();

    private WebApplication? _app;
    private RemoteTuneContext _tune = new();
    private TuneInput? _currentInput;
    private string _lastActivity = "Remote server is stopped.";
    private AzomLiveSnapshot? _remoteBeforeSnapshot;
    private string? _remoteChangedProperty;
    private int _failedPairAttempts;
    private DateTime _pairBlockedUntilUtc;

    public event EventHandler? StateChanged;
    public event EventHandler<RemoteAzomChangedEventArgs>? AzomChanged;

    // These handlers are supplied by MainWindow so browser requests always
    // mutate the authoritative Windows UI/tuning state on the WPF dispatcher.
    public Func<string, CancellationToken, Task<RemoteActionResponse>>? SetIntentHandler { get; set; }
    public Func<CancellationToken, Task<RemoteActionResponse>>? GenerateTuneHandler { get; set; }

    public bool IsRunning => _app is not null;
    public bool RemoteWritesEnabled { get; private set; }
    public int Port { get; private set; } = DefaultPort;
    public string PairingCode { get; private set; } = "000000";
    public string PairToken { get; private set; } = "";

    public string LastActivity
    {
        get
        {
            lock (_stateGate)
                return _lastActivity;
        }
    }

    public RemoteServerService(TelemetryHubService telemetryHub)
    {
        _telemetryHub = telemetryHub ?? throw new ArgumentNullException(nameof(telemetryHub));
        RegeneratePairing();
    }

    public void UpdateTuneContext(TuneInput input, TuneResult? result)
    {
        lock (_stateGate)
        {
            _currentInput = input;
            _tune = new RemoteTuneContext
            {
                Wheelbase = input.Hardware.ToString(),
                SteeringWheel = input.Wheel.ToString(),
                DriftPack = input.DriftPack.Name,
                Car = input.Car.DisplayName,
                Intent = input.Intent.Name,
                HasGeneratedTune = result is not null,
                RecommendedAzom = result?.Azom,
                RecommendedAc = result?.Ac,
                SelfSteerScore = result?.SelfSteerScore ?? 0,
                StabilityScore = result?.StabilityScore ?? 0,
                DetailScore = result?.DetailScore ?? 0,
                EstimatedPeakWheelTorqueNm = result?.EstimatedPeakWheelTorqueNm ?? 0,
                Notes = result?.Notes?.ToList() ?? []
            };
        }

        RaiseStateChanged();
    }

    public void SetRemoteWritesEnabled(bool enabled)
    {
        RemoteWritesEnabled = enabled && IsRunning;
        SetActivity(RemoteWritesEnabled
            ? "Remote AZOM writes enabled for this Atomic run."
            : "Remote AZOM writes disabled.");
    }

    public void RegeneratePairing()
    {
        PairingCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        PairToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _failedPairAttempts = 0;
        _pairBlockedUntilUtc = DateTime.MinValue;
        SetActivity(IsRunning
            ? "Pairing credentials regenerated. Previously paired browsers must pair again."
            : "Pairing credentials ready. Start the remote server to connect.");
    }

    public IReadOnlyList<string> GetLanUrls()
    {
        var urls = new List<string>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork ||
                    !IsPrivateOrLoopback(address))
                    continue;

                var url = $"http://{address}:{Port}/";
                if (!urls.Contains(url, StringComparer.OrdinalIgnoreCase))
                    urls.Add(url);
            }
        }

        if (urls.Count == 0)
            urls.Add($"http://localhost:{Port}/");

        return urls;
    }

    public async Task StartAsync(int port = DefaultPort, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return;

        if (port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Remote port must be between 1024 and 65535.");

        Port = port;
        RemoteWritesEnabled = false;
        _remoteBeforeSnapshot = null;
        _remoteChangedProperty = null;
        RegeneratePairing();

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ApplicationName = typeof(RemoteServerService).Assembly.FullName,
                ContentRootPath = AppContext.BaseDirectory
            });

        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        });
        builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            // Safari can be aggressive about reusing GET responses. Atomic Remote
            // is live state, so never allow the browser/proxy to cache UI/API data.
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";

            var remoteAddress = context.Connection.RemoteIpAddress;
            if (remoteAddress is null || !IsPrivateOrLoopback(remoteAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Atomic Remote accepts local/private network clients only.");
                return;
            }

            if (context.Request.Path.StartsWithSegments("/api") &&
                !context.Request.Path.StartsWithSegments("/api/pair"))
            {
                var supplied = context.Request.Headers["X-Atomic-Token"].FirstOrDefault();
                if (!TokenMatches(supplied))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "Pairing required." });
                    return;
                }
            }

            await next();
        });

        app.MapGet("/", () => Results.Content(RemoteWebApp.Html, "text/html; charset=utf-8"));
        app.MapGet("/apple-touch-icon.png", () => Results.NotFound());

        app.MapPost("/api/pair", async (HttpContext context) =>
        {
            if (DateTime.UtcNow < _pairBlockedUntilUtc)
            {
                return Results.Json(
                    new { ok = false, error = "Too many pairing attempts. Wait 30 seconds and try again." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var request = await context.Request.ReadFromJsonAsync<RemotePairRequest>(cancellationToken: context.RequestAborted);
            if (request is null || !PairingCodeMatches(request.Code))
            {
                _failedPairAttempts++;
                if (_failedPairAttempts >= 5)
                {
                    _failedPairAttempts = 0;
                    _pairBlockedUntilUtc = DateTime.UtcNow.AddSeconds(30);
                }

                return Results.Json(
                    new { ok = false, error = "Incorrect pairing code." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            _failedPairAttempts = 0;
            SetActivity("A local browser paired with Atomic Remote.");
            return Results.Json(new { ok = true, token = PairToken });
        });

        app.MapGet("/api/status", () => Results.Json(BuildStatus()));

        app.MapGet("/api/intents", () =>
        {
            string selected;
            lock (_stateGate)
                selected = _tune.Intent;

            var intents = BuiltInProfiles.Intents()
                .Select(x => new RemoteIntentOption
                {
                    Name = x.Name,
                    Selected = string.Equals(x.Name, selected, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            return Results.Json(intents);
        });

        app.MapPost("/api/intent", async (HttpContext context) =>
        {
            var request = await context.Request.ReadFromJsonAsync<RemoteIntentRequest>(
                cancellationToken: context.RequestAborted);

            var intent = BuiltInProfiles.Intents().FirstOrDefault(
                x => request is not null &&
                     string.Equals(x.Name, request.Name, StringComparison.OrdinalIgnoreCase));

            if (intent is null)
                return Results.BadRequest(new { error = "Unknown drift target." });

            if (SetIntentHandler is null)
                return Results.Json(new { error = "Windows intent control is unavailable." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            var response = await SetIntentHandler(intent.Name, context.RequestAborted);
            if (response.Ok)
                SetActivity($"Remote selected drift target: {intent.Name}.");

            return response.Ok
                ? Results.Json(response)
                : Results.Json(response, statusCode: StatusCodes.Status400BadRequest);
        });

        app.MapPost("/api/tune/generate", async (HttpContext context) =>
        {
            if (GenerateTuneHandler is null)
                return Results.Json(new { error = "Windows tune generation is unavailable." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            var response = await GenerateTuneHandler(context.RequestAborted);
            if (response.Ok)
                SetActivity("Remote requested a tune generation. Windows Atomic generated and displayed the result.");

            return response.Ok
                ? Results.Json(response)
                : Results.Json(response, statusCode: StatusCodes.Status400BadRequest);
        });

        app.MapGet("/api/behavior", () => Results.Json(ReadBehaviorView()));

        app.MapPost("/api/behavior", async (HttpContext context) =>
        {
            var request = await context.Request.ReadFromJsonAsync<RemoteBehaviorUpdateRequest>(
                cancellationToken: context.RequestAborted);

            if (request is null)
                return Results.BadRequest(new { error = "Invalid Desired Behavior request." });

            var response = SaveBehaviorFromRemote(request);
            return response.Ok
                ? Results.Json(response)
                : Results.Json(response, statusCode: StatusCodes.Status400BadRequest);
        });

        app.MapGet("/api/telemetry", (HttpContext context) =>
        {
            try
            {
                context.RequestAborted.ThrowIfCancellationRequested();
                var view = BuildTelemetryView();

                // Keep this response deliberately primitive so mobile telemetry
                // transport cannot depend on desktop model serialization.
                return Results.Json(
                    new
                    {
                        connected = view.Connected,
                        error = view.Error,
                        sample = view.Sample is null
                            ? null
                            : new
                            {
                                packetId = view.Sample.PacketId,
                                speedKmh = view.Sample.SpeedKmh,
                                slipAngleDeg = view.Sample.SlipAngleDeg,
                                steeringAngleDeg = view.Sample.SteeringAngleDeg,
                                finalFfb = view.Sample.FinalFfb
                            },
                        isDrifting = view.IsDrifting,
                        serverTimeUtc = view.ServerTimeUtc
                    });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new
                    {
                        connected = false,
                        error = "Remote telemetry endpoint failed: " +
                                ex.GetType().Name + ": " + ex.Message,
                        sample = (object?)null,
                        isDrifting = false,
                        serverTimeUtc = DateTimeOffset.UtcNow
                    });
            }
        });
        app.MapGet("/api/azom", async (HttpContext context) =>
            Results.Json(await ReadAzomViewAsync(context.RequestAborted)));

        app.MapPost("/api/azom/apply", async (HttpContext context) =>
        {
            var request = await context.Request.ReadFromJsonAsync<RemoteAzomWriteRequest>(cancellationToken: context.RequestAborted);
            if (request is null)
                return Results.BadRequest(new { error = "Invalid write request." });

            var response = await ApplyRemoteSettingAsync(request, context.RequestAborted);
            return response.Ok
                ? Results.Json(response)
                : Results.Json(response, statusCode: StatusCodes.Status400BadRequest);
        });

        app.MapPost("/api/azom/revert", async (HttpContext context) =>
        {
            var response = await RevertLastRemoteSettingAsync(context.RequestAborted);
            return response.Ok
                ? Results.Json(response)
                : Results.Json(response, statusCode: StatusCodes.Status400BadRequest);
        });

        await app.StartAsync(cancellationToken);
        _app = app;

        // Best-effort connection. If AC is not running yet, the telemetry hub
        // retries automatically when desktop/remote clients request data.
        _telemetryHub.GetSnapshot();

        SetActivity($"Atomic Remote started on port {Port}. Remote writes are OFF.");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var app = _app;
        if (app is null)
            return;

        _app = null;
        RemoteWritesEnabled = false;

        try
        {
            await app.StopAsync(cancellationToken);
        }
        finally
        {
            await app.DisposeAsync();
            SetActivity("Remote server stopped. Remote writes are OFF.");
        }
    }

    private RemoteStatusView BuildStatus()
    {
        lock (_stateGate)
        {
            return new RemoteStatusView
            {
                AtomicVersion = DistributionInfo.Version,
                RemoteWritesEnabled = RemoteWritesEnabled,
                LastActivity = _lastActivity,
                Tune = _tune
            };
        }
    }

    private RemoteBehaviorView ReadBehaviorView()
    {
        TuneInput? input;
        lock (_stateGate)
            input = _currentInput;

        if (input is null)
        {
            return new RemoteBehaviorView
            {
                Ok = false,
                Error = "Select a wheelbase, wheel, drift pack and car in Windows Atomic first."
            };
        }

        try
        {
            var target = _behaviorStore.Load(input);
            target.Normalize();

            return new RemoteBehaviorView
            {
                Ok = true,
                DisplayName = target.DisplayName,
                UpdatedUtc = target.UpdatedUtc,
                FrontEndBite = target.FrontEndBite,
                RearGrip = target.RearGrip,
                SelfSteerSpeed = target.SelfSteerSpeed,
                TransitionSpeed = target.TransitionSpeed,
                AngleStability = target.AngleStability,
                ThrottleSteering = target.ThrottleSteering,
                InitiationSharpness = target.InitiationSharpness
            };
        }
        catch (Exception ex)
        {
            return new RemoteBehaviorView
            {
                Ok = false,
                Error = ex.Message
            };
        }
    }

    private RemoteActionResponse SaveBehaviorFromRemote(RemoteBehaviorUpdateRequest request)
    {
        TuneInput? input;
        lock (_stateGate)
            input = _currentInput;

        if (input is null)
        {
            return new RemoteActionResponse
            {
                Ok = false,
                Message = "Select a current car in Windows Atomic first."
            };
        }

        try
        {
            var target = new CarBehaviorTarget
            {
                FrontEndBite = request.FrontEndBite,
                RearGrip = request.RearGrip,
                SelfSteerSpeed = request.SelfSteerSpeed,
                TransitionSpeed = request.TransitionSpeed,
                AngleStability = request.AngleStability,
                ThrottleSteering = request.ThrottleSteering,
                InitiationSharpness = request.InitiationSharpness
            };

            target.Normalize();
            _behaviorStore.Save(input, target);

            SetActivity(
                $"Remote saved Desired Behavior for {input.DriftPack.Name} • {input.Car.DisplayName}. " +
                "This changes Atomic's per-car setup target only; it does not write the wheelbase.");

            return new RemoteActionResponse
            {
                Ok = true,
                Message = $"Saved Desired Behavior for {input.Car.DisplayName}."
            };
        }
        catch (Exception ex)
        {
            return new RemoteActionResponse
            {
                Ok = false,
                Message = ex.Message
            };
        }
    }

    private RemoteTelemetryView BuildTelemetryView()
    {
        var snapshot = _telemetryHub.GetSnapshot();
        var sample = snapshot.Sample;

        if (!snapshot.Connected || sample is null)
        {
            return new RemoteTelemetryView
            {
                Connected = false,
                Error = snapshot.Error ?? "Assetto Corsa telemetry unavailable.",
                ServerTimeUtc = DateTimeOffset.UtcNow
            };
        }

        var speed = FiniteOrNull(sample.SpeedKmh);
        var slip = FiniteOrNull(sample.SlipAngleDeg);
        var steering = FiniteOrNull(sample.SteeringAngleDeg);
        var ffb = FiniteOrNull(sample.FinalFfb);

        return new RemoteTelemetryView
        {
            Connected = true,
            Sample = new RemoteTelemetrySampleView
            {
                PacketId = sample.PacketId,
                SpeedKmh = speed,
                SlipAngleDeg = slip,
                SteeringAngleDeg = steering,
                FinalFfb = ffb
            },
            IsDrifting = speed.HasValue &&
                         slip.HasValue &&
                         speed.Value >= 20 &&
                         Math.Abs(slip.Value) >= 10,
            ServerTimeUtc = DateTimeOffset.UtcNow
        };
    }

    public string GetTelemetryDiagnosticText()
    {
        try
        {
            var snapshot = _telemetryHub.GetSnapshot();
            if (!snapshot.Connected || snapshot.Sample is null)
            {
                return "OFFLINE • " +
                       (snapshot.Error ?? "No shared telemetry sample is available.");
            }

            var ageMs = snapshot.UpdatedUtc.HasValue
                ? Math.Max(0, (DateTimeOffset.UtcNow - snapshot.UpdatedUtc.Value).TotalMilliseconds)
                : -1;

            return $"LIVE • packet {snapshot.Sample.PacketId} • sample age {(ageMs < 0 ? "?" : ageMs.ToString("0"))} ms";
        }
        catch (Exception ex)
        {
            return "ERROR • " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    private static double? FiniteOrNull(double value) =>
        double.IsFinite(value) ? value : null;

    private async Task<object> ReadAzomViewAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await CreateLiveController().ReadAsync(cancellationToken);
            var settings = SettingDefinitions
                .Select(def => new RemoteAzomSettingView
                {
                    PropertyName = def.PropertyName,
                    DisplayName = def.DisplayName,
                    Current = def.Getter(snapshot),
                    Min = def.Range.Min,
                    Max = def.Range.Max,
                    Unit = def.Range.Unit,
                    Writable = RemoteWritesEnabled &&
                               snapshot.SettingsReadable &&
                               string.Equals(snapshot.PropertyNamespace, "AZOM", StringComparison.OrdinalIgnoreCase) &&
                               def.Getter(snapshot).HasValue
                })
                .ToList();

            return new
            {
                ok = true,
                bridgeVersion = snapshot.BridgeVersion,
                propertyNamespace = snapshot.PropertyNamespace,
                settingsReadable = snapshot.SettingsReadable,
                baseConnected = snapshot.BaseConnected,
                remoteWritesEnabled = RemoteWritesEnabled,
                settings
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                error = ex.Message,
                remoteWritesEnabled = RemoteWritesEnabled,
                settings = Array.Empty<RemoteAzomSettingView>()
            };
        }
    }

    private async Task<RemoteAzomWriteResponse> ApplyRemoteSettingAsync(
        RemoteAzomWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!RemoteWritesEnabled)
        {
            return Failure(request, "Remote AZOM writes are disabled on the Windows PC.");
        }

        var definition = SettingDefinitions.FirstOrDefault(
            x => string.Equals(x.PropertyName, request.PropertyName, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
            return Failure(request, "That setting is not in Atomic Remote's explicit write allow-list.");

        if (request.Value < definition.Range.Min || request.Value > definition.Range.Max)
        {
            return Failure(
                request,
                $"Requested value is outside Atomic's known range {definition.Range.Display}.");
        }

        try
        {
            var controller = CreateLiveController();
            var before = await controller.ReadAsync(cancellationToken);

            if (!before.SettingsReadable ||
                !string.Equals(before.PropertyNamespace, "AZOM", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(request, "Current AZOM Base settings are not safely readable through the Atomic bridge.");
            }

            var current = definition.Getter(before);
            if (!current.HasValue || current.Value < 0)
                return Failure(request, "The requested live AZOM setting is not readable on this AZOM/base combination.");

            if (current.Value == request.Value)
            {
                SetActivity($"Remote request for {definition.DisplayName} already matched live value {request.Value}{definition.Range.Unit}.");
                return new RemoteAzomWriteResponse
                {
                    Ok = true,
                    Verified = true,
                    PropertyName = definition.PropertyName,
                    RequestedValue = request.Value,
                    LiveValue = current.Value,
                    Message = "Already matched; no write was sent."
                };
            }

            // Keep an in-memory remote-only rollback snapshot. The underlying
            // controller also preserves Atomic's normal pre-apply Revert record.
            _remoteBeforeSnapshot = before;
            _remoteChangedProperty = definition.PropertyName;

            var plan = new List<AzomApplyPlanItem>
            {
                new()
                {
                    Group = "Remote",
                    DisplayName = definition.DisplayName,
                    PropertyName = definition.PropertyName,
                    Kind = AzomApplyItemKind.Numeric,
                    CurrentInt = current.Value,
                    TargetInt = request.Value,
                    CurrentDisplay = current.Value + definition.Range.Unit,
                    TargetDisplay = request.Value + definition.Range.Unit,
                    ActionBase = definition.PropertyName,
                    FineStep = definition.FineStep,
                    CoarseStep = definition.CoarseStep,
                    CanApply = true,
                    IsSelectedForApply = true
                }
            };

            var result = await controller.ApplyAsync(plan, before, cancellationToken);
            var after = result.After ?? await controller.ReadAsync(cancellationToken);
            var live = definition.Getter(after);
            var verified = live.HasValue && live.Value == request.Value && result.VerifiedSettingsChanged == 1;

            var response = new RemoteAzomWriteResponse
            {
                Ok = verified,
                Verified = verified,
                PropertyName = definition.PropertyName,
                RequestedValue = request.Value,
                LiveValue = live,
                Message = verified
                    ? $"Verified live readback at {live}{definition.Range.Unit}."
                    : "Atomic did not verify the requested live value. The guarded batch stopped."
            };

            SetActivity(
                verified
                    ? $"REMOTE VERIFIED: {definition.DisplayName} {current}{definition.Range.Unit} → {live}{definition.Range.Unit}."
                    : $"REMOTE FAILED VERIFY: {definition.DisplayName} requested {request.Value}{definition.Range.Unit}; live {live?.ToString() ?? "N/A"}{definition.Range.Unit}.");

            AzomChanged?.Invoke(
                this,
                new RemoteAzomChangedEventArgs
                {
                    PropertyName = definition.PropertyName,
                    Value = live,
                    Verified = verified
                });

            return response;
        }
        catch (Exception ex)
        {
            SetActivity($"Remote AZOM write failed: {ex.Message}");
            return Failure(request, ex.Message);
        }
    }

    private async Task<RemoteAzomWriteResponse> RevertLastRemoteSettingAsync(CancellationToken cancellationToken)
    {
        if (!RemoteWritesEnabled)
        {
            return new RemoteAzomWriteResponse
            {
                Ok = false,
                Message = "Remote AZOM writes are disabled on the Windows PC."
            };
        }

        var beforeSnapshot = _remoteBeforeSnapshot;
        var changedProperty = _remoteChangedProperty;
        if (beforeSnapshot is null || string.IsNullOrWhiteSpace(changedProperty))
        {
            return new RemoteAzomWriteResponse
            {
                Ok = false,
                Message = "No successful remote-change snapshot is available to revert in this Atomic run."
            };
        }

        try
        {
            var controller = CreateLiveController();
            var current = await controller.ReadAsync(cancellationToken);
            var plan = controller.BuildRevertPlan(beforeSnapshot, current, new[] { changedProperty });
            var changed = plan.Where(x => x.CanApply && x.IsDifferent).ToList();

            if (changed.Count == 0)
            {
                _remoteBeforeSnapshot = null;
                _remoteChangedProperty = null;
                SetActivity("Remote Revert: the live setting already matches the saved pre-change snapshot.");
                return new RemoteAzomWriteResponse
                {
                    Ok = true,
                    Verified = true,
                    PropertyName = changedProperty,
                    Message = "Already reverted."
                };
            }

            var result = await controller.ApplyAsync(plan, current, cancellationToken);
            var after = result.After ?? await controller.ReadAsync(cancellationToken);
            var definition = SettingDefinitions.First(x => string.Equals(x.PropertyName, changedProperty, StringComparison.OrdinalIgnoreCase));
            var desired = definition.Getter(beforeSnapshot);
            var live = definition.Getter(after);
            var verified = desired.HasValue && live == desired && result.VerifiedSettingsChanged >= 1;

            if (verified)
            {
                _remoteBeforeSnapshot = null;
                _remoteChangedProperty = null;
            }

            SetActivity(verified
                ? $"REMOTE REVERT VERIFIED: {definition.DisplayName} restored to {live}{definition.Range.Unit}."
                : $"REMOTE REVERT FAILED VERIFY: {definition.DisplayName} live {live?.ToString() ?? "N/A"}{definition.Range.Unit}.");

            AzomChanged?.Invoke(
                this,
                new RemoteAzomChangedEventArgs
                {
                    PropertyName = changedProperty,
                    Value = live,
                    Verified = verified
                });

            return new RemoteAzomWriteResponse
            {
                Ok = verified,
                Verified = verified,
                PropertyName = changedProperty,
                RequestedValue = desired,
                LiveValue = live,
                Message = verified ? "Remote change reverted and verified." : "Revert did not verify; Atomic stopped the batch."
            };
        }
        catch (Exception ex)
        {
            SetActivity($"Remote Revert failed: {ex.Message}");
            return new RemoteAzomWriteResponse
            {
                Ok = false,
                PropertyName = changedProperty,
                Message = ex.Message
            };
        }
    }

    private AzomLiveController CreateLiveController()
    {
        var live = _settingsStore.Load().AzomLive ?? new AzomLiveConnectionSettings();
        SimHubActionInvoker? cliFallback = null;

        var exe = SimHubLocator.FindSimHubExe(live.SimHubExePath) ?? live.SimHubExePath;
        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
            cliFallback = new SimHubActionInvoker(exe, live.ActionDelayMs);

        return new AzomLiveController(
            new AzomBridgeClient(live.PipeName),
            live.ActionDelayMs,
            cliFallback);
    }

    private RemoteAzomWriteResponse Failure(RemoteAzomWriteRequest request, string message) =>
        new()
        {
            Ok = false,
            Verified = false,
            PropertyName = request.PropertyName,
            RequestedValue = request.Value,
            Message = message
        };

    private bool TokenMatches(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied) || supplied.Length != PairToken.Length)
            return false;

        var a = Encoding.UTF8.GetBytes(supplied);
        var b = Encoding.UTF8.GetBytes(PairToken);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private bool PairingCodeMatches(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied) || supplied.Length != PairingCode.Length)
            return false;

        var a = Encoding.UTF8.GetBytes(supplied.Trim());
        var b = Encoding.UTF8.GetBytes(PairingCode);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private void SetActivity(string activity)
    {
        lock (_stateGate)
            _lastActivity = activity;
        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10 ||
                   (b[0] == 172 && b[1] is >= 16 and <= 31) ||
                   (b[0] == 192 && b[1] == 168) ||
                   (b[0] == 169 && b[1] == 254);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal)
                return true;
            var b = address.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC; // fc00::/7 unique-local range
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
        public string Code { get; set; } = "";
    }

    // Test-build write surface: only the core/wheelbase controls Atomic already
    // knows how to range-check and verify. Preferences, EQ, curve nodes and
    // undocumented controls remain read-only/not exposed remotely for now.
    private static readonly IReadOnlyList<RemoteSettingDefinition> SettingDefinitions =
    [
        new("AZOM.FfbStrength", "Game FFB Strength", AzomSettingCatalog.GameFfbStrength, x => x.FfbStrength, 5, 10),
        new("AZOM.Torque", "Base Torque Output", AzomSettingCatalog.BaseTorqueOutput, x => x.Torque, 5, 10),
        new("AZOM.Rotation", "Wheel Rotation Angle", AzomSettingCatalog.WheelRotationAngle, x => x.Rotation, 90, 180),
        new("AZOM.WheelSpeedLimit", "Maximum Wheel Speed", AzomSettingCatalog.MaximumWheelSpeed, x => x.WheelSpeedLimit, 5, 10),
        new("AZOM.Interpolation", "Interpolation", AzomSettingCatalog.Interpolation, x => x.Interpolation, 1, 2),
        new("AZOM.Damper", "Wheel Damper", AzomSettingCatalog.WheelDamper, x => x.Damper, 5, 10),
        new("AZOM.Friction", "Wheel Friction", AzomSettingCatalog.WheelFriction, x => x.Friction, 5, 10),
        new("AZOM.Inertia", "Natural Inertia", AzomSettingCatalog.NaturalInertia, x => x.Inertia, 10, 50),
        new("AZOM.SpeedDamping", "High-Speed Damping", AzomSettingCatalog.HighSpeedDampingLevel, x => x.SpeedDamping, 5, 10),
        new("AZOM.SpeedDampingPoint", "High-Speed Trigger", AzomSettingCatalog.HighSpeedTriggerSpeed, x => x.SpeedDampingPoint, 10, 50)
    ];
}
