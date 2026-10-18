# Decision log

Decisions taken in the design session of 2026-10-17. Each entry: decision – rationale.
Details live in [SPEC.md](SPEC.md).

| # | Decision | Rationale |
|---|---|---|
| D1 | Single user now, multi-user-ready data model | Keeps phase 1 simple without blocking a later cloud version |
| D2 | Run locally first, built to move to the cloud without rewrite | Free and simple; cloud when the basics work |
| D3 | Strava **and** Polar AccessLink in phase 1 | Polar adds HR samples, cardio load, HRV and sleep – key for injury prevention |
| D4 | Import the Excel plan once; manage the plan in the app afterwards | One source of truth; generation comes in phase 2 |
| D5 | Phases: 1 foundation → 2 smart coach → 3 watch automation → 4 routes | Each phase is usable on its own; routes depend on plan data |
| D6 | `IWorkoutPublisher` abstraction; watch target decided in phase 3 | Polar has no official API to push workouts |
| D7 | Routes = loops from home, exact distance, GPX to Polar Flow | Matches how the athlete trains |
| D8 | Private GitHub repo `jamesdejaegere/training-coach` | Versioning, backup, continuity across sessions |
| D9 | .NET 10, Blazor Web App (Interactive Server), .NET Aspire | LTS; secrets stay server-side; Aspire already installed and eases cloud move |
| D10 | SQLite + EF Core | Zero install, enough for one user, swappable provider |
| D11 | Full Strava history once, then hourly polling + manual sync; webhooks after cloud | Webhooks need a public URL |
| D12 | Own TRIMP load; source order Polar HR → Strava HR → duration estimate; strength via session-RPE | Consistent load across all sports; RPE captures strength stress HR misses |
| D13 | Automatic plan matching + commute detection via home/work locations | Removes manual ticking-off |
| D14 | RPE and pain logged after every session | Early warning signal for injuries |
| D15 | Dutch UI, English code/docs | Owner's language; standard dev practice |
| D16 | xUnit tests + GitHub Actions CI | Calculations must be trustworthy |
| D17 | Data in configurable `data/` folder next to the app (outside `bin/`, git-ignored), 14 daily backups; secrets in User Secrets | Not mixed with personal documents; survives rebuilds |
| D18 | Manual start with catch-up sync | App does not run continuously yet |
| D19 | Warnings (build-up, recovery, pain) as suggestions only, in-app | Coach advises; automatic changes come in phase 2 |
| D20 | Strength logging + muscle map in phase 1; strength generation in phase 2 | Generation shares the coach logic with plan adaptation |
| D21 | Live strength logging on the phone via a `HomeNetwork` profile, no auth for now | Strength is done at home; auth added when the app runs continuously |
| D22 | Home equipment: bodyweight, ab wheel, stairs, chair, towel, wall; progression ladders | No weights available; progression via harder variants |
| D23 | Exercise text data from free-exercise-db (Unlicense); own muscle-map SVG instead of its photos | Photo licence unclear; legally clean and matches the muscle-map feature |
| D24 | Plan and generate running, cycling, strength, mobility; other sports only tracked | Focus on what drives the race goal |
| D25 | Calculation details (implemented in `TrainingCoach.Domain`): Banister TRIMP with male exponent 1.92 (configurable); session-RPE scale 0.44 (RPE 4 × 60 min ≈ TRIMP of an hour at 135 bpm); duration fallback per sport (run 1.7, ride 1.2, strength 0.9, mobility 0.3, other 0.8 per minute); fitness/fatigue as EWMA with α = 1 − e^(−1/τ) starting at 0, form = fitness − fatigue of the same day; ACWR = 7-day sum ÷ (28-day sum / 4); HRV band = mean ± 1 SD of history | Concrete, testable defaults for SPEC §7–8; revisit when real Polar/Strava data is available |
| D26 | Commute rides are never matched to planned sessions and are not "extra"; plan matching is greedy on smallest distance gap over all same-date/same-sport pairs | Commutes are not training; best overall pairing when several runs share a day |
