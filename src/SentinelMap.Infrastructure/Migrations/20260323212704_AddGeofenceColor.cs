using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SentinelMap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGeofenceColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "geofences",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "color",
                table: "geofences");
        }
    }
}
