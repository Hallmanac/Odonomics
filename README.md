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
odo walk [cars.com|carvana|autotrader] [--model "Make Model"] [--max N] [--revisit]
                                     an operator-assisted walk (see below)
odo dealer grade [--all | <vin>]    look up each ungraded dealer's CarEdge grade (see below)
odo rank [--budget N] [--term M] [--detail]
                                     score every vehicle in the ledger against the scenario; --detail
                                     adds each vehicle's loan payment under its row (see below)
odo show <vin> [--refresh] [--all-history]
                                     NHTSA decode, recalls, complaints, safety ratings, Marketcheck
                                     VIN history (grouped by seller; pass --all-history for the raw
                                     one-row-per-sighting list too), red flags, postings, notes,
                                     finalist status, and an itemized monthly cost (see below)
odo research [<vin> ...] [--refresh] [--quiet]
                                     NHTSA safety ratings and Marketcheck VIN history for every
                                     vehicle in the ledger that passes the scenario's filters (or
                                     just the given VINs), with a red-flags summary (see below)
odo note <vin> "<text>"             attach a free-text note to a vehicle
odo finalist <vin>                  mark a vehicle a finalist (needs a PPI note and a Carfax/AutoCheck note)
odo budget                          the fixed monthly running cost, then the payment room and max
                                     purchase price (asking price plus any shipping fee) per
                                     target monthly budget in the scenario
