using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketReservationApi.Api.Auth;
using TicketReservationApi.Application.Reservations;

namespace TicketReservationApi.Api.Controllers;

[ApiController]
[Route("api/reservations")]
[Authorize(Roles = Roles.User)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
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
        var created = await reservationService.CreateAsync(User.GetUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await reservationService.GetByIdAsync(id, User.GetUserId(), cancellationToken));

    [HttpGet("me")]
    public async Task<ActionResult<IReadOnlyList<ReservationResponse>>> GetMine(CancellationToken cancellationToken) =>
        Ok(await reservationService.GetForUserAsync(User.GetUserId(), cancellationToken));

    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReservationResponse>> Cancel(Guid id, CancellationToken cancellationToken) =>
        Ok(await reservationService.CancelAsync(id, User.GetUserId(), cancellationToken));
}
