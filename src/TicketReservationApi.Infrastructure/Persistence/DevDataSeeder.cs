using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Infrastructure.Persistence;

/// <summary>
/// Seeds fixed test users for development, until register/login exists (PLAN.md, step 5).
/// </summary>
public static class DevDataSeeder
{
    public static readonly IReadOnlyList<(Guid Id, string Email, string FullName)> TestUsers =
    [
        (Guid.Parse("11111111-1111-1111-1111-111111111111"), "ali@test.local", "Ali Test"),
        (Guid.Parse("22222222-2222-2222-2222-222222222222"), "ayse@test.local", "Ayşe Test"),
        (Guid.Parse("33333333-3333-3333-3333-333333333333"), "can@test.local", "Can Test")
    ];

    public static async Task SeedAsync(AppDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        var existingIds = await db.Users
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var (id, email, fullName) in TestUsers.Where(u => !existingIds.Contains(u.Id)))
        {
            db.Users.Add(new User
            {
                Id = id,
                Email = email,
                FullName = fullName,
                CreatedAt = timeProvider.GetUtcNow()
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
