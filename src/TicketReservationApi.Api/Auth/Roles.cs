using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Api.Auth;

/// <summary>Role names for <c>[Authorize(Roles = ...)]</c>; must match <see cref="UserRole"/>.</summary>
public static class Roles
{
    public const string Admin = nameof(UserRole.Admin);
    public const string User = nameof(UserRole.User);
}
