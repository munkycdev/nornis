using System.Net;
using System.Net.Sockets;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Connects only to public internet addresses (feature 39). An OpenAI-compatible key carries a base
/// URL its owner typed, and the API and the Worker call it from inside Azure: without this, a URL
/// naming the instance metadata service, a private address, or a name that resolves to one would
/// have Nornis make requests on the attacker's behalf. The check runs on every connection, after
/// DNS, so a name that resolves somewhere private later is refused then too; redirects are not
/// followed, so a public endpoint can't bounce a request inward.
/// </summary>
public static class PublicEndpointGuard
{
    /// <summary>A handler that refuses to connect anywhere but a public address.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = ConnectAsync,
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        var allowed = addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
        {
            throw new HttpRequestException($"'{host}' is not a public internet address, so Nornis won't call it.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Whether an address is on the public internet: not loopback, private, link-local
    /// (which includes the metadata service at 169.254.169.254), shared, reserved or multicast.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] is 0 or 10 or 127 or >= 224
                     || b[0] == 100 && b[1] is >= 64 and <= 127      // shared (carrier NAT)
                     || b[0] == 169 && b[1] == 254                   // link-local, metadata
                     || b[0] == 172 && b[1] is >= 16 and <= 31
                     || b[0] == 192 && b[1] == 168
                     || b[0] == 192 && b[1] == 0 && b[2] is 0 or 2   // IETF, documentation
                     || b[0] == 198 && b[1] is 18 or 19              // benchmarking
                     || b[0] == 198 && b[1] == 51 && b[2] == 100
                     || b[0] == 203 && b[1] == 0 && b[2] == 113);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IPv6Loopback.Equals(address) || IPAddress.IPv6None.Equals(address)
                || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return false;
            }

            var b = address.GetAddressBytes();
            return !((b[0] & 0xFE) == 0xFC                                  // unique local
                     || b[0] == 0x20 && b[1] == 0x02                       // 6to4: embeds any IPv4
                     || b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8 // documentation
                     || b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B); // NAT64
        }

        return false;
    }
}
