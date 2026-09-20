using System.Diagnostics;
using System.Globalization;
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
/// Four defenses: the page text is substituted into the prompt between explicit delimiters with an
/// instruction to treat it as inert data, the subprocess runs with `--tools ""` so it has no
/// tool access at all regardless of what it is told, every VIN that comes back is checked against
/// the standard VIN character set before being trusted, and every make/model/price/mileage that
/// comes back is checked for actually appearing in the source page text before being trusted -
/// an extraction that names a value the page never mentioned is dropped rather than passed on to
/// look like an observed fact.
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
        // --allowedTools is a pre-approval allowlist, not a restriction on what is available; only
        // --tools "" actually removes tool availability from the subprocess.
        psi.ArgumentList.Add("--tools");
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
                if (extracted is null)
                {
                    // The model can reply with the bare JSON literal "null" (e.g. for a removed
                    // listing) and deserialize cleanly to a null result. Callers rely on a null
                    // Error implying a non-null Result, so that has to be an error, not a silent
                    // null-for-null outcome.
                    return new ExtractionOutcome(null, costUsd, $"extraction returned null: {jsonText}");
                }

                extracted = GroundInPageText(extracted, truncated);
                return new ExtractionOutcome(extracted, costUsd, null);
            }
            catch (JsonException ex)
            {
                return new ExtractionOutcome(null, costUsd, $"could not parse extraction JSON ({ex.Message}): {jsonText}");
            }
        }
    }

    /// <summary>
    /// Drops any field the model returned that a prompt-injected instruction could have fabricated
    /// rather than read off the page: a VIN of the wrong shape, or a make/model/price/mileage that
    /// does not actually occur, in some form, in the page text the model was given.
    /// </summary>
    private static ExtractionResult GroundInPageText(ExtractionResult extracted, string pageText)
    {
        if (extracted.Vin is not null && !VinShape.IsMatch(extracted.Vin))
        {
            extracted = extracted with { Vin = null };
        }

        if (extracted.Make is not null && !ContainsLoosely(pageText, extracted.Make))
        {
            extracted = extracted with { Make = null };
        }

        if (extracted.Model is not null && !ContainsLoosely(pageText, extracted.Model))
        {
            extracted = extracted with { Model = null };
        }

        if (extracted.Price is not null && !ContainsNumber(pageText, extracted.Price.Value))
        {
            extracted = extracted with { Price = null };
        }

        if (extracted.Mileage is not null && !ContainsNumber(pageText, extracted.Mileage.Value))
        {
            extracted = extracted with { Mileage = null };
        }

        return extracted;
    }

    private static readonly Regex NumberToken = new(@"\d[\d,]*(?:\.\d+)?", RegexOptions.Compiled);

    /// <summary>
    /// A run of two or more digit groups joined by spaces or hyphens, and optionally wrapped in
    /// parentheses - the shape of a phone number ("(555) 123-4567") or a dashed stock/zip+4 number
    /// ("90210-1234"). Each such run is one unrelated number, not several standalone ones, so it is
    /// masked out before <see cref="ContainsNumber"/> tokenizes the page for legitimate matches.
    /// </summary>
    private static readonly Regex ConnectedNumberBlock = new(@"\(?\d+\)?(?:[ \t-]+\(?\d+\)?)+", RegexOptions.Compiled);

    /// <summary>
    /// Whether <paramref name="needle"/> occurs as a whole word in <paramref name="haystack"/>, not
    /// merely as a substring - so a fabricated make of "Ford" does not ground against unrelated page
    /// copy like "affordable".
    /// </summary>
    private static bool ContainsLoosely(string haystack, string needle) =>
        Regex.IsMatch(haystack, $@"\b{Regex.Escape(needle)}\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// Whether <paramref name="value"/> occurs as one of the page's own number tokens - tolerant of
    /// "$12,345", "12345.00", or "12,345 miles" all representing the same number the page actually
    /// shows, but not a number stitched together from the digits of unrelated numbers (a VIN, a zip
    /// code, a stock number, a phone number) that happen to sit next to each other on the page.
    /// </summary>
    private static bool ContainsNumber(string haystack, decimal value)
    {
        // Truncated decimal-to-decimal comparison, not a cast to long: a fabricated value from a
        // prompt injection can carry more digits than long can hold, and a decimal-to-integral cast
        // throws OverflowException in that case regardless of checked/unchecked context - exactly
        // the kind of value this check exists to reject, not crash on.
        decimal needle = Math.Truncate(value);
        string masked = ConnectedNumberBlock.Replace(haystack, match => new string(' ', match.Value.Length));
        foreach (Match match in NumberToken.Matches(masked))
        {
            string cleaned = match.Value.Replace(",", string.Empty);
            if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
                && Math.Truncate(parsed) == needle)
            {
                return true;
            }
        }

        return false;
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
