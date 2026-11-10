using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using GrocerEase.Application.Abstractions;

namespace GrocerEase.Infrastructure.Authentication;

/// <summary>
/// A refresh token is 32 random bytes from the operating system's secure generator (256 bits), written in base64url so it
/// travels safely in JSON. Only its SHA-256 hash is stored.
/// </summary>
/// <remarks>
/// A plain, fast hash is right here, unlike for passwords. A password is short and guessable, so its hash must be slow to make
/// guessing expensive. A token has 256 bits of randomness: nobody can guess it, however fast the hash. A fast, unsalted hash also
/// gives the same output for the same token, which is what lets the server find the row by an indexed lookup.
/// </remarks>
public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    private const int TokenBytes = 32;

    public string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    public byte[] Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
