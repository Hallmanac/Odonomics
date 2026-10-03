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
/// or factory equipment list, a window sticker read by hand with `odo equipment set` (the same authority as
/// a walked sticker, since it is a sticker too), then the factory trim table (see <see cref="FactoryTrimTable"/>).
/// A dealer's description text is deliberately not a source: it never confirms or rules out a feature.</summary>
public enum EquipmentSource
{
    None,
    WindowSticker,
    ManualSticker,
    TrimTable,
}

/// <summary>One equipment status with its source. An unknown status has no source. <paramref name="Detail"/>
/// is the operator's own words about where a manual sticker reading came from, and is null for every other source.</summary>
public readonly record struct EquipmentFact(EquipmentStatus Status, EquipmentSource Source, string? Detail = null)
{
    public static EquipmentFact Unknown => new(EquipmentStatus.Unknown, EquipmentSource.None);

    /// <summary>The status as `odo show` prints it, for example "absent (window sticker)" or "unknown".</summary>
    public string Text => Status == EquipmentStatus.Unknown
        ? "unknown"
        : $"{Status.ToString().ToLowerInvariant()} ({SourceText})";

    /// <summary>The source in words, for a reason or a printed status. A manual sticker reading carries the
    /// operator's text after the label, for example "sticker (manual): Toyota PDF".</summary>
    public string SourceText => Source switch
    {
        EquipmentSource.WindowSticker => "window sticker",
        EquipmentSource.ManualSticker => string.IsNullOrWhiteSpace(Detail)
            ? "sticker (manual)"
            : $"sticker (manual): {Detail}",
        EquipmentSource.TrimTable => "factory trim table",
        _ => "no source",
    };

    /// <summary>This fact when it is known, otherwise <paramref name="fallback"/>: the precedence rule, so a
    /// window sticker's answer is never replaced by a lower source's.</summary>
    public EquipmentFact OrElse(EquipmentFact fallback) => Status == EquipmentStatus.Unknown ? fallback : this;
}

/// <summary>The features a scenario can require (see <see cref="HardFilters.RequiredFeatures"/>),
/// named the way the scenario file and the notes spell them. "smart-key entry" is proximity entry only.
/// "keyless entry" is the looser reading, satisfied by either a remote keyless fob or proximity entry; it is
/// never stored itself but worked out from <see cref="SmartKeyEntry"/> and <see cref="KeylessFobEntry"/>.</summary>
public static class EquipmentFeatures
{
    public const string SmartKeyEntry = "smart-key entry";
    public const string KeylessEntry = "keyless entry";
    public const string PushButtonStart = "push-button start";

    /// <summary>A remote keyless fob (the "Keyless Entry" a plain sticker lists). It is tracked and stored so
    /// <see cref="KeylessEntry"/> can be worked out, but a scenario cannot require it on its own, so it is not
    /// in <see cref="All"/>.</summary>
    public const string KeylessFobEntry = "keyless fob entry";

    public static readonly IReadOnlyList<string> All = [SmartKeyEntry, KeylessEntry, PushButtonStart];

    /// <summary>The note for a required feature whose status is unknown, for example "confirm push-button start".</summary>
    public static string ConfirmNote(string feature) => $"confirm {feature}";

    /// <summary>The canonical spelling of <paramref name="name"/>, matched ignoring case, or null when it is
    /// not a feature this project tracks.</summary>
    public static string? Canonical(string name) =>
        All.FirstOrDefault(feature => string.Equals(feature, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A vehicle's equipment facts, after the trim table has filled what the window sticker left
/// unknown. <paramref name="KeylessFobEntry"/> defaults to unknown, which is also the default value.</summary>
public readonly record struct VehicleEquipment(EquipmentFact SmartKeyEntry, EquipmentFact PushButtonStart, EquipmentFact KeylessFobEntry = default)
{
    public static VehicleEquipment Unknown => new(EquipmentFact.Unknown, EquipmentFact.Unknown);

    /// <summary>Whether the car opens without a key in the lock, by a remote fob or by proximity. It is present
    /// when either is present, absent only when both are confirmed absent, and unknown otherwise, so a car
    /// whose smart key is absent but whose fob is not yet known stays unknown. The source is the highest
    /// ranked one among the facts that decided it.</summary>
    public EquipmentFact KeylessEntry => (SmartKeyEntry.Status, KeylessFobEntry.Status) switch
    {
        (EquipmentStatus.Present, EquipmentStatus.Present) => Higher(SmartKeyEntry, KeylessFobEntry),
        (EquipmentStatus.Present, _) => SmartKeyEntry,
        (_, EquipmentStatus.Present) => KeylessFobEntry,
        (EquipmentStatus.Absent, EquipmentStatus.Absent) => Higher(SmartKeyEntry, KeylessFobEntry),
        _ => EquipmentFact.Unknown,
    };

    /// <summary>The fact for a canonical feature name (see <see cref="EquipmentFeatures"/>).</summary>
    public EquipmentFact For(string feature) => feature switch
    {
        EquipmentFeatures.SmartKeyEntry => SmartKeyEntry,
        EquipmentFeatures.KeylessEntry => KeylessEntry,
        EquipmentFeatures.PushButtonStart => PushButtonStart,
        EquipmentFeatures.KeylessFobEntry => KeylessFobEntry,
        _ => EquipmentFact.Unknown,
    };

    /// <summary>Whichever fact has the higher-ranked source (a window sticker over the trim table).</summary>
    private static EquipmentFact Higher(EquipmentFact first, EquipmentFact second) =>
        first.Source <= second.Source
            ? first
            : second;
}
