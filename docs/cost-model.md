# The cost model

odo ranks cars by what they cost to own, so this page explains what a price means, how the three monthly figures are built, and where you'll see each one. The inputs all come from [the scenario](scenario.md).

## What a purchase price means

Every purchase-price figure the cost model uses is the asking price plus one fee for taking the car home, plus the fees the page itemized on top of its price when it says they are not in it. The scenario's `fulfillment` says which take-home fee counts. Under `delivery`, the default, it's the posting's shipping fee when one is known. Under `pickup`, it's the posting's pickup fee. That way, a car shipped from far away is compared honestly with one you collect yourself, and a dealer who adds fees at the desk is compared honestly with one who doesn't. A vehicle with several postings is priced at the one that's cheapest to take home under the chosen fulfillment, meaning the asking price plus those fees. A posting with no stored fee costs its asking price, as it always did.

A posting whose pickup fee was never read costs its shipping fee under `pickup`, since nothing says collecting it is cheaper. A posting that shows a pickup option but no fee stores a pickup fee of 0, which is what Carvana's hub pickup prints today, so under `pickup` a Carvana car costs its asking price alone. Delivery stays the default because pickup depends on where the buyer is: a pickup location that suits one buyer may be a plane ticket away for another, so pickup is a what-if you turn on, not a standard. The `--fulfillment` option on `odo rank` and `odo budget` sets it for one run without editing the scenario file, and [scenario.md](scenario.md#field-by-field) describes the field.

Only an `itemized` fee posture adds fees. An `all-in` posting that also stores a shipping fee (CarGurus, whose price includes it) shows that fee and never adds it, since it is already in the asking price. A posting whose page says its price is `all-in` already has its fees in the asking price, and one that is `unknown` (or was never read) adds nothing because nothing is known. Fees are an input to the ranking and never a filter, so a car with a large fee total is priced higher and ranked lower, and is never dropped.

