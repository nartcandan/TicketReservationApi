using TicketReservationApi.Application.Common.Exceptions;
using TicketReservationApi.Domain.Entities;
using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Application.Reservations;

/// <summary>
/// Reservation rules that need no database access (PLAN.md, section 3). Under concurrency, K1 and K2
/// are guaranteed by the database; the checks here only give early, descriptive errors.
/// </summary>
public static class ReservationRules
{
    public static readonly TimeSpan CancellationCutoff = TimeSpan.FromHours(24);

    public static void EnsureReservable(Event ev, DateTimeOffset now)
    {
        // K3
        if (ev.Status == EventStatus.Cancelled)
        {
            throw new BusinessRuleException("K3", "Cannot reserve a cancelled event.");
        }

        if (ev.StartsAt <= now)
        {
            throw new BusinessRuleException("K3", "Cannot reserve an event that has already started.");
        }

        // K1
        if (ev.ReservedCount >= ev.Capacity)
        {
            throw new ConflictException("K1", "Event is fully booked.");
        }
    }

    /// <remarks>Requires <see cref="Reservation.Event"/> to be loaded.</remarks>
    public static void EnsureCancellable(Reservation reservation, Guid userId, DateTimeOffset now)
    {
        // K7: someone else's reservation is reported as not found
        if (reservation.UserId != userId)
        {
            throw new NotFoundException(nameof(Reservation), reservation.Id);
        }

        if (reservation.Status == ReservationStatus.Cancelled)
        {
            throw new BusinessRuleException(null, "Reservation is already cancelled.");
        }

        // K4
        if (reservation.Event.StartsAt - now < CancellationCutoff)
        {
            throw new BusinessRuleException("K4",
                "Reservations cannot be cancelled less than 24 hours before the event starts.");
        }
    }
}
