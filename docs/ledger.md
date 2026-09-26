# The ledger

The ledger is a plain SQLite file named `odonomics.db`, and it lives in the data directory described in [setup.md](setup.md#the-data-directory). odo applies any pending schema changes whenever it opens the file, so you never run a migration by hand. Nothing in it is ever deleted.

## The tables

- Vehicles: one row per VIN. The year, make, model, trim, and mileage reflect the most recent sighting, and the row also carries when the VIN was first and last seen and when it was marked a finalist.
- Postings: one row per source and URL where a vehicle has been seen, with its first and last seen times, its dealer, and its latest shipping fee. A vehicle can have several postings, such as the same car on two sites.
- Price observations: one row each time a posting's price was recorded. A row is only written on a posting's first sighting or when its price actually changed, so a posting's rows read as its price history.
- Runs: one row per `odo search` or `odo walk`. It holds when the run started and finished, the zip and radius it searched with, and the list of source-and-model pairs it actually got a usable result for.
- Dealers: one row per dealer, keyed by name and location. It holds the CarEdge grade and when the dealer was last checked, and [dealers.md](dealers.md) explains how a dealer is identified.
- VIN records: one row per researched VIN. It keeps the most recent fetch of the NHTSA decode, recalls, complaints, and safety ratings, plus the Marketcheck VIN history. The NHTSA pieces each have a fetched-at stamp of their own, and the row has one researched-at stamp that the seven-day rule reads. [research.md](research.md) explains the caching.

Notes from `odo note` sit in a table of their own, and so does the list of one-time data migrations that have already run.

## How a run stamps LastSeen

Every posting a run sees, whether from a detail page or a search card, gets its last seen time set to the run's own start time, not the wall clock. A vehicle row gets the same when the run saves it. That way the diff can find a "gone" posting by comparing its last seen time with the previous run's start, instead of guessing from how much time has passed.

A posting counts as still listed when its last seen time matches the latest run that covered its source and model, or when no run has covered them yet. A run only lists the pairs it got a usable result for, so a missing API key, a failed query, or a site you didn't walk never makes a posting look gone.
