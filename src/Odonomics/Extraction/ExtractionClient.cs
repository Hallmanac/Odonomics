using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Odonomics.Extraction;

public sealed record ExtractionResult(
    [property: JsonPropertyName("vin")] string? Vin,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("make")] string? Make,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("trim")] string? Trim,
    [property: JsonPropertyName("price")] decimal? Price,
    [property: JsonPropertyName("mileage")] int? Mileage,
    [property: JsonPropertyName("dealerName")] string? DealerName,
    [property: JsonPropertyName("dealerLocation")] string? DealerLocation,
    [property: JsonPropertyName("fuelType")] string? FuelType = null);

public sealed record ExtractionOutcome(ExtractionResult? Result, decimal CostUsd, string? Error);

/// <summary>
/// Turns one page's visible text into structured vehicle fields, ported from the spike
/// (spike/Extraction.cs) with the same untrusted-input defenses: the page text is substituted
/// into the prompt between explicit delimiters with an instruction to treat it as inert data, the
/// subprocess (CLI path) runs with no tool access, and every VIN/make/model/price/mileage
/// returned is checked against the source page text before being trusted.
///
/// Two paths, per the brief: if Anthropic:ApiKey is set, call the Messages API directly; otherwise
/// shell out to the operator's already-authenticated `claude` CLI, same as the spike. The API path
/// does not track a per-call dollar cost in v0 (Anthropic's token pricing is not tracked here);
/// the CLI path reads its own cost back from --output-format json, same as the spike did.
/// </summary>
public sealed class ExtractionClient
{
    private const string SystemPrompt =
        "You are a data extraction engine. Output only valid JSON matching the requested schema, no markdown fences, no commentary.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex VinShape = new("^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.Compiled);
    private static readonly TimeSpan PerCallTimeout = TimeSpan.FromMinutes(2);

    private readonly string _promptTemplate;
    private readonly string? _anthropicApiKey;
    private readonly HttpClient? _http;

    public ExtractionClient(string promptPath, string schemaPath, string? anthropicApiKey, HttpClient? http)
    {
        string schema = File.ReadAllText(schemaPath);
        _promptTemplate = File.ReadAllText(promptPath).Replace("{{SCHEMA}}", schema);
        _anthropicApiKey = anthropicApiKey;
        _http = http;
    }

    public static ExtractionClient FromAppDirectory(string? anthropicApiKey, HttpClient? http)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Extraction");
        return new ExtractionClient(Path.Combine(dir, "prompt.md"), Path.Combine(dir, "schema.json"), anthropicApiKey, http);
    }

    public Task<ExtractionOutcome> ExtractAsync(string pageText, CancellationToken cancellationToken)
    {
        const int maxChars = 12_000;
        string truncated = pageText.Length > maxChars ? pageText[..maxChars] : pageText;
        string userPrompt = _promptTemplate.Replace("{{PAGE_TEXT}}", truncated);

        return !string.IsNullOrWhiteSpace(_anthropicApiKey) && _http is not null
            ? ExtractWithApiAsync(userPrompt, truncated, cancellationToken)
            : ExtractWithCliAsync(userPrompt, truncated, cancellationToken);
    }

    private async Task<ExtractionOutcome> ExtractWithApiAsync(string userPrompt, string groundingText, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 1024,
            system = SystemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _anthropicApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http!.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ExtractionOutcome(null, 0m, $"could not call the Anthropic API: {ex.Message}");
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ExtractionOutcome(null, 0m, $"Anthropic API returned HTTP {(int)response.StatusCode}: {body}");
            }

            using JsonDocument doc = JsonDocument.Parse(body);
            string? text = doc.RootElement.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array
                ? content.EnumerateArray()
                    .Where(c => c.TryGetProperty("type", out JsonElement t) && t.GetString() == "text")
                    .Select(c => c.TryGetProperty("text", out JsonElement txt) ? txt.GetString() : null)
                    .FirstOrDefault(t => t is not null)
                : null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return new ExtractionOutcome(null, 0m, "empty result from the Anthropic API");
            }

            return ParseExtraction(text, groundingText, costUsd: 0m);
        }
    }

    private async Task<ExtractionOutcome> ExtractWithCliAsync(string userPrompt, string groundingText, CancellationToken cancellationToken)
    {
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
        // No tool access: page text is untrusted third-party content that must not be able to
        // steer a tool call. --allowedTools is a pre-approval allowlist, not a restriction; only
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

            return ParseExtraction(resultText, groundingText, costUsd);
        }
    }

    private static ExtractionOutcome ParseExtraction(string resultText, string groundingText, decimal costUsd)
    {
        string jsonText = StripFences(resultText);
        try
        {
            ExtractionResult? extracted = JsonSerializer.Deserialize<ExtractionResult>(jsonText, JsonOptions);
            if (extracted is null)
            {
                return new ExtractionOutcome(null, costUsd, $"extraction returned null: {jsonText}");
            }

            extracted = GroundInPageText(extracted, groundingText);
            return new ExtractionOutcome(extracted, costUsd, null);
        }
        catch (JsonException ex)
        {
            return new ExtractionOutcome(null, costUsd, $"could not parse extraction JSON ({ex.Message}): {jsonText}");
        }
    }

    /// <summary>Drops any field the model returned that a prompt-injected instruction could have
    /// fabricated rather than read off the page.</summary>
    private static ExtractionResult GroundInPageText(ExtractionResult extracted, string pageText)
    {
        if (extracted.Vin is not null && (!VinShape.IsMatch(extracted.Vin) || !ContainsLoosely(pageText, extracted.Vin)))
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

        if (extracted.DealerName is not null && !ContainsLoosely(pageText, extracted.DealerName))
        {
            extracted = extracted with { DealerName = null };
        }

        if (extracted.DealerLocation is not null && !ContainsLoosely(pageText, extracted.DealerLocation))
        {
            extracted = extracted with { DealerLocation = null };
        }

        return extracted;
    }

    private static readonly Regex NumberToken = new(@"\d[\d,]*(?:\.\d+)?", RegexOptions.Compiled);

    /// <summary>Matches hyphen-joined digit groups (a zip+4, a dashed stock number, the "123-4567"
    /// half of a phone number), optionally paren-wrapped. Deliberately does not bridge on bare
    /// whitespace: two independent comma-grouped numbers routinely sit on the same line separated
    /// only by a space (a price immediately followed by a mileage), and bridging on whitespace
    /// alone destroyed both instead of masking one unrelated number.</summary>
    private static readonly Regex ConnectedNumberBlock = new(@"\(?\d+\)?(?:[ \t]*-[ \t]*\(?\d+\)?)+", RegexOptions.Compiled);

    private static bool ContainsLoosely(string haystack, string needle) =>
        Regex.IsMatch(haystack, $@"\b{Regex.Escape(needle)}\b", RegexOptions.IgnoreCase);

    private static bool ContainsNumber(string haystack, decimal value)
    {
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
            // best-effort: the process may already have exited on its own
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
