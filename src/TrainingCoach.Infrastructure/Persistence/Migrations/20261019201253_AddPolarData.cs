using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainingCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPolarData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HrSampleIntervalSeconds",
                table: "Activities",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HrSamples",
                table: "Activities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PolarCardioLoad",
                table: "Activities",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PolarTrainingLoad",
                table: "Activities",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecoveryDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Hrv = table.Column<double>(type: "REAL", nullable: true),
                    RestingHeartRate = table.Column<double>(type: "REAL", nullable: true),
                    BreathingRate = table.Column<double>(type: "REAL", nullable: true),
                    AnsCharge = table.Column<double>(type: "REAL", nullable: true),
                    AnsChargeStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    NightlyRechargeStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    SleepScore = table.Column<double>(type: "REAL", nullable: true),
                    SleepSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    DeepSleepSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    RemSleepSeconds = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryDays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_UserId_PolarId",
                table: "Activities",
                columns: new[] { "UserId", "PolarId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryDays_UserId_Date",
                table: "RecoveryDays",
                columns: new[] { "UserId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecoveryDays");

            migrationBuilder.DropIndex(
                name: "IX_Activities_UserId_PolarId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "HrSampleIntervalSeconds",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "HrSamples",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "PolarCardioLoad",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "PolarTrainingLoad",
                table: "Activities");
        }
    }
}
