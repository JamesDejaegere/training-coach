# Training Coach – Product & Technical Specification

Status: **Agreed** (design session 2026-10-17)
Owner: James Dejaegere
Scope of this document: the whole product vision (phases 1–4) with full detail for **Phase 1**.

---

## 1. Purpose

A personal workout application that acts as a training coach. It combines running, cycling,
strength and mobility training, grounds every insight in the athlete's real data (Strava + Polar),
and — in later phases — plans, adapts and publishes training automatically.

Immediate goal of the athlete: run the **Antwerp 10 Miles on Sunday 25 April 2027** faster than
1:27:22 (target 1:25:00, ambitious 1:22:00) **without injury**. Injury history in 2025–2026
(foot sprain, hamstring, calf twice) makes load management and strength work first-class concerns.

## 2. Users

- Phase 1–3: **single user** (the owner).
- The data model must not preclude multiple users later (every aggregate is owned by a `UserId`),
  but no user management, authentication or tenant isolation is built now.

## 3. Phasing

| Phase | Name | Content |
|---|---|---|
| **1** | Foundation | Strava + Polar sync, plan import & management, plan-vs-actual, load/fitness/fatigue/form, recovery, warnings, live strength logging, muscle map |
| 2 | Smart coach | Adaptive plan (reschedules after missed/extra sessions), plan generation for running, cycling, **strength and mobility** based on status/fatigue |
| 3 | Watch automation | Workouts published to the watch automatically through a swappable publishing layer |
| 4 | Routes | Routes generated to match the planned session (loops from home, exact distance, GPX to Polar Flow), optionally cycling routes |

Each phase must be usable on its own. Phase 2 starts only after Phase 1's definition of done (§12).

## 4. Architecture

| Concern | Decision |
|---|---|
| Runtime | .NET 10 (LTS) |
| UI | Blazor Web App, **Interactive Server** render mode, responsive (phone-friendly) |
| Orchestration | .NET Aspire (AppHost + ServiceDefaults; dashboard for logs and background jobs) |
| Persistence | SQLite via EF Core (migrations); provider swappable for cloud later |
| Background work | Hosted service: catch-up sync on start, then hourly; manual "Sync now" |
| Tests | xUnit for all calculations (load, matching, warnings, progression) |
| CI | GitHub Actions: build + test on every push/PR |
| Hosting (phase 1) | Local on the owner's Windows laptop, started manually |
| Network profiles | `Local` (default, localhost only) and `HomeNetwork` (binds to LAN so the phone can open it during strength workouts). **No authentication for now** – to be added once the app runs continuously |
| Language | UI in **Dutch**; code, comments and technical docs in English |
| Repository | Private GitHub repo `jamesdejaegere/training-coach` |

### 4.1 Data location

- Configurable `DataDirectory`; default: a `data/` folder **next to the application**, outside the
  build output (`bin/`) so builds never wipe it. During development: `data/` in the repository root,
  listed in `.gitignore`.
- Contents: SQLite database, `backups/` with a daily copy, keeping the latest **14**.
- Secrets (Strava/Polar client id + secret, OAuth tokens encryption key) in **.NET User Secrets**,
  never in the data folder or Git.
- Cloud/multi-user storage is an explicit open point for the cloud migration (see §13).

### 4.2 Suggested solution layout

```
src/
  TrainingCoach.AppHost/          Aspire host
  TrainingCoach.ServiceDefaults/  Aspire defaults
  TrainingCoach.Web/              Blazor Web App (UI, endpoints, OAuth callbacks)
  TrainingCoach.Domain/           Entities, value objects, calculations (pure, no I/O)
  TrainingCoach.Infrastructure/   EF Core, Strava client, Polar client, importers, background sync
tests/
  TrainingCoach.Domain.Tests/
  TrainingCoach.Infrastructure.Tests/
docs/
```

The publishing of workouts to a watch (phase 3) sits behind an `IWorkoutPublisher` abstraction so the
target (Polar Flow browser automation, manual hand-off, Garmin/Coros) can be chosen later without a rewrite.

## 5. Data sources

### 5.1 Strava (API v3, OAuth)

