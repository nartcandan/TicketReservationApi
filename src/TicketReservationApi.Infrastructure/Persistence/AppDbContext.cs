using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Application.Common.Interfaces;
using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
