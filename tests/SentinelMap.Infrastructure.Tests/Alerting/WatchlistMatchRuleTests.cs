using FluentAssertions;
using Moq;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Alerting;
using SentinelMap.SharedKernel.Enums;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Tests.Alerting;

public class WatchlistMatchRuleTests
{
    private readonly Mock<IWatchlistRepository> _watchlistRepo = new();
    private readonly Mock<IDatabase> _redisDb = new();
    private readonly WatchlistMatchRule _sut;

    public WatchlistMatchRuleTests()
    {
        _sut = new WatchlistMatchRule(_watchlistRepo.Object, _redisDb.Object);

        // Default: not already alerted
        _redisDb.Setup(db => db.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);
        _redisDb.Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Default: not watchlisted
        _watchlistRepo.Setup(r => r.IsWatchlistedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private static TrackedEntity MakeEntity(string displayName = "Vessel X") => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = displayName,
        Type = EntityType.Vessel
    };

    [Fact]
    public async Task EntityMatchingWatchlistEntry_ReturnsCriticalTrigger()
    {
        var entity = MakeEntity("Vessel Wanted");
        _watchlistRepo.Setup(r => r.IsWatchlistedAsync("Name", "Vessel Wanted", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().HaveCount(1);
        triggers[0].Type.Should().Be(AlertType.WatchlistMatch);
        triggers[0].Severity.Should().Be(AlertSeverity.Critical);
        triggers[0].Summary.Should().Contain("Vessel Wanted");
    }

    [Fact]
    public async Task EntityNotOnWatchlist_ReturnsEmpty()
    {
        var entity = MakeEntity("Vessel Clean");

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().BeEmpty();
    }

    [Fact]
    public async Task EntityAlreadyAlerted_Debounce_ReturnsEmpty()
    {
        var entity = MakeEntity("Vessel Alerted");
        _redisDb.Setup(db => db.KeyExistsAsync($"watchlist:alerted:{entity.Id}", It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _watchlistRepo.Setup(r => r.IsWatchlistedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().BeEmpty();
        _watchlistRepo.Verify(r => r.IsWatchlistedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EntityWithTypedIdentifier_MatchesWatchlistByIdentifier()
    {
        var entity = MakeEntity("Vessel Foxtrot");
        entity.Identifiers.Add(new EntityIdentifier
        {
            IdentifierType = "MMSI",
            IdentifierValue = "123456789",
            Source = "AIS"
        });

        _watchlistRepo.Setup(r => r.IsWatchlistedAsync("MMSI", "123456789", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var triggers = await _sut.EvaluateAsync(entity);

        triggers.Should().HaveCount(1);
        triggers[0].Severity.Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public async Task EntityMatchedOnWatchlist_SetsRedisDebounceKey_SubsequentCallReturnsEmpty()
    {
        var entity = MakeEntity("Vessel Zulu");
        var alertedKey = $"watchlist:alerted:{entity.Id}";

        _watchlistRepo.Setup(r => r.IsWatchlistedAsync("Name", "Vessel Zulu", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // First call: should return a trigger
        var firstTriggers = await _sut.EvaluateAsync(entity);
        firstTriggers.Should().HaveCount(1);

        // Simulate the debounce key now existing after first evaluation
        _redisDb.Setup(db => db.KeyExistsAsync(alertedKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Second call: debounce should suppress the alert
        var secondTriggers = await _sut.EvaluateAsync(entity);
        secondTriggers.Should().BeEmpty();
    }
}
