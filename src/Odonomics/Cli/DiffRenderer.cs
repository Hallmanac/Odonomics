using Odonomics.Ledger;
using Spectre.Console;

namespace Odonomics.Cli;

/// <summary>Renders a search/walk diff as four narrow tables (new, moved, price-dropped, gone, the last with a reason per row),
/// each column-limited to still read at 80 columns.</summary>
public static class DiffRenderer
{
    public static void Render(SearchDiff diff)
    {
        RenderNew(diff.New);
        RenderMoved(diff.Moved);
        RenderPriceDrops(diff.PriceDrops);
        RenderGone(diff.Gone);
    }

    private static void RenderNew(IReadOnlyList<NewPostingEntry> entries)
    {
        AnsiConsole.MarkupLine($"[bold]New ({entries.Count})[/]");
        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("  none");
            return;
        }

        Table table = NarrowTable("VIN", "Year", "Model", "Price", "Source");
        foreach (NewPostingEntry entry in entries)
        {
            table.AddRow(Format.Cell(entry.Vin), entry.Year.ToString(), Format.Cell($"{entry.Make} {entry.Model}"), Format.Money(entry.Price), Format.Cell(entry.Source));
        }

        AnsiConsole.Write(table);
    }

    private const int MovedVinColumnWidth = 17;
    private const int MovedModelColumnWidth = 12;
    private const int MovedSourceColumnWidth = 8;
    private const int MovedUrlColumnWidth = 14;

    private static void RenderMoved(IReadOnlyList<MovedPostingEntry> entries)
    {
        AnsiConsole.MarkupLine($"[bold]Moved ({entries.Count})[/]");
        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("  none");
            return;
        }

        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn(new TableColumn("VIN") { Width = MovedVinColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Model") { Width = MovedModelColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Source") { Width = MovedSourceColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Old URL") { Width = MovedUrlColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("New URL") { Width = MovedUrlColumnWidth, NoWrap = true });
        foreach (MovedPostingEntry entry in entries)
        {
            table.AddRow(
                Format.Cell(Format.Truncate(entry.Vin, MovedVinColumnWidth)),
                Format.Cell(Format.Truncate($"{entry.Make} {entry.Model}", MovedModelColumnWidth)),
                Format.Cell(Format.Truncate(entry.Source, MovedSourceColumnWidth)),
                Format.Cell(Format.Truncate(entry.OldUrl, MovedUrlColumnWidth)),
                Format.Cell(Format.Truncate(entry.NewUrl, MovedUrlColumnWidth)));
        }

        AnsiConsole.Write(table);
    }

    private static void RenderPriceDrops(IReadOnlyList<PriceDropEntry> entries)
    {
        AnsiConsole.MarkupLine($"[bold]Price drops ({entries.Count})[/]");
        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("  none");
            return;
        }

        Table table = NarrowTable("VIN", "Model", "Was", "Now", "Source");
        foreach (PriceDropEntry entry in entries)
        {
            table.AddRow(Format.Cell(entry.Vin), Format.Cell($"{entry.Make} {entry.Model}"), Format.Money(entry.PreviousPrice), Format.Money(entry.CurrentPrice), Format.Cell(entry.Source));
        }

        AnsiConsole.Write(table);
    }

    // The VIN, price, source, and reason cells never break across lines, so they get fixed widths; only the
    // model name is free to wrap, at a word boundary, to make room.
    private const int GoneVinColumnWidth = 17;
    private const int GonePriceColumnWidth = 10;
    private const int GoneSourceColumnWidth = 11;
    private const int GoneReasonColumnWidth = 18;

    private static void RenderGone(IReadOnlyList<GonePostingEntry> entries)
    {
        AnsiConsole.MarkupLine($"[bold]Gone ({entries.Count})[/]");
        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("  none");
            return;
        }

        AnsiConsole.Write(BuildGoneTable(entries));
    }

    public static Table BuildGoneTable(IReadOnlyList<GonePostingEntry> entries)
    {
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn(new TableColumn("VIN") { Width = GoneVinColumnWidth, NoWrap = true });
        table.AddColumn("Model");
        table.AddColumn(new TableColumn("Last price") { Width = GonePriceColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Source") { Width = GoneSourceColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Reason") { Width = GoneReasonColumnWidth, NoWrap = true });
        foreach (GonePostingEntry entry in entries)
        {
            table.AddRow(Format.Cell(entry.Vin), Format.Cell($"{entry.Make} {entry.Model}"), Format.Money(entry.LastKnownPrice), Format.Cell(Format.Truncate(entry.Source, GoneSourceColumnWidth)), Format.Cell(entry.Reason));
        }

        return table;
    }

    private static Table NarrowTable(params string[] columns)
    {
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        foreach (string column in columns)
        {
            table.AddColumn(column);
        }

        return table;
    }
}
