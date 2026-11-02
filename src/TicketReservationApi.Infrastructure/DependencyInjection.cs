using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketReservationApi.Application.Common.Interfaces;
using TicketReservationApi.Infrastructure.Persistence;

namespace TicketReservationApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<AppDbContext>(options => options
            .UseSqlServer(connectionString)
            // EXPERIMENT (PLAN.md, step 4): disabling the RowVersion concurrency token is a model-only change
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        return services;
    }
}
