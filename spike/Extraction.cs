using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
///
/// The page text handed to this method is untrusted: it comes from a listing a third party wrote,
/// and a hostile seller could write a prompt-injection attempt straight into a description field.
/// Two defenses: the subprocess runs with `--allowedTools ""`, so it has no tool access at all
/// regardless of what it is told, and every VIN that comes back is checked against the standard
/// VIN character set before being trusted.
/// </summary>
public sealed class ExtractionClient
{
    private const string SystemPrompt =
        "You are a data extraction engine. Output only valid JSON matching the requested schema, no markdown fences, no commentary.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex VinShape = new("^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.Compiled);
    private static readonly TimeSpan PerCallTimeout = TimeSpan.FromMinutes(2);

    private readonly string _promptTemplate;

    public ExtractionClient(string promptPath, string schemaPath)
    {
        string schema = File.ReadAllText(schemaPath);
        _promptTemplate = File.ReadAllText(promptPath)
            .Replace("{{SCHEMA}}", schema);
    }

    public async Task<ExtractionOutcome> ExtractAsync(string pageText, CancellationToken cancellationToken)
    {
        const int maxChars = 12_000;
        string truncated = pageText.Length > maxChars ? pageText[..maxChars] : pageText;
        string userPrompt = _promptTemplate.Replace("{{PAGE_TEXT}}", truncated);

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
        // No tool access: this call only ever needs to read the prompt and produce JSON, and page
        // text is untrusted third-party content that must not be able to steer a tool call.
        psi.ArgumentList.Add("--allowedTools");
        psi.ArgumentList.Add("");

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("could not start claude CLI");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ExtractionOutcome(null, 0m, $"could not start claude CLI: {ex.Message}");
        }

        using (process)
        {
            // A single call that hangs must not be able to consume the whole run's budget by
            // itself, and a call cancelled for either reason must not leave the child running
            // against the operator's own session.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(PerCallTimeout);
            CancellationToken callToken = timeoutCts.Token;

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(callToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(callToken);
            try
            {
                await process.WaitForExitAsync(callToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new ExtractionOutcome(null, 0m, $"claude CLI timed out after {PerCallTimeout.TotalMinutes:0} minutes");
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                return new ExtractionOutcome(null, 0m, $"claude CLI exited {process.ExitCode}: {stderr}");
            }

            JsonElement root;
            try
            {
                using JsonDocument envelope = JsonDocument.Parse(stdout);
                root = envelope.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                return new ExtractionOutcome(null, 0m, $"could not parse claude CLI output as JSON ({ex.Message}): {stdout}");
            }

            decimal costUsd = root.TryGetProperty("total_cost_usd", out JsonElement costEl) && costEl.ValueKind == JsonValueKind.Number
                ? (decimal)costEl.GetDouble()
                : 0m;
            string? resultText = root.TryGetProperty("result", out JsonElement resultEl) && resultEl.ValueKind == JsonValueKind.String
                ? resultEl.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(resultText))
            {
                return new ExtractionOutcome(null, costUsd, "empty result from claude CLI");
            }

            string jsonText = StripFences(resultText);
            try
            {
                ExtractionResult? extracted = JsonSerializer.Deserialize<ExtractionResult>(jsonText, JsonOptions);
                if (extracted?.Vin is not null && !VinShape.IsMatch(extracted.Vin))
                {
                    extracted = extracted with { Vin = null };
                }
                return new ExtractionOutcome(extracted, costUsd, null);
            }
            catch (JsonException ex)
            {
                return new ExtractionOutcome(null, costUsd, $"could not parse extraction JSON ({ex.Message}): {jsonText}");
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Best-effort: the process may already have exited on its own between the timeout
            // firing and this call.
        }
    }

    private static string StripFences(string text)
    {
        string trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        int firstNewline = trimmed.IndexOf('\n');
        trimmed = firstNewline >= 0 ? trimmed[(firstNewline + 1)..] : trimmed;
        int fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (fenceEnd >= 0)
        {
            trimmed = trimmed[..fenceEnd];
        }

        return trimmed.Trim();
    }
}
