using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>The year, make, model, trim, mileage, and asking price a CarMax detail page's own text
/// states about its car, read directly off the page's visible text rather than through
/// <see cref="Extraction.ExtractionClient"/>'s LLM call. <see cref="CarMaxBackfill"/> needs this because
/// a recorded CarMax detail page carries no VIN and no URL of its own to match it back to a ledger
/// posting by (the happy path, where the VIN read off the page's HTML actually succeeds, never keeps
/// that HTML on disk, and the page's visible text never states the page's own URL either): the only
/// thing a recorded page and a posting can be matched on is what they both say about the car itself,
/// and every one of these figures is exactly what the original walk read off the same page to store
/// the posting in the first place.</summary>
public sealed record CarMaxDetailFingerprint(int Year, string Make, string Model, string? Trim, int Mileage, decimal? Price);

public static class CarMaxDetailFingerprints
{
    /// <summary>"2022 Toyota Corolla Hybrid": a four-digit year, a capitalized make word, then the
    /// rest of the line as the model. Only the first such line in the page counts, the same
    /// convention <c>WalkSites.DetailTitleLine</c> follows, since a "similar vehicles" card further
    /// down the page can repeat the shape for an unrelated car.</summary>
    private static readonly Regex TitleLine = new(
        @"^[ \t]*(?<year>\d{4})[ \t]+(?<make>[A-Z][A-Za-z]*)[ \t]+(?<model>\S.*?)[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>"33k miles" or "630 miles": CarMax rounds anything at or above 1,000 miles to the
    /// nearest thousand on both its cards and its detail pages, so the figure here is exactly what
    /// the original extraction would also have read off the same page.</summary>
    private static readonly Regex MileageLine = new(
        @"^[ \t]*(?<num>[\d,]+)(?<k>[kK])?[ \t]+miles?[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>A lone "$26,998" line is the asking price; "$649 shipping" and "$799 shipping" carry
    /// more text after the amount and are never mistaken for it.</summary>
    private static readonly Regex BarePriceLine = new(
        @"^[ \t]*\$(?<amount>[\d,]+)[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>The fingerprint <paramref name="pageText"/> states, or null when it carries no
    /// title line naming a year and make, or no mileage line: those two are the minimum a page needs
    /// to be worth comparing against a posting at all. <see cref="CarMaxDetailFingerprint.Trim"/> is
    /// null when the line under the title is itself the mileage line (a page with no separate trim
    /// line), and <see cref="CarMaxDetailFingerprint.Price"/> is null when the page states no lone
    /// dollar-amount line.</summary>
    public static CarMaxDetailFingerprint? Read(string pageText)
    {
        string text = pageText.Replace("\r\n", "\n");

        Match title = TitleLine.Match(text);
        if (!title.Success)
        {
            return null;
        }

        Match mileage = MileageLine.Match(text);
        if (!mileage.Success)
        {
            return null;
        }

        string[] lines = text.Split('\n');
        int titleLineIndex = text[..title.Index].Count(c => c == '\n');
        string? trim = null;
        for (int i = titleLineIndex + 1; i < lines.Length; i++)
        {
            string candidate = lines[i].Trim();
            if (candidate.Length == 0)
            {
                continue;
            }

            if (!MileageLine.IsMatch(lines[i]))
            {
                trim = candidate;
            }

            break;
        }

        int mileageNumber = int.Parse(mileage.Groups["num"].Value.Replace(",", ""));
        int mileageValue = mileage.Groups["k"].Success
            ? mileageNumber * 1000
            : mileageNumber;

        Match price = BarePriceLine.Match(text);
        decimal? priceValue = price.Success
            ? decimal.Parse(price.Groups["amount"].Value.Replace(",", ""))
            : null;

        return new CarMaxDetailFingerprint(
            int.Parse(title.Groups["year"].Value),
            title.Groups["make"].Value,
            Regex.Replace(title.Groups["model"].Value, @"\s+", " ").Trim(),
            trim,
            mileageValue,
            priceValue);
    }
}
