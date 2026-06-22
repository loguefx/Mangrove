using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mangrove.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2Metadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MetadataLocked",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetadataLocked",
                table: "Series");
        }
    }
}
