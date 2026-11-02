using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Domain.Enums;
using TicketReservationApi.IntegrationTests.Infrastructure;

namespace TicketReservationApi.IntegrationTests;

/// <summary>
/// Concurrency experiment from PLAN.md, section 7. Each test fires parallel reservation
/// requests and compares the observed outcome with the expected one as a single summary,
/// so a failure shows exactly what happened (e.g. overbooking or lost updates).
/// </summary>
[Collection(nameof(ApiCollection))]
public class ConcurrencyTests(ApiFactory factory)
{
    private const int ParallelRequests = 10;

    [Fact]
    public async Task Parallel_requests_for_a_single_seat_book_exactly_one()
    {
        var eventId = await factory.SeedEventAsync(capacity: 1);
        var userIds = await factory.SeedUsersAsync(ParallelRequests);

        var statusCodes = await SendInParallelAsync(userIds.Select(userId => (eventId, userId)));

        Assert.Equal(
            "201 x1, 409 x9 | ReservedCount=1, ActiveReservations=1",
            await SummarizeAsync(eventId, statusCodes));
    }

    [Fact]
    public async Task Parallel_requests_by_the_same_user_create_one_reservation()
    {
        var eventId = await factory.SeedEventAsync(capacity: 100);
        var userId = (await factory.SeedUsersAsync(1))[0];

        var statusCodes = await SendInParallelAsync(Enumerable.Repeat((eventId, userId), ParallelRequests));

        Assert.Equal(
            "201 x1, 409 x9 | ReservedCount=1, ActiveReservations=1",
            await SummarizeAsync(eventId, statusCodes));
    }

    [Fact]
    public async Task Parallel_requests_with_enough_seats_all_succeed()
    {
        var eventId = await factory.SeedEventAsync(capacity: 100);
        var userIds = await factory.SeedUsersAsync(ParallelRequests);

        var statusCodes = await SendInParallelAsync(userIds.Select(userId => (eventId, userId)));

        Assert.Equal(
            "201 x10 | ReservedCount=10, ActiveReservations=10",
            await SummarizeAsync(eventId, statusCodes));
    }

    [Fact]
    public async Task Parallel_cancellations_release_the_seat_once()
    {
        var eventId = await factory.SeedEventAsync(capacity: 1);
        var userId = (await factory.SeedUsersAsync(1))[0];
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/api/reservations", new { eventId, userId });
        var reservation = await created.Content.ReadFromJsonAsync<IdResponse>();

        var responses = await Task.WhenAll(Enumerable.Range(0, ParallelRequests).Select(_ =>
            client.PatchAsJsonAsync($"/api/reservations/{reservation!.Id}/cancel", new { userId })));

        Assert.Equal(
            "200 x1, 422 x9 | ReservedCount=0, ActiveReservations=0",
            await SummarizeAsync(eventId, responses.Select(r => r.StatusCode).ToArray()));
    }

    private record IdResponse(Guid Id);

    private async Task<HttpStatusCode[]> SendInParallelAsync(IEnumerable<(Guid EventId, Guid UserId)> requests)
    {
        var client = factory.CreateClient();

        var responses = await Task.WhenAll(requests.Select(r =>
            client.PostAsJsonAsync("/api/reservations", new { r.EventId, r.UserId })));

        return responses.Select(r => r.StatusCode).ToArray();
    }

    private async Task<string> SummarizeAsync(Guid eventId, HttpStatusCode[] statusCodes)
    {
        await using var db = factory.CreateDbContext();

        var reservedCount = await db.Events
            .Where(e => e.Id == eventId)
            .Select(e => e.ReservedCount)
            .SingleAsync();

        var activeReservations = await db.Reservations
            .CountAsync(r => r.EventId == eventId && r.Status == ReservationStatus.Active);

        var codes = string.Join(", ", statusCodes
            .GroupBy(c => (int)c)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key} x{g.Count()}"));

        return $"{codes} | ReservedCount={reservedCount}, ActiveReservations={activeReservations}";
    }
}
