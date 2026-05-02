using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;

namespace ITAMS.Api.Services;

public sealed class TokenService(IOptions<JwtSettings> settings)
{
    private readonly JwtSettings _jwtSettings = settings.Value;

    public (string AccessToken, DateTime ExpiresAtUtc) CreateAccessToken(UserDocument user, ObjectId sessionId)
    {
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.AddMinutes(_jwtSettings.AccessTokenMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Sid, sessionId.ToString()),
                new Claim("role", user.Role)
            ]),
            Expires = expiresAtUtc,
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return (handler.WriteToken(token), expiresAtUtc);
    }

    public (string RefreshToken, string RefreshTokenHash, DateTime ExpiresAtUtc) CreateRefreshToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var refreshToken = Convert.ToBase64String(tokenBytes);
        return (refreshToken, HashRefreshToken(refreshToken), DateTime.UtcNow.AddHours(_jwtSettings.RefreshTokenHours));
    }

    public static string HashRefreshToken(string refreshToken)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(refreshToken);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToHexString(hashBytes);
    }
}
