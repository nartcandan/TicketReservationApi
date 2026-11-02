using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketReservationApi.Domain.Entities;
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

    public async Task<Guid> SeedEventAsync(int capacity)
    {
        await using var db = CreateDbContext();

        var ev = new Event
        {
            Id = Guid.CreateVersion7(),
            Title = "Concurrency test",
            Venue = "Test venue",
            StartsAt = DateTimeOffset.UtcNow.AddDays(30),
            Capacity = capacity,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Events.Add(ev);
        await db.SaveChangesAsync();

        return ev.Id;
    }

    public async Task<IReadOnlyList<Guid>> SeedUsersAsync(int count)
    {
        await using var db = CreateDbContext();

        var users = Enumerable.Range(0, count)
            .Select(_ => new User
            {
                Id = Guid.CreateVersion7(),
                Email = $"{Guid.NewGuid():N}@test.local",
                FullName = "Test User",
                CreatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        return users.Select(u => u.Id).ToList();
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public class ApiCollection : ICollectionFixture<ApiFactory>;
