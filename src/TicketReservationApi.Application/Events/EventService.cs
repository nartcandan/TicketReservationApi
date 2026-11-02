using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Application.Common;
using TicketReservationApi.Application.Common.Exceptions;
using TicketReservationApi.Application.Common.Interfaces;
using TicketReservationApi.Domain.Entities;
using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Application.Events;

public interface IEventService
{
    Task<PagedResponse<EventResponse>> ListAsync(EventListQuery query, CancellationToken cancellationToken);
    Task<EventResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<EventResponse> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken);
    Task<EventResponse> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken);
    Task CancelAsync(Guid id, CancellationToken cancellationToken);
}

public class EventService(
    IAppDbContext db,
    TimeProvider timeProvider,
    IValidator<CreateEventRequest> createValidator,
    IValidator<UpdateEventRequest> updateValidator,
    IValidator<EventListQuery> listValidator) : IEventService
{
    public async Task<PagedResponse<EventResponse>> ListAsync(EventListQuery query, CancellationToken cancellationToken)
    {
        await listValidator.ValidateAndThrowAsync(query, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var from = query.From ?? now;

        var events = db.Events
            .AsNoTracking()
            .Where(e => e.StartsAt >= from);

        if (query.To is { } to)
        {
            events = events.Where(e => e.StartsAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Translated to LIKE; case-insensitive under the database's default collation
            var search = query.Search.Trim();
            events = events.Where(e => e.Title.Contains(search) || e.Venue.Contains(search));
        }

        if (query.OnlyAvailable)
        {
            events = events.Where(e =>
                e.Status == EventStatus.Active && e.StartsAt > now && e.ReservedCount < e.Capacity);
        }

        var totalCount = await events.CountAsync(cancellationToken);

        var page = await events
            .OrderBy(e => e.StartsAt)
            .ThenBy(e => e.Id) // stable order across pages when StartsAt ties
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<EventResponse>(
            page.Select(EventResponse.From).ToList(),
            query.Page,
            query.PageSize,
            totalCount);
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

        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Event), id);

        EventRules.EnsureModifiable(ev, timeProvider.GetUtcNow());
        EventRules.EnsureCapacityCoversReservations(ev, request.Capacity);

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
        var ev = await db.Events
            .Include(e => e.Reservations.Where(r => r.Status == ReservationStatus.Active))
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Event), id);

        var now = timeProvider.GetUtcNow();

        EventRules.EnsureModifiable(ev, now);
        EventRules.Cancel(ev, now);

        // RowVersion check: a reservation made since loading the event causes a 409 instead of a lost update
        await db.SaveChangesAsync(cancellationToken);
    }
}
