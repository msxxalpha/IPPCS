using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    [DbContext(typeof(ProductionDb))]
    [Migration("20261029130000_StageFiftyThreeWorkCenterProductCapacities")]
    public partial class StageFiftyThreeWorkCenterProductCapacities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkCenterProductCapacities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoutingOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapacityUnitsPerHour = table.Column<decimal>(
                        type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedBy = table.Column<string>(
                        type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCenterProductCapacities", x => x.Id);
                    table.CheckConstraint(
                        "CK_WorkCenterProductCapacities_CapacityUnitsPerHour_Positive",
                        "[CapacityUnitsPerHour] >= 0.0001");
                    table.ForeignKey(
                        name: "FK_WorkCenterProductCapacities_RoutingOperations_RoutingOperationId",
                        column: x => x.RoutingOperationId,
                        principalTable: "RoutingOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenterProductCapacities_RoutingOperationId",
                table: "WorkCenterProductCapacities",
                column: "RoutingOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenterProductCapacities_IsActive_RoutingOperationId",
                table: "WorkCenterProductCapacities",
                columns: new[] { "IsActive", "RoutingOperationId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WorkCenterProductCapacities");
        }
    }
}
