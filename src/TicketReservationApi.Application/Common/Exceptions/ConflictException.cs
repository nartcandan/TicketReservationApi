namespace TicketReservationApi.Application.Common.Exceptions;

/// <summary>Maps to 409 Conflict.</summary>
public class ConflictException(string message) : Exception(message);
