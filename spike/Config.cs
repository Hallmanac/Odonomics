using Microsoft.Extensions.Configuration;

namespace Spike;

public sealed class Config
{
    private readonly IConfiguration _configuration;

    public Config()
    {
        _configuration = new ConfigurationBuilder()
            .AddUserSecrets("odonomics-spike")
            .AddEnvironmentVariables()
            .Build();
    }

    public string? AutoDevApiKey => _configuration["AutoDev:ApiKey"];
    public string? MarketcheckApiKey => _configuration["Marketcheck:ApiKey"];
}
