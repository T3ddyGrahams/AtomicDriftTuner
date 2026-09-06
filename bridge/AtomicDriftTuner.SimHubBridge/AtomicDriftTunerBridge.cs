using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using GameReaderCommon;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimHub.Plugins;

namespace AtomicDriftTuner.SimHubBridge
{
    [PluginName("Atomic Drift Tuner Bridge")]
    [PluginDescription("Bridge exposing AZOM state, relaying SimHub actions, and providing guarded/readback-verified AZOM compatibility writes for Atomic Drift Tuner.")]
    [PluginAuthor("Atomic Drift Tuner")]
    public sealed class AtomicDriftTunerBridge : IPlugin, IDataPlugin
    {
        private const string PipeName = "AtomicDriftTuner.AzomBridge.v1";
        private const string BridgeVersion = "0.8.2-beta.1";

        private const int MaxRequestChars = 16 * 1024;
        private const int MaxAzomNameLength = 256;
        private const int MaxErrorMessageChars = 1000;

        private const int ActionWaitTimeoutMs = 3000;
        private const int DirectWriteWaitTimeoutMs = 3500;

        private const int RequestPending = 0;
        private const int RequestExecuting = 1;
        private const int RequestCompleted = 2;
        private const int RequestCancelled = 3;
        private Thread? _serverThread;
        private volatile bool _stop;
        private NamedPipeServerStream? _activeServer;
        private readonly object _serverLock = new object();

        // Live AZOM values are captured only from SimHub's own DataUpdate thread.
        // The pipe thread never touches AZOM internals or SimHub property getters.
        private readonly object _snapshotLock = new object();
        private object? _cachedSnapshot;
        private int _lastCaptureTick;

        // Desktop mutation requests are queued here and executed only from
        // SimHub's DataUpdate thread. Every request has a pending/executing/
        // completed/cancelled state so a pipe timeout can cancel work that has
        // not started yet. A timed-out pending request is never allowed to wake
        // up on a later DataUpdate and surprise the wheelbase.
        private readonly ConcurrentQueue<ActionRequest> _pendingActions =
            new ConcurrentQueue<ActionRequest>();

        private readonly ConcurrentQueue<DirectSettingRequest> _pendingDirectSets =
            new ConcurrentQueue<DirectSettingRequest>();

        private abstract class PendingRequest
        {
            private int _state = RequestPending;

            public readonly ManualResetEventSlim Completed =
                new ManualResetEventSlim(false);

            public int DeadlineTick;

            public string? Error;

            public bool IsExpired(int now)
            {
                // All bridge request lifetimes are only a few seconds, so the
                // normal unchecked TickCount subtraction pattern remains safe
                // across the ~24.9-day signed wrap boundary.
                return unchecked(
                    now -
                    DeadlineTick) >= 0;
            }

            public bool IsExecuting
            {
                get
                {
                    return Volatile.Read(ref _state) == RequestExecuting;
                }
            }

            public bool IsCompleted
            {
                get
                {
                    return Volatile.Read(ref _state) == RequestCompleted;
                }
            }

            public bool TryBeginExecution()
            {
                return Interlocked.CompareExchange(
                    ref _state,
                    RequestExecuting,
                    RequestPending) == RequestPending;
            }

            public bool TryCancelPending(string error)
            {
                if (Interlocked.CompareExchange(
                        ref _state,
                        RequestCancelled,
                        RequestPending) != RequestPending)
                {
                    return false;
                }

                Error = error;
                Completed.Set();
                return true;
            }

            public void Complete()
            {
                Interlocked.Exchange(
                    ref _state,
                    RequestCompleted);

                Completed.Set();
            }
        }

        private sealed class ActionRequest : PendingRequest
        {
            public string ActionName = "";
        }

        // Bridge-side guard for the reflection compatibility path. We never
        // block SimHub's DataUpdate thread; requests are deferred to a later
        // update until the minimum spacing has elapsed.
        private const int DirectWriteMinIntervalMs = 120;
        private int _lastDirectWriteTick;

        private sealed class DirectSettingRequest : PendingRequest
        {
            public string PropertyName = "";
            public int? TargetInt;
            public bool? TargetBool;
            public string? Method;
            public bool Suppressed;
        }

        public PluginManager PluginManager { get; set; } = null!;

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            _stop = false;
            _serverThread = new Thread(ServerLoop) { IsBackground = true, Name = "AtomicDriftTunerBridge" };
            _serverThread.Start();
            SimHub.Logging.Current.Info("[Atomic Drift Tuner Bridge] Started named-pipe bridge.");
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // Execute all requested writes inside the already-running SimHub
            // process. Direct compatibility writes are bounded and only use
            // known AZOM Base-setting commit methods.
            DrainPendingDirectSets();
            DrainPendingActions(pluginManager);

            // Capture live AZOM state at ~5 Hz.
            int now = Environment.TickCount;
            if (unchecked(now - _lastCaptureTick) < 200)
                return;
            _lastCaptureTick = now;

            try
            {
                var snapshot = CaptureSnapshotOnSimHubThread();
                lock (_snapshotLock)
                    _cachedSnapshot = snapshot;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("[Atomic Drift Tuner Bridge] Snapshot capture failed: " + ex);
                lock (_snapshotLock)
                    _cachedSnapshot = DiagnosticSnapshot("capture error: " + ex.GetType().Name);
            }
        }

        private void DrainPendingActions(PluginManager pluginManager)
        {
            int processed = 0;

            while (processed < 8 &&
                   _pendingActions.TryDequeue(out var request))
            {
                processed++;

                int now =
                    Environment.TickCount;

                if (request.IsExpired(now))
                {
                    request.TryCancelPending(
                        "ADT bridge action request expired before SimHub began execution; no action was performed.");

                    continue;
                }

                if (!request.TryBeginExecution())
                    continue;

                try
                {
                    string validationError;
                    if (!TryValidateActionName(
                            request.ActionName,
                            out validationError))
                    {
                        throw new InvalidOperationException(
                            validationError);
                    }

                    pluginManager.TriggerAction(
                        request.ActionName);

                    SimHub.Logging.Current.Info(
                        "[Atomic Drift Tuner Bridge] Triggered approved action " +
                        request.ActionName);
                }
                catch (Exception ex)
                {
                    request.Error =
                        SafeError(
                            ex.GetBaseException().Message);

                    SimHub.Logging.Current.Error(
                        "[Atomic Drift Tuner Bridge] Action failed " +
                        request.ActionName + ": " + ex);
                }
                finally
                {
                    request.Complete();
                }
            }
        }

