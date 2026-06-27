using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Api.Infrastructure.Outbox.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRetryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAtUtc",
                schema: "outbox",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                schema: "outbox",
                table: "OutboxMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_NextRetryAtUtc",
                schema: "outbox",
                table: "OutboxMessages",
                column: "NextRetryAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_NextRetryAtUtc",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextRetryAtUtc",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                schema: "outbox",
                table: "OutboxMessages");
        }
    }
}
