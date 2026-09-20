# Odonomics

Odonomics is a personal car-search and total-cost-of-ownership tool that finds used-car listings across dealer APIs and major retail sites, vets the VIN and the dealer, and ranks every candidate by what it will actually cost to own per month over ten years.

It's built to answer one question: which car goes the longest for the least, with the fewest surprises.

## odo v0

`odo` is the CLI. This is v0: one purchase, one scenario, a plain SQLite ledger, and an operator-assisted browser walk. It promotes the spike's working sources, extraction, and fixtures (see `spike/`, left untouched) into a real, usable tool. The proper multi-purchase architecture waits for a retrospective after the car is bought.

`spike/` is a separate, earlier throwaway app; it is not part of `Odonomics.sln` and is not touched by anything below.

## Setup

Needs the .NET 10 SDK on both Windows and macOS.

```
git clone <repo>
cd odonomics
dotnet build
dotnet test
```

### Secrets

No key is ever committed. `odo` reads three optional API keys from `dotnet user-secrets` under id `odonomics`, falling back to an environment variable when the secret is not set:

| Purpose | user-secrets key | environment variable |
|---|---|---|
| Auto.dev listings API | `AutoDev:ApiKey` | `AUTODEV__APIKEY` |
| Marketcheck listings API | `Marketcheck:ApiKey` | `MARKETCHECK__APIKEY` |
| Anthropic Messages API (optional; see below) | `Anthropic:ApiKey` | `ANTHROPIC__APIKEY` |

Set them from `src/Odonomics`:

```
cd src/Odonomics
dotnet user-secrets set "AutoDev:ApiKey" "..."
dotnet user-secrets set "Marketcheck:ApiKey" "..."
```

`odo search` still runs with either key missing; it reports "could not run" for that source and continues with the other.

`odo walk` extracts vehicle fields from page text using the operator's already-authenticated `claude` CLI by default. If `Anthropic:ApiKey` is set, it calls the Anthropic Messages API directly instead; otherwise no Anthropic key is required at all.

### Data directory

The SQLite ledger and the walk's recorded page text live under the platform's own per-user data directory:

| Platform | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\Odonomics` |
| macOS | `~/Library/Application Support/Odonomics` |
| Linux | `$XDG_DATA_HOME/odonomics` (falling back to `~/.local/share/odonomics`) |

Set `ODO_DATA_DIR` to override this entirely (the test suite always does, pointed at a temp directory).

## Commands

Run from the repo root, either as `dotnet run --project src/Odonomics -- <command>` from a checkout, or as `odo <command>` once you `dotnet publish` (the executable is named `odo`). Both need to run from the repo root (or anywhere `scenarios/daughter.json` resolves relative to `--scenario`), since the default scenario path is relative.

```
odo search                          run Auto.dev and Marketcheck, upsert the ledger, print the diff
odo walk cars.com|carvana [--max N] an operator-assisted walk of one site (see below)
odo rank [--budget N] [--term M]    score every vehicle in the ledger against the scenario
odo show <vin>                      NHTSA decode, recalls, complaints, postings, notes, finalist status
odo note <vin> "<text>"             attach a free-text note to a vehicle
odo finalist <vin>                  mark a vehicle a finalist (needs a PPI note and a Carfax/AutoCheck note)
odo budget                          max purchase price per target monthly budget in the scenario
```

Every command that scores against a scenario defaults to `scenarios/daughter.json`; pass `--scenario <path>` to use a different one.

## The assisted walk

`odo walk` never launches a browser. It connects over the Chrome DevTools Protocol to a Chrome the operator starts by hand, in its own dedicated profile so it never touches the operator's everyday browsing session. If nothing is listening on the debugging port, `odo walk` prints the exact command for your OS and exits; running `odo walk` again picks it up.

Launch line (also printed by `odo walk` itself, with your own data directory filled in):

**macOS**

```
/Applications/Google\ Chrome.app/Contents/MacOS/Google\ Chrome --remote-debugging-port=9222 --user-data-dir="$HOME/Library/Application Support/Odonomics/chrome-profile"
```

**Windows** (PowerShell or cmd)

```
"C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="%LOCALAPPDATA%\Odonomics\chrome-profile"
```

Leave that Chrome window open; `odo walk cars.com` or `odo walk carvana` then visits one search page, scrolls and dwells like a person, opens a capped number of detail pages with a random gap between each, and beeps and waits for Enter if a page looks like a bot-defense challenge, so you can solve it by hand in the same window before the walk continues.

## Scenario

`scenarios/daughter.json` is the shipped scenario: target models, hard filters, and every cost assumption (APR, gas price, insurance, mpg, fees, residual value, and so on), each either a single pinned value or a loose min/max range. A loose input makes every cost line in `odo rank` and `odo show` a band instead of a point. Edit the file directly; there is no `odo scenario set` in v0.

## Development

```
dotnet build          # Odonomics.sln: src/Odonomics and tests/Odonomics.Tests
dotnet test
dotnet build spike/spike.csproj   # the untouched spike app; not part of the solution
```

Tables render at 80 columns; set `NO_COLOR=1` to turn off color, same as any other tool that honors the convention.