```

`odo search` and `odo walk` both finish by printing what changed in the ledger under up to five headings. "New" lists only vehicles whose ledger row this run created, one row per VIN even when two sources returned it in the same run, with the source of the first posting the run saw. "Also listed at" lists a vehicle the ledger already knew that this run found on an additional source for the first time, one row per VIN naming every source it gained (for example "auto.dev, marketcheck"); it prints only when there is something to show. "Moved" lists a known posting that came back under a different URL, "Price drops" lists postings whose price fell, and "Gone" lists postings the previous run had that this run no longer saw, each with a reason.

Every command that scores against a scenario defaults to `scenarios/daughter.json`; pass `--scenario <path>` to use a different one.

`odo search` asks both APIs for used cars only, matching the walk, which already drops new-car listings: Marketcheck's search carries `car_type=used`, and Auto.dev's listings request carries `condition=used` together with `condition=certified pre-owned`, so certified pre-owned cars (which are used cars) still come through. As a backstop against a filter an API ignores, a record either API still marks as new (Auto.dev's `condition`, Marketcheck's `inventory_type`) is dropped with a rejection naming its VIN, and the per-source summary line counts it among the rejected like any other rejection. A record with no such field is kept. The scenario's minimum model year and maximum mileage are still sent as facets, unchanged.

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
  count, e.g. "16 sellers: ...", and lists at most three names, then "and N more"; the cell is never
  cut short, so a long name list wraps onto lines under the group's row), price range, and mileage
  range, so a syndicated 50-row history still reads as a handful of lines. When Marketcheck reports
  no days on market for the current listing, `odo show` counts the days since the current seller
  group (the one seen most recently) began its latest unbroken run of sightings, provided that
  group was last seen within the past 14 days; otherwise the car is treated as not currently
  listed and it prints `(unknown)`. Pass `--all-history` to also print the raw,
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
  to zero or nearly zero (a placeholder/reset value, not a real odometer reading), a drop measured
  against a sighting with no mileage at all (neither kind of sighting serves as a baseline, so a
  rollback that straddles one still flags), a drop between two listings first seen on the same
  calendar day (the same snapshot re-scraped, not two real readings), and a drop smaller than the
  larger of 500 miles or 1% of the prior mileage (rounding and minor re-entry noise). Both the
  500-mile floor and the 1% figure are constants in `RedFlagsEvaluator`; no scenario field or CLI
  flag exposes them yet. A drop that survives all of that but sits between two listings in the *same
  seller group* whose windows are on consecutive or overlapping days is judged a same-listing
  odometer correction rather than a real rollback (e.g.
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

Leave that window open. A bare `odo walk` then walks the whole shortlist in one command: cars.com first, then carvana, then autotrader, and on each site every model in the scenario's `allowedModels`, in order, printing which site and model it's about to visit before each. `odo walk cars.com`, `odo walk carvana`, or `odo walk autotrader` narrows it to that one site (still every model); `--model "Make Model"` narrows either form to that one model exactly, matching the scenario's own casing case-insensitively. Every site-and-model pair visits its search page (two of them for a model with a hybrid-only rule on cars.com, see below), scrolls and dwells like a person, and opens detail pages, with a random gap between each, until it has visited every car the search returns that the ledger does not already hold; there's a further random gap before moving on to the next model and before moving on to the next site. Every detail tab opens as a CDP background target and closes the same way, so the browser stays out of the way on your desktop the whole time; it never comes to the front of your other windows. The one exception is a bot-defense challenge: the walk beeps and brings the challenged page to the front so you can solve it by hand, then puts your previous window back in front once you press Enter, and the walk itself goes back to running in the background.

Autotrader is the third site, and the only one of the three that lists private sellers as well as dealers (cars.com and carvana carry none), so it is also where smaller dealers who do not list on cars.com turn up. Its search URL is `https://www.autotrader.com/cars-for-sale/used-cars/<make>/<base model>?zip=<zip>&searchRadius=<radius>&startYear=<minimum model year>&maxMileage=<maximum mileage>&sortBy=relevance`, so the zip, radius, minimum model year, and maximum mileage all come from the scenario like every other site's facets. There is no `sellerTypes` parameter, on purpose: leaving it out returns dealers and private sellers together (`sellerTypes=d` and `sellerTypes=p` narrow to one or the other). Autotrader has no separate hybrid model. Its model slugs are the base models (`prius`, `corolla`, `camry`, `insight`), and a slug it does not know silently returns every listing for the make, so a hybrid variant of a base model ("Corolla Hybrid", "Camry Hybrid") searches its base model and adds `&fuelTypeGroup=HYB`, while a model that is hybrid by name ("Prius", "Insight") gets no fuel facet. A hybrid-only-from-year rule needs no second search here for the same reason it needs none on Carvana: the fuel facet matches each listing's actual fuel type. A search page states its result count ("13 Matches") and then fills the rest of the page with cards the facets never asked for (other years, other models, new cars, and listings beyond the radius), and a page with no matches at all is filled with such cards entirely, so the walk reads the count first: a page reading "0 Matches" contributes no links, and a page with N matches contributes at most N listings, drawn from the cards the page marks as ordinary results (`clickType=listing`) so the sponsored card at the top, which ignores the search facets and is not one of the N, never takes a match's place. A detail link matches `/cars-for-sale/vehicle/<digits>`, and the walk stores it without its query string or a fragment such as `#purchaseConfidence`, so one listing is one candidate however many times a page links it. A private seller's detail page marks the seller with a "(Private Seller)" line, and the walk stores that posting with the dealer "Private seller" and no location rather than the person's name and city; every other rule (the model check, the cap, the pacing, the bot-challenge pause) applies to it unchanged.

A walked page counts as a hybrid model's match when the extraction names the hybrid, when the page's own title line reads "<year> <make> <base model> Hybrid" (for example "2023 Toyota Corolla Hybrid") even though the extraction split it into a base model and a trim, or when a fuel spec line in the page's own spec block says hybrid. Autotrader titles its pre-2025 Corolla Hybrids and Camry Hybrids like the gas cars ("Certified 2026 Toyota Corolla SE FWD") and says so only in the spec block, so the walk accepts a line that starts with "Hybrid: Gas/Electric", "Fuel Type: Hybrid", "Fuel: Hybrid", or "Engine: ... Hybrid" (any letter case), provided the extracted make and base model match and the line sits above any similar-vehicles or recommended-cars heading. Only the title line and that spec line decide it: a plain Corolla page that merely mentions a Corolla Hybrid elsewhere, or whose padded similar-vehicles cards carry a hybrid spec line, is still dropped as the wrong model, and the saved candidate carries the walked model either way. The drop reason for a hybrid query says what was checked ("no Hybrid in title, trim, or spec line"). The extraction also reads a nullable `fuelType` (Gas, Hybrid, Plug-in Hybrid, Electric) from the page's spec line, and the walk prints it on each page's console line, but it does not store it and the match never depends on it.

