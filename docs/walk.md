# The assisted walk

`odo walk` opens the shopping sites in a real browser, reads what's listed, and saves it to the ledger. This page starts with the browser you have to launch, then covers what a walk does, the rules every site shares, what's different about each site, where the recorded pages land, and how the page text becomes vehicle fields.

## Your browser

`odo walk` never launches a browser. It connects over the Chrome DevTools Protocol to a Chrome or Microsoft Edge that you start by hand, in a dedicated profile so it never touches your everyday browsing session. Edge speaks the same protocol as Chrome and is an equally valid choice, so use whichever Chromium browser you actually have installed.

Chrome and Edge each get their own profile directory rather than sharing one, since each browser encrypts its stored cookies with a key only it can read. A bot-defense challenge you clear in one browser still has to be cleared again the first time you switch to the other. If nothing is listening on the debugging port, `odo walk` prints the exact command for your OS and exits, and running `odo walk` again picks it up.

Here are the launch lines, which `odo walk` also prints itself with your own data directory filled in.

**macOS, Chrome**

```
/Applications/Google\ Chrome.app/Contents/MacOS/Google\ Chrome --remote-debugging-port=9222 --user-data-dir="$HOME/Library/Application Support/Odonomics/chrome-profile"
```

**macOS, Edge**

```
/Applications/Microsoft\ Edge.app/Contents/MacOS/Microsoft\ Edge --remote-debugging-port=9222 --user-data-dir="$HOME/Library/Application Support/Odonomics/edge-profile"
```

**Windows (PowerShell), Chrome**

```
& "C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="$env:LOCALAPPDATA\Odonomics\chrome-profile"
```

**Windows (PowerShell), Edge**

```
& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --remote-debugging-port=9222 --user-data-dir="$env:LOCALAPPDATA\Odonomics\edge-profile"
```

**Windows (cmd), Chrome**

```
"C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="%LOCALAPPDATA%\Odonomics\chrome-profile"
```

**Windows (cmd), Edge**

```
"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --remote-debugging-port=9222 --user-data-dir="%LOCALAPPDATA%\Odonomics\edge-profile"
```

A bare quoted path needs PowerShell's `&` call operator to run at all, while cmd runs it either way. When `odo walk` can't find a browser listening, it prints all four Windows forms (Chrome and Edge, PowerShell and cmd), and on macOS and Linux it prints both browsers' lines. On Linux those are `google-chrome` and `microsoft-edge`, with the same `--remote-debugging-port=9222` flag and a `--user-data-dir` of `chrome-profile` or `edge-profile` under your data directory.

Leave that window open for as long as you're walking. The one time the browser comes to the front is a bot-defense challenge. The walk beeps and brings the challenged page to the front so you can solve it by hand, then the walk goes back to running in the background once you press Enter. On macOS it also puts your previous window back in front at that point.

## What a walk does

A bare `odo walk` walks the whole shortlist in one command: cars.com first, then Carvana, then Autotrader. On each site it visits every model in the scenario's `allowedModels`, in order, and it prints which site and model it's about to visit before each one. `odo walk cars.com`, `odo walk carvana`, or `odo walk autotrader` narrows it to that one site, still with every model. `--model "Make Model"` narrows either form to that one model exactly, matching the scenario's own casing case-insensitively.

Every site-and-model pair visits its search page, and a model with a hybrid-only rule gets two of them on cars.com. The walk scrolls and dwells there like a person, then opens detail pages with a random gap between each. It keeps going until it has visited every car the search returns that the ledger doesn't already hold.

The pacing is spread out on purpose. A search page gets four scroll steps with two to four seconds between them, then a dwell of 20 to 60 seconds. The gap between detail pages is 15 to 45 seconds, and there's a further gap of the same size before the next model and before the next site. The gap between detail pages also runs between one search's last detail page and the next search page.

