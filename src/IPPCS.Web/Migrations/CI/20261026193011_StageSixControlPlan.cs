using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    /// <inheritdoc />
    public partial class StageSixControlPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManufacturingControlPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoutingRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingControlPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingControlPlans_RoutingRevisions_RoutingRevisionId",
                        column: x => x.RoutingRevisionId,
                        principalTable: "RoutingRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingLotGenealogies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceConfirmationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationConfirmationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LinkedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LinkedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingLotGenealogies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingLotGenealogies_ManufacturingConfirmations_DestinationConfirmationId",
                        column: x => x.DestinationConfirmationId,
                        principalTable: "ManufacturingConfirmations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingLotGenealogies_ManufacturingConfirmations_SourceConfirmationId",
                        column: x => x.SourceConfirmationId,
                        principalTable: "ManufacturingConfirmations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingLotGenealogies_ManufacturingOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ControlPlanCharacteristics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationSequence = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Minimum = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Maximum = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Mandatory = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlPlanCharacteristics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ControlPlanCharacteristics_ManufacturingControlPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "ManufacturingControlPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingInspectionRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCharacteristicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Minimum = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Maximum = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Mandatory = table.Column<bool>(type: "bit", nullable: false),
                    LastPassed = table.Column<bool>(type: "bit", nullable: true),
                    LastMeasuredUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingInspectionRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingInspectionRequirements_ControlPlanCharacteristics_SourceCharacteristicId",
                        column: x => x.SourceCharacteristicId,
                        principalTable: "ControlPlanCharacteristics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingInspectionRequirements_ManufacturingOrderOperations_OrderOperationId",
                        column: x => x.OrderOperationId,
                        principalTable: "ManufacturingOrderOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingInspectionResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequirementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    QuarantinedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingInspectionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingInspectionResults_ManufacturingInspectionRequirements_RequirementId",
                        column: x => x.RequirementId,
                        principalTable: "ManufacturingInspectionRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturingInspectionResults_ManufacturingOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "ManufacturingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ControlPlanCharacteristics_PlanId_OperationSequence_Code",
                table: "ControlPlanCharacteristics",
                columns: new[] { "PlanId", "OperationSequence", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingControlPlans_RoutingRevisionId_Code",
                table: "ManufacturingControlPlans",
                columns: new[] { "RoutingRevisionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingInspectionRequirements_OrderOperationId_Code",
                table: "ManufacturingInspectionRequirements",
                columns: new[] { "OrderOperationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingInspectionRequirements_SourceCharacteristicId",
                table: "ManufacturingInspectionRequirements",
                column: "SourceCharacteristicId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingInspectionResults_OrderId",
                table: "ManufacturingInspectionResults",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingInspectionResults_RequirementId",
                table: "ManufacturingInspectionResults",
                column: "RequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingLotGenealogies_DestinationConfirmationId",
                table: "ManufacturingLotGenealogies",
                column: "DestinationConfirmationId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingLotGenealogies_OrderId",
                table: "ManufacturingLotGenealogies",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingLotGenealogies_SourceConfirmationId_DestinationConfirmationId",
                table: "ManufacturingLotGenealogies",
                columns: new[] { "SourceConfirmationId", "DestinationConfirmationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufacturingInspectionResults");

            migrationBuilder.DropTable(
                name: "ManufacturingLotGenealogies");

            migrationBuilder.DropTable(
                name: "ManufacturingInspectionRequirements");

            migrationBuilder.DropTable(
                name: "ControlPlanCharacteristics");

            migrationBuilder.DropTable(
                name: "ManufacturingControlPlans");
        }
    }
}
