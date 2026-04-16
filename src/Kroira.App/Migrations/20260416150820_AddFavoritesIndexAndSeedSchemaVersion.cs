using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoritesIndexAndSeedSchemaVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SchemaVersions",
                columns: new[] { "Id", "AppliedAt", "IsValidated", "VersionNumber" },
                values: new object[] { 1, new DateTime(2026, 4, 16, 0, 0, 0, 0, DateTimeKind.Utc), true, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_ContentType_ContentId",
                table: "Favorites",
                columns: new[] { "ContentType", "ContentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Favorites_ContentType_ContentId",
                table: "Favorites");

            migrationBuilder.DeleteData(
                table: "SchemaVersions",
                keyColumn: "Id",
                keyValue: 1);
        }
    }
}
