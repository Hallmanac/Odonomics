using System.Globalization;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Marketcheck;
using Odonomics.Nhtsa;
using Spectre.Console;

namespace Odonomics.Cli;

public static class ShowRenderer
{
    // Column widths add up, with Border.Minimal's per-column padding and separators (3 chars per
    // column plus 1 for the table's own edges: 3 * 5 + 1 = 16 for five columns), to exactly 80:
    // 16 + DealerWidth + 10 + 10 + PriceWidth + MileageWidth = 80. The date columns are fixed
    // ("yyyy-MM-dd" is always 10 characters), but price and mileage rarely need their worst-case
    // width (a group whose mileage or price has six digits at both ends, e.g.
    // "105,000-150,000", 15 characters, or "$105,000-$150,000", 17 characters); reserving that width
    // unconditionally starves the Dealer column, the one the grouping feature exists to make
    // readable, of space it needs far more. So price and mileage width are computed per render call
    // from what the actual groups need (floored at their short header's own length so the header
    // itself is never truncated, capped at the six-digit worst case so a wide range still survives),
    // and whatever they don't use goes to Dealer. The price and mileage cells are truncated to their
    // column's width before they reach the table, since Spectre wraps a cell that overflows its
    // declared width onto a second line rather than cropping it; the date cells need no truncation
    // because an ISO date is always exactly the width they reserve. The Dealer cell is the exception:
    // it is never truncated, because cutting a seller group's names short hides the very thing the
    // grouping exists to show. It wraps instead, so a long name list continues on lines under the
    // group's row while the other columns stay on the first line; DealerNamesCell still leads with the
    // seller count for a multi-seller group so that fact sits at the front of the cell.
    private const int TableOverheadWidth = 16;
    private const int DateColumnWidth = 10;
    private const int PriceHeaderWidth = 5;
    private const int MileageHeaderWidth = 5;
    private const int PriceColumnMaxWidth = 17;
    private const int MileageColumnMaxWidth = 15;
    private const int MaxDealerNamesShown = 3;
    private const int CurrentListingMaxIdleDays = 14;

    public static void Render(
        VehicleEntity vehicle,
        VinResearchResult research,
        IReadOnlyList<RedFlag> redFlags,
        bool allHistory,
        CostBreakdown? monthlyCost,
        string? monthlyCostUnavailable,
        PurchasePrice? purchasePrice = null)
    {
        (VinDecodeResult decode, RecallsResult recalls, ComplaintsResult complaints, SafetyRatingsResult safety, VinHistoryResult history) = research;

        AnsiConsole.MarkupLineInterpolated($"[bold]{vehicle.Year} {vehicle.Make} {vehicle.Model} {vehicle.Trim}[/] ({vehicle.Vin})");
        AnsiConsole.MarkupLineInterpolated($"mileage: {vehicle.Mileage:N0}");
        AnsiConsole.MarkupLine(vehicle.FinalistMarkedAt is not null ? "[bold green]finalist[/]" : "not a finalist");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]NHTSA vPIC decode[/]");
        AnsiConsole.MarkupLineInterpolated($"  body class: {decode.BodyClass ?? "(unknown)"}");
        AnsiConsole.MarkupLineInterpolated($"  engine cylinders: {decode.EngineCylinders ?? "(unknown)"}");
        if (decode.ErrorText is not null)
        {
            AnsiConsole.MarkupLineInterpolated($"  decode note: {decode.ErrorText}");
        }

