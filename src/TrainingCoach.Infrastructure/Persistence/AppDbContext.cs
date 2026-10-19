using Microsoft.EntityFrameworkCore;

namespace TrainingCoach.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<PlannedSessionEntity> PlannedSessions => Set<PlannedSessionEntity>();
    public DbSet<WorkoutTemplateEntity> Workouts => Set<WorkoutTemplateEntity>();
    public DbSet<ActivityEntity> Activities => Set<ActivityEntity>();
    public DbSet<ProviderConnectionEntity> Connections => Set<ProviderConnectionEntity>();
    public DbSet<RecoveryDayEntity> RecoveryDays => Set<RecoveryDayEntity>();
    public DbSet<AthleteSettingsEntity> AthleteSettings => Set<AthleteSettingsEntity>();
    public DbSet<PainReportEntity> PainReports => Set<PainReportEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlannedSessionEntity>(e =>
        {
            e.HasIndex(s => new { s.UserId, s.Date });
            e.Property(s => s.Sport).HasConversion<string>();
            e.Property(s => s.Status).HasConversion<string>();
        });

        modelBuilder.Entity<WorkoutTemplateEntity>(e =>
        {
            e.HasIndex(w => new { w.UserId, w.Name }).IsUnique();
            e.HasMany(w => w.Phases).WithOne().HasForeignKey(p => p.WorkoutTemplateEntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkoutPhaseEntity>(e => e.Property(p => p.Kind).HasConversion<string>());

        modelBuilder.Entity<ActivityEntity>(e =>
        {
            e.HasIndex(a => new { a.UserId, a.StravaId }).IsUnique();
            e.HasIndex(a => new { a.UserId, a.PolarId }).IsUnique();
            e.HasIndex(a => new { a.UserId, a.StartLocal });
            e.Property(a => a.Sport).HasConversion<string>();
        });

        modelBuilder.Entity<AthleteSettingsEntity>(e => e.HasIndex(s => s.UserId).IsUnique());
        modelBuilder.Entity<PainReportEntity>(e => e.HasIndex(p => new { p.UserId, p.Date }));

        modelBuilder.Entity<RecoveryDayEntity>(e => e.HasIndex(r => new { r.UserId, r.Date }).IsUnique());

        modelBuilder.Entity<ProviderConnectionEntity>(e => e.HasIndex(c => new { c.UserId, c.Provider }).IsUnique());
    }
}
