namespace GrocerEase.Application.Abstractions;

/// <summary>
/// Makes refresh tokens and turns one into the only form the server keeps. Infrastructure implements it, so a handler never
/// knows how the randomness or the hash is produced.
/// </summary>
public interface IRefreshTokenGenerator
{
    /// <summary>A new, unguessable token to give to the client. The server must not store it: store <see cref="Hash"/> of it.</summary>
    string Generate();

    /// <summary>The hash of a token, the same for the same token every time, so a presented token can be looked up by it.</summary>
    byte[] Hash(string token);
}
