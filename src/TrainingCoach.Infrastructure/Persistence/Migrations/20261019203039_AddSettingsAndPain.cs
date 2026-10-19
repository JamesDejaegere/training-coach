using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainingCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsAndPain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AthleteSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    HrMax = table.Column<int>(type: "INTEGER", nullable: false),
                    RestingHrOverride = table.Column<int>(type: "INTEGER", nullable: true),
                    BodyWeightKg = table.Column<double>(type: "REAL", nullable: true),
                    HomeLatitude = table.Column<double>(type: "REAL", nullable: true),
                    HomeLongitude = table.Column<double>(type: "REAL", nullable: true),
                    WorkLatitude = table.Column<double>(type: "REAL", nullable: true),
                    WorkLongitude = table.Column<double>(type: "REAL", nullable: true),
                    CommuteRadiusMeters = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AthleteSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PainReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivityId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PainReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AthleteSettings_UserId",
                table: "AthleteSettings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PainReports_UserId_Date",
                table: "PainReports",
                columns: new[] { "UserId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AthleteSettings");

            migrationBuilder.DropTable(
                name: "PainReports");
        }
    }
}
