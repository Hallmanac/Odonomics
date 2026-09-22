using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class DealerNormalizerTests
{
    [Theory]
    [InlineData("Holler Honda", "HOLLER HONDA")]
    [InlineData("  Holler   Honda  ", "HOLLER HONDA")]
    [InlineData("holler honda", "HOLLER HONDA")]
    public void Normalize_DifferentCasingOrSpacing_ProducesTheSameKey(string value, string expected)
    {
        Assert.Equal(expected, DealerNormalizer.Normalize(value));
    }

    [Theory]
    [InlineData("Winter Park, FL", "WINTER PARK FL")]
    [InlineData("Winter Park FL", "WINTER PARK FL")]
    public void Normalize_WithOrWithoutPunctuation_ProducesTheSameKey(string value, string expected)
    {
        Assert.Equal(expected, DealerNormalizer.Normalize(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_NullOrBlank_ReturnsEmptyString(string? value)
    {
        Assert.Equal("", DealerNormalizer.Normalize(value));
    }
}
