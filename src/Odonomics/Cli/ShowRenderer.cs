using Odonomics.Ledger;
using Odonomics.Nhtsa;
using Spectre.Console;

namespace Odonomics.Cli;

public static class ShowRenderer
{
    public static void Render(VehicleEntity vehicle, VinDecodeResult decode, IReadOnlyList<RecallEntry> recalls, int complaintCount)
    {
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
        AnsiConsole.MarkupLine("[bold]Postings[/]");
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn("Source");
        table.AddColumn("First");
        table.AddColumn("Last");
        table.AddColumn("Price history");
        foreach (PostingEntity posting in vehicle.Postings)
        {
            string history = string.Join(" -> ", posting.PriceObservations
                .OrderBy(o => o.ObservedAt)
                .Select(o => Format.Money(o.Price)));
            table.AddRow(Format.Cell(posting.Source), posting.FirstSeen.ToString("yyyy-MM-dd"), posting.LastSeen.ToString("yyyy-MM-dd"), Format.Cell(history));
        }

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"[bold]Notes ({vehicle.Notes.Count})[/]");
        foreach (NoteEntity note in vehicle.Notes.OrderBy(n => n.CreatedAt))
        {
            AnsiConsole.MarkupLineInterpolated($"  [[{note.CreatedAt:yyyy-MM-dd}]] {note.Text}");
        }
    }
}
