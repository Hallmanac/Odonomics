using Odonomics.Cli;
using Odonomics.Ledger;
using Odonomics.Walk;
using Spectre.Console.Testing;

namespace Odonomics.Tests.Cli;

public class ShowRendererPostingsTableRenderingTests
{
    private static readonly DateTimeOffset Seen = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    private static PostingEntity CarvanaPosting(string? extractedDealerName)
    {
        // The same resolution the walk applies before upserting: the page's own name, or Carvana.
        string? dealerName = WalkSites.Carvana.ResolveDealerName(extractedDealerName);
        return new PostingEntity
        {
            VehicleVin = "JTDEAMDE3NJ058833",
            Source = "carvana",
            Url = "https://www.carvana.com/vehicle/1",
            FirstSeen = Seen,
            LastSeen = Seen,
            PriceObservations = [new PriceObservationEntity { PostingId = 1, Price = 21590m, ObservedAt = Seen }],
            Dealer = dealerName is null
                ? null
                : new DealerEntity { Name = dealerName, NormalizedName = DealerNormalizer.Normalize(dealerName), NormalizedLocation = "" },
        };
    }

    [Fact]
    public void BuildPostingsTable_CarvanaPostingWhosePageNamedNoDealer_ShowsCarvanaNotADash()
    {
        string[] lines = Render(CarvanaPosting(null));

        string row = Assert.Single(lines, line => line.Contains("carvana"));
        Assert.Contains("Carvana", row);
        Assert.DoesNotContain(" - ", row.Replace("->", ""));
    }

    [Fact]
    public void BuildPostingsTable_CarvanaPostingWhosePageNamedAHub_ShowsTheHub()
    {
        string[] lines = Render(CarvanaPosting("Carvana Winder"));

        string row = Assert.Single(lines, line => line.Contains("carvana"));
        Assert.Contains("Carvana Winder", row);
    }

    [Fact]
    public void BuildPostingsTable_PostingWithNoDealerAtAll_StillShowsADash()
    {
        var posting = CarvanaPosting(null);
        posting.Dealer = null;

        string[] lines = Render(posting);

        string row = Assert.Single(lines, line => line.Contains("carvana"));
        Assert.EndsWith("-", row.TrimEnd());
    }

    private static string[] Render(PostingEntity posting)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Profile.Capabilities.Ansi = false;

        console.Write(ShowRenderer.BuildPostingsTable([posting]));

        return console.Output.Replace("\r\n", "\n").Split('\n');
    }
}
