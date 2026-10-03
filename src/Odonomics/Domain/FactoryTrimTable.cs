using System.Text.Json;
using System.Text.RegularExpressions;

namespace Odonomics.Domain;

/// <summary>One row of the factory trim table: what the factory fitted to a model, over a range of model
/// years, in one trim. A feature the row does not state is left null and the row says nothing about it.</summary>
public sealed record FactoryTrimEntry
{
    public required string Make { get; init; }
    public required string Model { get; init; }
    public required int YearFrom { get; init; }
    public required int YearTo { get; init; }
    public required string Trim { get; init; }

    /// <summary>"present" or "absent" when the factory fitted, or did not fit, proximity entry on this trim.</summary>
    public string? SmartKeyEntry { get; init; }

    /// <summary>"present" or "absent" when the factory fitted, or did not fit, push-button start on this trim.</summary>
    public string? PushButtonStart { get; init; }

    /// <summary>"present" or "absent" when the factory fitted, or did not fit, a remote keyless fob on this trim.
    /// Proximity entry implies a fob works too, so a row only needs this where it says <see cref="SmartKeyEntry"/>
    /// is absent (a car with remote entry only).</summary>
    public string? KeylessFobEntry { get; init; }

    /// <summary>Where the row's answers come from, so each one can be checked later.</summary>
    public required string Source { get; init; }

    /// <summary>How well the source backs the row's answers ("high" or "medium" in the shipped file).</summary>
    public string? Confidence { get; init; }

    /// <summary>A caveat on the row, for example that the Smart Key covers only the driver's door.</summary>
    public string? Note { get; init; }

    internal bool Matches(string make, string model, int year, string trim) =>
        string.Equals(Make, make, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Model, model, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Trim, trim.Trim(), StringComparison.OrdinalIgnoreCase)
        && year >= YearFrom
        && year <= YearTo;

    internal string? StatedFor(string feature) => feature switch
    {
        EquipmentFeatures.SmartKeyEntry => SmartKeyEntry,
        EquipmentFeatures.KeylessFobEntry => KeylessFobEntry,
        _ => PushButtonStart,
    };
}

/// <summary>The factory trim table: a data file (<c>Data/factory-trim-equipment.json</c>, copied beside the
/// program) that says which models, model-year ranges, and trims came from the factory with smart-key
/// entry and push-button start. It ranks below a listing's own window sticker and above nothing: it only
/// fills a status the sticker left unknown, and only when the matching rows give a definite answer. Rows
/// that match one car but disagree about a feature give no answer for it, since a table that contradicts
/// itself is not definite. Its rows come from a sourced research note, and only rows that note rated high or medium are in it.</summary>
public sealed class FactoryTrimTable(IReadOnlyList<FactoryTrimEntry> entries)
{
    public const string FileName = "factory-trim-equipment.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly Regex DrivetrainSuffix = new(
        @"^(?<base>.+?)\s+(?:FWD|AWD-e|AWD|4WD|2WD)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static FactoryTrimTable Empty { get; } = new([]);

    public IReadOnlyList<FactoryTrimEntry> Entries => entries;

    /// <summary>The table shipped beside the program.</summary>
    public static FactoryTrimTable LoadShipped() =>
        Load(Path.Combine(AppContext.BaseDirectory, "Data", FileName));

    public static FactoryTrimTable Load(string path) => Parse(File.ReadAllText(path));

    public static FactoryTrimTable Parse(string json)
    {
        TableFile file = JsonSerializer.Deserialize<TableFile>(json, Options)
            ?? throw new JsonException("factory trim table deserialized to null");

        foreach (FactoryTrimEntry entry in file.Entries)
        {
            Validate(entry);
        }

        return new FactoryTrimTable(file.Entries);
    }

