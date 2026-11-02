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
    Task<ReservationResponse> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReservationResponse>> GetForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReservationResponse>> GetForEventAsync(Guid eventId, CancellationToken cancellationToken);
}

public class ReservationService(
    IAppDbContext db,
    TimeProvider timeProvider,
    IValidator<CreateReservationRequest> createValidator) : IReservationService
{
    // The early checks give descriptive errors. Under concurrency the real guarantees are
    // the atomic conditional UPDATE (K1) and the filtered unique index (K2). See PLAN.md, section 7.
    public async Task<ReservationResponse> CreateAsync(Guid userId, CreateReservationRequest request, CancellationToken cancellationToken)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            ?? throw new NotFoundException(nameof(Event), request.EventId);

        ReservationRules.EnsureReservable(ev, timeProvider.GetUtcNow());

        // K2
        var alreadyReserved = await db.Reservations.AnyAsync(
            r => r.EventId == ev.Id && r.UserId == userId && r.Status == ReservationStatus.Active,
            cancellationToken);

        if (alreadyReserved)
        {
            throw new ConflictException("K2", "User already has an active reservation for this event.");
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

        if (reservation is null)
        {
            throw new NotFoundException(nameof(Reservation), id);
        }

        var now = timeProvider.GetUtcNow();

        ReservationRules.EnsureCancellable(reservation, userId, now);

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

    public async Task<ReservationResponse> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        // K7: filtering by owner makes someone else's reservation indistinguishable from a missing one
        var reservation = await db.Reservations
            .AsNoTracking()
            .Include(r => r.Event)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Reservation), id);

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
