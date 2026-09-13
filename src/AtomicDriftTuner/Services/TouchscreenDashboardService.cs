using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AtomicDriftTuner.Services;

/// <summary>Creates a SimHub entry point for ADT's adaptive touchscreen page.</summary>
public static class TouchscreenDashboardService
{
    public const string Name = "ADT Control Center";
    public static string LocalAddress(int port)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        return $"http://127.0.0.1:{port}/dash";
    }

    public static string DeviceAddress(int port, string? baseAddress = null)
    {
        var local = LocalAddress(port);
        if (string.IsNullOrWhiteSpace(baseAddress)) return local;
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                IPAddress.TryParse(uri.Host, out var ip) && (IPAddress.IsLoopback(ip) || LanAddressService.IsPrivateIPv4(ip))))
            throw new ArgumentException("Choose an HTTP address on this PC's private network.", nameof(baseAddress));
        return new UriBuilder(Uri.UriSchemeHttp, uri.Host, port, "/dash").Uri.AbsoluteUri;
    }

    public static string Render(int port, string? baseAddress = null)
    {
        var dashboard = JsonNode.Parse(Template)!;
        dashboard["Screens"]![0]!["Items"]![0]!["StartAddress"] = DeviceAddress(port, baseAddress) + "/launch";
        dashboard["Metadata"]!["DashboardVersion"] = DistributionInfo.Version;
        return dashboard.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static string Install(string? simHubPath, int port, string? baseAddress = null)
    {
        var json = Render(port, baseAddress);
        if (string.IsNullOrWhiteSpace(simHubPath)) throw new InvalidOperationException("Choose your SimHub folder in Setup & Paths first.");
        var root = Path.GetFullPath(simHubPath);
        if (!File.Exists(Path.Combine(root, "SimHub.Plugins.dll"))) throw new InvalidOperationException("The selected folder is not a SimHub installation. Check Setup & Paths.");
        var folder = Path.Combine(root, "DashTemplates", Name);
        var path = Path.Combine(folder, Name + ".djson");
        Directory.CreateDirectory(folder);
        SavePreservingExisting(path, json);
        SavePreservingExisting(path + ".metadata", JsonNode.Parse(json)!["Metadata"]!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static void SavePreservingExisting(string path, string text)
    {
        if (File.Exists(path))
        {
            if (File.ReadAllText(path) == text) return;
            var backups = Path.Combine(Path.GetDirectoryName(path)!, "_Backups");
            Directory.CreateDirectory(backups);
            File.Copy(path, Path.Combine(backups, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".bak"));
        }
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text); File.Move(temp, path, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private const string Template = """
{
  "Version": 2,
  "Id": "e9b2fe3d-2b11-4f36-9ebd-7f8e77c2b761",
  "BaseHeight": 720,
  "BaseWidth": 1280,
  "BackgroundColor": "#FF0E1116",
  "Screens": [{
    "Name": "ADT Control Center", "ScreenId": "2c8732e3-87ad-48c7-bb74-67926a5ea2ca",
    "InGameScreen": true, "IdleScreen": true, "PitScreen": true,
    "AllowOverlays": true, "BackgroundColor": "#FF0E1116",
    "Items": [{
      "$type": "SimHub.Plugins.OutputPlugins.GraphicalDash.Models.WebPageItem, SimHub.Plugins",
      "Name": "ADT touchscreen", "StartAddress": "http://127.0.0.1:5190/dash/launch",
      "AllowTransparency": false, "ClickThrough": false, "BackgroundColor": "#FF0E1116",
      "Height": 720.0, "Width": 1280.0, "Left": 0.0, "Top": 0.0, "Visible": true,
      "RenderingSkip": 0, "MinimumRefreshIntervalMS": 0.0
    }]
  }],
  "Images": [],
  "Metadata": {
    "SettingsBuilder": {"Settings": [], "IsEditMode": false},
    "ScreenCount": 1.0, "InGameScreensIndexs": [0], "IdleScreensIndexs": [0], "PitScreensIndexs": [0],
    "MainPreviewIndex": 0, "IsOverlay": false, "MetadataVersion": 2.0,
    "EnableOnDashboardMessaging": true, "PreferredTouchMode": 0,
    "Width": 1280.0, "Height": 720.0, "Title": "ADT Control Center",
    "Description": "Open ADT on this screen for adaptive touch controls. No per-device resolution settings.",
    "Author": "Atomic Drift Tuner", "DashboardVersion": ""
  },
  "ShowOnScreenControls": true, "IsOverlay": false, "EnableClickThroughOverlay": false,
  "EnableOnDashboardMessaging": true, "UseStrictJSIsolation": false
}
""";
}
