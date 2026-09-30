using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PropaneDriver.Server.Data;
using PropaneDriver.Server.Services;

namespace PropaneDriver.Tests;

// Pure unit tests for the JWT issuer. We don't go through the auth
// pipeline here — that's covered by AuthorizationTests. This file proves
// the issued token has the right shape and that an issuer/audience/key
// mismatch breaks validation, so a mis-configured deploy gets caught fast.
public class JwtTokenServiceTests
{
    private const string SigningKey = "unit-test-signing-key-must-be-at-least-32-chars-long-12345";
    private const string Issuer = "UnitTestIssuer";
    private const string Audience = "UnitTestAudience";

    private static IConfiguration BuildConfig(
        string? key = SigningKey,
        string? issuer = Issuer,
        string? audience = Audience,
        string? expirationHours = "2")
    {
        var pairs = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = issuer,
            ["Jwt:Audience"] = audience,
            ["Jwt:Key"] = key,
            ["Jwt:ExpirationHours"] = expirationHours,
        };
        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }

    private static UserDbRecord MakeUser(string userName = "unit-driver")
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            PasswordHash = "irrelevant-for-jwt-tests",
            FirstName = "Unit",
            LastName = "Tester",
            Email = $"{userName}@test.local",
            PhoneNumber = "555-0100",
            CreatedAt = DateTime.UtcNow,
        };

    private static JwtSecurityToken DecodeAndValidate(
        string tokenString,
        string expectedIssuer = Issuer,
        string expectedAudience = Audience,
        string signingKey = SigningKey)
    {
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = expectedIssuer,
            ValidAudience = expectedAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.Zero,
        };

        new JwtSecurityTokenHandler().ValidateToken(tokenString, validationParameters, out var validated);
        return (JwtSecurityToken)validated;
    }

    [Fact]
    public void CreateToken_EmbedsSubAndNameIdentifierAsUserId()
    {
        var user = MakeUser();
        var jwtTokenService = new JwtTokenService(BuildConfig());

        var tokenString = jwtTokenService.CreateTokenForUser(user, ["driver"]);
        var decoded = DecodeAndValidate(tokenString);

        Assert.Equal(user.Id.ToString(), decoded.Subject);
        Assert.Equal(user.Id.ToString(),
            decoded.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);
    }

    [Fact]
    public void CreateToken_EmbedsUserNameClaim()
    {
        var user = MakeUser(userName: "claim-check-user");
        var jwtTokenService = new JwtTokenService(BuildConfig());

        var decoded = DecodeAndValidate(jwtTokenService.CreateTokenForUser(user, ["driver"]));

        Assert.Equal("claim-check-user",
            decoded.Claims.Single(c => c.Type == ClaimTypes.Name).Value);
    }

    [Fact]
    public void CreateToken_EmbedsOneRoleClaimPerRole()
    {
        var jwtTokenService = new JwtTokenService(BuildConfig());

        var decoded = DecodeAndValidate(jwtTokenService.CreateTokenForUser(MakeUser(), ["driver", "supervisor"]));

        Assert.Equal(["driver", "supervisor"],
            decoded.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value));
    }

    [Fact]
    public void CreateToken_NoRoles_EmbedsNoRoleClaim()
    {
        var jwtTokenService = new JwtTokenService(BuildConfig());

        var decoded = DecodeAndValidate(jwtTokenService.CreateTokenForUser(MakeUser(), []));

        Assert.DoesNotContain(decoded.Claims, c => c.Type == ClaimTypes.Role);
    }

    [Fact]
    public void CreateToken_SetsConfiguredIssuerAndAudience()
    {
        var jwtTokenService = new JwtTokenService(BuildConfig(
            issuer: "CustomIssuer", audience: "CustomAudience"));

        var decoded = DecodeAndValidate(
            jwtTokenService.CreateTokenForUser(MakeUser(), ["driver"]),
            expectedIssuer: "CustomIssuer",
            expectedAudience: "CustomAudience");

        Assert.Equal("CustomIssuer", decoded.Issuer);
        Assert.Contains("CustomAudience", decoded.Audiences);
    }

    [Fact]
    public void CreateToken_MissingIssuerInConfig_FallsBackToDefault()
    {
        // Defaulting matters because a misconfigured deploy that drops the
        // issuer line shouldn't silently start issuing un-validatable tokens.
        var jwtTokenService = new JwtTokenService(BuildConfig(issuer: null, audience: null));

        var decoded = DecodeAndValidate(
            jwtTokenService.CreateTokenForUser(MakeUser(), ["driver"]),
            expectedIssuer: "PropaneDriver",
            expectedAudience: "PropaneDriverClient");

        Assert.Equal("PropaneDriver", decoded.Issuer);
        Assert.Contains("PropaneDriverClient", decoded.Audiences);
    }

    [Fact]
    public void CreateToken_HonorsConfiguredExpirationHours()
    {
        var jwtTokenService = new JwtTokenService(BuildConfig(expirationHours: "3"));
        var beforeUtc = DateTime.UtcNow;

        var decoded = DecodeAndValidate(jwtTokenService.CreateTokenForUser(MakeUser(), ["driver"]));

        // Allow 1 minute slack for slow runners. Expected validity ~3h.
        var expectedExpiry = beforeUtc.AddHours(3);
        Assert.InRange(decoded.ValidTo, expectedExpiry.AddMinutes(-1), expectedExpiry.AddMinutes(1));
    }

    [Fact]
    public void CreateToken_NoExpirationConfigured_DefaultsTo12Hours()
    {
        var jwtTokenService = new JwtTokenService(BuildConfig(expirationHours: null));
        var beforeUtc = DateTime.UtcNow;

        var decoded = DecodeAndValidate(jwtTokenService.CreateTokenForUser(MakeUser(), ["driver"]));

        var expectedExpiry = beforeUtc.AddHours(12);
        Assert.InRange(decoded.ValidTo, expectedExpiry.AddMinutes(-1), expectedExpiry.AddMinutes(1));
    }

    [Fact]
    public void CreateToken_NotValidatableWithDifferentSigningKey()
    {
        var jwtTokenService = new JwtTokenService(BuildConfig());
        var tokenString = jwtTokenService.CreateTokenForUser(MakeUser(), ["driver"]);

        // Swapping the verifier's key should reject the signature.
        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            DecodeAndValidate(
                tokenString,
                signingKey: "totally-different-signing-key-still-32-chars-long-x"))
        ;
    }

    [Fact]
    public void CreateToken_NotValidatableWithDifferentIssuer()
    {
        var jwtTokenService = new JwtTokenService(BuildConfig());
        var tokenString = jwtTokenService.CreateTokenForUser(MakeUser(), ["driver"]);

        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            DecodeAndValidate(tokenString, expectedIssuer: "SomeOtherIssuer"));
    }

    [Fact]
    public void CreateToken_MissingSigningKey_Throws()
    {
        var jwtTokenService = new JwtTokenService(BuildConfig(key: null));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            jwtTokenService.CreateTokenForUser(MakeUser(), ["driver"]));
        Assert.Contains("Jwt:Key", ex.Message);
    }

    [Fact]
    public void CreateToken_EachCallGetsDistinctJti()
    {
        // The Jti is just an entropy sink today, but proving it's unique
        // means future revocation/replay-prevention work can rely on it.
        var jwtTokenService = new JwtTokenService(BuildConfig());

        var first = DecodeAndValidate(jwtTokenService.CreateTokenForUser(MakeUser(), ["driver"]));
        var second = DecodeAndValidate(jwtTokenService.CreateTokenForUser(MakeUser(), ["driver"]));

        var jtiFirst = first.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var jtiSecond = second.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        Assert.NotEqual(jtiFirst, jtiSecond);
    }
}
