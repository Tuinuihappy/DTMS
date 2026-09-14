using DTMS.Fleet.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.Fleet.Infrastructure.Migrations
{
    /// <summary>
    /// CARRIER QR LABELS — remove the second identifier nobody reads.
    ///
    /// <para><b>The case it existed for never once occurred.</b> Barcode was added
    /// as "the scan tag when it differs from CarrierCode". In the live database
    /// every carrier had Barcode set to exactly its own CarrierCode — a column
    /// whose whole purpose was to hold a *different* value, holding the same one,
    /// every time. Operators saw an empty box on the form and retyped the code
    /// into it, which is the behaviour any second identifier field invites.</para>
    ///
    /// <para><b>Nothing read it.</b> There was no GetByBarcodeAsync anywhere. The
    /// only consumers were BarcodeExistsAsync — a uniqueness guard protecting the
    /// column from itself — and one ILIKE clause in the registry search. Carrying
    /// a column, an index, and two pre-checks for that is not a trade worth making.</para>
    ///
    /// <para><b>It was about to become a liability.</b> Scanning a QR label has to
    /// turn a scanned string into one carrier. Two identifier spaces with no
    /// constraint between them means one carrier's Barcode could equal another's
    /// CarrierCode, so the lookup would need precedence rules and new validation to
    /// stay unambiguous. With this column gone, scan-resolve is the existing
    /// GET /carriers/{code} and there is nothing left to disambiguate.</para>
    ///
    /// REVERSIBLE: Down() recreates the column and its partial unique index, but
    ///   empty. The values it held were duplicates of CarrierCode, so nothing of
    ///   substance is lost — but a rollback cannot restore them, and the UI no
    ///   longer offers a way to enter one.
    /// </summary>
    [DbContext(typeof(FleetDbContext))]
    [Migration("20260911100000_DropCarrierBarcode")]
    public partial class DropCarrierBarcode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carriers_Barcode",
                schema: "fleet",
                table: "Carriers");

            migrationBuilder.DropColumn(
                name: "Barcode",
                schema: "fleet",
                table: "Carriers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                schema: "fleet",
                table: "Carriers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_Barcode",
                schema: "fleet",
                table: "Carriers",
                column: "Barcode",
                unique: true,
                filter: "\"Barcode\" IS NOT NULL");
        }
    }
}
