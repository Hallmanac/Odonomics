using System.Text.RegularExpressions;
using Odonomics.Domain;

namespace Odonomics.Extraction;

/// <summary>Guards what the extraction says about smart-key entry, a keyless fob and push-button start. The
/// extraction is told to read those only from a window sticker or factory equipment list and never from a dealer's
/// description, but a model can still misread or be steered by the page, so a claim survives only when the
/// page text itself carries the evidence for it. A claim the page cannot back is dropped to null (unknown),
/// never flipped to the opposite status.</summary>
public static partial class WindowStickerEquipment
{
    /// <summary>How much of the text after the cut a sticker excerpt may add, so a page that runs long still
    /// shows the extraction its sticker.</summary>
    private const int ExcerptChars = 3_000;

    private const int ExcerptLeadChars = 200;

    /// <summary>How far past a sticker marker its equipment list is taken to run when looking for evidence, so
    /// a dealer's description elsewhere on the page cannot stand in for the sticker's own wording.</summary>
    private const int StickerSectionChars = 2_500;

    /// <summary>A heading that marks a section as the factory's own statement of equipment. It has to be the
    /// whole line, since the same words run through dealer prose ("options in addition to the standard
    /// equipment", "any equipment listed") and in links such as "View Window Sticker".</summary>
    [GeneratedRegex(@"^[ \t]*(?:window\s+sticker|monroney(?:\s+label)?|factory\s+equipment(?:\s+list)?)[ \t]*:?[ \t]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex StickerMarker();

    /// <summary>What a sticker or equipment list says when the car has proximity entry.</summary>
    [GeneratedRegex(@"smart[\s-]?key|proximity|intelligent\s+key|passive\s+entry|keyless\s+access|smart\s+entry", RegexOptions.IgnoreCase)]
    private static partial Regex SmartKeyTerm();

    /// <summary>What a sticker or equipment list says when the car has push-button start; a Toyota "Smart Key
    /// System" is that make's name for proximity entry with push-button start.</summary>
    [GeneratedRegex(@"push[\s-]?button|push[\s-]?to[\s-]?start|keyless\s+(?:start|ignition)|engine\s+start[\s/-]?stop|start/stop\s+button|smart[\s-]?key\s+system", RegexOptions.IgnoreCase)]
    private static partial Regex PushButtonTerm();

    /// <summary>What a sticker or equipment list says when the car has only a fob and a turn key. A sticker
    /// that lists this shows the car lacks proximity entry; it says nothing about push-button start.</summary>
    [GeneratedRegex(@"keyless\s+entry|remote\s+entry|key\s*fob|turn[\s-]?key|keyed\s+ignition", RegexOptions.IgnoreCase)]
    private static partial Regex FobTerm();

    /// <summary>What a sticker or equipment list says when the car has a remote keyless fob.</summary>
    [GeneratedRegex(@"keyless\s+entry|remote\s+(?:keyless\s+)?entry|key\s*fob|remote\s+key", RegexOptions.IgnoreCase)]
    private static partial Regex FobPresentTerm();

    /// <summary>What a sticker or equipment list says when the car starts with a turn key.</summary>
    [GeneratedRegex(@"turn[\s-]?key|keyed\s+ignition", RegexOptions.IgnoreCase)]
    private static partial Regex KeyedIgnitionTerm();

    /// <summary>A sticker that states push-button start is missing, such as "No push button start".</summary>
    [GeneratedRegex(@"\b(?:no|without|lacks?|not\s+available)\s+(?:a\s+)?(?:push[\s-]?button(?:\s+start)?|push[\s-]?to[\s-]?start|keyless\s+(?:start|ignition))", RegexOptions.IgnoreCase)]
    private static partial Regex NoPushButtonStatement();

    /// <summary>A sticker that states a remote keyless fob is missing, such as "No keyless entry".</summary>
    [GeneratedRegex(@"\b(?:no|without|lacks?|not\s+available)\s+(?:a\s+)?(?:remote\s+)?(?:keyless\s+entry|key\s*fob)", RegexOptions.IgnoreCase)]
    private static partial Regex NoFobStatement();

    /// <summary>The status the extraction's <paramref name="claimed"/> text names: present, absent, or unknown
    /// for anything else, including null.</summary>
    public static EquipmentStatus StatusOf(string? claimed) => claimed?.Trim().ToLowerInvariant() switch
    {
        "present" => EquipmentStatus.Present,
        "absent" => EquipmentStatus.Absent,
        _ => EquipmentStatus.Unknown,
    };

    /// <summary><paramref name="claimed"/> when the page text backs it for <paramref name="feature"/> (a name from
    /// <see cref="EquipmentFeatures"/>), otherwise null. The evidence has to sit in a sticker section: the stretch
    /// of the page that starts at a window-sticker or factory-equipment heading. "present" needs the feature's own
    /// wording there. "absent" needs the sticker to state the lack, and no wording for the feature in any sticker
    /// section, since a sticker that names the item shows the car has it. For smart-key entry the lack is a
    /// fob-and-key wording such as "Keyless Entry", since a sticker states a missing proximity key by listing
    /// only that. For push-button start it is a turn key or keyed ignition, or a plain "no push button start":
    /// a fob "Keyless Entry" alone says nothing about how the car starts, so push-button start stays unknown.
    /// For a keyless fob it is a turn key or keyed ignition, or a plain "no keyless entry", with no fob wording
    /// in any section. Wording elsewhere on the page, such as a dealer's description claiming push-button start,
    /// is no evidence either way.</summary>
    public static string? Ground(string? claimed, string feature, string pageText)
    {
        EquipmentStatus status = StatusOf(claimed);
        if (status == EquipmentStatus.Unknown)
        {
            return null;
        }

        List<string> sections = [.. StickerMarker().Matches(pageText).Select(marker =>
            pageText.Substring(marker.Index, Math.Min(StickerSectionChars, pageText.Length - marker.Index)))];

        // A stated lack ("No push button start") must not read as the item being named, so it is cut out before
        // looking for wording that shows the car has something.
        List<string> named = [.. sections.Select(section => NoFobStatement().Replace(NoPushButtonStatement().Replace(section, " "), " "))];
        bool backed = (feature, status) switch
        {
            (EquipmentFeatures.SmartKeyEntry, EquipmentStatus.Present) => named.Any(section => SmartKeyTerm().IsMatch(section)),
            (EquipmentFeatures.PushButtonStart, EquipmentStatus.Present) => named.Any(section => PushButtonTerm().IsMatch(section)),
            (EquipmentFeatures.KeylessFobEntry, EquipmentStatus.Present) => named.Any(section => FobPresentTerm().IsMatch(section)),
            (EquipmentFeatures.SmartKeyEntry, _) => sections.Any(section => FobTerm().IsMatch(section))
                && !named.Any(section => SmartKeyTerm().IsMatch(section) || PushButtonTerm().IsMatch(section)),
            (EquipmentFeatures.PushButtonStart, _) => sections.Any(section => KeyedIgnitionTerm().IsMatch(section) || NoPushButtonStatement().IsMatch(section))
                && !named.Any(section => SmartKeyTerm().IsMatch(section) || PushButtonTerm().IsMatch(section)),
            (EquipmentFeatures.KeylessFobEntry, _) => sections.Any(section => KeyedIgnitionTerm().IsMatch(section) || NoFobStatement().IsMatch(section))
                && !named.Any(section => SmartKeyTerm().IsMatch(section) || PushButtonTerm().IsMatch(section) || FobPresentTerm().IsMatch(section)),
            _ => false,
        };
        return backed
            ? status.ToString().ToLowerInvariant()
            : null;
    }

    /// <summary>The page text the extraction reads: the first <paramref name="maxChars"/> characters, plus, when
    /// no sticker heading appears within them but one does after the cut, an excerpt around the first such
    /// heading, so a long page does not hide its sticker from the extraction.</summary>
    public static string TextForExtraction(string pageText, int maxChars)
    {
        if (pageText.Length <= maxChars)
        {
            return pageText;
        }

        string head = pageText[..maxChars];
        Match marker = StickerMarker().Match(pageText, maxChars);
        if (!marker.Success || StickerMarker().IsMatch(head))
        {
            return head;
        }

        int start = Math.Max(maxChars, marker.Index - ExcerptLeadChars);
        int length = Math.Min(ExcerptChars, pageText.Length - start);
        return $"{head}\n[...]\n{pageText.Substring(start, length)}";
    }
}
