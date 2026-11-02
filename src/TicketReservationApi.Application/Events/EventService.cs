using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Application.Common.Exceptions;
using TicketReservationApi.Application.Common.Interfaces;
using TicketReservationApi.Domain.Entities;
using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Application.Events;

public interface IEventService
{
    Task<IReadOnlyList<EventResponse>> GetAllAsync(CancellationToken cancellationToken);
    Task<EventResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<EventResponse> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken);
    Task<EventResponse> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken);
    Task CancelAsync(Guid id, CancellationToken cancellationToken);
}

public class EventService(
    IAppDbContext db,
    TimeProvider timeProvider,
    IValidator<CreateEventRequest> createValidator,
    IValidator<UpdateEventRequest> updateValidator) : IEventService
{
    public async Task<IReadOnlyList<EventResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var events = await db.Events
            .AsNoTracking()
            .OrderBy(e => e.StartsAt)
            .ToListAsync(cancellationToken);

        return events.Select(EventResponse.From).ToList();
    }

    public async Task<EventResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Event), id);

        return EventResponse.From(ev);
    }

    public async Task<EventResponse> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var ev = new Event
        {
            Id = Guid.CreateVersion7(),
            Title = request.Title,
            Description = request.Description,
            Venue = request.Venue,
            StartsAt = request.StartsAt,
            Capacity = request.Capacity,
            CreatedAt = timeProvider.GetUtcNow()
        };

        db.Events.Add(ev);
        await db.SaveChangesAsync(cancellationToken);

        return EventResponse.From(ev);
    }

    public async Task<EventResponse> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var ev = await FindAsync(id, cancellationToken);

        EnsureModifiable(ev);

        // K6
        if (request.Capacity < ev.ReservedCount)
        {
            throw new BusinessRuleException("K6",
                $"Capacity cannot be lower than the current reservation count ({ev.ReservedCount}).");
        }

        ev.Title = request.Title;
        ev.Description = request.Description;
        ev.Venue = request.Venue;
        ev.StartsAt = request.StartsAt;
        ev.Capacity = request.Capacity;

        await db.SaveChangesAsync(cancellationToken);

        return EventResponse.From(ev);
    }

    public async Task CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var ev = await FindAsync(id, cancellationToken);

        EnsureModifiable(ev);

        // K8 (cancelling active reservations) is added together with Reservation in step 3
        ev.Status = EventStatus.Cancelled;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Event> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Events.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Event), id);

    // K9
    private void EnsureModifiable(Event ev)
    {
        if (ev.Status == EventStatus.Cancelled)
        {
            throw new BusinessRuleException("K9", "Cancelled events cannot be modified.");
        }

        if (ev.StartsAt <= timeProvider.GetUtcNow())
        {
            throw new BusinessRuleException("K9", "Events that have already started cannot be modified.");
        }
    }
}
