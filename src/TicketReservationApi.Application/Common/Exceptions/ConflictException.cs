namespace TicketReservationApi.Application.Common.Exceptions;

/// <summary>Maps to 409 Conflict. <see cref="Rule"/> is the rule id from PLAN.md (e.g. "K1").</summary>
public class ConflictException(string rule, string message) : Exception(message)
{
    public string Rule { get; } = rule;
}