The walk is uncapped by default: the scenario's own facets (model, minimum year, maximum mileage, zip and radius) are the only filter, and a pair visits every car its searches return, following a paged site (Carvana) page after page until its own stop rules end it (the stated count reached, a page that adds nothing new, or "No exact matches"). That is a long run. Each detail page takes about a minute once the random gaps and scroll pauses are counted, so a first walk of a whole shortlist can run to hundreds of pages and several hours, and it grows with the ledger only by the cars that are new. Before its first detail page each pair prints how many it is about to visit and roughly how many minutes that is (for a pair with two searches under `--max`, only the count its first search starts with), and the start of the run prints the pairs, the limit if there is one, and how many postings the ledger already holds for each pair, so you can judge the length before committing to it; a pair with nothing new to open says so and moves on. `--max N` puts a limit on it: it caps matching detail pages per site-and-model pair, not per run or raw page visits: a detail page the walk opens and then rejects for being some other model (a plain Corolla on a Corolla Hybrid walk, say) or for repeating a VIN this pair already saved never spends any of the cap, so the walk keeps taking links until the cap is met with real matches or the search page runs out of links to try. A pair's own coverage is only stamped on the run once that pair's walk actually finishes; a pair that errors (a page that never loads, a site that fails outright) is reported inline and the walk moves on to the next pair rather than aborting the whole run, and a walk interrupted partway through (Ctrl-C, a crash) leaves the pairs that already finished marked covered and the rest untouched. After each pair's own walk finishes, it prints one line summarizing that pair: how many detail pages were visited, how many known links were kept current from their search cards without a visit (see below), how many were saved, and how many were dropped, broken out by reason (wrong model, new-car listing, missing fields, no VIN, repeat, failed to load, extraction failed) when non-zero. When that line would be wider than the console, the breakdown moves intact to a second line indented by four spaces rather than wrapping mid-parenthetical. At the end, a summary table lists the same figures for every pair, followed by the usual new/also-listed/moved/price-dropped/gone diff for the whole run. Every row under "Gone" ends with a short reason, so a car the walk simply no longer looks for is not mistaken for one that sold. Reasons are checked in this order: "below year facet" when the vehicle's year is under the scenario's minimum for its model, "over mileage" when its mileage is over the scenario's maximum, "search moved" when this run's zip or radius differs from those of the run that last saw the posting, and "not on search page" otherwise. Only that last reason suggests the car may have left the market. A run recorded before the ledger kept its zip and radius is never treated as a moved search. "New" lists only a vehicle whose ledger row this run created, once per VIN. A VIN the ledger already knew about is listed under "Also listed at" the first time a posting for it turns up on a source it has never been seen on before, naming every source it gained in the run. A VIN the ledger already knew about on a given source is never listed under either heading again just because one of its postings on that same source turned up under a different URL (a relisting, or a site changing its own URL shape). That case shows under "Moved" instead, with the posting's old and new URL, unless the price dropped too, in which case it's folded into "Price drops" rather than shown twice.

