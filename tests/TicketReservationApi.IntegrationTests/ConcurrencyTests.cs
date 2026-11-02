using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
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
        var clients = (await factory.SeedUsersAsync(ParallelRequests)).Select(factory.CreateClientFor);

        var statusCodes = await ReserveInParallelAsync(eventId, clients);

        Assert.Equal(
            "201 x1, 409 x9 | ReservedCount=1, ActiveReservations=1",
            await SummarizeAsync(eventId, statusCodes));
    }

    [Fact]
    public async Task Parallel_requests_by_the_same_user_create_one_reservation()
    {
        var eventId = await factory.SeedEventAsync(capacity: 100);
        var client = factory.CreateClientFor(await factory.SeedUserAsync());

        var statusCodes = await ReserveInParallelAsync(eventId, Enumerable.Repeat(client, ParallelRequests));

        Assert.Equal(
            "201 x1, 409 x9 | ReservedCount=1, ActiveReservations=1",
            await SummarizeAsync(eventId, statusCodes));
    }

    [Fact]
    public async Task Parallel_duplicate_reservations_all_report_K2()
    {
        var eventId = await factory.SeedEventAsync(capacity: 100);
        var client = factory.CreateClientFor(await factory.SeedUserAsync());

        var responses = await Task.WhenAll(Enumerable.Range(0, ParallelRequests).Select(_ =>
            client.PostAsJsonAsync("/api/reservations", new { eventId })));

        // Whether the duplicate is caught by the early check or by the unique index, the response is the same
        Assert.Equal(
            Enumerable.Repeat("K2", ParallelRequests - 1),
            await RulesOfConflictsAsync(responses));
    }

    [Fact]
    public async Task Parallel_registrations_with_the_same_email_create_one_user()
    {
        var client = factory.CreateClient();
        var body = new { email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!", fullName = "Racer" };

        var responses = await Task.WhenAll(Enumerable.Range(0, ParallelRequests).Select(_ =>
            client.PostAsJsonAsync("/api/auth/register", body)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(
            Enumerable.Repeat("EmailTaken", ParallelRequests - 1),
            await RulesOfConflictsAsync(responses));
    }

    [Fact]
    public async Task Parallel_requests_with_enough_seats_all_succeed()
    {
        var eventId = await factory.SeedEventAsync(capacity: 100);
        var clients = (await factory.SeedUsersAsync(ParallelRequests)).Select(factory.CreateClientFor);

        var statusCodes = await ReserveInParallelAsync(eventId, clients);

        Assert.Equal(
            "201 x10 | ReservedCount=10, ActiveReservations=10",
            await SummarizeAsync(eventId, statusCodes));
    }

    [Fact]
    public async Task Parallel_cancellations_release_the_seat_once()
    {
        var eventId = await factory.SeedEventAsync(capacity: 1);
        var client = factory.CreateClientFor(await factory.SeedUserAsync());

        var created = await client.PostAsJsonAsync("/api/reservations", new { eventId });
        var reservation = await created.Content.ReadFromJsonAsync<IdResponse>();

        var responses = await Task.WhenAll(Enumerable.Range(0, ParallelRequests).Select(_ =>
            client.PatchAsync($"/api/reservations/{reservation!.Id}/cancel", null)));

        Assert.Equal(
            "200 x1, 422 x9 | ReservedCount=0, ActiveReservations=0",
            await SummarizeAsync(eventId, responses.Select(r => r.StatusCode).ToArray()));
    }

    private record IdResponse(Guid Id);

    /// <summary>The <c>rule</c> of every 409 response ("&lt;none&gt;" when missing).</summary>
    private static async Task<List<string>> RulesOfConflictsAsync(IEnumerable<HttpResponseMessage> responses)
    {
        var rules = new List<string>();

        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            rules.Add(problem!.Extensions.TryGetValue("rule", out var rule) ? rule!.ToString()! : "<none>");
        }

        return rules;
    }

    /// <param name="clients">One authenticated client per request, created before the race starts.</param>
    private static async Task<HttpStatusCode[]> ReserveInParallelAsync(Guid eventId, IEnumerable<HttpClient> clients)
    {
        var ready = clients.ToList();

        var responses = await Task.WhenAll(ready.Select(client =>
            client.PostAsJsonAsync("/api/reservations", new { eventId })));

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
