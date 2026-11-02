using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TicketReservationApi.Application.Auth;
using TicketReservationApi.Application.Events;
using TicketReservationApi.Application.Reservations;
using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IReservationService, ReservationService>();

        return services;
    }
}
