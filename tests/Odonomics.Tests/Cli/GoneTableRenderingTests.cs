using Odonomics.Cli;
using Odonomics.Ledger;
using Spectre.Console;

namespace Odonomics.Tests.Cli;

[Collection(NoColorEnvironmentCollection.Name)]
public class GoneTableRenderingTests
{
    private static readonly List<GonePostingEntry> Entries =
    [
        new("JHMZE2H79AS041642", 2010, "Honda", "Insight", "cars.com", "https://cars.com/a", 9000m, GoneReasons.BelowYearFacet),
        new("4T1DAACK9TU267793", 2026, "Toyota", "Camry Hybrid", "carvana.com", "https://carvana.com/b", 24000m, GoneReasons.SearchMoved),
        new("JTDKN3DU0A0000001", 2020, "Toyota", "Corolla Hybrid", "auto.dev", "https://auto.dev/c", 123456m, GoneReasons.NotOnSearchPage),
        new("JTDKN3DU0A0000002", 2020, "Toyota", "Prius", "marketcheck", "https://mc/d", 15000m, GoneReasons.OverMileage),
        new("JTDKN3DU0A0000003", 2021, "Toyota", "Prius", "carvana", "https://carvana.com/e", 16000m, GoneReasons.BeyondTheCap),
    ];

    [Fact]
    public void BuildGoneTable_WithEveryReason_KeepsEveryVinAndReasonOnOneLineWithinEightyColumns()
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            var writer = new StringWriter();
            IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

            console.Write(DiffRenderer.BuildGoneTable(Entries));

            string[] lines = writer.ToString().Replace("\r\n", "\n").Split('\n');

            Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
            Assert.Contains(lines, line => line.Contains("Reason"));
            foreach (GonePostingEntry entry in Entries)
            {
                string row = Assert.Single(lines, line => line.Contains(entry.Vin));
                Assert.Contains(entry.Reason, row);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original);
        }
    }

    [Fact]
    public void GoneHeading_WithRowsBeyondTheCap_SaysHowManyThereAre()
    {
        Assert.Equal("Gone (5, 1 beyond the cap)", DiffRenderer.GoneHeading(Entries));
    }

    [Fact]
    public void GoneHeading_WithNoRowsBeyondTheCap_IsTheBareCount()
    {
        Assert.Equal("Gone (4)", DiffRenderer.GoneHeading([.. Entries.Where(e => e.Reason != GoneReasons.BeyondTheCap)]));
        Assert.Equal("Gone (0)", DiffRenderer.GoneHeading([]));
    }
}
