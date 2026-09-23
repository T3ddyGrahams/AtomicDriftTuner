using System.IO;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal sealed class CarTestFixture
{
    public string DirectoryPath { get; }
    public string Baseline { get; }
    public string Definitions { get; }
    public TuneInput Input { get; }
    public SavedTelemetrySession Run { get; }
    public TuningAssistantReport Report { get; }
    public AssistantCarTestService Service { get; } = new();
    public IReadOnlyList<AssistantCarTestService.Choice> Build() => Service.Build(Input, Run, Report, Baseline);
    public CarTestFixture(string root, string mode = "0", double pressure = 28, bool includePair = true)
    {
        DirectoryPath = Path.Combine(root, "car-test-" + Guid.NewGuid().ToString("N"));
        var carPath = Path.Combine(DirectoryPath, "isolated_car_test"); var data = Path.Combine(carPath, "data");
        Directory.CreateDirectory(data);
        Definitions = Path.Combine(data, "setup.ini");
        var sections = new[] { "PRESSURE_LF", "PRESSURE_RF", "PRESSURE_LR", "PRESSURE_RR" };
        File.WriteAllText(Definitions, string.Join("\n", sections.Select(s => $"[{s}]\nMIN=10\nMAX=45\nSTEP=1\nSHOW_CLICKS={mode}\nUNITS=psi\n")));
        File.WriteAllText(Path.Combine(data, "car.ini"), "[BASIC]\nTOTALMASS=1200\n");
        Baseline = Path.Combine(DirectoryPath, "Baseline.ini");
        File.WriteAllText(Baseline, "[CAR]\nMODEL=isolated_car_test\n; preserve driver metadata\n" +
            string.Join("\n", sections.Where(s => includePair || s != "PRESSURE_RR").Select(s => $"[{s}]\nVALUE={pressure}\n")) + "[FUEL]\nVALUE=30\n[ECU_MAP]\nVALUE=2\n");
        Input = new() { Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0],
            Car = BuiltInProfiles.Cars()[0], Intent = BuiltInProfiles.Intents()[1] };
        Input.Intent.Kind = DriftStyleKind.Competition;
        Input.Car.Id = "isolated_car_test"; Input.Car.SourceFolderName = "isolated_car_test"; Input.Car.SourceFolderPath = carPath; Input.Car.IsInstalled = true;
        var history = new RunHistoryStore(Path.Combine(DirectoryPath, "history")); var driver = history.GetOrCreateDriver("Synthetic test driver");
        var goal = new CarBehaviorTarget { RearGrip = 2 };
        var tune = history.CaptureTune(Input, driver, "Baseline", goal, null, Baseline, focus: TuningFocus.CarSetupOnly,
            gearingTargets: new GearingTargetStore(Path.Combine(DirectoryPath, "gearing")));
        var session = new TelemetrySession { CarFolder = Input.Car.SourceFolderName, CarName = "Synthetic setup review", Context = new() {
            DriverId = driver.Id, DriverName = driver.Name, Focus = TuningFocus.CarSetupOnly, CarIdentityVerified = true, TuneConfirmedInUse = true, Tune = tune, TrackId = "fixture", Conditions = "dry solo" } };
        for (int i = 0; i < 4000; i++)
        {
            double t = i / 50.0, p = t % 20;
            double angle = p < 3 ? 0 : p < 4 ? (p - 3) * 30 : p < 10 ? 30 : p < 11 ? 30 - (p - 10) * 60 : p < 18 ? -30 : 0;
            session.Samples.Add(new TelemetrySample { TimeSeconds = t, PacketId = i + 1, SpeedKmh = 60, SlipAngleDeg = angle,
                SteeringAngleDeg = -angle * 2, YawRateDegPerSec = angle * .7, Throttle = .75, Clutch = 1, Gear = 2,
                FrontWheelSlipAvg = 1, RearWheelSlipAvg = 3, FinalFfb = .4, HasExtendedSignals = true });
        }
        Run = new() { Session = session, Analysis = new TelemetryAnalyzer().Analyze(session) };
        // Isolate a rear-grip finding; the other diagnosis/quality evidence stays real.
        Run.Analysis.Diagnosis.Metrics.RemoveAll(m => m.Key != "rear-slip-share");
        Report = new DriftAssistantReportBuilder().Build(Input, goal, Run, null);
    }
}
