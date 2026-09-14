using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner;

public partial class CarSetupWindow
{
    private void ReadAngleGoal(CarBehaviorTarget target)
    {
        target.SustainedAngle = (SustainedAnglePreference)Math.Clamp(AngleGoalBox.SelectedIndex, 0, 2);
        target.CustomAngleMinDeg = CustomAngleRangeCheck.IsChecked == true ? Math.Round(AngleMinSlider.Value) : null;
        target.CustomAngleMaxDeg = CustomAngleRangeCheck.IsChecked == true ? Math.Round(AngleMaxSlider.Value) : null;
    }
    private void ApplyAngleGoal(CarBehaviorTarget target)
    {
        bool wasLoading = _loadingBehavior;
        _loadingBehavior = true;
        try
        {
            AngleGoalBox.SelectedIndex = (int)target.SustainedAngle;
            CustomAngleRangeCheck.IsChecked = target.CustomAngleMinDeg is not null;
            AngleMinSlider.Value = target.AngleMinDeg;
            AngleMaxSlider.Value = target.AngleMaxDeg;
            UpdateAngleGoalLabels();
        }
        finally { _loadingBehavior = wasLoading; }
    }
    private void UpdateAngleGoalLabels()
    {
        var target = new CarBehaviorTarget(); ReadAngleGoal(target);
        AngleRangeExpander.IsEnabled = target.HasAngleGoal;
        CustomAngleRangePanel.Visibility = CustomAngleRangeCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AngleGoalHelpText.Text = target.HasAngleGoal ?
            target.AngleGoalLabel + ". ADT checks how long you hold it, speed retained and recovery. Save this goal before recording a new baseline." :
            "Keep the usual angle assessment. You can still adjust stability and handling below.";
        AngleRangeText.Text = $"Starting target: {target.AngleMinDeg:0}–{target.AngleMaxDeg:0}° of body angle relative to travel, not steering-wheel rotation. " +
            "Choose a range suited to this car. A goal is not a guarantee of achievable angle; steering geometry, grip and power still matter.";
    }
    private void AngleGoal_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _loadingBehavior) return;
        if (CustomAngleRangeCheck.IsChecked != true)
        {
            _loadingBehavior = true;
            try
            {
                var target = new CarBehaviorTarget { SustainedAngle = (SustainedAnglePreference)Math.Clamp(AngleGoalBox.SelectedIndex, 0, 2) };
                AngleMinSlider.Value = target.AngleMinDeg; AngleMaxSlider.Value = target.AngleMaxDeg;
            }
            finally { _loadingBehavior = false; }
        }
        AngleRange_Changed(sender, e);
    }
    private void AngleRange_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _loadingBehavior) return;
        UpdateAngleGoalLabels();
        BehaviorSlider_ValueChanged(sender, new RoutedPropertyChangedEventArgs<double>(0, 0));
    }
    private void AngleRangeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _loadingBehavior) return;
        _loadingBehavior = true;
        try
        {
            if (AngleMaxSlider.Value - AngleMinSlider.Value < 5)
            {
                if (ReferenceEquals(sender, AngleMinSlider)) AngleMaxSlider.Value = AngleMinSlider.Value + 5;
                else AngleMinSlider.Value = AngleMaxSlider.Value - 5;
            }
        }
        finally { _loadingBehavior = false; }
        AngleRange_Changed(sender, e);
    }
}
