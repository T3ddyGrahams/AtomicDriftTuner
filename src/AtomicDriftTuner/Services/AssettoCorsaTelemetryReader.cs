using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AssettoCorsaTelemetryReader : IDisposable
{
    private MemoryMappedFile? _physicsMap;
    private double? _lastTime;
    private double? _lastSteer;

    public bool IsConnected => _physicsMap is not null;

    public bool TryConnect()
    {
        if (_physicsMap is not null) return true;
        try
        {
            _physicsMap = MemoryMappedFile.OpenExisting("Local\\acpmf_physics", MemoryMappedFileRights.Read);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public TelemetrySample Read(double elapsedSeconds)
    {
        if (_physicsMap is null && !TryConnect())
            throw new InvalidOperationException("Assetto Corsa shared memory was not found. Start a driving session in Assetto Corsa first.");

        var physics = ReadStruct<AcPhysics>(_physicsMap!);
        double steeringDeg = physics.SteerAngle * 180.0 / Math.PI;
        double steerRate = 0;
        if (_lastTime is double lt && _lastSteer is double ls)
        {
            double dt = elapsedSeconds - lt;
            if (dt > 0.001 && dt < 0.5)
                steerRate = (steeringDeg - ls) / dt;
        }
        _lastTime = elapsedSeconds;
        _lastSteer = steeringDeg;

        double lateral = physics.LocalVelocity?.Length > 0 ? physics.LocalVelocity[0] : 0;
        double longitudinal = physics.LocalVelocity?.Length > 2 ? physics.LocalVelocity[2] : 0;
        double slipAngle = Math.Atan2(lateral, Math.Max(0.01, Math.Abs(longitudinal))) * 180.0 / Math.PI;
        double yaw = physics.LocalAngularVelocity?.Length > 1 ? physics.LocalAngularVelocity[1] * 180.0 / Math.PI : 0;

        return new TelemetrySample
        {
            TimeSeconds = elapsedSeconds,
            PacketId = physics.PacketId,
            SpeedKmh = physics.SpeedKmh,
            Throttle = physics.Gas,
            Brake = physics.Brake,
            Clutch = physics.Clutch,
            Gear = physics.Gear,
            Rpm = physics.Rpms,
            SteeringAngleDeg = steeringDeg,
            SteeringRateDegPerSec = steerRate,
            SlipAngleDeg = slipAngle,
            YawRateDegPerSec = yaw,
            LateralG = physics.AccG?.Length > 0 ? physics.AccG[0] : 0,
            LongitudinalG = physics.AccG?.Length > 2 ? physics.AccG[2] : 0,
            FrontWheelSlipAvg = AvgPair(physics.WheelSlip, 0, 1),
            RearWheelSlipAvg = AvgPair(physics.WheelSlip, 2, 3),
            FinalFfb = physics.FinalFF,
            FrontTyrePressureAvg = AvgPair(physics.WheelsPressure, 0, 1),
            RearTyrePressureAvg = AvgPair(physics.WheelsPressure, 2, 3)
        };
    }

    public void ResetDerivativeState()
    {
        _lastTime = null;
        _lastSteer = null;
    }

    private static double AvgPair(float[]? values, int a, int b) =>
        values is not null && values.Length > b ? (values[a] + values[b]) / 2.0 : 0;

    private static T ReadStruct<T>(MemoryMappedFile map) where T : struct
    {
        using var stream = map.CreateViewStream(0, Marshal.SizeOf<T>(), MemoryMappedFileAccess.Read);
        using var reader = new BinaryReader(stream);
        byte[] bytes = reader.ReadBytes(Marshal.SizeOf<T>());
        if (bytes.Length < Marshal.SizeOf<T>())
            throw new EndOfStreamException("Assetto Corsa shared memory returned an incomplete physics frame.");
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
    }

    public void Dispose()
    {
        _physicsMap?.Dispose();
        _physicsMap = null;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Vec3 { public float X; public float Y; public float Z; }

    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
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
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public float[] Velocity;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public float[] AccG;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] WheelSlip;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] WheelLoad;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] WheelsPressure;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] WheelAngularSpeed;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreWear;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreDirtyLevel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreCoreTemperature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] CamberRad;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] SuspensionTravel;
        public float Drs;
        public float TC;
        public float Heading;
        public float Pitch;
        public float Roll;
        public float CgHeight;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public float[] CarDamage;
        public int NumberOfTyresOut;
        public int PitLimiterOn;
        public float Abs;
        public float KersCharge;
        public float KersInput;
        public int AutoShifterOn;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public float[] RideHeight;
        public float TurboBoost;
        public float Ballast;
        public float AirDensity;
        public float AirTemp;
        public float RoadTemp;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public float[] LocalAngularVelocity;
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
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] BrakeTemp;
        public float Clutch;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreTempI;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreTempM;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreTempO;
        public int IsAIControlled;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public Vec3[] TyreContactPoint;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public Vec3[] TyreContactNormal;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public Vec3[] TyreContactHeading;
        public float BrakeBias;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public float[] LocalVelocity;
    }
}
