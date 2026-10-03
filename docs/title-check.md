# Title check

`odo title check` looks vehicles up in the public salvage-auction archives and raises a red flag when one sold at a Copart or IAA auction. It exists because the free title checks miss cars: the first one it was built for, a 2025 Corolla Hybrid XLE, was Copart lot 1-55637026 (sold 2026-07-16 with a California salvage certificate) while NICB VINCheck showed nothing and the Marketcheck history showed only an Erepairables.com row.

It makes no Marketcheck call and no other paid API call. Everything is read from public web pages through the same hand-launched browser `odo walk` and `odo dealer grade` use.

## Running it

```
odo title check [--top N]
odo title check <vin> [<vin> ...]
```

With no VINs it checks the first N vehicles of the current rank, 20 unless `--top` says otherwise. "The current rank" means the vehicles `odo rank` lists under Ranked, best first, scored under the scenario exactly as rank scores them. With VINs it checks those vehicles, which must already be in the ledger. Passing both VINs and `--top` is an error.

The command connects over the Chrome DevTools Protocol to the browser described in [walk.md](walk.md#your-browser). If nothing is listening on the debugging port, it prints the same launch-line message `odo dealer grade` prints and exits.

## What it does for each VIN

It handles one VIN at a time and waits between VINs the way the walk waits between detail pages. For each one it:

1. Searches `html.duckduckgo.com` for the VIN. A captcha, or a page whose layout it does not recognise, ends the lookup there as could not read.
2. Takes the search results that are on a known archive site (the list is `AuctionSearchPageReader.ArchiveHosts`: bid.cars, copart.com, iaai.com, and a few similar sites), in the order DuckDuckGo ranked them, and opens at most three of them, one after another with a short pause.
3. Reads the auction, lot number, sale date, sale document, primary and secondary damage, ACV, repair estimate, and odometer off each page, and stops at the first page that has them.

A page counts only if it names the VIN as the lot's own labelled VIN field. A page whose own VIN is a different one is ignored, even when it lists the checked VIN in a compare list of other lots. A page that names the VIN without a labelled VIN field, or that names it but has none of the lot fields the parser knows, is could not read rather than "not found", so a changed layout is never mistaken for a car with no auction history.

## What is stored

Each VIN's result is stored on its vehicle with a checked-at stamp, as one of three outcomes:

- Found: the sale's fields, as far as the page printed them.
- Not found: the search found nothing on an archive site, or every page it opened was readable and had no record for the VIN.
- Could not read: with the reason, such as a captcha, a block, a page too short to be a lot page, or a layout the parser does not recognise.

A VIN is looked up again only when its last result is more than seven days old or was could not read. A VIN that does not need a lookup is skipped, and the command prints what is stored for it. A captcha or block is recorded as could not read and the run moves on to the next VIN. It never retries within a run, so a blocked site costs one page load per VIN and not a loop. The command exits non-zero when every VIN it tried could not be read.

Every page it reads is also written as text under the data directory, in `walks/auctions/<run>/titles/`, the same way the walk records its pages.

## Where the result shows

A found sale raises the `salvage-auction` red flag, which names the sale document, the damage, and the date, for example `sold at salvage auction (Salvage certificate (CA), Side/Front end, 2026-07-16)`. Any part the record did not print is left out of that text. The flag comes from the stored lookup and not from the research fetch, so it appears with the other red flags:

- `odo show <vin>` lists it under Red flags and prints the whole stored record (auction, lot, sale date, sale document, damage, ACV, repair estimate, odometer, and the page it came from) under "Salvage-auction archives". For a vehicle with no sale it says that none was found or why the archives could not be read. It still shows a vehicle `odo rank` excludes for its sale, the same as it does for any other filter.
- `odo rank` excludes the vehicle, as described below.
- `odo research` includes `salvage-auction` among a vehicle's short tags.

## What rank does with a found sale

`odo rank` excludes a vehicle whose stored result is found, and lists it under Excluded with a reason that names the auction, the sale date, and the sale document, for example `sold at salvage auction: Copart 2026-07-16, Salvage certificate (CA)`. Any part the record did not print is left out of that text. A not found result, a could not read result, and a vehicle that has never been checked are not affected and rank as before.

The exclusion applies to any found sale whatever its sale document, which is Brian's ruling ("drop any salvage-auction sales"). A Copart or IAA sale does not always mean a salvage title, since an archive also lists clean-title sales, but both are insurance-salvage auctions and a car that went through one is dropped even when the lot sold on a clean title. The sale document is in the reason so it is still there to read.

The exclusion reads the stored result and makes no network call, so it holds for a vehicle nobody has researched. Three consequences follow from the stored result being what decides:

- A vehicle drops out of the rank only after `odo title check` has looked it up. Until then it ranks, with the check's result unknown.
- `odo research` and `odo title check` with no VINs both pick vehicles the way rank scores them, so neither spends a lookup on a vehicle rank excludes for its sale. A found sale is therefore not looked up again on the seven-day schedule unless you name its VIN.
- The exclusion goes away on its own if a later lookup of that VIN stores not found, since it is recomputed from the stored result on every run. A later lookup that could not read the archives never replaces a stored found sale: the sale and the exclusion stay, and the stored lookup keeps its old date so naming the VIN looks it up again.

The `salvage-seller` red flag from `odo research` (a dealer name that is a known salvage marketplace in the Marketcheck history) is a different signal and stays a warning only.

## Archive sites change and may block

These are third-party sites that do not offer this as a service. They rearrange their pages, add captchas, and may refuse automated reads, and a hand-launched browser does not prevent that. When it happens the result is could not read, with the reason, and the vehicle is tried again on a later run. A change in layout is fixed in one place, `AuctionPageParser`, which reads "Label: value" lines, tab-separated rows, and a label on one line with its value on the next, and where a new label spelling or a new site is one more entry in its lists.

The volume is deliberately small: a default run is twenty VINs, each with one search and at most three archive pages. If the archives block automated reads for good, a paid NMVTIS report (VinAudit and similar) is the fallback, and it has not been built.

The tests read page text under `tests/Odonomics.Tests/fixtures/auctions`. The bid.cars lot pages and two DuckDuckGo results pages there are recordings from a live run on 2026-10-03. The DuckDuckGo no-results and captcha pages, the Cloudflare-style captcha page, the changed-layout page, and the tab-separated lot page are modeled on what those sites print, because no run has met them yet. When a live run records one, drop the recording in under the same name.