The fees are treated as part of the purchase price, so they're financed, taxed, and depreciated along with the car. The price red flags are the exception, and [research.md](research.md#red-flags) says why. Carvana stores both fees and CarMax stores a transfer fee as its shipping fee. [walk.md](walk.md#carvana) explains how the walk reads Carvana's, and [walk.md](walk.md#carmax) explains CarMax's, which comes off each search card. cars.com and Autotrader store a fee posture, and [walk.md](walk.md#fee-statements) explains how the walk reads it. CarGurus stores a posture and a shipping fee that its price already includes, and [walk.md](walk.md#cargurus) explains that.

`odo show` prints the asking price, then any shipping fee, pickup fee and itemized fees, then the purchase price they add up to (with the fulfillment that chose the take-home fee), and last the fee posture, all above the monthly figures. For an all-in posting whose page listed its fees, the posture line says how much of the price they are, and that figure is not added. `odo rank --detail` names the same fees on a line under each row.

## The three monthly figures

Every figure starts from the purchase price. Sales tax is the state rate on the whole price plus the county surtax on its first $5,000. The purchase cost is the price, that tax, and the scenario's fees. The loan finances the purchase cost less the down payment, at the scenario's APR over its term, and the payment is the standard amortized one.

The running costs are insurance, fuel, maintenance, and the emergency reserve. Fuel is the annual miles divided by the model's mpg, times the gas price, spread over twelve months. Maintenance is the per-mile cost times the annual miles, also spread over twelve months.

- During-loan monthly is the loan payment plus the four running costs. It isn't a car payment.
- After-payoff monthly is the same four running costs with no payment, which is what the car costs each month once the loan is gone.
- Ten-year average monthly spreads the total cost of the hold over its months. The total is the down payment, plus every loan payment made within the hold, plus the running costs for the whole hold, minus what the car is worth at the end. Depreciation is already inside that residual, so it's never added a second time.

Each figure is the scenario's expected value, or a `$low-$high` range when an input feeding it is loose. Ranking sorts on the ten-year average.

## Where the figures appear

### budget

`odo budget` runs the model backwards. It prints the fixed monthly running cost first, using the average of the known insurance figures and the mpg figures in the scenario's per-model tables, since no specific car has been picked yet. Then it prints one row per target monthly budget with the payment room left after those running costs and the maximum purchase price that keeps the during-loan figure at or under the target.

That maximum is a purchase price in the sense above, so it's the most the asking price plus any fee can come to, the shipping fee under delivery and the pickup fee under pickup. A car with a $1,590 shipping fee needs an asking price $1,590 below that figure, and the command says so under its table, along with which fulfillment it assumed. `--fulfillment pickup` changes that line and nothing else, since the maximum itself doesn't depend on any one car's fee.

### rank

`odo rank` prints one line above its sections saying what During-loan and 10yr avg include, for example "During-loan is the loan payment plus running costs of about $268-$279 a month (insurance, fuel, maintenance, reserve). 10yr avg spreads those costs, the down payment, the loan payments, and resale value over the hold." Only During-loan is the loan payment plus running costs, so the note offers no remainder for 10yr avg. The running-cost figure comes from the ranked and over-budget vehicles, so it's a single figure or range when they agree and the span across them when they differ (insurance is per model). It isn't printed when there's no rankable vehicle, and with `--detail` it leaves out its pointer to `--detail`, since the payments are printed right below.

Each row shows the vehicle's price, its during-loan and 10yr avg figures, and its research and dealer-grade markers. The price is the purchase price, so it includes the fee for the fulfillment in use (any shipping fee under delivery, any pickup fee under pickup).

A vehicle whose cheapest posting has a deal badge or a dealer rating recorded also gets a short `site` field at the end of the line that names it, such as `site GrD 4.9`. The deal badge is abbreviated (`GrD` great deal, `GD` good deal, `FD` fair deal, `GrP` great price, `GP` good price) and the rating is the dealer's rating on that site. A legend under the section heading spells the abbreviations out. The field sits on the VIN line, and not on the figures line, so it never pushes a row past 80 columns. It comes from the same posting whose price the row shows, so it speaks for the listing you'd actually buy.

### Site badges never touch the score

The badges are the sites' own opinions of a price, and the score doesn't use them. The ranking order and every cost figure come out the same with the badges and without them (a test ranks one ledger both ways and compares). The reason is that nobody has tested whether a site's price opinion means anything. A site's "Great Deal" may or may not predict a car that sells fast or holds its price. That can only be checked against the ledger's own history, by asking whether badged cars sell faster or drop less, and that needs months of walks. Until the history says so, the badges are shown beside odo's own ranking so you can compare the two, and they stay out of it.

`--detail` adds one line under each ranked and over-budget row with that vehicle's loan payment beside its during-loan total (`loan payment $324-$348  of during $592-$627`). It's a separate line rather than a column, so the row above it keeps its 80-column fit. A vehicle whose posting has a fee also gets a line above the payment naming it and the fulfillment that chose it (`price $16,410 asking + $1,590 shipping = $18,000 (delivery)`). Under `--fulfillment pickup` the same car reads `price $16,410 asking + $0 pickup = $16,410 (pickup at Orlando, FL)`, or `(pickup, fee unknown)` beside its shipping fee when no pickup fee was read. Free shipping shows as `$0 shipping`, and a vehicle with no stored fee gets no such line.

`--fulfillment delivery|pickup` overrides the scenario's `fulfillment` for that run, the way `--term` overrides the loan term.

When no rankable vehicle's expected during-loan cost meets one or more of the scenario's target monthly budgets, rank says so in one line above the Ranked section. That line names only the unmet targets and the cheapest vehicle's during-loan range, then points at `odo budget` for the purchase price each target allows. It reads the targets from the scenario rather than from `--budget`, and it still counts vehicles that `--budget` moved into the over-budget section, so a tight `--budget` doesn't hide it. It isn't printed when every target is met or when there's no rankable vehicle. A rankable vehicle is one that passes the scenario's filters, has a known insurance figure, and has a current price.

### show

`odo show <vin>` prints a "Monthly cost" block after the postings, for a vehicle with a current asking price. When that posting has a shipping fee or a pickup option, the block opens with the asking price, each fee it knows on its own line under it, and the purchase price they add up to, followed by the fulfillment the analysis assumed ("with delivery" or "with pickup"). Both fees print whenever both are known, so the other way of taking the car home can be read at a glance: a Carvana car shows its shipping fee, then `Pickup fee $0 at Orlando, FL`. A pickup fee that was never read prints as "not read, so the shipping fee is assumed" when the fulfillment is pickup. A vehicle with no stored fee prints none of these lines. `show` reads the scenario's `fulfillment`, so a run that wants the pickup figure sets it there. Below that come the loan payment, insurance, fuel, maintenance, and reserve, then the during-loan total and the 10-year average. The during-loan and ten-year totals are the figures `odo rank` shows.

The itemized lines are rounded to whole dollars once, so their low ends, and their high ends, add up to the during-loan total printed beneath them. `odo rank --detail` takes its loan payment from that same rounding, so the two commands print the identical payment for a vehicle.

`show` prices a vehicle the scenario's filters would exclude too. A vehicle with no current price, or a model the scenario has no insurance or mpg figure for, gets a line saying why nothing was computed instead.
