using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

/// <summary>Marks a vehicle a finalist. Refuses unless the vehicle already carries a note
/// mentioning "PPI" and a note mentioning "Carfax" or "AutoCheck" (either can be the same note),
/// per the decision gate in the brief.</summary>
public static class FinalistCommand
{
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

        bool hasPpiNote = vehicle.Notes.Any(n => n.Text.Contains("PPI", StringComparison.OrdinalIgnoreCase));
        bool hasHistoryReportNote = vehicle.Notes.Any(n =>
            n.Text.Contains("Carfax", StringComparison.OrdinalIgnoreCase) || n.Text.Contains("AutoCheck", StringComparison.OrdinalIgnoreCase));

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
