using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    /// <inheritdoc />
    public partial class StageNineProductionDeviations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionDeviations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MpsLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RootCause = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrectiveAction = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Responsible = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActionDueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionDeviations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionDeviations_ManufacturingOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionDeviations_MpsDemandLines_MpsLineId",
                        column: x => x.MpsLineId,
                        principalTable: "MpsDemandLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionDeviationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionDeviationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionDeviationEvents_ProductionDeviations_DeviationId",
                        column: x => x.DeviationId,
                        principalTable: "ProductionDeviations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDeviationEvents_DeviationId",
                table: "ProductionDeviationEvents",
                column: "DeviationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDeviations_MpsLineId_Status",
                table: "ProductionDeviations",
                columns: new[] { "MpsLineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionDeviations_OrderId",
                table: "ProductionDeviations",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionDeviationEvents");

            migrationBuilder.DropTable(
                name: "ProductionDeviations");
        }
    }
}
