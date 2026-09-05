using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AssettoCorsaTelemetryReader : IDisposable
{
    private const string PhysicsMapName =
        @"Local\acpmf_physics";

    private const double MinimumDerivativeDeltaSeconds =
        0.001;

    private const double MaximumDerivativeDeltaSeconds =
        0.5;

    private const double MinimumSlipLongitudinalSpeed =
        0.01;

    private MemoryMappedFile? _physicsMap;

    private double? _lastTime;
    private double? _lastSteer;
    private int? _lastPacketId;

    private bool _disposed;

    public bool IsConnected =>
        !_disposed &&
        _physicsMap is not null;

    public bool TryConnect()
    {
        ThrowIfDisposed();

        if (_physicsMap is not null)
        {
            return true;
        }

        try
        {
            _physicsMap =
                MemoryMappedFile.OpenExisting(
                    PhysicsMapName,
                    MemoryMappedFileRights.Read);

            ResetDerivativeState();

            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public TelemetrySample Read(
        double elapsedSeconds)
    {
        ThrowIfDisposed();

        if (!double.IsFinite(elapsedSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedSeconds),
                "ADT telemetry time must be a finite number.");
        }

        if (_physicsMap is null &&
            !TryConnect())
        {
            throw new InvalidOperationException(
                "Assetto Corsa shared memory was not found. Start Assetto Corsa and enter an on-track session.");
        }

        var physics =
            ReadStruct<AcPhysics>(
                _physicsMap!);

        if (PacketContinuityWasBroken(
                physics.PacketId,
                elapsedSeconds))
        {
            ResetDerivativeState();
        }

        var steeringDeg =
            ToFinite(
                physics.SteerAngle * 180.0 / Math.PI);

        var steerRate =
            CalculateSteeringRate(
                elapsedSeconds,
                steeringDeg);

        _lastTime =
            elapsedSeconds;

        _lastSteer =
            steeringDeg;

        _lastPacketId =
            physics.PacketId;

        var lateralVelocity =
            GetArrayValue(
                physics.LocalVelocity,
                0);

        var longitudinalVelocity =
            GetArrayValue(
                physics.LocalVelocity,
                2);

        var slipAngle =
            CalculateSlipAngle(
                lateralVelocity,
                longitudinalVelocity);

        var yawRate =
            ToFinite(
                GetArrayValue(
                    physics.LocalAngularVelocity,
                    1) *
                180.0 /
                Math.PI);

        return new TelemetrySample
        {
            TimeSeconds =
                elapsedSeconds,

            PacketId =
                physics.PacketId,

            SpeedKmh =
                ToFinite(
                    physics.SpeedKmh),

            Throttle =
                ToFinite(
                    physics.Gas),

            Brake =
                ToFinite(
                    physics.Brake),

            Clutch =
                ToFinite(
                    physics.Clutch),

            Gear =
                physics.Gear,

            Rpm =
                physics.Rpms,

            SteeringAngleDeg =
                steeringDeg,

            SteeringRateDegPerSec =
                steerRate,

            SlipAngleDeg =
                slipAngle,

            YawRateDegPerSec =
                yawRate,

            LateralG =
                GetArrayValue(
                    physics.AccG,
                    0),

            LongitudinalG =
                GetArrayValue(
                    physics.AccG,
                    2),

            FrontWheelSlipAvg =
                AveragePair(
                    physics.WheelSlip,
                    0,
                    1),

            RearWheelSlipAvg =
                AveragePair(
                    physics.WheelSlip,
                    2,
                    3),

            FinalFfb =
                ToFinite(
                    physics.FinalFF),

            FrontTyrePressureAvg =
                AveragePair(
                    physics.WheelsPressure,
                    0,
                    1),

            RearTyrePressureAvg =
                AveragePair(
                    physics.WheelsPressure,
                    2,
                    3)
        };
    }

    public void ResetDerivativeState()
    {
        _lastTime =
            null;

        _lastSteer =
            null;

        _lastPacketId =
            null;
    }

    private bool PacketContinuityWasBroken(
        int packetId,
        double elapsedSeconds)
    {
        if (_lastPacketId is not int previousPacket)
        {
            return false;
        }

        if (_lastTime is double previousTime &&
            elapsedSeconds <= previousTime)
        {
            return true;
        }

        // AC normally increments packetId while physics is updating.
        //
        // A lower packet ID indicates that the shared-memory producer likely
        // restarted or a new session established a fresh packet sequence.
        if (packetId < previousPacket)
        {
            return true;
        }

        return false;
    }

    private double CalculateSteeringRate(
        double elapsedSeconds,
        double steeringDeg)
    {
        if (
            _lastTime is not double previousTime ||
            _lastSteer is not double previousSteer)
        {
            return 0;
        }

        var deltaSeconds =
            elapsedSeconds -
            previousTime;

        if (
            deltaSeconds <= MinimumDerivativeDeltaSeconds ||
            deltaSeconds >= MaximumDerivativeDeltaSeconds)
        {
            return 0;
        }

        return ToFinite(
            (steeringDeg - previousSteer) /
            deltaSeconds);
    }

    private static double CalculateSlipAngle(
        double lateralVelocity,
        double longitudinalVelocity)
    {
        if (
            !double.IsFinite(lateralVelocity) ||
            !double.IsFinite(longitudinalVelocity))
        {
            return 0;
        }

        // ADT derives a drift-oriented body slip angle from local velocity:
        // X = lateral velocity
        // Z = longitudinal velocity.
        //
        // Do not feed signed reverse/rollback longitudinal velocity directly
        // into Atan2. A negative Z value can otherwise wrap an ordinary
        // reverse movement toward +/-180 degrees and be mistaken for an
        // extreme drift/spin event.
        //
        // Folding the longitudinal direction keeps the derived ADT slip
        // metric in the useful -90..+90 degree range while preserving the
        // lateral sign used to distinguish left/right slip.
        var denominator =
            Math.Max(
                Math.Abs(longitudinalVelocity),
                MinimumSlipLongitudinalSpeed);

        return ToFinite(
            Math.Atan2(
                lateralVelocity,
                denominator) *
            180.0 /
            Math.PI);
    }

    private static double AveragePair(
        float[]? values,
        int first,
        int second)
    {
        if (
            values is null ||
            first < 0 ||
            second < 0 ||
            first >= values.Length ||
            second >= values.Length)
        {
            return 0;
        }

        var a =
            ToFinite(
                values[first]);

        var b =
            ToFinite(
                values[second]);

        return ToFinite(
            (a + b) /
            2.0);
    }

    private static double GetArrayValue(
        float[]? values,
        int index)
    {
        if (
            values is null ||
            index < 0 ||
            index >= values.Length)
        {
            return 0;
        }

        return ToFinite(
            values[index]);
    }

    private static double ToFinite(
        double value)
    {
        return double.IsFinite(value)
            ? value
            : 0;
    }

    private static T ReadStruct<T>(
        MemoryMappedFile map)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(
            map);

        var size =
            StructSize<T>.Value;

        using var stream =
            map.CreateViewStream(
                0,
                size,
                MemoryMappedFileAccess.Read);

        var bytes =
            new byte[size];

        var offset =
            0;

        while (offset < bytes.Length)
        {
            var read =
                stream.Read(
                    bytes,
                    offset,
                    bytes.Length - offset);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Assetto Corsa shared memory returned an incomplete physics frame.");
            }

            offset +=
                read;
        }

        var handle =
            GCHandle.Alloc(
                bytes,
                GCHandleType.Pinned);

        try
        {
            return Marshal.PtrToStructure<T>(
                handle.AddrOfPinnedObject());
        }
        catch (Exception ex)
            when (
                ex is ArgumentException ||
                ex is MarshalDirectiveException)
        {
            throw new InvalidDataException(
                "ADT could not decode the Assetto Corsa physics shared-memory frame.",
                ex);
        }
        finally
        {
            handle.Free();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(AssettoCorsaTelemetryReader));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed =
            true;

        ResetDerivativeState();

        try
        {
            _physicsMap?.Dispose();
        }
        finally
        {
            _physicsMap =
                null;
        }
    }

    private static class StructSize<T>
        where T : struct
    {
        public static readonly int Value =
            Marshal.SizeOf<T>();
    }

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 4)]
    private struct Vec3
    {
        public float X;
        public float Y;
        public float Z;
    }

    [StructLayout(
        LayoutKind.Sequential,
        Pack = 4)]
    private struct AcPhysics
    {
        public int PacketId;
        public float Gas;
        public float Brake;
        public float Fuel;
        public int Gear;
        public int Rpms;
        public float SteerAngle;
        public float SpeedKmh;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 3)]
        public float[] Velocity;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 3)]
        public float[] AccG;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] WheelSlip;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] WheelLoad;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] WheelsPressure;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] WheelAngularSpeed;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] TyreWear;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] TyreDirtyLevel;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] TyreCoreTemperature;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] CamberRad;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] SuspensionTravel;

        public float Drs;
        public float TC;
        public float Heading;
        public float Pitch;
        public float Roll;
        public float CgHeight;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 5)]
        public float[] CarDamage;

        public int NumberOfTyresOut;
        public int PitLimiterOn;
        public float Abs;
        public float KersCharge;
        public float KersInput;
        public int AutoShifterOn;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 2)]
        public float[] RideHeight;

        public float TurboBoost;
        public float Ballast;
        public float AirDensity;
        public float AirTemp;
        public float RoadTemp;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 3)]
        public float[] LocalAngularVelocity;

        public float FinalFF;
        public float PerformanceMeter;
        public int EngineBrake;
        public int ErsRecoveryLevel;
        public int ErsPowerLevel;
        public int ErsHeatCharging;
        public int ErsIsCharging;
        public float KersCurrentKJ;
        public int DrsAvailable;
        public int DrsEnabled;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] BrakeTemp;

        public float Clutch;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] TyreTempI;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] TyreTempM;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public float[] TyreTempO;

        public int IsAIControlled;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public Vec3[] TyreContactPoint;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public Vec3[] TyreContactNormal;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 4)]
        public Vec3[] TyreContactHeading;

        public float BrakeBias;

        [MarshalAs(
            UnmanagedType.ByValArray,
            SizeConst = 3)]
        public float[] LocalVelocity;
    }
}