Every detail tab opens as a CDP background target and closes the same way, so the browser stays out of your way on your desktop the whole time. It never comes to the front of your other windows, and the one exception is the bot-defense challenge described above.

### Known cars

A repeat walk visits only new cars. Each detail page costs about a minute, and a car the ledger already holds has nothing new to say on it except its price and whether it's still listed. Both of those are on the search page's own result card. So before the walk opens a link, it checks it against the postings the ledger already holds for that site, and it doesn't open one it already holds.

Instead, the walk touches that posting from its card. It stamps the posting as seen in this run, once the pair's walk has finished, so a pair that fails or is interrupted leaves its known postings exactly as the last completed run left them. When the card's asking price can be read and differs from the latest one on the ledger, it records the new price. The diff then works exactly as it does after a full visit. A card showing a lower price than before is listed under "Price drops", and a posting no card shows any more is listed under "Gone", because it wasn't stamped.

A touched link never enters the pool of detail visits and never spends any of `--max`. That means `--max`, when you give it, goes only to cars the ledger has never seen, and paging on Carvana goes on past pages made only of known cars until the pool of new links is full or the search runs out. What a card can't tell the walk stays as the last detail visit left it: the dealer, the Carvana shipping fee, and the vehicle's year, mileage, and trim. A card price under $1,000 is treated as a misread and not a price.

`--revisit` turns all of this off and opens a detail page for every link, as the walk did before. Use it to refresh the dealer, shipping fee, or pickup option of cars the ledger already holds, or to check a site whose cards the walk can't read yet. A posting that was dropped on its detail visit (the wrong model, a new car) never reached the ledger, so it's opened again on every walk.

### Badges

Each site prints its own opinion of a car's price as a badge on the result card, and the walk records what the card says. It reads them from the same card text it already collects for the price, so there is no extra page load. The badges are stored as posting attributes ([ledger.md](ledger.md#site-specific-facts)), which are for reading only: `odo show` lists them under the posting, and `odo rank` shows the deal badge and the dealer rating in one short field. They never touch the score ([cost-model.md](cost-model.md#site-badges-never-touch-the-score)).

A badge is a line of the card that is exactly the badge's text. A card with no badge records nothing. A posting already on the ledger has its badges refreshed when its card is touched, without a detail visit, and a new posting gets the badges of the card it was found on when its detail page is saved. A name a card doesn't show is left as it was, since a card that drops a badge doesn't prove the badge is gone.

| Site | Attribute | What the card prints |
| --- | --- | --- |
| cars.com | `deal` | Great Deal, Good Deal, Fair Deal |
| cars.com | `demand` | High Demand |
| cars.com | `dealer-rating` | the dealer's rating, a line of its own such as `4.9` |
| Autotrader | `deal` | Great Price, Good Price |
| Autotrader | `price-drop` | Price Drop |
| Autotrader | `paperwork` | Online Paperwork |
| Carvana | `deal` | Great Deal |
| Carvana | `price-drop` | Price Drop |
| Carvana | `shipping` | Free shipping |

A cars.com card shows a dealer rating whether or not it has a badge, so a card with a rating and no badge records the rating alone. Carvana's "Recent" tag is a sort label the page repeats, not a badge, and the walk doesn't read it. Autotrader also prints badges such as High Demand and Newly Listed that aren't recorded. Autotrader's card shape hasn't been confirmed (see below), so until a walk shows that its card text holds the badges, an Autotrader posting may get none.

### Length and the cap

The walk is uncapped by default. The scenario's own facets (model, minimum year, maximum mileage, zip and radius) are the only filter, and a pair visits every car its searches return. On a paged site (Carvana) that means following it page after page until its own stop rules end it, which are the stated count being reached, a page that adds nothing new, or "No exact matches".

That's a long run. Each detail page takes about a minute once the random gaps and scroll pauses are counted, so a first walk of a whole shortlist can run to hundreds of pages and several hours. After that it only grows by the cars that are new.

