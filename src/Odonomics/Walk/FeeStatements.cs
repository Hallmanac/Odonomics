using System.Globalization;
using System.Text.RegularExpressions;
using Odonomics.Ledger;

namespace Odonomics.Walk;

/// <summary>What a detail page says about the dealer fees behind its price: the
/// <see cref="FeePostures"/> value, and the sum of the fee lines the page itemized (null when it
/// itemized none). The sum is kept whatever the posture: on an itemized page it is what the fees add
/// on top of the price, and on an all-in page it is how much of the price is fees, which is shown and
/// never added. <paramref name="ListingPrice"/> and <paramref name="TotalPrice"/> are the page's own
/// pre-fee and fee-inclusive figures when its price block prints both, so the walk can tell which of
/// the two it actually stored (see <see cref="ReconciledWith"/>).</summary>
public sealed record FeeStatement(string Posture, decimal? ItemizedTotal, decimal? ListingPrice = null, decimal? TotalPrice = null)
{
    /// <summary>This statement and the asking price to store, reconciled with the price the walk
    /// extracted for the page. The posture rests on the assumption that the stored price is the page's
    /// headline (fee-inclusive) price, but the price comes from the extraction model, which sometimes
    /// stores the pre-fee listing price instead. When the page prints a listing price and a total that
    /// differ by exactly its fee lines, an extracted price equal to the listing price is replaced by the
    /// total, so the stored price is the headline the statement assumes and the posture stays all-in.
    /// It is not turned into itemized instead: a later cars.com search-card touch re-bases the stored
    /// price on the card's fee-inclusive figure while leaving the posture alone, which would count the
    /// fees twice. An extracted price equal to the total means the fees are in it, so an itemized
    /// reading becomes all-in and they are not counted twice. Any other extracted price, or a page
    /// whose figures do not add up, leaves both as they were.</summary>
    public (FeeStatement Statement, decimal AskingPrice) ReconciledWith(decimal askingPrice)
    {
        if (ItemizedTotal is not decimal fees || ListingPrice is not decimal listing
            || TotalPrice is not decimal total || listing == total || listing + fees != total)
        {
            return (this, askingPrice);
        }

        return (Posture, askingPrice) switch
        {
            (FeePostures.AllIn, _) when askingPrice == listing => (this, total),
            (FeePostures.Itemized, _) when askingPrice == total => (this with { Posture = FeePostures.AllIn }, askingPrice),
            _ => (this, askingPrice),
        };
    }
}

/// <summary>Reads a dealer's own fee statements off a cars.com or Autotrader detail page's text into
/// a <see cref="FeeStatement"/>. It is read here and not by the extraction model because it decides
/// what a car costs to take home and whether a red flag fires, so it should never depend on a model's
/// reading. Every reader answers one question: does the page say the price it shows already contains
/// the dealer's fees? A page that says so is all-in, whatever fee lines it also itemizes, because the
/// price the walk stores is normally the page's headline price and a headline that already contains
/// those lines must not have them added again (on every 2026-09-26 recorded page that itemizes fees,
/// the headline equals the printed total, fees included). The lines' sum is still kept on the
/// statement so it can be shown. A page that lists fee lines with amounts and never says they are
/// included is itemized, and its total is what the fees add on top. A page that says neither is
/// unknown. Where the stored price turns out not to be the headline, <see cref="FeeStatement.ReconciledWith"/>
/// corrects the price or the reading.
/// Each reader looks only at its page's own price block, never at the page as a whole, since a
/// similar-vehicles or search-style section further down carries other cars' "Dealer Fees Included"
/// badges and one car's page must not borrow another's statement.</summary>
public static class FeeStatements
{
    private const string CarsComNoExtraFees = "Seller has no extra fees";
    private const string CarsComAllInSentence = "The price shown here is the all-in total";
    private const string CarsComNotDisclosed = "Seller has not disclosed fees";
    private const string CarsComBreakdownHeading = "Vehicle price breakdown";
    private const string AutotraderListingPrice = "Listing Price";
    private const string AutotraderNoAdditionalFees = "No Additional Dealer Fees";
    private const string AutotraderTotalPrice = "Total Price";
    private const string AutotraderFeesIncluded = "Dealer Fees Included";

