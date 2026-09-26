using System.CommandLine;
using Odonomics.Cli.Commands;

namespace Odonomics.Tests.Walk;

public class WalkRevisitOptionTests
{
    [Fact]
    public void RevisitOption_WithoutTheFlag_IsOff()
    {
        Option<bool> option = WalkCommand.CreateRevisitOption();
        var command = new Command("walk") { option };

        ParseResult parseResult = command.Parse([]);

        Assert.Empty(parseResult.Errors);
        Assert.False(parseResult.GetValue(option));
    }

    [Fact]
    public void RevisitOption_WithTheFlag_IsOn()
    {
        Option<bool> option = WalkCommand.CreateRevisitOption();
        var command = new Command("walk") { option };

        ParseResult parseResult = command.Parse(["--revisit"]);

        Assert.Empty(parseResult.Errors);
        Assert.True(parseResult.GetValue(option));
    }

    [Fact]
    public async Task RevisitOption_HelpText_SaysKnownLinksAreOtherwiseKeptCurrentFromTheirCards()
    {
        Option<bool> option = WalkCommand.CreateRevisitOption();
        var command = new RootCommand("walk") { option };
        var output = new StringWriter();

        await command.Parse(["--help"]).InvokeAsync(new InvocationConfiguration { Output = output }, CancellationToken.None);

        Assert.Contains("--revisit", output.ToString());
        Assert.Contains("search cards", output.ToString());
    }
}
