using FluentAssertions;
using Moq;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Pipeline;

namespace SentinelMap.Infrastructure.Tests.Pipeline;

public class IngestionPipelineTests
{
    private readonly ObservationValidator _validator = new();
    private readonly InMemoryDeduplicationService _dedup = new();
    private readonly Mock<IObservationRepository> _repo = new();
    private readonly Mock<IObservationPublisher> _publisher = new();

    private IngestionPipeline BuildPipeline() =>
        new(_validator, _dedup, _repo.Object, _publisher.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IngestionPipeline>.Instance);

    private static Observation ValidObservation() => new()
    {
        SourceType = "AIS",
        ExternalId = "235009888",
        Position = new Point(-1.5, 51.0) { SRID = 4326 },
        ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
    };

    [Fact]
    public async Task ValidObservation_PersistsAndPublishes()
    {
        var pipeline = BuildPipeline();
        var obs = ValidObservation();

        await pipeline.ProcessAsync(obs);

        _repo.Verify(r => r.AddAsync(obs, It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.PublishAsync(obs, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidObservation_SkipsWithoutPersisting()
    {
        var pipeline = BuildPipeline();
        var obs = ValidObservation();
        obs.ExternalId = "";  // invalid

        await pipeline.ProcessAsync(obs);

        _repo.Verify(r => r.AddAsync(It.IsAny<Observation>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.PublishAsync(It.IsAny<Observation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DuplicateObservation_SkipsWithoutPersisting()
    {
        var pipeline = BuildPipeline();
        var obs = ValidObservation();

        await pipeline.ProcessAsync(obs);  // first — passes through
        await pipeline.ProcessAsync(obs);  // second — same position/time bucket → duplicate

        _repo.Verify(r => r.AddAsync(It.IsAny<Observation>(), It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.PublishAsync(It.IsAny<Observation>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishExceptionDoesNotPreventCommit()
    {
        _publisher.Setup(p => p.PublishAsync(It.IsAny<Observation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis unavailable"));

        var pipeline = BuildPipeline();
        var obs = ValidObservation();

        await pipeline.Invoking(p => p.ProcessAsync(obs)).Should().NotThrowAsync();

        _repo.Verify(r => r.AddAsync(obs, It.IsAny<CancellationToken>()), Times.Once);
    }
}
