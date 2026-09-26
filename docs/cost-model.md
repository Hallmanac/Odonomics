# The cost model

odo ranks cars by what they cost to own, so this page explains what a price means, how the three monthly figures are built, and where you'll see each one. The inputs all come from [the scenario](scenario.md).

## What a purchase price means

Every purchase-price figure the cost model uses is the asking price plus the posting's shipping fee when one is known. That way, a car shipped from far away is compared honestly with one you pick up in Orlando. A vehicle with several postings is priced at the one that's cheapest to take home, meaning the asking price plus the fee. A posting with no stored fee costs its asking price, as it always did.

The fee is treated as part of the purchase price, so it's financed, taxed, and depreciated along with the car. The price red flags are the exception, and [research.md](research.md#red-flags) says why. Carvana is the only site that stores a fee, and [walk.md](walk.md#carvana) explains how the walk reads it.

Picking a car up at the Orlando hub instead of having it shipped is a choice the fee doesn't model. The ranking prices a Carvana car as shipped, since that's the fee the page shows.

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

That maximum is a purchase price in the sense above, so it's the most the asking price plus any shipping fee can come to. A car with a $1,590 fee needs an asking price $1,590 below that figure, and the command says so under its table.

### rank

`odo rank` prints one line above its sections saying what During-loan and 10yr avg include, for example "During-loan is the loan payment plus running costs of about $268-$279 a month (insurance, fuel, maintenance, reserve). 10yr avg spreads those costs, the down payment, the loan payments, and resale value over the hold." Only During-loan is the loan payment plus running costs, so the note offers no remainder for 10yr avg. The running-cost figure comes from the ranked and over-budget vehicles, so it's a single figure or range when they agree and the span across them when they differ (insurance is per model). It isn't printed when there's no rankable vehicle, and with `--detail` it leaves out its pointer to `--detail`, since the payments are printed right below.

Each row shows the vehicle's price, its during-loan and 10yr avg figures, and its research and dealer-grade markers. The price is the purchase price, so it includes any shipping fee.

A vehicle whose cheapest posting has a deal badge or a dealer rating recorded also gets a short `site` field at the end of the line that names it, such as `site GrD 4.9`. The deal badge is abbreviated (`GrD` great deal, `GD` good deal, `FD` fair deal, `GrP` great price, `GP` good price) and the rating is the dealer's rating on that site. A legend under the section heading spells the abbreviations out. The field sits on the VIN line, and not on the figures line, so it never pushes a row past 80 columns. It comes from the same posting whose price the row shows, so it speaks for the listing you'd actually buy.

### Site badges never touch the score

The badges are the sites' own opinions of a price, and the score doesn't use them. The ranking order and every cost figure come out the same with the badges and without them (a test ranks one ledger both ways and compares). The reason is that nobody has tested whether a site's price opinion means anything. A site's "Great Deal" may or may not predict a car that sells fast or holds its price. That can only be checked against the ledger's own history, by asking whether badged cars sell faster or drop less, and that needs months of walks. Until the history says so, the badges are shown beside odo's own ranking so you can compare the two, and they stay out of it.

`--detail` adds one line under each ranked and over-budget row with that vehicle's loan payment beside its during-loan total (`loan payment $324-$348  of during $592-$627`). It's a separate line rather than a column, so the row above it keeps its 80-column fit. A vehicle whose posting has a shipping fee also gets a line above the payment naming it (`price $16,410 asking + $1,590 shipping = $18,000`). Free shipping shows as `$0 shipping`, and a vehicle with no stored fee gets no such line.

When no rankable vehicle's expected during-loan cost meets one or more of the scenario's target monthly budgets, rank says so in one line above the Ranked section. That line names only the unmet targets and the cheapest vehicle's during-loan range, then points at `odo budget` for the purchase price each target allows. It reads the targets from the scenario rather than from `--budget`, and it still counts vehicles that `--budget` moved into the over-budget section, so a tight `--budget` doesn't hide it. It isn't printed when every target is met or when there's no rankable vehicle. A rankable vehicle is one that passes the scenario's filters, has a known insurance figure, and has a current price.

### show

`odo show <vin>` prints a "Monthly cost" block after the postings, for a vehicle with a current asking price. When that posting has a shipping fee, the block opens with the asking price, the fee on its own line under it, and the purchase price they add up to. A vehicle with no stored fee prints none of those three lines. Below that come the loan payment, insurance, fuel, maintenance, and reserve, then the during-loan total and the 10-year average. The during-loan and ten-year totals are the figures `odo rank` shows.

The itemized lines are rounded to whole dollars once, so their low ends, and their high ends, add up to the during-loan total printed beneath them. `odo rank --detail` takes its loan payment from that same rounding, so the two commands print the identical payment for a vehicle.

`show` prices a vehicle the scenario's filters would exclude too. A vehicle with no current price, or a model the scenario has no insurance or mpg figure for, gets a line saying why nothing was computed instead.
