using System.Globalization;
using System.Text;
using Spike.Models;

namespace Spike.Reporting;

public static class FindingsAppender
{
    private const string BeginMarker = "<!-- SPIKE-RESULTS:BEGIN -->";
    private const string EndMarker = "<!-- SPIKE-RESULTS:END -->";
    private const string HeaderRow =
        "| Day | Source | Candidates found | Candidates with VIN | VINs unique to source | Wall time | Dollars | Failures |";
    private const string SeparatorRow = "|---|---|---|---|---|---|---|---|";

    public static async Task AppendDayResultsAsync(string findingsPath, int day, IReadOnlyList<SourceRunResult> results, CancellationToken cancellationToken)
    {
        List<string> newRows = results.Select(r => FormatRow(day, r)).ToList();

        if (!File.Exists(findingsPath))
        {
            string scaffold = $"""
                # Spike findings

                <!-- This section is appended to by `dotnet run --project spike`. Everything else in this file is written by hand. -->

                {BeginMarker}
                {HeaderRow}
                {SeparatorRow}
                {EndMarker}
                """;
            await File.WriteAllTextAsync(findingsPath, scaffold + Environment.NewLine, cancellationToken);
        }

        string content = await File.ReadAllTextAsync(findingsPath, cancellationToken);
        int beginIndex = content.IndexOf(BeginMarker, StringComparison.Ordinal);
        int endIndex = content.IndexOf(EndMarker, StringComparison.Ordinal);
        if (beginIndex < 0 || endIndex < 0 || endIndex < beginIndex)
        {
            throw new InvalidOperationException($"{findingsPath} is missing the {BeginMarker}/{EndMarker} table markers");
        }

        string before = content[..endIndex];
        string after = content[endIndex..];
        string updated = before.TrimEnd('\n', '\r') + "\n" + string.Join('\n', newRows) + "\n" + after;

        await File.WriteAllTextAsync(findingsPath, updated, cancellationToken);
    }

    private static string FormatRow(int day, SourceRunResult r)
    {
        string found = r.CouldNotRun ? "-" : r.CandidatesFound.ToString(CultureInfo.InvariantCulture);
        string withVin = r.CouldNotRun ? "-" : r.CandidatesWithVin.ToString(CultureInfo.InvariantCulture);
        string unique = r.CouldNotRun ? "-" : r.VinsUniqueToSource.ToString(CultureInfo.InvariantCulture);
        string wallTime = r.CouldNotRun ? "-" : $"{r.WallTime.TotalMinutes:0.0} min";
        string dollars = r.CouldNotRun ? "-" : $"${r.DollarsSpent:0.00}";
        var failures = new StringBuilder();
        if (r.CouldNotRun)
        {
            failures.Append("could not run: ").Append(r.CouldNotRunReason);
        }
        if (r.Failures.Count > 0)
        {
            if (failures.Length > 0) failures.Append("; ");
            failures.Append(string.Join("; ", r.Failures));
        }
        if (failures.Length == 0)
        {
            failures.Append("none");
        }

        // Failure text can carry raw stderr or a model's raw (possibly pretty-printed, multi-line)
        // output; both must collapse to one physical line or they split this Markdown table apart
        // partway through the file, corrupting everything after it.
        string failureText = failures.ToString()
            .Replace("\r\n", " ")
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace("|", "\\|");
        return $"| {day} | {r.Source} | {found} | {withVin} | {unique} | {wallTime} | {dollars} | {failureText} |";
    }
}
