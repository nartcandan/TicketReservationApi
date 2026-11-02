using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Application.Reservations;

public record CreateReservationRequest(Guid EventId);

public record ReservationResponse(
    Guid Id,
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStartsAt,
    Guid UserId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt)
{
    /// <remarks>Requires <see cref="Reservation.Event"/> to be loaded.</remarks>
    public static ReservationResponse From(Reservation r) => new(
        r.Id,
        r.EventId,
        r.Event.Title,
        r.Event.StartsAt,
        r.UserId,
        r.Status.ToString(),
        r.CreatedAt,
        r.CancelledAt);
}
