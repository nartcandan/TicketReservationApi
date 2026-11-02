using FluentValidation;

namespace TicketReservationApi.Application.Events;

public abstract class EventRequestValidator<T> : AbstractValidator<T> where T : IEventRequest
{
    protected EventRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Venue).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Capacity).GreaterThan(0);

        // K9: StartsAt cannot be in the past
        RuleFor(x => x.StartsAt)
            .Must(startsAt => startsAt > timeProvider.GetUtcNow())
            .WithMessage("'Starts At' must be in the future.");
    }
}

public class CreateEventRequestValidator(TimeProvider timeProvider)
    : EventRequestValidator<CreateEventRequest>(timeProvider);

public class UpdateEventRequestValidator(TimeProvider timeProvider)
    : EventRequestValidator<UpdateEventRequest>(timeProvider);