You can judge the length before committing to it. The start of the run prints the pairs, the limit if there is one, and how many postings the ledger already holds for each pair. Before its first detail page, each pair prints how many pages it's about to visit and roughly how many minutes that is. Under `--max` a pair with two searches prints only the count its first search starts with. A pair with nothing new to open says so and moves on.

`--max N` puts a limit on it, and the limit is per site-and-model pair, not per run and not per raw page visit. A detail page the walk opens and then rejects for being some other model (a plain Corolla on a Corolla Hybrid walk, say), for repeating a VIN this pair already saved, or for being a new-car listing never spends any of the cap. So the walk keeps taking links until the cap is met with real matches or the search page runs out of links to try.

With `--max`, each site's candidate pool is sized to twice the limit, so a wrong-model or repeat page can be replaced by a spare link instead of spending the cap outright. Without it, every link is visited and there's nothing to replace.

### When a pair fails

A pair's own coverage is only stamped on the run once that pair's walk actually finishes. A pair that errors (a page that never loads, a site that fails outright) is reported inline, and the walk moves on to the next pair rather than aborting the whole run. A walk that's interrupted partway through (Ctrl-C, a crash) leaves the pairs that already finished marked covered and the rest untouched.

### The pair line and the end table

After each pair's walk finishes, it prints one line summarizing that pair. The line gives how many detail pages were visited, how many known links were kept current from their search cards without a visit, how many were saved, and how many were dropped. The dropped count is broken out by reason when it's non-zero: wrong model, new-car listing, missing fields, no VIN, repeat, failed to load, and extraction failed. When that line would be wider than the console, the breakdown moves intact to a second line indented by four spaces, and it never wraps mid-parenthetical. A pair still reports as one row, however many searches or result pages it took.

