# The ledger

The ledger is a plain SQLite file named `odonomics.db`, and it lives in the data directory described in [setup.md](setup.md#the-data-directory). odo applies any pending schema changes whenever it opens the file, so you never run a migration by hand. No search, walk, or other run ever deletes anything from it. The only rows that are ever removed are duplicates that a one-time data migration folds into the row that survives, such as two postings for one car whose URLs differed only by a session id, or a Carvana dealer that was saved once per pickup city.

## The tables

- Vehicles: one row per VIN. The year, make, model, trim, and mileage reflect the most recent sighting, and the row also carries when the VIN was first and last seen and when it was marked a finalist.
- Postings: one row per source and URL where a vehicle has been seen, with its first and last seen times, its dealer, its latest shipping fee, the site-specific fee facts described [below](#site-specific-facts), and the run, if any, whose detail visit found its page saying the car had sold. A vehicle can have several postings, such as the same car on two sites.
- Price observations: one row each time a posting's price was recorded. A row is only written on a posting's first sighting or when its price actually changed, so a posting's rows read as its price history.
- Runs: one row per `odo search` or `odo walk`. It holds when the run started and finished, the zip and radius it searched with, and the list of source-and-model pairs it actually got a usable result for.
- Dealers: one row per dealer, keyed by name and location. It holds the CarEdge grade, the doc fee and add-ons note CarEdge printed beside it, and when the dealer was last checked, and [dealers.md](dealers.md) explains how a dealer is identified.
- VIN records: one row per researched VIN. It keeps the most recent fetch of the NHTSA decode, recalls, complaints, and safety ratings, plus the Marketcheck VIN history. The NHTSA pieces each have a fetched-at stamp of their own, and the row has one researched-at stamp that the seven-day rule reads. [research.md](research.md) explains the caching.

- Posting attributes: one row per posting and name, for the display-only facts a site shows about a listing. See [below](#site-specific-facts).

Notes from `odo note` sit in a table of their own, and so does the list of one-time data migrations that have already run.

## Site-specific facts

Every site says different things about a listing, and the ledger has two places to keep them. The rule for choosing is this: if arithmetic or a red flag reads a fact, it is a typed column, and otherwise it is an attribute.

The typed columns on a posting are the ones the cost model and the red flags read:

- `FeePosture` says how the site's price relates to its fees. It's one of `all-in` (the price already includes them), `itemized` (the site lists them on top), or `unknown` (a run read the page and couldn't tell). It's null when no run has ever read it, which is different from `unknown`.
- `ItemizedFeesTotal` is the sum of the fees an `itemized` site listed on top of the price.
- `PickupFee` and `PickupLocation` are what it costs to pick the car up instead of having it delivered, and where. They sit beside the shipping fee as an option and are never a default, so a posting that ships to you keeps its shipping fee whether or not a pickup fee is stored. The walk fills them for Carvana, and the scenario's `fulfillment` field chooses which of the two fees the cost model counts ([cost-model.md](cost-model.md#what-a-purchase-price-means)).

On a dealer, `DocFee` is the documentation fee in dollars and `AddOnsNote` is the add-ons line, both as CarEdge prints them on the dealer's card. `odo dealer grade` fills them in when it records a grade. A dealer graded before the ledger kept them has them empty until `odo dealer grade --all --refresh` looks it up again ([dealers.md](dealers.md#the-grade-pass)).

Everything else a site shows that only matters to a reader, such as a badge or a rating, goes in the attributes table. Each row has the posting, a name, a value, and the run that observed it, and a posting holds at most one row per name. When a later run reads the same name again, its value and run replace the earlier row. A name a run didn't read is left as it was, since a page that stops showing a badge doesn't prove the badge is gone. `odo show` prints a posting's attributes as one line per name and value under its postings table, and prints its dealer's doc fee and add-ons note on the dealer line. `odo rank` reads only the deal badge and the dealer rating, to show them beside a row, and never scores on them.

The walk fills the attributes table from each site's result cards with the badges and the cars.com dealer rating ([walk.md](walk.md#badges)). It doesn't read the fee columns off the pages yet, so those stay empty until it does. A dealer's doc fee and add-ons note are already filled in by the grade pass.

## How a run stamps LastSeen

Every posting a run sees, whether from a detail page or a search card, gets its last seen time set to the run's own start time, not the wall clock. A vehicle row gets the same when the run saves it. That way the diff can find a "gone" posting by comparing its last seen time with the previous run's start, instead of guessing from how much time has passed.

A posting counts as still listed when its last seen time is at or after the latest run that covered its source and model in full, or when no run has covered them yet. If only partial runs have ever covered the pair, the first of them stands in for that full run, so a posting last seen before it is not listed. A run that stopped short of the site because of an explicit `--max` marks that pair as capped, and a run whose later result page failed to load marks it as unread. Neither kind of partial run replaces the coverage before it: a posting the capped walk never reached, or that sat on a page that failed to load, keeps the last seen time of the earlier walk, and stays listed until a full walk of the pair runs without seeing it. `odo rank`, `odo show` and `odo research` all read this rule from one shared helper, and the Gone list in `odo search` and `odo walk` looks back past capped and unread runs the same way. A run only lists the pairs it got a usable result for, so a missing API key, a failed query, or a site you didn't walk never makes a posting look gone.