    private static readonly Regex Amount = new(@"^\$(?<amount>\d[\d,]*(?:\.\d{2})?)$", RegexOptions.Compiled);
    private static readonly Regex AddedAmount = new(@"^\+\s*\$(?<amount>\d[\d,]*(?:\.\d{2})?)$", RegexOptions.Compiled);
    private static readonly Regex TableHeader = new(@"^Description\s+Amount$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AllInTotalRow = new(@"^All-in total(?:\s+price)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>cars.com prints one of three things about a listing's fees. "Seller has no extra fees"
    /// with "The price shown here is the all-in total" says the price is all-in. A "Vehicle price
    /// breakdown" table lists the vehicle price, then one row per fee (Dealer Service Fee, Electronic
    /// Filing Fee, and so on), then an "All-in total price" row that equals the headline price: those
    /// fees are already in it, so the page is all-in (with the fee rows' sum kept), and only a table
    /// with fee rows and no total row is itemized. The generic taxes-and-registration disclaimer, or
    /// "Seller has not disclosed fees", is unknown. The all-in sentence is matched as the whole
    /// statement and not as the words "all-in total", because the undisclosed-fees notice says to
    /// "confirm the all-in total price directly with the seller". The first statement on the page is
    /// the listing's own, since similar listings come after it, so a later one is never read.</summary>
    public static FeeStatement ReadCarsCom(string pageText)
    {
        string[] lines = PageLines(pageText);
        int allIn = Array.FindIndex(lines, l => l.Equals(CarsComNoExtraFees, StringComparison.OrdinalIgnoreCase)
            || l.StartsWith(CarsComAllInSentence, StringComparison.OrdinalIgnoreCase));
        int undisclosed = Array.FindIndex(lines, l => l.Equals(CarsComNotDisclosed, StringComparison.OrdinalIgnoreCase));
        int heading = Array.FindIndex(lines, l => l.Equals(CarsComBreakdownHeading, StringComparison.OrdinalIgnoreCase));

        int own = new[] { allIn, undisclosed, heading }.Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        if (own < 0 || own == undisclosed)
        {
            return Unknown;
        }

        if (own == allIn)
        {
            return AllIn(null, null, null);
        }

        int row = heading + 1;
        if (row < lines.Length && TableHeader.IsMatch(lines[row]))
        {
            row++;
        }

        // The first row is the vehicle price itself; every row after it, up to the total, is a fee.
        decimal fees = 0m;
        decimal? vehiclePrice = null;
        for (; row + 1 < lines.Length && Amount.Match(lines[row + 1]) is { Success: true } amount; row += 2)
        {
            if (AllInTotalRow.IsMatch(lines[row]))
            {
                return AllIn(fees, vehiclePrice, Dollars(amount));
            }

            if (vehiclePrice is null)
            {
                vehiclePrice = Dollars(amount);
            }
            else
            {
                fees += Dollars(amount);
            }
        }

        return Itemized(fees, vehiclePrice, null);
    }

    /// <summary>Autotrader prints a "Listing Price" block on a detail page: the listing price, then
    /// either "No Additional Dealer Fees", or one "+ $amount" line per fee (Dealer Fee, Electronic
    /// Filing Fee, Private Tag Agency Fee, Pre-Delivery Service Fee, and so on) followed by a "Total
    /// Price" and the badge "Dealer Fees Included". Both statements mean the headline price already
    /// contains the fees, so the page is all-in (with the fee lines' sum kept); fee lines with no such
    /// statement are itemized; a block with neither, or no block at all (a search-style page some
    /// detail links land on, which lists other cars' badges), is unknown.</summary>
    public static FeeStatement ReadAutotrader(string pageText)
    {
        string[] lines = PageLines(pageText);
        int block = Array.FindIndex(lines, l => l.Equals(AutotraderListingPrice, StringComparison.OrdinalIgnoreCase));
        if (block < 0 || block + 1 >= lines.Length || Amount.Match(lines[block + 1]) is not { Success: true } listing)
        {
            return Unknown;
        }

        decimal listingPrice = Dollars(listing);
        decimal fees = 0m;
        int i = block + 2;
        while (i < lines.Length)
        {
            if (lines[i].Equals(AutotraderNoAdditionalFees, StringComparison.OrdinalIgnoreCase))
            {
                return AllIn(null, listingPrice, null);
            }

            if (lines[i].Equals(AutotraderTotalPrice, StringComparison.OrdinalIgnoreCase))
            {
                decimal? total = i + 1 < lines.Length && Amount.Match(lines[i + 1]) is { Success: true } totalAmount
                    ? Dollars(totalAmount)
                    : null;
                bool badgeFollowsTotal = i + 2 < lines.Length
                    && lines[i + 2].Equals(AutotraderFeesIncluded, StringComparison.OrdinalIgnoreCase);
                return badgeFollowsTotal
                    ? AllIn(fees, listingPrice, total)
                    : Itemized(fees, listingPrice, total);
            }

            if (i + 1 < lines.Length && AddedAmount.Match(lines[i + 1]) is { Success: true } added)
            {
                fees += Dollars(added);
                i += 2;
                continue;
            }

            break;
        }

        return Itemized(fees, listingPrice, null);
    }

    private static readonly FeeStatement Unknown = new(FeePostures.Unknown, null);

    private static FeeStatement AllIn(decimal? fees, decimal? listingPrice, decimal? totalPrice) =>
        new(FeePostures.AllIn, fees > 0m ? fees : null, listingPrice, totalPrice);

    private static FeeStatement Itemized(decimal fees, decimal? listingPrice, decimal? totalPrice) =>
        fees > 0m
            ? new FeeStatement(FeePostures.Itemized, fees, listingPrice, totalPrice)
            : Unknown;

    private static decimal Dollars(Match match) =>
        decimal.Parse(match.Groups["amount"].Value, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    /// <summary>The page's text as trimmed, non-blank lines: a recorded page pads its rows with blank
    /// and tab-only lines that carry no meaning.</summary>
    private static string[] PageLines(string pageText) =>
        [.. pageText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];
}
