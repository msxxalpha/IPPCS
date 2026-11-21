using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    [DbContext(typeof(ProductionDb))]
    [Migration("20261027221500_StageThirtySevenApsReasonCatalog")]
    public partial class StageThirtySevenApsReasonCatalog : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var rows=new[]{
                ("MAINTENANCE","تعمیرات برنامه‌ای"),
                ("CALIBRATION","کالیبراسیون و تنظیم ابزار"),
                ("CHANGEOVER","تعویض قالب یا فیکسچر"),
                ("ENERGY","عدم دسترسی برنامه‌ریزی‌شده انرژی"),
                ("SAFETY","محدودیت ایمنی خط"),
                ("OTHER","سایر علل برنامه‌ریزی‌شده")
            };
            for(var i=0;i<rows.Length;i++)
            {
                var (code,label)=rows[i];
                migrationBuilder.InsertData(table:"FactoryMasterOptions",
                    columns:new[]{"Id","Kind","Code","Label","IsActive","SortOrder",
                        "CreatedUtc","UpdatedUtc"},
                    columnTypes:new[]{"uniqueidentifier","nvarchar(40)","nvarchar(40)",
                        "nvarchar(100)","bit","int","datetime2","datetime2"},
                    values:new object[]{
                        Guid.Parse($"0e5a3700-0000-4000-8000-{(i+1):000000000000}"),
                        "CapacityBlackoutReason",code,label,true,(i+1)*10,
                        new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc),
                        new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc)
                    });
            }
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            for(var i=0;i<6;i++)
                migrationBuilder.Sql(
                    $"DELETE FROM [FactoryMasterOptions] WHERE [Id] = '0e5a3700-0000-4000-8000-{(i+1):000000000000}'");
        }
    }
}
