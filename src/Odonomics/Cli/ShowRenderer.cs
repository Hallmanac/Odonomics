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
    // width (a group whose mileage or price crosses from five digits to six, e.g.
    // "95,000-150,000", 14 characters, or "$95,000-$150,000", 16 characters); reserving that width
    // unconditionally starves the Dealer column, the one the grouping feature exists to make
    // readable, of space it needs far more. So price and mileage width are computed per render call
    // from what the actual groups need (floored at their header text's own length so the header
    // itself is never truncated, capped at the six-digit worst case so a wide range still survives),
    // and whatever they don't use goes to Dealer. Every cell is also truncated to its column's width
    // before it reaches the table, since Spectre wraps a cell that overflows its declared width onto
    // a second line rather than cropping it, which would turn one seller group's row into two lines
    // and defeat the "readable at a glance" point of grouping in the first place; DealerNamesCell
    // leads with the seller count for a multi-seller group specifically so that fact survives even
    // when the name list itself has to be cut short.
    private const int TableOverheadWidth = 16;
    private const int DateColumnWidth = 10;
    private const int PriceHeaderWidth = 11;
    private const int MileageHeaderWidth = 11;
    private const int PriceColumnMaxWidth = 16;
    private const int MileageColumnMaxWidth = 14;
    private const int MaxDealerNamesShown = 3;

    public static void Render(VehicleEntity vehicle, VinResearchResult research, IReadOnlyList<RedFlag> redFlags, bool allHistory)
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
                string prefix = Markup.Escape($"  {recall.CampaignNumber} ({recall.ReportReceivedDate}): {recall.Component} - ");
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
            AnsiConsole.MarkupLineInterpolated($"  days on market (current listing): {(history.CurrentListingDaysOnMarket is int dom ? dom.ToString("N0") : "(unknown)")}");
            if (history.PriorListings.Count == 0)
            {
                AnsiConsole.MarkupLine("  no prior listings on file");
            }
            else
            {
                IReadOnlyList<VinHistoryPoint> points = [.. history.PriorListings
                    .Select(l => new VinHistoryPoint(l.Dealer, l.FirstSeen, l.LastSeen, l.Price, l.Mileage))];
                IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
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
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn("Source");
        table.AddColumn("First");
        table.AddColumn("Last");
        table.AddColumn("Price history");
        table.AddColumn("Dealer");
        foreach (PostingEntity posting in vehicle.Postings)
        {
            string priceHistory = string.Join(" -> ", posting.PriceObservations
                .OrderBy(o => o.ObservedAt)
                .Select(o => Format.Money(o.Price)));
            table.AddRow(
                Format.Cell(posting.Source),
                posting.FirstSeen.ToString("yyyy-MM-dd"),
                posting.LastSeen.ToString("yyyy-MM-dd"),
                Format.Cell(priceHistory),
                Format.Cell(DealerCell(posting.Dealer)));
        }

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"[bold]Notes ({vehicle.Notes.Count})[/]");
        foreach (NoteEntity note in vehicle.Notes.OrderBy(n => n.CreatedAt))
        {
            AnsiConsole.MarkupLineInterpolated($"  [[{note.CreatedAt:yyyy-MM-dd}]] {note.Text}");
        }
    }

    /// <summary>The listing history grouped by seller (see <see cref="RedFlagsEvaluator.GroupBySeller"/>),
    /// one row per group: dealer name(s), led by the seller count for a multi-seller group and capped
    /// at <see cref="MaxDealerNamesShown"/> names plus "and N more" (see <see cref="DealerNamesCell"/>),
    /// the group's overall first/last seen dates, and its price and mileage ranges. Built without
    /// writing it, so a rendering test can capture it against a fixed-width console instead of the
    /// real one, the same as <c>WalkCommand.BuildSummaryTable</c>.</summary>
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
        table.AddColumn(new TableColumn("Dealer(s)") { Width = dealerColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("First") { Width = DateColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Last") { Width = DateColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Price range") { Width = priceColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Miles range") { Width = mileageColumnWidth, NoWrap = true });
        foreach (SellerGroupSummary group in groups)
        {
            table.AddRow(
                Format.Cell(Format.Truncate(DealerNamesCell(group.DealerNames), dealerColumnWidth)),
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
    /// spans) survives <see cref="Format.Truncate"/> even when the name list itself has to be cut
    /// short to fit the dealer column's width: truncation always cuts from the end, so whatever
    /// sits at the front of the string is what the column-width-limited cell keeps.</summary>
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
    /// dealer is known or it hasn't been checked yet, "Name (Grade)" once checked, and the F
    /// reason appended when the grade is F.</summary>
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
        return dealer.Grade is not null && dealer.Grade.StartsWith('F') && dealer.GradeReason is not null
            ? $"{label}: {dealer.GradeReason}"
            : label;
    }
}
