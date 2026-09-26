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
- `targetMonthlyBudgets` is the list of monthly figures that `odo budget` solves for, and it's what `odo rank` checks the cheapest vehicle against.

[cost-model.md](cost-model.md) says how these inputs turn into the figures on screen.

## Filters

`filters` holds the hard rules a vehicle has to pass to be ranked at all. `allowedModels` lists the target models, each written as "Make Model". `minModelYear` is the oldest model year you'll consider, and `maxMileage` is the highest odometer reading. Both are sent to the listing APIs and the walk as search facets, so the searches only bring back cars that can pass.

A vehicle that fails the filters lands in the Excluded section of `odo rank` with its reasons. Two more reasons come from the scorer rather than the file. A vehicle with under 500 miles, or a model year beyond the current year, is excluded as new stock. A vehicle with no current asking price is excluded because every posting for it is gone.

## Per-model maps

Four fields are keyed by model, and each key is the same "Make Model" string, such as `"Toyota Camry Hybrid"`.

`insuranceMonthlyByModel` is your monthly insurance figure for each model. A missing key or a `null` value both mean unknown and never zero, so a vehicle of that model shows up under "Not ranked: insurance unknown" until you add a figure. `mpgByModel` is the EPA combined mpg for each model, and it feeds the fuel cost. The VIN decode doesn't return fuel economy, so v0 always uses this table.

`minModelYearOverrides` (inside `filters`) names a model whose minimum year differs from `minModelYear`, and it wins when both apply. The shipped scenario lets a 2018 Camry Hybrid through while every other model starts at 2019, and the walk's search facets follow the same rule.

`hybridOnlyFromModelYear` is optional. It names the model year a base model stopped shipping a gas-only trim, so a candidate at or above that year matches the scenario's hybrid model even when its own listing text never says "Hybrid". Toyota dropped the gas-only Camry for model year 2025, so the shipped scenario has `"Toyota Camry Hybrid": 2025`. A plain "2025 Camry SE" is then kept and scored as a Camry Hybrid instead of being dropped as a gas trim. A candidate below the named year, or a model with no rule at all, is still rejected unless its own text says "Hybrid". `ScenarioLoader` rejects a file whose key isn't one of `filters.allowedModels`, whose key repeats when case is ignored, or whose value isn't a four-digit year. [walk.md](walk.md#rules-every-site-shares) covers what the rule changes about the walk's searches.