- Full history import once (all activities), then incremental sync.
- Per activity: summary (sport, start, distance, moving/elapsed time, elevation, speeds, cadence,
  calories, Relative Effort), and HR/pace/power streams when needed for load.
- Webhooks only after cloud migration (need a public URL); phase 1 polls.
- Strava strength activities contain **no** exercise/set data for this athlete (verified) – strength
  details are logged in-app.

### 5.2 Polar AccessLink (OAuth)

- Read-only API. **No endpoint exists to create training targets/favourites** (verified) – relevant for phase 3.
- Exercises: summary, HR zones, `training_load` / `training_load_pro`, HR samples (`samples=true`),
  route. **Only exercises uploaded after the user registered with our client, last 30 days.**
- Also used: Cardio load, Nightly Recharge (HRV, resting HR, ANS charge), Sleep, Continuous HR (5-min).
- Redirect URL (local): `https://localhost:7001/callback/polar`.

### 5.3 Polar Flow data export (one-off import)

- The athlete requests "Download your data" in the Polar account; the app imports the zip (JSON)
  to back-fill HRV, recovery, sleep and exercise history that AccessLink cannot provide.
- Exact file format to be verified against a real export before implementing the importer.

### 5.4 Training plan (one-off import)

- Import `Schema_Antwerp_10_Miles_2027.xlsx` (sheet `Dagschema`, plus `Polar trainingen` for the
  phased workout definitions). After import the **app is the source of truth**; the Excel is not re-read.

### 5.5 De-duplication

The same session can arrive from Strava and Polar. Sessions are merged when sport family matches and
start times are within 5 minutes and durations within 10 %. Polar is preferred for HR data, Strava
keeps its id/link and Relative Effort.

## 6. Domain model (phase 1, conceptual)

- **Athlete settings**: max HR (start 168), HR zones and pace zones (**effective-dated**), resting HR
  (derived from Nightly Recharge, overridable), body weight, home & work locations (for commute
  detection), equipment profiles, data directory.
- **Activity**: sport, start, duration, distance, sources (Strava id, Polar id), HR summary, load
  (value + method), Relative Effort, Polar cardio load, commute flag, RPE (1–10), pain reports.
- **Planned session**: date, sport, title, structured phases (type, duration/distance, target zone,
  notes), planned distance, status (planned / done / skipped / moved), link to matched activity.
- **Strength session**: start, duration, location/equipment profile, exercise entries → sets
  (reps or duration, side L/R, optional added load, completed flag), session RPE, pain reports.
- **Exercise** (library): name (NL + EN), pattern, primary/secondary muscles, equipment, level,
  instructions (NL), progression ladder membership + step index.
- **Recovery day**: date, HRV, resting HR, ANS charge, sleep metrics (from Polar).
- **Daily load**: per day per sport family; derived fitness/fatigue/form.
- **Warning**: type, date, message, suggested action, acknowledged.

## 7. Calculations

### 7.1 Training load

Per activity, best available source:
1. **Polar HR samples** → Banister **TRIMP** using HRmax and resting HR.
2. **Strava HR stream** → same TRIMP.
3. No HR → duration-based estimate per sport.
4. **Strength / mobility** → **session-RPE** (RPE × minutes), scaled to the TRIMP range so all
   sports add up in one total.

Polar cardio load and Strava Relative Effort are stored and displayed for comparison only.

### 7.2 Fitness / fatigue / form

- Fitness = exponentially weighted average of daily load, time constant **42 days**.
- Fatigue = same, **7 days**.
- Form = fitness − fatigue.
- Also weekly volume per sport (km, time) and acute:chronic ratio (7-day load ÷ 28-day average weekly load).

### 7.3 Muscle load

- Strength: sets per muscle group (primary = 1.0, secondary = 0.5).
- Running and cycling contribute to calves, hamstrings, quadriceps, glutes (and hip flexors for
  running) proportionally to their load, so leg fatigue after a long run is visible.
- Shown for a single session and as a rolling 7-day view.

### 7.4 Plan ↔ activity matching

- Same date + same sport family → match; multiple candidates → closest planned distance.
- Rides that start within 500 m of home and end within 500 m of work (or vice versa) are **commutes**.
- Unmatched activities become "extra". The user can re-link or unlink manually.

