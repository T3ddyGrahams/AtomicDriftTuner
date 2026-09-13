using System.Windows;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class RemoteControlWindow
{
    private void UpdateTouchscreenUi()
    {
        TouchscreenAddressBox.Text = _remote.IsRunning ? TouchscreenDashboardService.LocalAddress(_remote.Port) : "Start ADT Remote below to use the touchscreen.";
        var lan = _remote.IsRunning ? GetUsableLanUrls().FirstOrDefault() : null;
        TouchscreenLanAddressBox.Text = lan is not null ? TouchscreenDashboardService.DeviceAddress(_remote.Port, lan) : "No active private network address. Connect this PC to your home network, then start Remote.";
        CopyTouchscreenAddressButton.IsEnabled = InstallTouchscreenButton.IsEnabled = _remote.IsRunning && !_busy;
        CopyTouchscreenLanAddressButton.IsEnabled = lan is not null && !_busy;
    }

    private void CopyTouchscreenLanAddress_Click(object sender, RoutedEventArgs e)
    {
        if (!_remote.IsRunning || _busy || _closed) return;
        var lan = GetUsableLanUrls().FirstOrDefault();
        if (lan is null) return;
        try { Clipboard.SetText(TouchscreenDashboardService.DeviceAddress(_remote.Port, lan)); TouchscreenInstallText.Text = "Open this address in the Pi/tablet browser on the same home network. Tap Full screen, then enter the pairing code. The layout adapts automatically."; }
        catch (Exception ex) { TouchscreenInstallText.Text = "Could not copy the address: " + ex.Message; }
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
            var lan = GetUsableLanUrls().FirstOrDefault();
            var path = TouchscreenDashboardService.Install(new AppSettingsStore().Load().SimHubRoot, _remote.Port, lan);
            TouchscreenInstallText.Text = "Installed: " + path + "\nReopen ADT Control Center in SimHub. On a Pi/tablet, tap Open ADT to use the whole browser area, then Full screen. Pair with the code below. Keep ADT running. No screen dimensions need to be entered." +
                (lan is null ? "\nOnly a local PC address is available. Connect to your home network and install again before using a separate device." : "\nInstall again if this PC's network address or Remote port changes.");
        }
        catch (Exception ex) { TouchscreenInstallText.Text = "Dashboard installation did not finish: " + ex.Message; }
    }
}
