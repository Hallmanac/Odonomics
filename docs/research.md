# Research and red flags

Both `odo show <vin>` and `odo research` pull the same free background research for a VIN, and both turn it into red flags. This page follows that in the order you'd hit it: what gets fetched, when it's cached, how a batch run reports, what the flags mean, and how rank and show display the results.

## What is fetched

On top of the NHTSA decode, recalls, and complaints that `odo show` has always fetched, there are two more pieces of research.

- NHTSA safety ratings: the overall and per-category (front crash, side crash, rollover) star ratings for that year, make, and model.
- Marketcheck VIN history: every prior listing recorded for the VIN, with its dealer, first and last seen dates, price, and mileage, along with the current listing's days on market. This one needs `Marketcheck:ApiKey`, and with no key set it degrades to a "could not fetch" line rather than failing the command.

## Caching and refresh

Both pieces are cached on the vehicle's ledger row with a researched-at stamp, and the NHTSA pieces also carry a fetched-at stamp of their own. They're only re-fetched in five cases:

- The vehicle has never been researched, or no NHTSA piece has ever come back for it.
- The cached research is more than seven days old.
- You pass `--refresh`.
- The Marketcheck VIN history was never successfully fetched, whether from a missing key or a failed call.
- The NHTSA recalls, complaints, or safety-ratings call previously couldn't be fetched, and each of those retries on its own, independently of the seven-day window.

A `show` or `research` inside the window, with a fetched history and no outstanding NHTSA failure, reads the cache and makes no network call. When a refresh does run, even one set off by a single failed NHTSA piece, the Marketcheck history is fetched again along with it.

api.nhtsa.gov has occasionally answered a healthy call with an HTML error page, or not at all, so odo retries a failed NHTSA call once and, if it still fails, only that piece (recalls, complaints, or safety ratings) shows a "could not fetch" reason while its last known-good value stays on display and in `odo rank`'s red-flag math.

## Running odo research

`odo research` runs that lookup for many vehicles in one go. With no VINs given, it covers every vehicle in the ledger that passes the scenario's hard filters (see [scenario.md](scenario.md#filters)), whether or not that vehicle needs a refresh. The one difference from ranking is that a vehicle with no current asking price is still eligible. That way the run always works the whole filtered set rather than only the slice that happened to be stale.

There's a one-second pause after each vehicle it actually fetches, except the last, so it doesn't hammer either API. A cached vehicle makes no network call and adds no pause, so a run over an entirely cached set finishes in a moment.

While it runs, the progress output prints one line per vehicle that was actually fetched this run, and nothing for a cached one, since nothing happened for it. The line says `researched` followed by the vehicle's flags as short tags, for example `2025 Toyota Camry Hybrid (4T1...): researched, 12-sellers, mileage-drop`. When only some of a vehicle's pieces came back, it's a yellow line naming which pieces failed and which data is still stored. A vehicle whose lookup fails outright gets a red "could not be reached" line, which in practice means the NHTSA VIN decode couldn't be fetched, and the rest of the batch carries on. If only the recalls, complaints, or safety-ratings calls fail, the vehicle counts as partially researched instead. One bad vehicle never fails the batch, and one bad NHTSA answer never fails the whole vehicle.

The command exits non-zero only when at least one vehicle was unreachable and no vehicle fetched this run came back with any data at all, fully or partially. A cached vehicle needs no fetch, so it never counts toward either side of that check.

Pass `--quiet` to replace all of that with a single counter line that overwrites itself in place, such as `researching 12/84 (8 fetched, 4 cached, 0 unreachable)`. When output is redirected to a file or a pipe there's no line to overwrite, so each update prints on its own line instead.

The run ends with a tally line, "N fully researched, M partially researched, K unreachable", which counts only the vehicles fetched this run. Then comes the summary, and it starts with a header for the whole filtered set (`84 vehicles, 40 fetched, 44 cached, 0 unreachable`). Below it is one line per vehicle in that set, fetched or cached alike, so the summary is the one place the whole set's flags show up together.

