using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkPacingTests
{
    [Fact]
    public void RandomDwell_AlwaysWithinTwentyToSixtySeconds()
    {
        var pacing = new WalkPacing(new Random(42));

        for (int i = 0; i < 100; i++)
        {
            TimeSpan dwell = pacing.RandomDwell();
            Assert.InRange(dwell, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60));
        }
    }

    [Fact]
    public void RandomDetailGap_AlwaysWithinFifteenToFortyFiveSeconds()
    {
        var pacing = new WalkPacing(new Random(42));

        for (int i = 0; i < 100; i++)
        {
            TimeSpan gap = pacing.RandomDetailGap();
            Assert.InRange(gap, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45));
        }
    }

    [Fact]
    public void RandomPairGap_AlwaysWithinFifteenToFortyFiveSeconds()
    {
        var pacing = new WalkPacing(new Random(42));

        for (int i = 0; i < 100; i++)
        {
            TimeSpan gap = pacing.RandomPairGap();
            Assert.InRange(gap, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45));
        }
    }

    [Fact]
    public void RandomScrollPause_AlwaysWithinTwoToFourSeconds()
    {
        var pacing = new WalkPacing(new Random(42));

        for (int i = 0; i < 100; i++)
        {
            TimeSpan pause = pacing.RandomScrollPause();
            Assert.InRange(pause, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        }
    }
}
