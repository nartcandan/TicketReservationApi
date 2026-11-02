using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Application.Common.Exceptions;
using TicketReservationApi.Application.Common.Interfaces;
using TicketReservationApi.Domain.Entities;
using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Application.Reservations;

public interface IReservationService
{
    Task<ReservationResponse> CreateAsync(Guid userId, CreateReservationRequest request, CancellationToken cancellationToken);
    Task<ReservationResponse> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReservationResponse>> GetForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReservationResponse>> GetForEventAsync(Guid eventId, CancellationToken cancellationToken);
}

public class ReservationService(
    IAppDbContext db,
    TimeProvider timeProvider,
    IValidator<CreateReservationRequest> createValidator) : IReservationService
{
    private static readonly TimeSpan CancellationCutoff = TimeSpan.FromHours(24);

    // The checks below give early, descriptive errors. Under concurrency the real guarantees are
    // the atomic conditional UPDATE (K1) and the filtered unique index (K2). See PLAN.md, section 7.
    public async Task<ReservationResponse> CreateAsync(Guid userId, CreateReservationRequest request, CancellationToken cancellationToken)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            ?? throw new NotFoundException(nameof(Event), request.EventId);

        // K3
        if (ev.Status == EventStatus.Cancelled)
        {
            throw new BusinessRuleException("K3", "Cannot reserve a cancelled event.");
        }

        if (ev.StartsAt <= timeProvider.GetUtcNow())
        {
            throw new BusinessRuleException("K3", "Cannot reserve an event that has already started.");
        }

        // K2
        var alreadyReserved = await db.Reservations.AnyAsync(
            r => r.EventId == ev.Id && r.UserId == userId && r.Status == ReservationStatus.Active,
            cancellationToken);

        if (alreadyReserved)
        {
            throw new ConflictException("K2", "User already has an active reservation for this event.");
        }

        // K1
        if (ev.ReservedCount >= ev.Capacity)
        {
            throw new ConflictException("K1", "Event is fully booked.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // K1: check and increment in a single statement; the row lock serializes concurrent requests
        var incremented = await db.Events
            .Where(e => e.Id == ev.Id && e.Status == EventStatus.Active && e.ReservedCount < e.Capacity)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReservedCount, e => e.ReservedCount + 1), cancellationToken);

        if (incremented == 0)
        {
            throw new ConflictException("K1", "Event is fully booked.");
        }

        var reservation = new Reservation
        {
            Id = Guid.CreateVersion7(),
            EventId = ev.Id,
            Event = ev,
            UserId = userId,
            CreatedAt = timeProvider.GetUtcNow()
        };

        db.Reservations.Add(reservation);

        // K2: a concurrent duplicate violates the filtered unique index here (-> 409) and the transaction rolls back
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ReservationResponse.From(reservation);
    }

    public async Task<ReservationResponse> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var reservation = await db.Reservations
            .AsNoTracking()
            .Include(r => r.Event)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        // K7: someone else's reservation is reported as not found
        if (reservation is null || reservation.UserId != userId)
        {
            throw new NotFoundException(nameof(Reservation), id);
        }

        if (reservation.Status == ReservationStatus.Cancelled)
        {
            throw new BusinessRuleException(null, "Reservation is already cancelled.");
        }

        var now = timeProvider.GetUtcNow();

        // K4
        if (reservation.Event.StartsAt - now < CancellationCutoff)
        {
            throw new BusinessRuleException("K4",
                "Reservations cannot be cancelled less than 24 hours before the event starts.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Conditional on Status so that two concurrent cancels cannot both release the seat
        var cancelled = await db.Reservations
            .Where(r => r.Id == id && r.Status == ReservationStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReservationStatus.Cancelled)
                .SetProperty(r => r.CancelledAt, now), cancellationToken);

        if (cancelled == 0)
        {
            throw new BusinessRuleException(null, "Reservation is already cancelled.");
        }

        // K5
        await db.Events
            .Where(e => e.Id == reservation.EventId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReservedCount, e => e.ReservedCount - 1), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancelledAt = now;

        return ReservationResponse.From(reservation);
    }

    public async Task<IReadOnlyList<ReservationResponse>> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var reservations = await db.Reservations
            .AsNoTracking()
            .Include(r => r.Event)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return reservations.Select(ReservationResponse.From).ToList();
    }

    public async Task<IReadOnlyList<ReservationResponse>> GetForEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, cancellationToken))
        {
            throw new NotFoundException(nameof(Event), eventId);
        }

        var reservations = await db.Reservations
            .AsNoTracking()
            .Include(r => r.Event)
            .Where(r => r.EventId == eventId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return reservations.Select(ReservationResponse.From).ToList();
    }
}