Each summary line starts with a one-letter marker for where the data came from this run: `F` for fetched, `C` for cached, or `U` for unreachable. It's followed by the year, make, model, VIN, and the flags as short tags (`mileage-drop`, `5-sellers`, `no-remedy-recall`, or `none`), and the line is bounded to 80 columns. An unreachable vehicle's tag reads `unreachable`, and when a vehicle's tags don't fit they collapse to a trailing `+N` and then to a plain count of flags. That keeps a batch of dozens of vehicles readable as a compact scan, and `odo show <vin>` is where the full detail for any one vehicle lives.

A vehicle with one or more failed pieces this run also carries a `partial` tag, so a line never reads as fully checked when part of its data is stale or missing. A run where every vehicle is already cached still prints the full summary, because the cache only skips the network calls and never the reporting.

## Red flags

The red-flags section is shown in full by `odo show` and as short tags by `odo research`. Each flag comes with a reason, and an empty section says so explicitly rather than staying blank (`odo show` prints "none found" and `odo research` prints `none`).

| Tag | Rule | Threshold |
|---|---|---|
| `mileage-drop` | A later listing of the VIN shows fewer miles than an earlier one | A drop of at least the larger of 500 miles or 1% of the earlier reading |
| `N-sellers` (for example `3-sellers`) | The VIN was listed by several distinct sellers close together | Three or more sellers within a 90-day window |
| `no-remedy-recall` | An open NHTSA recall has no remedy published yet | One or more such recalls |
| `low-safety-rating` | NHTSA's overall safety rating is low | Fewer than four stars |
| `price-spike` | The current price is well above the VIN's earlier prices | More than 15% above the average of at least two prior listing prices |
| `add-ons-dealer` | The vehicle's cheapest posting doesn't say whether its price includes fees, at a dealer CarEdge says sells add-ons | Fee posture `unknown` and a dealer add-ons note with a dollar amount above zero, such as `$358 add-ons` |

Two of these rules start from the same seller-group clustering of the listing history. Two listings belong to the same seller group when their windows (first seen to last seen) overlap, or touch within two days, and they show the same real mileage (more than 50 miles), regardless of what dealer name either one carries. That's what catches a dealer-group syndication feed relisting one physical car under a dozen-plus rooftop names at once. As a secondary merge, two listings also join the same group when their dealer names share a word-for-word stem, compared without regard to case. "Schaller Honda" and "Schaller Honda Subaru Mitsubishi" merge that way, and so do "Alm Hyundai Florence" and "ALM Hyundai Florence". That catches the same dealer spelled or franchised differently across sightings, even when the mileage moved between them.

Mileage-drop ignores a drop to zero or nearly zero, meaning 50 miles or less, because that's a placeholder or reset value and not a real odometer reading. It also ignores a drop measured against a sighting with no mileage at all. Neither kind of sighting serves as a baseline, so a rollback that straddles one still flags. A drop between two listings first seen on the same calendar day is the same snapshot re-scraped, so it's ignored too, and so is a drop smaller than the threshold, since that's rounding and minor re-entry noise. Both the 500-mile floor and the 1% figure are constants in `RedFlagsEvaluator`, and no scenario field or CLI flag exposes them yet.

A drop that survives all of that but sits between two listings in the same seller group, whose windows pick up within a day of each other, is judged a same-listing odometer correction and not a real rollback. It's recorded as a note on the vehicle instead, the same as your own `odo note` would be, and it reads `mileage corrected <from> to <to> at <dealer> on <date>`. A drop across two different seller groups, or across a real gap even at the same seller, still flags.

