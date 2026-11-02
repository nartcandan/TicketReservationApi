namespace TicketReservationApi.Infrastructure.Auth;

/// <summary>Claim names used in access tokens; also used to configure token validation.</summary>
public static class JwtClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
}
