# Setup

This page gets a checkout building and tells you where odo keeps its keys and its data. Once it builds, [running.md](running.md) walks a search from start to finish.

## The .NET SDK and the build

odo needs the .NET 10 SDK, and it's built for both Windows and macOS. Clone the repo, build it, and run the tests:

```
git clone https://github.com/Hallmanac/Odonomics.git
cd Odonomics
dotnet build
dotnet test
```

## Secrets

No key is ever committed. odo reads three optional API keys from `dotnet user-secrets` under the id `odonomics` and from a matching environment variable. If a key is set in both places, the environment variable wins, because odo reads it last.

| Purpose | user-secrets key | environment variable |
|---|---|---|
| Auto.dev listings API | `AutoDev:ApiKey` | `AUTODEV__APIKEY` |
| Marketcheck listings and VIN history API | `Marketcheck:ApiKey` | `MARKETCHECK__APIKEY` |
| Anthropic Messages API (optional, see [Extraction](walk.md#extraction)) | `Anthropic:ApiKey` | `ANTHROPIC__APIKEY` |

Set the two listing keys from `src/Odonomics`:

```
cd src/Odonomics
dotnet user-secrets set "AutoDev:ApiKey" "..."
dotnet user-secrets set "Marketcheck:ApiKey" "..."
```

`odo search` still runs with either key missing. It reports "could not run" for that source and carries on with the other one.

## The data directory

The SQLite ledger and the walk's recorded page text both live under your platform's own per-user data directory.

| Platform | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\Odonomics` |
| macOS | `~/Library/Application Support/Odonomics` |
| Linux | `$XDG_DATA_HOME/odonomics` (falling back to `~/.local/share/odonomics`) |

Set `ODO_DATA_DIR` to override the location entirely. The tests that touch the data directory do this, and they point the variable at a temp directory.

## The browser

`odo walk` and `odo dealer grade` don't launch a browser. Both attach over the Chrome DevTools Protocol to a Chrome or Microsoft Edge that you start by hand with remote debugging turned on, so you'll want one of those installed. The launch line for each operating system is in [walk.md](walk.md#your-browser).

## Development

The build and test lines are the same ones you ran above, plus one more for the earlier spike app:

```
dotnet build          # Odonomics.sln: src/Odonomics and tests/Odonomics.Tests
dotnet test
dotnet build spike/spike.csproj   # the earlier spike app; not part of the solution
```

`spike/` is a separate, earlier throwaway app, and nothing else in these docs touches it. odo promotes the spike's working sources, extraction, and fixtures into a real tool, and what the spike found is written up in [spike-findings.md](spike-findings.md).

Tables render at 80 columns. Set `NO_COLOR=1` to turn off color, the same as any other tool that honors that convention.
