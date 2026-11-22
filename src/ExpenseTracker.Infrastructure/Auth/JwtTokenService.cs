using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ExpenseTracker.Application.Abstractions;
using ExpenseTracker.Application.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseTracker.Infrastructure.Auth;

/// <summary>
/// Issues HS256 JWT access tokens via <see cref="JsonWebTokenHandler"/> and opaque SHA-256 hashed refresh tokens.
/// </summary>
public sealed class JwtTokenService(
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : ITokenService
{
    private static readonly JsonWebTokenHandler TokenHandler = new();
    private readonly JwtOptions _options = jwtOptions.Value;

    public AccessToken CreateAccessToken(User user)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = utcNow.AddMinutes(_options.AccessTokenMinutes);

        var signingKey = new SymmetricSecurityKey(_options.GetKeyBytes());
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(CultureInfo.InvariantCulture),
            [JwtRegisteredClaimNames.Email] = user.Email,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Claims = claims,
            NotBefore = utcNow,
            IssuedAt = utcNow,
            Expires = expiresAt,
            SigningCredentials = credentials
        };

        var token = TokenHandler.CreateToken(descriptor);
        return new AccessToken(token, expiresAt);
    }

    public RefreshTokenValue CreateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var tokenHash = HashRefreshToken(rawToken);
        var expiresAt = timeProvider.GetUtcNow().UtcDateTime.AddDays(_options.RefreshTokenDays);

        return new RefreshTokenValue(rawToken, tokenHash, expiresAt);
    }

    public string HashRefreshToken(string rawToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(hashBytes);
    }
}
