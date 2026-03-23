using FluentAssertions;
using SentinelMap.Infrastructure.Correlation;

namespace SentinelMap.Infrastructure.Tests.Correlation;

public class JaroWinklerTests
{
    [Fact]
    public void IdenticalStrings_Returns1()
    {
        JaroWinkler.Similarity("HELLO", "HELLO").Should().Be(1.0);
    }

    [Fact]
    public void EmptyStrings_Returns1()
    {
        JaroWinkler.Similarity("", "").Should().Be(1.0);
    }

    [Fact]
    public void OneEmpty_Returns0()
    {
        JaroWinkler.Similarity("HELLO", "").Should().Be(0.0);
        JaroWinkler.Similarity("", "HELLO").Should().Be(0.0);
    }

    [Fact]
    public void MarthaAndMarhta_ApproximatelyExpected()
    {
        // Classic Jaro-Winkler test case: MARTHA vs MARHTA ≈ 0.961
        var score = JaroWinkler.Similarity("MARTHA", "MARHTA");
        score.Should().BeApproximately(0.961, 0.01);
    }

    [Fact]
    public void EverGivenVariants_HighSimilarity()
    {
        var score = JaroWinkler.Similarity("EVER GIVEN", "EVERGIVEN");
        score.Should().BeGreaterThan(0.85);
    }

    [Fact]
    public void CompletelyDifferentStrings_LowScore()
    {
        var score = JaroWinkler.Similarity("ABCDEF", "ZYXWVU");
        score.Should().BeLessThan(0.5);
    }

    [Fact]
    public void SingleCharacterStrings_Match()
    {
        JaroWinkler.Similarity("A", "A").Should().Be(1.0);
    }

    [Fact]
    public void SingleCharacterStrings_NoMatch()
    {
        JaroWinkler.Similarity("A", "B").Should().Be(0.0);
    }

    [Fact]
    public void NullInputs_ReturnExpected()
    {
        JaroWinkler.Similarity(null!, null!).Should().Be(1.0);
        JaroWinkler.Similarity(null!, "HELLO").Should().Be(0.0);
        JaroWinkler.Similarity("HELLO", null!).Should().Be(0.0);
    }
}
