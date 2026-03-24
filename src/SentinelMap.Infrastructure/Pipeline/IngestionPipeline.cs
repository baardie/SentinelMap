using Microsoft.Extensions.Logging;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Pipeline;

public class IngestionPipeline
{
    private readonly ObservationValidator _validator;
    private readonly IDeduplicationService _dedup;
    private readonly IObservationRepository _repo;
    private readonly IObservationPublisher _publisher;
    private readonly ILogger<IngestionPipeline> _logger;

    private static readonly TimeSpan DedupTtl = TimeSpan.FromMinutes(2);

    public IngestionPipeline(
        ObservationValidator validator,
        IDeduplicationService dedup,
        IObservationRepository repo,
        IObservationPublisher publisher,
        ILogger<IngestionPipeline> logger)
    {
        _validator = validator;
        _dedup = dedup;
        _repo = repo;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task ProcessAsync(Observation observation, CancellationToken ct = default)
    {
        // Infrastructure and safety messages bypass validation/dedup — publish directly to Redis
        if (observation.SourceType is "AIS_INFRA" or "AIS_SAFETY")
        {
            try { await _publisher.PublishAsync(observation, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish {SourceType} observation to Redis", observation.SourceType);
            }
            return;
        }

        // Stage 1: Validate
        var validation = await _validator.ValidateAsync(observation, ct);
        if (!validation.IsValid)
        {
            _logger.LogDebug("Observation failed validation: {Errors}",
                string.Join(", ", validation.Errors.Select(e => e.ErrorMessage)));
            return;
        }

        // Stage 2: Deduplicate
        var dedupKey = RedisDeduplicationService.BuildKey(
            observation.SourceType,
            observation.ExternalId,
            observation.Position!.Y,
            observation.Position.X,
            observation.ObservedAt);

        if (await _dedup.IsDuplicateAsync(dedupKey, DedupTtl, ct))
        {
            _logger.LogDebug("Observation deduplicated: {Source}:{ExternalId}", observation.SourceType, observation.ExternalId);
            return;
        }

        // Stage 3: Persist
        await _repo.AddAsync(observation, ct);

        // Stage 4: Publish (non-blocking — publish failure must not roll back the persist)
        try
        {
            await _publisher.PublishAsync(observation, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish observation {Id} to Redis — observation is persisted, correlation will recover on restart", observation.Id);
        }
    }
}
