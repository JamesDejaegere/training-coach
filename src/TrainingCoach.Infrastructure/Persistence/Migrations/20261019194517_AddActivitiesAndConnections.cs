using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainingCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivitiesAndConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    StravaId = table.Column<long>(type: "INTEGER", nullable: true),
                    PolarId = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Sport = table.Column<string>(type: "TEXT", nullable: false),
                    StravaSportType = table.Column<string>(type: "TEXT", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartLocal = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MovingSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    ElapsedSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    DistanceMeters = table.Column<double>(type: "REAL", nullable: false),
                    ElevationGainMeters = table.Column<double>(type: "REAL", nullable: false),
                    AverageSpeedMps = table.Column<double>(type: "REAL", nullable: false),
                    MaxSpeedMps = table.Column<double>(type: "REAL", nullable: false),
                    AverageHeartRate = table.Column<double>(type: "REAL", nullable: true),
                    MaxHeartRate = table.Column<double>(type: "REAL", nullable: true),
                    AverageCadence = table.Column<double>(type: "REAL", nullable: true),
                    AverageWatts = table.Column<double>(type: "REAL", nullable: true),
                    Kilojoules = table.Column<double>(type: "REAL", nullable: true),
                    RelativeEffort = table.Column<double>(type: "REAL", nullable: true),
                    StartLatitude = table.Column<double>(type: "REAL", nullable: true),
                    StartLongitude = table.Column<double>(type: "REAL", nullable: true),
                    EndLatitude = table.Column<double>(type: "REAL", nullable: true),
                    EndLongitude = table.Column<double>(type: "REAL", nullable: true),
                    StravaCommuteFlag = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsTrainer = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCommute = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rpe = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Connections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalAthleteId = table.Column<long>(type: "INTEGER", nullable: true),
                    AccessTokenProtected = table.Column<string>(type: "TEXT", nullable: false),
                    RefreshTokenProtected = table.Column<string>(type: "TEXT", nullable: false),
                    AccessTokenExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConnectedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SyncCursorUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_UserId_StartLocal",
                table: "Activities",
                columns: new[] { "UserId", "StartLocal" });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_UserId_StravaId",
                table: "Activities",
                columns: new[] { "UserId", "StravaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Connections_UserId_Provider",
                table: "Connections",
                columns: new[] { "UserId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "Connections");
        }
    }
}
