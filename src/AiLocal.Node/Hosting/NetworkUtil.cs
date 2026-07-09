using System.Net;
using System.Net.Sockets;

namespace AiLocal.Node.Hosting;

public static class NetworkUtil
{
    /// <summary>Best-effort primary LAN IPv4 address of this machine.</summary>
    public static string LocalIPv4()
    {
        // Opening a UDP socket toward a public IP reveals which local NIC/address
        // would be used for outbound traffic. No packet is actually sent.
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep)
                return ep.Address.ToString();
        }
        catch { /* fall through */ }

        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip is not null) return ip.ToString();
        }
        catch { /* fall through */ }

        return "127.0.0.1";
    }
}
