namespace GrocerEase.Infrastructure.Authentication;

/// <summary>
/// The names of the claims this application adds to a token, besides the registered ones such as <c>sub</c> and <c>email</c>.
/// One definition, shared by the generator that writes them and everything that reads them.
/// </summary>
public static class JwtClaims
{
    /// <summary>The name of the role the user holds, such as <c>Customer</c>.</summary>
    public const string Role = "role";
}
