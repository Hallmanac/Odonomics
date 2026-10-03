using System.Globalization;
using System.Text.RegularExpressions;
using Odonomics.Walk;

namespace Odonomics.Auctions;

/// <summary>Reads one public salvage-auction archive page's text (bid.cars and similar) into an
/// <see cref="AuctionRecord"/>. Archive sites print the same facts as "Label: value" lines, as a
/// tab-separated row, or as a label on one line with its value on the next, so all three are read.
/// A page that is a captcha or block, too short to be a lot page, or names the VIN without any lot
/// field it recognises is a could-not-read with a reason, never a "not found": a changed layout must
/// not be mistaken for a car with no auction history. A page that does not mention the VIN at all is
/// a readable page with no record for it. These sites change and may block automated reads, so the
/// label lists below are the one place to teach the parser a new spelling.</summary>
public static partial class AuctionPageParser
{
    /// <summary>A page mentioning a captcha is only a block page below this length; a long lot page
    /// can carry the word in its footer.</summary>
    private const int MaxChallengeBodyLength = 2_000;

    private static readonly string[] BlockPhrases =
    [
        "captcha",
        "verify you are human",
        "verifying you are human",
        "checking your browser",
        "enable javascript and cookies",
        "unusual traffic",
        "access denied",
        "request blocked",
    ];

