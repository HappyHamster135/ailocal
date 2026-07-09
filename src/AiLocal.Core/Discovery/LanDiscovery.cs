using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiLocal.Core.Discovery;

/// <summary>
/// UDP-multicast peer discovery. The Host announces itself on an interval;
/// Workers and the Overseer listen and learn the Host endpoint automatically -
/// no manual IP configuration needed on the LAN.
/// </summary>
public sealed class LanDiscovery
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IPAddress _group;
    private readonly int _port;

    public LanDiscovery(string multicastAddress, int port)
    {
        _group = IPAddress.Parse(multicastAddress);
        _port = port;
    }

    /// <summary>Periodically broadcast the beacon until cancelled.</summary>
    public async Task AnnounceAsync(DiscoveryBeacon beacon, TimeSpan interval, CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.JoinMulticastGroup(_group);
        var endpoint = new IPEndPoint(_group, _port);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(beacon, JsonOpts);

        while (!ct.IsCancellationRequested)
        {
            try { await udp.SendAsync(bytes, bytes.Length, endpoint); }
            catch { /* transient network issue */ }

            try { await Task.Delay(interval, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Listen for beacons and invoke the callback for each one received.</summary>
    public async Task ListenAsync(Action<DiscoveryBeacon> onBeacon, CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.ExclusiveAddressUse = false;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
        udp.JoinMulticastGroup(_group);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct);
                var beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(result.Buffer, JsonOpts);
                if (beacon is not null) onBeacon(beacon);
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore malformed datagrams */ }
        }
    }
}
