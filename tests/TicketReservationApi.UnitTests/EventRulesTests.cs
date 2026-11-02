using TicketReservationApi.Application.Common.Exceptions;
using TicketReservationApi.Application.Events;
using TicketReservationApi.Domain.Enums;
using static TicketReservationApi.UnitTests.TestData;

namespace TicketReservationApi.UnitTests;

public class EventRulesTests
{
    [Fact]
    public void Upcoming_active_event_is_modifiable()
    {
        EventRules.EnsureModifiable(Event(), Now);
    }

    [Fact]
    public void K9_cancelled_event_is_not_modifiable()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            EventRules.EnsureModifiable(Event(status: EventStatus.Cancelled), Now));

        Assert.Equal("K9", ex.Rule);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void K9_started_event_is_not_modifiable(int startsInMinutes)
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            EventRules.EnsureModifiable(Event(startsIn: TimeSpan.FromMinutes(startsInMinutes)), Now));

        Assert.Equal("K9", ex.Rule);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void K6_capacity_can_be_reduced_down_to_the_reservation_count(int newCapacity)
    {
        EventRules.EnsureCapacityCoversReservations(Event(capacity: 10, reservedCount: 5), newCapacity);
    }

    [Fact]
    public void K6_capacity_cannot_go_below_the_reservation_count()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            EventRules.EnsureCapacityCoversReservations(Event(capacity: 10, reservedCount: 5), 4));

        Assert.Equal("K6", ex.Rule);
    }

    [Fact]
    public void K8_cancelling_an_event_cancels_its_active_reservations()
    {
        var ev = Event(capacity: 10, reservedCount: 2);
        var active1 = Reservation(ev);
        var active2 = Reservation(ev);
        var alreadyCancelled = Reservation(ev, status: ReservationStatus.Cancelled);
        alreadyCancelled.CancelledAt = Now.AddDays(-2);

        ev.Reservations.Add(active1);
        ev.Reservations.Add(active2);
        ev.Reservations.Add(alreadyCancelled);

        EventRules.Cancel(ev, Now);

        Assert.Equal(EventStatus.Cancelled, ev.Status);
        Assert.Equal(0, ev.ReservedCount);
        Assert.All([active1, active2], r =>
        {
            Assert.Equal(ReservationStatus.Cancelled, r.Status);
            Assert.Equal(Now, r.CancelledAt);
        });

        // Earlier cancellations keep their original timestamp
        Assert.Equal(Now.AddDays(-2), alreadyCancelled.CancelledAt);
    }
}