A repeat walk visits only new cars. Each detail page costs about a minute, and a car the ledger already holds has nothing new to say on it except its price and whether it is still listed, both of which the search page's own result card shows. So before the walk opens any detail page for a pair, it compares every link on the search pages against the postings the ledger already holds for that site, and a link it already holds is not opened. Instead the walk touches that posting from its card: it stamps the posting as seen in this run (once the pair's walk has finished, so a pair that fails or is interrupted leaves its known postings exactly as the last completed run left them), and when the card's asking price can be read and differs from the latest one on the ledger it records the new price. The diff then works exactly as it does after a full visit: a card showing a lower price than before is listed under "Price drops", and a posting no card shows any more is listed under "Gone", because it was not stamped. A touched link never enters the pool of detail visits and never spends any of `--max`, so `--max`, when given, now goes only to cars the ledger has never seen, and paging (Carvana) goes on past pages made only of known cars until the pool of new links is full or the search runs out. What a card cannot tell the walk stays as the last detail visit left it: the dealer, the Carvana shipping fee, and the vehicle's year, mileage, and trim. A card price is read as the first dollar amount on a cars.com card (its price comes before a price-drop amount, the mileage, and the "Used 2023 ..." title) and as the amount after "Current price:" on a Carvana card. Autotrader's card shape has not been confirmed from a recorded page (its cards show the price as bare digits, with no dollar sign), so it reads no card price yet: a known autotrader listing is still kept listed from its card, but a change in its price is only seen once `--revisit` opens its detail page. A card price under $1,000 is treated as a misread, not a price. The end-of-run table has a "Known" column beside "Pages" and "Saved" for the same count. `--revisit` turns all of this off and opens a detail page for every link, as the walk did before; use it to refresh the dealer or shipping fee of cars the ledger already holds, or to check a site whose cards the walk cannot yet read. A posting that was dropped on its detail visit (the wrong model, a new car) never reached the ledger, so it is opened again on every walk.

Both cars.com and Carvana do have a working hybrid facet: an earlier build read the spike's SPIKE-FINDINGS.md recording as proof that neither site's search filter separates a hybrid or plug-in variant from its base model, but that recording doesn't hold up against the spike's own day-one capture. The cars.com response to a `toyota-corolla_hybrid` query that day is a complete, non-degraded results page whose own selected-filters still resolved back to the base `toyota-corolla`, so the earlier conclusion rested on a request whose facet just didn't apply that day, not on a bot-defended or otherwise broken response. Brian has since confirmed the same underscored value does apply when a person ticks the hybrid facet by hand, by pasting the URL his own warmed Edge browser built. cars.com's model facet lowercases the model name and collapses every run of non-alphanumeric characters to a single underscore after the make and a hyphen (`models[]=toyota-corolla_hybrid` for "Toyota Corolla Hybrid", `models[]=honda-cr_v_hybrid` for "Honda CR-V Hybrid"), so the search URL isolates the hybrid model directly. Carvana has no separate hybrid model at all; it filters its base-model search by fuel type instead (its `cvnaid` query parameter carries a base64url-encoded JSON filter naming the base model plus `"fuelTypes":["Hybrid"]`). One exception: for a model the scenario's `hybridOnlyFromModelYear` marks as hybrid-only from some year on (see below), cars.com is searched twice, since a base model that went hybrid-only is filed under its own base-model bucket for the post-cutover years rather than the separate hybrid one, while the pre-cutover years stay under the hybrid bucket. The two facets cannot share one URL: cars.com takes a single `year_min` per request, so a combined search would have to start the base model at the scenario's minimum year too (2018 for the Camry Hybrid) and would fill up with gas Camrys from 2018 through 2024, which the walk then rejects as the wrong model after the cap is spent on them. So the walk issues one search for the hybrid facet (`models[]=toyota-camry_hybrid`) from the scenario's minimum year and a second for the base-model facet (`models[]=toyota-camry`) from the hybrid-only year itself (or the scenario's minimum year, if that is later), visiting them in that order. With no `--max` there is nothing to share out: both searches are loaded first (with a random gap between them), a link both carry is kept once, and every link is then visited, so the pair's page count is known before its first detail page. With `--max`, each search starts with an even share of the cap (the odd page goes to the hybrid-facet search) and its own link pool of twice its share, and a share a search cannot spend because its page runs out of candidates rolls forward to the next search; if the first search still has links it never visited when the second is done, the cap that is left goes to those, so the pair falls short of `--max` only when both searches are exhausted, and never saves more than `--max` in total. The random gap between detail pages also runs between one search's last detail page and the next search page, and the console labels each search page ("search 2 of 2") with its model facet. The pair still reports as one row in the per-pair line and the end-of-run table. The recorder writes one search text per search: `search.txt` for the first and `search-2.txt` for the second, with the detail pages numbered straight through the pair (`detail-1.txt` and on) so nothing collides. Beside each search text the recorder also writes `cards.json` (`cards-2.json` for the second), a list of the search page's detail links, each with its own text and the text of the result card it sits in, so a fixture can be cut from a real page later; `search.txt` itself is unchanged. A pair with one search behaves exactly as it always has. Carvana needs no such second search, since its fuel-type filter already matches those listings' actual fuel type regardless of the model name. Carvana is paged instead: it renders about 21 cards per result page but reports far more cars for the same filters (a hundred for one Prius search), so after collecting a search page's links the walk requests the same URL with `page=2`, `page=3` and so on, for as long as the candidate pool is below its size (unbounded without `--max`, twice the cap with it) and the last page added at least one link the pool did not already hold. Two more rules keep the paging to cars the facets asked for. The first page states how many cars match ("16 cars"), and that count bounds the links the pages contribute in total, taken in page order, even when the page shows a few more results than it states. And a result page that says "No exact matches" contributes no links and ends the paging, because everything after that notice is similar vehicles (other models, or other years and mileages) that the search never asked for; the console says the search ran out of exact matches on that page. Each page gets the same scroll and dwell as the first, is recorded as its own search text (`search.txt`, `search-2.txt`, `search-3.txt`), and is announced on the console ("search page, page 2"). Only the first page is required: if a later page fails to load, the walk prints a warning, stops paging, and goes on with the links it already has rather than failing the pair. The per-pair cap is unchanged, cars.com is not paged, and the pair still reports as one row in the per-pair line and the end-of-run table. cars.com's used search still mixes in new-car cards ("New 2027 Toyota Corolla Hybrid LE"), which the scenario can never rank, so the walk skips any card whose title begins with "New " when it collects detail links: those links never enter the candidate pool and never count against the cap. As a backstop, a detail page that still reads as a new car (a "New <year>" title, or an MSRP line with no Mileage line) is dropped as a "new-car listing", which likewise never spends the cap, rather than as "missing fields"; that reason stays for a used car whose page lacks its year, price, or mileage. Both search URLs also carry the scenario's minimum model year and maximum mileage as search facets, so the per-pair cap is spent only on cars the scenario can rank rather than on ones the walk would then drop as below the minimum year or over the mileage limit. cars.com takes them as the plain `year_min` and `mileage_max` query parameters; Carvana takes them inside the `cvnaid` JSON as `"year":{"min":N}` (singular; a `"years"` key is silently ignored) and `"mileage":{"max":N}`. Both values are read from the scenario, the minimum year per model (`minModelYearOverrides` wins over `minModelYear`, so the shipped scenario's Camry Hybrid searches from 2018 and every other model from 2019) and the maximum mileage from `maxMileage`, so changing the scenario changes the search with no code edit. With both search URLs now filtering to the exact requested model in the common case, the cap-doesn't-count-mismatches behavior above stays in place as a safety net rather than as the primary way the walk narrows down to real candidates: with `--max`, each site's candidate pool is still sized to twice the limit, so a wrong-model or repeat page can be replaced by a spare link instead of spending the cap outright; without it every link is visited and there is nothing to replace.

