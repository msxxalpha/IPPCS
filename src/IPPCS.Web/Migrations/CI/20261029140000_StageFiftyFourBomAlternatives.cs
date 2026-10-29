using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    [DbContext(typeof(ProductionDb))]
    [Migration("20261029140000_StageFiftyFourBomAlternatives")]
    public partial class StageFiftyFourBomAlternatives : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateTable(
                name: "ManufacturingOrderMaterialUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BomLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LotCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, defaultValue: "unknown"),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingOrderMaterialUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrderMaterialUsages_ManufacturingBomLines_BomLineId",
                        column: x => x.BomLineId,
                        principalTable: "ManufacturingBomLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrderMaterialUsages_ManufacturingOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrderMaterialUsages_PlanningItems_ComponentItemId",
                        column: x => x.ComponentItemId,
                        principalTable: "PlanningItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderMaterialAllocations_BomLineId",
                table: "ManufacturingOrderMaterialAllocations",
                column: "BomLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderMaterialAllocations_OrderId_BomLineId",
                table: "ManufacturingOrderMaterialAllocations",
                columns: new[] { "OrderId", "BomLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderMaterialUsages_BomLineId",
                table: "ManufacturingOrderMaterialUsages",
                column: "BomLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderMaterialUsages_ComponentItemId",
                table: "ManufacturingOrderMaterialUsages",
                column: "ComponentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderMaterialUsages_OrderId_BomLineId_OccurredUtc",
                table: "ManufacturingOrderMaterialUsages",
                columns: new[] { "OrderId", "BomLineId", "OccurredUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ManufacturingOrderMaterialAllocations");
            migrationBuilder.DropTable(name: "ManufacturingOrderMaterialUsages");
            migrationBuilder.DropColumn(name: "AlternativeGroupCode", table: "ManufacturingBomLines");
            migrationBuilder.DropColumn(name: "AlternativePriority", table: "ManufacturingBomLines");
            migrationBuilder.DropColumn(name: "IsDefaultAlternative", table: "ManufacturingBomLines");
        }
    }
}
