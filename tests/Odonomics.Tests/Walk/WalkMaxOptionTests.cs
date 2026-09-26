using System.CommandLine;
using Odonomics.Cli.Commands;

namespace Odonomics.Tests.Walk;

public class WalkMaxOptionTests
{
    [Fact]
    public void MaxOption_WithoutTheFlag_HasNoValueSoTheWalkSetsNoCap()
    {
        Option<int?> option = WalkCommand.CreateMaxOption();
        var command = new Command("walk") { option };

        ParseResult parseResult = command.Parse([]);

        Assert.Empty(parseResult.Errors);
        Assert.Null(parseResult.GetValue(option));
    }

    [Fact]
    public void MaxOption_WithTheFlag_CarriesTheLimit()
    {
        Option<int?> option = WalkCommand.CreateMaxOption();
        var command = new Command("walk") { option };

        ParseResult parseResult = command.Parse(["--max", "5"]);

        Assert.Empty(parseResult.Errors);
        Assert.Equal(5, parseResult.GetValue(option));
    }

    [Fact]
    public async Task MaxOption_HelpText_ShowsNoDefaultAndSaysWhatOmittingItDoes()
    {
        Option<int?> option = WalkCommand.CreateMaxOption();
        var command = new RootCommand("walk") { option };
        var output = new StringWriter();

        await command.Parse(["--help"]).InvokeAsync(new InvocationConfiguration { Output = output }, CancellationToken.None);

        Assert.DoesNotContain("[default:", output.ToString());
        Assert.Contains("without it the walk visits every car", output.ToString());
    }
}
