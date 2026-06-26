using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Fleet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRetryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAtUtc",
                schema: "fleet",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                schema: "fleet",
                table: "OutboxMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_NextRetryAtUtc",
                schema: "fleet",
                table: "OutboxMessages",
                column: "NextRetryAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_NextRetryAtUtc",
                schema: "fleet",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextRetryAtUtc",
                schema: "fleet",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                schema: "fleet",
                table: "OutboxMessages");
        }
    }
}