        private void DrainPendingDirectSets()
        {
            if (!_pendingDirectSets.TryDequeue(out var request))
                return;

            int requestNow =
                Environment.TickCount;

            if (request.IsExpired(requestNow))
            {
                request.TryCancelPending(
                    "ADT bridge direct-write request expired before SimHub began execution; no setting write was performed.");

                return;
            }

            int now =
                Environment.TickCount;

            int elapsed =
                unchecked(
                    now -
                    _lastDirectWriteTick);

            if (_lastDirectWriteTick != 0 &&
                elapsed >= 0 &&
                elapsed < DirectWriteMinIntervalMs)
            {
                // Do not sleep/block SimHub's update thread. Keep the request
                // pending and let a later update process it. Its deadline still
                // applies while it is waiting here.
                _pendingDirectSets.Enqueue(
                    request);

                return;
            }

            if (!request.TryBeginExecution())
                return;

            try
            {
                string validationError;
                if (!TryValidateDirectTarget(
                        request.PropertyName,
                        request.TargetInt,
                        request.TargetBool,
                        out validationError))
                {
                    throw new InvalidOperationException(
                        validationError);
                }

                if (IsDirectTargetAlreadyLive(
                        request.PropertyName,
                        request.TargetInt,
                        request.TargetBool))
                {
                    request.Suppressed = true;
                    request.Method =
                        "ADT write guard: duplicate target already live; write suppressed";

                    SimHub.Logging.Current.Info(
                        "[Atomic Drift Tuner Bridge] Suppressed duplicate AZOM target " +
                        request.PropertyName);

                    return;
                }

                string method;
                if (!TryApplyAzomSettingDirect(
                        request.PropertyName,
                        request.TargetInt,
                        request.TargetBool,
                        out method,
                        out var error))
                {
                    throw new InvalidOperationException(
                        error ??
                        "AZOM direct setting compatibility path was not available.");
                }

                request.Method =
                    method;

                // Only count an actual commit against bridge write spacing.
                if (method.IndexOf(
                        "already at target",
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _lastDirectWriteTick =
                        Environment.TickCount;
                }

                SimHub.Logging.Current.Info(
                    "[Atomic Drift Tuner Bridge] Exact AZOM commit " +
                    request.PropertyName + " via " + method);
            }
            catch (Exception ex)
            {
                request.Error =
                    SafeError(
                        ex.GetBaseException().Message);

                SimHub.Logging.Current.Error(
                    "[Atomic Drift Tuner Bridge] Direct AZOM setting failed " +
                    request.PropertyName + ": " + ex);
            }
            finally
            {
                request.Complete();
            }
        }

        private bool IsDirectTargetAlreadyLive(
            string propertyName,
            int? targetInt,
            bool? targetBool)
        {
            try
            {
                if (targetInt.HasValue)
                {
                    var current =
                        Int(propertyName);

                    if (current.HasValue &&
                        current.Value == targetInt.Value)
                    {
                        return true;
                    }
                }

                if (targetBool.HasValue)
                {
                    if (string.Equals(
                            propertyName,
                            "AZOM.WorkMode",
                            StringComparison.Ordinal))
                    {
                        var currentMode =
                            Int(propertyName);

                        int wanted =
                            targetBool.Value
                                ? 1
                                : 0;

                        if (currentMode.HasValue &&
                            currentMode.Value == wanted)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        var current =
                            Bool(propertyName);

                        if (current.HasValue &&
                            current.Value == targetBool.Value)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // If the public property cannot be read, fall through to the
                // exact AZOM path, which performs its own numeric no-op check.
            }

            return false;
        }

        private sealed class NumericRange
        {
            public NumericRange(
                int minimum,
                int maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            public int Minimum { get; }

            public int Maximum { get; }

            public bool Contains(int value)
            {
                return value >= Minimum &&
                       value <= Maximum;
            }
        }

        // Exact display-unit ranges ADT supports for live AZOM writes.
        private static readonly Dictionary<string, NumericRange>
            DirectNumericRanges =
            new Dictionary<string, NumericRange>(StringComparer.Ordinal)
            {
                { "AZOM.FfbStrength", new NumericRange(0, 100) },
                { "AZOM.Torque", new NumericRange(50, 100) },
                { "AZOM.Rotation", new NumericRange(60, 2700) },
                { "AZOM.WheelSpeedLimit", new NumericRange(0, 200) },
                { "AZOM.Interpolation", new NumericRange(0, 10) },
                { "AZOM.GearshiftVibration", new NumericRange(0, 5) },

                { "AZOM.Damper", new NumericRange(0, 100) },
                { "AZOM.Friction", new NumericRange(0, 100) },
                { "AZOM.Inertia", new NumericRange(100, 500) },
                { "AZOM.Spring", new NumericRange(0, 100) },

                { "AZOM.GameDamper", new NumericRange(0, 100) },
                { "AZOM.GameFriction", new NumericRange(0, 100) },
                { "AZOM.GameInertia", new NumericRange(0, 100) },
                { "AZOM.GameSpring", new NumericRange(0, 100) },

                { "AZOM.NaturalInertia", new NumericRange(100, 4000) },
                { "AZOM.SoftLimitStiffness", new NumericRange(1, 10) },
                { "AZOM.SpeedDamping", new NumericRange(0, 100) },
                { "AZOM.SpeedDampingPoint", new NumericRange(0, 400) },
                { "AZOM.RoadSensitivity", new NumericRange(0, 10) },

                { "AZOM.Equalizer1", new NumericRange(0, 400) },
                { "AZOM.Equalizer2", new NumericRange(0, 400) },
                { "AZOM.Equalizer3", new NumericRange(0, 400) },
                { "AZOM.Equalizer4", new NumericRange(0, 400) },
                { "AZOM.Equalizer5", new NumericRange(0, 400) },
                { "AZOM.Equalizer6", new NumericRange(0, 400) },
                { "AZOM.Equalizer7", new NumericRange(0, 400) },
                { "AZOM.Equalizer8", new NumericRange(0, 400) },
                { "AZOM.Equalizer9", new NumericRange(0, 400) },
                { "AZOM.Equalizer10", new NumericRange(0, 400) },

                { "AZOM.FfbCurveX1", new NumericRange(0, 100) },
                { "AZOM.FfbCurveX2", new NumericRange(0, 100) },
                { "AZOM.FfbCurveX3", new NumericRange(0, 100) },
                { "AZOM.FfbCurveX4", new NumericRange(0, 100) },
                { "AZOM.FfbCurveY1", new NumericRange(0, 100) },
                { "AZOM.FfbCurveY2", new NumericRange(0, 100) },
                { "AZOM.FfbCurveY3", new NumericRange(0, 100) },
                { "AZOM.FfbCurveY4", new NumericRange(0, 100) },
                { "AZOM.FfbCurveY5", new NumericRange(0, 100) }
            };

        private static readonly HashSet<string> DirectToggleAllowList =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "AZOM.Protection",
                "AZOM.FfbReverse",
                "AZOM.SoftLimitRetain",
                "AZOM.BaseStatusLed",
                "AZOM.Bluetooth",
                "AZOM.WorkMode"
            };

        private static readonly HashSet<string> ToggleActionAllowList =
            new HashSet<string>(StringComparer.Ordinal)
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

        private static bool TryValidateDirectTarget(
            string propertyName,
            int? targetInt,
            bool? targetBool,
            out string error)
        {
            error = "";

            if (!IsValidAzomName(propertyName))
            {
                error =
                    "Invalid AZOM property name.";
                return false;
            }

            if (DirectNumericRanges.TryGetValue(
                    propertyName,
                    out var range))
            {
                if (!targetInt.HasValue ||
                    targetBool.HasValue)
                {
                    error =
                        propertyName +
                        " requires exactly one numeric target.";
                    return false;
                }

                if (!range.Contains(
                        targetInt.Value))
                {
                    error =
                        "ADT refused " +
                        propertyName +
                        "=" +
                        targetInt.Value +
                        "; supported range is " +
                        range.Minimum +
                        ".." +
                        range.Maximum +
                        ".";
                    return false;
                }

                return true;
            }

            if (DirectToggleAllowList.Contains(
                    propertyName))
            {
                if (!targetBool.HasValue ||
                    targetInt.HasValue)
                {
                    error =
                        propertyName +
                        " requires exactly one boolean target.";
                    return false;
                }

                return true;
            }

            error =
                "Direct bridge writes are not allowed for " +
                propertyName +
                ".";

            return false;
        }

        private static bool TryValidateActionName(
            string actionName,
            out string error)
        {
            error = "";

            if (!IsValidAzomName(
                    actionName))
            {
                error =
                    "Invalid AZOM action name.";
                return false;
            }

            if (ToggleActionAllowList.Contains(
                    actionName))
            {
                return true;
            }

            string[] suffixes =
            {
                "UpCoarse",
                "DownCoarse",
                "Up",
                "Down"
            };

            foreach (var suffix in suffixes)
            {
                if (!actionName.EndsWith(
                        suffix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string propertyName =
                    actionName.Substring(
                        0,
                        actionName.Length -
                        suffix.Length);

                if (DirectNumericRanges.ContainsKey(
                        propertyName))
                {
                    return true;
                }
            }

            error =
                "Bridge action is not in ADT's approved AZOM action set: " +
                actionName +
                ".";

            return false;
        }

        private static bool IsValidAzomName(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
                return false;

            if (value.Length > MaxAzomNameLength ||
                !value.StartsWith(
                    "AZOM.",
                    StringComparison.Ordinal))
                return false;

            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character) &&
                    character != '.' &&
                    character != '_' &&
                    character != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryApplyAzomSettingDirect(
            string propertyName,
            int? targetInt,
            bool? targetBool,
            out string methodUsed,
            out string? error)
        {
            methodUsed = "";
            error = null;

            string validationError;
            if (!TryValidateDirectTarget(
                    propertyName,
                    targetInt,
                    targetBool,
                    out validationError))
            {
                error =
                    validationError;
                return false;
            }

            try
            {
                if (!TryGetAzomRuntime(
                        out var azomAssembly,
                        out var pluginType,
                        out var pluginInstance,
                        out var data))
                {
                    error = "AZOM runtime objects were not available.";
                    return false;
                }

                string settingName =
                    propertyName.StartsWith("AZOM.", StringComparison.Ordinal)
                        ? propertyName.Substring(5)
                        : propertyName;

                // WorkMode is registered by AZOM outside BaseSettingCatalog.
                // Atomic models targetBool=true as Standby Mode ON (raw 1).
                if (string.Equals(settingName, "WorkMode", StringComparison.Ordinal) &&
                    targetBool.HasValue)
                {
                    int raw = targetBool.Value ? 1 : 0;
                    var field = data.GetType().GetField(
                        "WorkMode",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (field == null)
                    {
                        error = "AZOM WorkMode field was not found.";
                        return false;
                    }

                    int current = Convert.ToInt32(field.GetValue(data));
                    if (current != raw)
                    {
                        field.SetValue(data, raw);

                        var write = pluginType.GetMethod(
                            "WriteIfBaseConnected",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(string), typeof(int) },
                            null);
                        var save = pluginType.GetMethod(
                            "SaveSettings",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                        if (write == null || save == null)
                        {
                            error = "AZOM WorkMode commit methods were not found.";
                            return false;
                        }

                        write.Invoke(pluginInstance, new object[] { "main-set-work-mode", raw });
                        save.Invoke(pluginInstance, null);
                    }

                    methodUsed = "MozaPlugin WorkMode exact commit";
                    return true;
                }

                // RoadSensitivity is intentionally separate in AZOM because
                // changing the sensitivity preset also rewrites the EQ curve.
                if (targetInt.HasValue &&
                    string.Equals(
                        settingName,
                        "RoadSensitivity",
                        StringComparison.Ordinal))
                {
                    var registrar =
                        CreateAzomRegistrar(
                            azomAssembly,
                            pluginType,
                            pluginInstance,
                            out var registrarType);

                    if (registrar == null || registrarType == null)
                    {
                        error = "Could not construct AZOM SimHubRegistrar.";
                        return false;
                    }

                    var method =
                        registrarType.GetMethod(
                            "StepRoadSensitivity",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic |
                            BindingFlags.Public);

                    if (method == null)
                    {
                        error =
                            "AZOM StepRoadSensitivity method was not found.";
                        return false;
                    }

                    int current =
                        ReadRoadSensitivityPreset(
                            azomAssembly,
                            data);

                    int delta =
                        current >= 0
                            ? targetInt.Value - current
                            : (targetInt.Value >= 5 ? 1 : -1);

                    if (delta != 0)
                        method.Invoke(
                            registrar,
                            new object[] { delta });

                    methodUsed =
                        "SimHubRegistrar.StepRoadSensitivity";
                    return true;
                }

                // Current AZOM exposes every Base numeric setting through
                // BaseSettingCatalog.Numeric and commits it through the live
                // SimHubRegistrar.StepBaseSetting(def, delta) path.
                if (targetInt.HasValue &&
                    TryInvokeModernNumericSetting(
                        azomAssembly,
                        pluginType,
                        pluginInstance,
                        data,
                        settingName,
                        targetInt.Value,
                        out methodUsed,
                        out error))
                {
                    return true;
                }

                // Compatibility with older AZOM builds that used individual
                // StepFfbStrength / StepTorque / StepRotation methods.
                if (targetInt.HasValue &&
                    TryInvokeExplicitV157NumericMethod(
                        azomAssembly,
                        pluginType,
                        pluginInstance,
                        data,
                        settingName,
                        targetInt.Value,
                        out methodUsed,
                        out error))
                {
                    return true;
                }

                // Generic toggle path for builds that expose BaseSettingCatalog.Toggles.
                if (targetBool.HasValue &&
                    TryInvokeModernToggleSetting(
                        azomAssembly,
                        pluginType,
                        pluginInstance,
                        settingName,
                        targetBool.Value,
                        out methodUsed,
                        out error))
                {
                    return true;
                }

                // Compatibility with AZOM builds predating the generic
                // BaseSettingCatalog action path.
                if (targetInt.HasValue &&
                    TryInvokeLegacyNumericMethod(
                        azomAssembly,
                        pluginType,
                        pluginInstance,
                        data,
                        settingName,
                        targetInt.Value,
                        out methodUsed,
                        out error))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(error))
                {
                    error =
                        "No compatible AZOM internal commit method was found " +
                        "for " + propertyName + ".";
                }

                return false;
            }
            catch (TargetInvocationException ex)
            {
                error =
                    (ex.InnerException ?? ex).GetBaseException().Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
                return false;
            }
        }

        private static bool TryGetAzomRuntime(
            out Assembly azomAssembly,
            out Type pluginType,
            out object pluginInstance,
            out object data)
        {
            azomAssembly = null!;
            pluginType = null!;
            pluginInstance = null!;
            data = null!;

            azomAssembly =
                AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(
                        a => string.Equals(
                            a.GetName().Name,
                            "MozaPlugin",
                            StringComparison.OrdinalIgnoreCase))!;

            if (azomAssembly == null)
                return false;

            pluginType =
                azomAssembly.GetType(
                    "MozaPlugin.MozaPlugin",
                    throwOnError: false)!;

            if (pluginType == null)
                return false;

            object? instance = null;

            var instanceField =
                pluginType.GetField(
                    "Instance",
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (instanceField != null)
                instance = instanceField.GetValue(null);

            if (instance == null)
            {
                var instanceProp =
                    pluginType.GetProperty(
                        "Instance",
                        BindingFlags.Static |
                        BindingFlags.Public |
                        BindingFlags.NonPublic);

                if (instanceProp != null &&
                    instanceProp.GetIndexParameters().Length == 0)
                {
                    instance =
                        instanceProp.GetValue(
                            null,
                            null);
                }
            }

            if (instance == null)
                return false;

            object? liveData =
                GetFieldOrProperty(
                    instance,
                    "Data");

            if (liveData == null)
                return false;

            pluginInstance = instance;
            data = liveData;
            return true;
        }

        private static object? CreateAzomRegistrar(
            Assembly azomAssembly,
            Type pluginType,
            object pluginInstance,
            out Type? registrarType)
        {
            // AZOM owns a live SimHubRegistrar instance in MozaPlugin._simHubRegistrar.
            // Reuse that object first. This matters because it is the registrar AZOM
            // initialized and registered with SimHub; constructing a second one is
            // unnecessary and can diverge from the running plugin state.
            var liveField =
                pluginType.GetField(
                    "_simHubRegistrar",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (liveField != null)
            {
                var live = liveField.GetValue(pluginInstance);
                if (live != null)
                {
                    registrarType = live.GetType();
                    return live;
                }
            }

            // Compatibility fallback for AZOM builds that do not retain the field.
            registrarType =
                azomAssembly.GetTypes()
                    .FirstOrDefault(
                        t => string.Equals(
                            t.Name,
                            "SimHubRegistrar",
                            StringComparison.Ordinal) &&
                             string.Equals(
                                t.Namespace,
                                "MozaPlugin",
                                StringComparison.Ordinal));

            if (registrarType == null)
                return null;

            var ctor =
                registrarType
                    .GetConstructors(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic)
                    .FirstOrDefault(
                        c =>
                        {
                            var p = c.GetParameters();
                            return p.Length == 1 &&
                                   p[0].ParameterType
                                       .IsInstanceOfType(pluginInstance);
                        });

            return ctor?.Invoke(
                new object[] { pluginInstance });
        }

        private static bool TryInvokeModernNumericSetting(
            Assembly azomAssembly,
            Type pluginType,
            object pluginInstance,
            object data,
            string settingName,
            int target,
            out string methodUsed,
            out string? error)
        {
            methodUsed = "";
            error = null;

            var catalogType =
                FindTypeByName(
                    azomAssembly,
                    "BaseSettingCatalog");

            if (catalogType == null)
            {
                error = null;
                return false;
            }

            var numericDefs =
                GetStaticEnumerable(
                    catalogType,
                    "Numeric");

            if (numericDefs == null)
                return false;

            object? def =
                numericDefs.Cast<object>()
                    .FirstOrDefault(
                        x => string.Equals(
                            GetStringMember(
                                x,
                                "Name"),
                            settingName,
                            StringComparison.Ordinal));

            if (def == null)
            {
                error =
                    "AZOM BaseSettingCatalog did not contain " +
                    settingName + ".";
                return false;
            }

            int current =
                GetNumericDefinitionDisplayValue(
                    def,
                    data);

            int delta = target - current;

            if (delta == 0)
            {
                methodUsed =
                    "BaseSettingCatalog already at target";
                return true;
            }

            var registrar =
                CreateAzomRegistrar(
                    azomAssembly,
                    pluginType,
                    pluginInstance,
                    out var registrarType);

            if (registrar == null ||
                registrarType == null)
            {
                error =
                    "Could not construct AZOM SimHubRegistrar.";
                return false;
            }

            var stepMethod =
                registrarType
                    .GetMethods(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic)
                    .FirstOrDefault(
                        m =>
                        {
                            if (!string.Equals(
                                    m.Name,
                                    "StepBaseSetting",
                                    StringComparison.Ordinal))
                                return false;

                            var p = m.GetParameters();
                            return p.Length == 2 &&
                                   p[1].ParameterType ==
                                       typeof(int) &&
                                   p[0].ParameterType
                                       .IsInstanceOfType(def);
                        });

            if (stepMethod == null)
            {
                error =
                    "AZOM StepBaseSetting method was not found on registrar type " +
                    registrarType.FullName + ".";
                return false;
            }

            // AZOM's method accepts any signed delta in display units,
            // clamps once, writes the exact resulting raw value, and saves.
            stepMethod.Invoke(
                registrar,
                new object[] { def, delta });

            methodUsed =
                "SimHubRegistrar.StepBaseSetting(" +
                settingName + ", delta=" +
                delta + ")";

            return true;
        }

        private static bool TryInvokeModernToggleSetting(
            Assembly azomAssembly,
            Type pluginType,
            object pluginInstance,
            string settingName,
            bool target,
            out string methodUsed,
            out string? error)
        {
            methodUsed = "";
            error = null;

            var catalogType =
                FindTypeByName(
                    azomAssembly,
                    "BaseSettingCatalog");

            if (catalogType == null)
                return false;

            var toggleDefs =
                GetStaticEnumerable(
                    catalogType,
                    "Toggles");

            if (toggleDefs == null)
                return false;

            object? def =
                toggleDefs.Cast<object>()
                    .FirstOrDefault(
                        x => string.Equals(
                            GetStringMember(
                                x,
                                "Name"),
                            settingName,
                            StringComparison.Ordinal));

            if (def == null)
            {
                error =
                    "AZOM toggle catalog did not contain " +
                    settingName + ".";
                return false;
            }

            var registrar =
                CreateAzomRegistrar(
                    azomAssembly,
                    pluginType,
                    pluginInstance,
                    out var registrarType);

            if (registrar == null ||
                registrarType == null)
            {
                error =
                    "Could not construct AZOM SimHubRegistrar.";
                return false;
            }

            var setMethod =
                registrarType
                    .GetMethods(
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic)
                    .FirstOrDefault(
                        m =>
                        {
                            if (!string.Equals(
                                    m.Name,
                                    "SetToggle",
                                    StringComparison.Ordinal))
                                return false;

                            var p = m.GetParameters();
                            return p.Length == 2 &&
                                   p[1].ParameterType ==
                                       typeof(bool) &&
                                   p[0].ParameterType
                                       .IsInstanceOfType(def);
                        });

            if (setMethod == null)
            {
                error =
                    "AZOM SetToggle method was not found.";
                return false;
            }

            setMethod.Invoke(
                registrar,
                new object[] { def, target });

            methodUsed =
                "SimHubRegistrar.SetToggle(" +
                settingName + "=" +
                target + ")";

            return true;
        }

        private static bool TryInvokeExplicitV157NumericMethod(
            Assembly azomAssembly,
            Type pluginType,
            object pluginInstance,
            object data,
            string settingName,
            int target,
            out string methodUsed,
            out string? error)
        {
            methodUsed = "";
            error = null;

            string? methodName = null;
            int? current = null;

            switch (settingName)
            {
                case "FfbStrength":
                    methodName = "StepFfbStrength";
                    var rawFfb = ReadFieldInt(data, "FfbStrength");
                    current = rawFfb.HasValue
                        ? (int?)Math.Round(rawFfb.Value / 10.0)
                        : null;
                    break;

                case "Torque":
                    methodName = "StepTorque";
                    current = ReadFieldInt(data, "Torque");
                    break;

                case "Rotation":
                    methodName = "StepRotation";
                    var rawLimit = ReadFieldInt(data, "Limit");
                    current = rawLimit.HasValue
                        ? rawLimit.Value * 2
                        : (int?)null;
                    break;

                default:
                    return false;
            }

            if (!current.HasValue)
            {
                error = "AZOM live value for " + settingName + " was unavailable.";
                return false;
            }

            var registrar = CreateAzomRegistrar(
                azomAssembly,
                pluginType,
                pluginInstance,
                out var registrarType);

            if (registrar == null || registrarType == null)
            {
                error = "Could not construct AZOM SimHubRegistrar.";
                return false;
            }

            var method = registrarType.GetMethod(
                methodName!,
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.Public);

            if (method == null)
            {
                error = "AZOM " + methodName + " method was not found.";
                return false;
            }

            int delta = target - current.Value;
            if (delta != 0)
                method.Invoke(registrar, new object[] { delta });

            methodUsed =
                "SimHubRegistrar." + methodName +
                "(delta=" + delta + ")";

            return true;
        }

        private static bool TryInvokeLegacyNumericMethod(
            Assembly azomAssembly,
            Type pluginType,
            object pluginInstance,
            object data,
            string settingName,
            int target,
            out string methodUsed,
            out string? error)
        {
            methodUsed = "";
            error = null;

            string? methodName = null;
            int? current = null;

            switch (settingName)
            {
                case "FfbStrength":
                    methodName = "StepFfbStrength";
                    var rawFfb =
                        ReadFieldInt(
                            data,
                            "FfbStrength");
                    current =
                        rawFfb.HasValue
                            ? (int?)Math.Round(
                                rawFfb.Value / 10.0)
                            : null;
                    break;

                case "Torque":
                    methodName = "StepTorque";
                    current =
                        ReadFieldInt(
                            data,
                            "Torque");
                    break;

                case "Rotation":
                    methodName = "StepRotation";
                    var rawLimit =
                        ReadFieldInt(
                            data,
                            "Limit");
                    current =
                        rawLimit.HasValue
                            ? rawLimit.Value * 2
                            : (int?)null;
                    break;
            }

            if (methodName == null ||
                !current.HasValue)
                return false;

            var registrar =
                CreateAzomRegistrar(
                    azomAssembly,
                    pluginType,
                    pluginInstance,
                    out var registrarType);

            if (registrar == null ||
                registrarType == null)
            {
                error =
                    "Could not construct AZOM SimHubRegistrar.";
                return false;
            }

            var method =
                registrarType.GetMethod(
                    methodName,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (method == null)
                return false;

            int delta =
                target - current.Value;

            if (delta != 0)
            {
                method.Invoke(
                    registrar,
                    new object[] { delta });
            }

            methodUsed =
                "SimHubRegistrar." +
                methodName +
                "(delta=" + delta + ")";

            return true;
        }

        private static Type? FindTypeByName(
            Assembly assembly,
            string name)
        {
            try
            {
                return assembly.GetTypes()
                    .FirstOrDefault(
                        t => string.Equals(
                            t.Name,
                            name,
                            StringComparison.Ordinal));
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types
                    .Where(t => t != null)
                    .FirstOrDefault(
                        t => string.Equals(
                            t!.Name,
                            name,
                            StringComparison.Ordinal));
            }
        }

        private static IEnumerable? GetStaticEnumerable(
            Type type,
            string memberName)
        {
            var field =
                type.GetField(
                    memberName,
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (field != null)
                return field.GetValue(null)
                    as IEnumerable;

            var prop =
                type.GetProperty(
                    memberName,
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (prop != null &&
                prop.GetIndexParameters().Length == 0)
            {
                return prop.GetValue(
                    null,
                    null)
                    as IEnumerable;
            }

            return null;
        }

        private static string? GetStringMember(
            object target,
            string name)
        {
            var value =
                GetFieldOrProperty(
                    target,
                    name);

            return value?.ToString();
        }

        private static object? GetFieldOrProperty(
            object target,
            string name)
        {
            var type = target.GetType();

            var field =
                type.GetField(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (field != null)
                return field.GetValue(target);

            var prop =
                type.GetProperty(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (prop != null &&
                prop.GetIndexParameters().Length == 0)
            {
                return prop.GetValue(
                    target,
                    null);
            }

            return null;
        }

        private static int GetNumericDefinitionDisplayValue(
            object def,
            object data)
        {
            // AZOM BaseSettingCatalog.NumericSetting stores GetRaw and ToDisplay
            // as public delegate FIELDS:
            //   Func<MozaData,int> GetRaw
            //   Func<int,int> ToDisplay
            // They are not methods. Invoke them as delegates.
            var getRawObj =
                GetFieldOrProperty(
                    def,
                    "GetRaw");

            var toDisplayObj =
                GetFieldOrProperty(
                    def,
                    "ToDisplay");

            if (!(getRawObj is Delegate getRaw))
            {
                throw new InvalidOperationException(
                    "AZOM numeric setting GetRaw delegate was not found.");
            }

            if (!(toDisplayObj is Delegate toDisplay))
            {
                throw new InvalidOperationException(
                    "AZOM numeric setting ToDisplay delegate was not found.");
            }

            var rawObj =
                getRaw.DynamicInvoke(
                    data);

            if (rawObj == null)
            {
                throw new InvalidOperationException(
                    "AZOM numeric setting GetRaw returned no value.");
            }

            int raw =
                Convert.ToInt32(
                    rawObj);

            var displayObj =
                toDisplay.DynamicInvoke(
                    raw);

            if (displayObj == null)
            {
                throw new InvalidOperationException(
                    "AZOM numeric setting ToDisplay returned no value.");
            }

            return Convert.ToInt32(
                displayObj);
        }

        private static int ReadRoadSensitivityPreset(
            Assembly azomAssembly,
            object data)
        {
            var raw =
                ReadFieldInt(
                    data,
                    "RoadSensitivity");

            if (!raw.HasValue)
                return -1;

            var catalogType =
                FindTypeByName(
                    azomAssembly,
                    "BaseSettingCatalog");

            var method =
                catalogType?.GetMethod(
                    "RoadSensitivityPresetFromRaw",
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (method == null)
                return -1;

            var value =
                method.Invoke(
                    null,
                    new object[] { raw.Value });

            return value == null
                ? -1
                : Convert.ToInt32(value);
        }

        public void End(PluginManager pluginManager)
        {
            _stop = true;

            CancelPendingRequests(
                "ADT SimHub Bridge is shutting down; pending mutation request was cancelled before execution.");

            lock (_serverLock)
            {
                try
                {
                    _activeServer?.Dispose();
                }
                catch
                {
                }

                _activeServer =
                    null;
            }

            if (_serverThread != null &&
                _serverThread.IsAlive)
            {
                _serverThread.Join(
                    1500);
            }

            SimHub.Logging.Current.Info(
                "[Atomic Drift Tuner Bridge] Stopped.");
        }

        private void ServerLoop()
        {
            while (!_stop)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    var security =
                        new PipeSecurity();

                    var currentUser =
                        WindowsIdentity
                            .GetCurrent()
                            .User;

                    if (currentUser == null)
                    {
                        throw new InvalidOperationException(
                            "ADT SimHub Bridge could not determine the current Windows user SID.");
                    }

                    // The bridge can cause wheelbase-setting changes, so only the
                    // Windows account running SimHub receives pipe access. An
                    // elevated/non-elevated process for the same account has the
                    // same user SID; unrelated authenticated users do not.
                    security.SetOwner(
                        currentUser);

                    security.SetAccessRuleProtection(
                        isProtected:
                            true,
                        preserveInheritance:
                            false);

                    security.AddAccessRule(
                        new PipeAccessRule(
                            currentUser,
                            PipeAccessRights.FullControl,
                            AccessControlType.Allow));

                    server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None,
                        4096,
                        4096,
                        security);
                    lock (_serverLock) _activeServer = server;
                    server.WaitForConnection();
                    if (_stop) break;
                    HandleClient(server);
                }
                catch (ObjectDisposedException) when (_stop) { break; }
                catch (Exception ex)
                {
                    if (!_stop) SimHub.Logging.Current.Error("[Atomic Drift Tuner Bridge] Pipe error: " + ex);
                    Thread.Sleep(150);
                }
                finally
                {
                    lock (_serverLock) if (ReferenceEquals(_activeServer, server)) _activeServer = null;
                    try { server?.Dispose(); } catch { }
                }
            }
        }

        private void HandleClient(Stream pipe)
        {
            var strictUtf8 =
                new UTF8Encoding(
                    false,
                    true);

            using (var reader =
                new StreamReader(
                    pipe,
                    strictUtf8,
                    false,
                    4096,
                    true))
            using (var writer =
                new StreamWriter(
                    pipe,
                    strictUtf8,
                    4096,
                    true)
                {
                    AutoFlush = true
                })
            {
                try
                {
                    var line =
                        ReadBoundedLine(
                            reader,
                            MaxRequestChars);

                    if (line == null || string.IsNullOrWhiteSpace(line))
                        return;

                    var request =
                        JObject.Parse(
                            line);

                    var commandToken =
                        request["command"];

                    if (commandToken == null ||
                        commandToken.Type != JTokenType.String)
                    {
                        WriteError(
                            writer,
                            "command is required.");

                        return;
                    }

                    var command =
                        ((string?)commandToken ??
                         "")
                            .Trim();

                    if (string.Equals(
                            command,
                            "snapshot",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteLine(
                            JsonConvert.SerializeObject(
                                new
                                {
                                    ok = true,
                                    snapshot = ReadSnapshot()
                                }));

                        return;
                    }

                    if (string.Equals(
                            command,
                            "triggerAction",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        HandleActionRequest(
                            writer,
                            request);

                        return;
                    }

                    if (string.Equals(
                            command,
                            "setSettingDirect",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        HandleDirectWriteRequest(
                            writer,
                            request);

                        return;
                    }

                    if (string.Equals(
                            command,
                            "ping",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteLine(
                            JsonConvert.SerializeObject(
                                new
                                {
                                    ok = true,
                                    bridgeVersion =
                                        BridgeVersion
                                }));

                        return;
                    }

                    WriteError(
                        writer,
                        "Unknown command.");
                }
                catch (DecoderFallbackException)
                {
                    WriteError(
                        writer,
                        "Request was not valid UTF-8.");
                }
                catch (JsonException)
                {
                    WriteError(
                        writer,
                        "Request was not valid JSON.");
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Error(
                        "[Atomic Drift Tuner Bridge] Request handling failed: " +
                        ex);

                    WriteError(
                        writer,
                        SafeError(
                            ex.GetBaseException().Message));
                }
            }
        }

        private void HandleActionRequest(
            StreamWriter writer,
            JObject request)
        {
            var token =
                request["actionName"];

            if (token == null ||
                token.Type != JTokenType.String)
            {
                WriteError(
                    writer,
                    "actionName is required.");

                return;
            }

            var actionName =
                ((string?)token ??
                 "")
                    .Trim();

            string validationError;
            if (!TryValidateActionName(
                    actionName,
                    out validationError))
            {
                WriteError(
                    writer,
                    validationError);

                return;
            }

            var action =
                new ActionRequest
                {
                    ActionName =
                        actionName,

                    DeadlineTick =
                        unchecked(
                            Environment.TickCount +
                            ActionWaitTimeoutMs)
                };

            _pendingActions.Enqueue(
                action);

            if (!action.Completed.Wait(
                    ActionWaitTimeoutMs))
            {
                if (action.TryCancelPending(
                        "ADT bridge action request expired before SimHub began execution; no action was performed."))
                {
                    WriteError(
                        writer,
                        action.Error);

                    return;
                }

                // If SimHub already claimed the request, we cannot promise that
                // it did not execute. Report an explicitly uncertain timeout
                // instead of returning a normal failure that might trigger a
                // conflicting fallback.
                if (action.IsExecuting)
                {
                    WriteError(
                        writer,
                        "Timed out waiting for SimHub action after execution began; outcome is uncertain.");

                    return;
                }

                // Completion may have raced the timeout boundary.
                action.Completed.Wait(
                    50);
            }

            if (!string.IsNullOrWhiteSpace(
                    action.Error))
            {
                WriteError(
                    writer,
                    action.Error);

                return;
            }

            if (!action.IsCompleted)
            {
                WriteError(
                    writer,
                    "Timed out waiting for SimHub action completion; outcome is uncertain.");

                return;
            }

            writer.WriteLine(
                JsonConvert.SerializeObject(
                    new
                    {
                        ok = true,
                        bridgeVersion =
                            BridgeVersion,
                        action =
                            actionName
                    }));
        }

        private void HandleDirectWriteRequest(
            StreamWriter writer,
            JObject request)
        {
            var propertyToken =
                request["propertyName"];

            if (propertyToken == null ||
                propertyToken.Type != JTokenType.String)
            {
                WriteError(
                    writer,
                    "propertyName is required.");

                return;
            }

            var propertyName =
                ((string?)propertyToken ??
                 "")
                    .Trim();

            int? targetInt =
                null;

            bool? targetBool =
                null;

            var targetIntToken =
                request["targetInt"];

            if (targetIntToken != null &&
                targetIntToken.Type != JTokenType.Null)
            {
                if (targetIntToken.Type !=
                    JTokenType.Integer)
                {
                    WriteError(
                        writer,
                        "targetInt must be an integer or null.");

                    return;
                }

                try
                {
                    targetInt =
                        (int)targetIntToken;
                }
                catch
                {
                    WriteError(
                        writer,
                        "targetInt is outside the supported 32-bit integer range.");

                    return;
                }
            }

            var targetBoolToken =
                request["targetBool"];

            if (targetBoolToken != null &&
                targetBoolToken.Type != JTokenType.Null)
            {
                if (targetBoolToken.Type !=
                    JTokenType.Boolean)
                {
                    WriteError(
                        writer,
                        "targetBool must be a boolean or null.");

                    return;
                }

                targetBool =
                    (bool)targetBoolToken;
            }

            string validationError;
            if (!TryValidateDirectTarget(
                    propertyName,
                    targetInt,
                    targetBool,
                    out validationError))
            {
                WriteError(
                    writer,
                    validationError);

                return;
            }

            var direct =
                new DirectSettingRequest
                {
                    PropertyName =
                        propertyName,

                    TargetInt =
                        targetInt,

                    TargetBool =
                        targetBool,

                    DeadlineTick =
                        unchecked(
                            Environment.TickCount +
                            DirectWriteWaitTimeoutMs)
                };

            _pendingDirectSets.Enqueue(
                direct);

            if (!direct.Completed.Wait(
                    DirectWriteWaitTimeoutMs))
            {
                if (direct.TryCancelPending(
                        "ADT bridge direct-write request expired before SimHub began execution; no setting write was performed."))
                {
                    WriteError(
                        writer,
                        direct.Error);

                    return;
                }

                if (direct.IsExecuting)
                {
                    WriteError(
                        writer,
                        "Timed out waiting for AZOM direct write after execution began; outcome is uncertain.");

                    return;
                }

                direct.Completed.Wait(
                    50);
            }

            if (!string.IsNullOrWhiteSpace(
                    direct.Error))
            {
                WriteError(
                    writer,
                    direct.Error);

                return;
            }

            if (!direct.IsCompleted)
            {
                WriteError(
                    writer,
                    "Timed out waiting for AZOM direct-write completion; outcome is uncertain.");

                return;
            }

            writer.WriteLine(
                JsonConvert.SerializeObject(
                    new
                    {
                        ok = true,
                        bridgeVersion =
                            BridgeVersion,
                        propertyName,
                        method =
                            direct.Method,
                        suppressed =
                            direct.Suppressed
                    }));
        }

        private void CancelPendingRequests(
            string reason)
        {
            while (_pendingActions.TryDequeue(
                out var action))
            {
                action.TryCancelPending(
                    reason);
            }

            while (_pendingDirectSets.TryDequeue(
                out var direct))
            {
                direct.TryCancelPending(
                    reason);
            }
        }

        private static string? ReadBoundedLine(
            StreamReader reader,
            int maxChars)
        {
            if (maxChars <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maxChars));

            var builder =
                new StringBuilder(
                    Math.Min(
                        maxChars,
                        4096));

            while (true)
            {
                int value =
                    reader.Read();

                if (value < 0)
                {
                    return builder.Length == 0
                        ? null
                        : builder.ToString();
                }

                char character =
                    (char)value;

                if (character == '\n')
                {
                    if (builder.Length > 0 &&
                        builder[builder.Length - 1] == '\r')
                    {
                        builder.Length--;
                    }

                    return builder.ToString();
                }

                if (builder.Length >=
                    maxChars)
                {
                    throw new InvalidDataException(
                        "ADT bridge request exceeded the supported " +
                        maxChars +
                        "-character limit.");
                }

                builder.Append(
                    character);
            }
        }

        private static void WriteError(
            StreamWriter writer,
            string? error)
        {
            writer.WriteLine(
                JsonConvert.SerializeObject(
                    new
                    {
                        ok = false,
                        bridgeVersion =
                            BridgeVersion,
                        error =
                            SafeError(
                                error)
                    }));
        }

        private static string SafeError(
            string? error)
        {
            if (error == null || string.IsNullOrWhiteSpace(error))
            {
                return
                    "ADT SimHub Bridge operation failed.";
            }

            var cleaned =
                error
                    .Replace('\r', ' ')
                    .Replace('\n', ' ')
                    .Replace('\0', ' ')
                    .Trim();

            if (cleaned.Length >
                MaxErrorMessageChars)
            {
                cleaned =
                    cleaned.Substring(
                        0,
                        MaxErrorMessageChars) +
                    "...";
            }

            return cleaned;
        }

        private object ReadSnapshot()
        {
            lock (_snapshotLock)
            {
                if (_cachedSnapshot != null)
                    return _cachedSnapshot;
            }

            return DiagnosticSnapshot("cache warming");
        }

        private object CaptureSnapshotOnSimHubThread()
        {
            // First try AZOM's documented SimHub properties by exact name. We do
            // not enumerate the global property registry because that behaved
            // inconsistently across SimHub builds.
            var propertySnapshot = CaptureFromKnownProperties();
            if (SnapshotIsReadable(propertySnapshot))
                return propertySnapshot;

            // Fallback for SimHub builds where sibling AttachDelegate properties
            // are not available through PluginManager.GetPropertyValue. This runs
            // only on SimHub's DataUpdate thread, never from the named-pipe thread.
            object direct;
            if (TryCaptureAzomDataFieldOnly(out direct))
                return direct;

            return propertySnapshot;
        }

        private object CaptureFromKnownProperties()
        {
            bool? baseConnected = Bool("AZOM.BaseConnected");
            int? ffb = Int("AZOM.FfbStrength");
            int? torque = Int("AZOM.Torque");
            int? rotation = Int("AZOM.Rotation");
            if (!rotation.HasValue)
                rotation = Int("AZOM.MaxAngle");

            int? wheelSpeed = Int("AZOM.WheelSpeedLimit");
            int? interpolation = Int("AZOM.Interpolation");
            int? gearshift = Int("AZOM.GearshiftVibration");
            int? damper = Int("AZOM.Damper");
            int? friction = Int("AZOM.Friction");
            int? inertia = Int("AZOM.Inertia");
            int? spring = Int("AZOM.Spring");
            int? gameDamper = Int("AZOM.GameDamper");
            int? gameFriction = Int("AZOM.GameFriction");
            int? gameInertia = Int("AZOM.GameInertia");
            int? gameSpring = Int("AZOM.GameSpring");
            int? naturalInertia = Int("AZOM.NaturalInertia");
            int? softLimit = Int("AZOM.SoftLimitStiffness");
            int? speedDamping = Int("AZOM.SpeedDamping");
            int? speedPoint = Int("AZOM.SpeedDampingPoint");
            int? road = Int("AZOM.RoadSensitivity");

            int? eq1 = Int("AZOM.Equalizer1");
            int? eq2 = Int("AZOM.Equalizer2");
            int? eq3 = Int("AZOM.Equalizer3");
            int? eq4 = Int("AZOM.Equalizer4");
            int? eq5 = Int("AZOM.Equalizer5");
            int? eq6 = Int("AZOM.Equalizer6");
            int? eq7 = Int("AZOM.Equalizer7");
            int? eq8 = Int("AZOM.Equalizer8");
            int? eq9 = Int("AZOM.Equalizer9");
            int? eq10 = Int("AZOM.Equalizer10");

            int? x1 = Int("AZOM.FfbCurveX1");
            int? x2 = Int("AZOM.FfbCurveX2");
            int? x3 = Int("AZOM.FfbCurveX3");
            int? x4 = Int("AZOM.FfbCurveX4");
            int? y1 = Int("AZOM.FfbCurveY1");
            int? y2 = Int("AZOM.FfbCurveY2");
            int? y3 = Int("AZOM.FfbCurveY3");
            int? y4 = Int("AZOM.FfbCurveY4");
            int? y5 = Int("AZOM.FfbCurveY5");

            bool? protection = Bool("AZOM.Protection");
            bool? reverse = Bool("AZOM.FfbReverse");
            bool? retain = Bool("AZOM.SoftLimitRetain");
            bool? performance = Bool("AZOM.PerformanceOutput");
            bool? led = Bool("AZOM.BaseStatusLed");
            bool? bluetooth = Bool("AZOM.Bluetooth");
            int? workMode = Int("AZOM.WorkMode");

            int count = CountPresent(
                ffb, torque, rotation, wheelSpeed, interpolation, gearshift,
                damper, friction, inertia, spring, gameDamper, gameFriction,
                gameInertia, gameSpring, naturalInertia, softLimit, speedDamping,
                speedPoint, road, eq1, eq2, eq3, eq4, eq5, eq6, eq7, eq8, eq9, eq10,
                x1, x2, x3, x4, y1, y2, y3, y4, y5, workMode);

            bool detected = baseConnected.HasValue || count > 0 ||
                            protection.HasValue || reverse.HasValue || retain.HasValue;
            bool readable = ffb.HasValue && ffb.Value >= 0 &&
                            torque.HasValue && torque.Value >= 0 &&
                            rotation.HasValue && rotation.Value >= 0;

            return new
            {
                bridgeVersion = BridgeVersion,
                capturedUtc = DateTime.UtcNow,
                azomAvailable = readable,
                pluginDetected = detected,
                settingsReadable = readable,
                propertyNamespace = "AZOM",
                readSource = "SimHub DataUpdate cache / AZOM properties",
                baseConnected = baseConnected,
                azomPropertyCount = count,
                legacyMozaPropertyCount = 0,
                settingsPropertyCount = count,
                publishedProperties = Array.Empty<string>(),

                ffbStrength = ffb,
                torque = torque,
                rotation = rotation,
                wheelSpeedLimit = wheelSpeed,
                interpolation = interpolation,
                gearshiftVibration = gearshift,
                damper = damper,
                friction = friction,
                inertia = inertia,
                spring = spring,
                gameDamper = gameDamper,
                gameFriction = gameFriction,
                gameInertia = gameInertia,
                gameSpring = gameSpring,
                naturalInertia = naturalInertia,
                softLimitStiffness = softLimit,
                speedDamping = speedDamping,
                speedDampingPoint = speedPoint,
                roadSensitivity = road,

                equalizer1 = eq1,
                equalizer2 = eq2,
                equalizer3 = eq3,
                equalizer4 = eq4,
                equalizer5 = eq5,
                equalizer6 = eq6,
                equalizer7 = eq7,
                equalizer8 = eq8,
                equalizer9 = eq9,
                equalizer10 = eq10,

                ffbCurveX1 = x1,
                ffbCurveX2 = x2,
                ffbCurveX3 = x3,
                ffbCurveX4 = x4,
                ffbCurveY1 = y1,
                ffbCurveY2 = y2,
                ffbCurveY3 = y3,
                ffbCurveY4 = y4,
                ffbCurveY5 = y5,

                protection = protection,
                ffbReverse = reverse,
                softLimitRetain = retain,
                performanceOutput = performance,
                baseStatusLed = led,
                bluetooth = bluetooth,
                workMode = workMode
            };
        }

        private static bool SnapshotIsReadable(object snapshot)
        {
            try
            {
                var prop = snapshot.GetType().GetProperty("settingsReadable");
                var value = prop == null ? null : prop.GetValue(snapshot, null);
                return value is bool && (bool)value;
            }
            catch
            {
                return false;
            }
        }

        private bool TryCaptureAzomDataFieldOnly(out object snapshot)
        {
            snapshot = null!;
            try
            {
                var azomAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "MozaPlugin", StringComparison.OrdinalIgnoreCase));
                if (azomAssembly == null)
                    return false;

                var pluginType = azomAssembly.GetType("MozaPlugin.MozaPlugin", throwOnError: false);
                if (pluginType == null)
                    return false;

                // Resolve Instance and Data only. For the Data object itself we read
                // fields only; we never invoke arbitrary property getters.
                object? instance = null;
                var instanceField = pluginType.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (instanceField != null)
                    instance = instanceField.GetValue(null);
                if (instance == null)
                {
                    var instanceProp = pluginType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (instanceProp != null && instanceProp.GetIndexParameters().Length == 0)
                        instance = instanceProp.GetValue(null, null);
                }
                if (instance == null)
                    return false;

                object? data = null;
                var dataField = pluginType.GetField("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (dataField != null)
                    data = dataField.GetValue(instance);
                if (data == null)
                {
                    var dataProp = pluginType.GetProperty("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (dataProp != null && dataProp.GetIndexParameters().Length == 0)
                        data = dataProp.GetValue(instance, null);
                }
                if (data == null)
                    return false;

                bool? baseConnected = ReadFieldBool(data, "IsBaseConnected");
                bool baseSupportsEq10 = ReadFieldBool(data, "BaseSupportsEq10") ?? false;

                int? ffb = TenthsToDisplay(ReadFieldInt(data, "FfbStrength"));
                int? torque = ReadFieldInt(data, "Torque");
                int? limit = ReadFieldInt(data, "Limit");

                int? wheelSpeed = TenthsToDisplay(ReadFieldInt(data, "Speed"));
                int? interpolation = TenthsToDisplay(ReadFieldInt(data, "Interpolation"));
                int? damper = TenthsToDisplay(ReadFieldInt(data, "Damper"));
                int? friction = TenthsToDisplay(ReadFieldInt(data, "Friction"));
                int? inertia = TenthsToDisplay(ReadFieldInt(data, "Inertia"));
                int? spring = TenthsToDisplay(ReadFieldInt(data, "Spring"));

                int? gameDamper = From255(ReadFieldInt(data, "GameDamper"));
                int? gameFriction = From255(ReadFieldInt(data, "GameFriction"));
                int? gameInertia = From255(ReadFieldInt(data, "GameInertia"));
                int? gameSpring = From255(ReadFieldInt(data, "GameSpring"));

                int? softRaw = ReadFieldInt(data, "SoftLimitStiffness");
                int? roadRaw = ReadFieldInt(data, "RoadSensitivity");

                bool readable = ffb.HasValue && ffb.Value >= 0 &&
                                torque.HasValue && torque.Value >= 0 &&
                                limit.HasValue && limit.Value > 0;

                snapshot = new
                {
                    bridgeVersion = BridgeVersion,
                    capturedUtc = DateTime.UtcNow,
                    azomAvailable = readable,
                    pluginDetected = true,
                    settingsReadable = readable,
                    propertyNamespace = "AZOM",
                    readSource = "SimHub DataUpdate cache / AZOM field fallback",
                    baseConnected = baseConnected,
                    azomPropertyCount = 0,
                    legacyMozaPropertyCount = 0,
                    settingsPropertyCount = readable ? 35 : 0,
                    publishedProperties = Array.Empty<string>(),

                    ffbStrength = ffb,
                    torque = torque,
                    rotation = limit.HasValue ? limit.Value * 2 : (int?)null,
                    wheelSpeedLimit = wheelSpeed,
                    interpolation = interpolation,
                    gearshiftVibration = ReadFieldInt(data, "GearshiftVibration"),
                    damper = damper,
                    friction = friction,
                    inertia = inertia,
                    spring = spring,
                    gameDamper = gameDamper,
                    gameFriction = gameFriction,
                    gameInertia = gameInertia,
                    gameSpring = gameSpring,
                    naturalInertia = ReadFieldInt(data, "NaturalInertia"),
                    softLimitStiffness = FromSoftLimit(softRaw),
                    speedDamping = ReadFieldInt(data, "SpeedDamping"),
                    speedDampingPoint = ReadFieldInt(data, "SpeedDampingPoint"),
                    roadSensitivity = RoadPresetFromRaw(roadRaw),

                    equalizer1 = ReadFieldInt(data, "Equalizer1"),
                    equalizer2 = ReadFieldInt(data, "Equalizer2"),
                    equalizer3 = ReadFieldInt(data, "Equalizer3"),
                    equalizer4 = ReadFieldInt(data, "Equalizer4"),
                    equalizer5 = ReadFieldInt(data, "Equalizer5"),
                    equalizer6 = ReadFieldInt(data, "Equalizer6"),
                    equalizer7 = baseSupportsEq10 ? ReadFieldInt(data, "Equalizer7") : null,
                    equalizer8 = baseSupportsEq10 ? ReadFieldInt(data, "Equalizer8") : null,
                    equalizer9 = baseSupportsEq10 ? ReadFieldInt(data, "Equalizer9") : null,
                    equalizer10 = baseSupportsEq10 ? ReadFieldInt(data, "Equalizer10") : null,

                    ffbCurveX1 = ReadFieldInt(data, "FfbCurveX1"),
                    ffbCurveX2 = ReadFieldInt(data, "FfbCurveX2"),
                    ffbCurveX3 = ReadFieldInt(data, "FfbCurveX3"),
                    ffbCurveX4 = ReadFieldInt(data, "FfbCurveX4"),
                    ffbCurveY1 = ReadFieldInt(data, "FfbCurveY1"),
                    ffbCurveY2 = ReadFieldInt(data, "FfbCurveY2"),
                    ffbCurveY3 = ReadFieldInt(data, "FfbCurveY3"),
                    ffbCurveY4 = ReadFieldInt(data, "FfbCurveY4"),
                    ffbCurveY5 = ReadFieldInt(data, "FfbCurveY5"),

                    protection = IsRawOn(ReadFieldInt(data, "Protection"), 1),
                    ffbReverse = IsRawOn(ReadFieldInt(data, "FfbReverse"), 1),
                    softLimitRetain = IsRawOn(ReadFieldInt(data, "SoftLimitRetain"), 1),
                    performanceOutput = IsRawOn(ReadFieldInt(data, "TempStrategy"), 1),
                    baseStatusLed = IsRawOn(ReadFieldInt(data, "LedStatus"), 1),
                    bluetooth = IsRawOn(ReadFieldInt(data, "BleMode"), 0),
                    workMode = ReadFieldInt(data, "WorkMode")
                };

                return true;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("[Atomic Drift Tuner Bridge] Safe AZOM field capture failed: " + ex);
                return false;
            }
        }

        private static int? ReadFieldInt(object target, string name)
        {
            try
            {
                var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) return null;
                var value = field.GetValue(target);
                return value == null ? (int?)null : Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }

        private static bool? ReadFieldBool(object target, string name)
        {
            try
            {
                var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) return null;
                var value = field.GetValue(target);
                return value == null ? (bool?)null : Convert.ToBoolean(value);
            }
            catch
            {
                return null;
            }
        }

        private static int CountPresent(params int?[] values)
        {
            int n = 0;
            foreach (var v in values)
                if (v.HasValue) n++;
            return n;
        }

        private object DiagnosticSnapshot(string source)
        {
            return new
            {
                bridgeVersion = BridgeVersion,
                capturedUtc = DateTime.UtcNow,
                azomAvailable = false,
                pluginDetected = false,
                settingsReadable = false,
                propertyNamespace = "AZOM",
                readSource = source,
                baseConnected = (bool?)null,
                azomPropertyCount = 0,
                legacyMozaPropertyCount = 0,
                settingsPropertyCount = 0,
                publishedProperties = Array.Empty<string>()
            };
        }

        private static int? TenthsToDisplay(int? raw)
            => raw.HasValue ? (int?)Math.Round(raw.Value / 10.0) : null;

        private static int? From255(int? raw)
            => raw.HasValue ? (int?)Math.Round(raw.Value / 2.55) : null;

        private static int? FromSoftLimit(int? raw)
        {
            if (!raw.HasValue) return null;
            const double step = 400.0 / 9.0;
            return (int)Math.Round((raw.Value / step) - 2.25 + 1.0);
        }

        private static int? RoadPresetFromRaw(int? raw)
        {
            if (!raw.HasValue || raw.Value < 10) return null;
            int n = (int)Math.Round((raw.Value - 10) / 4.0);
            return Math.Max(0, Math.Min(10, n));
        }

        private static bool? IsRawOn(int? raw, int onValue)
            => raw.HasValue ? (bool?)(raw.Value == onValue) : null;

        private static bool IsWheelbaseSettingProperty(string suffix)
        {
            switch (suffix)
            {
                case "FfbStrength":
                case "Torque":
                case "Rotation":
                case "WheelSpeedLimit":
                case "Interpolation":
                case "GearshiftVibration":
                case "Damper":
                case "Friction":
                case "Inertia":
                case "Spring":
                case "GameDamper":
                case "GameFriction":
                case "GameInertia":
                case "GameSpring":
                case "NaturalInertia":
                case "SoftLimitStiffness":
                case "SpeedDamping":
                case "SpeedDampingPoint":
                case "RoadSensitivity":
                case "Protection":
                case "FfbReverse":
                case "SoftLimitRetain":
                case "PerformanceOutput":
                case "BaseStatusLed":
                case "Bluetooth":
                case "WorkMode":
                    return true;
                default:
                    return suffix.StartsWith("Equalizer", StringComparison.OrdinalIgnoreCase) ||
                           suffix.StartsWith("FfbCurve", StringComparison.OrdinalIgnoreCase);
            }
        }

        private List<string> GetAllPropertyNames()
        {
            try
            {
                var mi = PluginManager.GetType().GetMethod(
                    "GetAllPropertiesNames",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var values = mi?.Invoke(PluginManager, null) as IEnumerable;
                if (values == null) return new List<string>();

                var list = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in values)
                {
                    var s = item as string;
                    if (s != null && !string.IsNullOrWhiteSpace(s) && seen.Add(s))
                        list.Add(s);
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);
                return list;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("[Atomic Drift Tuner Bridge] Property enumeration failed: " + ex);
                return new List<string>();
            }
        }

        private int? IntAny(params string[] names)
        {
            foreach (var name in names)
            {
                var value = Int(name);
                if (value.HasValue) return value;
            }
            return null;
        }

        private bool? BoolAny(params string[] names)
        {
            foreach (var name in names)
            {
                var value = Bool(name);
                if (value.HasValue) return value;
            }
            return null;
        }

        private int? Int(string name)
        {
            try
            {
                var value = PluginManager.GetPropertyValue(name);
                if (value == null) return null;
                return Convert.ToInt32(value);
            }
            catch { return null; }
        }

        private bool? Bool(string name)
        {
            try
            {
                var value = PluginManager.GetPropertyValue(name);
                if (value == null) return null;
                return Convert.ToBoolean(value);
            }
            catch { return null; }
        }
    }
}
