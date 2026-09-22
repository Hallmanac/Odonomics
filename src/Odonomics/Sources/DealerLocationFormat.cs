namespace Odonomics.Sources;

/// <summary>Formats a dealer's city and state, both optional and independently absent in an API
/// response, into the single "City, ST" string <see cref="Odonomics.Ledger.ListingCandidate.DealerLocation"/>
/// expects.</summary>
public static class DealerLocationFormat
{
    public static string? Build(string? city, string? state)
    {
        bool hasCity = !string.IsNullOrWhiteSpace(city);
        bool hasState = !string.IsNullOrWhiteSpace(state);
        return (hasCity, hasState) switch
        {
            (true, true) => $"{city}, {state}",
            (true, false) => city,
            (false, true) => state,
            _ => null,
        };
    }
}
