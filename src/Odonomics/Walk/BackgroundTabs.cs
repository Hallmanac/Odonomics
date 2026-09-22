using Microsoft.Playwright;

namespace Odonomics.Walk;

/// <summary>Opens a new page as a CDP background target, the only way to give the walk a tab
/// without stealing OS-level focus: <see cref="IBrowserContext.NewPageAsync()"/> issues its own
/// Target.createTarget call with background left at its default of false, which raises the
/// browser window on macOS every time a detail page opens. Sending Target.createTarget directly
/// with background true creates the tab unfocused; Playwright still notices it and exposes it as
/// a normal <see cref="IPage"/> through the context's own page-tracking, the same way it exposes
/// a popup a page opens on its own.</summary>
public static class BackgroundTabs
{
    public static Task<IPage> OpenAsync(ICDPSession browserCdpSession, IBrowserContext context) =>
        context.RunAndWaitForPageAsync(async () =>
        {
            await browserCdpSession.SendAsync("Target.createTarget", new Dictionary<string, object>
            {
                ["url"] = "about:blank",
                ["background"] = true,
            });
        });
}
