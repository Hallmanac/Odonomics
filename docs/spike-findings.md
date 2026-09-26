# Spike findings

**Status: day 1 of 3.** The build session ran day one below. The operator runs days two and
three on the same machine with:

```
dotnet run --project spike
```

run from the repo root, after the following are in place (none of this is set up by the command
itself):

- **.NET 10 SDK** (the spike targets `net10.0`).
- **Playwright's Chromium browser**, installed once with `pwsh spike/bin/Debug/net10.0/playwright.ps1
  install chromium` (or `playwright install chromium` if the Playwright CLI is on PATH). Without
  this, every page-walk source (cars.com, carvana) fails immediately with a
  Playwright-executable-not-found error and is reported as "crashed."
- **A display to open a visible Chrome window on.** Page walks run headed
  (`Headless = false`), by design (see "Cost and time" below and the surprises on bot defenses):
  this will not run over SSH with no display attached.
- **An authenticated `claude` CLI on PATH.** Extraction shells out to it (see `Extraction.cs`);
  without it, every detail-page fetch across cars.com, carvana, and craigslist fails extraction.
  This also means extraction cost is billed to the operator's own Claude account through their
  existing CLI session, not to any project-scoped key: the "$0.75" figure below came out of that
  account, not a metered API key.
- **Both API keys**, in `dotnet user-secrets --id odonomics-spike` under `AutoDev:ApiKey` and
  `Marketcheck:ApiKey` (or the `AUTODEV__APIKEY` / `MARKETCHECK__APIKEY` environment variables), per
  `spike/SPIKE.md`.

Each run auto-detects which day it is from `spike/recorded/run-state.txt` (only once the run
finishes; an interrupted run does not consume a day, see `RunState.Peek`/`RunState.Commit`),
writes that day's raw responses under `spike/recorded/<source>/day<N>/`, and appends a row per
source to the table below. After each day, commit the new `spike/recorded/` contents and the
updated table rows and push. A follow-up run on this task finalizes the verdict once day three's
rows exist.

<!-- This section is appended to by `dotnet run --project spike`. Everything else in this file is written by hand. -->

<!-- SPIKE-RESULTS:BEGIN -->
| Day | Source | Candidates found | Candidates with VIN | VINs unique to source | Wall time | Dollars | Failures |
|---|---|---|---|---|---|---|---|
| 1 | auto.dev | 15 | 15 | 11 | 0.0 min | $0.00 | none |
| 1 | marketcheck | 12 | 12 | 7 | 0.0 min | $0.00 | none |
| 1 | cars.com | 44 | 3 | 1 | 3.4 min | $0.19 | Toyota Camry Hybrid: blocked on search page (bot-defense challenge page (title contained "just a moment")); Toyota Prius: skipped, cars.com search already blocked earlier in this run; Honda Insight detail 1: site returned a non-matching vehicle (2019 Honda Insight LX), excluded from counts but kept for the extraction hand-check; Honda Insight detail 4: blocked (bot-defense challenge page (title contained "just a moment")); Honda Insight detail 6: blocked (bot-defense challenge page (title contained "just a moment")); Toyota Corolla Hybrid detail 7: blocked (bot-defense challenge page (title contained "just a moment")); Toyota Corolla Hybrid detail 8: site returned a non-matching vehicle (2024 Toyota Corolla LE), excluded from counts but kept for the extraction hand-check; Toyota Corolla Hybrid detail 9: site returned a non-matching vehicle (2012 Toyota Corolla S), excluded from counts but kept for the extraction hand-check; Toyota Corolla Hybrid detail 10: blocked (HTTP 403); Toyota Corolla Hybrid detail 11: site returned a non-matching vehicle (2026 Toyota Corolla SE), excluded from counts but kept for the extraction hand-check; Toyota Corolla Hybrid detail 12: blocked (bot-defense challenge page (title contained "just a moment")) |
| 1 | carvana | 84 | 8 | 8 | 6.8 min | $0.56 | Toyota Corolla Hybrid detail 7: site returned a non-matching vehicle (2026 Toyota Corolla LE), excluded from counts but kept for the extraction hand-check; Toyota Corolla Hybrid detail 8: site returned a non-matching vehicle (2019 Toyota Corolla XLE), excluded from counts but kept for the extraction hand-check; Toyota Corolla Hybrid detail 9: site returned a non-matching vehicle (2025 Toyota Corolla LE), excluded from counts but kept for the extraction hand-check; Toyota Corolla Hybrid detail 10: site returned a non-matching vehicle (2018 Toyota Corolla SE), excluded from counts but kept for the extraction hand-check; Toyota Corolla Hybrid detail 11: site returned a non-matching vehicle (2026 Toyota Corolla Hatchback SE), excluded from counts but kept for the extraction hand-check; Toyota Corolla Hybrid detail 12: site returned a non-matching vehicle (2020 Toyota Corolla LE), excluded from counts but kept for the extraction hand-check; Toyota Camry Hybrid detail 13: site returned a non-matching vehicle (2025 Toyota Camry LE), excluded from counts but kept for the extraction hand-check; Toyota Camry Hybrid detail 14: site returned a non-matching vehicle (2016 Toyota Camry LE), excluded from counts but kept for the extraction hand-check; Toyota Camry Hybrid detail 15: site returned a non-matching vehicle (2022 Toyota Camry XLE), excluded from counts but kept for the extraction hand-check; Toyota Camry Hybrid detail 16: site returned a non-matching vehicle (2026 Toyota Camry Nightshade), excluded from counts but kept for the extraction hand-check; Toyota Camry Hybrid detail 17: site returned a non-matching vehicle (2026 Toyota Camry LE), excluded from counts but kept for the extraction hand-check; Toyota Camry Hybrid detail 18: site returned a non-matching vehicle (2011 Toyota Camry LE), excluded from counts but kept for the extraction hand-check; Toyota Prius detail 20: site returned a non-matching vehicle (2015 Toyota Prius Two), excluded from counts but kept for the extraction hand-check; Toyota Prius detail 22: site returned a non-matching vehicle (2016 Toyota Prius Two Eco), excluded from counts but kept for the extraction hand-check; Toyota Prius detail 23: site returned a non-matching vehicle (2012 Toyota Prius Four), excluded from counts but kept for the extraction hand-check; Toyota Prius detail 24: site returned a non-matching vehicle (2019 Toyota Prius Limited), excluded from counts but kept for the extraction hand-check |
| 1 | craigslist | 0 | 0 | 0 | 0.2 min | $0.00 | Honda Insight: no matching postings found on the search page; Toyota Corolla Hybrid: no matching postings found on the search page; Toyota Camry Hybrid: no matching postings found on the search page; Toyota Prius posting 1: search match did not match the query (2019 Toyota Prius Limited), excluded from counts but kept for the extraction hand-check |
<!-- SPIKE-RESULTS:END -->

All five sources ran on day one; none needed a "could not run" excuse (both API keys were present
in `dotnet user-secrets --id odonomics-spike`, and every page walk got at least some real traffic
through). Cars.com never needed the Autotrader fallback: it kept producing candidates through the
run even while individual requests were being blocked, so `primary.Candidates.Count > 0` stayed
true the whole time.

## Extraction hand-check

Every page-walk candidate that made it through model extraction, matching the target query or
not, was kept as a data point for judging extraction accuracy: matching a query and extracting a
page correctly are different questions, and a wrong-model listing is just as good a test of
"did the model read the VIN off this page correctly" as a right-model one. That gives a
hand-check pool of 32 page-walk candidates (7 from cars.com, 24 from carvana, 1 from craigslist),
comfortably over the 20 the pass mark asks for.

For each one, the VIN, price, and mileage were read directly off the recorded raw HTML (stripped
of markup, independent of the extraction step) and compared to what the model extracted:

| # | Source | VIN (page) | VIN (extracted) | VIN match | Price (page) | Price (extracted) | Price match | Mileage (page) | Mileage (extracted) | Mileage match |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | cars.com | 19XZE4F19KE023626 | 19XZE4F19KE023626 | yes | $16299 | $16299 | yes | 101731 | 101731 | yes |
| 2 | cars.com | 19XZE4F59LE013764 | 19XZE4F59LE013764 | yes | $21094 | $21094 | yes | 87613 | 87613 | yes |
| 3 | cars.com | 19XZE4F52ME000999 | 19XZE4F52ME000999 | yes | $19393 | $19393 | yes | 69599 | 69599 | yes |
| 4 | cars.com | 19XZE4F1XKE019164 | 19XZE4F1XKE019164 | yes | $18998 | $18998 | yes | 80924 | 80924 | yes |
| 5 | cars.com | 5YFB4MDE7RP171910 | 5YFB4MDE7RP171910 | yes | $23492 | $23492 | yes | 21944 | 21944 | yes |
| 6 | cars.com | 5YFBU4EE3CP073561 | 5YFBU4EE3CP073561 | yes | $10792 | $10792 | yes | 139249 | 139249 | yes |
| 7 | cars.com | JTND4MBE2T3257732 | JTND4MBE2T3257732 | yes | $26990 | $26990 | yes | 2865 | 2865 | yes |
| 8 | carvana | 19XZE4F58LE012282 | 19XZE4F58LE012282 | yes | $23990 | $23990 | yes | 37003 | 37003 | yes |
| 9 | carvana | 19XZE4F59KE029672 | 19XZE4F59KE029672 | yes | $21990 | $21990 | yes | 49354 | 49354 | yes |
| 10 | carvana | 19XZE4F98NE001692 | 19XZE4F98NE001692 | yes | $24990 | $24990 | yes | 46987 | 46987 | yes |
| 11 | carvana | 19XZE4F96NE002808 | 19XZE4F96NE002808 | yes | $25990 | $25990 | yes | 56765 | 56765 | yes |
| 12 | carvana | 19XZE4F50ME003643 | 19XZE4F50ME003643 | yes | $19590 | $19590 | yes | 84944 | 84944 | yes |
| 13 | carvana | 19XZE4F10KE006004 | 19XZE4F10KE006004 | yes | $22590 | $22590 | yes | 35141 | 35141 | yes |
| 14 | carvana | 5YFB4MDE6TP382040 | 5YFB4MDE6TP382040 | yes | $23990 | $23990 | yes | 20090 | 20090 | yes |
| 15 | carvana | 2T1BURHE7KC140900 | 2T1BURHE7KC140900 | yes | $18290 | $18290 | yes | 96027 | 96027 | yes |
| 16 | carvana | 5YFB4MDE1SP287190 | 5YFB4MDE1SP287190 | yes | $24990 | $24990 | yes | 8528 | 8528 | yes |
| 17 | carvana | 2T1BURHE5JC968653 | 2T1BURHE5JC968653 | yes | $19590 | $19590 | yes | 58271 | 58271 | yes |
| 18 | carvana | JTND4MBE0T3267076 | JTND4MBE0T3267076 | yes | $28590 | $28590 | yes | 753 | 753 | yes |
| 19 | carvana | 5YFEPRAEXLP133651 | 5YFEPRAEXLP133651 | yes | $19990 | $19990 | yes | 58220 | 58220 | yes |
| 20 | carvana | 4T1DAACK1SU119930 | 4T1DAACK1SU119930 | yes | $26590 | $26590 | yes | 30623 | 30623 | yes |
| 21 | carvana | 4T1BF1FK1GU150767 | 4T1BF1FK1GU150767 | yes | $19990 | $19990 | yes | 36712 | 36712 | yes |
| 22 | carvana | 4T1F11AK4NU068553 | 4T1F11AK4NU068553 | yes | $27990 | $27990 | yes | 30565 | 30565 | yes |
| 23 | carvana | 4T1DAACK7TU211240 | 4T1DAACK7TU211240 | yes | $34590 | $34590 | yes | 11640 | 11640 | yes |
| 24 | carvana | 4T1DAACK5TU677152 | 4T1DAACK5TU677152 | yes | $28990 | $28990 | yes | 22156 | 22156 | yes |
| 25 | carvana | 4T1BF3EK8BU647711 | 4T1BF3EK8BU647711 | yes | $13990 | $13990 | yes | 91241 | 91241 | yes |
| 26 | carvana | JTDACAAU5R3038094 | JTDACAAU5R3038094 | yes | $34990 | $34990 | yes | 19223 | 19223 | yes |
| 27 | carvana | JTDKN3DU9F0459576 | JTDKN3DU9F0459576 | yes | $17590 | $17590 | yes | 81091 | 81091 | yes |
| 28 | carvana | JTDACAAU1S3060017 | JTDACAAU1S3060017 | yes | $29590 | $29590 | yes | 26613 | 26613 | yes |
| 29 | carvana | JTDKARFU0G3506053 | JTDKARFU0G3506053 | yes | $21990 | $21990 | yes | 49750 | 49750 | yes |
| 30 | carvana | JTDKN3DU7C5436201 | JTDKN3DU7C5436201 | yes | $14590 | $14590 | yes | 113776 | 113776 | yes |
| 31 | carvana | JTDKARFUXK3084253 | JTDKARFUXK3084253 | yes | $27590 | $27590 | yes | 17171 | 17171 | yes |
| 32 | craigslist | (none on page) | (none) | yes | $23000 | $23000 | yes | 30050 (labeled "odometer:", not "mi.") | 30050 | yes |

**Result: 32/32 (100%) VIN precision, 32/32 (100%) price within 1%, 32/32 (100%) mileage within
1%.** Row 32's page mileage is not written as "N mi." on that Craigslist post (it says
`odometer: 30,050`), which is why an automated `\d+ mi\.` regex alone would miss it; reading the
actual page confirms the model extracted the right number anyway. No row shows an invented VIN
where the page had none, and no row shows a wrong VIN.

## Manual Cars.com count and coverage

**Correction (post-review): the manual counts originally recorded here for Honda Insight (9) and
Toyota Corolla Hybrid (84) do not match Cars.com's own recorded search-page facets for the
identical query, and have been replaced below with the counts read directly off the committed
`spike/recorded/cars.com/day1/*-search.html` fixtures.** Neither Camry Hybrid nor Prius has a
usable count at all: Cars.com's own search for both was blocked before any facet data was
captured (see the report table), so the "30" and "74" figures previously stated for them were not
backed by any recorded artifact and have been removed rather than restated.

For Honda Insight, `spike/recorded/cars.com/day1/Honda-Insight-search.html` carries the site's own
model-facet JSON with `"value":"honda-insight","selected":true,"summary":"3"`. The wording "3 Honda
Insight vehicles" is real but sits in the page's chat-widget payload
(`"open_chat_cta":"I found 3 Honda Insight vehicles. Want personalized recommendations?"`), not a
page heading; the page's own `<h1>` reads "Used Honda Insight for sale near Daytona Beach, FL" and
carries no count. The query the app actually sent (`makes[]=honda&models[]=honda-insight`, 50
miles of 32114) is the same query a person would land on by using Cars.com's own dropdown, so 3 is
the correct same-query denominator, not 9.

For Toyota Corolla Hybrid, the walk itself did not query hybrid inventory: see the
`PageWalkSources.cs` finding under "surprises" below. Cars.com's own recorded response for that
day-one request shows `selected_search_filters` resolved to plain `["toyota-corolla"]`, not
`["toyota-corolla_hybrid"]`, and the chat-widget payload reads
`"open_chat_cta":"I found 195 Toyota Corolla vehicles in your area. Can I help you narrow it
down?"` (gas and hybrid trims together, all model years); the page's own `<h1>` again carries no
count. The hybrid-specific facet, visible in the same page's own
facet JSON but never actually selected by the request, reads
`"name":"Corolla Hybrid","value":"toyota-corolla_hybrid","summary":"21"`. None of 195, 21, or the
previously-stated 84 are the same quantity, and since the request itself asked for the wrong
inventory, no same-query denominator exists for this group from day one at all; it needs a
corrected walk (the model-slug fix in `PageWalkSources.cs` following this review) on a future day.

| Query group | Cars.com listing count (50 mi of 32114, model only) | Source |
|---|---|---|
| Honda Insight | 3 | Cars.com's own recorded facet JSON and chat-widget payload, day one |
| Toyota Corolla Hybrid | not measurable from day one | day one's request resolved to plain Corolla, not Corolla Hybrid; see above |
| Toyota Camry Hybrid | not measurable from day one | Cars.com search was blocked before any facet data was captured |
| Toyota Prius | not measurable from day one | Cars.com search was skipped (blocked earlier in the same run) |

Honda Insight is the only group with a valid same-query comparison. Cars.com's own page lists 3
in-radius Honda Insight listings (`19XZE4F19KE023626`, `19XZE4F59LE013764`, `19XZE4F52ME000999`),
but one of those three, `19XZE4F19KE023626` at 101,731 miles (`data-mileage="101731"` on that
page's own anchor), is over this spike's 100,000-mile cap and therefore out of scope, leaving an
in-scope denominator of 2. Auto.dev and Marketcheck together found 4 unique matching VINs on day
one (`19XZE4F52ME000999`, `19XZE4F95ME001552`, `19XZE4F93NE011501`, `19XZE4F59LE013764`); of
those, only the two that actually appear on Cars.com's own page (`19XZE4F59LE013764`,
`19XZE4F52ME000999`) count toward coverage of Cars.com's in-scope listings. The other two,
`19XZE4F95ME001552` and `19XZE4F93NE011501`, are inventory Cars.com never listed here at all, so
they cannot cover anything in this denominator; they show the free sources finding inventory
Cars.com misses, which is worth noting, but is not itself coverage of Cars.com's listings.

**Coverage: 2 of 2 in-scope Honda Insight listings, 100 percent.** That clears the 80 percent pass
mark on the one group that could be measured cleanly on day one; the earlier "roughly 50
percent... FAIL" verdict was computed against a manual count this branch's own recorded evidence
does not support. Coverage for the other three groups still cannot be stated as a number
from day one: none of them has a valid same-query Cars.com count to compare against, for the
reasons in the table above, not because of any risk to the browser profile (a person counting
listings in their own browser consumes no Playwright profile; PageWalkEngine discards its profile
after every single page load regardless). A corrected measurement for the remaining three groups
is work for a future day.

## Pass mark

1. **VIN precision 100 percent on ≥20 hand-checked page-walk candidates: PASS.** 32/32, see above.
2. **Price and mileage within 1 percent for ≥95 percent of the sample: PASS.** 32/32 for both.
3. **Two free/cheap sources find ≥80 percent of the manual Cars.com count: PARTIAL, on the only
   group day one can actually measure.** Honda Insight, the one group with a same-query Cars.com
   count backed by this branch's own recorded evidence, measures at 100 percent (2 of 2 in-scope
   listings), clearing the mark. The other three groups have no valid same-query denominator from day one at
   all (see "Manual Cars.com count and coverage" above): not a FAIL, since there is no clean
   number to fail against, but not a clean PASS either until a corrected walk measures them. This
   replaces the original "roughly 50 percent... FAIL" verdict, which was computed against a manual
   count this branch's own recorded fixtures contradict.
4. **Every page walk completes on all three days without a code or prompt change after day one:
   PENDING DAYS TWO AND THREE.** Neither Cars.com nor Carvana completed *cleanly* even on day
   one: both hit Cloudflare's bot defense on some fraction of requests, and getting any data at
   all from them required discovering and building the one-fresh-profile-per-page workaround
   below, mid-session, before day one's official run. Whether that workaround keeps working on
   days two and three, unchanged, is exactly what remains to observe.

## Recommendation per source

- **auto.dev: keep.** Free tier, structured JSON with VIN already attached, zero failures, fastest
  and cheapest source today (15 candidates, $0, under a second of wall time).
- **marketcheck: keep, watch the trial quota.** Same shape as Auto.dev (structured, VIN attached,
  zero failures) and found VINs Auto.dev didn't (7 of its 12 were unique to it). The key used
  today is on a trial tier; nothing in today's response headers stated a hard quota, so what
  happens after the trial ends is unknown and worth checking before relying on it further.
- **cars.com: keep, provisionally keep-with-paid-key if days two and three don't improve.** Real
  VIN-bearing detail pages are reachable, and the hand-check shows the extraction is trustworthy
  once a page loads. But roughly half of today's cars.com detail-page requests were blocked by
  Cloudflare even after the fresh-profile-per-page fix, so this source is fragile in its current
  free-headed-Chrome form. If days two and three show the same block rate, the fallback is
  either a paid Cars.com data feed or a residential-proxy rotation, not more workarounds on top of
  Playwright. **Caveat:** day one's Corolla Hybrid group actually walked plain Corolla inventory
  (gas and hybrid together), not Corolla Hybrid specifically, because of the model-slug bug
  described under "surprises" below; 3 of the 4 non-matching candidates in the day-one cars.com row
  (details 8, 9, and 11: a 2024 LE, a 2012 S, and a 2026 SE) are non-matching gas Corollas as a
  direct result (the fourth, Honda Insight detail 1, is unrelated: an in-range Insight excluded for
  mileage). That bug is fixed in the code following this review, but it means day one's "44
  candidates found" and its per-group failure list should not be read as evidence about Cars.com's
  hybrid coverage specifically, only about its reachability and per-page extraction accuracy, which
  the hand-check does support.
- **carvana: keep.** Best raw yield today (84 candidates found on search pages, 8 matched and
  extracted cleanly) and every VIN it found was unique to Carvana, consistent with it selling its
  own retail inventory rather than syndicating dealer listings. Same Cloudflare fragility as
  Cars.com applies, but it blocked a smaller fraction of requests today. Unlike Cars.com, Carvana
  was always queried by base model only (it has no known hybrid-specific facet to try), so its
  wrong-model candidates are the expected, by-design result of that choice, not a bug: Carvana
  simply has no hybrid-only inventory filter to ask for, and `QueryGroup.MatchesExtractedVehicle`
  is exactly the backstop meant to catch that.
- **craigslist: drop, provisionally.** Zero real query matches across all four model groups within
  50 miles of Daytona Beach on day one; the only posting that came back at all (a 2019 Prius,
  outside the "2020 and newer" window) was a dealer repost, not a private-seller ad, and it still
  had no VIN. This may simply mean this specific niche-vehicle query has near-empty Craigslist
  inventory in this market rather than that Craigslist itself is worthless; worth one more look
  after days two and three before finalizing the drop.
- **autotrader (fallback): unverified, no recommendation yet.** Cars.com's own walk never emptied
  out completely enough to trigger the fallback today, so Autotrader's URL scheme (guessed, never
  confirmed against the live site) was never actually exercised end-to-end. If a future day
  triggers the fallback, its output should be checked carefully before being trusted.

## Surprises

- **Correction (post-review): VINs are on the search page, on the two sites this spike actually
  reached.** The original claim here (that a VIN only ever appeared on a detail page, "with no way
  around it for any of the three page-walk sites tried") is contradicted by this branch's own
  recorded fixtures and was wrong; Autotrader was never one of the three anyway (see below).
  Carvana's search page carries a schema.org `Vehicle` JSON-LD block per card, with the VIN,
  mileage, and price all present (`"vehicleIdentificationNumber":"19XZE4F58LE012282", "offers":
  {"price":23990}`, matching hand-check row 8 exactly). Cars.com's search-result cards carry the
  same data as `data-vin`, `data-year`, `data-make`, `data-model`, `data-trim`, `data-price`, and
  `data-mileage` attributes directly on the listing's anchor tag. Following this review,
  `PageWalkListingSource` reads both of these before queuing any detail-page fetch, and only
  fetches a detail page for a listing neither carrier resolved a VIN for, which is what the brief
  asked for ("detail pages only where the VIN is not on the search page") and was not previously
  implemented. Day one's own numbers show what this costs when skipped: every one of Carvana's four
  day-one search pages carries a JSON-LD VIN for every single listing it links to (21 for 21,
  confirmed for all four query groups, not just Honda Insight), so Carvana's 24 detail fetches
  (6.8 minutes, $0.56, the day's dominant cost and its entire bot-defense exposure) could plausibly
  have been zero, not "one per candidate, with no way around it." **Operational note for days two
  and three:** this also means most Cars.com and Carvana candidates going forward will resolve
  straight from the search page (`WasExtracted = false`) and will not land in the extraction
  hand-check pool the way day one's did. If a future day's page-walk candidates alone don't reach
  the ≥20 the pass mark asks for, that is expected under the fix, not a regression; Craigslist and
  any detail pages a search page genuinely doesn't resolve (still fetched and still extracted, same
  as before) remain the source of hand-checkable candidates.
- **A persistent profile's second request is what gets blocked, not the first, and a fresh
  profile is not a guaranteed fix either.** The very first live test today (a single Cars.com
  search) succeeded cleanly; the very next request in that same browser profile, whether it was
  another search or a detail page, was challenged by Cloudflare every time it was tried. Giving
  every single page load its own disposable browser profile (rather than the one persistent
  profile the brief originally called for) fixed most of this, but not all of it: even a
  brand-new profile's first request was blocked outright at least once today, and roughly half of
  Cars.com's detail-page loads and about a third of Carvana's were blocked even with a fresh
  profile each. That points to an IP-level rate or reputation signal layered on top of
  profile/cookie identity, not a simple "one page per profile" rule. This is a real deviation from
  the brief's "persistent profile" premise, worth flagging before any production design leans on
  it.
- **Auto.dev's free tier showed no cap today.** All 4 query groups returned complete results on
  the first call, no HTTP 429, no truncation notice. One day of light use doesn't rule out a cap
  existing; it just didn't show up today.
- **Sites silently ignore a model facet they don't recognize (or don't accept the way it was
  asked for) instead of erroring, and a facet appearing in a site's own JSON is not proof a URL
  query parameter can select it.** Guessing `toyota-corolla-hybrid` (hyphen) against Cars.com
  didn't 404 or come back empty: it silently fell back to showing every Corolla trim, gas and
  hybrid alike, and once combined with multiple makes in one query it fell back further, to
  showing unrelated vehicles (a Toyota Tacoma showed up in a "Toyota Corolla Hybrid" search).
  **Correction (post-review): the day-one build session then guessed that the underscored form,
  `toyota-corolla_hybrid`, was the fix, because that exact string appears as a distinct facet
  value in Cars.com's own model-facet JSON.** It is not: this branch's own recorded response for
  that exact query shows `selected_search_filters` resolving back to plain `["toyota-corolla"]`,
  and the chat-widget payload reading "I found 195 Toyota Corolla vehicles in your area," meaning
  the underscored slug did not apply either, and day one's Corolla Hybrid group silently walked
  all-Corolla inventory instead of
  what it asked for. A facet existing in a site's own JSON says only that the *site's own UI* can
  reach that filter state somehow (a click, a cookie, a different parameter shape); it says nothing
  about whether a URL query parameter reproduces it. Following this review, Cars.com's query
  building was reverted to base-model-only, same as every other page-walk site, since no working
  hybrid-specific slug has actually been confirmed against the live site. Any code that builds
  these URLs by guessing needs to validate the returned vehicle actually matches what was asked
  for regardless of which slug it guessed, which is exactly what
  `QueryGroup.MatchesExtractedVehicle` does for every page-walk candidate.
  **Correction (2026-09-22): the underscored slug is confirmed working after all, from a
  different browser session than this spike's.** Brian ticked "Corolla Hybrid" by hand in his own
  warmed Edge profile and the URL it built was `models[]=toyota-corolla_hybrid`, the exact
  underscored form this entry says didn't apply (project home `notes/run-session-2026-09-22.md`,
  "Facet URLs from Brian"). What differs between that request and this spike's (cookies, an A/B
  bucket, some other session state) is unconfirmed, but the operator-built URL is what the walk
  now trusts; see `WalkSites.cs`'s class remarks and `README.md`'s walk section for how. The
  `MatchesExtractedVehicle`-as-safety-net conclusion two paragraphs up still holds regardless of
  which facet value turns out to work.
- **Craigslist's canonical listing URL has moved.** Individual postings are no longer at the
  classic `<city>.craigslist.org/cto/d/...html` shape; they're at
  `https://www.craigslist.org/view/d/<slug>/<id>`, discoverable only via the JSON-LD-adjacent
  markup on the search page, not the JSON-LD block itself (which only carries images, price, and
  description, no URL).
- **The same car really does show up on multiple sources, at different prices.** 5 of the 32
  matching VINs found today (16 percent) appeared on two or three sources at once. The starkest
  case: VIN `JTDACACU8S3046841` (a 2025 Prius SE) was $31,998 on Auto.dev and $34,348 on
  Marketcheck the same day, a $2,350 difference for the identical car.
- **A year floor with no ceiling pulls in brand-new dealer stock.** The brief's Prius query
  ("2020 and newer") has no upper bound, so Marketcheck's results included several 2026 and 2027
  model-year Priuses with 0 miles on them: new inventory, not used. That's a literal, correct
  match for the stated query, but worth a product-level decision about whether "used-car market
  coverage" should include zero-mile new stock at all.

## Cost and time, day one

Total spend: **$0.75** (extraction only; both APIs and the search-page/list-harvesting steps
themselves are free). Total wall time: **~11 minutes** across all five sources, dominated by
Carvana's detail-page walk (6.8 min) and Cars.com's (3.4 min); Auto.dev and Marketcheck together
took under two seconds. These figures are historical for day one's actual code path; they are not
restated above, but the search-page VIN fix described under "surprises" means days two and three
should show materially fewer detail-page fetches, and therefore less cost and less bot-defense
exposure, for Carvana and Cars.com both.
