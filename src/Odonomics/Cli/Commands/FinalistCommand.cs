using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

/// <summary>Marks a vehicle a finalist. Refuses unless the vehicle already carries a note
/// mentioning "PPI" and a note mentioning "Carfax" or "AutoCheck" (either can be the same note),
/// per the decision gate in the brief.</summary>
public static class FinalistCommand
{
    private static readonly Regex PpiWord = new(@"\bPPI\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HistoryReportWord = new(@"\b(Carfax|AutoCheck)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<int> RunAsync(string vin, CancellationToken cancellationToken)
    {
        using OdonomicsDbContext db = LedgerFactory.Open();

        VehicleEntity? vehicle = await db.Vehicles
            .Include(v => v.Notes)
            .FirstOrDefaultAsync(v => v.Vin == vin, cancellationToken);

        if (vehicle is null)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]no vehicle with VIN {vin} in the ledger[/]");
            return 1;
        }

        bool hasPpiNote = vehicle.Notes.Any(n => PpiWord.IsMatch(n.Text));
        bool hasHistoryReportNote = vehicle.Notes.Any(n => HistoryReportWord.IsMatch(n.Text));

        if (!hasPpiNote || !hasHistoryReportNote)
        {
            AnsiConsole.MarkupLine("[red]cannot mark finalist: needs a note mentioning \"PPI\" and a note mentioning \"Carfax\" or \"AutoCheck\"[/]");
            if (!hasPpiNote)
            {
                AnsiConsole.MarkupLine("  missing: a PPI note");
            }

            if (!hasHistoryReportNote)
            {
                AnsiConsole.MarkupLine("  missing: a Carfax or AutoCheck note");
            }

            return 1;
        }

        vehicle.FinalistMarkedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        AnsiConsole.MarkupLineInterpolated($"[bold green]{vin} marked finalist[/]");
        return 0;
    }
}
