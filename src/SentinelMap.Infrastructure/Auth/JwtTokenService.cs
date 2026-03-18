using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace SentinelMap.Infrastructure.Auth;

public class JwtTokenService
{
    private readonly RSA _rsa;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtTokenService(RSA rsa, string issuer, string audience)
    {
        _rsa = rsa;
        _issuer = issuer;
        _audience = audience;
    }

    public string GenerateAccessToken(Guid userId, string email, string role, string clearanceLevel)
    {
        var key = new RsaSecurityKey(_rsa);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("role", role),
            new Claim("clearance", clearanceLevel),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