## 8. Warnings (phase 1: suggestions only, never automatic plan changes)

| Type | Rule |
|---|---|
| Build-up | Running volume this week > +10 % vs last week, **or** acute:chronic ratio > 1.3 |
| Recovery | HRV below the normal band for 3 consecutive nights, **or** resting HR > 5 bpm above baseline – while a hard session is planned |
| Pain | Pain reported twice within 7 days at the same location → suggest replacing the next run by a ride or rest |

All thresholds are configurable. Warnings appear in-app only (on **Vandaag**); e-mail/push after cloud migration.

## 9. Strength training

- **Equipment profile (home)**: bodyweight, ab wheel, stairs/step, chair/bench, towel, wall.
  Extensible (bands, dumbbells, gym later).
- **Exercise library**: text data from **free-exercise-db** (Unlicense / public domain), a curated
  subset translated to Dutch, plus custom exercises. **The repository's photos are not used**
  (image licence unclear); each exercise is illustrated with the app's own muscle-map SVG
  highlighting primary/secondary muscles.
- **Progression ladders** per movement pattern, e.g.
  - calf: two-leg → single-leg → single-leg slow eccentric → with pause in stretch
  - hamstring: bridge → single-leg bridge → towel curl → Nordic curl
  - squat: squat → split squat → Bulgarian split squat → skater squat
  Reaching the top of the rep range (e.g. 3×15) at RPE ≤ 7 suggests the next step.
- **Live logging on the phone** (via `HomeNetwork` profile): tick sets, rest timer, previous
  performance shown next to each set, session RPE + pain at the end.
- **Muscle map**: own SVG, front and back; per-session highlight and 7-day load heat map.

### 9.1 Generator rules (implemented in phase 2, recorded now)

- 2 sessions/week, 20–30 min.
- Every week at least calf, hamstring, hip and core.
- No heavy leg work within 24 h before a quality run or long run; prefer the same day as an easy run, after it.
- 48 h recovery per heavily loaded muscle group.
- Poor recovery (low HRV or strongly negative form) → shorter/lighter session or a mobility routine instead.
- Taper weeks before the target race: maintenance only, fewer sets.
- Goal setting: running support first until the 10 Miles, general fitness selectable afterwards.

## 10. Screens (phase 1, Dutch UI)

1. **Vandaag** – today's planned session with its phases, form + last night's recovery, warnings.
2. **Schema** – week calendar, plan vs done; move, skip and edit sessions.
3. **Activiteiten** – list + detail, RPE and pain entry, re-link to plan.
4. **Krachttraining** – live session mode, exercise library, progression.
5. **Belasting & herstel** – fitness/fatigue/form chart, weekly volume per sport, HRV and resting HR, muscle map.
6. **Instellingen** – zones (effective-dated), connections (Strava, Polar), home/work locations,
   equipment profiles, warning thresholds, data directory.

## 11. Planning scope

- Planned and (from phase 2) generated: **running, cycling, strength, mobility**.
- Other sports (tennis, walking, golf, …) are synced and count towards load but are not planned.

## 12. Phase 1 – definition of done

Phase 1 is done when, for one real week of the training plan:
- all Strava and Polar sessions are imported and correctly linked to the plan (commutes recognised);
- at least one strength session is logged live on the phone with a correct muscle map;
- fitness/fatigue/form and recovery charts are consistent with what Polar and Strava show;
- at least one warning appears correctly for a test case;
- tests pass in CI.

## 13. Open points / deferred

- **Cloud migration**: hosting choice, multi-user storage and tenant isolation, authentication,
  Strava webhooks, notifications.
- **Authentication** on the home network: deferred until the app runs continuously.
- **Watch publishing** (phase 3): choose between Polar Flow browser automation (unofficial),
  manual hand-off, or a different watch brand.
- **Polar export format**: verify with a real export.
- **Auto-start with Windows**: optional, later.

## 14. Prerequisites (owner actions)

1. Create the empty private GitHub repository `training-coach` and link GitHub to Claude.
2. Create a Strava API application (Authorization Callback Domain: `localhost`).
3. Register a Polar AccessLink client (redirect URL `https://localhost:7001/callback/polar`).
4. Request the Polar Flow data export.
