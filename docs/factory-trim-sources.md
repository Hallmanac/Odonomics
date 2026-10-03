# Factory trim table sources

This note is the research behind `src/Odonomics/Data/factory-trim-equipment.json` (see [scenario.md](scenario.md#the-factory-trim-table)). It is a copy of the Result section of a web-research job run on 2026-10-03; the table below is the research as it was written, and the section after it says how each row became a table entry.

## How the research became table entries

- A row rated high or medium confidence became a table entry, per feature. "Standard" maps to `present` and "Not available" to `absent`.
- "Optional", "Unsure", and "Probably" map to unknown, and so does every row rated low (including "medium-low"). Unknown is never written to the table: the entry leaves that feature out, and a car with nothing definite on either feature has no entry at all. Those cars fall back to the window sticker, and `odo rank` asks for a check when the sticker is silent.
- Rows with two confidences (for example "Start high, entry medium") keep both in the entry's `confidence` text when both features are stated, and only the stated feature's confidence otherwise.
- Where the Smart Key covers only the driver's door (Prius L Eco and LE, 2019 to 2022), the entry says `present` for proximity entry and its `note` says so. For 2022 the research only says "probably", so entry is left unknown there and the note records the lead.
- Trims named with their all-wheel-drive variant in the research ("LE (incl. AWD-e)", "SE, SE AWD") became one entry per spelling, since a trim is matched exactly first. Only when no row matches the stored trim does the lookup drop a trailing drivetrain token (FWD, AWD, AWD-e, 4WD, 2WD) and try again, so a stored "Limited FWD" reads the "Limited" row (see [scenario.md](scenario.md#the-factory-trim-table)).
- Every entry keeps the source URL (or URLs) the research gave, in `source`.
- Rows left out entirely: Corolla Hybrid LE 2020 to 2021 (low), Corolla Hybrid LE 2023 to 2026 (optional), Camry Hybrid 2019 to 2024 (low, KBB only), Prius L Eco 2020 to 2021 (low or probably), and Prius LE 2023 to 2026 (low).
- The Honda Insight LX records proximity entry as absent and push-button start as present for 2019 to 2021. Whether a remote keyless fob alone satisfies the scenario's "keyless or smart key entry" wording is a separate ruling; the table only records the facts, and the scenario requirement decides what to do with them.

## Research result

Research done 2026-10-03. Nothing here needed Brian. Only web research was done: no repository changes, no odo commands, no Marketcheck calls.

### How to read this

- Confidence "high" means a manufacturer page (Honda's information center or a Toyota newsroom release) states the row directly. "Medium" means a manufacturer page implies it, or a spec database (KBB) states it and nothing contradicts it. "Low" means a single weak source or conflicting sources.
- KBB's spec pages are inconsistent with themselves (for example the Corolla Hybrid LE reads "standard" for 2020 and 2022 but "optional" for 2024 to 2026, and the Prius LE reads "none" for 2023 to 2025 but "standard" for 2026), so no row rests on KBB alone unless it is marked low or medium.
- Toyota's product-information PDFs, toyota.com, Edmunds, and every Monroney page we tried returned 403, 404, or 410. A Monroney sticker or Toyota product-information PDF opened in a browser would settle most of the medium and low rows. The Camry rows for 2018 to 2024 in particular have no Toyota-origin source at all.
- "Driver's door" in the Prius rows means the Smart Key proximity antenna covers only the driver's door (plus ignition), where XLE and above cover three doors. Either counts as proximity entry for the mechanism, but it is a real difference.
- Package names and prices for Corolla Hybrid LE and Camry 2025 LE/SE came from search snippets or dealer pages, not from Toyota, so treat them as leads.

### Table

| Model | Years | Trim | Smart-key entry | Push-button start | Source URL | Confidence |
|---|---|---|---|---|---|---|
| Honda Insight | 2019 to 2021 | LX | Not available (remote entry only) | Standard | https://www.hondainfocenter.com/2019/Insight/Feature-Guide/Features-by-Trim/ (same page pattern for /2020/ and /2021/) | High |
| Honda Insight | 2019 to 2021 | EX | Standard (Smart Entry with Walk Away Auto Lock) | Standard | https://www.hondainfocenter.com/2019/Insight/Feature-Guide/Features-by-Trim/ | High |
| Honda Insight | 2019 to 2021 | Touring | Standard (carried from EX) | Standard | https://www.hondainfocenter.com/2019/Insight/Feature-Guide/Features-by-Trim/ | High |
| Honda Insight | 2022 | LX | Trim not offered in 2022 | Trim not offered in 2022 | https://www.hondainfocenter.com/2022/Insight/Feature-Guide/Features-by-Trim/ (lists only EX and Touring) | High |
| Honda Insight | 2022 | EX, Touring | Standard | Standard | https://www.hondainfocenter.com/2022/Insight/Feature-Guide/Features-by-Trim/ | High |
| Toyota Corolla Hybrid | 2020 | LE (only trim) | Unsure | Unsure (KBB says standard) | https://www.kbb.com/toyota/corolla-hybrid/2020/specs | Low |
| Toyota Corolla Hybrid | 2021 | LE (only trim) | Unsure (KBB says standard) | Unsure (KBB says standard) | https://www.kbb.com/toyota/corolla-hybrid/2021/specs | Low |
| Toyota Corolla Hybrid | 2022 | LE (only trim) | Standard | Standard | https://pressroom.toyota.com/corolla-hybrid-legendary-value-epic-mpg/ ("A Smart Key System with Push Button Start is also standard.", dated 2021-12-16) | High |
| Toyota Corolla Hybrid | 2023 | LE, LE AWD | Optional (leads: "LE Convenience Package" or "LE Premium Package"; unconfirmed) | Optional (same package) | https://www.kbb.com/toyota/corolla-hybrid/2023/specs; Toyota's release is silent on LE: https://pressroom.toyota.com/toyota-boosts-2023-corolla-hybrid-with-all-new-infrared-edition-new-grades-and-available-awd/ | Medium |
| Toyota Corolla Hybrid | 2023 | SE, SE AWD, SE Infrared Edition, XLE | Standard | Standard | https://pressroom.toyota.com/toyota-boosts-2023-corolla-hybrid-with-all-new-infrared-edition-new-grades-and-available-awd/ | High |
| Toyota Corolla Hybrid | 2024 | LE, LE AWD | Optional | Optional | https://www.kbb.com/toyota/corolla-hybrid/2024/specs | Medium |
| Toyota Corolla Hybrid | 2024 | SE, SE AWD, Nightshade, XLE | Standard | Standard | https://www.kbb.com/toyota/corolla-hybrid/2024/specs | Medium |
| Toyota Corolla Hybrid | 2025 | LE, LE AWD | Optional (lead: "LE Premium Package") | Optional | https://www.kbb.com/toyota/corolla-hybrid/2025/specs | Medium |
| Toyota Corolla Hybrid | 2025 | SE, SE AWD, XLE | Standard | Standard | https://www.kbb.com/toyota/corolla-hybrid/2025/specs | Medium |
| Toyota Corolla Hybrid | 2026 | LE, LE AWD | Optional (lead: "LE Premium Package") | Optional | https://www.kbb.com/toyota/corolla-hybrid/2026/specs | Medium |
| Toyota Corolla Hybrid | 2026 | SE, SE AWD, XLE | Standard | Standard | https://www.kbb.com/toyota/corolla-hybrid/2026/specs | Medium |
| Toyota Camry Hybrid | 2018 | LE, SE, XLE | Standard (proximity sensing keyless entry) | Standard | https://www.kbb.com/toyota/camry-hybrid/2018/specs | Medium |
| Toyota Camry Hybrid | 2019 | LE, SE, XLE | Standard | Standard | https://www.kbb.com/toyota/camry-hybrid/2019/specs/ | Low (KBB only) |
| Toyota Camry Hybrid | 2020 | LE, SE, XLE | Standard | Standard | https://www.kbb.com/toyota/camry-hybrid/2020/specs/ | Low (KBB only) |
| Toyota Camry Hybrid | 2021 | LE, SE, XLE, XSE | Standard | Standard | https://www.kbb.com/toyota/camry-hybrid/2021/specs/ | Low (KBB only) |
| Toyota Camry Hybrid | 2022 | LE, SE, SE Nightshade, XLE, XSE | Standard | Standard | https://www.kbb.com/toyota/camry-hybrid/2022/specs/ | Low (KBB only) |
| Toyota Camry Hybrid | 2023 | LE, SE, SE Nightshade, XLE, XSE | Standard | Standard | https://www.kbb.com/toyota/camry-hybrid/2023/specs/ | Low (KBB only) |
| Toyota Camry Hybrid | 2024 | LE, SE, SE Nightshade, XLE, XSE | Standard | Standard | https://www.kbb.com/toyota/camry-hybrid/2024/specs/ | Low (KBB only) |
| Toyota Camry Hybrid | 2025 | LE, SE | Optional (Convenience Package adds Smart Key on front doors); without it, remote keyless entry only | Standard | https://pressroom.toyota.com/toyota-camry-goes-exclusively-hybrid-plus-a-new-look-and-more-technology/ (push-button start); https://www.startoyota.com/toyota-research/camry-trim-levels/ (package, dealer page) | Start high, entry medium |
| Toyota Camry Hybrid | 2025 | XLE, XSE | Standard | Standard | https://www.kbb.com/toyota/camry/2025/specs (entry); Toyota release above (start) | Start high, entry medium-low |
| Toyota Prius | 2019 | L Eco, LE (incl. AWD-e) | Standard, driver's door | Standard | https://pressroom.toyota.com/2019-prius-most-capable-thanks-available-new-awd-e-system/ ("comes standard with driver's door Smart Key System, Push Button Start") | High |
| Toyota Prius | 2019 | XLE, Limited | Standard, three doors | Standard | Same Toyota page | High |
| Toyota Prius | 2020 | LE (incl. AWD-e) | Standard, driver's door | Standard | https://pressroom.toyota.com/2020-toyota-prius-brings-its-a-game-with-standard-apple-carplay-and-many-more-features/ | High |
| Toyota Prius | 2020 | XLE (incl. AWD-e) | Standard, three doors | Standard | Same Toyota page | High |
| Toyota Prius | 2020 | L Eco | Probably standard, driver's door | Probably standard | https://www.kbb.com/toyota/prius/2020/specs | Low |
| Toyota Prius | 2020 | Limited | Standard, three doors | Standard | https://www.kbb.com/toyota/prius/2020/specs | Medium |
| Toyota Prius | 2021 | L Eco, LE, XLE, Limited | Standard (driver's door on L Eco and LE, three doors on XLE and Limited, by carry-over) | Standard | https://www.kbb.com/toyota/prius/2021/specs | Medium (L Eco low) |
| Toyota Prius | 2021 | 2020 Edition (XLE-based) | Standard, three doors | Standard | https://pressroom.toyota.com/?p=60401 | High |
| Toyota Prius | 2022 | L Eco, LE, LE AWD-e | Probably standard, driver's door | Standard | https://pressroom.toyota.com/best-selling-hybrid-gets-dramatic-2022-toyota-prius-adds-night-shade-special-edition/ (XLE, Nightshade, Limited "add Smart Key on three doors"); https://www.kbb.com/toyota/prius/2022/specs | Medium |
| Toyota Prius | 2022 | XLE, XLE AWD-e, Limited | Standard, three doors | Standard | Same Toyota page (entry); KBB 2022 (start) | Entry high, start medium |
| Toyota Prius | 2022 | Nightshade (FWD, AWD-e) | Standard, three doors | Standard | https://pressroom.toyota.com/?p=66260 | High |
| Toyota Prius | 2023 to 2026 | LE (FWD, AWD) | Unsure (probably driver's door only) | Probably standard | https://pressroom.toyota.com/hybrid-reborn-2023-toyota-prius-revealed/ (Smart Key listed as an XLE add over LE); see unsure list | Low |
| Toyota Prius | 2023 to 2026 | XLE, Limited | Standard, three doors | Standard (inferred) | https://pressroom.toyota.com/hybrid-reborn-2023-toyota-prius-revealed/; KBB 2023 to 2025 | Entry high, start medium |
| Toyota Prius | 2025 to 2026 | Nightshade (XLE-based) | Standard, three doors | Standard | https://www.kbb.com/toyota/prius/2025/specs; https://pressroom.toyota.com/?p=98271 | Medium |

### Years where entry and start differ

- Honda Insight LX, 2019 to 2021: push-button start without proximity entry. This is the clearest case.
- Toyota Camry Hybrid LE and SE, 2025: push-button start is standard, but front-door Smart Key is part of the Convenience Package.
- Toyota Prius 2023 and later LE: start is on every trim, but entry may cover the driver's door only (unconfirmed).
- Toyota Prius L Eco and LE, 2019 to 2022: both features are present, but proximity entry covers only the driver's door.
- Toyota Corolla Hybrid: no year found where the two differ. They come together (standard or in the same package).

### Rows I am unsure about

1. **Corolla Hybrid LE 2020 and 2021.** Toyota's pages I could open do not state it. KBB says push-button start is standard for 2020 but lists proximity entry as not available, which is odd for one system. Toyota's Apex Edition release says an LE Convenience Package including Smart Key is new for the 2021 gasoline sedan, and does not mention the Hybrid. Treat as unsure and check a sticker.
2. **Corolla Hybrid LE 2023 to 2026 package.** Toyota's 2023 release is silent on the LE; KBB says optional; and the rank-3 car (a 2023 LE with only "Keyless Entry, Power Door Locks" on the sticker) fits "not standard on LE". Cars.com's 2023 specs page says all trims have it, which conflicts with the sticker, so I discounted it. The package names (LE Convenience Package, about $1,340 for 2023 and 2024; LE Premium Package for 2023, 2025, and 2026) came from search snippets and were not confirmed on any page.
3. **Corolla Hybrid SE, XLE, Nightshade 2024 to 2026.** Toyota's pages I could open say nothing on Smart Key. "Standard" is inferred from KBB plus Toyota's 2023 wording.
4. **Camry Hybrid 2018 to 2024, every trim.** Every cell rests on KBB (plus one undated dealer page for 2018). Seven years of consistent answers is mildly reassuring, but no Toyota page or Monroney sticker confirmed any of them.
5. **Camry Hybrid 2025 LE and SE.** The optional status and package contents come from KBB and a dealer page. Trunk-only access on LE and SE without the package is unknown. XLE and XSE "standard" comes from KBB only. KBB's 2025 page also claims all-wheel drive is standard on all trims, which looks wrong, so that page deserves extra caution.
6. **Prius LE 2023 to 2026.** Toyota's 2023 release lists Smart Key only under XLE and does not say the LE lacks it. The owner's manual marks the front-passenger antenna "(if equipped)", which fits driver-door-only entry. The only source naming LE as driver's door and ignition only is a Priuschat thread that could not be fetched. KBB says the LE has no keyless entry or push start for 2023 to 2025 (I discounted this because the manual describes a POWER switch with no key slot) and says standard for 2026, which contradicts its own earlier pages. Get an LE sticker if this matters.
7. **Prius L Eco 2020 to 2022.** Toyota's releases list the feature under the LE and are silent on the L Eco. The 2019 release says the Prius "comes standard" with it, so carry-over is likely.
8. **Prius 2021 and 2024, grade-level rows.** No Toyota grade-level text found; the rows rely on KBB or carry-over.
9. **Not covered.** The Prius Prime, Prius c, and Corolla Hybrid 2026 Toyota-origin text were not checked. The 2026 Camry Hybrid was out of scope (the job stops at 2025).

### Suggestion for the table

For the mechanism, the safest first version is to treat a row as "confirmed" only when marked high, treat medium rows as "likely", and send low and unsure rows (Corolla Hybrid 2020 and 2021 LE, Corolla Hybrid 2023 to 2026 LE, Prius 2023 and later LE) to the window sticker check that already exists, since those are exactly the cases where dealer descriptions and spec databases disagree.