Sometimes the higher reading is a spike, meaning an earlier reading no higher than the lower one precedes it. Then the flag names the readings on either side of it, each with its date, because that shape is usually one mistyped odometer value (`one listing showed 181,407 miles on 2026-02-01 between readings of 85,960 on 2025-10-16 and 86,663 on 2026-06-01`). With no such earlier reading it reads `mileage dropped from X to Y between listings`.

N-sellers never counts a group with no dealer name at all, because there's no evidence to tell it apart from a repeat of the seller before or after it. Of the named groups, odo walks them in chronological order. The first group is seller one. Every later group counts as a new seller only when its window starts after the previous counted group's window ended, and it is not the case that both sides have a real, equal mileage reading. An absent or placeholder reading on either side is never proof that the mileage stayed the same, so it still counts as a change.

A group that overlaps the previous one, or resumes at the same real mileage, is folded in instead, since neither shape tells it apart from the same car sitting with the same owner. That's what separates a real dealer-to-dealer flip (sequential windows, mileage that actually moved) from syndication (overlapping windows, identical mileage), even when a shared-stem name never ties the rooftops together.

The seller-count threshold of three is a constant in `RedFlagsEvaluator`, not a scenario or CLI setting. The printed list caps at three names followed by "and N more", so a syndication feed's 30-plus rooftop names never dominates the line.

A no-remedy-recall flag only fires for an open recall with no remedy published. An open recall with a remedy already available doesn't raise a flag on its own, and `odo rank` shows the total open-recall count instead. NHTSA's free recallsByVehicle endpoint has no true per-VIN remedy-completed status, so "remedy available" here means the campaign's own remedy text has been published. It doesn't mean this VIN's owner already had the fix done.

Low-safety-rating only fires when NHTSA has an overall rating for the vehicle, so a car with no rating on record isn't flagged.

Price-spike compares asking prices, since they're checked against other listings' asking prices, and it needs at least two prior listing prices to average.

Add-ons-dealer is the one flag that comes from the vehicle's own postings and not from its VIN history, so it needs no research fetch. It looks only at the posting the vehicle is priced at, the cheapest active one. It fires when that posting's fee posture is `unknown`, meaning the walk read the page and the page never said whether its price includes the dealer's fees ([walk.md](walk.md#fee-statements)), and the posting's dealer has a CarEdge add-ons note that names a dollar amount, such as `$358 add-ons`. Its detail names the dealer and, when CarEdge printed one, its doc fee. A posting that is `all-in` or `itemized` never fires it, since the page said what its fees are, and neither does a posting whose posture was never read, a dealer whose note reads `No add-ons`, or a dealer CarEdge has no card for. Because it reads the stored note, it needs `odo dealer grade` to have looked the dealer up (and `--refresh` for a dealer graded before the ledger kept the note).

## How rank and show display research status

`odo rank` marks each vehicle's row with whether it's been researched yet and whether any red flag turned up. The marker is `-` for not yet researched, `clean`, or `flag`. A vehicle whose posting raises the add-ons-dealer flag shows `flag` even before it has been researched, since that flag needs no research. The row also has an `rc` figure showing the total open NHTSA recall count. It's `-` when the vehicle hasn't been researched, or when recalls have never been successfully fetched for it even though other pieces have. Both are display-only and never change the ranking math itself.

`odo show` renders the Marketcheck VIN history grouped by seller, using the clustering above. Each group is one row with its first and last seen dates, its dealer names, its price range, and its mileage range, so a syndicated 50-row history still reads as a handful of lines. A multi-seller group's dealer cell leads with its seller count, such as "16 sellers: ...", and lists at most three names followed by "and N more". The cell is never cut short, so a long name list wraps onto extra lines under the group's row. Pass `--all-history` to also print the raw one-row-per-sighting list underneath.

When Marketcheck reports no days on market for the current listing, `odo show` counts the days since the current seller group began its latest unbroken run of sightings. The current group is the one seen most recently, and it only counts when it was last seen within the past 14 days. Otherwise the car is treated as not currently listed, and `odo show` prints `(unknown)`.