        AnsiConsole.WriteLine();
        if (recalls.CouldNotFetchReason is not null)
        {
            AnsiConsole.MarkupLine("[bold]Recalls[/]");
            AnsiConsole.MarkupLineInterpolated($"  could not fetch: {recalls.CouldNotFetchReason}");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"[bold]Recalls ({recalls.Entries.Count})[/]");
            foreach (RecallEntry recall in recalls.Entries)
            {
                // Markup.Escape, not MarkupLineInterpolated, for the data half: remedyClause carries
                // real [red]...[/] markup that MarkupLineInterpolated would otherwise auto-escape
                // into literal brackets if it were passed as an interpolated argument.
                string remedyClause = recall.RemedyAvailable
                    ? "remedy available"
                    : "[red]no remedy yet[/]";
                string prefix = Markup.Escape($"  {recall.CampaignNumber} ({RecallDate(recall.ReportReceivedDate)}): {recall.Component} - ");
                AnsiConsole.MarkupLine($"{prefix}{remedyClause}");
            }
        }

        AnsiConsole.WriteLine();
        if (complaints.CouldNotFetchReason is not null)
        {
            AnsiConsole.MarkupLine("[bold]Complaints[/]");
            AnsiConsole.MarkupLineInterpolated($"  could not fetch: {complaints.CouldNotFetchReason}");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"[bold]Complaints for model year {vehicle.Year}: {complaints.Count}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]NHTSA safety ratings[/]");
        if (safety.CouldNotFetchReason is not null)
        {
            AnsiConsole.MarkupLineInterpolated($"  could not fetch: {safety.CouldNotFetchReason}");
        }
        else if (safety.ErrorText is not null)
        {
            AnsiConsole.MarkupLineInterpolated($"  {safety.ErrorText}");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"  overall: {Stars(safety.OverallRating)}");
            AnsiConsole.MarkupLineInterpolated($"  front crash: {Stars(safety.FrontRating)}");
            AnsiConsole.MarkupLineInterpolated($"  side crash: {Stars(safety.SideRating)}");
            AnsiConsole.MarkupLineInterpolated($"  rollover: {Stars(safety.RolloverRating)}");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Marketcheck VIN history[/]");
        if (history.CouldNotFetchReason is not null)
        {
            AnsiConsole.MarkupLineInterpolated($"  could not fetch: {history.CouldNotFetchReason}");
        }
        else
        {
            IReadOnlyList<VinHistoryPoint> points = [.. history.PriorListings
                .Select(l => new VinHistoryPoint(l.Dealer, l.FirstSeen, l.LastSeen, l.Price, l.Mileage))];
            IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            AnsiConsole.MarkupLineInterpolated($"  days on market (current listing): {DaysOnMarket(history.CurrentListingDaysOnMarket, groups, now)}");
            if (history.PriorListings.Count == 0)
            {
                AnsiConsole.MarkupLine("  no prior listings on file");
            }
            else
            {
                int undated = points.Count(p => p.FirstSeen is null);
                if (groups.Count == 0)
                {
                    AnsiConsole.MarkupLine(allHistory
                        ? "  no prior listings have a known first-seen date"
                        : "  no prior listings have a known first-seen date; pass --all-history to see them");
                }
                else
                {
                    AnsiConsole.Write(BuildGroupedHistoryTable(groups));
                    if (undated > 0 && !allHistory)
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"  {undated} listing{(undated == 1 ? "" : "s")} without a known first-seen date not shown above; pass --all-history to see {(undated == 1 ? "it" : "them")}");
                    }
                }

                if (allHistory)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("  all listings:");
                    AnsiConsole.Write(BuildRawHistoryTable(history.PriorListings));
                }
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"[bold]Red flags ({redFlags.Count})[/]");
        if (redFlags.Count == 0)
        {
            AnsiConsole.MarkupLine("  none found");
        }
        else
        {
            foreach (RedFlag flag in redFlags)
            {
                AnsiConsole.MarkupLineInterpolated($"  [red]- {flag.Detail}[/]");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Postings[/]");
        AnsiConsole.Write(BuildPostingsTable(vehicle.Postings));
        foreach (string line in PostingAttributeLines(vehicle.Postings))
        {
            AnsiConsole.WriteLine(line);
        }

        AnsiConsole.WriteLine();
        RenderMonthlyCost(AnsiConsole.Console, monthlyCost, monthlyCostUnavailable, purchasePrice);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"[bold]Notes ({vehicle.Notes.Count})[/]");
        foreach (NoteEntity note in vehicle.Notes.OrderBy(n => n.CreatedAt))
        {
            AnsiConsole.MarkupLineInterpolated($"  [[{note.CreatedAt:yyyy-MM-dd}]] {note.Text}");
        }
    }

    private const int MonthlyCostLabelWidth = 18;

    /// <summary>Itemizes what the scenario says one vehicle costs a month: the loan payment and each
    /// running cost as the scenario's expected value (or range, when an input feeding it is loose),
    /// then the during-loan total and the ten-year average. Every figure is read from the same
    /// <see cref="CostBreakdown"/> `odo rank` prints, so a "$592-$627" during-loan figure can be
    /// seen to be a payment plus running costs, not a car payment. When the scenario cannot price the
    /// vehicle, says why in place of the figures. When the vehicle's posting shows a shipping fee, a
    /// pickup option or itemized fees, each sits on its own line under the asking price, both fees when
    /// both are known, and the purchase price they add up to closes the group with the fulfillment the
    /// analysis assumed ("with delivery" or "with pickup"), since that price is what the figures are
    /// computed from; when the posting's fee posture was read, a last line says what it is. A vehicle
    /// with none of these prints none of those lines. Written line by line rather than as a table so
    /// nothing wraps at 80 columns.</summary>
    public static void RenderMonthlyCost(IAnsiConsole console, CostBreakdown? cost, string? unavailableReason, PurchasePrice? purchasePrice = null)
    {
        console.MarkupLine("[bold]Monthly cost[/]");
        if (purchasePrice is { } price && (price.ShippingFee is not null || price.PickupFee is not null || price.ItemizedFees is not null || price.FeePosture is not null))
        {
            console.MarkupLine($"  {"Asking price".PadRight(MonthlyCostLabelWidth)}{Format.Money(price.Asking)}");
            if (price.ShippingFee is decimal shippingFee)
            {
                console.MarkupLine($"  {"Shipping fee".PadRight(MonthlyCostLabelWidth)}{Format.Money(shippingFee)}");
            }

            if (price.PickupFee is decimal pickupFee)
            {
                string place = string.IsNullOrWhiteSpace(price.PickupLocation) ? "" : $" at {price.PickupLocation}";
                console.MarkupLineInterpolated($"  {"Pickup fee".PadRight(MonthlyCostLabelWidth)}{Format.Money(pickupFee)}{place}");
            }
            else if (price.PickupFeeAssumed)
            {
                console.MarkupLine($"  {"Pickup fee".PadRight(MonthlyCostLabelWidth)}not read, so the shipping fee is assumed");
            }

            if (price.ItemizedFees is decimal itemizedFees)
            {
                console.MarkupLine($"  {"Itemized fees".PadRight(MonthlyCostLabelWidth)}{Format.Money(itemizedFees)}");
            }

            if (price.AppliedFee is not null || price.ItemizedFees is not null)
            {
                string fulfillment = price.AppliedFee is null
                    ? ""
                    : $" with {price.Fulfillment.Word()}";
                console.MarkupLine($"  {"Purchase price".PadRight(MonthlyCostLabelWidth)}{Format.Money(price.Total)}{fulfillment}");
            }

            if (price.FeePosture is string posture)
            {
                console.MarkupLine($"  {"Fee posture".PadRight(MonthlyCostLabelWidth)}{FeePostureText(posture, price.IncludedFees)}");
            }
        }

        if (cost is null)
        {
            console.MarkupLineInterpolated($"  not computed: {unavailableReason ?? "no cost available"}");
            return;
        }

        foreach (RoundedLine line in Format.DuringLoanLines(cost))
        {
            console.MarkupLine($"  {line.Label.PadRight(MonthlyCostLabelWidth)}{line.Figure}");
        }

        console.MarkupLine($"  {"During-loan total".PadRight(MonthlyCostLabelWidth)}{Format.Band(cost.DuringLoanMonthly)}");
        console.MarkupLine($"  {"10-year average".PadRight(MonthlyCostLabelWidth)}{Format.Band(cost.TenYearAverageMonthly)}");
    }

    /// <summary>What a fee posture means for the price above it, in a few words: the price holds the
    /// fees (naming how much of the price they are, when the page listed them), the fees come on top of
    /// it (and are the itemized line), or the page did not say.</summary>
    private static string FeePostureText(string posture, decimal? includedFees) => posture switch
    {
        FeePostures.AllIn when includedFees is decimal included => $"all-in ({Format.Money(included)} of fees are in the price)",
        FeePostures.AllIn => "all-in (fees are in the price)",
        FeePostures.Itemized => "itemized (fees are on top)",
        FeePostures.Unknown => "unknown (the page did not say)",
        _ => posture,
    };

    /// <summary>The ledger's own postings for one vehicle, one row per posting with its source,
    /// first and last seen dates, price history, and dealer. Built without writing it, so a
    /// rendering test can capture it against a fixed-width console, the same as
    /// <see cref="BuildGroupedHistoryTable"/>.</summary>
    public static Table BuildPostingsTable(IEnumerable<PostingEntity> postings)
    {
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn("Source");
        table.AddColumn("First");
        table.AddColumn("Last");
        table.AddColumn("Price history");
        table.AddColumn("Dealer");
        foreach (PostingEntity posting in postings)
        {
            string priceHistory = PriceHistory([.. posting.PriceObservations.OrderBy(o => o.ObservedAt).Select(o => o.Price)]);
            table.AddRow(
                Format.Cell(posting.Source),
                posting.FirstSeen.ToString("yyyy-MM-dd"),
                posting.LastSeen.ToString("yyyy-MM-dd"),
                Format.Cell(priceHistory),
                Format.Cell(DealerCell(posting.Dealer)));
        }

        return table;
    }

    /// <summary>One line per name and value of each posting's attributes (see
    /// <see cref="PostingAttributeEntity"/>), led by the posting's source so a line says which site
    /// showed it, in posting order and then name order; empty when no posting has any.</summary>
    public static IReadOnlyList<string> PostingAttributeLines(IEnumerable<PostingEntity> postings) =>
        [.. postings.SelectMany(p => p.Attributes.OrderBy(a => a.Name, StringComparer.Ordinal).Select(a => $"  {p.Source} {a.Name}: {a.Value}"))];

    /// <summary>A recall's report date as yyyy-MM-dd. NHTSA's recallsByVehicle endpoint reports it as
    /// dd/MM/yyyy text; anything that does not parse as that (an ISO date already, or a blank) is
    /// returned unchanged rather than guessed at.</summary>
    public static string RecallDate(string reportReceivedDate) =>
        DateTime.TryParseExact(reportReceivedDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : reportReceivedDate;

    /// <summary>A posting's observed prices, oldest first, joined with arrows; consecutive equal
    /// observations collapse to one, since "$22,489 -> $22,489" only says the price did not move.</summary>
    public static string PriceHistory(IReadOnlyList<decimal> pricesOldestFirst)
    {
        List<decimal> changes = [];
        foreach (decimal price in pricesOldestFirst)
        {
            if (changes.Count == 0 || changes[^1] != price)
            {
                changes.Add(price);
            }
        }

        return string.Join(" -> ", changes.Select(Format.Money));
    }

    /// <summary>Days on market for the current listing: Marketcheck's own figure when it reported one,
    /// otherwise the days since the current seller group (the group whose window ends last) began its
    /// most recent unbroken run of sightings (see <see cref="SellerGroupSummary.LatestRunStart"/>, so a
    /// relisting months after an earlier listing by the same dealer counts from the relisting), and
    /// "(unknown)" when neither exists. Marketcheck reports no figure both for a listing that carries
    /// no days on market and for a VIN that is not listed at all, so the fallback only applies when
    /// that group was last seen within <see cref="CurrentListingMaxIdleDays"/> days of
    /// <paramref name="now"/>; a group last seen longer ago is a past listing, not the current one.
    /// Days are counted between calendar dates in each sighting's own offset, the same dates the
    /// grouped table prints.</summary>
    public static string DaysOnMarket(int? reportedDays, IReadOnlyList<SellerGroupSummary> groups, DateTimeOffset now)
    {
        if (reportedDays is int reported)
        {
            return reported.ToString("N0");
        }

        SellerGroupSummary? current = groups.MaxBy(g => g.LastSeen);
        return current is null || DaysSince(current.LastSeen, now) > CurrentListingMaxIdleDays
            ? "(unknown)"
            : Math.Max(0, DaysSince(current.LatestRunStart, now)).ToString("N0");
    }

    private static int DaysSince(DateTimeOffset seen, DateTimeOffset now) =>
        (now.ToOffset(seen.Offset).Date - seen.Date).Days;

    /// <summary>The listing history grouped by seller (see <see cref="RedFlagsEvaluator.GroupBySeller"/>),
    /// one row per group: dealer name(s), led by the seller count for a multi-seller group and capped
    /// at <see cref="MaxDealerNamesShown"/> names plus "and N more" (see <see cref="DealerNamesCell"/>)
    /// and wrapped, never truncated, within its column, the group's overall first/last seen dates, and
    /// its price and mileage ranges. Built without writing it, so a rendering test can capture it
    /// against a fixed-width console instead of the real one, the same as
    /// <c>WalkCommand.BuildSummaryTable</c>.</summary>
    public static Table BuildGroupedHistoryTable(IReadOnlyList<SellerGroupSummary> groups)
    {
        int priceColumnWidth = groups.Count == 0
            ? PriceHeaderWidth
            : Math.Clamp(groups.Max(g => RangeCell(g.MinPrice, g.MaxPrice, Format.Money).Length), PriceHeaderWidth, PriceColumnMaxWidth);
        int mileageColumnWidth = groups.Count == 0
            ? MileageHeaderWidth
            : Math.Clamp(groups.Max(g => RangeCell(g.MinMileage, g.MaxMileage, m => m.ToString("N0")).Length), MileageHeaderWidth, MileageColumnMaxWidth);
        int dealerColumnWidth = 80 - TableOverheadWidth - (2 * DateColumnWidth) - priceColumnWidth - mileageColumnWidth;

        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn(new TableColumn("Dealer(s)") { Width = dealerColumnWidth });
        table.AddColumn(new TableColumn("First") { Width = DateColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Last") { Width = DateColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Price") { Width = priceColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Miles") { Width = mileageColumnWidth, NoWrap = true });
        foreach (SellerGroupSummary group in groups)
        {
            table.AddRow(
                Format.Cell(DealerNamesCell(group.DealerNames)),
                group.FirstSeen.ToString("yyyy-MM-dd"),
                group.LastSeen.ToString("yyyy-MM-dd"),
                Format.Cell(Format.Truncate(RangeCell(group.MinPrice, group.MaxPrice, Format.Money), priceColumnWidth)),
                Format.Cell(Format.Truncate(RangeCell(group.MinMileage, group.MaxMileage, m => m.ToString("N0")), mileageColumnWidth)));
        }

        return table;
    }

    /// <summary>The raw, one-row-per-sighting history table (every prior listing exactly as
    /// Marketcheck reported it), shown only with `odo show --all-history`: the grouped table above is
    /// the readable default, this is the detail underneath it.</summary>
    public static Table BuildRawHistoryTable(IReadOnlyList<VinHistoryListing> priorListings)
    {
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn("Dealer");
        table.AddColumn("First");
        table.AddColumn("Last");
        table.AddColumn("Price");
        table.AddColumn("Miles");
        foreach (VinHistoryListing listing in priorListings.OrderBy(l => l.FirstSeen))
        {
            table.AddRow(
                Format.Cell(listing.Dealer ?? "(unknown)"),
                listing.FirstSeen?.ToString("yyyy-MM-dd") ?? "?",
                listing.LastSeen?.ToString("yyyy-MM-dd") ?? "?",
                listing.Price is decimal price ? Format.Money(price) : "?",
                listing.Mileage is int miles ? miles.ToString("N0") : "?");
        }

        return table;
    }

    /// <summary>Leads a multi-seller group's cell with its seller count ("N sellers: ...") before the
    /// capped name list, so the one fact this table exists to surface (how many rooftops a group
    /// spans) is the first thing in the cell. The cell is never truncated; the dealer column wraps it.</summary>
    private static string DealerNamesCell(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return "(unknown)";
        }

        if (names.Count == 1)
        {
            return names[0];
        }

        string shown = string.Join(", ", names.Take(MaxDealerNamesShown));
        int remaining = names.Count - Math.Min(MaxDealerNamesShown, names.Count);
        string list = remaining > 0 ? $"{shown} and {remaining} more" : shown;
        return $"{names.Count} sellers: {list}";
    }

    private static string RangeCell<T>(T? min, T? max, Func<T, string> format) where T : struct, IEquatable<T>
    {
        if (min is not T lo || max is not T hi)
        {
            return "?";
        }

        return lo.Equals(hi) ? format(lo) : $"{format(lo)}-{format(hi)}";
    }

    private static string Stars(int? rating) => rating is int stars ? $"{stars}/5" : "(not rated)";

    /// <summary>The dealer name plus grade for one posting's "Dealer" cell: bare name when no
    /// dealer is known or it hasn't been checked yet, "Name (Grade)" once checked, followed by the
    /// doc fee and the add-ons note when the dealer has them, and the F reason appended when the
    /// grade is F.</summary>
    private static string DealerCell(DealerEntity? dealer)
    {
        if (dealer is null)
        {
            return "-";
        }

        if (dealer.GradeCheckedAt is null)
        {
            return dealer.Name;
        }

        string label = dealer.Grade is null ? $"{dealer.Name} (ungraded)" : $"{dealer.Name} ({dealer.Grade})";
        if (DealerFeeText(dealer) is string feeText)
        {
            label = $"{label}, {feeText}";
        }

        return dealer.Grade is not null && dealer.Grade.StartsWith('F') && dealer.GradeReason is not null
            ? $"{label}: {dealer.GradeReason}"
            : label;
    }

    /// <summary>The dealer's doc fee and add-ons note as CarEdge printed them ("doc fee $1,199, No
    /// add-ons"), whichever of the two are known, or null when neither is.</summary>
    private static string? DealerFeeText(DealerEntity dealer)
    {
        string[] parts = [.. new[]
        {
            dealer.DocFee is decimal docFee ? $"doc fee {Format.Money(docFee)}" : null,
            dealer.AddOnsNote,
        }.OfType<string>()];
        return parts.Length == 0 ? null : string.Join(", ", parts);
    }
}
