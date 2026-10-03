namespace Odonomics.Domain;

/// <summary>What is known about whether a car has one piece of equipment. Unknown is the default and
/// means nobody has confirmed it either way; it is never read as absent.</summary>
public enum EquipmentStatus
{
    Unknown,
    Present,
    Absent,
}

/// <summary>Where an equipment status came from, in order of precedence: the listing's own window sticker
/// or factory equipment list, then the factory trim table (see <see cref="FactoryTrimTable"/>). A dealer's
/// description text is deliberately not a source: it never confirms or rules out a feature.</summary>
public enum EquipmentSource
{
    None,
    WindowSticker,
    TrimTable,
}

/// <summary>One equipment status with its source. An unknown status has no source.</summary>
public readonly record struct EquipmentFact(EquipmentStatus Status, EquipmentSource Source)
{
    public static EquipmentFact Unknown => new(EquipmentStatus.Unknown, EquipmentSource.None);

    /// <summary>The status as `odo show` prints it, for example "absent (window sticker)" or "unknown".</summary>
    public string Text => Status == EquipmentStatus.Unknown
        ? "unknown"
        : $"{Status.ToString().ToLowerInvariant()} ({SourceText})";

    /// <summary>The source in words, for a reason or a printed status.</summary>
    public string SourceText => Source switch
    {
        EquipmentSource.WindowSticker => "window sticker",
        EquipmentSource.TrimTable => "factory trim table",
        _ => "no source",
    };

    /// <summary>This fact when it is known, otherwise <paramref name="fallback"/>: the precedence rule, so a
    /// window sticker's answer is never replaced by a lower source's.</summary>
    public EquipmentFact OrElse(EquipmentFact fallback) => Status == EquipmentStatus.Unknown ? fallback : this;
}

/// <summary>The two features a scenario can require (see <see cref="HardFilters.RequiredFeatures"/>),
/// named the way the scenario file and the notes spell them.</summary>
public static class EquipmentFeatures
{
    public const string SmartKeyEntry = "smart-key entry";
    public const string PushButtonStart = "push-button start";

    public static readonly IReadOnlyList<string> All = [SmartKeyEntry, PushButtonStart];

    /// <summary>The note for a required feature whose status is unknown, for example "confirm push-button start".</summary>
    public static string ConfirmNote(string feature) => $"confirm {feature}";

    /// <summary>The canonical spelling of <paramref name="name"/>, matched ignoring case, or null when it is
    /// not a feature this project tracks.</summary>
    public static string? Canonical(string name) =>
        All.FirstOrDefault(feature => string.Equals(feature, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A vehicle's two equipment facts, after the trim table has filled what the window sticker left
/// unknown.</summary>
public readonly record struct VehicleEquipment(EquipmentFact SmartKeyEntry, EquipmentFact PushButtonStart)
{
    public static VehicleEquipment Unknown => new(EquipmentFact.Unknown, EquipmentFact.Unknown);

    /// <summary>The fact for a canonical feature name (see <see cref="EquipmentFeatures"/>).</summary>
    public EquipmentFact For(string feature) => feature switch
    {
        EquipmentFeatures.SmartKeyEntry => SmartKeyEntry,
        EquipmentFeatures.PushButtonStart => PushButtonStart,
        _ => EquipmentFact.Unknown,
    };
}
