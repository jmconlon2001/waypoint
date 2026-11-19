using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Waypoint.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FutureEntryLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "future_entry_max_minutes",
                table: "settings",
                type: "integer",
                nullable: false,
                defaultValue: 240);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "future_entry_max_minutes",
                table: "settings");
        }
    }
}
