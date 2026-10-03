# The scenario

`scenarios/daughter.json` is the shipped scenario. It holds the target models, the hard filters, and every cost assumption (APR, gas price, insurance, mpg, fees, residual value, and so on). Edit the file directly, because there's no `odo scenario set` in v0.

Every command that scores against a scenario defaults to `scenarios/daughter.json`. Pass `--scenario <path>` to use a different file.

## Pinned and loose values

A cost input is either a single pinned value, like `"apr": 0.07`, or a loose range, like `"apr": { "min": 0.065, "max": 0.095 }`. A loose input makes each cost line that depends on it in `odo rank` and `odo show` a band instead of a point. The expected figure uses each loose input's midpoint, and the two ends are the lowest and highest results across every combination of the loose inputs' own low and high ends.

Only some fields can be loose, and the list below marks them. The others are plain numbers, so a range there won't load.

## Field by field

- `name` is a label for the scenario, and `zip` and `radiusMiles` are the search area. The searches and the walk use them (Carvana takes the zip alone and no radius), and each run records them so a later run can tell when the search moved.
- `annualMiles` (loose) is how far you drive in a year. It sets the fuel cost, and it sets the maintenance cost through `maintenancePerMile`.
- `gasPricePerGallon` (loose) is the price per gallon that the fuel cost uses.
- `holdYears` is how long you expect to keep the car. The ten-year figures in `odo rank` cover this many years, and the average spreads them over `holdYears` times twelve months.
- `downPayment` (loose) comes off the financed amount. `apr` (loose) and `termMonths` set the loan payment.
- `maintenancePerMile` (loose) is the dollars per mile you expect to spend on maintenance, and `emergencyReservePerMonth` (loose) is a monthly amount set aside for surprises.
- `salesTaxStateRate` applies to the whole price. `countySurtaxRate` applies to the first $5,000 of it only, and `countySurtaxSource` is a note saying where that rate came from. odo doesn't read the note when it does the math.
- `fees` (loose) is the one-time cost on top of the price and tax, and `residualFraction` (loose) is the share of the purchase cost you expect the car to be worth when the hold ends.
- `fulfillment` is optional, and it's either `"delivery"` or `"pickup"`. It says how you'd take a car home, which decides whether a listing's shipping fee or its pickup fee is added to the asking price (see [cost-model.md](cost-model.md#what-a-purchase-price-means)). Leaving it out means `"delivery"`, so a scenario written before the field existed prices as it did. Choose `"pickup"` when you'd collect the car yourself, or to see what a pickup would save. `ScenarioLoader` rejects any other value. `odo rank` and `odo budget` take `--fulfillment` to override the field for one run.
- `targetMonthlyBudgets` is the list of monthly loan-payment figures that `odo budget` solves for, and it's what `odo rank` checks the cheapest vehicle's loan payment against. It's a payment target, not a total-cost target: running costs (insurance, fuel, maintenance, the emergency reserve) ride on top of it rather than counting against it.

[cost-model.md](cost-model.md) says how these inputs turn into the figures on screen.

## Filters

`filters` holds the hard rules a vehicle has to pass to be ranked at all. `allowedModels` lists the target models, each written as "Make Model". `minModelYear` is the oldest model year you'll consider, and `maxMileage` is the highest odometer reading. Both are sent to the listing APIs and the walk as search facets, so the searches only bring back cars that can pass.

`maxPrice` is optional; absent means no ceiling. When set, it's the most a vehicle's current asking price plus its stored shipping fee may come to and still be ranked, researched, walked past its search card, or shown a monthly cost. `odo rank` and `odo research` exclude a vehicle over it the same way they exclude one below the minimum year or over the maximum mileage. `odo show` applies neither of those filters, but for a vehicle over the price ceiling it prints the ceiling reason in place of a monthly cost instead of computing one. `odo budget` lists no vehicles at all: it only prints the max-purchase-price table from the scenario's own budgets, and is unaffected by this ceiling. The walk checks it earlier still, at link collection rather than at the ledger: a search card whose own stated price, plus any shipping or delivery fee that same card states, already comes to more than `maxPrice` never earns a detail visit at all (see [walk.md](walk.md#rules-every-site-shares)), so a car you'd never buy costs no page visits. The shipped scenario sets `maxPrice` to 25000, Brian's own ceiling, deliberately not derived from `odo budget`'s own figures.

