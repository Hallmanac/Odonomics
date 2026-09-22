using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class ChromeLaunchLineTests
{
    [Fact]
    public void Build_IncludesDebugPortAndADedicatedProfileDirectory()
    {
        string line = ChromeLaunchLine.Build();

        Assert.Contains("--remote-debugging-port=9222", line);
        Assert.Contains("--user-data-dir=", line);
        Assert.Contains("chrome-profile", line);
    }

    [Fact]
    public void BuildForPowerShell_PrefixesTheCallOperator()
    {
        string line = ChromeLaunchLine.BuildForPowerShell();

        Assert.StartsWith("& ", line);
        Assert.EndsWith(ChromeLaunchLine.Build(), line);
    }
}
