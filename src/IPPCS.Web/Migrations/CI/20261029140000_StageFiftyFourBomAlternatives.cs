using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    public partial class StageFiftyFourBomAlternatives : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Details",
                table: "ManufacturingOrderAudits",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlternativeGroupCode",
                table: "ManufacturingBomLines",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AlternativePriority",
                table: "ManufacturingBomLines",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultAlternative",
                table: "ManufacturingBomLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ManufacturingBomLines_AlternativePriority_Positive",
                table: "ManufacturingBomLines",
                sql: "[AlternativePriority] >= 1");

            migrationBuilder.CreateTable(
                name: "ManufacturingOrderMaterialAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BomLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SharePercent = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingOrderMaterialAllocations", x => x.Id);
                    table.CheckConstraint("CK_ManufacturingOrderMaterialAllocations_SharePercent",
                        "[SharePercent] > 0 AND [SharePercent] <= 100");
                    table.CheckConstraint("CK_ManufacturingOrderMaterialAllocations_PlannedQuantity",
                        "[PlannedQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_ManufacturingOrderMaterialAllocations_ManufacturingBomLines_BomLineId",
                        column: x => x.BomLineId,
                        principalTable: "ManufacturingBomLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrderMaterialAllocations_ManufacturingOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingBomLines_RevisionId_AlternativeGroupCode_AlternativePriority",
                table: "ManufacturingBomLines",
                columns: new[] { "RevisionId", "AlternativeGroupCode", "AlternativePriority" },
                unique: true,
                filter: "[AlternativeGroupCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingBomLines_RevisionId_AlternativeGroupCode",
                table: "ManufacturingBomLines",
                columns: new[] { "RevisionId", "AlternativeGroupCode" },
                unique: true,
                filter: "[AlternativeGroupCode] IS NOT NULL AND [IsDefaultAlternative] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderMaterialAllocations_BomLineId",
                table: "ManufacturingOrderMaterialAllocations",
                column: "BomLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderMaterialAllocations_OrderId_BomLineId",
                table: "ManufacturingOrderMaterialAllocations",
                columns: new[] { "OrderId", "BomLineId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ManufacturingOrderMaterialAllocations");
            migrationBuilder.DropIndex(
                name: "IX_ManufacturingBomLines_RevisionId_AlternativeGroupCode_AlternativePriority",
                table: "ManufacturingBomLines");
            migrationBuilder.DropIndex(
                name: "IX_ManufacturingBomLines_RevisionId_AlternativeGroupCode",
                table: "ManufacturingBomLines");
            migrationBuilder.DropCheckConstraint(
                name: "CK_ManufacturingBomLines_AlternativePriority_Positive",
                table: "ManufacturingBomLines");
            migrationBuilder.DropColumn(name: "AlternativeGroupCode", table: "ManufacturingBomLines");
            migrationBuilder.DropColumn(name: "AlternativePriority", table: "ManufacturingBomLines");
            migrationBuilder.DropColumn(name: "IsDefaultAlternative", table: "ManufacturingBomLines");
            migrationBuilder.AlterColumn<string>(
                name: "Details",
                table: "ManufacturingOrderAudits",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
