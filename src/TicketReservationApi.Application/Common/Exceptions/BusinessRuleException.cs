namespace TicketReservationApi.Application.Common.Exceptions;

/// <summary>Maps to 422 Unprocessable Entity. <see cref="Rule"/> is the rule id from PLAN.md (e.g. "K6").</summary>
public class BusinessRuleException(string rule, string message) : Exception(message)
{
    public string Rule { get; } = rule;
}
