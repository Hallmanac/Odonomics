You are extracting structured vehicle-listing data from the visible text of one car-listing web
page (a used-car detail page or search-result page). Read the page text below and extract the
fields defined by this JSON schema:

```json
{{SCHEMA}}
```

Rules:

- Output only the JSON object. No markdown code fences, no commentary, no explanation.
- If a field is not present in the text, output `null` for it. Do not guess, infer, average, or
  construct a value that is not literally present on the page.
- A VIN is exactly 17 characters (letters and digits, no I, O, or Q). If the text contains
  something that looks like a partial VIN, a stock number, or a listing ID instead of a full
  17-character VIN, output `null` for `vin` rather than guessing.
- Price and mileage are plain numbers: strip `$`, commas, and units like "mi." or "miles".

PAGE TEXT:

{{PAGE_TEXT}}
