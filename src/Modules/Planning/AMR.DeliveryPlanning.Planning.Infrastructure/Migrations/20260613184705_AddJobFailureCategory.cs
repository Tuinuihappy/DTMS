using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobFailureCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureCategory",
                schema: "planning",
                table: "Jobs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "None");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureCategory",
                schema: "planning",
                table: "Jobs");
        }
    }
}
