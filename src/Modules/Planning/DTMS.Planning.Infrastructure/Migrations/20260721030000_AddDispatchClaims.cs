using DTMS.Planning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Planning.Infrastructure.Migrations
{
    /// <summary>
    /// Idempotent manual dispatch — records one attempt of
    /// <c>POST /order-templates/{id}/create</c> so a repeat of the same
    /// operator intent is de-duplicated before the vendor is called again
    /// (RIOT3 does not de-duplicate on upperKey).
    ///
    /// New table only — no existing schema is touched, so this is safe for
    /// a rolling deploy. The unique index is on IdempotencyKey ALONE:
    /// dispatching the same template repeatedly is normal operation.
    /// </summary>
    [DbContext(typeof(PlanningDbContext))]
    [Migration("20260721030000_AddDispatchClaims")]
    public partial class AddDispatchClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DispatchClaims",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrderTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpperKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VendorOrderKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchClaims", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DispatchClaims_IdempotencyKey_Unique",
                schema: "planning",
                table: "DispatchClaims",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchClaims_OrderTemplateId_CreatedAt",
                schema: "planning",
                table: "DispatchClaims",
                columns: new[] { "OrderTemplateId", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispatchClaims",
                schema: "planning");
        }
    }
}
