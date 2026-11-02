using Microsoft.Extensions.Time.Testing;
using TicketReservationApi.Application.Auth;
using TicketReservationApi.Application.Events;
using static TicketReservationApi.UnitTests.TestData;

namespace TicketReservationApi.UnitTests;

public class ValidatorTests
{
    private readonly FakeTimeProvider _time = new(Now);

    private static CreateEventRequest ValidEvent() =>
        new("Concert", null, "Hall", Now.AddDays(7), Capacity: 100);

    [Fact]
    public void Valid_event_request_passes()
    {
        Assert.True(new CreateEventRequestValidator(_time).Validate(ValidEvent()).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void K9_event_start_must_be_in_the_future(int startsInMinutes)
    {
        var request = ValidEvent() with { StartsAt = Now.AddMinutes(startsInMinutes) };

        var result = new UpdateEventRequestValidator(_time).Validate(request.ToUpdate());

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateEventRequest.StartsAt));
    }

    [Fact]
    public void Event_start_is_checked_against_the_current_time_not_a_fixed_one()
    {
        var request = ValidEvent() with { StartsAt = Now.AddHours(1) };
        var validator = new CreateEventRequestValidator(_time);

        Assert.True(validator.Validate(request).IsValid);

        _time.Advance(TimeSpan.FromHours(2));

        Assert.False(validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Event_capacity_must_be_positive(int capacity)
    {
        var result = new CreateEventRequestValidator(_time).Validate(ValidEvent() with { Capacity = capacity });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateEventRequest.Capacity));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 100, true)]
    [InlineData(0, 20, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 101, false)]
    public void Event_list_paging_bounds(int page, int pageSize, bool isValid)
    {
        var result = new EventListQueryValidator().Validate(new EventListQuery { Page = page, PageSize = pageSize });

        Assert.Equal(isValid, result.IsValid);
    }

    [Fact]
    public void Event_list_to_cannot_be_before_from()
    {
        var query = new EventListQuery { From = Now, To = Now.AddDays(-1) };

        Assert.False(new EventListQueryValidator().Validate(query).IsValid);
    }

    [Theory]
    [InlineData("user@test.local", "Passw0rd!", true)]
    [InlineData("not-an-email", "Passw0rd!", false)]
    [InlineData("user@test.local", "short", false)]
    public void Register_requires_valid_email_and_password_of_8_characters(string email, string password, bool isValid)
    {
        var result = new RegisterRequestValidator().Validate(new RegisterRequest(email, password, "Name"));

        Assert.Equal(isValid, result.IsValid);
    }
}

file static class EventRequestExtensions
{
    public static UpdateEventRequest ToUpdate(this CreateEventRequest r) =>
        new(r.Title, r.Description, r.Venue, r.StartsAt, r.Capacity);
}
