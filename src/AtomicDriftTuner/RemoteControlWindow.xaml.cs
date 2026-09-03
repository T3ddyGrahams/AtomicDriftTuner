using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class RemoteControlWindow : Window
{
    private readonly RemoteServerService _remote;
    private readonly DispatcherTimer _refreshTimer;
    private bool _updatingCheckbox;

    public RemoteControlWindow(RemoteServerService remote)
    {
        InitializeComponent();
        _remote = remote;
        PortBox.Text = RemoteServerService.DefaultPort.ToString(CultureInfo.InvariantCulture);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += (_, _) => RefreshUi();

        Loaded += (_, _) =>
        {
            _refreshTimer.Start();
            RefreshUi();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(PortBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
                throw new InvalidOperationException("Port must be a whole number.");

            await _remote.StartAsync(port);
            RefreshUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message +
                "\n\nIf the port is already in use, try 5191. Windows Firewall may also prompt the first time Atomic opens a local listening port.",
                "Atomic Remote",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _remote.StopAsync();
            RefreshUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Atomic Remote", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AllowWrites_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingCheckbox)
            return;

        if (AllowWritesBox.IsChecked == true)
        {
            if (!_remote.IsRunning)
            {
                MessageBox.Show(
                    "Start Atomic Remote before enabling remote writes.",
                    "Atomic Remote",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                SetCheckbox(false);
                return;
            }

            var answer = MessageBox.Show(
                "Enable remote AZOM writes for this Atomic run?\n\n" +
                "A paired device will be able to request changes to the limited test allow-list. " +
                "Windows Atomic still validates ranges, serializes writes, uses the existing AZOM guards, verifies live readback, and stops on failure.\n\n" +
                "Direct-drive wheelbases can generate substantial force. Review values on the phone before applying them.",
                "Enable Remote AZOM Writes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                SetCheckbox(false);
                return;
            }

            _remote.SetRemoteWritesEnabled(true);
        }
        else
        {
            _remote.SetRemoteWritesEnabled(false);
        }

        RefreshUi();
    }

    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        var address = _remote.GetLanUrls().FirstOrDefault();
        if (string.IsNullOrWhiteSpace(address))
            return;

        try
        {
            Clipboard.SetText(address);
            ActivityText.Text = "Copied " + address;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Clipboard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RegeneratePairing_Click(object sender, RoutedEventArgs e)
    {
        _remote.RegeneratePairing();
        RefreshUi();
    }

    private void RefreshUi()
    {
        ServerStatusText.Text = _remote.IsRunning
            ? $"RUNNING • local/private network only • port {_remote.Port}"
            : "STOPPED • no remote network listener is active";

        TelemetryStatusText.Text = _remote.GetTelemetryDiagnosticText();
        AddressBox.Text = string.Join(Environment.NewLine, _remote.GetLanUrls());
        PairingCodeText.Text = _remote.PairingCode;
        ActivityText.Text = _remote.LastActivity;

        StartButton.IsEnabled = !_remote.IsRunning;
        StopButton.IsEnabled = _remote.IsRunning;
        PortBox.IsEnabled = !_remote.IsRunning;
        SetCheckbox(_remote.RemoteWritesEnabled);
    }

    private void SetCheckbox(bool value)
    {
        _updatingCheckbox = true;
        try
        {
            AllowWritesBox.IsChecked = value;
        }
        finally
        {
            _updatingCheckbox = false;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
