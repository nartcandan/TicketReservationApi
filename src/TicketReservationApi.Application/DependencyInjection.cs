using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TicketReservationApi.Application.Events;

namespace TicketReservationApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IEventService, EventService>();

        return services;
    }
}
