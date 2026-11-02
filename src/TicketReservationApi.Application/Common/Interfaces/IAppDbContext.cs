using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Event> Events { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
