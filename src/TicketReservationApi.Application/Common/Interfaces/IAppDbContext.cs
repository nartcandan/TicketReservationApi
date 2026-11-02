using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Event> Events { get; }
    DbSet<User> Users { get; }
    DbSet<Reservation> Reservations { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
