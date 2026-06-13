using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiProxyGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowListInit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "BlockedDomains",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AllowedDomains",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowedDomains", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedDomains_Source_ExpiresAtUtc",
                table: "BlockedDomains",
                columns: new[] { "Source", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AllowedDomains_Domain",
                table: "AllowedDomains",
                column: "Domain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllowedDomains");

            migrationBuilder.DropIndex(
                name: "IX_BlockedDomains_Source_ExpiresAtUtc",
                table: "BlockedDomains");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "BlockedDomains");
        }
    }
}
