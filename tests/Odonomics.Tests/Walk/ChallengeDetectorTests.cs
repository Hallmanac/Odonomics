using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class ChallengeDetectorTests
{
    [Theory]
    [InlineData("Just a moment...")]
    [InlineData("Attention Required! | Cloudflare")]
    [InlineData("Access Denied")]
    public void IsChallenge_KnownTitleMarker_ReturnsTrue(string title)
    {
        bool result = ChallengeDetector.IsChallenge(title, new string('x', 2000));

        Assert.True(result);
    }

    [Fact]
    public void IsChallenge_NormalTitleButTinyBody_ReturnsTrue()
    {
        bool result = ChallengeDetector.IsChallenge("2020 Toyota Prius for sale", "short");

        Assert.True(result);
    }

    [Fact]
    public void IsChallenge_NormalTitleAndSubstantialBody_ReturnsFalse()
    {
        bool result = ChallengeDetector.IsChallenge("2020 Toyota Prius for sale", new string('x', 2000));

        Assert.False(result);
    }
}
