# Odonomics

## What it is

Odonomics is a personal car-search and total-cost-of-ownership tool that finds used-car listings across dealer APIs and major retail sites, vets the VIN and the dealer, and ranks every candidate by what it will actually cost to own per month over ten years.

It's built to answer one question: which car goes the longest for the least, with the fewest surprises.

## Why it exists

A used car's asking price says very little about what it will cost you. The real number is the loan, the insurance, the fuel, the maintenance, and what the car is worth when you're done with it, so odo works all of that out per month and lets you compare cars on the same footing. It also does the vetting that usually eats an evening per car, checking recalls, safety ratings, listing history, and the dealer, so what you end up with is a short list you can trust.

## Features

- [Search two listing APIs](docs/commands.md#what-search-asks-for) for used cars that fit your scenario
- [Walk cars.com, Carvana, and Autotrader](docs/walk.md) in your own browser
- [Vet each VIN](docs/research.md) with safety ratings, listing history, and red flags
- [Grade dealers](docs/dealers.md) with CarEdge
- [Rank every car](docs/cost-model.md) by its monthly cost over ten years
- [Work backwards from a monthly budget](docs/cost-model.md#budget) to a maximum price
- [Describe your purchase](docs/scenario.md) in one scenario file
- [Keep it all](docs/ledger.md) in a local SQLite ledger

## Quick start

```
git clone https://github.com/Hallmanac/Odonomics.git
cd Odonomics
dotnet build
cd src/Odonomics
dotnet user-secrets set "AutoDev:ApiKey" "..."
dotnet user-secrets set "Marketcheck:ApiKey" "..."
cd ../..
dotnet run --project src/Odonomics -- search
```

The docs call that last command `odo search`. From here, [docs/running.md](docs/running.md) takes a purchase from start to finish.

## Docs

- [setup.md](docs/setup.md): the SDK, secrets, data directory, and development
- [running.md](docs/running.md): a purchase, start to finish
- [commands.md](docs/commands.md): every command and flag
- [research.md](docs/research.md): VIN research and red flags
- [walk.md](docs/walk.md): the assisted browser walk
- [dealers.md](docs/dealers.md): dealer identity and grades
- [cost-model.md](docs/cost-model.md): how monthly cost is built
- [scenario.md](docs/scenario.md): the scenario file
- [ledger.md](docs/ledger.md): the SQLite ledger
- [decisions.md](docs/decisions.md): dated stories behind the rules
- [spike-findings.md](docs/spike-findings.md): what the earlier spike found

## Status

This is v0, with one purchase, one scenario, and a tool built for one person, so there are no releases and the multi-purchase design waits for a retrospective after the car is bought.
