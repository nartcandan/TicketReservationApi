using TicketReservationApi.Application.Common.Exceptions;
using TicketReservationApi.Application.Reservations;
using TicketReservationApi.Domain.Enums;
using static TicketReservationApi.UnitTests.TestData;

namespace TicketReservationApi.UnitTests;

public class ReservationRulesTests
{
    [Fact]
    public void Upcoming_event_with_free_seats_is_reservable()
    {
        ReservationRules.EnsureReservable(Event(capacity: 2, reservedCount: 1), Now);
    }

    [Fact]
    public void K1_full_event_is_not_reservable()
    {
        var ex = Assert.Throws<ConflictException>(() =>
            ReservationRules.EnsureReservable(Event(capacity: 2, reservedCount: 2), Now));

        Assert.Equal("K1", ex.Rule);
    }

    [Fact]
    public void K3_cancelled_event_is_not_reservable()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            ReservationRules.EnsureReservable(Event(status: EventStatus.Cancelled), Now));

        Assert.Equal("K3", ex.Rule);
    }

    [Theory]
    [InlineData(0)]   // starting right now
    [InlineData(-60)] // already started
    public void K3_started_event_is_not_reservable(int startsInMinutes)
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            ReservationRules.EnsureReservable(Event(startsIn: TimeSpan.FromMinutes(startsInMinutes)), Now));

        Assert.Equal("K3", ex.Rule);
    }

    [Fact]
    public void K3_takes_precedence_over_K1_for_a_cancelled_full_event()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            ReservationRules.EnsureReservable(
                Event(capacity: 1, reservedCount: 1, status: EventStatus.Cancelled), Now));

        Assert.Equal("K3", ex.Rule);
    }

    [Fact]
    public void Own_active_reservation_can_be_cancelled_24_hours_or_more_before_start()
    {
        var userId = Guid.NewGuid();
        var reservation = Reservation(Event(startsIn: TimeSpan.FromHours(24)), userId);

        ReservationRules.EnsureCancellable(reservation, userId, Now);
    }

    [Fact]
    public void K4_cannot_cancel_less_than_24_hours_before_start()
    {
        var userId = Guid.NewGuid();
        var reservation = Reservation(Event(startsIn: TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1)), userId);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            ReservationRules.EnsureCancellable(reservation, userId, Now));

        Assert.Equal("K4", ex.Rule);
    }

    [Fact]
    public void K7_someone_elses_reservation_is_reported_as_not_found()
    {
        var reservation = Reservation(Event());

        Assert.Throws<NotFoundException>(() =>
            ReservationRules.EnsureCancellable(reservation, userId: Guid.NewGuid(), Now));
    }

    [Fact]
    public void K7_is_checked_before_revealing_anything_about_the_reservation()
    {
        // A cancelled reservation close to the event would fail K4 or "already cancelled" for its owner,
        // but another user must only ever see 404.
        var reservation = Reservation(Event(startsIn: TimeSpan.FromHours(1)), status: ReservationStatus.Cancelled);

        Assert.Throws<NotFoundException>(() =>
            ReservationRules.EnsureCancellable(reservation, userId: Guid.NewGuid(), Now));
    }

    [Fact]
    public void Already_cancelled_reservation_cannot_be_cancelled_again()
    {
        var userId = Guid.NewGuid();
        var reservation = Reservation(Event(), userId, ReservationStatus.Cancelled);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            ReservationRules.EnsureCancellable(reservation, userId, Now));

        Assert.Null(ex.Rule);
    }
}
