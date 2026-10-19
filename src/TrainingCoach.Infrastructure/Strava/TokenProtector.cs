using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace TrainingCoach.Infrastructure.Strava;

/// <summary>Key for encrypting OAuth tokens at rest; lives in User Secrets (<c>Security:TokenKey</c>, base64, 32 bytes).</summary>
public sealed class SecurityOptions
{
    public const string Section = "Security";

    public string TokenKey { get; set; } = "";
}

/// <summary>AES-256-GCM encryption of OAuth tokens before they are stored in the database (SPEC 4.1).</summary>
public sealed class TokenProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public TokenProtector(IOptions<SecurityOptions> options)
    {
        var base64 = options.Value.TokenKey;
        if (string.IsNullOrWhiteSpace(base64))
            throw new InvalidOperationException(
                "Security:TokenKey is not configured. Set it with: dotnet user-secrets set \"Security:TokenKey\" <base64 of 32 random bytes> --project src/TrainingCoach.Web");

        try { _key = Convert.FromBase64String(base64); }
        catch (FormatException ex) { throw new InvalidOperationException("Security:TokenKey is not valid base64.", ex); }

        if (_key.Length != 32)
            throw new InvalidOperationException("Security:TokenKey must be 32 bytes (base64 encoded).");
    }

    public string Protect(string plain)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plain);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string protectedText)
    {
        var data = Convert.FromBase64String(protectedText);
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Protected token is too short.");

        var nonce = data[..NonceSize];
        var tag = data[NonceSize..(NonceSize + TagSize)];
        var cipher = data[(NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return System.Text.Encoding.UTF8.GetString(plain);
    }
}
