using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TicketReservationApi.Application.Auth;
using TicketReservationApi.Application.Common.Interfaces;
using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Infrastructure.Auth;

public class AccessTokenGenerator(IOptions<JwtOptions> options, TimeProvider timeProvider) : IAccessTokenGenerator
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Generate(User user)
    {
        var jwt = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(jwt.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtClaims.Subject, user.Id.ToString()),
                new Claim(JwtClaims.Email, user.Email),
                new Claim(JwtClaims.Name, user.FullName),
                new Claim(JwtClaims.Role, user.Role.ToString())
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
