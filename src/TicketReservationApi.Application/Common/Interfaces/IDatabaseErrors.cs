using Microsoft.EntityFrameworkCore;

namespace TicketReservationApi.Application.Common.Interfaces;

/// <summary>Classifies provider-specific database errors without the Application layer depending on the provider.</summary>
public interface IDatabaseErrors
{
    bool IsUniqueViolation(DbUpdateException exception);
}
