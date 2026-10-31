using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    public partial class StageThirtyTwoEcoApprovals : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive authorization evidence only. Existing approved BOM,
            // routing, work orders, journals, users and measurements stay intact.
            migrationBuilder.CreateTable(
                name: "FactoryEcoApprovals",
                columns: table => new
                {
                    Id=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
                    Kind=table.Column<string>(type:"nvarchar(16)",maxLength:16,nullable:false),
                    ItemId=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
                    BaselineId=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
                    CandidateId=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
                    CutoverOn=table.Column<DateOnly>(type:"date",nullable:false),
                    ApprovalReference=table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:false),
                    Reason=table.Column<string>(type:"nvarchar(300)",maxLength:300,nullable:false),
                    CandidateDigest=table.Column<string>(type:"nvarchar(64)",maxLength:64,nullable:false),
                    Status=table.Column<int>(type:"int",nullable:false),
                    RequestedBy=table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:false),
                    RequestedUtc=table.Column<DateTime>(type:"datetime2",nullable:false),
                    QualityReviewedBy=table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:true),
                    QualityReviewedUtc=table.Column<DateTime>(type:"datetime2",nullable:true),
                    QualityEvidenceReference=table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:true),
                    QualityObservation=table.Column<string>(type:"nvarchar(400)",maxLength:400,nullable:true),
                    ExecutedUtc=table.Column<DateTime>(type:"datetime2",nullable:true)
                },
                constraints: table=>table.PrimaryKey("PK_FactoryEcoApprovals",x=>x.Id));
            migrationBuilder.CreateIndex(name:"IX_FactoryEcoApprovals_ApprovalReference",
                table:"FactoryEcoApprovals",column:"ApprovalReference",unique:true);
            migrationBuilder.CreateIndex(name:"IX_FactoryEcoApprovals_Kind_CandidateId",
                table:"FactoryEcoApprovals",columns:new[]{"Kind","CandidateId"});
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name:"FactoryEcoApprovals");
        }
    }
}
