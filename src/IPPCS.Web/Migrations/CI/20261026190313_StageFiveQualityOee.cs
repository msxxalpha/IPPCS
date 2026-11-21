using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    /// <inheritdoc />
    public partial class StageFiveQualityOee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QualityPendingQuantity",
                table: "ManufacturingOrderOperations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QualityRejectedQuantity",
                table: "ManufacturingOrderOperations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ManufacturingOeePlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedMinutes = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingOeePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingOeePlans_WorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "WorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingQualityHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DispositionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RaisedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ResolvedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RaisedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingQualityHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingQualityHolds_ManufacturingOrderOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "ManufacturingOrderOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingQualityHolds_ManufacturingOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOeePlans_WorkCenterId_ProductionDate",
                table: "ManufacturingOeePlans",
                columns: new[] { "WorkCenterId", "ProductionDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingQualityHolds_OperationId",
                table: "ManufacturingQualityHolds",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingQualityHolds_OrderId_OperationId_Status",
                table: "ManufacturingQualityHolds",
                columns: new[] { "OrderId", "OperationId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufacturingOeePlans");

            migrationBuilder.DropTable(
                name: "ManufacturingQualityHolds");

            migrationBuilder.DropColumn(
                name: "QualityPendingQuantity",
                table: "ManufacturingOrderOperations");

            migrationBuilder.DropColumn(
                name: "QualityRejectedQuantity",
                table: "ManufacturingOrderOperations");
        }
    }
}
