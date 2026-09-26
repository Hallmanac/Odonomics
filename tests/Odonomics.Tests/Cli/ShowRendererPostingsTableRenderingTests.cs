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

    private static PostingEntity GradedPosting(decimal? docFee, string? addOnsNote)
    {
        PostingEntity posting = CarvanaPosting("Holler Honda");
        posting.Dealer = new DealerEntity
        {
            Name = "Holler Honda",
            NormalizedName = "HOLLER HONDA",
            NormalizedLocation = "",
            Grade = "B",
            GradeCheckedAt = Seen,
            DocFee = docFee,
            AddOnsNote = addOnsNote,
        };
        return posting;
    }

    // A long dealer cell wraps inside its column, so compare the row's text with the wrapping and the
    // column separators taken out.
    private static string Flattened(PostingEntity posting) =>
        string.Join(' ', string.Join(' ', Render(posting)).Replace('│', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void BuildPostingsTable_DealerWithADocFeeAndAddOnsNote_ShowsBothOnTheDealerLine()
    {
        string table = Flattened(GradedPosting(1199m, "$358 add-ons"));

        Assert.Contains("Holler Honda (B), doc fee $1,199, $358 add-ons", table);
    }

    [Fact]
    public void BuildPostingsTable_DealerWithOnlyAnAddOnsNote_ShowsJustTheNote()
    {
        string table = Flattened(GradedPosting(null, "No add-ons"));

        Assert.Contains("Holler Honda (B), No add-ons", table);
        Assert.DoesNotContain("doc fee", table);
    }

    [Fact]
    public void BuildPostingsTable_DealerGradedBeforeFeesWereKept_ShowsTheGradeAlone()
    {
        string table = Flattened(GradedPosting(null, null));

        Assert.Contains("Holler Honda (B)", table);
        Assert.DoesNotContain("doc fee", table);
        Assert.DoesNotContain("(B),", table);
    }

    [Fact]
    public void PostingAttributeLines_ListsOneLinePerNameAndValueWithTheSource()
    {
        PostingEntity posting = CarvanaPosting(null);
        posting.Attributes =
        [
            new PostingAttributeEntity { PostingId = 1, Name = "Rating", Value = "4.8", ObservedRunId = 1 },
            new PostingAttributeEntity { PostingId = 1, Name = "Badge", Value = "Certified [new]", ObservedRunId = 1 },
        ];

        IReadOnlyList<string> lines = ShowRenderer.PostingAttributeLines([posting]);

        Assert.Equal(["  carvana Badge: Certified [new]", "  carvana Rating: 4.8"], lines);
    }

    [Fact]
    public void PostingAttributeLines_PostingsWithNoAttributes_PrintNothing()
    {
        Assert.Empty(ShowRenderer.PostingAttributeLines([CarvanaPosting(null)]));
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
