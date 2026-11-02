using FluentValidation;

namespace TicketReservationApi.Application.Reservations;

public class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public class CancelReservationRequestValidator : AbstractValidator<CancelReservationRequest>
{
    public CancelReservationRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
