using System;
using DTMS.Api.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Api.Infrastructure.Outbox.Migrations
{
    /// <summary>
    /// Phase S.5 (B2) — per-callback route override + order/trip linkage on
    /// <c>outbox.OutboxMessages</c>.
    ///
    /// Adds:
    ///   • <c>CallbackPath</c> / <c>CallbackMethod</c> — let a formatter route a
    ///     callback to a non-default endpoint (e.g. OMS's <c>/api/shipments</c>)
    ///     instead of the dispatcher's default <c>POST /events</c>. NULL = default.
    ///   • <c>RelatedOrderId</c> / <c>RelatedTripId</c> — so a dispatch-outcome
    ///     consumer can write the per-order OMS-notification audit the UI reads.
    ///
    /// All nullable, no backfill — existing rows and non-callback outbox use are
    /// unaffected (dispatcher falls back to POST /events when NULL).
    ///
    /// REVERSIBLE: Yes — Down() drops all four columns.
    /// </summary>
    [DbContext(typeof(OutboxDbContext))]
    [Migration("20260708150000_AddOutboxCallbackRoute")]
    public partial class AddOutboxCallbackRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CallbackPath",
                schema: "outbox",
                table: "OutboxMessages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CallbackMethod",
                schema: "outbox",
                table: "OutboxMessages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedOrderId",
                schema: "outbox",
                table: "OutboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedTripId",
                schema: "outbox",
                table: "OutboxMessages",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RelatedTripId", schema: "outbox", table: "OutboxMessages");
            migrationBuilder.DropColumn(
                name: "RelatedOrderId", schema: "outbox", table: "OutboxMessages");
            migrationBuilder.DropColumn(
                name: "CallbackMethod", schema: "outbox", table: "OutboxMessages");
            migrationBuilder.DropColumn(
                name: "CallbackPath", schema: "outbox", table: "OutboxMessages");
        }
    }
}
