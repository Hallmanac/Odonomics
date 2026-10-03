namespace Odonomics.Tests.Auctions;

/// <summary>Reads the archive-page fixtures under fixtures/auctions. Four are recordings from a live run
/// on 2026-10-03: the bid.cars lot page for JTDBCMFEXS3070309 (the Copart sale the project's first hit was
/// found on), the bid.cars page a search for JTDKARFU6K3085884 opened (a different VIN), and the
/// html.duckduckgo.com results pages for JTDBCMFEXS3070309 (archive links) and 19XZE4F52ME000999 (no
/// archive site among them). The rest (the DuckDuckGo no-results and captcha pages, the Cloudflare-style
/// captcha page, the changed-layout page, and the tab-separated and stacked lot page) are modeled, because
/// no live run has hit them yet; a recording of one replaces its model by taking its name.</summary>
internal static class AuctionFixtures
{
    public const string CorollaHybridVin = "JTDBCMFEXS3070309";
    public const string CorollaHybridLotUrl = "https://bid.cars/en/lot/1-55637026/2025-Toyota-Corolla-JTDBCMFEXS3070309";

    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "auctions", fileName));
}
