using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    [Migration("20260418120000_AddTmdbMetadataFields")]
    public partial class AddTmdbMetadataFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackdropUrl",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstAirDate",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Genres",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImdbId",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataUpdatedAt",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalLanguage",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Overview",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "Popularity",
                table: "Series",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "TmdbBackdropPath",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TmdbPosterPath",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "VoteAverage",
                table: "Series",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "BackdropUrl",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Genres",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImdbId",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataUpdatedAt",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalLanguage",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Overview",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Popularity",
                table: "Movies",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseDate",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TmdbBackdropPath",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TmdbPosterPath",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VoteAverage",
                table: "Movies",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateIndex(
                name: "IX_Series_MetadataUpdatedAt",
                table: "Series",
                column: "MetadataUpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Series_TmdbId",
                table: "Series",
                column: "TmdbId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_MetadataUpdatedAt",
                table: "Movies",
                column: "MetadataUpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_TmdbId",
                table: "Movies",
                column: "TmdbId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Series_MetadataUpdatedAt", table: "Series");
            migrationBuilder.DropIndex(name: "IX_Series_TmdbId", table: "Series");
            migrationBuilder.DropIndex(name: "IX_Movies_MetadataUpdatedAt", table: "Movies");
            migrationBuilder.DropIndex(name: "IX_Movies_TmdbId", table: "Movies");

            migrationBuilder.DropColumn(name: "BackdropUrl", table: "Series");
            migrationBuilder.DropColumn(name: "FirstAirDate", table: "Series");
            migrationBuilder.DropColumn(name: "Genres", table: "Series");
            migrationBuilder.DropColumn(name: "ImdbId", table: "Series");
            migrationBuilder.DropColumn(name: "MetadataUpdatedAt", table: "Series");
            migrationBuilder.DropColumn(name: "OriginalLanguage", table: "Series");
            migrationBuilder.DropColumn(name: "Overview", table: "Series");
            migrationBuilder.DropColumn(name: "Popularity", table: "Series");
            migrationBuilder.DropColumn(name: "TmdbBackdropPath", table: "Series");
            migrationBuilder.DropColumn(name: "TmdbPosterPath", table: "Series");
            migrationBuilder.DropColumn(name: "VoteAverage", table: "Series");

            migrationBuilder.DropColumn(name: "BackdropUrl", table: "Movies");
            migrationBuilder.DropColumn(name: "Genres", table: "Movies");
            migrationBuilder.DropColumn(name: "ImdbId", table: "Movies");
            migrationBuilder.DropColumn(name: "MetadataUpdatedAt", table: "Movies");
            migrationBuilder.DropColumn(name: "OriginalLanguage", table: "Movies");
            migrationBuilder.DropColumn(name: "Overview", table: "Movies");
            migrationBuilder.DropColumn(name: "Popularity", table: "Movies");
            migrationBuilder.DropColumn(name: "ReleaseDate", table: "Movies");
            migrationBuilder.DropColumn(name: "TmdbBackdropPath", table: "Movies");
            migrationBuilder.DropColumn(name: "TmdbPosterPath", table: "Movies");
            migrationBuilder.DropColumn(name: "VoteAverage", table: "Movies");
        }
    }
}
