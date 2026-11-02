using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Application.Events;

public interface IEventRequest
{
    string Title { get; }
    string? Description { get; }
    string Venue { get; }
    DateTimeOffset StartsAt { get; }
    int Capacity { get; }
}

public record CreateEventRequest(
    string Title,
    string? Description,
    string Venue,
    DateTimeOffset StartsAt,
    int Capacity) : IEventRequest;

public record UpdateEventRequest(
    string Title,
    string? Description,
    string Venue,
    DateTimeOffset StartsAt,
    int Capacity) : IEventRequest;

public record EventResponse(
    Guid Id,
    string Title,
    string? Description,
    string Venue,
    DateTimeOffset StartsAt,
    int Capacity,
    int ReservedCount,
    int AvailableSeats,
    string Status,
    DateTimeOffset CreatedAt)
{
    public static EventResponse From(Event e) => new(
        e.Id,
        e.Title,
        e.Description,
        e.Venue,
        e.StartsAt,
        e.Capacity,
        e.ReservedCount,
        e.Capacity - e.ReservedCount,
        e.Status.ToString(),
        e.CreatedAt);
}
