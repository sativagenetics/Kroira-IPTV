using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateV2DataIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Channels_ChannelCategoryId"";");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""SourceProtectedCredentialSecrets"" (
                    ""Id""               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SourceProfileId""  INTEGER NOT NULL,
                    ""Name""             TEXT    NOT NULL DEFAULT '',
                    ""ProtectedValue""   TEXT    NOT NULL DEFAULT '',
                    ""ProtectionScheme"" TEXT    NOT NULL DEFAULT '',
                    ""UpdatedAtUtc""     TEXT    NOT NULL DEFAULT '',
                    CONSTRAINT ""FK_SourceProtectedCredentialSecrets_SourceProfiles_SourceProfileId""
                        FOREIGN KEY (""SourceProfileId"")
                        REFERENCES ""SourceProfiles"" (""Id"")
                        ON DELETE CASCADE
                );");

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Series_SourceProfileId"" ON ""Series"" (""SourceProfileId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Series_SourceProfileId_CanonicalTitleKey"" ON ""Series"" (""SourceProfileId"", ""CanonicalTitleKey"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Series_SourceProfileId_ContentKind"" ON ""Series"" (""SourceProfileId"", ""ContentKind"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Series_SourceProfileId_ExternalId"" ON ""Series"" (""SourceProfileId"", ""ExternalId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Movies_SourceProfileId"" ON ""Movies"" (""SourceProfileId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Movies_SourceProfileId_CanonicalTitleKey"" ON ""Movies"" (""SourceProfileId"", ""CanonicalTitleKey"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Movies_SourceProfileId_ContentKind"" ON ""Movies"" (""SourceProfileId"", ""ContentKind"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Movies_SourceProfileId_ExternalId"" ON ""Movies"" (""SourceProfileId"", ""ExternalId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_EpgPrograms_ChannelId_StartTimeUtc_EndTimeUtc"" ON ""EpgPrograms"" (""ChannelId"", ""StartTimeUtc"", ""EndTimeUtc"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_EpgPrograms_StartTimeUtc_EndTimeUtc"" ON ""EpgPrograms"" (""StartTimeUtc"", ""EndTimeUtc"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_EpgMappingDecisions_SourceProfileId_ChannelId"" ON ""EpgMappingDecisions"" (""SourceProfileId"", ""ChannelId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_EpgMappingDecisions_SourceProfileId_ChannelIdentityKey"" ON ""EpgMappingDecisions"" (""SourceProfileId"", ""ChannelIdentityKey"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Channels_ChannelCategoryId_ProviderEpgChannelId"" ON ""Channels"" (""ChannelCategoryId"", ""ProviderEpgChannelId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Channels_NormalizedName"" ON ""Channels"" (""NormalizedName"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Channels_ProviderEpgChannelId"" ON ""Channels"" (""ProviderEpgChannelId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ChannelCategories_SourceProfileId"" ON ""ChannelCategories"" (""SourceProfileId"");");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SourceProtectedCredentialSecrets_SourceProfileId_Name"" ON ""SourceProtectedCredentialSecrets"" (""SourceProfileId"", ""Name"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceProtectedCredentialSecrets");

            migrationBuilder.DropIndex(
                name: "IX_Series_SourceProfileId",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Series_SourceProfileId_CanonicalTitleKey",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Series_SourceProfileId_ContentKind",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Series_SourceProfileId_ExternalId",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Movies_SourceProfileId",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_SourceProfileId_CanonicalTitleKey",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_SourceProfileId_ContentKind",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_SourceProfileId_ExternalId",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_EpgPrograms_ChannelId_StartTimeUtc_EndTimeUtc",
                table: "EpgPrograms");

            migrationBuilder.DropIndex(
                name: "IX_EpgPrograms_StartTimeUtc_EndTimeUtc",
                table: "EpgPrograms");

            migrationBuilder.DropIndex(
                name: "IX_EpgMappingDecisions_SourceProfileId_ChannelId",
                table: "EpgMappingDecisions");

            migrationBuilder.DropIndex(
                name: "IX_EpgMappingDecisions_SourceProfileId_ChannelIdentityKey",
                table: "EpgMappingDecisions");

            migrationBuilder.DropIndex(
                name: "IX_Channels_ChannelCategoryId_ProviderEpgChannelId",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Channels_NormalizedName",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Channels_ProviderEpgChannelId",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_ChannelCategories_SourceProfileId",
                table: "ChannelCategories");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_ChannelCategoryId",
                table: "Channels",
                column: "ChannelCategoryId");
        }
    }
}
