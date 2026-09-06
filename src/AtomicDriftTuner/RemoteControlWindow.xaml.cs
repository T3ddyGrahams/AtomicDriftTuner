using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class RemoteControlWindow : Window
{
    private readonly RemoteServerService _remote;

    private readonly DispatcherTimer _refreshTimer =
        new()
        {
            Interval =
                TimeSpan.FromSeconds(
                    1)
        };

    private readonly SemaphoreSlim _serverOperationGate =
        new(
            1,
            1);

    private readonly CancellationTokenSource _lifetimeCancellation =
        new();

    private bool _updatingCheckbox;
    private bool _busy;
    private bool _closed;

    public RemoteControlWindow(
        RemoteServerService remote)
    {
        ArgumentNullException.ThrowIfNull(
            remote);

        InitializeComponent();

        _remote =
            remote;

        PortBox.Text =
            (
                remote.IsRunning
                    ? remote.Port
                    : RemoteServerService.DefaultPort
            ).ToString(
                CultureInfo.InvariantCulture);

        _refreshTimer.Tick +=
            RefreshTimer_Tick;

        _remote.StateChanged +=
            Remote_StateChanged;

        Closed +=
            RemoteControlWindow_Closed;

        // MainWindow embeds this Window's Content without showing the backing
        // Window, so Window.Loaded is not a reliable initialization hook here.
        // Start the lightweight status refresh directly from the constructor.
        _refreshTimer.Start();

        TryRefreshUi();
    }

    private async void Start_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy ||
            _closed)
        {
            return;
        }

        if (
            !int.TryParse(
                PortBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var port) ||
            port is < 1024 or > 65535)
        {
            MessageBox.Show(
                "Remote port must be a whole number between 1024 and 65535.",
                "ADT Remote",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        await RunServerOperationAsync(
            cancellationToken =>
                _remote.StartAsync(
                    port,
                    cancellationToken),
            "Start ADT Remote",
            "\n\nIf the port is already in use, try 5191. Windows Firewall may also prompt the first time ADT opens a local listening port.");
    }

    private async void Stop_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy ||
            _closed)
        {
            return;
        }

        await RunServerOperationAsync(
            cancellationToken =>
                _remote.StopAsync(
                    cancellationToken),
            "Stop ADT Remote");
    }

    private async Task RunServerOperationAsync(
        Func<CancellationToken, Task> operation,
        string errorTitle,
        string errorSuffix = "")
    {
        if (_closed)
        {
            return;
        }

        if (
            !await _serverOperationGate
                .WaitAsync(
                    0))
        {
            return;
        }

        try
        {
            _busy =
                true;

            TryRefreshUi();

            await operation(
                _lifetimeCancellation.Token);

            if (!_closed)
            {
                TryRefreshUi();
            }
        }
        catch (OperationCanceledException)
            when (
                _closed ||
                _lifetimeCancellation
                    .IsCancellationRequested)
        {
            // The control workspace is closing. Do not surface a cancellation
            // dialog for a user-requested shutdown of this UI.
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                MessageBox.Show(
                    ex.Message +
                    errorSuffix,
                    errorTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _busy =
                false;

            if (!_closed)
            {
                TryRefreshUi();
            }

            _serverOperationGate.Release();
        }
    }

    private void AllowWrites_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            _updatingCheckbox ||
            _busy ||
            _closed)
        {
            return;
        }

        try
        {
            if (
                AllowWritesBox.IsChecked ==
                true)
            {
                if (!_remote.IsRunning)
                {
                    MessageBox.Show(
                        "Start ADT Remote before enabling remote writes.",
                        "ADT Remote",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    SetCheckbox(
                        false);

                    return;
                }

                var answer =
                    MessageBox.Show(
                        "Enable remote AZOM writes for this ADT run?\n\n" +
                        "A paired device will be able to request changes only to ADT Remote's explicit supported allow-list. " +
                        "Windows ADT still validates ranges, serializes writes, uses the existing AZOM guards, verifies live readback, and stops a guarded batch on failure.\n\n" +
                        "Direct-drive wheelbases can generate substantial force. Only enable this while the rig is stationary and you are prepared for unexpected wheel movement.\n\n" +
                        "Continue?",
                        "Enable Remote AZOM Writes",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (
                    answer !=
                    MessageBoxResult.Yes)
                {
                    SetCheckbox(
                        false);

                    return;
                }

                _remote.SetRemoteWritesEnabled(
                    true);

                if (!_remote.RemoteWritesEnabled)
                {
                    SetCheckbox(
                        false);

                    MessageBox.Show(
                        "ADT Remote is no longer running, so remote writes were not enabled.",
                        "ADT Remote",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else
            {
                _remote.SetRemoteWritesEnabled(
                    false);
            }
        }
        catch (Exception ex)
        {
            try
            {
                _remote.SetRemoteWritesEnabled(
                    false);
            }
            catch
            {
                // Best-effort fail-safe only.
            }

            SetCheckbox(
                false);

            MessageBox.Show(
                ex.Message,
                "ADT Remote Write Safety",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            TryRefreshUi();
        }
    }

    private void CopyAddress_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            !_remote.IsRunning ||
            _busy ||
            _closed)
        {
            return;
        }

        try
        {
            var address =
                GetUsableLanUrls()
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(
                    address))
            {
                MessageBox.Show(
                    "ADT Remote is running, but no usable private IPv4 LAN address is currently available for another device. " +
                    "Check that the PC is connected to your trusted local network/Wi-Fi.",
                    "ADT Remote Address",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            Clipboard.SetText(
                address);

            ActivityText.Text =
                "Copied " +
                address;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Clipboard",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RegeneratePairing_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            !_remote.IsRunning ||
            _busy ||
            _closed)
        {
            return;
        }

        var answer =
            MessageBox.Show(
                "Generate new ADT Remote pairing credentials?\n\n" +
                "Previously paired browsers will be signed out and must pair again. " +
                "Remote AZOM writes will also be turned OFF and must be explicitly re-enabled afterward.",
                "New Pairing Code",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (
            answer !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            // Credential rotation changes which browser is authorized. Require
            // a fresh, explicit write-enable decision after that trust change.
            _remote.SetRemoteWritesEnabled(
                false);

            _remote.RegeneratePairing();

            TryRefreshUi();
        }
        catch (Exception ex)
        {
            try
            {
                _remote.SetRemoteWritesEnabled(
                    false);
            }
            catch
            {
                // Best-effort fail-safe only.
            }

            MessageBox.Show(
                ex.Message,
                "ADT Remote Pairing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            TryRefreshUi();
        }
    }

    private void RefreshTimer_Tick(
        object? sender,
        EventArgs e)
    {
        TryRefreshUi();
    }

    private void Remote_StateChanged(
        object? sender,
        EventArgs e)
    {
        if (
            _closed ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            TryRefreshUi();
            return;
        }

        _ =
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(
                    TryRefreshUi));
    }

    private void TryRefreshUi()
    {
        if (_closed)
        {
            return;
        }

        try
        {
            RefreshUi();
        }
        catch (Exception ex)
        {
            ServerStatusText.Text =
                _remote.IsRunning
                    ? "RUNNING • ADT could not refresh all Remote status details"
                    : "STOPPED • no remote network listener is active";

            ActivityText.Text =
                "Remote status refresh failed: " +
                ex.Message;

            StartButton.IsEnabled =
                !_busy &&
                !_remote.IsRunning;

            StopButton.IsEnabled =
                !_busy &&
                _remote.IsRunning;

            PortBox.IsEnabled =
                !_busy &&
                !_remote.IsRunning;

            CopyAddressButton.IsEnabled =
                false;

            RegeneratePairingButton.IsEnabled =
                !_busy &&
                _remote.IsRunning;

            AllowWritesBox.IsEnabled =
                !_busy &&
                _remote.IsRunning;

            SetCheckbox(
                _remote.IsRunning &&
                _remote.RemoteWritesEnabled);
        }
    }

    private void RefreshUi()
    {
        var running =
            _remote.IsRunning;

        ServerStatusText.Text =
            running
                ? $"RUNNING • trusted local/private network only • port {_remote.Port}"
                : "STOPPED • no remote network listener is active";

        TelemetryStatusText.Text =
            _remote.GetTelemetryDiagnosticText();

        var lanUrls =
            running
                ? GetUsableLanUrls()
                : Array.Empty<string>();

        if (running)
        {
            AddressBox.Text =
                lanUrls.Count > 0
                    ? string.Join(
                        Environment.NewLine,
                        lanUrls)
                    : "No usable private IPv4 LAN address detected. Check the PC's trusted local network/Wi-Fi connection.";

            PairingCodeText.Text =
                _remote.PairingCode;
        }
        else
        {
            AddressBox.Text =
                "Start ADT Remote to show active LAN addresses.";

            PairingCodeText.Text =
                "------";
        }

        ActivityText.Text =
            _remote.LastActivity;

        StartButton.IsEnabled =
            !_busy &&
            !running;

        StopButton.IsEnabled =
            !_busy &&
            running;

        PortBox.IsEnabled =
            !_busy &&
            !running;

        CopyAddressButton.IsEnabled =
            !_busy &&
            running &&
            lanUrls.Count > 0;

        RegeneratePairingButton.IsEnabled =
            !_busy &&
            running;

        AllowWritesBox.IsEnabled =
            !_busy &&
            running;

        SetCheckbox(
            running &&
            _remote.RemoteWritesEnabled);
    }

    private IReadOnlyList<string> GetUsableLanUrls()
    {
        return
            _remote.GetLanUrls()
                .Where(
                    url =>
                    {
                        if (
                            !Uri.TryCreate(
                                url,
                                UriKind.Absolute,
                                out var uri))
                        {
                            return false;
                        }

                        return
                            !string.Equals(
                                uri.Host,
                                "localhost",
                                StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(
                                uri.Host,
                                "127.0.0.1",
                                StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(
                                uri.Host,
                                "::1",
                                StringComparison.OrdinalIgnoreCase);
                    })
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private void SetCheckbox(
        bool value)
    {
        _updatingCheckbox =
            true;

        try
        {
            AllowWritesBox.IsChecked =
                value;
        }
        finally
        {
            _updatingCheckbox =
                false;
        }
    }

    private void RemoteControlWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _closed =
            true;

        _refreshTimer.Stop();

        _refreshTimer.Tick -=
            RefreshTimer_Tick;

        _remote.StateChanged -=
            Remote_StateChanged;

        Closed -=
            RemoteControlWindow_Closed;

        _lifetimeCancellation.Cancel();

        // Closing the controls must never leave a hidden remote-write
        // authorization active. The server itself is intentionally left
        // running so read-only telemetry/status access can continue.
        try
        {
            _remote.SetRemoteWritesEnabled(
                false);
        }
        catch
        {
            // Best-effort fail-safe during close.
        }
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}
