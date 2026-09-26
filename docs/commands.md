# Commands

You run odo from the repo root, either as `dotnet run --project src/Odonomics -- <command>` from a checkout, or as `odo <command>` once you've run `dotnet publish`. The published executable is named `odo`, and it's a local build rather than a release. Both forms need the repo root as the working directory, unless you pass a `--scenario` path that resolves from wherever you are, because the default scenario path is relative.

The table below lists the commands in the order you'd run them. [running.md](running.md) tells the same story as a single pass through a purchase.

```
odo search                          run Auto.dev and Marketcheck, upsert the ledger, print the diff
odo walk [cars.com|carvana|autotrader] [--model "Make Model"] [--max N] [--revisit]
                                    an operator-assisted browser walk of the shortlist
odo research [<vin> ...] [--refresh] [--quiet]
                                    safety ratings and VIN history, with a red-flags summary
odo dealer grade (--all | <vin>) [--refresh]
                                    look up each ungraded dealer's CarEdge grade
odo budget [--fulfillment delivery|pickup]
                                    the fixed monthly running cost, then the max purchase price
                                    for each target monthly budget
odo rank [--budget N] [--term M] [--detail] [--fulfillment delivery|pickup]
                                    score every vehicle in the ledger against the scenario
odo show <vin> [--refresh] [--all-history]
                                    everything odo knows about one vehicle, with its monthly cost
odo note <vin> "<text>"             attach a free-text note to a vehicle
odo finalist <vin>                  mark a vehicle a finalist
```

## search

`odo search` takes no flags of its own beyond `--scenario`. It runs both listing APIs, upserts what they return into the ledger, and prints what changed. The section on [what search asks for](#what-search-asks-for) below covers the rules it applies.

## walk

`odo walk` connects to a browser you've already launched, so [walk.md](walk.md) is the page to read before your first one. Its flags narrow or change what a walk visits:

- The optional site argument is `cars.com`, `carvana`, or `autotrader`, and leaving it out walks all three.
- `--model "Make Model"` narrows the walk to that one model, matching the scenario's own casing case-insensitively.
- `--max N` limits how many matching detail pages a site-and-model pair visits. Without it, a pair visits every car its search returns that the ledger doesn't already hold.
- `--revisit` opens a detail page for every link, including cars the ledger already holds.

## research

`odo research` fetches safety ratings and VIN history for many vehicles in one go, and [research.md](research.md) explains what it fetches and how it caches. With no VINs it covers every vehicle in the ledger that passes the scenario's filters, and given one or more VINs it covers just those. `--refresh` forces a re-fetch whatever the cache says, and `--quiet` swaps the per-vehicle progress lines for one counter line that overwrites itself.

## dealer grade

`odo dealer grade` looks up each ungraded dealer's grade on CarEdge, and it needs the same hand-launched browser the walk uses. Pass exactly one of them. `--all` grades every dealer the ledger has never checked, and `<vin>` grades the dealers behind that one VIN's postings that haven't been checked yet. `--refresh` also looks up dealers that already have a grade, so ones graded before the ledger kept the doc fee and add-ons note can pick them up. [dealers.md](dealers.md) has the details.

## budget

`odo budget` prints the fixed monthly running cost first, then the payment room and the maximum purchase price for each target monthly budget in the scenario. `--fulfillment delivery|pickup` overrides the scenario's `fulfillment` for the run, which decides whether the closing line says the maximum counts a shipping fee or a pickup fee. [cost-model.md](cost-model.md#budget) explains the figures.

## rank

`odo rank` scores every vehicle in the ledger against the scenario. `--budget N` moves any vehicle whose during-loan monthly cost is above N under a separate over-budget heading, rather than leaving it in the ranked list. `--term M` overrides the scenario's loan term with M months (48, 60, or 72, say). `--fulfillment delivery|pickup` overrides the scenario's `fulfillment`, so a run can price every Carvana car as collected instead of shipped without editing the file. `--detail` adds a line under each ranked row with that vehicle's loan payment, and [cost-model.md](cost-model.md#rank) explains what the columns mean.

## show

`odo show <vin>` prints the NHTSA decode, recalls, complaints, safety ratings, and the Marketcheck VIN history grouped by seller. It also shows the red flags, the postings, your notes, whether the vehicle is a finalist, and an itemized monthly cost. `--refresh` re-fetches the research even when the cache is fresh, and `--all-history` prints the raw one-row-per-sighting history underneath the grouped one. Because the monthly cost reads the scenario, `show` takes `--scenario <path>` like the other commands and needs the scenario file to resolve.

## note and finalist

`odo note <vin> "<text>"` attaches a free-text note to a vehicle. `odo finalist <vin>` marks a vehicle a finalist, and it refuses until a note mentions PPI and a note mentions Carfax or AutoCheck (one note can do both), so you'll want to add those first with `odo note`.

## What search asks for

`odo search` queries two APIs, Auto.dev and Marketcheck, once per target model in the scenario. Each API gets the scenario's zip and radius along with its minimum model year and maximum mileage, and both of those are sent as facets.

Both APIs are asked for used cars only, the same as the walk, which already drops new-car listings. Marketcheck's search carries `car_type=used`. Auto.dev's listings request carries `condition=used` together with `condition=certified pre-owned`, so certified pre-owned cars (which are used cars) still come through.

As a backstop against a filter an API ignores, a record either API still marks as new gets dropped, using Auto.dev's `condition` and Marketcheck's `inventory_type`. The rejection names the VIN, and the per-source summary line counts it among the rejected like any other rejection. A record with no such field is kept.

A price below $1,000 is a placeholder, not an asking price (Auto.dev has returned 0 for a car with no price posted). Both sources reject a candidate priced that low, with a rejection naming the VIN. A placeholder that's already stored on the ledger is ignored whenever odo picks a vehicle's current price. The diff below skips one under every heading except "Gone".

`odo search` and `odo walk` both finish by printing what changed in the ledger under up to five headings:

- "New" lists only vehicles whose ledger row this run created. That's one row per VIN, even when two sources returned it in the same run, and it names the source of the first posting the run saw.
- "Also listed at" lists a vehicle the ledger already knew that this run found on an additional source for the first time. It's one row per VIN naming every source the vehicle gained (for example "auto.dev, marketcheck"), and it prints only when there's something to show.
- "Moved" lists a known posting that came back under a different URL, whether that's a relisting or a site changing its own URL shape. It shows the posting's old and new URL, and a vehicle already known on that source never lands under "New" or "Also listed at" just because of it.
- "Price drops" lists postings whose price fell. When a moved posting's price dropped too, it's folded in here rather than shown twice.
- "Gone" lists postings the previous run had that this run no longer saw, each with a reason. The reasons are spelled out in [walk.md](walk.md#the-gone-reasons).
