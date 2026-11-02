namespace TicketReservationApi.Application.Common.Exceptions;

/// <summary>Maps to 404 Not Found.</summary>
public class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.");
