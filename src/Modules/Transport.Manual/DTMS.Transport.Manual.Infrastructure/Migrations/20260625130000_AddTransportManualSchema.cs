using System;
using DTMS.Transport.Manual.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Transport.Manual.Infrastructure.Migrations
{
    /// <summary>
    /// PHASE: 4.1 — Manual transport mode skeleton.
    /// PURPOSE: Create the transportmanual schema and the 5 tables the
    ///   Manual mode needs to function: Operators (root), OperatorCertifications,
    ///   OperatorPushSubscriptions, GeofenceOverrideRequests, ManualTripExtensions.
    ///   Phase 4.2 wires endpoints over these tables; Phase 4.4 promotes
    ///   ManualDispatchStrategy from stub to real implementation.
    /// DEPENDS ON: Nothing in transportmanual schema (first migration in module).
    ///   Cross-module timestamp 20260625130000 is unique against the shared
    ///   public.__EFMigrationsHistory table (latest peers: Dispatch 20260624150000,
    ///   DeliveryOrder 20260623130000).
    /// REVERSIBLE: Yes — Down() drops all 5 tables then the schema.
    ///   No data preservation needed since this is a greenfield schema —
    ///   if rolled back before any Operator rows exist, nothing is lost;
    ///   if rolled back later, the rows were skeleton-only (no Phase 4.4+
    ///   business value yet) so data loss is acceptable.
    /// </summary>
    [DbContext(typeof(TransportManualDbContext))]
    [Migration("20260625130000_AddTransportManualSchema")]
    public partial class AddTransportManualSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "transportmanual");

            // ── Operators (aggregate root) ─────────────────────────────
            migrationBuilder.CreateTable(
                name: "Operators",
                schema: "transportmanual",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PrimaryWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentTripId = table.Column<Guid>(type: "uuid", nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operators", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Operators_EmployeeCode",
                schema: "transportmanual",
                table: "Operators",
                column: "EmployeeCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operators_Status",
                schema: "transportmanual",
                table: "Operators",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Operators_CurrentTripId",
                schema: "transportmanual",
                table: "Operators",
                column: "CurrentTripId",
                filter: "\"CurrentTripId\" IS NOT NULL");

            // ── OperatorCertifications (child) ─────────────────────────
            migrationBuilder.CreateTable(
                name: "OperatorCertifications",
                schema: "transportmanual",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorCertifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorCertifications_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalSchema: "transportmanual",
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperatorCertifications_OperatorId_Type_IsActive",
                schema: "transportmanual",
                table: "OperatorCertifications",
                columns: new[] { "OperatorId", "Type", "IsActive" });

            // ── OperatorPushSubscriptions (child) ──────────────────────
            migrationBuilder.CreateTable(
                name: "OperatorPushSubscriptions",
                schema: "transportmanual",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AuthSecret = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeviceLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SubscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSucceededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorPushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorPushSubscriptions_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalSchema: "transportmanual",
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperatorPushSubscriptions_Endpoint",
                schema: "transportmanual",
                table: "OperatorPushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatorPushSubscriptions_OperatorId",
                schema: "transportmanual",
                table: "OperatorPushSubscriptions",
                column: "OperatorId");

            // ── GeofenceOverrideRequests (independent aggregate) ───────
            migrationBuilder.CreateTable(
                name: "GeofenceOverrideRequests",
                schema: "transportmanual",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedLatitude = table.Column<double>(type: "double precision", nullable: false),
                    ReportedLongitude = table.Column<double>(type: "double precision", nullable: false),
                    DistanceFromGeofenceM = table.Column<double>(type: "double precision", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PhotoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedByOperatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeofenceOverrideRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeofenceOverrideRequests_Status",
                schema: "transportmanual",
                table: "GeofenceOverrideRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_GeofenceOverrideRequests_TripId",
                schema: "transportmanual",
                table: "GeofenceOverrideRequests",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_GeofenceOverrideRequests_Status_ExpiresAt",
                schema: "transportmanual",
                table: "GeofenceOverrideRequests",
                columns: new[] { "Status", "ExpiresAt" },
                filter: "\"Status\" = 'Pending'");

            // ── ManualTripExtensions (1:0..1 with dispatch.Trips) ──────
            migrationBuilder.CreateTable(
                name: "ManualTripExtensions",
                schema: "transportmanual",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickedUpAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DroppedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickupGeofenceOverrideId = table.Column<Guid>(type: "uuid", nullable: true),
                    DropGeofenceOverrideId = table.Column<Guid>(type: "uuid", nullable: true),
                    PickupPodKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DropPodKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AckDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickupDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DropDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualTripExtensions", x => x.TripId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManualTripExtensions_OperatorId",
                schema: "transportmanual",
                table: "ManualTripExtensions",
                column: "OperatorId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop in reverse dependency order — children before parent
            // before schema.
            migrationBuilder.DropTable(
                name: "ManualTripExtensions",
                schema: "transportmanual");

            migrationBuilder.DropTable(
                name: "GeofenceOverrideRequests",
                schema: "transportmanual");

            migrationBuilder.DropTable(
                name: "OperatorPushSubscriptions",
                schema: "transportmanual");

            migrationBuilder.DropTable(
                name: "OperatorCertifications",
                schema: "transportmanual");

            migrationBuilder.DropTable(
                name: "Operators",
                schema: "transportmanual");

            migrationBuilder.Sql(@"DROP SCHEMA IF EXISTS transportmanual CASCADE;");
        }
    }
}
