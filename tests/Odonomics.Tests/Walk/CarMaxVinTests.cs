using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the VIN read off a carmax detail page's HTML. The HTML fixture is cut to carry the
/// VIN the way the 2026-09-26 probe found it on car/29085801 (JTDACACU8S3046841 is in the page's HTML and
/// not in its visible text): in structured data, an attribute, and the page's embedded data, with a
/// similar car's VIN after the page's own.</summary>
public class CarMaxVinTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    [Fact]
    public void Read_RecordedDetailHtml_ReturnsThePagesOwnVinAndNotASimilarCars()
    {
        Assert.Equal("JTDACACU8S3046841", CarMaxVin.Read(Fixture("carmax-detail-29085801.html")));
    }

    [Fact]
    public void Read_RecordedDetailText_HasNoVinToRead()
    {
        Assert.DoesNotContain("JTDACACU8S3046841", Fixture("carmax-detail-29085801.txt"));
        Assert.Null(CarMaxVin.Read(Fixture("carmax-detail-29085801.txt")));
    }

    [Theory]
    [InlineData("{\"vin\":\"JTDACACU8S3046841\"}")]
    [InlineData("{\\\"vin\\\":\\\"JTDACACU8S3046841\\\"}")]
    [InlineData("\"vehicleIdentificationNumber\": \"JTDACACU8S3046841\"")]
    [InlineData("<div data-vin=\"JTDACACU8S3046841\">")]
    [InlineData("<a href=\"/x?VIN=JTDACACU8S3046841&stock=1\">")]
    [InlineData("VIN: JTDACACU8S3046841")]
    public void Read_VinUnderAKey_ReturnsIt(string html)
    {
        Assert.Equal("JTDACACU8S3046841", CarMaxVin.Read(html));
    }

    [Theory]
    [InlineData("{\"vin\":\"JTDACACU8S304684\"}")]
    [InlineData("{\"vin\":\"JTDACACU8S30468411\"}")]
    [InlineData("{\"vin\":\"JTDACACU8S3O46841\"}")]
    [InlineData("{\"vinyl\":\"JTDACACU8S3046841\"}")]
    [InlineData("{\"stockNumber\":\"JTDACACU8S3046841\"}")]
    [InlineData("<html>no vehicle data here</html>")]
    [InlineData("")]
    public void Read_NoWellFormedVinUnderAKey_ReturnsNull(string html)
    {
        Assert.Null(CarMaxVin.Read(html));
    }
}