Carvana ships every car to the buyer, and its detail page prints a one-time shipping fee beside the price ("Free shipping" or, for example, "$1,590 shipping", and again in the delivery block), which depends on the car and on the buyer's zip. The walk reads that line straight off the page text, with no model in between: "Free shipping" stores 0, "$1,590 shipping" stores 1590, and a page that shows neither stores nothing, so an unknown fee is never mistaken for free. It reads only the page's own line, ahead of the "Need it sooner?" block that lists other cars. The fee is stored on the posting as its own `ShippingFee` column, not in the price history, because it is not part of the asking price, only of what taking the car home costs; each sighting replaces the last one's value, so a later walk that finds a different fee (or none) leaves the newer figure. Every other site stores no fee. Picking a car up at the Orlando hub instead of having it shipped is a choice the fee does not model; the ranking prices a Carvana car as shipped, since that is the fee the page shows. See Monthly cost below for how the fee counts.

## Dealer grades

`odo search` and `odo walk` link every posting they upsert to a dealer, keyed by the dealer's
normalized name and location, when the source names one: Auto.dev and Marketcheck carry a dealer
name and city/state in their API response, and the walk's own extraction reads a dealer name and
location off the page text the same way it reads the vehicle's own fields. The location key expands a city's "Ft.", "Mt." and "St." to Fort, Mount and Saint and drops a trailing zip code, so "St. Augustine, FL" and "Saint Augustine, FL" are one dealer. A carvana detail page
usually names no dealer, so the walk stores such a posting with the dealer "Carvana" rather than
none; a page that does name a hub, such as "Carvana Winder", keeps that name. Carvana is one dealer
row per name and never per city: a Carvana page prints the buyer's pickup city, not where the hub
is, so the ledger keys every Carvana dealer ("Carvana" or a hub such as "Carvana Winder") on its
name alone and drops any location a walk or an API source reports for it. When the page names no
hub, the walk still looks in the Marketcheck VIN history the ledger already stores for that VIN: if
a Carvana hub's listing overlaps the posting's own sighting window (give or take two days, since
Marketcheck's dates trail the walk by about a day), the posting is linked to that hub, and the most
recent such listing wins. A later sighting that names no hub moves a posting only off the bare
"Carvana" row (or a legacy located one) and only onto a hub the history names; it never replaces a
link to any other dealer, so a posting stored under a hub the page named stays under it. On the
first startup after the update, existing carvana postings with no dealer are stamped "Carvana",
every located "Carvana, <city>" row an older walk left behind is folded into the bare row (its
postings re-pointed, the row removed), and every carvana posting on the bare row moves to the hub
its VIN history names for its window, so `odo show` does not print "Carvana Winder" in the prior
listings and a different Carvana beside it in Postings for any posting the migration saw. The same
fold applies to a located hub row ("Carvana Winder, Orlando FL"): it is merged into the hub's row
with no location, so each Carvana name ends up with one row. A posting whose history names no hub
stays on the bare row, and the bare row's grade is cleared so the next dealer grade run records its
own verdict on it. A posting walked later for a VIN whose history the ledger did not yet hold stays
on the bare row until the next carvana walk moves it, so the two can briefly disagree until then.

The dealer grade pass has one rule for Carvana: the bare "Carvana" row is never looked up.
CarEdge lists Carvana as a card per hub ("Carvana Orlando" and so on), so a search for the bare name
returns hub cards, and taking one would grade the chain by an arbitrary hub. The run stamps the row checked with a recorded reason
and no grade (`odo show` prints it as "Carvana (ungraded)"), and every hub row is graded by its hub
name like any other dealer. The parser backs that up: a search for the bare name "Carvana" only
accepts a card named exactly "Carvana", never a hub's card that merely contains it, whatever
location the dealer row has.

The "Private seller" row that autotrader's private listings share is never looked up either: a
private seller is a person, and CarEdge has no Dealer Rating for one, so a search for that name could
only match an unrelated business. The run stamps it checked with a recorded reason and no grade, the
same way it does the bare "Carvana" row.

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
"City, ST" line one part at a time: both are split into a city and a state, case-folded, stripped of
punctuation and any trailing zip code, and "Ft.", "Mt." and "St." in the city (never the state, so a
bare "MT" stays Montana) are read as Fort, Mount and Saint, so the walk's "Winter Park, FL 32792"
still finds its own "Winter Park, FL" card while a bare "Palm Beach" never matches "West Palm Beach,
FL". Two more outcomes leave the dealer unstamped and retried, so they are never recorded as "not on
CarEdge": a card carried the dealer's name but every such card was in another city or state (the run
prints "name matched, location did not"), and a dealer whose location is only a city or only a state
matched more than one same-named card, which leaves it ungraded until a fuller location is known. A
page that is CarEdge's own 404 means the search URL itself is dead, not that this one dealer is
unrateable: the run prints the dead URL and keeps going, but stops and exits non-zero after three
such pages in a row, leaving every dealer it never got to ungraded for the next run.
`odo show <vin>` prints the grade beside each posting; on the graded pages recorded so far, the
current dealer card carries no "why this grade" text, so no reason is shown alongside it. No F-grade
page has been recorded, so whether an F still carries one is unknown. `odo rank` shows each
vehicle's grade whenever at least one vehicle in that section has one: as a `gr` marker on the
ranked and over-budget lines, and as a Grade column in the insurance-unknown and F-graded-only
tables. It also lists any vehicle whose only postings come from F-range-graded dealers under its
own warning heading, still ranked, never hidden.

