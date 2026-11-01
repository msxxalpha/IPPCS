using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    /// <inheritdoc />
    public partial class StageEightMpsOrderPegging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManufacturingOrderPegs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MpsLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LinkedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingOrderPegs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrderPegs_ManufacturingOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingOrderPegs_MpsDemandLines_MpsLineId",
                        column: x => x.MpsLineId,
                        principalTable: "MpsDemandLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderPegs_MpsLineId",
                table: "ManufacturingOrderPegs",
                column: "MpsLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingOrderPegs_OrderId_MpsLineId",
                table: "ManufacturingOrderPegs",
                columns: new[] { "OrderId", "MpsLineId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufacturingOrderPegs");
        }
    }
}