A vehicle that fails the filters lands in the Excluded section of `odo rank` with its reasons. Three more reasons come from the scorer rather than the file. A vehicle with under 500 miles, or a model year beyond the current year, is excluded as new stock. A vehicle with no current asking price is excluded because every posting for it is gone, unless its only postings are cars.com copies of a CarMax listing (see below), which gets its own reason instead. A vehicle is excluded the same way when none of its live postings is both purchasable and priced: either every one is a CarMax listing currently marked "Reserved for another buyer" or "In transit, not yet purchasable"; or a CarMax listing marked "Only at &lt;store&gt;" whose store sits farther than `radiusMiles` from `zip`, since CarMax will not transfer a car like this to another store, and it is excluded with the reason "only at &lt;store&gt;, out of radius" rather than the reserved-or-in-transit one; or the ones that aren't reserved, in transit, or out of radius have no current price. A vehicle with another live posting that is purchasable and priced still ranks, priced from that posting rather than the excluded one, and one whose reservation is lifted is ranked again on its own, the next time a `--revisit` walk reads its detail page (see [walk.md](walk.md#carmax)), not any walk. A cars.com posting for one of CarMax's own stores never counts as that other live posting, whatever it says about purchasability, whether or not it happens to be a copy of that same vehicle's own "Only at" store: cars.com carries CarMax's whole nationwide inventory as if it delivered anywhere, so its own copy of any CarMax listing is excluded outright rather than trusted (see [walk.md](walk.md#carscom)), and the vehicle stands or falls on its own CarMax posting and any non-CarMax postings instead. A vehicle whose only postings are cars.com copies of a CarMax listing this way (CarMax's own walk hasn't reached it yet) is excluded with its own reason, "no current asking price (only a cars.com copy of a CarMax posting; see the CarMax walk)", rather than the plain "every posting is gone" one, since the car is still listed, just not priced from that redundant copy; `odo show` gives the same reason instead of a monthly cost. An "Only at" posting whose store sits inside the radius still ranks and prices from its own posting like any other, and its stored $0 shipping fee is never treated as a free delivery: CarMax offers no delivery at all on a car like this, only pickup at its one store. The out-of-radius check itself only knows the stores and zips this project has hand-curated the coordinates for (`CarMaxStores`); a store or a scenario `zip` neither one has an entry for is never excluded, since a distance nobody actually measured never proves a store out of range. `odo rank` and `odo show` both say so when it happens, rather than silently ranking or pricing the car as if the distance were confirmed: `odo rank` lists it under its own "Only at a store this project can't yet measure, treated as in radius" heading, and `odo show` prints a note above the vehicle's monthly cost.

## Per-model maps

Four fields are keyed by model, and each key is the same "Make Model" string, such as `"Toyota Camry Hybrid"`.

`insuranceMonthlyByModel` is your monthly insurance figure for each model, and `mpgByModel` is the EPA combined mpg for each model, feeding the fuel cost (the VIN decode doesn't return fuel economy, so v0 always uses this table). A missing key or a `null` value both mean unknown and never zero, in either table, so a vehicle of a model missing an insurance or an mpg figure shows up under "Not ranked: scenario data missing" until you add one. `Toyota Prius Prime` is not in either table, and not in `allowedModels` either: it's Toyota's plug-in hybrid Prius variant, and Brian ruled it out as a candidate, so a ledger row for one is excluded outright as a disallowed model rather than shown as missing scenario data (see [walk.md](walk.md#rules-every-site-shares) for how the walk and the search sources recognize and exclude it without ever deleting the row).

`minModelYearOverrides` (inside `filters`) names a model whose minimum year differs from `minModelYear`, and it wins when both apply. The shipped scenario lets a 2018 Camry Hybrid through while every other model starts at 2019, and the walk's search facets follow the same rule.

`hybridOnlyFromModelYear` is optional. It names the model year a base model stopped shipping a gas-only trim, so a candidate at or above that year matches the scenario's hybrid model even when its own listing text never says "Hybrid". Toyota dropped the gas-only Camry for model year 2025, so the shipped scenario has `"Toyota Camry Hybrid": 2025`. A plain "2025 Camry SE" is then kept and scored as a Camry Hybrid instead of being dropped as a gas trim. A candidate below the named year, or a model with no rule at all, is still rejected unless its own text says "Hybrid". `ScenarioLoader` rejects a file whose key isn't one of `filters.allowedModels`, whose key repeats when case is ignored, or whose value isn't a four-digit year. [walk.md](walk.md#rules-every-site-shares) covers what the rule changes about the walk's searches.

## Required features

`filters.requiredFeatures` is optional. It lists equipment every candidate must have, using the names `"smart-key entry"` (proximity entry) and `"push-button start"`. A name matches ignoring case, and `ScenarioLoader` rejects any other name. The shipped scenario requires both, because Brian ruled that a candidate must have keyless or smart-key entry and push-to-start. A scenario that leaves the field out requires nothing, and a car is then ranked as it was before.

Each vehicle carries a status for each feature: present, absent, or unknown, with its source. The sources rank in this order:

1. The listing's window sticker or factory equipment list, read by the walk (see [walk.md](walk.md#window-sticker-equipment)).
2. The factory trim table, which fills a status the sticker left unknown.
3. Nothing else. A dealer's description never confirms or rules out either feature.

A car whose status for a required feature is absent is excluded by `odo rank`, with the feature and the source named in its reason, such as "push-button start is absent (window sticker), which the scenario requires". The exclusion only applies to a confirmed absence. A car whose status is unknown is still ranked, and `odo rank` prints a yellow note under its row for each unknown required feature, such as "confirm push-button start", so it is a prompt to check with the dealer and not a penalty. `odo research` and `odo title check` score cars the same way, so neither spends a lookup on a car `odo rank` excludes for equipment. `odo show` prints both statuses and their sources in an Equipment section, along with the same "confirm" note or a line saying `odo rank` excludes the car, but it still shows the car, as it does for any other filter.

### The factory trim table

The trim table is one data file, `src/Odonomics/Data/factory-trim-equipment.json`, copied beside the program and read each time `odo rank`, `odo show`, `odo research`, or `odo title check` runs. It starts empty. Each row names a make, a model as the ledger spells it (for example "Corolla Hybrid"), a range of model years, a trim, an answer of `"present"` or `"absent"` for `smartKeyEntry`, `pushButtonStart`, or both, and a `source` that says where the answer came from:

```json
{
  "entries": [
    {
      "make": "Toyota",
      "model": "Corolla Hybrid",
      "yearFrom": 2020,
      "yearTo": 2023,
      "trim": "LE",
      "smartKeyEntry": "present",
      "pushButtonStart": "present",
      "source": "where the answer was read"
    }
  ]
}
```

A row applies to a car whose make, model, and trim match ignoring case and whose model year is inside the range. A car with no trim never matches. When more than one row applies to a car and they disagree about a feature, the table gives no answer for that feature, since a table that contradicts itself is not definite. The table is only ever read to fill an unknown status. It never replaces one a window sticker read, and what it fills is not stored in the ledger, so editing the file changes the next run's ranking with no walk. A malformed row stops the command with an error that names the row.

To set the requirement on a scenario that does not have it, add `"requiredFeatures": ["smart-key entry", "push-button start"]` to its `filters` object, then run `odo walk <site> --revisit` so the cars already on the ledger get their window stickers read.
