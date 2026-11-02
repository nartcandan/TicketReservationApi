namespace TicketReservationApi.Application.Auth;

public record RegisterRequest(string Email, string Password, string FullName);

public record LoginRequest(string Email, string Password);

public record UserResponse(Guid Id, string Email, string FullName, string Role);

public record AccessToken(string Token, DateTimeOffset ExpiresAt);
