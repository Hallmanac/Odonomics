using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Recognizes a cars.com detail page for a new car from its captured page text. A new-car
/// listing has no mileage to rank on, so the walk drops it as <see cref="DetailPageOutcome.NewCar"/>
/// rather than as <see cref="DetailPageOutcome.MissingFields"/>, which is reserved for a used car
/// whose page lacks year, price, or mileage. Two independent signs are checked: the page's own title
/// line reads "New 2027 Toyota Corolla Hybrid LE" (the first "New/Used/Certified year make model"
/// line, so a "Similar vehicles" card further down never decides it), or the page prints an MSRP
/// line but no Mileage line, which is what a used listing's page never does.</summary>
public static partial class NewCarPage
{
    [GeneratedRegex(@"^\s*(New|Used|Certified)\s+\d{4}\s", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionTitleLine();

    public static bool Reads(string pageText)
    {
        string[] lines = pageText.Split('\n', StringSplitOptions.TrimEntries);

        string? title = lines.FirstOrDefault(l => ConditionTitleLine().IsMatch(l));
        if (title is not null && title.StartsWith("New", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return lines.Contains("MSRP") && !lines.Contains("Mileage");
    }
}
