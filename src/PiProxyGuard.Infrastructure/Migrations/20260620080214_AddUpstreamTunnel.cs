using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiProxyGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUpstreamTunnel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TunneledDomains",
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
                    table.PrimaryKey("PK_TunneledDomains", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TunneledDomains_Domain",
                table: "TunneledDomains",
                column: "Domain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TunneledDomains");
        }
    }
}
