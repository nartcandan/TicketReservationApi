using System.Net;
using System.Net.Http.Json;
using TicketReservationApi.Domain.Enums;
using TicketReservationApi.IntegrationTests.Infrastructure;

namespace TicketReservationApi.IntegrationTests;

/// <summary>
/// The test database is shared, so each test tags its events with a unique token in the title
/// and narrows every query with <c>search=&lt;tag&gt;</c>.
/// </summary>
[Collection(nameof(ApiCollection))]
public class EventListTests(ApiFactory factory)
{
    private record EventItem(Guid Id, string Title, DateTimeOffset StartsAt);

    private record PagedEvents(List<EventItem> Items, int Page, int PageSize, int TotalCount);

    private readonly string _tag = Guid.NewGuid().ToString("N")[..12];
    private readonly DateTimeOffset _baseTime = DateTimeOffset.UtcNow.Date.AddDays(10);

    [Fact]
    public async Task Pages_are_ordered_by_start_time()
    {
        for (var day = 4; day >= 0; day--)
        {
            await factory.SeedEventAsync(capacity: 10, startsAt: _baseTime.AddDays(day), title: $"{_tag} day {day}");
        }

        var page = await ListAsync($"page=2&pageSize=2");

        Assert.Equal(5, page.TotalCount);
        Assert.Equal((2, 2), (page.Page, page.PageSize));
        Assert.Equal([$"{_tag} day 2", $"{_tag} day 3"], page.Items.Select(e => e.Title));
    }

    [Fact]
    public async Task From_and_to_are_inclusive()
    {
        for (var day = 0; day < 5; day++)
        {
            await factory.SeedEventAsync(capacity: 10, startsAt: _baseTime.AddDays(day), title: $"{_tag} day {day}");
        }

        var from = Uri.EscapeDataString(_baseTime.AddDays(1).ToString("O"));
        var to = Uri.EscapeDataString(_baseTime.AddDays(3).ToString("O"));

        var page = await ListAsync($"from={from}&to={to}");

        Assert.Equal([$"{_tag} day 1", $"{_tag} day 2", $"{_tag} day 3"], page.Items.Select(e => e.Title));
    }

    [Fact]
    public async Task Search_matches_title_or_venue_case_insensitively()
    {
        await factory.SeedEventAsync(capacity: 10, title: $"{_tag} jazz night");
        await factory.SeedEventAsync(capacity: 10, title: "Unrelated", venue: $"{_tag} HALL");
        await factory.SeedEventAsync(capacity: 10, title: "Unrelated", venue: "Elsewhere");

        var page = await ListAsync("", search: _tag.ToUpperInvariant());

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_as_literals()
    {
        await factory.SeedEventAsync(capacity: 10, title: $"{_tag} 100% fun");
        await factory.SeedEventAsync(capacity: 10, title: $"{_tag} 1000 fun");

        var page = await ListAsync("", search: $"{_tag} 100%");

        Assert.Equal([$"{_tag} 100% fun"], page.Items.Select(e => e.Title));
    }

    [Fact]
    public async Task Past_events_are_hidden_unless_from_is_given()
    {
        await factory.SeedEventAsync(capacity: 10, startsAt: DateTimeOffset.UtcNow.AddDays(-2), title: $"{_tag} past");
        await factory.SeedEventAsync(capacity: 10, startsAt: DateTimeOffset.UtcNow.AddDays(2), title: $"{_tag} upcoming");

        var byDefault = await ListAsync("");
        var withFrom = await ListAsync($"from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"))}");

        Assert.Equal([$"{_tag} upcoming"], byDefault.Items.Select(e => e.Title));
        Assert.Equal([$"{_tag} past", $"{_tag} upcoming"], withFrom.Items.Select(e => e.Title));
    }

    [Fact]
    public async Task Only_available_excludes_full_and_cancelled_events()
    {
        await factory.SeedEventAsync(capacity: 10, title: $"{_tag} available");
        await factory.SeedEventAsync(capacity: 2, reservedCount: 2, title: $"{_tag} full");
        await factory.SeedEventAsync(capacity: 10, status: EventStatus.Cancelled, title: $"{_tag} cancelled");

        var all = await ListAsync("");
        var available = await ListAsync("onlyAvailable=true");

        // Full and cancelled upcoming events are still listed by default, with their status and seats
        Assert.Equal(3, all.TotalCount);
        Assert.Equal([$"{_tag} available"], available.Items.Select(e => e.Title));
    }

    [Fact]
    public async Task Only_available_excludes_past_events_even_when_from_is_in_the_past()
    {
        await factory.SeedEventAsync(capacity: 10, startsAt: DateTimeOffset.UtcNow.AddDays(-1), title: $"{_tag} past");

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"));
        var available = await ListAsync($"from={from}&onlyAvailable=true");

        Assert.Empty(available.Items);
    }

    [Fact]
    public async Task Defaults_to_first_page_of_20()
    {
        var page = await ListAsync("");

        Assert.Equal((1, 20), (page.Page, page.PageSize));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("from=2026-12-10T00:00:00Z&to=2026-12-01T00:00:00Z")]
    public async Task Invalid_parameters_return_400(string queryString)
    {
        var response = await factory.CreateClient().GetAsync($"/api/events?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<PagedEvents> ListAsync(string queryString, string? search = null)
    {
        var url = $"/api/events?search={Uri.EscapeDataString(search ?? _tag)}&{queryString}";
        var page = await factory.CreateClient().GetFromJsonAsync<PagedEvents>(url);
        return page!;
    }
}
