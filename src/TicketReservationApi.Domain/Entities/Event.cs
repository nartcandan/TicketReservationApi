using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Domain.Entities;

public class Event
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Venue { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public int Capacity { get; set; }
    public int ReservedCount { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Active;

    // SQL Server rowversion; optimistic concurrency token
    public byte[] RowVersion { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}