## Unmet target budgets

`odo rank` says plainly, in one line above the Ranked section, when no rankable vehicle's expected during-loan monthly cost meets one or more of the scenario's target monthly budgets. The line names only the unmet targets and the cheapest vehicle's during-loan range, then points at `odo budget` for the purchase price each target allows. It reads the targets from the scenario rather than from `--budget`, and it still counts vehicles that `--budget` moved into the over-budget section, so a tight `--budget` does not hide it. It is not printed when every target is met or when there is no rankable vehicle to name. A rankable vehicle is one that passes the scenario's filters, has a known insurance figure, and has a current price, so excluded and insurance-unknown vehicles are never considered.

## Monthly cost

Every purchase-price figure the cost model uses is the asking price plus the posting's shipping fee when one is known (see the Carvana notes in the assisted walk section), so a car shipped from far away is compared honestly with one picked up in Orlando. A vehicle with several postings is priced at the one that is cheapest to take home, asking price plus fee, and a posting with no stored fee costs its asking price, as it always did. The fee is treated as part of the purchase price, so it is financed, taxed, and depreciated with the car. The price red flags keep comparing asking prices, since they are checked against other listings' asking prices. `odo budget` solves for the same figure: its max purchase price is the most the asking price plus any shipping fee can come to, so a car with a $1,590 fee needs an asking price $1,590 below that figure, and the command says so under its table.