At the end, a summary table lists the same figures for every pair, with a "Known" column beside "Pages" and "Saved" for the known count. The usual diff for the whole run follows the table, using the five headings described in [commands.md](commands.md#what-search-asks-for).

### The Gone reasons

Every row under "Gone" ends with a short reason, so a car the walk simply no longer looks for isn't mistaken for one that sold. The reasons are checked in this order:

- "below year facet" when the vehicle's year is under the scenario's minimum for its model.
- "over mileage" when its mileage is over the scenario's maximum.
- "search moved" when this run's zip or radius differs from those of the run that last saw the posting.
- "pages unread" when a result page after the first failed to load, so the walk never read the pages after it (see below).
- "beyond the cap" when an explicit `--max` stopped the pair before the site ran out of results (see below).
- "not on search page" otherwise.

Only that last reason suggests the car may have left the market. When a pair has both reasons (a cap on one of its searches and a failed page on another), "pages unread" wins. A run recorded before the ledger kept its zip and radius is never treated as a moved search.

A pair is capped when `--max` kept the walk from reading everything the site returned. That happens when the pool of links filled while a paged site had more pages, or when a second search was never opened because the cap was spent. With `--revisit`, where the pool also holds links the ledger already knows, a link the pool had no room for or the cap left unvisited caps the pair too. Without `--revisit` a leftover link is one the ledger does not hold, and every known posting on the pages that were read has already been kept current from its card, so leftovers alone never cap a pair. A pair whose pages were exhausted is not capped, even when it filled the cap exactly, whenever the site states its match count. A Carvana page that shows none and fills the pool on its last page is still treated as capped, since only loading one more page would tell.

A capped pair still counts as covered, so a car it did reach is kept current as usual. It is also stamped as partial coverage, with a `capped:` token beside its `source:model` one in the run's sources. A posting of that pair the run did not touch is listed as "beyond the cap" rather than "not on search page", because the walk never looked for it and nothing says it sold. A later run that covers the pair in full compares against the newest run that covered it in full, so a posting a capped run never reached is still reported (as "not on search page" if it is still missing) rather than dropping out of the comparison. When any rows are beyond the cap, the heading says how many, for example "Gone (33, 20 beyond the cap)". The pair's own summary line ends with ", capped", and its Status in the end-of-run table reads "capped".

A pair whose later result page failed to load is treated the same way, with a marker of its own: an `unread:` token beside its `source:model` one. Its untouched postings are listed as "pages unread", the heading counts them beside the beyond-the-cap rows (for example "Gone (33, 20 beyond the cap, 5 pages unread)"), its summary line ends with ", page N failed" (N being the first result page that failed), and its Status in the end-of-run table reads "unread". A later run that covers the pair in full compares against the newest run that covered it in full, exactly as it does after a capped run. Re-running the walk reads the pages again.

## Rules every site shares

Every site's search URL carries the scenario's minimum model year and maximum mileage as facets. That way the per-pair cap is spent only on cars the scenario can rank, and not on ones the walk would then drop as below the minimum year or over the mileage limit. Both values come from the scenario. The minimum year is per model, and `minModelYearOverrides` wins over `minModelYear`, so the shipped scenario's Camry Hybrid searches from 2018 and every other model from 2019. The maximum mileage comes from `maxMileage`. Changing the scenario changes the search with no code edit.

Every detail page is checked against the model its pair is walking. A page for some other model is dropped as "wrong model". Since the search URLs now filter to the exact requested model in the common case, this check is a safety net and not the way the walk narrows down to real candidates.

A walked page counts as a hybrid model's match when the extraction names the hybrid. It also counts when the page's own title line reads `<year> <make> <base model> Hybrid` (for example "2023 Toyota Corolla Hybrid"), even though the extraction split it into a base model and a trim. A third route is a fuel spec line in the page's own spec block that says hybrid. Autotrader titles its pre-2025 Corolla Hybrids and Camry Hybrids like the gas cars ("Certified 2026 Toyota Corolla SE FWD") and says so only in the spec block. So the walk accepts a line that starts with "Hybrid: Gas/Electric", "Fuel Type: Hybrid", "Fuel: Hybrid", or "Engine: ... Hybrid" (in any letter case), provided the extracted make and base model match and the line sits above any similar-vehicles or recommended-cars heading.

Only the title line and that spec line decide it. A plain Corolla page that merely mentions a Corolla Hybrid elsewhere, or whose padded similar-vehicles cards carry a hybrid spec line, is still dropped as the wrong model, and the saved candidate carries the walked model either way. The drop line for a hybrid query says what was checked ("no Hybrid in title, trim, or spec line").

The hybrid-only-from-year rule feeds the same check. When the scenario's `hybridOnlyFromModelYear` says a base model has no gas version left, a page at or above that year matches the hybrid model even if it never says "Hybrid", and [scenario.md](scenario.md#per-model-maps) covers the field itself. Below that year the page has to say "Hybrid" like any other, and the drop line for a gas page names the year it's gas-only before. This rule is also why cars.com gets a second search for such a model.

cars.com's used search still mixes in new-car cards ("New 2027 Toyota Corolla Hybrid LE"), which the scenario can never rank. So the walk skips any card whose title begins with "New " when it collects detail links, and those links never enter the candidate pool and never count against the cap. As a backstop, a cars.com detail page that still reads as a new car is dropped as a "new-car listing", which likewise never spends the cap. A page reads as a new car when it has a "New" title before the year, or when it has an MSRP line with no Mileage line. "Missing fields" stays as the reason for a used car whose page lacks its year, price, or mileage.

## Per site

### cars.com

cars.com's model facet lowercases the model name and collapses every run of non-alphanumeric characters to a single underscore, after the make and a hyphen. "Toyota Corolla Hybrid" becomes `models[]=toyota-corolla_hybrid`, and "Honda CR-V Hybrid" becomes `models[]=honda-cr_v_hybrid`. That way the search URL isolates the hybrid model directly. The scenario's minimum model year and maximum mileage go in as the plain `year_min` and `mileage_max` query parameters. Every cars.com search URL also carries `page_size=100`, the largest value of the site's own per-page selector (20, 50, 100), because its used search would otherwise stop at its default page size (walk run 5 saved only 30 Camry Hybrids from the first pages of its two searches). If the live site ignores that parameter, the `page=N` paging alone still covers every result at the default page size.

A model the scenario marks hybrid-only from some year on is searched twice. A base model that went hybrid-only is filed under its own base-model bucket for the years after the cutover, rather than the separate hybrid one, while the years before it stay under the hybrid bucket. So the walk issues one search for the hybrid facet (`models[]=toyota-camry_hybrid`) from the scenario's minimum year. It then issues a second for the base-model facet (`models[]=toyota-camry`) from the hybrid-only year itself, or the scenario's minimum year if that's later, and it visits them in that order.

The two facets can't share one URL, because cars.com takes a single `year_min` per request. A combined search would have to start the base model at the scenario's minimum year too, which is 2018 for the Camry Hybrid. It would then fill up with gas Camrys from 2018 through 2024, which the walk rejects as the wrong model after the cap is spent on them.

With no `--max` there's nothing to share out. Both searches are loaded first, with a random gap between them, and a link both carry is kept once. Every link is then visited, so the pair's page count is known before its first detail page.

With `--max`, each search starts with an even share of the cap, and the odd page goes to the hybrid-facet search. Each search gets its own link pool of twice its share, and a share a search can't spend because its page runs out of candidates rolls forward to the next search. If the first search still has links it never visited when the second is done, the cap that's left goes to those. So the pair falls short of `--max` only when both searches are exhausted, and it never saves more than `--max` in total.

The console labels each search page ("search 2 of 2") with its model facet.

cars.com is paged like Carvana (see below), so a pair with more matches than one result page holds is not truncated at its first page. Each of the two searches of a Camry pair pages on its own facets. With `--max`, the per-pair cap is unchanged and the two searches still share it exactly as described above. Of the two extra stop rules Carvana uses, cars.com uses neither yet: its recorded search text states no match count and no "No exact matches" marker is known for it, so its paging ends when the pool is full (only with `--max`) or a page adds no new link.

The card price on a cars.com card is the first dollar amount on it, because its price comes before a price-drop amount, the mileage, and the "Used 2023 ..." title.

### Carvana

Carvana has no separate hybrid model at all. It filters its base-model search by fuel type instead, and its `cvnaid` query parameter carries a base64url-encoded JSON filter naming the base model plus `"fuelTypes":["Hybrid"]`. The minimum model year and maximum mileage go inside that same JSON as `"year":{"min":N}` (singular, since a `"years"` key is silently ignored) and `"mileage":{"max":N}`. The URL carries the zip and no radius. Because the fuel-type filter matches those listings' actual fuel type regardless of the model name, Carvana never needs a second search.

Carvana is paged, and so is cars.com. Carvana renders about 21 cards per result page but reports far more cars for the same filters (a hundred for one Prius search). So after collecting a search page's links, the walk requests the same URL with `page=2`, `page=3`, and so on. It keeps going for as long as the candidate pool is below its size (unbounded without `--max`, twice the cap with it) and the last page showed at least one link that hadn't appeared on an earlier page, which counts a car the ledger already holds.

Two more rules keep the paging to cars the facets asked for. The first page states how many cars match ("16 cars"), and that count bounds the links the pages contribute in total, taken in page order, even when a page shows a few more results than it states. And a result page that says "No exact matches" contributes no links and ends the paging, because everything after that notice is similar vehicles (other models, or other years and mileages) that the search never asked for. The console says the search ran out of exact matches on that page.

Each page gets the same scroll and dwell as the first, is recorded as its own search text (see [Recorded pages](#recorded-pages)), and is announced on the console ("search page, page 2", or "search 2 of 2, toyota-camry facet, page 1" for a cars.com pair with two searches). Only the first page is required. If a later page fails to load, the walk prints a warning, stops paging, and goes on with the links it already has rather than failing the pair. The pages after the one that failed were never read, so the pair's coverage is recorded as partial (see [the Gone reasons](#the-gone-reasons)): its line says which page failed, and a posting the ledger holds from those pages is listed as "pages unread", not as a car that left the market. A first page that fails to load still fails the pair, and a failed pair is never stamped as covered.

A Carvana card's price is the amount after "Current price:".

Carvana ships every car to the buyer, and its detail page prints a one-time shipping fee beside the price ("Free shipping" or, for example, "$1,590 shipping", and again in the delivery block). The fee depends on the car and on the buyer's zip. The walk reads that line straight off the page text, with no model in between. "Free shipping" stores 0, "$1,590 shipping" stores 1590, and a page that shows neither stores nothing, so an unknown fee is never mistaken for free. It reads only the page's own line, ahead of the "Need it sooner?" block that lists other cars.

The page also offers pickup, in a "Pickup and Delivery" block that lists the two options one after the other: "Pickup Wednesday", "Pick it up from our Orlando location", "Orlando, FL", then "or", then "Delivery Tuesday", "Delivered to you within 3 days", "Orlando, FL", and the shipping amount. The shipping fee sits under the delivery option only, and the pickup option prints no fee. The walk reads the block from the page text the same way, with no model in between. The location is the city line under the pickup line, and the pickup fee is 0 unless a dollar amount or "Free" is printed under the pickup option before the "or". A page whose block never rendered stores no pickup fee and no location, and keeps its shipping fee from the price header, so an unread pickup is never mistaken for a free one.

The block renders lazily, only once the page has been scrolled about a third of the way down. A Carvana detail page therefore gets one screen of scrolling as every page does, and if the text still lacks "Pickup and Delivery", up to three more screens, each with its own pause, stopping as soon as the block appears. Some cars never render it, and the walk gives up on those after the last step rather than waiting.

The fee is stored on the posting as its own `ShippingFee` column, not in the price history, because it isn't part of the asking price, only of what taking the car home costs. The pickup option goes in the posting's `PickupFee` and `PickupLocation` columns. Each sighting replaces the last one's values, so a later walk that finds a different fee (or none) leaves the newer figure, and a later page whose block didn't render clears the pickup option rather than keeping an old one. Every other site stores no fee. [cost-model.md](cost-model.md#what-a-purchase-price-means) covers how each fee counts, and how the scenario's `fulfillment` chooses between them.

### Autotrader

Autotrader is the third site, and the only one of the three that lists private sellers as well as dealers, since cars.com and Carvana carry none. That also makes it where smaller dealers who don't list on cars.com turn up. Its search URL is `https://www.autotrader.com/cars-for-sale/used-cars/<make>/<base model>?zip=<zip>&searchRadius=<radius>&startYear=<minimum model year>&maxMileage=<maximum mileage>&sortBy=relevance`. The zip, radius, minimum model year, and maximum mileage all come from the scenario like every other site's facets.

There's no `sellerTypes` parameter, on purpose. Leaving it out returns dealers and private sellers together, and `sellerTypes=d` and `sellerTypes=p` narrow to one or the other.

Autotrader has no separate hybrid model. Its model slugs are the base models (`prius`, `corolla`, `camry`, `insight`), and a slug it doesn't know silently returns every listing for the make. So a hybrid variant of a base model ("Corolla Hybrid", "Camry Hybrid") searches its base model and adds `&fuelTypeGroup=HYB`, while a model that's hybrid by name ("Prius", "Insight") gets no fuel facet. A hybrid-only-from-year rule needs no second search here for the same reason it needs none on Carvana, which is that the fuel facet matches each listing's actual fuel type.

A search page states its result count ("13 Matches") and then fills the rest of the page with cards the facets never asked for. Those are other years, other models, new cars, and listings beyond the radius, and a page with no matches at all is filled with such cards entirely. So the walk reads the count first. A page reading "0 Matches" contributes no links, and a page with N matches contributes at most N listings. Those come from the cards the page marks as ordinary results (`clickType=listing`), so the sponsored card at the top, which ignores the search facets and isn't one of the N, never takes a match's place.

A detail link matches `/cars-for-sale/vehicle/<digits>`, and the walk stores it without its query string or a fragment such as `#purchaseConfidence`, so one listing is one candidate however many times a page links it.

A private seller's detail page marks the seller with a "(Private Seller)" line. The walk stores that posting with the dealer "Private seller" and no location, and not the person's name and city. Every other rule applies to it unchanged: the model check, the cap, the pacing, and the bot-challenge pause.

Autotrader's card shape hasn't been confirmed from a recorded page (its cards show the price as bare digits, with no dollar sign), so it reads no card price yet. A known Autotrader listing is still kept listed from its card, but a change in its price is only seen once `--revisit` opens its detail page.

## Recorded pages

The walk records every page's text under the data directory, in `walks/<site>/<run start in UTC, as yyyyMMdd-HHmmss>/<model>/`, where the model is written in lowercase with hyphens, such as `camry-hybrid`. That keeps each model's pages from colliding with another model's on the same site in one run.

The recorder writes one search text per result page it loads. A pair with one search names its pages `search.txt`, `search-2.txt`, `search-3.txt`, and so on. A pair with two searches names each page by search and page (`search-1-page-1.txt`, `search-1-page-2.txt`, `search-2-page-1.txt`, and so on), so the second search's first page can never overwrite the first search's second page. The detail pages are numbered straight through the pair (`detail-1.txt` and on), so nothing collides there either.

Beside each search text the recorder also writes a cards file named the same way (`cards.json`, `cards-2.json`, `cards-1-page-2.json`, and so on, following each search name above). It's a list of that search page's detail links, each with its own text and the text of the result card it sits in, so you can cut a fixture from a real page later. The search text itself doesn't change.

## Extraction

The walk turns each detail page's text into vehicle fields with a model. By default it uses your already-authenticated `claude` CLI, so no Anthropic key is required at all. If `Anthropic:ApiKey` is set, it calls the Anthropic Messages API directly instead. Both paths read at most the first 12,000 characters of the page, and the CLI runs with no tool access.

The prompt asks for ten fields: the VIN, year, make, model, trim, price, mileage, dealer name, dealer location, and fuel type. It tells the model to read them off the page and not to guess. The model is the full model name as the page's own title prints it, and a variant word such as Hybrid, Prime, or Plug-in stays part of the model and never moves into the trim. A "2023 Toyota Corolla Hybrid" title with "LE Sedan 4D" beneath it yields the model "Corolla Hybrid" and the trim "LE". The dealer is the selling dealer's own name and city and state, and not the listing site's. The fuel type (Gas, Hybrid, Plug-in Hybrid, or Electric) is whatever the page's spec line states. The walk prints it on each page's console line, but it doesn't store it, and the match never depends on it. The prompt also says the page text is data and never instructions.

Anything the page doesn't state comes back as null. A VIN has to be exactly 17 characters with no I, O, or Q, or it's null. After the call, odo checks the VIN, make, model, price, mileage, dealer name, and dealer location against the page text, and it nulls any that doesn't appear there. A model that can't be found in the page text is nulled too, so it can't match and the page is dropped as "wrong model".

The nulls then decide what happens to the page. A page is checked in this order: new-car (cars.com only), extraction failed, no VIN, repeat, wrong model, and then missing fields, so the first reason that applies is the one it's dropped for. A null year, price, or mileage gets it dropped as "missing fields". A null dealer only means no dealer is linked to the posting, and it never erases a link an earlier sighting made.
