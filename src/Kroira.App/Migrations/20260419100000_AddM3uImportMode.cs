using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    [Migration("20260419100000_AddM3uImportMode")]
    public partial class AddM3uImportMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default = 1 (LiveAndMovies) — safe conservative mode for all
            // existing rows. Xtream sources ignore this column entirely.
            migrationBuilder.AddColumn<int>(
                name: "M3uImportMode",
                table: "SourceCredentials",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "M3uImportMode",
                table: "SourceCredentials");
        }
    }
}
