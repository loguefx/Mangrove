using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Mangrove.Server.Data;
using Microsoft.IdentityModel.Tokens;

namespace Mangrove.Server.Auth;

/// <summary>Issues JWT access tokens and opaque (rotating) refresh tokens (spec §7).</summary>
public sealed class JwtTokenService
{
    private readonly JwtOptions _options;
    public JwtTokenService(JwtOptions options) => _options = options;

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(_options.Secret));
    public JwtOptions Options => _options;

    public string CreateAccessToken(User user, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var creds = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(_options.AccessTokenLifetime),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Returns a fresh opaque refresh token plus its storable SHA-256 hash.</summary>
    public (string Raw, string Hash) CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return (raw, HashRefreshToken(raw));
    }

    public static string HashRefreshToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
