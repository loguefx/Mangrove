using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mangrove.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class LibraryPaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryPaths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryPaths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryPaths_Credentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "Credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LibraryPaths_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryPaths_CredentialId",
                table: "LibraryPaths",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryPaths_LibraryId",
                table: "LibraryPaths",
                column: "LibraryId");

            // Seed one path per existing library from its current RootPath so libraries created before
            // multi-path support keep working without a re-scan.
            migrationBuilder.Sql(
                "INSERT INTO LibraryPaths (LibraryId, Path, CredentialId) " +
                "SELECT Id, RootPath, CredentialId FROM Libraries " +
                "WHERE RootPath IS NOT NULL AND RootPath <> '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryPaths");
        }
    }
}
