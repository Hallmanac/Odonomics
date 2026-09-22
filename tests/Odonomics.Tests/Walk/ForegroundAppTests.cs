using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class ForegroundAppTests
{
    [Fact]
    public void CaptureFrontmostScript_AsksSystemEventsForTheFrontmostBundleIdentifier()
    {
        Assert.Contains("System Events", ForegroundApp.CaptureFrontmostScript);
        Assert.Contains("bundle identifier", ForegroundApp.CaptureFrontmostScript);
        Assert.Contains("frontmost", ForegroundApp.CaptureFrontmostScript);
    }

    [Fact]
    public void RestoreScript_ActivatesTheApplicationPassedInAsArgv()
    {
        Assert.Contains("tell application id (item 1 of argv) to activate", ForegroundApp.RestoreScript);
    }

    [Fact]
    public async Task CaptureFrontmostAsync_OnNonMacOS_ReturnsNull()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        string? result = await ForegroundApp.CaptureFrontmostAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RestoreAsync_WithNoCapturedApp_DoesNothing()
    {
        await ForegroundApp.RestoreAsync(null, CancellationToken.None);
    }
}
