using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

internal static partial class Program
{
    private static TelemetryAnalysis PedalFixture()
    {
        var session = new TelemetrySession();
        for (int i = 0; i < 2000; i++)
        {
            double t = i / 50.0, phase = t % 10;
            session.Samples.Add(new TelemetrySample { TimeSeconds = t, PacketId = i + 1, SpeedKmh = 60,
                Throttle = phase is >= 2 and < 6 ? .85 : .15, Brake = phase is >= 4 and < 5 ? .3 : 0,
                Clutch = phase is >= 2 and < 2.3 ? 0 : 1, Gear = 3, Rpm = phase is >= 2 and < 2.5 ? 6300 : 5000,
                SlipAngleDeg = phase is >= 2.2 and < 6 ? 40 : 30, YawRateDegPerSec = 25, SteeringAngleDeg = -60,
                FrontWheelSlipAvg = 1, RearWheelSlipAvg = 3, HasExtendedSignals = true });
        }
        return new TelemetryAnalyzer().Analyze(session);
    }
    private static void SeedPedalEvidence(FrameworkElement root)
    {
        if (root.FindName("PedalGrid") is not DataGrid grid) return;
        var pedals = PedalFixture().Diagnosis.Pedals;
        grid.ItemsSource = pedals.Events;
        grid.SelectedIndex = 0;
        ((DataGrid)root.FindName("PedalContextGrid")).ItemsSource = pedals.ContextMetrics;
        ((TextBlock)root.FindName("PedalSummaryText")).Text = "Synthetic preview — " + pedals.Summary;
        ((TextBlock)root.FindName("PedalLimitationsText")).Text = pedals.Limitations;
    }
}
