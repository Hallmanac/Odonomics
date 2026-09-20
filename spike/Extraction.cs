using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spike;

public sealed record ExtractionResult(
    [property: JsonPropertyName("vin")] string? Vin,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("make")] string? Make,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("trim")] string? Trim,
    [property: JsonPropertyName("price")] decimal? Price,
    [property: JsonPropertyName("mileage")] int? Mileage);

public sealed record ExtractionOutcome(ExtractionResult? Result, decimal CostUsd, string? Error);

/// <summary>
/// Runs the model extraction step by shelling out to the already-authenticated `claude` CLI.
/// This spike has no Anthropic API key configured, so extraction rides the operator's existing
/// Claude Code session rather than a raw API call; a real build would call the Messages API
/// directly. Cost is read back from the CLI's own --output-format json envelope.
/// </summary>
public sealed class ExtractionClient
{
    private const string SystemPrompt =
        "You are a data extraction engine. Output only valid JSON matching the requested schema, no markdown fences, no commentary.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _promptTemplate;

    public ExtractionClient(string promptPath, string schemaPath)
    {
        var schema = File.ReadAllText(schemaPath);
        _promptTemplate = File.ReadAllText(promptPath)
            .Replace("{{SCHEMA}}", schema);
    }

    public async Task<ExtractionOutcome> ExtractAsync(string pageText, CancellationToken cancellationToken)
    {
        const int maxChars = 12_000;
        var truncated = pageText.Length > maxChars ? pageText[..maxChars] : pageText;
        var userPrompt = _promptTemplate.Replace("{{PAGE_TEXT}}", truncated);

        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(userPrompt);
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add("haiku");
        psi.ArgumentList.Add("--system-prompt");
        psi.ArgumentList.Add(SystemPrompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("could not start claude CLI");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            return new ExtractionOutcome(null, 0m, $"claude CLI exited {process.ExitCode}: {stderr}");
        }

        using var envelope = JsonDocument.Parse(stdout);
        var root = envelope.RootElement;
        var costUsd = root.TryGetProperty("total_cost_usd", out var costEl) ? (decimal)costEl.GetDouble() : 0m;
        var resultText = root.TryGetProperty("result", out var resultEl) ? resultEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(resultText))
        {
            return new ExtractionOutcome(null, costUsd, "empty result from claude CLI");
        }

        var jsonText = StripFences(resultText);
        try
        {
            var extracted = JsonSerializer.Deserialize<ExtractionResult>(jsonText, JsonOptions);
            return new ExtractionOutcome(extracted, costUsd, null);
        }
        catch (JsonException ex)
        {
            return new ExtractionOutcome(null, costUsd, $"could not parse extraction JSON ({ex.Message}): {jsonText}");
        }
    }

    private static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        trimmed = firstNewline >= 0 ? trimmed[(firstNewline + 1)..] : trimmed;
        var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (fenceEnd >= 0)
        {
            trimmed = trimmed[..fenceEnd];
        }

        return trimmed.Trim();
    }
}
