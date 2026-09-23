namespace Odonomics.CarEdge;

/// <summary>Builds the CarEdge dealer search URL for one dealer, ready for the browser
/// `odo dealer grade` is attached to over CDP to navigate. CarEdge retired `/dealer-reviews?search=`
/// (now a 404) in favor of `/dealers?q=`.</summary>
public static class CarEdgeSearch
{
    public static string BuildSearchUrl(string dealerName, string? location)
    {
        string query = string.IsNullOrWhiteSpace(location) ? dealerName : $"{dealerName} {location}";
        return $"https://caredge.com/dealers?q={Uri.EscapeDataString(query)}";
    }
}