    /// <summary>What the table says about the car's smart-key entry, keyless fob, and push-button start. A car with no
    /// trim, or with no row for its model, year, and trim, gets unknown for both. When no row matches the trim as
    /// stored, a trailing drivetrain token is dropped and the lookup retried, so "Limited FWD" reads the "Limited" row;
    /// a row that matches the stored trim exactly (such as "LE AWD-e") always wins.</summary>
    public VehicleEquipment Lookup(string make, string model, int year, string? trim)
    {
        if (string.IsNullOrWhiteSpace(trim))
        {
            return VehicleEquipment.Unknown;
        }

        FactoryTrimEntry[] matches = MatchesFor(make, model, year, trim);
        if (matches.Length == 0 && StripDrivetrain(trim) is string baseTrim)
        {
            matches = MatchesFor(make, model, year, baseTrim);
        }

        return new VehicleEquipment(
            Definite(matches, EquipmentFeatures.SmartKeyEntry),
            Definite(matches, EquipmentFeatures.PushButtonStart),
            Definite(matches, EquipmentFeatures.KeylessFobEntry));
    }

    private FactoryTrimEntry[] MatchesFor(string make, string model, int year, string trim) =>
        [.. entries.Where(entry => entry.Matches(make, model, year, trim))];

    /// <summary>The trim without a trailing drivetrain token (FWD, AWD, AWD-e, 4WD, 2WD), or null when it has none
    /// or nothing would be left.</summary>
    private static string? StripDrivetrain(string trim)
    {
        Match match = DrivetrainSuffix.Match(trim.Trim());
        return match.Success
            ? match.Groups["base"].Value
            : null;
    }

    /// <summary>Fills each status of <paramref name="fromSticker"/> that is still unknown from the table.</summary>
    public VehicleEquipment Fill(VehicleEquipment fromSticker, string make, string model, int year, string? trim)
    {
        if (entries.Count == 0)
        {
            return fromSticker;
        }

        VehicleEquipment fromTable = Lookup(make, model, year, trim);
        return new VehicleEquipment(
            fromSticker.SmartKeyEntry.OrElse(fromTable.SmartKeyEntry),
            fromSticker.PushButtonStart.OrElse(fromTable.PushButtonStart),
            fromSticker.KeylessFobEntry.OrElse(fromTable.KeylessFobEntry));
    }

    private static EquipmentFact Definite(FactoryTrimEntry[] matches, string feature)
    {
        string[] stated = [.. matches.Select(m => m.StatedFor(feature)).OfType<string>().Select(s => s.Trim().ToLowerInvariant()).Distinct()];
        return stated switch
        {
            ["present"] => new EquipmentFact(EquipmentStatus.Present, EquipmentSource.TrimTable),
            ["absent"] => new EquipmentFact(EquipmentStatus.Absent, EquipmentSource.TrimTable),
            _ => EquipmentFact.Unknown,
        };
    }

    private static void Validate(FactoryTrimEntry entry)
    {
        string label = $"factory trim table row \"{entry.Make} {entry.Model} {entry.Trim}\"";
        if (string.IsNullOrWhiteSpace(entry.Make) || string.IsNullOrWhiteSpace(entry.Model) || string.IsNullOrWhiteSpace(entry.Trim))
        {
            throw new JsonException($"{label} needs a make, a model, and a trim");
        }

        if (entry.YearFrom is < 1000 or > 9999 || entry.YearTo is < 1000 or > 9999 || entry.YearFrom > entry.YearTo)
        {
            throw new JsonException($"{label} needs a four-digit yearFrom no later than its yearTo");
        }

        if (string.IsNullOrWhiteSpace(entry.Source))
        {
            throw new JsonException($"{label} needs a source for its answers");
        }

        foreach ((string name, string? value) in new[] { ("smartKeyEntry", entry.SmartKeyEntry), ("pushButtonStart", entry.PushButtonStart), ("keylessFobEntry", entry.KeylessFobEntry) })
        {
            if (value is not null && value.Trim().ToLowerInvariant() is not ("present" or "absent"))
            {
                throw new JsonException($"{label} has {name} \"{value}\", which is not \"present\" or \"absent\"");
            }
        }

        if (entry.SmartKeyEntry is null && entry.PushButtonStart is null && entry.KeylessFobEntry is null)
        {
            throw new JsonException($"{label} states none of smartKeyEntry, pushButtonStart, or keylessFobEntry");
        }
    }

    private sealed record TableFile
    {
        public IReadOnlyList<FactoryTrimEntry> Entries { get; init; } = [];
    }
}
