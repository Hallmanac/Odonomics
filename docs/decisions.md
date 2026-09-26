# Decisions

This page keeps the stories behind odo's rules: the reversals and the worked examples that explain why a rule looks the way it does. The rules themselves live in the topic pages, and each entry says which one. Entries are in date order.

## 2026-09-22: cars.com and Carvana do have a working hybrid facet

An earlier build read the spike's recording (now [spike-findings.md](spike-findings.md)) as proof that neither site's search filter separates a hybrid or plug-in variant from its base model. That recording doesn't hold up against the spike's own day-one capture. The cars.com response to a `toyota-corolla_hybrid` query that day is a complete, non-degraded results page, and its own selected filters still resolved back to the base `toyota-corolla`. So the earlier conclusion rested on a request whose facet just didn't apply that day, and not on a bot-defended or otherwise broken response.

That evening Brian pasted the URLs his own warmed Edge browser built when he ticked the Corolla Hybrid facet by hand. The cars.com one carried the same underscored value, `models[]=toyota-corolla_hybrid`. The Carvana one carried a `cvnaid` filter naming the base model plus `"fuelTypes":["Hybrid"]`. What differs between his requests and the spike's (cookies, an A/B bucket, some other session state) is still unconfirmed, so the walk trusts the URL an operator's own browser built.

His next walk confirmed it. On cars.com the Corolla Hybrid saved 10 of 10 and the Camry Hybrid saved 7 of 14, where both had saved nothing before, and on Carvana they saved 10 of 11 and 10 of 12 (PR #15). The rules that came out of this are in [walk.md](walk.md#carscom) and [walk.md](walk.md#carvana).

## 2026-09-23: the CarEdge search URL moved

A dealer grade run failed for all 33 dealers. CarEdge had retired its `/dealer-reviews?search=` path, and the pages we'd recorded from it were CarEdge's own 404. The replacement is `/dealers?q=<name>`, which renders a card with a grade and a score. Its layout is different too, because "Grade:" on the new page is a filter label and not a dealer's grade, and the old "Dealer Grade: X" text is gone.

We did three things about it (PR #20). The search URL now points at `/dealers?q=`, and the card parser was rebuilt from recorded pages of the new layout. A 404 is now recognized as a dead search URL and not as a dealer with no rating, and the run stops after three in a row, which tells a dead URL apart from dealers that merely aren't rated. The rules are in [dealers.md](dealers.md#the-grade-pass).

## 2026-09-23: Carvana dealers became one row per name

A Carvana detail page prints the buyer's pickup city, not where the hub is, so the ledger had been keying Carvana dealers by a location that said nothing about the car. That left a located "Carvana, <city>" row for each pickup city, beside the hub rows. It also meant `odo show` could print "Carvana Winder" in a vehicle's prior listings and a different Carvana beside it in Postings.

The fix keys every Carvana dealer on its name alone and takes the hub from the Marketcheck VIN history when the page names none. A one-time data migration applied it to what was already in the ledger on the first startup after the change. It stamped existing Carvana postings that had no dealer with "Carvana". It folded every located "Carvana, <city>" row into the bare row, re-pointing its postings and removing the row. It moved every Carvana posting on the bare row to the hub its VIN history names for its window, so that mismatch is gone for any posting the migration saw. The same fold merged a located hub row such as "Carvana Winder, Orlando FL" into the hub's row with no location, so each Carvana name ended up with one row.

A posting whose history names no hub stays on the bare row, and the migration cleared the bare row's grade so the next dealer grade run records its own verdict on it. The rules are in [dealers.md](dealers.md#how-a-dealer-is-identified).

## 2026-09-23: seller groups replaced dealer names in the seller count

The `N-sellers` flag first counted distinct dealer names, after normalizing case, punctuation, and a shared leading name stem. That held up until research ran over the 74-vehicle ledger and 22 of the 40 vehicles it fetched flagged, with the 2025 and 2026 Camry Hybrids showing 11 and 16 sellers. `odo show` on the sixteen-seller Camry told the story: fifty history rows with identical mileage (36,005), overlapping windows, and sixteen rooftop names that the shared-stem rule couldn't tie together. It was one physical car relisted by a dealer-group syndication feed, so the count was measuring a feed and not owners.

Now two listings share a seller group when their windows overlap or touch and their mileage is identical, whatever the dealer name says, and a new seller only counts when a later group starts after the earlier one ended and the mileage actually moved. The shared-stem rule stayed as a secondary merge (PR #19). The rule is in [research.md](research.md#red-flags).

## 2026-09-23: a same-dealer mileage drop became a note

The same research pass flagged a 2026 Camry Hybrid for `mileage-drop`, even though the placeholder and 500-mile or 1% exclusions were already in place. `odo show` made it clear the flag was correct by the rules and still wrong in spirit. Daytona Toyota's own listing was corrected from 4,703 to 3,852 miles between Sep 9 and Sep 10 on a demo car, which is 851 miles at the same dealer on consecutive days. That's an odometer correction on one listing, and not a rollback on a resold car.

So a drop between two listings in the same seller group, whose windows pick up within a day of each other, is now recorded as a note on the vehicle instead of a flag. The note for that car reads `mileage corrected 4,703 to 3,852 at Daytona Toyota on Sep 10` (PR #19). A drop across two different seller groups, or across a real gap even at the same seller, still flags. The rule is in [research.md](research.md#red-flags).

## 2026-09-24: cars.com searches twice for a model that went hybrid-only

Toyota's 2025 and later Camry is filed on cars.com under plain `camry` and never under `camry_hybrid`, so a search on the hybrid facet alone missed every post-cutover car. The first fix asked for both facets in one URL. The fourth walk run showed what that costs. The Camry Hybrid pair read "35 pages, 11 saved, 24 dropped (14 wrong model, 10 missing fields)". The fourteen wrong-model pages were 2019 to 2024 gas Camrys, which the combined URL has to return because cars.com takes a single `year_min` per request and it had to start at the scenario's minimum of 2018. Under best-match sorting the first real Camry Hybrid titles sat at positions 24 and 25 of 35, so the cap went mostly to cars the matcher would reject.

The walk now issues two searches for a model with a hybrid-only rule. One asks for the hybrid facet from the scenario's minimum year, and the other asks for the base-model facet from the hybrid-only year, so no page is spent on a car that has to be rejected. Review changed how the two share the cap, from half each to one shared cap where an unspent share rolls forward (PR #40). The rule is in [walk.md](walk.md#carscom).
