using FluentValidation;

namespace TicketReservationApi.Application.Events;

/// <summary>Query string parameters of <c>GET /api/events</c>.</summary>
public record EventListQuery
{
    public const int MaxPageSize = 100;

    /// <summary>Only events starting at or after this time.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Only events starting at or before this time.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Case-insensitive match on title or venue.</summary>
    public string? Search { get; init; }

    /// <summary>Only events that can be reserved now: active, not started and not full.</summary>
    public bool OnlyAvailable { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class EventListQueryValidator : AbstractValidator<EventListQuery>
{
    public EventListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, EventListQuery.MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(200);

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("'To' must be later than or equal to 'From'.");
    }
}
