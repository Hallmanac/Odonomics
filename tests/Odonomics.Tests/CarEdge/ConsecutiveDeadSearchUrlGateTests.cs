using Odonomics.CarEdge;

namespace Odonomics.Tests.CarEdge;

public class ConsecutiveDeadSearchUrlGateTests
{
    [Fact]
    public void ShouldStop_IsFalseUntilThreeConsecutiveDeadSearchUrls()
    {
        var gate = new ConsecutiveDeadSearchUrlGate();

        gate.RecordDeadSearchUrl();
        Assert.False(gate.ShouldStop);

        gate.RecordDeadSearchUrl();
        Assert.False(gate.ShouldStop);

        gate.RecordDeadSearchUrl();
        Assert.True(gate.ShouldStop);
    }

    [Fact]
    public void RecordOtherOutcome_ResetsTheCount()
    {
        var gate = new ConsecutiveDeadSearchUrlGate();

        gate.RecordDeadSearchUrl();
        gate.RecordDeadSearchUrl();
        gate.RecordOtherOutcome();
        gate.RecordDeadSearchUrl();
        gate.RecordDeadSearchUrl();

        Assert.False(gate.ShouldStop);
    }

    [Fact]
    public void ShouldStop_StaysTrueOnceThresholdIsReachedEvenIfCountingContinues()
    {
        var gate = new ConsecutiveDeadSearchUrlGate();

        gate.RecordDeadSearchUrl();
        gate.RecordDeadSearchUrl();
        gate.RecordDeadSearchUrl();
        gate.RecordDeadSearchUrl();

        Assert.True(gate.ShouldStop);
    }
}
