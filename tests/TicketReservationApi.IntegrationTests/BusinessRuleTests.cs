using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Domain.Enums;
using TicketReservationApi.IntegrationTests.Infrastructure;

namespace TicketReservationApi.IntegrationTests;

/// <summary>
/// End-to-end checks that each rule from PLAN.md, section 3 is enforced through the API with the
/// agreed status code and rule id. The rule logic itself is unit tested in TicketReservationApi.UnitTests.
/// </summary>
[Collection(nameof(ApiCollection))]
public class BusinessRuleTests(ApiFactory factory)
{
    private record IdResponse(Guid Id);

    [Fact]
    public async Task K1_reserving_a_full_event_returns_409()
    {
        var eventId = await factory.SeedEventAsync(capacity: 1, reservedCount: 1);
        var user = await UserClientAsync();

        var response = await user.PostAsJsonAsync("/api/reservations", new { eventId });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "K1");
    }

    [Fact]
    public async Task K2_reserving_the_same_event_twice_returns_409()
    {
        var eventId = await factory.SeedEventAsync(capacity: 10);
        var user = await UserClientAsync();

        await user.PostAsJsonAsync("/api/reservations", new { eventId });
        var second = await user.PostAsJsonAsync("/api/reservations", new { eventId });

        await AssertProblemAsync(second, HttpStatusCode.Conflict, "K2");
    }

    [Fact]
    public async Task K2_allows_reserving_again_after_cancelling()
    {
        var eventId = await factory.SeedEventAsync(capacity: 10);
        var user = await UserClientAsync();

        var first = await ReserveAsync(user, eventId);
        await user.PatchAsync($"/api/reservations/{first}/cancel", null);
        var again = await user.PostAsJsonAsync("/api/reservations", new { eventId });

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task K3_reserving_a_cancelled_event_returns_422()
    {
        var eventId = await factory.SeedEventAsync(capacity: 10, status: EventStatus.Cancelled);

        var response = await (await UserClientAsync()).PostAsJsonAsync("/api/reservations", new { eventId });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "K3");
    }

    [Fact]
    public async Task K3_reserving_a_past_event_returns_422()
    {
        var eventId = await factory.SeedEventAsync(capacity: 10, startsAt: DateTimeOffset.UtcNow.AddHours(-1));

        var response = await (await UserClientAsync()).PostAsJsonAsync("/api/reservations", new { eventId });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "K3");
    }

    [Fact]
    public async Task K4_cancelling_within_24_hours_of_the_event_returns_422()
    {
        var eventId = await factory.SeedEventAsync(capacity: 10, startsAt: DateTimeOffset.UtcNow.AddHours(2));
        var user = await UserClientAsync();
        var reservationId = await ReserveAsync(user, eventId);

        var response = await user.PatchAsync($"/api/reservations/{reservationId}/cancel", null);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "K4");
    }

    [Fact]
    public async Task K5_cancelling_frees_the_seat_for_someone_else()
    {
        var eventId = await factory.SeedEventAsync(capacity: 1);
        var first = await UserClientAsync();
        var second = await UserClientAsync();
        var reservationId = await ReserveAsync(first, eventId);

        var cancel = await first.PatchAsync($"/api/reservations/{reservationId}/cancel", null);
        var reserve = await second.PostAsJsonAsync("/api/reservations", new { eventId });

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reserve.StatusCode);
    }

    [Fact]
    public async Task K6_reducing_capacity_below_reservations_returns_422()
    {
        var eventId = await factory.SeedEventAsync(capacity: 10, reservedCount: 5);

        var response = await (await AdminClientAsync()).PutAsJsonAsync($"/api/events/{eventId}", UpdateBody(capacity: 4));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "K6");
    }

    [Fact]
    public async Task K7_cancelling_someone_elses_reservation_returns_404()
    {
        var eventId = await factory.SeedEventAsync(capacity: 10);
        var reservationId = await ReserveAsync(await UserClientAsync(), eventId);

        var response = await (await UserClientAsync()).PatchAsync($"/api/reservations/{reservationId}/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task K8_cancelling_an_event_cancels_its_reservations()
    {
        var eventId = await factory.SeedEventAsync(capacity: 10);
        await ReserveAsync(await UserClientAsync(), eventId);
        await ReserveAsync(await UserClientAsync(), eventId);

        var response = await (await AdminClientAsync()).PatchAsync($"/api/events/{eventId}/cancel", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = factory.CreateDbContext();
        var ev = await db.Events.Include(e => e.Reservations).SingleAsync(e => e.Id == eventId);

        Assert.Equal(EventStatus.Cancelled, ev.Status);
        Assert.Equal(0, ev.ReservedCount);
        Assert.Equal(2, ev.Reservations.Count);
        Assert.All(ev.Reservations, r => Assert.Equal(ReservationStatus.Cancelled, r.Status));
    }

    [Fact]
    public async Task K9_updating_a_cancelled_event_returns_422()
    {
        var eventId = await factory.SeedEventAsync(capacity: 10, status: EventStatus.Cancelled);

        var response = await (await AdminClientAsync()).PutAsJsonAsync($"/api/events/{eventId}", UpdateBody());

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "K9");
    }

    [Fact]
    public async Task K9_moving_an_event_into_the_past_returns_400()
    {
        var eventId = await factory.SeedEventAsync(capacity: 10);

        var response = await (await AdminClientAsync())
            .PutAsJsonAsync($"/api/events/{eventId}", UpdateBody(startsAt: DateTimeOffset.UtcNow.AddDays(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static object UpdateBody(int capacity = 10, DateTimeOffset? startsAt = null) => new
    {
        title = "Updated",
        venue = "Hall",
        startsAt = startsAt ?? DateTimeOffset.UtcNow.AddDays(30),
        capacity
    };

    private async Task<HttpClient> UserClientAsync() =>
        factory.CreateClientFor(await factory.SeedUserAsync());

    private async Task<HttpClient> AdminClientAsync() =>
        factory.CreateClientFor(await factory.SeedUserAsync(UserRole.Admin));

    private static async Task<Guid> ReserveAsync(HttpClient client, Guid eventId)
    {
        var response = await client.PostAsJsonAsync("/api/reservations", new { eventId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string rule)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(rule, problem!.Extensions["rule"]?.ToString());
    }
}
