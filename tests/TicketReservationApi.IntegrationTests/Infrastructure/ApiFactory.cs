using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketReservationApi.Application.Common.Interfaces;
using TicketReservationApi.Domain.Entities;
using TicketReservationApi.Domain.Enums;
using TicketReservationApi.Infrastructure.Persistence;

namespace TicketReservationApi.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the API in memory against a real SQL Server database (recreated once per test run).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnectionString =
        "Server=.;Database=TicketReservation_Tests;Trusted_Connection=True;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-not-a-secret-0123456789");
    }

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public AppDbContext CreateDbContext() =>
        Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    /// <summary>Client authenticated as <paramref name="user"/> with a real access token.</summary>
    public HttpClient CreateClientFor(User user)
    {
        var token = Services.GetRequiredService<IAccessTokenGenerator>().Generate(user);

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        return client;
    }

    public async Task<Guid> SeedEventAsync(int capacity, DateTimeOffset? startsAt = null)
    {
        await using var db = CreateDbContext();

        var ev = new Event
        {
            Id = Guid.CreateVersion7(),
            Title = "Test event",
            Venue = "Test venue",
            StartsAt = startsAt ?? DateTimeOffset.UtcNow.AddDays(30),
            Capacity = capacity,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Events.Add(ev);
        await db.SaveChangesAsync();

        return ev.Id;
    }

    public async Task<IReadOnlyList<User>> SeedUsersAsync(int count, UserRole role = UserRole.User)
    {
        await using var db = CreateDbContext();

        var users = Enumerable.Range(0, count)
            .Select(_ => new User
            {
                Id = Guid.CreateVersion7(),
                Email = $"{Guid.NewGuid():N}@test.local",
                FullName = "Test User",
                Role = role,
                CreatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        return users;
    }

    public async Task<User> SeedUserAsync(UserRole role = UserRole.User) => (await SeedUsersAsync(1, role))[0];
}

[CollectionDefinition(nameof(ApiCollection))]
public class ApiCollection : ICollectionFixture<ApiFactory>;
