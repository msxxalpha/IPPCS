using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    [DbContext(typeof(ProductionDb))]
    [Migration("20261027110100_StageTwentyTwoEngineeringCutovers")]
    public partial class StageTwentyTwoEngineeringCutovers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only an additive independent journal; existing engineering
            // revisions and manufacturing history are not reset or rewritten.
            migrationBuilder.CreateTable(
                name: "FactoryEngineeringCutoverEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaselineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CutoverOn = table.Column<DateOnly>(type: "date", nullable: false),
                    BaselinePreviousEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    BaselineNewEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    ApprovalReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DraftOrdersToRebind = table.Column<int>(type: "int", nullable: false),
                    FrozenOrdersPreserved = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactoryEngineeringCutoverEvents", x => x.Id);
                });
            migrationBuilder.CreateIndex(
                name: "IX_FactoryEngineeringCutoverEvents_Kind_CandidateId",
                table: "FactoryEngineeringCutoverEvents",
                columns: new[] { "Kind", "CandidateId" },
                unique: true);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FactoryEngineeringCutoverEvents");
        }
    }
}
