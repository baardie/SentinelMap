using FluentAssertions;
using SentinelMap.Infrastructure.Pipeline;

namespace SentinelMap.Infrastructure.Tests.Pipeline;

public class RedisDeduplicationServiceTests
{
    private readonly InMemoryDeduplicationService _sut = new();

    [Fact]
    public async Task FirstOccurrence_ReturnsFalse()
    {
        var result = await _sut.IsDuplicateAsync("key1", TimeSpan.FromMinutes(1));
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SecondOccurrenceWithinTtl_ReturnsTrue()
    {
        await _sut.IsDuplicateAsync("key1", TimeSpan.FromMinutes(1));
        var result = await _sut.IsDuplicateAsync("key1", TimeSpan.FromMinutes(1));
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DifferentKeys_BothReturnFalse()
    {
        var r1 = await _sut.IsDuplicateAsync("key1", TimeSpan.FromMinutes(1));
        var r2 = await _sut.IsDuplicateAsync("key2", TimeSpan.FromMinutes(1));
        r1.Should().BeFalse();
        r2.Should().BeFalse();
    }

    [Fact]
    public async Task ObservationDeduplicationKey_SamePositionSameBucket_IsDuplicate()
    {
        var key = RedisDeduplicationService.BuildKey("AIS", "235009888", 51.1234, -1.5678, DateTimeOffset.UtcNow);
        await _sut.IsDuplicateAsync(key, TimeSpan.FromMinutes(1));

        var key2 = RedisDeduplicationService.BuildKey("AIS", "235009888", 51.12345, -1.56789, DateTimeOffset.UtcNow);
        var result = await _sut.IsDuplicateAsync(key2, TimeSpan.FromMinutes(1));

        result.Should().BeTrue("micro-jitter within 4dp precision maps to the same key");
    }

    [Fact]
    public async Task ObservationDeduplicationKey_DifferentPosition_IsNotDuplicate()
    {
        var key1 = RedisDeduplicationService.BuildKey("AIS", "235009888", 51.1234, -1.5678, DateTimeOffset.UtcNow);
        var key2 = RedisDeduplicationService.BuildKey("AIS", "235009888", 52.0000, -2.0000, DateTimeOffset.UtcNow);

        await _sut.IsDuplicateAsync(key1, TimeSpan.FromMinutes(1));
        var result = await _sut.IsDuplicateAsync(key2, TimeSpan.FromMinutes(1));

        result.Should().BeFalse("genuinely different position is not a duplicate");
    }
}
