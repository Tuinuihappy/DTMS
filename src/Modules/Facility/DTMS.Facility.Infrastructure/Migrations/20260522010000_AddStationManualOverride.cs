using System;
using DTMS.Facility.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Facility.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FacilityDbContext))]
    [Migration("20260522010000_AddStationManualOverride")]
    public partial class AddStationManualOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ManualOverrideOffline",
                schema: "facility",
                table: "Stations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ManualOverrideReason",
                schema: "facility",
                table: "Stations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualOverrideBy",
                schema: "facility",
                table: "Stations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManualOverrideAt",
                schema: "facility",
                table: "Stations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManualOverrideExpiresAt",
                schema: "facility",
                table: "Stations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stations_ManualOverrideExpiresAt",
                schema: "facility",
                table: "Stations",
                column: "ManualOverrideExpiresAt",
                filter: "\"ManualOverrideOffline\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stations_ManualOverrideExpiresAt",
                schema: "facility",
                table: "Stations");

            migrationBuilder.DropColumn(name: "ManualOverrideExpiresAt", schema: "facility", table: "Stations");
            migrationBuilder.DropColumn(name: "ManualOverrideAt", schema: "facility", table: "Stations");
            migrationBuilder.DropColumn(name: "ManualOverrideBy", schema: "facility", table: "Stations");
            migrationBuilder.DropColumn(name: "ManualOverrideReason", schema: "facility", table: "Stations");
            migrationBuilder.DropColumn(name: "ManualOverrideOffline", schema: "facility", table: "Stations");
        }
    }
}
