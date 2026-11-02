using System.Net;
using System.Net.Http.Json;
using TicketReservationApi.Domain.Enums;
using TicketReservationApi.IntegrationTests.Infrastructure;

namespace TicketReservationApi.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class AuthTests(ApiFactory factory)
{
    private record TokenResponse(string Token, DateTimeOffset ExpiresAt);

    private record UserResponse(Guid Id, string Email, string FullName, string Role);

    private record IdResponse(Guid Id);

    [Fact]
    public async Task Register_then_login_returns_a_token_that_can_reserve()
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@Test.Local";

        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Passw0rd!", fullName = "New User" });

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var user = await register.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("User", user!.Role);
        Assert.Equal(email.ToLowerInvariant(), user.Email);

        // Email lookup is case-insensitive
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = email.ToUpperInvariant(), password = "Passw0rd!" });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();

        var eventId = await factory.SeedEventAsync(capacity: 5);
        var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new("Bearer", token!.Token);

        var reserve = await authed.PostAsJsonAsync("/api/reservations", new { eventId });

        Assert.Equal(HttpStatusCode.Created, reserve.StatusCode);
    }

    [Fact]
    public async Task Register_with_existing_email_returns_409()
    {
        var client = factory.CreateClient();
        var body = new { email = $"{Guid.NewGuid():N}@test.local", password = "Passw0rd!", fullName = "Dup" };

        await client.PostAsJsonAsync("/api/auth/register", body);
        var second = await client.PostAsJsonAsync("/api/auth/register", body);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData(null)]
    public async Task Login_with_wrong_password_or_unknown_email_returns_401(string? password)
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";

        if (password is not null)
        {
            await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd!", fullName = "X" });
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = password ?? "Passw0rd!" });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_to_protected_endpoints_return_401_problem_details()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/reservations/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Event_list_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_manage_events_and_admins_can()
    {
        var body = new { title = "T", venue = "V", startsAt = DateTimeOffset.UtcNow.AddDays(7), capacity = 10 };

        var asUser = await factory.CreateClientFor(await factory.SeedUserAsync())
            .PostAsJsonAsync("/api/events", body);
        var asAdmin = await factory.CreateClientFor(await factory.SeedUserAsync(UserRole.Admin))
            .PostAsJsonAsync("/api/events", body);

        Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);
        Assert.Equal(HttpStatusCode.Created, asAdmin.StatusCode);
    }

    [Fact]
    public async Task Created_reservation_location_points_to_the_reservation()
    {
        var eventId = await factory.SeedEventAsync(capacity: 5);
        var client = factory.CreateClientFor(await factory.SeedUserAsync());

        var created = await client.PostAsJsonAsync("/api/reservations", new { eventId });
        var createdBody = await created.Content.ReadFromJsonAsync<IdResponse>();

        Assert.NotNull(created.Headers.Location);
        var fetched = await client.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(createdBody!.Id, (await fetched.Content.ReadFromJsonAsync<IdResponse>())!.Id);
    }

    [Fact]
    public async Task Users_cannot_see_or_cancel_reservations_of_others()
    {
        var eventId = await factory.SeedEventAsync(capacity: 5);
        var owner = factory.CreateClientFor(await factory.SeedUserAsync());
        var other = factory.CreateClientFor(await factory.SeedUserAsync());

        var created = await owner.PostAsJsonAsync("/api/reservations", new { eventId });
        var reservation = await created.Content.ReadFromJsonAsync<IdResponse>();

        // K7
        var cancelByOther = await other.PatchAsync($"/api/reservations/{reservation!.Id}/cancel", null);
        var othersList = await other.GetFromJsonAsync<List<IdResponse>>("/api/reservations/me");

        Assert.Equal(HttpStatusCode.NotFound, cancelByOther.StatusCode);
        Assert.Empty(othersList!);
    }
}
