using System.Net;
using System.Net.Sockets;

namespace Eudoria.Core.Identity.Throttling;

/// <summary>The key a client is counted under for the limits: its IPv4 address, or its IPv6 /64 prefix.</summary>
internal static class ClientAddress
{
    /// <summary>Clients without a known address (e.g. in-memory test host) share one key.</summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// An individual usually gets a whole IPv6 /64 block from their provider: counting address by
    /// address would let them change address at every attempt (owner decision, plan 1.4).
    /// IPv4 addresses (including IPv4 mapped to IPv6) are counted as they are.
    /// </summary>
    public static string Key(IPAddress? address)
    {
        if (address is null)
        {
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        byte[] bytes = address.GetAddressBytes(); // the zone (scope id) is dropped
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
