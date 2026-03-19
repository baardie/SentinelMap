using FluentValidation;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Pipeline;

public class ObservationValidator : AbstractValidator<Observation>
{
    private static readonly TimeSpan FutureSkew = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxStaleness = TimeSpan.FromHours(24);

    public ObservationValidator()
    {
        RuleFor(o => o.SourceType).NotEmpty();
        RuleFor(o => o.ExternalId).NotEmpty();

        RuleFor(o => o.Position).NotNull();

        When(o => o.Position is not null, () =>
        {
            RuleFor(o => o.Position!.Y)   // Y = latitude in NetTopologySuite
                .InclusiveBetween(-90.0, 90.0)
                .WithName("Latitude");

            RuleFor(o => o.Position!.X)   // X = longitude
                .InclusiveBetween(-180.0, 180.0)
                .WithName("Longitude");
        });

        RuleFor(o => o.ObservedAt)
            .Must(t => t <= DateTimeOffset.UtcNow.Add(FutureSkew))
            .WithMessage("ObservedAt is in the future.")
            .Must(t => t >= DateTimeOffset.UtcNow - MaxStaleness)
            .WithMessage("ObservedAt is more than 24h stale.");
    }
}
