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
odo walk [cars.com|carvana] [--model "Make Model"] [--max N]
                                     an operator-assisted walk (see below)
odo dealer grade [--all | <vin>]    look up each ungraded dealer's CarEdge grade (see below)
odo rank [--budget N] [--term M]    score every vehicle in the ledger against the scenario
odo show <vin> [--refresh] [--all-history]
                                     NHTSA decode, recalls, complaints, safety ratings, Marketcheck
                                     VIN history (grouped by seller; pass --all-history for the raw
                                     one-row-per-sighting list too), red flags, postings, notes,
                                     finalist status
odo research [<vin> ...] [--refresh] [--quiet]
                                     NHTSA safety ratings and Marketcheck VIN history for every
                                     vehicle in the ledger that passes the scenario's filters (or
                                     just the given VINs), with a red-flags summary (see below)
odo note <vin> "<text>"             attach a free-text note to a vehicle
odo finalist <vin>                  mark a vehicle a finalist (needs a PPI note and a Carfax/AutoCheck note)
odo budget                          max purchase price per target monthly budget in the scenario
```

Every command that scores against a scenario defaults to `scenarios/daughter.json`; pass `--scenario <path>` to use a different one.

## Background research and red flags

`odo show <vin>` and `odo research` both pull the same two pieces of free background research for a
VIN, on top of the NHTSA decode/recalls/complaints `odo show` has always fetched:

- **NHTSA safety ratings**: overall and per-category (front crash, side crash, rollover) star
  ratings for that year, make, and model.
- **Marketcheck VIN history**: every prior listing recorded for the VIN (dealer, first/last seen
  dates, price, mileage) and the current listing's days on market. Needs `Marketcheck:ApiKey`; with
  no key set, this degrades to a "could not fetch" line rather than failing the command. `odo show`
  renders this history grouped by seller (see the seller-group rule below): one row per group with
  its first/last seen dates, dealer name(s) (a multi-seller group's cell leads with its seller
  count, e.g. "16 sellers: ...", and whose names cap at three plus "and N more"; the cell is never
  cut short, so a long name list wraps onto lines under the group's row), price range, and mileage
  range, so a syndicated 50-row history still reads as a handful of lines. When Marketcheck reports
  no days on market for the current listing, `odo show` counts the days since the current seller
  group (the one seen most recently) was first seen, provided that group was last seen within the
  past 14 days; otherwise the car is treated as not currently listed and it prints `(unknown)`. Pass `--all-history` to also print the raw,
  one-row-per-sighting list underneath.

Both are cached on the vehicle's ledger row with a fetched-at stamp, and are only re-fetched when
the cached research is more than seven days old, `--refresh` is passed, the Marketcheck VIN
history was never successfully fetched (a missing key or a failed call), or the NHTSA recalls,
complaints, or safety-ratings call previously could not be fetched (see below: each retries on its
own, independently of the seven-day window); a `show`/`research` inside the window with a fetched
history and no outstanding NHTSA failure reads the cache and makes no network call.

api.nhtsa.gov has occasionally been observed answering an otherwise-healthy call with an HTML error
page, or not responding at all, instead of its usual JSON. Odonomics retries a failed NHTSA call
once; if it still fails, that piece alone (recalls, complaints, or safety ratings) degrades to a
"could not fetch" reason shown in place of the data, and the vehicle's last known-good value for
that piece (if any) stays on display and in `odo rank`'s red-flag math. One bad NHTSA answer never
fails the whole vehicle, and one bad vehicle never fails the batch.

`odo research` runs that lookup for many vehicles in one go, with a short pause after each vehicle it
actually fetches so as not to hammer either API; a cached vehicle makes no network call, so it adds
no pause, and a run over an entirely-cached filtered set finishes in a moment rather than one second
per vehicle. With no VINs given, it covers every vehicle in the ledger that passes the scenario's
hard filters (allowed model, minimum year, maximum mileage, not new stock; a vehicle with no current
asking price is still eligible), whether or not that vehicle needs a refresh, so the run always works
the whole filtered set rather than only the slice that happened to be stale.

While it runs, the progress output prints one line per vehicle actually fetched this run (not one
served from the cache, since nothing happened for that vehicle): `researched` followed by its flags
as short tags inline, e.g. `2025 Toyota Camry Hybrid (4T1...): researched, 12-sellers,
mileage-drop`, or a yellow line naming which piece(s) failed and which data is still stored when
only some of a vehicle's pieces came back this run. A vehicle NHTSA and Marketcheck could not be
reached for at all prints a red "could not be reached" line and the rest of the batch continues; the
command exits non-zero only when at least one vehicle was unreachable and no vehicle fetched this run
came back with any data at all, fully or partially (a cached vehicle needs no fetch, so it never
counts toward either side of that check). Pass `--quiet` to replace all of that with a single counter
line that overwrites itself in place (`researching 12/84 (8 fetched, 4 cached, 0 unreachable)`)
instead of scrolling; redirected to a file or a pipe, where there's no line to overwrite, each update
prints on its own line instead.

The run ends with a tally line ("N fully researched, M partially researched, K unreachable") counting
only the vehicles actually fetched this run, not cached ones (a cached vehicle did no work this run
to tally), and then the summary: a header stating the whole filtered set's counts (`84 vehicles, 40
fetched, 44 cached, 0 unreachable`) followed by one line per vehicle in that set, fetched or cached
alike, so the summary is the one place the whole filtered set's flags are visible together rather
than only the vehicles fetched this run. Each line starts with a one-letter marker for where that
vehicle's data came from this run, `F` fetched, `C` cached, or `U` unreachable, then year, make,
model, VIN, and its flags as short tags (`mileage-drop`, `5-sellers`, `no-remedy-recall`, or `none`),
bounded to 80 columns so a batch of dozens of vehicles reads as a compact scan rather than a wall of
dealer lists and full-text reasons; full detail for any one vehicle is what `odo show <vin>` is for.
A vehicle with one or more failed pieces this run also carries a `partial` tag alongside its flags,
so a summary line never reads as a clean, fully-checked vehicle when part of its data is actually
stale or missing. A run where every vehicle in the filtered set is already cached still prints the
full summary, not an empty one; the cache only skips the network calls, never the reporting.

The red-flags section (shown in full by `odo show`, as short tags by `odo research`) lists, with a
reason, anything the data shows:

Both rules below start from the same seller-group clustering of the listing history: two listings
belong to the same seller group when their windows (first seen to last seen) overlap, or touch
within two days, AND their mileage is identical, regardless of what dealer name either one carries.
This is what catches a dealer-group syndication feed relisting one physical car under a dozen-plus
rooftop names at once. As a secondary merge, two listings also belong to the same group
when their dealer names share a word-for-word stem case-insensitively (e.g. "Schaller Honda" of
"Schaller Honda Subaru Mitsubishi", or "Alm Hyundai Florence" of "ALM Hyundai Florence"), which
catches the same dealer spelled or franchised differently across sightings even when the mileage
moved between them.

- **mileage that decreased between two listings of the same VIN** (`mileage-drop`), ignoring a drop
  to zero or nearly zero (a placeholder/reset value, not a real odometer reading), a drop between
  two listings first seen on the same calendar day (the same snapshot re-scraped, not two real
  readings), and a drop smaller than the larger of 500 miles or 1% of the prior mileage (rounding
  and minor re-entry noise). Both the 500-mile floor and the 1% figure are constants in
  `RedFlagsEvaluator`; no scenario field or CLI flag exposes them yet. A drop that survives all of
  that but sits between two listings in the *same seller group* whose windows are on consecutive or
  overlapping days is judged a same-listing odometer correction rather than a real rollback (e.g.
  Daytona Toyota's own listing corrected from 4,703 to 3,852 miles between Sep 9 and Sep 10 on a demo
  car): it's recorded as a note on the vehicle instead (`mileage corrected 4,703 to 3,852 at Daytona
  Toyota on Sep 10`), the same as an operator's own `odo note` would, and does not raise a flag. A
  drop across two different seller groups, or across a real gap even at the same seller, still flags.
  When the higher reading is a spike (an earlier reading no higher than the lower one precedes it),
  the flag names the readings on either side of it, each with its date (`one listing showed 181,407
  miles on 2026-02-01 between readings of 85,960 on 2025-10-16 and 86,663 on 2026-06-01`), since
  that shape is usually one mistyped odometer value; with no such earlier reading it reads `mileage
  dropped from X to Y between listings`.
- **three or more distinct sellers within a 90-day window of the listing history** (`N-sellers`). A
  group with no dealer name at all is never counted, the same as a nameless listing was ignored
  before this rule existed: there's no evidence at all to tell it apart from a repeat of the seller
  before or after it. Of the named groups, sellers are counted from the seller groups above, walked
  in chronological order: the first group is seller one, and every later group counts as a new
  seller only when its window starts after the previous counted group's own last window ended, and
  it is NOT the case that both sides have a real, equal mileage reading (an absent or placeholder
  reading on either side is never read as proof the mileage stayed the same, so it still counts as a
  change). A group that overlaps the previous one, or resumes at the same real mileage, is folded in
  rather than counted as a seller change, since neither shape tells it apart from the same car
  sitting with the same owner. This is what a real dealer-to-dealer flip looks like (sequential
  windows, mileage that actually moved) as opposed to syndication (overlapping windows, identical
  mileage) even when a shared-stem name never ties the rooftops together at all. The seller-count
  threshold (default three) is a constant in `RedFlagsEvaluator`, not a scenario or CLI setting. The
  printed list caps at three names
  followed by "and N more" so a syndication feed's 30-plus rooftop names never dominates the line.
- **one or more open NHTSA recalls with no remedy published yet** (`no-remedy-recall`). An open
  recall with a remedy already available does not raise a flag on its own; `odo rank` shows the
  total open-recall count instead (see below), and NHTSA's free recallsByVehicle endpoint has no
  true per-VIN remedy-completed status, so "remedy available" here means the campaign's own remedy
  text has actually been published, not that this specific VIN's owner already had the fix done.
- **an NHTSA overall safety rating below four stars** (`low-safety-rating`)
- **a current price more than 15% above the average of the VIN's prior listing prices**
  (`price-spike`)

An empty section says so explicitly (`odo show`: "none found"; `odo research`: `none`) rather than
being left blank.

`odo rank` marks each vehicle's row with whether it has been researched yet and whether any red
flag turned up (`-` for not yet researched, `clean`, or `flag`), and an `rc` figure showing the
total open NHTSA recall count (`-` when not yet researched, or when recalls have never been
successfully fetched for that vehicle even though other research pieces have); both are
display-only and never change the ranking math itself.

## The assisted walk

`odo walk` never launches a browser. It connects over the Chrome DevTools Protocol to a Chrome or Microsoft Edge the operator starts by hand, in a dedicated profile so it never touches the operator's everyday browsing session. Edge speaks the same DevTools Protocol as Chrome and is an equally valid choice; use whichever Chromium browser you actually have installed. Chrome and Edge each get their own profile directory rather than sharing one, since each browser encrypts its stored cookies with a key only it can read; a bot-defense challenge you clear in one browser still has to be cleared again the first time you switch to the other. If nothing is listening on the debugging port, `odo walk` prints the exact command for your OS and exits; running `odo walk` again picks it up.

Launch line (also printed by `odo walk` itself, with your own data directory filled in):

**macOS, Chrome**

```
/Applications/Google\ Chrome.app/Contents/MacOS/Google\ Chrome --remote-debugging-port=9222 --user-data-dir="$HOME/Library/Application Support/Odonomics/chrome-profile"
```

**macOS, Edge**

```
/Applications/Microsoft\ Edge.app/Contents/MacOS/Microsoft\ Edge --remote-debugging-port=9222 --user-data-dir="$HOME/Library/Application Support/Odonomics/edge-profile"
```

**Windows (PowerShell), Chrome**

```
& "C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="$env:LOCALAPPDATA\Odonomics\chrome-profile"
```

**Windows (PowerShell), Edge**

```
& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --remote-debugging-port=9222 --user-data-dir="$env:LOCALAPPDATA\Odonomics\edge-profile"
```

**Windows (cmd), Chrome**

```
"C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="%LOCALAPPDATA%\Odonomics\chrome-profile"
```

**Windows (cmd), Edge**

```
"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --remote-debugging-port=9222 --user-data-dir="%LOCALAPPDATA%\Odonomics\edge-profile"
```

A bare quoted path needs PowerShell's `&` call operator to run at all; cmd runs it either way. `odo walk` itself prints all four forms (Chrome and Edge, PowerShell and cmd) on Windows, and both browsers' lines on macOS and Linux, when it can't find one listening.

Leave that window open. A bare `odo walk` then walks the whole shortlist in one command: cars.com first, then carvana, and on each site every model in the scenario's `allowedModels`, in order, printing which site and model it's about to visit before each. `odo walk cars.com` or `odo walk carvana` narrows it to that one site (still every model); `--model "Make Model"` narrows either form to that one model exactly, matching the scenario's own casing case-insensitively. Every site-and-model pair visits one search page, scrolls and dwells like a person, and opens detail pages, with a random gap between each, until it has `--max` matching ones (10 by default) or runs out of candidate links to try; there's a further random gap before moving on to the next model and before moving on to the next site. Every detail tab opens as a CDP background target and closes the same way, so the browser stays out of the way on your desktop the whole time; it never comes to the front of your other windows. The one exception is a bot-defense challenge: the walk beeps and brings the challenged page to the front so you can solve it by hand, then puts your previous window back in front once you press Enter, and the walk itself goes back to running in the background.

`--max` caps matching detail pages per site-and-model pair, not per run or raw page visits: a detail page the walk opens and then rejects for being some other model (a plain Corolla on a Corolla Hybrid walk, say) or for repeating a VIN this pair already saved never spends any of the cap, so the walk keeps taking links until the cap is met with real matches or the search page runs out of links to try. A pair's own coverage is only stamped on the run once that pair's walk actually finishes; a pair that errors (a page that never loads, a site that fails outright) is reported inline and the walk moves on to the next pair rather than aborting the whole run, and a walk interrupted partway through (Ctrl-C, a crash) leaves the pairs that already finished marked covered and the rest untouched. After each pair's own walk finishes, it prints one line summarizing that pair: how many detail pages were visited, how many were saved, and how many were dropped, broken out by reason (missing fields, no VIN, wrong model, failed to load, extraction failed, repeat) when non-zero. At the end, a summary table lists the same figures for every pair, followed by the usual new/moved/price-dropped/gone diff for the whole run. "New" lists a vehicle whose ledger row this run created, and also a VIN the ledger already knew about the first time a posting for it turns up on a source it's never been seen on before. A VIN the ledger already knew about on a given source is never listed under "New" again just because one of its postings on that same source turned up under a different URL (a relisting, or a site changing its own URL shape). That case shows under "Moved" instead, with the posting's old and new URL, unless the price dropped too, in which case it's folded into "Price drops" rather than shown twice.

Both cars.com and Carvana do have a working hybrid facet: an earlier build read the spike's SPIKE-FINDINGS.md recording as proof that neither site's search filter separates a hybrid or plug-in variant from its base model, but that recording doesn't hold up against the spike's own day-one capture. The cars.com response to a `toyota-corolla_hybrid` query that day is a complete, non-degraded results page whose own selected-filters still resolved back to the base `toyota-corolla`, so the earlier conclusion rested on a request whose facet just didn't apply that day, not on a bot-defended or otherwise broken response. Brian has since confirmed the same underscored value does apply when a person ticks the hybrid facet by hand, by pasting the URL his own warmed Edge browser built. cars.com's model facet lowercases the model name and collapses every run of non-alphanumeric characters to a single underscore after the make and a hyphen (`models[]=toyota-corolla_hybrid` for "Toyota Corolla Hybrid", `models[]=honda-cr_v_hybrid` for "Honda CR-V Hybrid"), so the search URL isolates the hybrid model directly. Carvana has no separate hybrid model at all; it filters its base-model search by fuel type instead (its `cvnaid` query parameter carries a base64url-encoded JSON filter naming the base model plus `"fuelTypes":["Hybrid"]`). One exception: for a model the scenario's `hybridOnlyFromModelYear` marks as hybrid-only from some year on (see below), cars.com queries both the hybrid facet and the base model, since `models[]` is a checkbox array the URL accepts more than one value on, and a base model that went hybrid-only is filed under its own base-model bucket for the post-cutover years rather than the separate hybrid one, while the pre-cutover years stay under the hybrid bucket; Carvana needs no such second query, since its fuel-type filter already matches those listings' actual fuel type regardless of the model name. With both search URLs now filtering to the exact requested model in the common case, the cap-doesn't-count-mismatches behavior above stays in place as a safety net rather than as the primary way the walk narrows down to real candidates: each site's candidate pool is still sized to twice `--max`, so a wrong-model or repeat page can be replaced by a spare link instead of spending the cap outright.

## Dealer grades

`odo search` and `odo walk` link every posting they upsert to a dealer, keyed by the dealer's
normalized name and location, when the source names one: Auto.dev and Marketcheck carry a dealer
name and city/state in their API response, and the walk's own extraction reads a dealer name and
location off the page text the same way it reads the vehicle's own fields.

`odo dealer grade --all` connects over CDP to the same operator-launched browser `odo walk` uses
(see above); if nothing is listening on the debugging port, it prints the identical launch-line
message and exits. It then looks up every dealer that has never been checked on CarEdge's Dealer
Ratings, paced and paused for challenges the same way the walk is. `odo dealer grade <vin>` does the
same for every distinct dealer behind that VIN's postings. Each dealer is looked up by searching
`https://caredge.com/dealers?q=<dealer name and location>` and reading the matching result card's
letter grade off the page. Each dealer is checked at most once: a dealer CarEdge positively says it
has no rating for (no card names it at all, or its own card is marked "Not rated") is stamped as
checked, since the fetched-at timestamp is what the cache rule keys off, not the grade itself, so it
is never looked up again on a later run. A page that merely failed to parse, a slow render, a
challenge, or a layout change, is left unstamped and retried on the next run rather than recorded as
either outcome. A card is only taken for a dealer when the dealer's location agrees with the card's
"City, ST" line one part at a time: both are split into a city and a state, lowercased, stripped of
punctuation and any trailing zip code, and "Ft.", "Mt." and "St." are read as Fort, Mount and Saint,
so the walk's "Winter Park, FL 32792" still finds its own "Winter Park, FL" card while a bare "Palm
Beach" never matches "West Palm Beach, FL". Two more outcomes leave the dealer unstamped and
retried, so they are never recorded as "not on CarEdge": a card carried the dealer's name but every
such card was in another city or state (the run prints "name matched, location did not"), and a
dealer whose location is only a city or only a state matched more than one same-named card, which
leaves it ungraded until a fuller location is known. A page that is CarEdge's own 404 means the search URL itself is dead, not that this
one dealer is unrateable: the run prints the dead URL and keeps going, but stops and exits non-zero
after three such pages in a row, leaving every dealer it never got to ungraded for the next run.
`odo show <vin>` prints the grade beside each posting; on the graded pages recorded so far, the
current dealer card carries no "why this grade" text, so no reason is shown alongside it. No F-grade
page has been recorded, so whether an F still carries one is unknown. `odo rank` shows each
vehicle's grade whenever at least one vehicle in that section has one: as a `gr` marker on the
ranked and over-budget lines, and as a Grade column in the insurance-unknown and F-graded-only
tables. It also lists any vehicle whose only postings come from F-range-graded dealers under its
own warning heading, still ranked, never hidden.

