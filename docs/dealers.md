# Dealers and grades

odo links every posting to the dealer that's selling it, and `odo dealer grade` looks that dealer up on CarEdge. Most of what's tricky here is deciding which dealer a posting belongs to, so that's where this page starts.

## How a dealer is identified

`odo search` and `odo walk` link every posting they upsert to a dealer when the source names one. Auto.dev and Marketcheck carry a dealer name and city and state in their API responses, and the walk's extraction reads a dealer name and location off the page text the same way it reads the vehicle's own fields. A dealer is keyed by its normalized name and location. The location key expands a city's "Ft.", "Mt." and "St." to Fort, Mount and Saint and drops a trailing zip code, so "St. Augustine, FL" and "Saint Augustine, FL" are one dealer.

Carvana needs its own rules, because a Carvana detail page usually names no dealer. The walk stores such a posting with the dealer "Carvana" instead of none, and a page that does name a hub, such as "Carvana Winder", keeps that name. Carvana is one dealer row per name and never per city. A Carvana page prints the buyer's pickup city and not where the hub is. So the ledger keys every Carvana dealer ("Carvana" or a hub such as "Carvana Winder") on its name alone, and it drops any location that a walk or an API source reports for it.

When the page names no hub, the walk still looks in the Marketcheck VIN history the ledger already stores for that VIN. If a Carvana hub's listing overlaps the posting's own sighting window, give or take two days, the posting is linked to that hub, and the most recent such listing wins. The two days are there because Marketcheck's dates trail the walk by about a day.

A later sighting that names no hub moves a posting only off the bare "Carvana" row (or a legacy located one), and only onto a hub the history names. It never replaces a link to any other dealer, so a posting stored under a hub the page named stays under it. A posting walked later for a VIN whose history the ledger didn't yet hold stays on the bare row until the next Carvana walk moves it, so the two can briefly disagree until then.

## Rows that are never looked up

The bare "Carvana" row is never looked up. CarEdge lists Carvana as a card per hub ("Carvana Orlando" and so on), so a search for the bare name returns hub cards, and taking one would grade the chain by an arbitrary hub. The run stamps the row checked with a recorded reason and no grade, and `odo show` prints it as "Carvana (ungraded)". Every hub row is graded by its hub name like any other dealer. The parser backs that up, because a search for the bare name "Carvana" only accepts a card named exactly "Carvana", never a hub's card that merely contains it, whatever location the dealer row has.

The "Private seller" row that Autotrader's private listings share is never looked up either. A private seller is a person, and CarEdge has no Dealer Rating for one, so a search for that name could only match an unrelated business. The run stamps it checked with a recorded reason and no grade, the same way it does the bare "Carvana" row.

## The grade pass

`odo dealer grade --all` connects over the Chrome DevTools Protocol to the same hand-launched browser `odo walk` uses, described in [walk.md](walk.md#your-browser). If nothing is listening on the debugging port, it prints the identical launch-line message and exits.

### What it looks up

With `--all`, it looks up every dealer that has never been checked on CarEdge's Dealer Ratings. It's paced, and it pauses for challenges, the same way the walk is. `odo dealer grade <vin>` does the same for the dealers behind that VIN's postings, and it only looks up the ones not yet checked, printing what's already stored for the rest. Each dealer is looked up by searching `https://caredge.com/dealers?q=<dealer name and location>` and reading the matching result card's letter grade off the page. The same card prints the dealer's doc fee and an add-ons line ("No add-ons" or, for example, "$358 add-ons"). Whenever a grade is recorded, the run stores the doc fee as a dollar amount ("$1,199" is stored as 1199) and the add-ons line as printed.

`--refresh` looks up dealers that already have a grade again, along with the never-checked ones, and it works with `--all` or with a VIN. It exists so a dealer graded before the ledger kept the doc fee and add-ons note can pick them up. A dealer that was stamped checked with no grade, either because CarEdge has no rating for it or because the run skipped it on purpose, is not looked up again, since a second look has nothing to add. A refresh that comes back with a new grade replaces the old one, and one that can't read the page leaves everything as it was. A refresh that finds CarEdge no longer has a card for the dealer keeps the stored grade, doc fee, and add-ons note, says so, and counts the dealer as kept in the closing tally rather than as ungraded. A refresh that finds the dealer's own card now marked "Not rated" is different, because CarEdge is positively saying there is no rating: the run clears the stored grade, doc fee, and add-ons note and stamps the dealer checked with no grade.

### How a card is matched

A card is only taken for a dealer when the dealer's location agrees with the card's "City, ST" line one part at a time. Both are split into a city and a state, case-folded, and stripped of punctuation and any trailing zip code. "Ft.", "Mt." and "St." in the city are read as Fort, Mount and Saint, but never in the state, so a bare "MT" stays Montana. That's how the walk's "Winter Park, FL 32792" still finds its own "Winter Park, FL" card, while a bare "Palm Beach" never matches "West Palm Beach, FL".

### What counts as checked

Unless `--refresh` is given, each dealer is checked at most once. A dealer that CarEdge positively says it has no rating for gets stamped as checked, meaning no card names it at all or its own card is marked "Not rated". The fetched-at timestamp is what the cache rule keys off, and not the grade itself, so that dealer is never looked up again on a later run.

Some outcomes leave the dealer unstamped and retried on the next run, so they're never recorded as "not on CarEdge":

- A page that merely failed to parse, whether from a slow render, a challenge, or a layout change.
- A card that carried the dealer's name when every such card was in another city or state. The run prints "name matched, location did not".
- A dealer whose location is missing, or only a city or only a state, that matched more than one same-named card. It stays ungraded until a fuller location is known.

A page that's CarEdge's own 404 means the search URL itself is dead, and not that this one dealer is unrateable. The run prints the dead URL and keeps going, but it stops and exits non-zero after three such pages in a row. That leaves every dealer it never got to ungraded for the next run.

## Where grades appear

`odo show <vin>` prints the grade beside each posting, followed by the dealer's doc fee and add-ons note when the ledger has them, as in `Daytona Toyota (B), doc fee $1,199, No add-ons`. On the graded pages recorded so far, the current dealer card carries no "why this grade" text, so no reason is shown alongside it. No F-grade page has been recorded, so whether an F still carries one is unknown.

`odo rank` shows each vehicle's grade whenever at least one vehicle in that section has one. It appears as a `gr` marker on the ranked and over-budget lines, and as a Grade column in the insurance-unknown and F-graded-only tables. Rank also lists any vehicle whose only postings come from F-range-graded dealers under its own warning heading, and that vehicle is still ranked and never hidden.
