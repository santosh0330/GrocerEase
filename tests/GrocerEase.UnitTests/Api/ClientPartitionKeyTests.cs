using System.Net;
using GrocerEase.Api.RateLimiting;

namespace GrocerEase.UnitTests.Api;

public class ClientPartitionKeyTests
{
    private static string Key(string address) => ClientPartitionKey.For(IPAddress.Parse(address));

    [Fact]
    public void NoAddress_SharesOneKey_SoUnknownClientsAreLimitedTogetherNotLetThrough() =>
        Assert.Equal("unknown", ClientPartitionKey.For(null), StringComparer.Ordinal);

    [Fact]
    public void AnIPv4Address_IsItsOwnKey() =>
        Assert.Equal("203.0.113.7", Key("203.0.113.7"), StringComparer.Ordinal);

    [Fact]
    public void DifferentIPv4Addresses_HaveDifferentKeys() =>
        Assert.NotEqual(Key("203.0.113.7"), Key("203.0.113.8"), StringComparer.Ordinal);

    [Fact]
    public void AnIPv4AddressWrappedInIPv6_IsTheSameClientAsThePlainIPv4Address() =>
        Assert.Equal(Key("203.0.113.7"), Key("::ffff:203.0.113.7"), StringComparer.Ordinal);

    [Fact]
    public void IPv6Addresses_InTheSame64BitPrefix_ShareAKey_SoRotatingTheLowBitsDoesNotEscapeTheLimit() =>
        Assert.Equal(Key("2001:db8:1:2:aaaa:bbbb:cccc:dddd"), Key("2001:db8:1:2::1"), StringComparer.Ordinal);

    [Fact]
    public void IPv6Addresses_InDifferent64BitPrefixes_HaveDifferentKeys() =>
        Assert.NotEqual(Key("2001:db8:1:2::1"), Key("2001:db8:1:3::1"), StringComparer.Ordinal);

    [Fact]
    public void AnIPv6Key_SaysItIsAPrefix() =>
        Assert.EndsWith("/64", Key("2001:db8:1:2::1"), StringComparison.Ordinal);

    [Fact]
    public void AnIPv4Key_CannotCollideWithAnIPv6Key() =>
        Assert.NotEqual(Key("0.0.0.1"), Key("::1"), StringComparer.Ordinal);
}
