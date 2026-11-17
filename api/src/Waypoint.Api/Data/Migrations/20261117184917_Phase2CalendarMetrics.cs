using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Waypoint.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2CalendarMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "calendar_ics_url",
                table: "settings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "calendar_internal_domains",
                table: "settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "calendar_last_sync_at",
                table: "settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "calendar_last_sync_error",
                table: "settings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "meeting_max_minutes",
                table: "settings",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "review_reminder_time",
                table: "settings",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "calendar_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_all_day = table.Column<bool>(type: "boolean", nullable: false),
                    attendee_count = table.Column<int>(type: "integer", nullable: false),
                    is_recurring = table.Column<bool>(type: "boolean", nullable: false),
                    is_free = table.Column<bool>(type: "boolean", nullable: false),
                    meeting_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    project_id = table.Column<int>(type: "integer", nullable: true),
                    synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_events_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "meeting_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title_pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    project_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_rules_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_plans",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    task_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_plans", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_plans_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_external_id",
                table: "calendar_events",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_project_id",
                table: "calendar_events",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_started_at",
                table: "calendar_events",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_rules_project_id",
                table: "meeting_rules",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_plans_date",
                table: "task_plans",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_task_plans_task_id_date",
                table: "task_plans",
                columns: new[] { "task_id", "date" },
                unique: true);

            // Phase 1 tasks already planned for a day become that day's plan history.
            migrationBuilder.Sql("""
                INSERT INTO task_plans (task_id, date)
                SELECT id, planned_date FROM tasks WHERE planned_date IS NOT NULL
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_events");

            migrationBuilder.DropTable(
                name: "meeting_rules");

            migrationBuilder.DropTable(
                name: "task_plans");

            migrationBuilder.DropColumn(
                name: "calendar_ics_url",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "calendar_internal_domains",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "calendar_last_sync_at",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "calendar_last_sync_error",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "meeting_max_minutes",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "review_reminder_time",
                table: "settings");
        }
    }
}
