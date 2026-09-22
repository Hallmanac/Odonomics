using Odonomics.Ledger;
using Odonomics.Marketcheck;
using Odonomics.Nhtsa;
using Spectre.Console;

namespace Odonomics.Cli;

public static class ShowRenderer
{
    public static void Render(VehicleEntity vehicle, VinResearchResult research, IReadOnlyList<string> redFlags)
    {
        (VinDecodeResult decode, IReadOnlyList<RecallEntry> recalls, int complaintCount, SafetyRatingsResult safety, VinHistoryResult history) = research;

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
        AnsiConsole.MarkupLineInterpolated($"[bold]Recalls, remedy status unknown ({recalls.Count})[/]");
        foreach (RecallEntry recall in recalls)
        {
            AnsiConsole.MarkupLineInterpolated($"  {recall.CampaignNumber} ({recall.ReportReceivedDate}): {recall.Component}");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"[bold]Complaints for model year {vehicle.Year}: {complaintCount}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]NHTSA safety ratings[/]");
        if (safety.ErrorText is not null)
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
                var historyTable = new Table { Border = TableBorder.Minimal };
                historyTable.Width(80);
                historyTable.AddColumn("Dealer");
                historyTable.AddColumn("First");
                historyTable.AddColumn("Last");
                historyTable.AddColumn("Price");
                historyTable.AddColumn("Miles");
                foreach (VinHistoryListing listing in history.PriorListings.OrderBy(l => l.FirstSeen))
                {
                    historyTable.AddRow(
                        Format.Cell(listing.Dealer ?? "(unknown)"),
                        listing.FirstSeen?.ToString("yyyy-MM-dd") ?? "?",
                        listing.LastSeen?.ToString("yyyy-MM-dd") ?? "?",
                        listing.Price is decimal price ? Format.Money(price) : "?",
                        listing.Mileage is int miles ? miles.ToString("N0") : "?");
                }

                AnsiConsole.Write(historyTable);
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
            foreach (string flag in redFlags)
            {
                AnsiConsole.MarkupLineInterpolated($"  [red]- {flag}[/]");
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
        return dealer.Grade == "F" && dealer.GradeReason is not null ? $"{label}: {dealer.GradeReason}" : label;
    }
}
