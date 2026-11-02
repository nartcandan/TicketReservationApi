using FluentValidation;

namespace TicketReservationApi.Application.Reservations;

public class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
    }
}
