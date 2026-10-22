using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlShortener.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueShortCodeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ShortUrls_ShortCode",
                table: "ShortUrls",
                column: "ShortCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShortUrls_ShortCode",
                table: "ShortUrls");
        }
    }
}
