using Odonomics.Cli;

namespace Odonomics.Tests.Cli;

public class ResearchProgressLineTests
{
    [Fact]
    public void Format_NoFlags_PrintsNone()
    {
        string line = ResearchProgressLine.Format("2025 Toyota Camry Hybrid (4T1G11AK0LU123456)", []);

        Assert.Equal("2025 Toyota Camry Hybrid (4T1G11AK0LU123456): researched, none", line);
    }

    [Fact]
    public void Format_WithFlags_NamesTheFlagKindsInline()
    {
        string line = ResearchProgressLine.Format("2025 Toyota Camry Hybrid (4T1...)", ["12-sellers", "mileage-drop"]);

        Assert.Equal("2025 Toyota Camry Hybrid (4T1...): researched, 12-sellers, mileage-drop", line);
    }

    [Fact]
    public void Format_ManyFlags_NeverExceedsEightyColumns()
    {
        string line = ResearchProgressLine.Format(
            "2025 Toyota Camry Hybrid (4T1G11AK0LU123456)",
            ["no-remedy-recall", "mileage-drop", "34-sellers", "low-safety-rating", "price-spike"]);

        Assert.True(line.Length <= ResearchProgressLine.MaxLineWidth, $"line exceeded {ResearchProgressLine.MaxLineWidth} columns ({line.Length}): \"{line}\"");
        Assert.EndsWith("…", line);
    }
}
