# Spike: source coverage and extraction quality

## The question

For one real purchase, how much of the used-car market can Odonomics see, from which sources,
at what cost per day, and how accurate is what it extracts. The product is only worth building
if the answer is good enough, so this runs before any architecture.

## The query (the same one against every source)

- Target models: Honda Insight 2019-2022, Toyota Corolla Hybrid 2020-2021, Toyota Camry Hybrid
  2018-2019, Toyota Prius 2020 and newer.
- Mileage at most 100,000.
- Within 50 miles of zip 32114.

## Sources

- Auto.dev listings API on the free tier. Key from `dotnet user-secrets` or an environment
  variable, never committed.
- Marketcheck on whatever trial or free tier it offers. Record the price per call and the quota.
  If no free access exists, record that and skip it.
- One aggregator by page walk: Cars.com (fall back to Autotrader if Cars.com blocks the walk).
- One retailer by page walk: Carvana.
- Craigslist by plain HTML fetch, one request per search page per run, honoring robots.txt.
- Not in scope: Facebook Marketplace, CarMax, eBay Motors, any other site.

Page walks use headed Chrome through Playwright with a persistent profile, one search page per
site per run, then detail pages only where the VIN is not on the search page. Page text goes to
a model extraction step against a JSON schema. No per-site parsers: if extraction is wrong, fix
the prompt.

## Shape

A throwaway .NET console app in `spike/` at the repo root. Not a member of any solution, no
event store, no ledger, no CLI, no tests beyond what the author needs to trust the numbers.
Every raw response (API JSON, page text) is written to `spike/recorded/<source>/<run>/`. The
extraction schema and prompt live in `spike/extraction/`.

The app runs the whole query once per invocation and appends one row per source to the report
table in `SPIKE-FINDINGS.md`. It is run on three separate days.

- Day one is run by the build session: build the app, run it, do the hand check and the manual
  Cars.com count, write the findings marked "day 1 of 3", and open the pull request. The
  findings must state the single command that runs a day (for example `dotnet run --project
  spike` from the repo root) so the operator can run days two and three by hand.
- Days two and three are run by the operator on the same machine with that command, each
  followed by a commit of the new recorded responses and report rows, pushed to the branch.
- A follow-up run on the same task, after day three, finalizes the verdict against the pass
  mark and the per-source recommendations.

## Secrets

API keys live in `dotnet user-secrets` under the fixed id `odonomics-spike`; the spike project
sets `<UserSecretsId>odonomics-spike</UserSecretsId>` so it picks them up. Keys are
`AutoDev:ApiKey` and `Marketcheck:ApiKey`. Environment variables `AUTODEV__APIKEY` and
`MARKETCHECK__APIKEY` are the fallback. A missing key is reported in the findings as "could not
run: no key", never worked around.

## Pass mark (decided before the spike starts)

- VIN precision 100 percent on a hand-checked sample of at least 20 page-walk candidates: a
  wrong VIN is worse than none.
- Price and mileage within 1 percent of the page for at least 95 percent of that sample.
- Two free or cheap sources together find at least 80 percent of what a person finds by hand on
  Cars.com for the same query on day one.
- Every page walk completes on all three days without a code or prompt change after day one
  (the operator reports any failure on days two or three to the follow-up run).

## What survives

- `SPIKE-FINDINGS.md` at the repo root: the report table for all three days, the hand-check
  results, cost per daily run in dollars and minutes, the surprises, and a recommendation per
  source (keep, keep with a paid key, drop) with the reason.
- The recorded raw responses, which become contract-test fixtures for the real adapters.
- The extraction schema and prompt.

Everything else in `spike/` is deleted by the first build task. No spike code moves into `src/`.

## Things to look for on purpose

- Whether VINs appear on search result pages or only on detail pages, and what that does to the
  fetch count per run.
- Whether Carvana's bot defenses tolerate a headed persistent profile at one page per day.
- Auto.dev free-tier caps on results, radius, or calls per day.
- How often a private-seller Craigslist post carries a VIN at all.
- Whether the same car appears on several sources at different prices, and how often.
