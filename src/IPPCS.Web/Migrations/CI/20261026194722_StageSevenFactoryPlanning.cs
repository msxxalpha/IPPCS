using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    /// <inheritdoc />
    public partial class StageSevenFactoryPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FactoryApsRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MpsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartOn = table.Column<DateOnly>(type: "date", nullable: false),
                    HorizonDays = table.Column<int>(type: "int", nullable: false),
                    Algorithm = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CalculatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryApsRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FactoryApsRuns_MpsVersions_MpsVersionId",
                        column: x => x.MpsVersionId,
                        principalTable: "MpsVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FactoryCenterCostRates",
                columns: table => new
                {
                    WorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversionRialsPerMinute = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ScrapRialsPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryCenterCostRates", x => x.WorkCenterId);
                    table.ForeignKey(
                        name: "FK_FactoryCenterCostRates_WorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "WorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FactoryDemands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryDemands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FactoryDemands_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FactoryMaterialUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityConsumed = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryMaterialUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FactoryMaterialUsages_Items_ComponentItemId",
                        column: x => x.ComponentItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FactoryMaterialUsages_ManufacturingOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FactorySopReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    Through = table.Column<DateOnly>(type: "date", nullable: false),
                    DemandQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RoughCutMinutes = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AvailableMinutes = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OverloadMinutes = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CalculatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactorySopReviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FactoryApsRuns_CalculatedUtc",
                table: "FactoryApsRuns",
                column: "CalculatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FactoryApsRuns_MpsVersionId",
                table: "FactoryApsRuns",
                column: "MpsVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FactoryDemands_ItemId_DueOn",
                table: "FactoryDemands",
                columns: new[] { "ItemId", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_FactoryMaterialUsages_ComponentItemId",
                table: "FactoryMaterialUsages",
                column: "ComponentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FactoryMaterialUsages_OrderId_ComponentItemId",
                table: "FactoryMaterialUsages",
                columns: new[] { "OrderId", "ComponentItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_FactorySopReviews_Code",
                table: "FactorySopReviews",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FactoryApsRuns");

            migrationBuilder.DropTable(
                name: "FactoryCenterCostRates");

            migrationBuilder.DropTable(
                name: "FactoryDemands");

            migrationBuilder.DropTable(
                name: "FactoryMaterialUsages");

            migrationBuilder.DropTable(
                name: "FactorySopReviews");
        }
    }
}
