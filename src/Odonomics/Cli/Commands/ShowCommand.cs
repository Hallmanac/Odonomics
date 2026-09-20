using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;
using Odonomics.Nhtsa;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

public static class ShowCommand
{
    public static async Task<int> RunAsync(string vin, CancellationToken cancellationToken)
    {
        using OdonomicsDbContext db = LedgerFactory.Open();

        VehicleEntity? vehicle = await db.Vehicles
            .Include(v => v.Postings).ThenInclude(p => p.PriceObservations)
            .Include(v => v.Notes)
            .Include(v => v.VinRecord)
            .FirstOrDefaultAsync(v => v.Vin == vin, cancellationToken);

        if (vehicle is null)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]no vehicle with VIN {vin} in the ledger[/]");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var nhtsa = new NhtsaClient(http);

        VinDecodeResult decode = await nhtsa.DecodeVinAsync(vin, cancellationToken);
        IReadOnlyList<RecallEntry> recalls = await nhtsa.GetRecallsAsync(vehicle.Make, vehicle.Model, vehicle.Year, cancellationToken);
        int complaintCount = await nhtsa.GetComplaintCountAsync(vehicle.Make, vehicle.Model, vehicle.Year, cancellationToken);

        VinRecordEntity? record = vehicle.VinRecord;
        if (record is null)
        {
            record = new VinRecordEntity { Vin = vin, DecodedAt = DateTimeOffset.UtcNow, DecodeRawJson = JsonSerializer.Serialize(decode) };
            db.VinRecords.Add(record);
        }
        else
        {
            record.DecodedAt = DateTimeOffset.UtcNow;
            record.DecodeRawJson = JsonSerializer.Serialize(decode);
        }

        record.OpenRecallCount = recalls.Count;
        record.RecallsRawJson = JsonSerializer.Serialize(recalls);
        record.ComplaintCount = complaintCount;
        await db.SaveChangesAsync(cancellationToken);

        ShowRenderer.Render(vehicle, decode, recalls, complaintCount);
        return 0;
    }
}
