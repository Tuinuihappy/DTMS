using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPackageBarcodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackageBarcodes",
                schema: "planning",
                table: "Jobs",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackageBarcodes",
                schema: "planning",
                table: "Jobs");
        }
    }
}
