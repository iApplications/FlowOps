using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOps.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateStudioPortalAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountSurface",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Portal");

            // Preserve known Studio signups and existing backoffice staff. Other legacy accounts remain Portal accounts.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" u SET "AccountSurface" = 'Studio'
                WHERE EXISTS (SELECT 1 FROM "AspNetUserClaims" c WHERE c."UserId" = u."Id"
                    AND c."ClaimType" = 'flowops:studio-signup' AND c."ClaimValue" = 'true')
                OR EXISTS (SELECT 1 FROM "OrganizationMembers" m WHERE m."UserId" = u."Id"
                    AND m."Role" IN ('Owner', 'Admin'));
                UPDATE "BrowserSessions" SET "RevokedAt" = CURRENT_TIMESTAMP WHERE "RevokedAt" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_AccountSurface_NormalizedEmail",
                table: "AspNetUsers",
                columns: new[] { "AccountSurface", "NormalizedEmail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_AccountSurface_NormalizedEmail",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AccountSurface",
                table: "AspNetUsers");
        }
    }
}
