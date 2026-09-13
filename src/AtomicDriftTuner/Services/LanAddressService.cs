using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AtomicDriftTuner.Services;

public static class LanAddressService
{
    public sealed record Candidate(IPAddress Address, bool HasGateway, bool IsVirtual);

    public static bool IsPrivateIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 ||
            bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31);
    }

    public static IReadOnlyList<string> SelectUrls(IEnumerable<Candidate> candidates, int port)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var urls = candidates.Where(c => IsPrivateIPv4(c.Address))
            .OrderByDescending(c => c.HasGateway)
            .ThenBy(c => c.IsVirtual)
            .Select(c => $"http://{c.Address}:{port}/")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (urls.Count == 0) urls.Add($"http://localhost:{port}/");
        return urls;
    }

    public static IReadOnlyList<string> GetUrls(int port)
    {
        var candidates = new List<Candidate>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var properties = nic.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !g.Address.Equals(IPAddress.Any) && !IPAddress.IsLoopback(g.Address));
                var description = nic.Name + " " + nic.Description;
                var isVirtual = new[] { "virtual", "vmware", "vethernet", "tailscale", "vpn", "docker" }
                    .Any(word => description.Contains(word, StringComparison.OrdinalIgnoreCase));
                foreach (var unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        unicast.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred)
                        candidates.Add(new(unicast.Address, hasGateway, isVirtual));
                }
            }
            catch (NetworkInformationException) { /* Adapter changed while enumerating; keep the other usable addresses. */ }
        }
        return SelectUrls(candidates, port);
    }
}
