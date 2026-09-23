using Odonomics.CarEdge;

namespace Odonomics.Tests.CarEdge;

public class CarEdgeSearchTests
{
    [Fact]
    public void BuildSearchUrl_NameAndLocation_IncludesBothInTheQuery()
    {
        string url = CarEdgeSearch.BuildSearchUrl("Holler Honda", "Winter Park, FL");

        Assert.StartsWith("https://caredge.com/dealers?q=", url);
        Assert.Contains(Uri.EscapeDataString("Holler Honda Winter Park, FL"), url);
    }

    [Fact]
    public void BuildSearchUrl_NoLocation_UsesNameAlone()
    {
        string url = CarEdgeSearch.BuildSearchUrl("Holler Honda", null);

        Assert.Contains(Uri.EscapeDataString("Holler Honda"), url);
        Assert.DoesNotContain("Winter+Park", url);
    }
}
