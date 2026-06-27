using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobFactsFailureCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureCategory",
                schema: "bi",
                table: "JobFacts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateIndex(
                name: "IX_JobFacts_FailureCategory_CreatedAt",
                schema: "bi",
                table: "JobFacts",
                columns: new[] { "FailureCategory", "CreatedAt" },
                filter: "\"FailureCategory\" <> 'None'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobFacts_FailureCategory_CreatedAt",
                schema: "bi",
                table: "JobFacts");

            migrationBuilder.DropColumn(
                name: "FailureCategory",
                schema: "bi",
                table: "JobFacts");
        }
    }
}
