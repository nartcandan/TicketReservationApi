using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public required string FullName { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Reservation> Reservations { get; } = [];
}
