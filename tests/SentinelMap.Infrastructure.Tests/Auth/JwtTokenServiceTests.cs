using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using FluentAssertions;
using SentinelMap.Infrastructure.Auth;

namespace SentinelMap.Infrastructure.Tests.Auth;

public class JwtTokenServiceTests
{
    [Fact]
    public void GenerateAccessToken_ReturnsValidJwt()
    {
        using var rsa = RSA.Create(2048);
        var service = new JwtTokenService(rsa, "SentinelMap", "SentinelMap");

        var token = service.GenerateAccessToken(
            userId: Guid.NewGuid(),
            email: "test@sentinel.local",
            role: "Analyst",
            clearanceLevel: "OfficialSensitive"
        );

        token.Should().NotBeNullOrEmpty();

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Issuer.Should().Be("SentinelMap");
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == "Analyst");
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GenerateRefreshToken_Returns64CharHex()
    {
        using var rsa = RSA.Create(2048);
        var service = new JwtTokenService(rsa, "SentinelMap", "SentinelMap");

        var token = service.GenerateRefreshToken();

        token.Should().HaveLength(64);
        token.Should().MatchRegex("^[a-f0-9]+$");
    }
}
