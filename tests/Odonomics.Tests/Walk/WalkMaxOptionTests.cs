using System.CommandLine;
using Odonomics.Cli.Commands;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkMaxOptionTests
{
    [Fact]
    public void DefaultMaxDetailPages_IsThirty()
    {
        Assert.Equal(30, WalkPacing.DefaultMaxDetailPages);
    }

    [Fact]
    public void MaxOption_WithoutTheFlag_DefaultsToThirty()
    {
        Option<int> option = WalkCommand.CreateMaxOption();
        var command = new Command("walk") { option };

        ParseResult parseResult = command.Parse([]);

        Assert.Empty(parseResult.Errors);
        Assert.Equal(30, parseResult.GetValue(option));
    }

    [Fact]
    public void MaxOption_WithTheFlag_OverridesTheDefault()
    {
        Option<int> option = WalkCommand.CreateMaxOption();
        var command = new Command("walk") { option };

        ParseResult parseResult = command.Parse(["--max", "5"]);

        Assert.Empty(parseResult.Errors);
        Assert.Equal(5, parseResult.GetValue(option));
    }

    [Fact]
    public async Task MaxOption_HelpText_ShowsTheDefaultOfThirty()
    {
        Option<int> option = WalkCommand.CreateMaxOption();
        var command = new RootCommand("walk") { option };
        var output = new StringWriter();

        await command.Parse(["--help"]).InvokeAsync(new InvocationConfiguration { Output = output }, CancellationToken.None);

        Assert.Contains("[default: 30]", output.ToString());
    }
}