The during-loan figure `odo rank` prints per vehicle is not a car payment. It is the loan payment plus the scenario's running costs (insurance, fuel, maintenance, and the emergency reserve), and the 10yr avg figure carries the same running costs. Three places break it apart, all reading the same cost figures the scenario produces for each vehicle:

- `odo show <vin>` prints a "Monthly cost" block after the postings, for a vehicle with a current asking price. When that posting has a shipping fee, the block opens with the asking price, the fee on its own line under it, and the purchase price they add up to, which is the price the figures below are computed from; a vehicle with no stored fee prints none of those three lines. It lists the loan payment, insurance, fuel, maintenance, and reserve, then the during-loan total and the 10-year average. Each figure is the scenario's expected value, or a `$low-$high` range when an input feeding it is loose. The during-loan and 10-year totals are the figures `odo rank` shows. The itemized lines are rounded to whole dollars once, so their low ends, and their high ends, add up to the during-loan total printed beneath them, and `odo rank --detail` takes its loan payment from that same rounding, so the two commands print the identical payment for a vehicle. `show` prices a vehicle the scenario's filters would exclude too, but a vehicle with no current price, or a model the scenario has no insurance or mpg figure for, gets a line saying why nothing was computed. Because it reads the scenario, `odo show` takes `--scenario <path>` like the other commands and needs the scenario file to resolve.
- `odo rank` prints one line above its sections saying what During-loan and 10yr avg include, for example "During-loan is the loan payment plus running costs of about $268-$279 a month (insurance, fuel, maintenance, reserve). 10yr avg spreads those costs, the down payment, the loan payments, and resale value over the hold." Only During-loan is the loan payment plus running costs, so the note offers no remainder for 10yr avg. When `--detail` is set the note leaves out its pointer to `--detail`, since the payments are printed right below. The figure is computed from the ranked and over-budget vehicles' running costs, so it is a single figure or range when they agree and the span across them when they differ (insurance is per model). It is not printed when there is no rankable vehicle.
- `odo rank --detail` adds one line under each ranked and over-budget row, giving that vehicle's loan payment beside its during-loan total (`loan payment $324-$348  of during $592-$627`). It is a separate line rather than a column, so the row above it keeps its 80-column fit. A vehicle whose posting has a shipping fee also gets a line above the payment naming it in the breakdown (`price $16,410 asking + $1,590 shipping = $18,000`), and the price on its row is that purchase price; free shipping shows as `$0 shipping`, and a vehicle with no stored fee gets no such line.

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
