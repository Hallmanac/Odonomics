using Microsoft.Extensions.Configuration;

namespace Odonomics.Secrets;

/// <summary>Reads API keys from `dotnet user-secrets` id "odonomics" first, then the matching
/// environment variable. Never reads from a file checked into the repo.</summary>
public sealed class SecretResolver
{
    private readonly IConfiguration _configuration;

    public SecretResolver()
    {
        _configuration = new ConfigurationBuilder()
            .AddUserSecrets("odonomics")
            .AddEnvironmentVariables()
            .Build();
    }

    public string? AutoDevApiKey => _configuration["AutoDev:ApiKey"];
    public string? MarketcheckApiKey => _configuration["Marketcheck:ApiKey"];
    public string? AnthropicApiKey => _configuration["Anthropic:ApiKey"];
}
