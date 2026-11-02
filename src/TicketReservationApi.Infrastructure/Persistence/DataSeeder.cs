using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketReservationApi.Application.Auth;
using TicketReservationApi.Domain.Entities;
using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Infrastructure.Persistence;

public class DataSeeder(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider,
    IOptions<AdminSeedOptions> adminOptions)
{
    /// <summary>Password of the development test users. Development only.</summary>
    public const string DevUserPassword = "Passw0rd!";

    public static readonly IReadOnlyList<(Guid Id, string Email, string FullName)> DevUsers =
    [
        (Guid.Parse("11111111-1111-1111-1111-111111111111"), "ali@test.local", "Ali Test"),
        (Guid.Parse("22222222-2222-2222-2222-222222222222"), "ayse@test.local", "Ayşe Test"),
        (Guid.Parse("33333333-3333-3333-3333-333333333333"), "can@test.local", "Can Test")
    ];

    public async Task SeedAdminAsync(CancellationToken cancellationToken = default)
    {
        var options = adminOptions.Value;

        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
        {
            return;
        }

        var email = AuthService.NormalizeEmail(options.Email);

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return;
        }

        var admin = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            FullName = options.FullName,
            Role = UserRole.Admin,
            CreatedAt = timeProvider.GetUtcNow()
        };

        admin.PasswordHash = passwordHasher.HashPassword(admin, options.Password);

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SeedDevUsersAsync(CancellationToken cancellationToken = default)
    {
        var ids = DevUsers.Select(u => u.Id).ToList();
        var existing = await db.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        foreach (var (id, email, fullName) in DevUsers)
        {
            if (!existing.TryGetValue(id, out var user))
            {
                user = new User
                {
                    Id = id,
                    Email = email,
                    FullName = fullName,
                    CreatedAt = timeProvider.GetUtcNow()
                };

                db.Users.Add(user);
            }

            // Users seeded before auth existed have no password yet
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                user.PasswordHash = passwordHasher.HashPassword(user, DevUserPassword);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
