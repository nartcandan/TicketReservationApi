using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Application.Common.Exceptions;
using TicketReservationApi.Application.Common.Interfaces;
using TicketReservationApi.Domain.Entities;
using TicketReservationApi.Domain.Enums;

namespace TicketReservationApi.Application.Reservations;

public interface IReservationService
{
    Task<ReservationResponse> CreateAsync(CreateReservationRequest request, CancellationToken cancellationToken);
    Task<ReservationResponse> CancelAsync(Guid id, CancelReservationRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReservationResponse>> GetForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReservationResponse>> GetForEventAsync(Guid eventId, CancellationToken cancellationToken);
}

public class ReservationService(
    IAppDbContext db,
    TimeProvider timeProvider,
    IValidator<CreateReservationRequest> createValidator,
    IValidator<CancelReservationRequest> cancelValidator) : IReservationService
{
    private static readonly TimeSpan CancellationCutoff = TimeSpan.FromHours(24);

    // Naive check-then-act: no protection against concurrent requests yet (PLAN.md, step 4).
    public async Task<ReservationResponse> CreateAsync(CreateReservationRequest request, CancellationToken cancellationToken)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken))
        {
            throw new NotFoundException(nameof(User), request.UserId);
        }

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
            r => r.EventId == ev.Id && r.UserId == request.UserId && r.Status == ReservationStatus.Active,
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

        var reservation = new Reservation
        {
            Id = Guid.CreateVersion7(),
            EventId = ev.Id,
            Event = ev,
            UserId = request.UserId,
            CreatedAt = timeProvider.GetUtcNow()
        };

        db.Reservations.Add(reservation);
        ev.ReservedCount++;

        await db.SaveChangesAsync(cancellationToken);

        return ReservationResponse.From(reservation);
    }

    public async Task<ReservationResponse> CancelAsync(Guid id, CancelReservationRequest request, CancellationToken cancellationToken)
    {
        await cancelValidator.ValidateAndThrowAsync(request, cancellationToken);

        var reservation = await db.Reservations
            .Include(r => r.Event)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        // K7: someone else's reservation is reported as not found
        if (reservation is null || reservation.UserId != request.UserId)
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

        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancelledAt = now;

        // K5
        reservation.Event.ReservedCount--;

        await db.SaveChangesAsync(cancellationToken);

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
