namespace AtomicDriftTuner.Data;

public readonly record struct AzomRange(int Min, int Max, string Unit = "")
{
    public int Clamp(int value) => Math.Clamp(value, Min, Max);
    public string Display => $"{Min}..{Max}{Unit}";
}

// Ranges captured from the AZOM Base-page min/max screenshots supplied for v0.4.1.
public static class AzomSettingCatalog
{
    public static readonly AzomRange WheelRotationAngle = new(60, 2700, "°");
    public static readonly AzomRange GameFfbStrength = new(0, 100, "%");
    public static readonly AzomRange BaseTorqueOutput = new(50, 100, "%");
    public static readonly AzomRange MaximumWheelSpeed = new(0, 200, "%");
    public static readonly AzomRange Interpolation = new(0, 10);

    public static readonly AzomRange ShiftIntensity = new(0, 5);
    public static readonly AzomRange ShiftDebounce = new(0, 1000, " ms");

    public static readonly AzomRange WheelDamper = new(0, 100, "%");
    public static readonly AzomRange WheelFriction = new(0, 100, "%");
    public static readonly AzomRange NaturalInertia = new(100, 500);
    public static readonly AzomRange WheelSpring = new(0, 100, "%");

    public static readonly AzomRange GameDamper = new(0, 100, "%");
    public static readonly AzomRange GameFriction = new(0, 100, "%");
    public static readonly AzomRange GameInertia = new(0, 100, "%");
    public static readonly AzomRange GameSpring = new(0, 100, "%");

    public static readonly AzomRange SteeringWheelInertia = new(100, 4000);
    public static readonly AzomRange SoftLimitStiffness = new(1, 10);

    // The supplied AZOM equalizer graph visibly spans 0..400 and labels 100% neutral / 400% max boost.
    public static readonly AzomRange EqualizerBand = new(0, 400, "%");
    public static readonly AzomRange EqualizerSensitivity = new(0, 10);
    public static readonly AzomRange CurveNode = new(0, 100);

    public static readonly AzomRange HighSpeedDampingLevel = new(0, 100, "%");
    public static readonly AzomRange HighSpeedTriggerSpeed = new(0, 400, " kph");
}
