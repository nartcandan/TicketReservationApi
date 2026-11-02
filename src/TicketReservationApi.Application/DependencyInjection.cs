using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TicketReservationApi.Application.Events;
using TicketReservationApi.Application.Reservations;

namespace TicketReservationApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IReservationService, ReservationService>();

        return services;
    }
}
