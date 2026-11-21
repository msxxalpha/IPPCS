using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    [DbContext(typeof(ProductionDb))]
    [Migration("20261029120000_StageFiftyTwoPeriodicProductTests")]
    public partial class StageFiftyTwoPeriodicProductTests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FactoryPeriodicTestPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cadence = table.Column<int>(type: "int", nullable: false),
                    IntervalMonths = table.Column<int>(type: "int", nullable: false),
                    FirstDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    MethodReference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AcceptanceCriteria = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Minimum = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Maximum = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryPeriodicTestPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FactoryPeriodicTestPlans_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FactoryPeriodicTestRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    PerformedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SampleReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MeasuredValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ReportReference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RecordedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryPeriodicTestRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FactoryPeriodicTestRuns_FactoryPeriodicTestPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "FactoryPeriodicTestPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FactoryPeriodicTestPlans_Code",
                table: "FactoryPeriodicTestPlans",
                column: "Code",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FactoryPeriodicTestPlans_ItemId_IsActive",
                table: "FactoryPeriodicTestPlans",
                columns: new[] { "ItemId", "IsActive" });
            migrationBuilder.CreateIndex(
                name: "IX_FactoryPeriodicTestRuns_DueOn_State",
                table: "FactoryPeriodicTestRuns",
                columns: new[] { "DueOn", "State" });
            migrationBuilder.CreateIndex(
                name: "IX_FactoryPeriodicTestRuns_PlanId_DueOn",
                table: "FactoryPeriodicTestRuns",
                columns: new[] { "PlanId", "DueOn" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FactoryPeriodicTestRuns");
            migrationBuilder.DropTable(name: "FactoryPeriodicTestPlans");
        }
    }
}
