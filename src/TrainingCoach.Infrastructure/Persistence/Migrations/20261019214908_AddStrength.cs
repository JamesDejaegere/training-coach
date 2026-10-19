using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainingCoach.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStrength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomExercises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    NameNl = table.Column<string>(type: "TEXT", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryMuscles = table.Column<string>(type: "TEXT", nullable: false),
                    SecondaryMuscles = table.Column<string>(type: "TEXT", nullable: false),
                    Equipment = table.Column<string>(type: "TEXT", nullable: false),
                    Unilateral = table.Column<bool>(type: "INTEGER", nullable: false),
                    Timed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Sets = table.Column<int>(type: "INTEGER", nullable: false),
                    Target = table.Column<int>(type: "INTEGER", nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomExercises", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrengthSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Rpe = table.Column<int>(type: "INTEGER", nullable: true),
                    ActivityId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrengthSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrengthEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExerciseKey = table.Column<string>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrengthEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrengthEntries_StrengthSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StrengthSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StrengthSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Reps = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Side = table.Column<string>(type: "TEXT", nullable: false),
                    AddedLoadKg = table.Column<double>(type: "REAL", nullable: true),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrengthSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrengthSets_StrengthEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "StrengthEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StrengthEntries_SessionId",
                table: "StrengthEntries",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StrengthSessions_UserId_StartedUtc",
                table: "StrengthSessions",
                columns: new[] { "UserId", "StartedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StrengthSets_EntryId",
                table: "StrengthSets",
                column: "EntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomExercises");

            migrationBuilder.DropTable(
                name: "StrengthSets");

            migrationBuilder.DropTable(
                name: "StrengthEntries");

            migrationBuilder.DropTable(
                name: "StrengthSessions");
        }
    }
}
