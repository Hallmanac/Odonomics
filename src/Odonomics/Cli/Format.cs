using Odonomics.Domain;
using Spectre.Console;

namespace Odonomics.Cli;

/// <summary>Locale-independent formatting for money and bands, so output is identical on every
/// machine regardless of the host's own culture settings (the project ships InvariantGlobalization).</summary>
public static class Format
{
    public static string Money(decimal value) => $"${value:N0}";

    public static string MoneyCents(decimal value) => $"${value:N2}";

    /// <summary>A point band renders as one figure; a range renders "$low-$high" so it still fits
    /// a narrow column at 80 total columns.</summary>
    public static string Band(Band band) =>
        band.IsRange ? $"{Money(band.Low)}-{Money(band.High)}" : Money(band.Expected);

    /// <summary>Escapes a value bound for Table.AddRow, which parses every string it's given as
    /// Spectre markup: a make, model, trim, or note that came from a scraped page or an operator
    /// keystroke can carry a literal '[' (e.g. a dealer's own "[SOLD]" prefix), which would
    /// otherwise be read as the start of a markup tag.</summary>
    public static string Cell(string value) => Markup.Escape(value);

    /// <summary>Bounds a value to at most <paramref name="maxWidth"/> characters, replacing the
    /// tail with an ellipsis when it's cut short. A fixed-width table column never wraps a row
    /// onto a second line no matter how long a scraped or scenario-file value turns out to be,
    /// only Spectre's own word-wrapping does that, so the value has to already fit before it
    /// reaches the table.</summary>
    public static string Truncate(string value, int maxWidth)
    {
        if (value.Length <= maxWidth)
        {
            return value;
        }

        return maxWidth <= 1 ? new string('…', Math.Max(maxWidth, 0)) : $"{value[..(maxWidth - 1)]}…";
    }
}
