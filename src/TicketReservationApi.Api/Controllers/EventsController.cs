using Microsoft.AspNetCore.Mvc;
using TicketReservationApi.Application.Events;
using TicketReservationApi.Application.Reservations;

namespace TicketReservationApi.Api.Controllers;

// TODO (step 5): restrict write endpoints to Admin
[ApiController]
[Route("api/events")]
public class EventsController(IEventService eventService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EventResponse>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await eventService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EventResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await eventService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EventResponse>> Create(CreateEventRequest request, CancellationToken cancellationToken)
    {
        var created = await eventService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EventResponse>> Update(Guid id, UpdateEventRequest request, CancellationToken cancellationToken) =>
        Ok(await eventService.UpdateAsync(id, request, cancellationToken));

    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await eventService.CancelAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/reservations")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ReservationResponse>>> GetReservations(
        Guid id,
        [FromServices] IReservationService reservationService,
        CancellationToken cancellationToken) =>
        Ok(await reservationService.GetForEventAsync(id, cancellationToken));
}
