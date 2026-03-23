using FluentAssertions;
using SentinelMap.Infrastructure.Correlation;

namespace SentinelMap.Infrastructure.Tests.Correlation;

public class NameNormaliserTests
{
    [Fact]
    public void StripsMvPrefix()
    {
        NameNormaliser.Normalise("MV EVER GIVEN").Should().Be("EVER GIVEN");
    }

    [Fact]
    public void StripsHmsPrefix_AndCollapsesWhitespace()
    {
        NameNormaliser.Normalise("  HMS  Queen  Elizabeth  ").Should().Be("QUEEN ELIZABETH");
    }

    [Fact]
    public void UppercasesInput()
    {
        NameNormaliser.Normalise("tanker blue").Should().Be("TANKER BLUE");
    }

    [Fact]
    public void NullOrEmpty_ReturnsEmpty()
    {
        NameNormaliser.Normalise(null!).Should().BeEmpty();
        NameNormaliser.Normalise("").Should().BeEmpty();
        NameNormaliser.Normalise("   ").Should().BeEmpty();
    }

    [Fact]
    public void DoesNotStripPrefixWithinName()
    {
        // "SS" should only be stripped if it's a prefix followed by space
        NameNormaliser.Normalise("MISSISSIPPI").Should().Be("MISSISSIPPI");
    }

    [Fact]
    public void StripsSsPrefix()
    {
        NameNormaliser.Normalise("SS United States").Should().Be("UNITED STATES");
    }

    [Fact]
    public void OnlyStripsOnePrefix()
    {
        // "MV SS Something" — strip MV, leave SS SOMETHING
        NameNormaliser.Normalise("MV SS Something").Should().Be("SS SOMETHING");
    }
}
