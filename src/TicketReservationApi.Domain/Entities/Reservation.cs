using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Domain.Entities;

public class Reservation
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public ReservationStatus Status { get; set; } = ReservationStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}
