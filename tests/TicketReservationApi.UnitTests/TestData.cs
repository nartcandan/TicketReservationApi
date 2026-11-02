using TicketReservationApi.Domain.Entities;
using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public static Event Event(
        TimeSpan? startsIn = null,
        int capacity = 10,
        int reservedCount = 0,
        EventStatus status = EventStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Concert",
        Venue = "Hall",
        StartsAt = Now + (startsIn ?? TimeSpan.FromDays(7)),
        Capacity = capacity,
        ReservedCount = reservedCount,
        Status = status,
        CreatedAt = Now.AddDays(-30)
    };

    public static Reservation Reservation(
        Event ev,
        Guid? userId = null,
        ReservationStatus status = ReservationStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        EventId = ev.Id,
        Event = ev,
        UserId = userId ?? Guid.NewGuid(),
        Status = status,
        CreatedAt = Now.AddDays(-1)
    };
}
