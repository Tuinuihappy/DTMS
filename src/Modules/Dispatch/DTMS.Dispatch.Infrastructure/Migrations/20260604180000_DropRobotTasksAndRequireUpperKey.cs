using DTMS.Dispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Dispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Phase b7 cleanup — remove the legacy per-task execution pipeline.
    /// Drops the RobotTasks table and any legacy trip rows that pre-date
    /// envelope dispatch (UpperKey IS NULL), then tightens UpperKey to
    /// NOT NULL with a plain unique index (replacing the filtered index).
    /// </summary>
    [DbContext(typeof(DispatchDbContext))]
    [Migration("20260604180000_DropRobotTasksAndRequireUpperKey")]
    public partial class DropRobotTasksAndRequireUpperKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the RobotTasks table — legacy per-task scheduling is gone.
            migrationBuilder.DropTable(
                name: "RobotTasks",
                schema: "dispatch");

            // Purge legacy Trip rows (those without a UpperKey came from the
            // pre-envelope DispatchTripCommand pipeline). The reconciler and
            // webhook lookup both key off UpperKey, so legacy rows are
            // unreachable in the new code path.
            migrationBuilder.Sql(
                "DELETE FROM dispatch.\"Trips\" WHERE \"UpperKey\" IS NULL;");

            // Replace the filtered unique index with a plain unique index
            // now that UpperKey is universally populated.
            migrationBuilder.DropIndex(
                name: "IX_Trips_UpperKey",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.AlterColumn<string>(
                name: "UpperKey",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_UpperKey",
                schema: "dispatch",
                table: "Trips",
                column: "UpperKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-create the table shape — content is not restored.
            migrationBuilder.DropIndex(
                name: "IX_Trips_UpperKey",
                schema: "dispatch",
                table: "Trips");

            migrationBuilder.AlterColumn<string>(
                name: "UpperKey",
                schema: "dispatch",
                table: "Trips",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_UpperKey",
                schema: "dispatch",
                table: "Trips",
                column: "UpperKey",
                unique: true,
                filter: "\"UpperKey\" IS NOT NULL");

            migrationBuilder.CreateTable(
                name: "RobotTasks",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<System.DateTime?>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SequenceOrder = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<System.DateTime?>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetStationId = table.Column<System.Guid?>(type: "uuid", nullable: true),
                    TripId = table.Column<System.Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RobotTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RobotTasks_Trips_TripId",
                        column: x => x.TripId,
                        principalSchema: "dispatch",
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RobotTasks_TripId",
                schema: "dispatch",
                table: "RobotTasks",
                column: "TripId");
        }
    }
}
