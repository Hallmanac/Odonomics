using Odonomics.Tests.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

[Collection(DataDirectoryEnvironmentCollection.Name)]
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
        string built = ChromeLaunchLine.Build();
        string line = ChromeLaunchLine.BuildForPowerShell();

        Assert.StartsWith("& ", line);
        Assert.EndsWith(built, line);
    }

    [Fact]
    public void BuildForEdge_IncludesDebugPortAndTheSharedProfileDirectoryAndNamesEdge()
    {
        string line = ChromeLaunchLine.BuildForEdge();

        Assert.Contains("--remote-debugging-port=9222", line);
        Assert.Contains("--user-data-dir=", line);
        Assert.Contains("chrome-profile", line);
        Assert.Contains("Edge", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAndBuildForEdge_UseTheSameProfileDirectory()
    {
        string chromeLine = ChromeLaunchLine.Build();
        string edgeLine = ChromeLaunchLine.BuildForEdge();

        Assert.Contains("--user-data-dir=", chromeLine);
        Assert.Contains("--user-data-dir=", edgeLine);
        Assert.Equal(
            chromeLine[chromeLine.IndexOf("--user-data-dir=", StringComparison.Ordinal)..],
            edgeLine[edgeLine.IndexOf("--user-data-dir=", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void BuildForEdgeForPowerShell_PrefixesTheCallOperator()
    {
        string builtForEdge = ChromeLaunchLine.BuildForEdge();
        string line = ChromeLaunchLine.BuildForEdgeForPowerShell();

        Assert.StartsWith("& ", line);
        Assert.EndsWith(builtForEdge, line);
    }

    [Fact]
    public void Build_OnMacOS_UsesTheEscapedChromeApplicationPath()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        string line = ChromeLaunchLine.Build();

        Assert.Contains("/Applications/Google\\ Chrome.app/Contents/MacOS/Google\\ Chrome", line);
    }

    [Fact]
    public void BuildForEdge_OnMacOS_UsesTheEscapedEdgeApplicationPath()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        string line = ChromeLaunchLine.BuildForEdge();

        Assert.Contains("/Applications/Microsoft\\ Edge.app/Contents/MacOS/Microsoft\\ Edge", line);
    }
}
