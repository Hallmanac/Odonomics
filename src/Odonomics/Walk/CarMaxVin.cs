using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads the VIN off a CarMax detail page's HTML. The page's visible text never prints
/// it, so the extraction that reads the text cannot find it, but the HTML carries it as a value
/// under a "vin" or "vehicleIdentificationNumber" key, in the page's embedded data or in an
/// attribute. It is read here and not by the extraction model because it is a plain string of
/// exactly 17 characters that the ledger keys a vehicle on.</summary>
public static class CarMaxVin
{
    // The key, then a few non-word characters (a quote, a colon, an escaped quote, an equals sign),
    // then the VIN itself. Only the key is matched without regard to letter case: a VIN is upper case,
    // and never contains I, O, or Q.
    private static readonly Regex KeyedVin = new(
        @"\b(?i:vin|vehicleIdentificationNumber)\W{1,8}(?<vin>[A-HJ-NPR-Z0-9]{17})(?![A-Za-z0-9])",
        RegexOptions.Compiled);

    /// <summary>The first VIN the page's HTML gives under a VIN key, or null when it gives none.</summary>
    public static string? Read(string html)
    {
        Match match = KeyedVin.Match(html);
        return match.Success ? match.Groups["vin"].Value : null;
    }
}
