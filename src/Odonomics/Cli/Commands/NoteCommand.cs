using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

public static class NoteCommand
{
    public static async Task<int> RunAsync(string vin, string text, CancellationToken cancellationToken)
    {
        using OdonomicsDbContext db = LedgerFactory.Open();

        bool exists = await db.Vehicles.AnyAsync(v => v.Vin == vin, cancellationToken);
        if (!exists)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]no vehicle with VIN {vin} in the ledger[/]");
            return 1;
        }

        db.Notes.Add(new NoteEntity { VehicleVin = vin, Text = text, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);

        AnsiConsole.MarkupLineInterpolated($"note added for {vin}");
        return 0;
    }
}
