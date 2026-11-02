namespace TicketReservationApi.Application.Common.Exceptions;

/// <summary>Maps to 401 Unauthorized.</summary>
public class AuthenticationFailedException(string message) : Exception(message);
