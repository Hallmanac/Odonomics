namespace Odonomics.Walk;

/// <summary>A page "looks like a challenge" when its title names a known bot-defense page, or
/// its body is suspiciously small (a real listing page is never this short). Ported from the
/// spike's PageWalkEngine block-title markers.</summary>
public static class ChallengeDetector
{
    private static readonly string[] TitleMarkers = ["just a moment", "attention required", "access denied", "are you a human", "unusual traffic"];

    public const int MinBodyTextLength = 500;

    public static bool IsChallenge(string title, string bodyText) =>
        TitleMarkers.Any(m => title.Contains(m, StringComparison.OrdinalIgnoreCase)) || bodyText.Length < MinBodyTextLength;
}
