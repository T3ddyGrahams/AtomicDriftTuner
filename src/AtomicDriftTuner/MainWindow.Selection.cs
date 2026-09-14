using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner;

public partial class MainWindow
{
    private bool _selectionReady;
    private bool _restoringSelection;
    private bool _selectionSavePending;
    private SessionSelection _rememberedSelection = new();
    private SessionSelection? _pendingCarSelection;
    private string _lastSelectionJson = "";

    private bool HasCompleteSessionSelection => HardwareBox.SelectedItem is HardwareProfile && WheelBox.SelectedItem is SteeringWheelProfile &&
        PackBox.SelectedItem is DriftPackProfile && CarBox.SelectedItem is CarProfile && IntentBox.SelectedItem is DriftIntent;

    private void RestoreSessionSelection(SessionSelection? saved)
    {
        _restoringSelection = true;
        try
        {
            _rememberedSelection = saved?.Clean() ?? new();
            _lastSelectionJson = JsonSerializer.Serialize(_rememberedSelection);
            var s = _rememberedSelection;
            HardwareBox.SelectedItem = _hardware.FirstOrDefault(x => x.Id == s.HardwareId);
            WheelBox.SelectedItem = _wheels.FirstOrDefault(x => x.Id == s.WheelId);
            IntentBox.SelectedItem = _intents.FirstOrDefault(x => x.Kind == s.Intent);
            PackBox.SelectedItem = _packs.FirstOrDefault(x => x.Id == s.PackId);
            PopulateHardware(); PopulateWheel();
            RefreshCarsForPack(s.CarIsInstalled ? null : s.CarId);
            if (HardwareBox.SelectedItem is not null) RestoreNumbers(s, SessionSelection.HardwareFields);
            if (WheelBox.SelectedItem is not null) RestoreNumbers(s, SessionSelection.WheelFields);
            if (CarBox.SelectedItem is not null) RestoreCarNumbers(s);
            _pendingCarSelection = s.CarIsInstalled && s.CarId is not null ? s : null;
        }
        finally { _restoringSelection = false; }
        UpdateSelectionReadiness();
    }

    private void RestoreNumbers(SessionSelection saved, IEnumerable<string> fields)
    {
        foreach (var field in fields)
            if (saved.Numbers.TryGetValue(field, out var value) && FindName(field) is TextBox box)
                box.Text = value.ToString("0.########", CultureInfo.InvariantCulture);
    }
    private void RestoreCarNumbers(SessionSelection saved)
    {
        RestoreNumbers(saved, SessionSelection.CarFields);
        if (saved.Grip is { } grip) GripBox.SelectedItem = grip;
    }

    private void RestorePendingCarSelection()
    {
        if (_pendingCarSelection is not { } saved) return;
        var car = _installedCars.FirstOrDefault(x => x.Id == saved.CarId &&
            (saved.CarFolder is null || string.Equals(x.SourceFolderName, saved.CarFolder, StringComparison.OrdinalIgnoreCase)));
        if (car is null) return; // Missing cars stay unselected. Never substitute the first car in a pack.
        var pack = _packs.FirstOrDefault(x => x.Id == car.PackId);
        if (pack is null) return;
        _restoringSelection = true;
        try
        {
            PackBox.SelectedItem = pack;
            RefreshCarsForPack(car.Id);
            RestoreCarNumbers(saved);
            _pendingCarSelection = null;
        }
        finally { _restoringSelection = false; }
    }

    private void SessionSelectionChanged(bool carChoice = false)
    {
        if (_selectionReady && !_restoringSelection && !_scanInProgress)
        {
            if (carChoice) _pendingCarSelection = null;
            ClearGeneratedSelection();
            QueueRememberSessionSelection();
        }
        UpdateSelectionReadiness();
        UpdateRemoteContextSafely();
    }

    private void ClearGeneratedSelection()
    {
        _lastResult = null;
        GuidedReadyCheck.IsChecked = false;
        SummaryText.Text = "Generate a tune for the selected car and hardware.";
        AzomText.Text = AcText.Text = BehaviorText.Text = "Generate a tune to see recommendations for these selections.";
        NotesText.Text = "";
    }

