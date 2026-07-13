using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTMS.DeliveryOrder.Infrastructure.Migrations
{
    /// <summary>
    /// Rename the UI/operator order origin slug from <c>manual</c> to
    /// <c>internal</c>. The origin is not an external system (the operator is
    /// recorded on <c>CreatedBy</c>); <c>internal</c> reads more clearly as
    /// "raised inside DTMS" than "manual". Back-fills existing rows so old and
    /// new orders share one canonical value.
    ///
    /// <para>Reversible — <c>Down</c> restores the <c>manual</c> slug.</para>
    /// </summary>
    [DbContext(typeof(DeliveryOrderDbContext))]
    [Migration("20260711140000_RenameManualSourceSystemToInternal")]
    public partial class RenameManualSourceSystemToInternal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""DeliveryOrders""
                SET ""SourceSystemKey"" = 'internal',
                    ""SourceSystemDisplayName"" = 'Internal'
                WHERE ""SourceSystemKey"" = 'manual';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE deliveryorder.""DeliveryOrders""
                SET ""SourceSystemKey"" = 'manual',
                    ""SourceSystemDisplayName"" = 'Manual'
                WHERE ""SourceSystemKey"" = 'internal';
            ");
        }
    }
}
