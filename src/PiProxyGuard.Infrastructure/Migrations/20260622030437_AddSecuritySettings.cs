using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiProxyGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecuritySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecuritySettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GeoIpCountryDatabasePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    GeoIpAsnDatabasePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    UrlhausEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    VirusTotalApiKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AbuseIpDbApiKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AbuseIpDbScoreThreshold = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyDigestEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DailyReportHour = table.Column<int>(type: "INTEGER", nullable: false),
                    DigestWindowHours = table.Column<int>(type: "INTEGER", nullable: false),
                    DigestTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DigestSeverity = table.Column<int>(type: "INTEGER", nullable: false),
                    ScanEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScanIntervalHours = table.Column<int>(type: "INTEGER", nullable: false),
                    ScanLookbackHours = table.Column<int>(type: "INTEGER", nullable: false),
                    ScanTopDomains = table.Column<int>(type: "INTEGER", nullable: false),
                    ScanRequestDelayMs = table.Column<int>(type: "INTEGER", nullable: false),
                    ScanAutoBlock = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScanAutoBlockTtlHours = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecuritySettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecuritySettings");
        }
    }
}
