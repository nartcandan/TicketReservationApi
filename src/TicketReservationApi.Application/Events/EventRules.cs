using TicketReservationApi.Application.Common.Exceptions;
using TicketReservationApi.Domain.Entities;
using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Application.Events;

/// <summary>Event rules that need no database access (PLAN.md, section 3).</summary>
public static class EventRules
{
    // K9
    public static void EnsureModifiable(Event ev, DateTimeOffset now)
    {
        if (ev.Status == EventStatus.Cancelled)
        {
            throw new BusinessRuleException("K9", "Cancelled events cannot be modified.");
        }

        if (ev.StartsAt <= now)
        {
            throw new BusinessRuleException("K9", "Events that have already started cannot be modified.");
        }
    }

    // K6
    public static void EnsureCapacityCoversReservations(Event ev, int newCapacity)
    {
        if (newCapacity < ev.ReservedCount)
        {
            throw new BusinessRuleException("K6",
                $"Capacity cannot be lower than the current reservation count ({ev.ReservedCount}).");
        }
    }

    // K8: expects the event's active reservations to be loaded
    public static void Cancel(Event ev, DateTimeOffset now)
    {
        ev.Status = EventStatus.Cancelled;

        foreach (var reservation in ev.Reservations.Where(r => r.Status == ReservationStatus.Active))
        {
            reservation.Status = ReservationStatus.Cancelled;
            reservation.CancelledAt = now;
        }

        ev.ReservedCount = 0;
    }
}