    private void UpdateSelectionReadiness()
    {
        if (SessionSelectionStatusText is null) return;
        DashboardGenerateButton.IsEnabled = HasCompleteSessionSelection;
        CarBox.IsEnabled = PackBox.SelectedItem is not null;
        SessionSelectionStatusText.Text = HasCompleteSessionSelection ? "Your selections are remembered on this Windows account. Generating a tune does not apply settings." :
            _pendingCarSelection is not null ? "Your saved car is waiting for an AC scan. Scan your installation, or choose another pack and car." :
            "Choose your wheelbase, rim, drift pack, car and drift target. ADT will remember your choices. No hardware or car is chosen for you.";
    }

    private void SessionValue_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_selectionReady || _restoringSelection || _scanInProgress) return;
        if (JsonSerializer.Serialize(CaptureSessionSelection()) != _lastSelectionJson) ClearGeneratedSelection();
        QueueRememberSessionSelection();
        UpdateRemoteContextSafely();
    }
    private void SessionGrip_Changed(object sender, SelectionChangedEventArgs e)
    {
        SessionValue_LostFocus(sender, e);
    }
    private void QueueRememberSessionSelection()
    {
        if (!_selectionReady || _restoringSelection || _scanInProgress || _selectionSavePending) return;
        _selectionSavePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _selectionSavePending = false;
            if (_selectionReady) RememberSessionSelection();
        }));
    }
    private void RememberSessionSelection()
    {
        if (!_selectionReady || _restoringSelection || _scanInProgress) return;
        var saved = CaptureSessionSelection();
        var json = JsonSerializer.Serialize(saved);
        if (json == _lastSelectionJson) return;
        try
        {
            var settings = _appSettingsStore.Load();
            settings.LastSessionSelection = saved;
            _appSettingsStore.Save(settings);
            _rememberedSelection = saved;
            _lastSelectionJson = json;
            UpdateSelectionReadiness();
        }
        catch (Exception ex) { SessionSelectionStatusText.Text = "Your selections work for this session, but ADT could not remember them: " + ex.Message; }
    }
    private SessionSelection CaptureSessionSelection()
    {
        var previous = _rememberedSelection;
        var car = CarBox.SelectedItem as CarProfile;
        var saved = new SessionSelection
        {
            HardwareId = (HardwareBox.SelectedItem as HardwareProfile)?.Id, WheelId = (WheelBox.SelectedItem as SteeringWheelProfile)?.Id,
            PackId = (PackBox.SelectedItem as DriftPackProfile)?.Id, CarId = car?.Id, CarFolder = car?.SourceFolderName,
            CarIsInstalled = car?.IsInstalled == true, Intent = (IntentBox.SelectedItem as DriftIntent)?.Kind,
            Grip = GripBox.SelectedItem as GripLevel?
        };
        void Capture(IEnumerable<string> fields, bool selected, bool same)
        {
            if (!selected) return;
            foreach (var field in fields)
            {
                var text = ((TextBox)FindName(field)).Text;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && Math.Abs(number) <= 100_000)
                    saved.Numbers[field] = number;
                else if (same && previous.Numbers.TryGetValue(field, out var old)) saved.Numbers[field] = old;
            }
        }
        Capture(SessionSelection.HardwareFields, saved.HardwareId is not null, saved.HardwareId == previous.HardwareId);
        Capture(SessionSelection.WheelFields, saved.WheelId is not null, saved.WheelId == previous.WheelId);
        Capture(SessionSelection.CarFields, car is not null, saved.CarId == previous.CarId);
        if (_pendingCarSelection is { } pending)
        {
            saved.PackId = pending.PackId; saved.CarId = pending.CarId; saved.CarFolder = pending.CarFolder; saved.CarIsInstalled = true; saved.Grip = pending.Grip;
            foreach (var field in SessionSelection.CarFields)
                if (pending.Numbers.TryGetValue(field, out var value)) saved.Numbers[field] = value;
        }
        return saved;
    }
    private void RestoreSameCarNumbers(SessionSelection saved)
    {
        if (CarBox.SelectedItem is CarProfile car && car.Id == saved.CarId &&
            string.Equals(car.SourceFolderName, saved.CarFolder, StringComparison.OrdinalIgnoreCase))
        {
            var restoring = _restoringSelection;
            _restoringSelection = true;
            try { RestoreCarNumbers(saved); }
            finally { _restoringSelection = restoring; }
        }
    }
    private void FocusMissingSessionSelection()
    {
        ShowDashboardSection(CurrentSessionCard);
        var missing = new[] { HardwareBox, WheelBox, PackBox, CarBox, IntentBox }.FirstOrDefault(x => x.SelectedItem is null);
        missing?.BringIntoView(); missing?.Focus();
    }
}
