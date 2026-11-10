using System.Text.Json;
using GrocerEase.Contracts.Authentication;

namespace GrocerEase.UnitTests.Contracts;

public class AuthenticationContractsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RegisterRequest_DeserializesFromCamelCaseJson()
    {
        const string json = """{"firstName":"Ada","lastName":"Lovelace","email":"ada@example.com","password":"s3cret!"}""";

        var request = JsonSerializer.Deserialize<RegisterRequest>(json, Web);

        Assert.Equal(new RegisterRequest("Ada", "Lovelace", "ada@example.com", "s3cret!"), request);
    }

    [Fact]
    public void LoginRequest_DeserializesFromCamelCaseJson()
    {
        const string json = """{"email":"ada@example.com","password":"s3cret!"}""";

        var request = JsonSerializer.Deserialize<LoginRequest>(json, Web);

        Assert.Equal(new LoginRequest("ada@example.com", "s3cret!"), request);
    }

    [Fact]
    public void AuthenticationResponse_SerializesToCamelCaseJson()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var response = new AuthenticationResponse(id, "Ada", "Lovelace", "ada@example.com", "jwt", "refresh");

        var json = JsonSerializer.Serialize(response, Web);

        Assert.Equal(
            """{"id":"11111111-2222-3333-4444-555555555555","firstName":"Ada","lastName":"Lovelace","email":"ada@example.com","token":"jwt","refreshToken":"refresh"}""",
            json);
    }

    [Fact]
    public void RegisterRequest_ToString_DoesNotContainPassword() =>
        Assert.DoesNotContain("s3cret!", new RegisterRequest("Ada", "Lovelace", "ada@example.com", "s3cret!").ToString(), StringComparison.Ordinal);

    [Fact]
    public void LoginRequest_ToString_DoesNotContainPassword() =>
        Assert.DoesNotContain("s3cret!", new LoginRequest("ada@example.com", "s3cret!").ToString(), StringComparison.Ordinal);

    [Fact]
    public void AuthenticationResponse_ToString_DoesNotContainEitherToken()
    {
        var text = new AuthenticationResponse(Guid.NewGuid(), "Ada", "Lovelace", "ada@example.com", "jwt-value", "refresh-value").ToString();

        Assert.DoesNotContain("jwt-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-value", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshRequest_DeserializesFromCamelCaseJson()
    {
        const string json = """{"refreshToken":"the-token"}""";

        var request = JsonSerializer.Deserialize<RefreshRequest>(json, Web);

        Assert.Equal(new RefreshRequest("the-token"), request);
    }

    [Fact]
    public void RefreshRequest_ToString_DoesNotContainTheRefreshToken()
    {
        var text = new RefreshRequest("refresh-value").ToString();

        Assert.DoesNotContain("refresh-value", text, StringComparison.Ordinal);
        Assert.Contains("RefreshToken = ***", text, StringComparison.Ordinal);
    }
}
