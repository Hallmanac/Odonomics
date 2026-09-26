# A run, start to finish

This is the order I'd run odo in, from an empty ledger to a car you're ready to call about. Each step says what you get out of it, and the page for that step has the details. [setup.md](setup.md) has to be done first so the build works and the two listing keys are set.

1. `odo search` asks Auto.dev and Marketcheck for used cars that fit the scenario, saves what comes back to the ledger, and prints what's new, moved, price-dropped, and gone. The rules it applies are in [commands.md](commands.md#what-search-asks-for).
2. `odo walk` visits cars.com, Carvana, and Autotrader in a browser you launched yourself, so you catch the listings the APIs never return. You get the same diff as search, and [walk.md](walk.md) explains how to start the browser and what a walk does.
3. `odo research` fetches NHTSA safety ratings and Marketcheck VIN history for every vehicle that passes the scenario's filters. You get a red-flags summary with one line per vehicle, and [research.md](research.md) explains each flag.
4. `odo dealer grade --all` looks up each dealer's CarEdge grade in the same browser the walk uses. You get a grade beside each posting whose dealer CarEdge has rated, and [dealers.md](dealers.md) covers how a dealer is matched to a card.
5. `odo budget` works backwards from each target monthly budget in the scenario. You get the payment room left after running costs and the most a car can cost, and [cost-model.md](cost-model.md) says how that price is built.
6. `odo rank` scores every vehicle in the ledger by what it costs to own per month over ten years. You get a ranked list that also shows each vehicle's research status, open recall count, and dealer grade.
7. `odo show <vin>` prints everything odo knows about one vehicle, including its red flags, its postings, and an itemized monthly cost that adds up to the figure rank printed.

Once a car looks good, `odo note <vin> "<text>"` records what you learn about it, such as the result of a pre-purchase inspection (PPI) or a Carfax report. `odo finalist <vin>` then marks it a finalist, and it won't do that until a note mentions PPI and a note mentions Carfax or AutoCheck.

Everything here is run by hand, and nothing is scheduled. Search and walk are worth rerunning whenever you want a fresh picture of the market, and research, grading, and rank come after them so they see the new cars.
