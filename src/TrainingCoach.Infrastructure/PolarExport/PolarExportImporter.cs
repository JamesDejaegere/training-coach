using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrainingCoach.Infrastructure.Persistence;

namespace TrainingCoach.Infrastructure.PolarExport;

public sealed record PolarExportResult(
    int SessionsCreated,
    int SessionsMerged,
    int SessionsUpdated,
    int NightsAdded,
    int NightsFilled,
    int FilesSkipped);

/// <summary>
/// One-off back-fill from the Polar Flow data export (SPEC 5.3): training sessions with 1 Hz heart rate and
/// nightly HRV, resting heart rate, ANS charge and sleep, i.e. what AccessLink only gives for the last 30 days.
/// Idempotent: running it again updates instead of duplicating.
/// </summary>
public sealed class PolarExportImporter(AppDbContext db, TimeProvider time)
{
    private const int BatchSize = 25;

    public async Task<PolarExportResult> ImportAsync(string zipPath, IProgress<string>? progress = null, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        await using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        return await ImportAsync(stream, progress, userId, ct);
    }

    public async Task<PolarExportResult> ImportAsync(Stream zip, IProgress<string>? progress = null, int userId = UserIds.Owner, CancellationToken ct = default)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var entries = archive.Entries.Select(e => (Entry: e, Kind: PolarExportReader.Classify(e))).Where(x => x.Kind != ExportEntryKind.Ignored).ToList();

        var skipped = 0;
        var sessions = await ImportSessionsAsync(entries.Where(x => x.Kind == ExportEntryKind.Session).Select(x => x.Entry).ToList(), progress, userId, ct);
        skipped += sessions.Skipped;

        var nights = new Dictionary<DateOnly, NightData>();
        foreach (var (entry, kind) in entries.Where(x => x.Kind != ExportEntryKind.Session))
        {
            try
            {
                switch (kind)
                {
                    case ExportEntryKind.NightlyRecovery:
                        foreach (var n in PolarExportReader.ParseNightlyRecovery(entry)) nights.GetOrAdd(n.Date).Recovery = n;
                        break;
                    case ExportEntryKind.SleepResult:
                        foreach (var n in PolarExportReader.ParseSleepResult(entry)) nights.GetOrAdd(n.Date).Phases = n;
                        break;
                    case ExportEntryKind.SleepScore:
                        foreach (var n in PolarExportReader.ParseSleepScore(entry)) nights.GetOrAdd(n.Date).Score = n;
                        break;
                }
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException)
            {
                skipped++;
            }
        }

        progress?.Report($"{nights.Count} nachten verwerken…");
        var (added, filled) = await ImportNightsAsync(nights, userId, ct);

        return new PolarExportResult(sessions.Created, sessions.Merged, sessions.Updated, added, filled, skipped);
    }

    // ---- sessions ----

    private async Task<(int Created, int Merged, int Updated, int Skipped)> ImportSessionsAsync(
        IReadOnlyList<ZipArchiveEntry> entries, IProgress<string>? progress, int userId, CancellationToken ct)
    {
        int created = 0, merged = 0, updated = 0, skipped = 0, sinceSave = 0;
        var zone = time.LocalTimeZone;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            ExportSession session;
            try { session = PolarExportReader.ParseSession(entry); }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException or KeyNotFoundException)
            {
                skipped++;
                continue;
            }

            var existing = await db.Activities.SingleOrDefaultAsync(a => a.UserId == userId && a.PolarId == session.ExerciseId, ct);
            if (existing is not null)
            {
                PolarExportMapper.Apply(session, existing, createdFromPolar: false, zone);
                updated++;
            }
            else if (await ActivityLinker.FindAnyAsync(db, userId, PolarExportMapper.KeyOf(session, zone), ct) is { } counterpart)
            {
                PolarExportMapper.Apply(session, counterpart, createdFromPolar: false, zone);
                merged++;
            }
            else
            {
                var row = new ActivityEntity { UserId = userId };
                PolarExportMapper.Apply(session, row, createdFromPolar: true, zone);
                db.Activities.Add(row);
                created++;
            }

            if (++sinceSave < BatchSize) continue;
            await db.SaveChangesAsync(ct);
            sinceSave = 0;
            progress?.Report($"{created + merged + updated} van {entries.Count} trainingen verwerkt…");
        }

        await db.SaveChangesAsync(ct);
        return (created, merged, updated, skipped);
    }

    // ---- nights ----

    private sealed class NightData
    {
        public ExportNight? Recovery { get; set; }
        public ExportSleep? Phases { get; set; }
        public ExportSleep? Score { get; set; }
    }

    private async Task<(int Added, int Filled)> ImportNightsAsync(Dictionary<DateOnly, NightData> nights, int userId, CancellationToken ct)
    {
        if (nights.Count == 0) return (0, 0);

        var existing = (await db.RecoveryDays.Where(r => r.UserId == userId).ToListAsync(ct)).ToDictionary(r => r.Date);
        int added = 0, filled = 0;

        foreach (var (date, data) in nights)
        {
            var isNew = !existing.TryGetValue(date, out var row);
            row ??= new RecoveryDayEntity { UserId = userId, Date = date };

            var changed = false;
            // Existing values (from AccessLink) win; the export only fills gaps.
            changed |= Fill(row.Hrv, data.Recovery?.Rmssd, v => row.Hrv = v);
            changed |= Fill(row.RestingHeartRate, data.Recovery?.RestingHr, v => row.RestingHeartRate = v);
            changed |= Fill(row.AnsCharge, data.Recovery?.AnsCharge, v => row.AnsCharge = v);
            changed |= Fill(row.AnsChargeStatus, data.Recovery?.AnsStatus, v => row.AnsChargeStatus = v);
            changed |= Fill(row.NightlyRechargeStatus, data.Recovery?.RecoveryIndicator, v => row.NightlyRechargeStatus = v);
            // AccessLink reports the sleep score rounded up, so do the same to keep the series consistent.
            changed |= Fill(row.SleepScore, data.Score?.Score is { } score ? Math.Ceiling(score) : null, v => row.SleepScore = v);
            changed |= Fill(row.DeepSleepSeconds, data.Phases?.DeepSeconds, v => row.DeepSleepSeconds = v);
            changed |= Fill(row.RemSleepSeconds, data.Phases?.RemSeconds, v => row.RemSleepSeconds = v);
            changed |= Fill(row.SleepSeconds, TotalSleep(data.Phases), v => row.SleepSeconds = v);

            if (isNew)
            {
                if (!changed) continue;
                db.RecoveryDays.Add(row);
                added++;
            }
            else if (changed)
            {
                filled++;
            }
        }

        await db.SaveChangesAsync(ct);
        return (added, filled);
    }

    private static int? TotalSleep(ExportSleep? phases) =>
        phases is null || (phases.LightSeconds is null && phases.DeepSeconds is null && phases.RemSeconds is null)
            ? null
            : (phases.LightSeconds ?? 0) + (phases.DeepSeconds ?? 0) + (phases.RemSeconds ?? 0);

    private static bool Fill<T>(T? current, T? incoming, Action<T> set) where T : struct
    {
        if (current is not null || incoming is null) return false;
        set(incoming.Value);
        return true;
    }
}

internal static class DictionaryExtensions
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key) where TKey : notnull where TValue : new()
    {
        if (!dictionary.TryGetValue(key, out var value)) dictionary[key] = value = new TValue();
        return value;
    }
}
