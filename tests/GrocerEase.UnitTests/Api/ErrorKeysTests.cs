using GrocerEase.Api.ExceptionHandling;

namespace GrocerEase.UnitTests.Api;

public class ErrorKeysTests
{
    [Theory]
    [InlineData("Lines", "lines")]
    [InlineData("DeliveryAddress", "deliveryAddress")]
    [InlineData("Lines[0].Quantity", "lines[0].quantity")]
    [InlineData("Lines[12].ProductId", "lines[12].productId")]
    [InlineData("Address.PostCode", "address.postCode")]
    [InlineData("$.email", "$.email")]
    [InlineData("$.lines[0].productId", "$.lines[0].productId")]
    [InlineData("lines", "lines")]
    [InlineData("", "")]
    public void CamelCase_NamesEverySegmentTheWayTheJsonDoes(string key, string expected) =>
        Assert.Equal(expected, ErrorKeys.CamelCase(key));

    [Fact]
    public void Rekey_MergesMessagesOfKeysThatBecomeTheSame()
    {
        var errors = ErrorKeys.Rekey(
        [
            new KeyValuePair<string, string[]>("Lines", ["a"]),
            new KeyValuePair<string, string[]>("lines", ["b", "c"]),
            new KeyValuePair<string, string[]>("DeliveryAddress", ["d"]),
        ]);

        Assert.Equal(["a", "b", "c"], errors["lines"]);
        Assert.Equal(["d"], errors["deliveryAddress"]);
        Assert.Equal(2, errors.Count);
    }
}
