using Microsoft.AspNetCore.Mvc;
using TicketReservationApi.Application.Reservations;

namespace TicketReservationApi.Api.Controllers;

// TODO (step 5): require User role and take the user id from the JWT
[ApiController]
[Route("api/reservations")]
public class ReservationsController(IReservationService reservationService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReservationResponse>> Create(CreateReservationRequest request, CancellationToken cancellationToken)
    {
        var created = await reservationService.CreateAsync(request, cancellationToken);
        return Created($"/api/reservations/{created.Id}", created);
    }

    [HttpGet("me")]
    public async Task<ActionResult<IReadOnlyList<ReservationResponse>>> GetMine(
        [FromQuery] Guid userId,
        CancellationToken cancellationToken) =>
        Ok(await reservationService.GetForUserAsync(userId, cancellationToken));

    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReservationResponse>> Cancel(
        Guid id,
        CancelReservationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await reservationService.CancelAsync(id, request, cancellationToken));
}