    private static readonly Dictionary<Field, string[]> Labels = new()
    {
        [Field.LotNumber] = ["lot number", "lot #", "lot no", "lot"],
        [Field.Auction] = ["auction", "auction name"],
        [Field.SaleDate] = ["sale date", "auction date", "sold date", "date of sale"],
        [Field.SaleDocument] = ["sale document", "document type", "doc type", "title type", "title/sale doc", "title/sale document"],
        [Field.PrimaryDamage] = ["primary damage"],
        [Field.SecondaryDamage] = ["secondary damage"],
        [Field.Acv] = ["acv", "actual cash value", "est. retail value", "estimated retail value"],
        [Field.AcvAndRepairEstimate] = ["acv / erc", "acv/erc"],
        [Field.RepairEstimate] = ["repair estimate", "estimated repair cost", "est. repair cost", "repair cost"],
        [Field.Odometer] = ["odometer", "odometer reading"],
    };

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "M/d/yyyy", "MM/dd/yyyy", "M/d/yyyy h:mm tt",
        "MMM d, yyyy", "MMMM d, yyyy", "dddd, MMMM d, yyyy", "d MMM yyyy", "d MMMM yyyy", "MMM dd, yyyy",
    ];

    private enum Field
    {
        LotNumber,
        Auction,
        SaleDate,
        SaleDocument,
        PrimaryDamage,
        SecondaryDamage,
        Acv,
        AcvAndRepairEstimate,
        RepairEstimate,
        Odometer,
    }

    public static AuctionPageReading Parse(string vin, string url, string title, string pageText)
    {
        string? blocked = BlockReason(title, pageText);
        if (blocked is not null)
        {
            return new AuctionPageReading(AuctionPageStatus.CouldNotRead, null, blocked);
        }

        if (!pageText.Contains(vin, StringComparison.OrdinalIgnoreCase))
        {
            return new AuctionPageReading(AuctionPageStatus.NoRecord, null, null);
        }

        Dictionary<Field, string> values = ReadLabelledValues(pageText);
        if (!values.ContainsKey(Field.LotNumber) || values.Count < 2)
        {
            return new AuctionPageReading(
                AuctionPageStatus.CouldNotRead,
                null,
                $"the page names the VIN but no lot fields were recognised, so its layout may have changed ({new Uri(url).Host})");
        }

        // bid.cars prints "ACV / ERC" as one value, "$23,937 USD / $22,579 USD": the actual cash value,
        // then the estimated repair cost.
        MatchCollection combined = Number().Matches(values.GetValueOrDefault(Field.AcvAndRepairEstimate) ?? "");
        decimal? acv = ParseMoney(values.GetValueOrDefault(Field.Acv)) ?? (combined.Count > 0 ? ParseMoney(combined[0].Value) : null);
        decimal? repairEstimate = ParseMoney(values.GetValueOrDefault(Field.RepairEstimate)) ?? (combined.Count > 1 ? ParseMoney(combined[1].Value) : null);

        var record = new AuctionRecord(
            Auction: NormalizeAuction(values.GetValueOrDefault(Field.Auction)) ?? AuctionNamedOnItsOwnLine(pageText) ?? AuctionFromSource(url, pageText),
            LotNumber: values[Field.LotNumber],
            SaleDate: ParseDate(values.GetValueOrDefault(Field.SaleDate)),
            SaleDocument: Tidy(values.GetValueOrDefault(Field.SaleDocument)),
            PrimaryDamage: Tidy(values.GetValueOrDefault(Field.PrimaryDamage)),
            SecondaryDamage: Tidy(values.GetValueOrDefault(Field.SecondaryDamage)),
            Acv: acv,
            RepairEstimate: repairEstimate,
            Odometer: ParseOdometer(values.GetValueOrDefault(Field.Odometer)),
            SourceUrl: url);
        return new AuctionPageReading(AuctionPageStatus.Found, record, null);
    }

    /// <summary>Why the page cannot be a lot page because the site blocked the read, or null when
    /// it does not look blocked. A block page names a captcha or a human check in its title or its
    /// short body; a real lot page is long, and a body under <see cref="ChallengeDetector.MinBodyTextLength"/>
    /// characters with no such wording is an empty shell, which also reads as blocked. A long page
    /// that merely mentions a captcha in its footer is still a lot page.</summary>
    public static string? BlockReason(string title, string pageText)
    {
        bool namesChallenge = BlockPhrases.Any(p => pageText.Contains(p, StringComparison.OrdinalIgnoreCase));
        bool tooShort = pageText.Length < ChallengeDetector.MinBodyTextLength;
        if (!ChallengeDetector.IsChallenge(title, pageText) && !(namesChallenge && pageText.Length < MaxChallengeBodyLength))
        {
            return null;
        }

        return tooShort && !namesChallenge
            ? "the page was too short to be a lot page, so the site likely blocked the read"
            : "the site showed a captcha or block page";
    }

    private static Dictionary<Field, string> ReadLabelledValues(string pageText)
    {
        string[] lines = [.. pageText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];
        Dictionary<Field, string> values = [];
        for (int i = 0; i < lines.Length; i++)
        {
            (string label, string value) = SplitLabel(lines[i]);
            if (FieldFor(label) is not Field field || values.ContainsKey(field))
            {
                continue;
            }

            if (value.Length == 0 && i + 1 < lines.Length && !LooksLikeLabelLine(lines[i + 1]))
            {
                value = lines[i + 1];
            }

            if (!IsPlaceholder(value))
            {
                values[field] = value;
            }
        }

        return values;
    }

    private static (string Label, string Value) SplitLabel(string line)
    {
        int separator = line.IndexOfAny([':', '\t']);
        return separator < 0
            ? (line, "")
            : (line[..separator].Trim(), line[(separator + 1)..].Trim());
    }

    /// <summary>Whether a line is itself a label with or without a value ("Location: CA - SAN MARTIN",
    /// "Sale date"), so a label left empty never takes the next label as its value.</summary>
    private static bool LooksLikeLabelLine(string line) =>
        FieldFor(SplitLabel(line).Label) is not null || LabelWithValue().IsMatch(line);

    private static Field? FieldFor(string label)
    {
        string normalized = ParentheticalSuffix().Replace(label, "").TrimEnd(':').Trim().ToLowerInvariant();
        foreach ((Field field, string[] names) in Labels)
        {
            if (names.Contains(normalized))
            {
                return field;
            }
        }

        return null;
    }

    private static bool IsPlaceholder(string value) =>
        value.Length == 0 || value is "-" or "--" or "N/A" or "n/a" or "NA" or "Unknown" or "unknown" or "None" or "none";

    private static string? NormalizeAuction(string? value) => value switch
    {
        null => null,
        _ when value.Contains("copart", StringComparison.OrdinalIgnoreCase) => "Copart",
        _ when value.Contains("iaa", StringComparison.OrdinalIgnoreCase) || value.Contains("insurance auto auctions", StringComparison.OrdinalIgnoreCase) => "IAA",
        _ => value,
    };

    /// <summary>The auction a lot page names on a line of its own, as bid.cars does between the VIN and
    /// the location ("Copart" or "IAAI"): the first such line on the page, which is the lot's own header
    /// and not the site-wide link list at the foot of the page.</summary>
    private static string? AuctionNamedOnItsOwnLine(string pageText) =>
        pageText.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Equals("Copart", StringComparison.OrdinalIgnoreCase) || l.Equals("IAA", StringComparison.OrdinalIgnoreCase) || l.Equals("IAAI", StringComparison.OrdinalIgnoreCase))
            .Select(NormalizeAuction)
            .FirstOrDefault();

    /// <summary>The auction a page that never labels one belongs to: its URL host names Copart or IAA, or
    /// its text mentions exactly one of them.</summary>
    private static string? AuctionFromSource(string url, string pageText)
    {
        string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : "";
        bool copart = host.Contains("copart", StringComparison.OrdinalIgnoreCase) || pageText.Contains("copart", StringComparison.OrdinalIgnoreCase);
        bool iaa = host.Contains("iaai", StringComparison.OrdinalIgnoreCase) || IaaWord().IsMatch(pageText);
        return (copart, iaa) switch
        {
            (true, false) => "Copart",
            (false, true) => "IAA",
            _ => null,
        };
    }

    /// <summary>Archive pages print some values in capitals ("SIDE", "SALVAGE CERTIFICATE (CA)"). An
    /// all-capitals value is lowered to sentence case with a parenthesised two-letter state kept in
    /// capitals; a value already in mixed case is left as printed.</summary>
    private static string? Tidy(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed != trimmed.ToUpperInvariant())
        {
            return trimmed;
        }

        string lowered = trimmed.ToLowerInvariant();
        string sentence = char.ToUpperInvariant(lowered[0]) + lowered[1..];
        return StateInParentheses().Replace(sentence, m => m.Value.ToUpperInvariant());
    }

    private static DateOnly? ParseDate(string? value) =>
        value is not null && DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime parsed)
            ? DateOnly.FromDateTime(parsed)
            : LeadingDate(value);

    /// <summary>A date followed by a time or a zone ("2026-07-16 10:00 AM PDT"): the leading date alone.</summary>
    private static DateOnly? LeadingDate(string? value)
    {
        Match iso = IsoDate().Match(value ?? "");
        return iso.Success && DateOnly.TryParseExact(iso.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
    }

    private static decimal? ParseMoney(string? value)
    {
        Match match = Number().Match(value ?? "");
        return match.Success && decimal.TryParse(match.Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount)
            ? amount
            : null;
    }

    /// <summary>The odometer reading in miles from "83 630 mi (134 589 km)" or "31,022 mi": the first
    /// number, whose thousands may be separated by a space, a comma, or a non-breaking space.</summary>
    private static int? ParseOdometer(string? value)
    {
        Match match = Odometer().Match(value ?? "");
        string digits = match.Success ? OdometerSeparators().Replace(match.Value, "") : "";
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int miles)
            ? miles
            : null;
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z /#.]{0,30}:(\s|$)")]
    private static partial Regex LabelWithValue();

    [GeneratedRegex(@"\s*\([^)]*\)")]
    private static partial Regex ParentheticalSuffix();

    [GeneratedRegex(@"\d+(?:[ ,\u00a0\u202f]\d{3})*")]
    private static partial Regex Odometer();

    [GeneratedRegex(@"[ ,\u00a0\u202f]")]
    private static partial Regex OdometerSeparators();

    [GeneratedRegex(@"\bIAAI?\b")]
    private static partial Regex IaaWord();

    [GeneratedRegex(@"\([a-z]{2}\)")]
    private static partial Regex StateInParentheses();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\d[\d,]*(\.\d+)?")]
    private static partial Regex Number();
}
