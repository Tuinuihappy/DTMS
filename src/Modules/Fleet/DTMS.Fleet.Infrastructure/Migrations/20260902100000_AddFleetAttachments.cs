using System;
using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Fleet.Infrastructure.Migrations
{
    /// <summary>
    /// IMAGE ATTACHMENTS — images belonging to a carrier, a carrier type, or one
    /// maintenance episode (ADR-019).
    ///
    /// <para><b>Three nullable owner columns, not (OwnerType, OwnerId).</b> A
    /// polymorphic owner cannot carry a foreign key, so nothing in the database
    /// could stop an OwnerId pointing at a row that no longer exists — and
    /// carriers are deleted for real, so that would happen. Naming each owner
    /// buys real FKs and real cascade for the price of one column per owner
    /// kind. CK_Attachments_ExactlyOneOwner keeps the arc exclusive using
    /// num_nonnulls(), which is a standard PostgreSQL function.</para>
    ///
    /// <para><b>Cascade on every owner.</b> An image is not history the way a
    /// maintenance episode is, so it must never block deleting the thing it
    /// describes. Note what cascade does NOT do: it removes the row, never the
    /// bytes in object storage, and it bypasses EF's change tracker entirely so
    /// no domain event fires. The delete handlers therefore read the object keys
    /// out before the row goes and hand them to the outbox.</para>
    ///
    /// <para><b>The owner indexes are filtered</b> so each holds only rows of
    /// that owner kind. Every query is "images for one owner"; none is ever
    /// "rows where this column is null", so the NULLs would be dead weight.</para>
    ///
    /// REVERSIBLE: Down() drops the table. Note that the objects themselves
    ///   survive in MinIO — a rollback after real use leaves files nothing
    ///   references, and they must be cleared from the bucket by hand.
    /// </summary>
    [DbContext(typeof(FleetDbContext))]
    [Migration("20260902100000_AddFleetAttachments")]
    public partial class AddFleetAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attachments",
                schema: "fleet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ThumbnailKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Bucket = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CarrierTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaintenanceLogId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.CheckConstraint(
                        "CK_Attachments_ExactlyOneOwner",
                        "num_nonnulls(\"CarrierId\", \"CarrierTypeId\", \"MaintenanceLogId\") = 1");
                    table.ForeignKey(
                        name: "FK_Attachments_Carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalSchema: "fleet",
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Attachments_CarrierTypes_CarrierTypeId",
                        column: x => x.CarrierTypeId,
                        principalSchema: "fleet",
                        principalTable: "CarrierTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Attachments_CarrierMaintenanceLog_MaintenanceLogId",
                        column: x => x.MaintenanceLogId,
                        principalSchema: "fleet",
                        principalTable: "CarrierMaintenanceLog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // One row per stored object. Also what turns a replayed confirm into
            // a clean 409 rather than two rows pointing at the same bytes.
            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ObjectKey",
                schema: "fleet",
                table: "Attachments",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_CarrierId",
                schema: "fleet",
                table: "Attachments",
                column: "CarrierId",
                filter: "\"CarrierId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_CarrierTypeId",
                schema: "fleet",
                table: "Attachments",
                column: "CarrierTypeId",
                filter: "\"CarrierTypeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_MaintenanceLogId",
                schema: "fleet",
                table: "Attachments",
                column: "MaintenanceLogId",
                filter: "\"MaintenanceLogId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Attachments", schema: "fleet");
        }
    }
}
