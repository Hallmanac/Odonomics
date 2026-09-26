using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads the model off a CarMax detail page's own title line ("2025 Toyota Camry"). The extraction
/// is asked for the model and then checked against the page text, and a model that does not appear there
/// is dropped, so a Camry page the model read as "Camry Hybrid" comes back with no model at all even though
/// the title names it. The trim ("SE") and the mileage are on the lines below the title, so what follows
/// the make on that line is the model. Only the first line that starts with a year counts, since the similar
/// cars listed further down open with a year too.</summary>
public static class CarMaxTitles
{
    /// <summary>The model the page's first year-led line names after <paramref name="year"/> and
    /// <paramref name="make"/>, or null when the make or year is unknown, the first such line is another
    /// car's, or nothing follows the make.</summary>
    public static string? ReadModel(string pageText, string? make, int? year)
    {
        if (string.IsNullOrWhiteSpace(make) || year is not int titleYear)
        {
            return null;
        }

        Match titleLine = TitleLine.Match(pageText);
        if (!titleLine.Success)
        {
            return null;
        }

        Match match = Regex.Match(
            titleLine.Value.Trim(),
            $@"^{titleYear}\s+{Regex.Escape(make.Trim())}\s+(?<model>\S.*)$",
            RegexOptions.IgnoreCase);
        return match.Success
            ? Regex.Replace(match.Groups["model"].Value, @"\s+", " ").Trim()
            : null;
    }

    private static readonly Regex TitleLine = new(@"^[ \t]*\d{4}[ \t]+\S.*$", RegexOptions.Compiled | RegexOptions.Multiline);
}
