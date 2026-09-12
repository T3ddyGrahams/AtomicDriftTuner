using System.Windows;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class RemoteControlWindow
{
    private void UpdateTouchscreenUi()
    {
        TouchscreenAddressBox.Text = _remote.IsRunning ? TouchscreenDashboardService.LocalAddress(_remote.Port) : "Start ADT Remote below to use the touchscreen.";
        CopyTouchscreenAddressButton.IsEnabled = InstallTouchscreenButton.IsEnabled = _remote.IsRunning && !_busy;
    }

    private void CopyTouchscreenAddress_Click(object sender, RoutedEventArgs e)
    {
        if (!_remote.IsRunning || _busy || _closed) return;
        try { Clipboard.SetText(TouchscreenDashboardService.LocalAddress(_remote.Port)); TouchscreenInstallText.Text = "Copied the address for a touchscreen or SimHub on this PC. Phones/tablets use this PC's LAN address instead of 127.0.0.1."; }
        catch (Exception ex) { TouchscreenInstallText.Text = "Could not copy the address: " + ex.Message; }
    }

    private void InstallTouchscreen_Click(object sender, RoutedEventArgs e)
    {
        if (!_remote.IsRunning || _busy || _closed) return;
        try
        {
            var path = TouchscreenDashboardService.Install(new AppSettingsStore().Load().SimHubRoot, _remote.Port);
            TouchscreenInstallText.Text = "Installed: " + path + "\nIn SimHub, open Dash Studio → ADT Control Center. Reopen Dash Studio if the new dashboard is not listed yet. Launch it as a window on your touchscreen and pair with the code below. Keep ADT running.";
        }
        catch (Exception ex) { TouchscreenInstallText.Text = "Dashboard installation did not finish: " + ex.Message; }
    }
}
