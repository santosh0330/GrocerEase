using System.Net;

namespace GrocerEase.Api.RateLimiting;

/// <summary>
/// Decides which requests count as "the same client" for rate limiting. The address is the only thing a client cannot
/// choose freely, but one machine can own many addresses, so the key groups the ones that belong together.
/// </summary>
public static class ClientPartitionKey
{
    /// <summary>Used when the connection has no remote address. They all share this key, so they are limited together, not exempt.</summary>
    public const string Unknown = "unknown";

    public static string For(IPAddress? address)
    {
        if (address is null)
        {
            return Unknown;
        }

        // "::ffff:203.0.113.7" and "203.0.113.7" are the same machine reaching a dual-stack server.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        // A single customer is normally given a whole /64 (2^64 addresses). Limiting per full IPv6 address would let
        // them use a fresh address for every attempt, so the key is the /64 prefix.
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }
}
