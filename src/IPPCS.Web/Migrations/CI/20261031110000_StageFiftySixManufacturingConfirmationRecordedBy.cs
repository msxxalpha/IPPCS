using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    public partial class StageFiftySixManufacturingConfirmationRecordedBy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The current EF model and snapshot already include RecordedBy,
            // but the original base-table migration omitted it. Backfill old
            // rows as "unknown" and require an actor for all new confirmations.
            migrationBuilder.AddColumn<string>(
                name: "RecordedBy",
                table: "ManufacturingConfirmations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "unknown");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordedBy",
                table: "ManufacturingConfirmations");
        }
    }
}