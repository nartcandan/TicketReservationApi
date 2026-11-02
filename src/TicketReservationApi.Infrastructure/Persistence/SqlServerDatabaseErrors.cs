using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Application.Common.Interfaces;

namespace TicketReservationApi.Infrastructure.Persistence;

public class SqlServerDatabaseErrors : IDatabaseErrors
{
    // Duplicate key in a unique index / unique constraint. Error numbers, unlike messages, are not localized.
    private static readonly int[] UniqueViolationErrorNumbers = [2601, 2627];

    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && UniqueViolationErrorNumbers.Contains(sql.Number);
}
