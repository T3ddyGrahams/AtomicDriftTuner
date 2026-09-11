using AtomicDriftTuner.Models;
namespace AtomicDriftTuner.Services;

public sealed class GuidedIntegrationService
{
    public async Task<IntegrationState> CheckAsync(string? simHubRoot, string pipe, CancellationToken token)
    {
        var path = SimHubLocator.FindSimHubRoot(simHubRoot);
        bool installed = SimHubLocator.IsValidRoot(path);
        bool running = new BridgeManagerService().IsSimHubRunning();
        bool bridgeInstalled = installed && File.Exists(Path.Combine(path!, BridgeManagerService.BridgeFileName));
        try
        {
            var live = await new AzomBridgeClient(pipe).ReadSnapshotAsync(1400, token);
            return new(installed, running, bridgeInstalled, true, live.PluginDetected || live.AzomAvailable, live.SettingsReadable);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return new(installed, running, bridgeInstalled, false, false, false); }
    }
}
