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
                name: "calendar_source", table: "settings", type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "None");
            migrationBuilder.AddColumn<int>(
                name: "max_meeting_minutes", table: "settings", type: "integer", nullable: false, defaultValue: 120);
            migrationBuilder.AddColumn<string>(
                name: "internal_domains", table: "settings", type: "character varying(1000)", maxLength: 1000, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "google_refresh_token_protected", table: "settings", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "google_account", table: "settings", type: "character varying(320)", maxLength: 320, nullable: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "calendar_last_sync_at", table: "settings", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "calendar_last_sync_error", table: "settings", type: "text", nullable: true);
            migrationBuilder.AddColumn<bool>(
                name: "review_reminder_enabled", table: "settings", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<TimeOnly>(
                name: "review_reminder_time", table: "settings", type: "time without time zone", nullable: false, defaultValue: new TimeOnly(17, 15, 0));

            migrationBuilder.CreateTable(
                name: "calendar_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    external_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    all_day = table.Column<bool>(type: "boolean", nullable: false),
                    attendee_count = table.Column<int>(type: "integer", nullable: false),
                    recurring = table.Column<bool>(type: "boolean", nullable: false),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attendee_domains = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    declined = table.Column<bool>(type: "boolean", nullable: false),
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
                name: "daily_aggregates",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    task_minutes = table.Column<double>(type: "double precision", nullable: false),
                    reactive_minutes = table.Column<double>(type: "double precision", nullable: false),
                    untracked_minutes = table.Column<double>(type: "double precision", nullable: false),
                    meeting_minutes = table.Column<double>(type: "double precision", nullable: false),
                    planned_focus_minutes = table.Column<double>(type: "double precision", nullable: false),
                    unlogged_minutes = table.Column<double>(type: "double precision", nullable: false),
                    interruption_count = table.Column<int>(type: "integer", nullable: false),
                    interruption_minutes = table.Column<double>(type: "double precision", nullable: false),
                    could_wait_count = table.Column<int>(type: "integer", nullable: false),
                    task_switches = table.Column<int>(type: "integer", nullable: false),
                    focus_blocks = table.Column<int>(type: "integer", nullable: false),
                    longest_focus_block_minutes = table.Column<double>(type: "double precision", nullable: false),
                    planned_tasks = table.Column<int>(type: "integer", nullable: false),
                    planned_tasks_worked = table.Column<int>(type: "integer", nullable: false),
                    planned_task_minutes = table.Column<double>(type: "double precision", nullable: false),
                    unplanned_task_minutes = table.Column<double>(type: "double precision", nullable: false),
                    meeting_count = table.Column<int>(type: "integer", nullable: false),
                    computed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_aggregates", x => x.date);
                });

            migrationBuilder.CreateTable(
                name: "meeting_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pattern = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    project_id = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
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
                    task_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_plans", x => new { x.task_id, x.date });
                    table.ForeignKey(
                        name: "fk_task_plans_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_project_id",
                table: "calendar_events",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_source_external_id",
                table: "calendar_events",
                columns: new[] { "source", "external_id" },
                unique: true);

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

            // Backfill plan history from the current planned dates.
            migrationBuilder.Sql("INSERT INTO task_plans (task_id, date) SELECT id, planned_date FROM tasks WHERE planned_date IS NOT NULL ON CONFLICT DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "calendar_events");
            migrationBuilder.DropTable(name: "daily_aggregates");
            migrationBuilder.DropTable(name: "meeting_rules");
            migrationBuilder.DropTable(name: "task_plans");

            foreach (var column in new[]
            {
                "calendar_source", "max_meeting_minutes", "internal_domains", "google_refresh_token_protected", "google_account",
                "calendar_last_sync_at", "calendar_last_sync_error", "review_reminder_enabled", "review_reminder_time",
            })
            {
                migrationBuilder.DropColumn(name: column, table: "settings");
            }
        }
    }
}
