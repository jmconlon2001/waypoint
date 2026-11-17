using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Waypoint.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    workday_start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    workday_end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    timer_warning_minutes = table.Column<int>(type: "integer", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    project_id = table.Column<int>(type: "integer", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    planned_date = table.Column<DateOnly>(type: "date", nullable: true),
                    pin_slot = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tasks_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "time_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    task_id = table.Column<int>(type: "integer", nullable: true),
                    project_id = table.Column<int>(type: "integer", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_time_entries_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_time_entries_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "interruptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    person_id = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    could_wait = table.Column<bool>(type: "boolean", nullable: true),
                    paused_time_entry_id = table.Column<int>(type: "integer", nullable: true),
                    converted_task_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interruptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_interruptions_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_interruptions_tasks_converted_task_id",
                        column: x => x.converted_task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_interruptions_time_entries_paused_time_entry_id",
                        column: x => x.paused_time_entry_id,
                        principalTable: "time_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interruptions_converted_task_id",
                table: "interruptions",
                column: "converted_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_interruptions_paused_time_entry_id",
                table: "interruptions",
                column: "paused_time_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_interruptions_person_id",
                table: "interruptions",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_interruptions_started_at",
                table: "interruptions",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_people_name",
                table: "people",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_name",
                table: "projects",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_planned_date",
                table: "tasks",
                column: "planned_date");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id",
                table: "tasks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_status",
                table: "tasks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_ended_at",
                table: "time_entries",
                column: "ended_at");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_project_id",
                table: "time_entries",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_started_at",
                table: "time_entries",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_task_id",
                table: "time_entries",
                column: "task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "interruptions");
            migrationBuilder.DropTable(name: "settings");
            migrationBuilder.DropTable(name: "people");
            migrationBuilder.DropTable(name: "time_entries");
            migrationBuilder.DropTable(name: "tasks");
            migrationBuilder.DropTable(name: "projects");
        }
    }
}
