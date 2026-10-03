using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
using Odonomics.Ledger;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

/// <summary>Records a window-sticker equipment reading for one VIN by hand, for a sticker read outside the walk
/// (a PDF, a photo). Each status given is stored with the manual-sticker source and the operator's text, and so
/// outranks the factory trim table the same way a walked sticker does. It makes no network call.</summary>
public static class EquipmentCommand
{
    /// <summary>The statuses `odo equipment set` accepts. "Unknown" is not one: a hand reading states a fact.</summary>
    public static readonly string[] AcceptedStatuses = ["present", "absent"];

    /// <summary>The parsed status for <paramref name="text"/> (present or absent, ignoring case), or null when it is
    /// neither, including null.</summary>
    public static EquipmentStatus? ParseStatus(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "present" => EquipmentStatus.Present,
        "absent" => EquipmentStatus.Absent,
        _ => null,
    };

    public static async Task<int> RunAsync(string vin, string? smartKeyEntry, string? pushButtonStart, string? keylessFobEntry, string? source, CancellationToken cancellationToken)
    {
        (string Option, string? Text)[] given =
        [
            ("--smart-key-entry", smartKeyEntry),
            ("--push-button-start", pushButtonStart),
            ("--keyless-fob-entry", keylessFobEntry),
        ];

        foreach ((string option, string? text) in given.Where(g => g.Text is not null))
        {
            if (ParseStatus(text) is null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{option} takes present or absent, not \"{text}\"[/]");
                return 1;
            }
        }

        if (given.All(g => g.Text is null))
        {
            AnsiConsole.MarkupLine("[red]give at least one of --smart-key-entry, --push-button-start, or --keyless-fob-entry[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            AnsiConsole.MarkupLine("[red]--source is required: say where the sticker was read, for example \"Toyota sticker PDF\"[/]");
            return 1;
        }

        using OdonomicsDbContext db = LedgerFactory.Open();
        bool found = await SetAsync(db, vin, ParseStatus(smartKeyEntry), ParseStatus(pushButtonStart), ParseStatus(keylessFobEntry), source, cancellationToken);
        if (!found)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]no vehicle with VIN {vin} in the ledger[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"equipment recorded for {vin}");
        return 0;
    }

    /// <summary>Stores each non-null status on the vehicle with <see cref="EquipmentSource.ManualSticker"/> and
    /// <paramref name="source"/> as its text, replacing whatever was stored for that feature, a walked sticker's
    /// reading included. A feature left null keeps its stored status. Returns false when the ledger has no
    /// vehicle with <paramref name="vin"/>.</summary>
    public static async Task<bool> SetAsync(OdonomicsDbContext db, string vin, EquipmentStatus? smartKeyEntry, EquipmentStatus? pushButtonStart, EquipmentStatus? keylessFobEntry, string source, CancellationToken cancellationToken)
    {
        VehicleEntity? vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Vin == vin, cancellationToken);
        if (vehicle is null)
        {
            return false;
        }

        string note = source.Trim();
        if (smartKeyEntry is EquipmentStatus smartKey)
        {
            vehicle.SmartKeyEntry = smartKey;
            vehicle.SmartKeyEntrySource = EquipmentSource.ManualSticker;
            vehicle.SmartKeyEntrySourceNote = note;
        }

        if (pushButtonStart is EquipmentStatus pushButton)
        {
            vehicle.PushButtonStart = pushButton;
            vehicle.PushButtonStartSource = EquipmentSource.ManualSticker;
            vehicle.PushButtonStartSourceNote = note;
        }

        if (keylessFobEntry is EquipmentStatus fob)
        {
            vehicle.KeylessFobEntry = fob;
            vehicle.KeylessFobEntrySource = EquipmentSource.ManualSticker;
            vehicle.KeylessFobEntrySourceNote = note;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