## Unmet target budgets

`odo rank` says plainly, in one line above the Ranked section, when no vehicle's expected during-loan monthly cost meets one or more of the scenario's target monthly budgets. The line names only the unmet targets and the cheapest vehicle's during-loan range, then points at `odo budget` for the purchase price each target allows. It reads the targets from the scenario rather than from `--budget`, and it still counts vehicles that `--budget` moved into the over-budget section, so a tight `--budget` does not hide it. It is not printed when every target is met or when there is no vehicle to name.

## Scenario

`scenarios/daughter.json` is the shipped scenario: target models, hard filters, and every cost assumption (APR, gas price, insurance, mpg, fees, residual value, and so on), each either a single pinned value or a loose min/max range. A loose input makes every cost line in `odo rank` and `odo show` a band instead of a point. Edit the file directly; there is no `odo scenario set` in v0.

`hybridOnlyFromModelYear` is an optional map, keyed the same way as `mpgByModel` and `insuranceMonthlyByModel` ("Make Model", e.g. `"Toyota Camry Hybrid"`), naming the model year a base model stopped shipping a gas-only trim. A candidate for that model at or above the named year matches this scenario's hybrid model even when its own listing text never says "Hybrid": Toyota dropped the gas-only Camry for model year 2025, so the shipped scenario ships `"Toyota Camry Hybrid": 2025`, and a plain "2025 Camry SE" candidate is kept and scored as a Camry Hybrid instead of dropped as a non-matching gas trim. A candidate below the named year, or a model with no rule at all, is still rejected unless its own text says "Hybrid", same as before this existed. `ScenarioLoader` rejects a scenario file whose key isn't one of `filters.allowedModels` or whose value isn't a four-digit year.

## Development

```
dotnet build          # Odonomics.sln: src/Odonomics and tests/Odonomics.Tests
dotnet test
dotnet build spike/spike.csproj   # the untouched spike app; not part of the solution
```

Tables render at 80 columns; set `NO_COLOR=1` to turn off color, same as any other tool that honors the convention.
