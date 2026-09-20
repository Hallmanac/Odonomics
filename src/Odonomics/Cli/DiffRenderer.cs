using Odonomics.Ledger;
using Spectre.Console;

namespace Odonomics.Cli;

/// <summary>Renders a search/walk diff as three narrow tables (new, price-dropped, gone), each
/// column-limited to still read at 80 columns.</summary>
public static class DiffRenderer
{
    public static void Render(SearchDiff diff)
    {
        RenderNew(diff.New);
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

    private static void RenderGone(IReadOnlyList<GonePostingEntry> entries)
    {
        AnsiConsole.MarkupLine($"[bold]Gone ({entries.Count})[/]");
        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("  none");
            return;
        }

        Table table = NarrowTable("VIN", "Model", "Last price", "Source");
        foreach (GonePostingEntry entry in entries)
        {
            table.AddRow(Format.Cell(entry.Vin), Format.Cell($"{entry.Make} {entry.Model}"), Format.Money(entry.LastKnownPrice), Format.Cell(entry.Source));
        }

        AnsiConsole.Write(table);
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
