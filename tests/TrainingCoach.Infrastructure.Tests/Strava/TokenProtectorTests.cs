using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using TrainingCoach.Infrastructure.Strava;

namespace TrainingCoach.Infrastructure.Tests.Strava;

public class TokenProtectorTests
{
    [Fact]
    public void Roundtrips_and_does_not_store_plain_text()
    {
        var protector = StravaTestData.Protector();

        var protectedText = protector.Protect("secret-access-token");

        Assert.DoesNotContain("secret", protectedText);
        Assert.Equal("secret-access-token", protector.Unprotect(protectedText));
    }

    [Fact]
    public void Same_input_gives_different_ciphertext_each_time()
    {
        var protector = StravaTestData.Protector();

        Assert.NotEqual(protector.Protect("x"), protector.Protect("x"));
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var protector = StravaTestData.Protector();
        var bytes = Convert.FromBase64String(protector.Protect("token"));
        bytes[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void A_different_key_cannot_decrypt()
    {
        var other = new TokenProtector(Options.Create(new SecurityOptions { TokenKey = Convert.ToBase64String(new byte[32]) }));

        Assert.ThrowsAny<CryptographicException>(() => other.Unprotect(StravaTestData.Protector().Protect("token")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")] // valid base64 but not 32 bytes
    public void Missing_or_invalid_key_fails_with_a_clear_message(string key)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new TokenProtector(Options.Create(new SecurityOptions { TokenKey = key })));

        Assert.Contains("Security:TokenKey", ex.Message);
    }
}
