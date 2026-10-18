using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainingCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlannedSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    OriginalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Sport = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    WorkoutName = table.Column<string>(type: "TEXT", nullable: true),
                    PlannedDistanceKm = table.Column<double>(type: "REAL", nullable: true),
                    Week = table.Column<int>(type: "INTEGER", nullable: false),
                    Phase = table.Column<string>(type: "TEXT", nullable: false),
                    IsCommute = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TotalKm = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutPhaseEntity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkoutTemplateEntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    DistanceKm = table.Column<double>(type: "REAL", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    Repeats = table.Column<int>(type: "INTEGER", nullable: true),
                    RepeatFromPhase = table.Column<int>(type: "INTEGER", nullable: true),
                    RepeatToPhase = table.Column<int>(type: "INTEGER", nullable: true),
                    Target = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutPhaseEntity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutPhaseEntity_Workouts_WorkoutTemplateEntityId",
                        column: x => x.WorkoutTemplateEntityId,
                        principalTable: "Workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlannedSessions_UserId_Date",
                table: "PlannedSessions",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutPhaseEntity_WorkoutTemplateEntityId",
                table: "WorkoutPhaseEntity",
                column: "WorkoutTemplateEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Workouts_UserId_Name",
                table: "Workouts",
                columns: new[] { "UserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannedSessions");

            migrationBuilder.DropTable(
                name: "WorkoutPhaseEntity");

            migrationBuilder.DropTable(
                name: "Workouts");
        }
    }
}
