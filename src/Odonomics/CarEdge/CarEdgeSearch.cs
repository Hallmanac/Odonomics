namespace Odonomics.CarEdge;

/// <summary>Builds the CarEdge Dealer Ratings search URL for one dealer, ready for the browser
/// `odo dealer grade` is attached to over CDP to navigate.</summary>
public static class CarEdgeSearch
{
    public static string BuildSearchUrl(string dealerName, string? location)
    {
        string query = string.IsNullOrWhiteSpace(location) ? dealerName : $"{dealerName} {location}";
        return $"https://caredge.com/dealer-reviews?search={Uri.EscapeDataString(query)}";
    }
}
