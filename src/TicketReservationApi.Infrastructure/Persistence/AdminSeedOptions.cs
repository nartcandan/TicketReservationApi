namespace TicketReservationApi.Infrastructure.Persistence;

/// <summary>Initial admin account. Seeding is skipped when Email or Password is missing.</summary>
public class AdminSeedOptions
{
    public const string SectionName = "Admin";

    public string? Email { get; set; }
    public string? Password { get; set; }
    public string FullName { get; set; } = "Administrator";
}
