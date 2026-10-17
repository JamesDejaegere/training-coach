# CLAUDE.md – Training Coach

Personal workout app for James Dejaegere (Aalter, Belgium). Read `docs/SPEC.md` (what to build) and
`docs/DECISIONS.md` (why) before changing anything. They are the agreed design; don't silently
deviate – propose changes to the owner first and update both files when a decision changes.

## Owner preferences

- Language with the owner: **Dutch**. UI text: Dutch. Code, comments, commits, technical docs: English.
- Default stack: **C# / Blazor**. .NET 10, Blazor Web App (Interactive Server), .NET Aspire, SQLite + EF Core, xUnit.
- Development machine: Windows x64 laptop with .NET SDK, Aspire and Visual Studio installed.

## Current status

- Design agreed 2026-10-17. Nothing implemented yet.
- **Next: Phase 1** (SPEC §3, §12). Suggested order:
  1. Solution skeleton (SPEC §4.2): AppHost, ServiceDefaults, Web, Domain, Infrastructure, test projects; CI workflow; `data/` folder handling (SPEC §4.1); `Local` and `HomeNetwork` launch profiles.
  2. Domain + calculations with tests first: TRIMP, session-RPE, fitness/fatigue/form, plan matching, commute detection, warnings.
  3. Plan import from `seed/Schema_Antwerp_10_Miles_2027.xlsx` (sheet `Dagschema`; workout phases in `Polar trainingen`).
  4. Strava OAuth + full-history sync + hourly background sync.
  5. Polar AccessLink OAuth + exercises/recovery sync; Polar data-export importer (verify format with a real export first).
  6. Screens: Vandaag, Schema, Activiteiten, Krachttraining (live, phone), Belasting & herstel (muscle map), Instellingen.

## Ground rules

- Secrets (Strava/Polar client id/secret) only in .NET User Secrets – never in Git or `data/`.
- `data/` is git-ignored and lives outside `bin/`.
- No authentication yet (deliberate, D21). Default launch profile binds to localhost only.
- Polar AccessLink is read-only: there is no API to push workouts to the watch (relevant for phase 3, D6).
- Exercise library: use free-exercise-db **text data only** (Unlicense); do not ship its photos (D23).
- Every calculation gets unit tests; CI must stay green.

## Athlete context (for sensible defaults and test data)

- Goal: Antwerp 10 Miles, Sunday 25 April 2027; previous 1:27:22 (2026), target 1:25:00.
- HRmax start value 168 (highest measured 165). Zones: Z1 105–124, Z2 125–137, Z3 138–146, Z4 147–156, Z5 157+.
- Commutes by bike Aalter ⇄ Ghent (~30 km each way), typically Tuesday and Friday.
- Injury history: foot sprain (Nov 2025), hamstring (Feb 2026), calf (Jun 2026, recurring).
- Home strength equipment: bodyweight, ab wheel, stairs, chair, towel, wall.
