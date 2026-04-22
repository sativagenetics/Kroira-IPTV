using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    [Migration("20260423050000_AddSourceCompanionRelay")]
    public partial class AddSourceCompanionRelay : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompanionMode",
                table: "SourceCredentials",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanionScope",
                table: "SourceCredentials",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CompanionUrl",
                table: "SourceCredentials",
                type: "TEXT",
                maxLength: 600,
                nullable: false,
                defaultValue: string.Empty);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanionMode",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "CompanionScope",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "CompanionUrl",
                table: "SourceCredentials");
        }
    }
}
