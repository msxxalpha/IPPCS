using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    public partial class StageThirtyThreeFactoryMasterData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive only: preserves all prior production, deviation, ECO and
            // quality evidence, including previously free-text owners.
            migrationBuilder.CreateTable(
                name:"FactoryMasterOptions",
                columns:table=>new
                {
                    Id=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
                    Kind=table.Column<string>(type:"nvarchar(40)",maxLength:40,nullable:false),
                    Code=table.Column<string>(type:"nvarchar(40)",maxLength:40,nullable:false),
                    Label=table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:false),
                    IsActive=table.Column<bool>(type:"bit",nullable:false),
                    SortOrder=table.Column<int>(type:"int",nullable:false),
                    CreatedUtc=table.Column<DateTime>(type:"datetime2",nullable:false),
                    UpdatedUtc=table.Column<DateTime>(type:"datetime2",nullable:false)
                },
                constraints:table=>table.PrimaryKey("PK_FactoryMasterOptions",x=>x.Id));
            migrationBuilder.CreateIndex(
                name:"IX_FactoryMasterOptions_Kind_Code",
                table:"FactoryMasterOptions",columns:new[]{"Kind","Code"},unique:true);
            migrationBuilder.AddColumn<string>(
                name:"ResponsibleCode",table:"ProductionDeviations",
                type:"nvarchar(40)",maxLength:40,nullable:true);
            migrationBuilder.AddColumn<string>(
                name:"RootCauseCategoryCode",table:"ProductionDeviations",
                type:"nvarchar(40)",maxLength:40,nullable:true);
            migrationBuilder.CreateIndex(
                name:"IX_ProductionDeviations_ResponsibleCode",
                table:"ProductionDeviations",column:"ResponsibleCode");
            migrationBuilder.CreateIndex(
                name:"IX_ProductionDeviations_RootCauseCategoryCode",
                table:"ProductionDeviations",column:"RootCauseCategoryCode");
            // Stable reporting codes mirror the existing public enum. These
            // are labels/options only, not additional inventory or HR data.
            var kinds=new []{
                ("DeviationCategory","0","مقدار تولید",10),
                ("DeviationCategory","1","موعد تحویل داخلی",20),
                ("DeviationCategory","2","کیفیت تولید",30),
                ("DeviationCategory","3","مواد پای خط",40),
                ("DeviationCategory","4","ظرفیت تولید",50),
                ("DeviationCategory","5","توقف تولید",60),
                ("DeviationCategory","6","سایر",70),
                ("RootCauseCategory","PROCESS","فرایند تولید",10),
                ("RootCauseCategory","MACHINE","ماشین‌آلات و تجهیزات",20),
                ("RootCauseCategory","METHOD","روش و دستورالعمل",30),
                ("RootCauseCategory","MATERIAL","مواد و قطعات پای خط",40),
                ("RootCauseCategory","MEASUREMENT","اندازه‌گیری و کنترل",50),
                ("RootCauseCategory","CAPACITY","ظرفیت و برنامه",60),
                ("RootCauseCategory","OTHER","سایر علل",70)
            };
            for(var i=0;i<kinds.Length;i++)
            {
                var (kind,optionCode,label,sort)=kinds[i];
                migrationBuilder.InsertData(
                    table:"FactoryMasterOptions",
                    columns:new[]{"Id","Kind","Code","Label","IsActive","SortOrder",
                        "CreatedUtc","UpdatedUtc"},
                    values:new object[]{
                        Guid.Parse($"0e5a3300-0000-4000-8000-{(i+1):000000000000}"),
                        kind,optionCode,label,true,sort,
                        new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc),
                        new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc)
                    });
            }
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name:"IX_ProductionDeviations_ResponsibleCode",
                table:"ProductionDeviations");
            migrationBuilder.DropIndex(name:"IX_ProductionDeviations_RootCauseCategoryCode",
                table:"ProductionDeviations");
            migrationBuilder.DropColumn(name:"ResponsibleCode",table:"ProductionDeviations");
            migrationBuilder.DropColumn(name:"RootCauseCategoryCode",table:"ProductionDeviations");
            migrationBuilder.DropTable(name:"FactoryMasterOptions");
        }
    }
}
