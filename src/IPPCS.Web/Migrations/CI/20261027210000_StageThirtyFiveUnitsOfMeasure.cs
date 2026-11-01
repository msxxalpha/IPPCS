using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace IPPCS.Web.Migrations.CI
{
    [DbContext(typeof(ProductionDb))]
    [Migration("20261027210000_StageThirtyFiveUnitsOfMeasure")]
    public partial class StageThirtyFiveUnitsOfMeasure : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An IPPCS-only catalog. No conversion rates are implied; no ERP,
            // warehouse, sales, procurement or HR integration is added.
            var options=new[]{
                ("PCS","عدد / قطعه"),("EA","هر عدد"),("SET","ست"),
                ("KG","کیلوگرم"),("G","گرم"),("L","لیتر"),("ML","میلی‌لیتر"),
                ("M","متر"),("CM","سانتی‌متر"),("MM","میلی‌متر"),
                ("N","نیوتن"),("KN","کیلونیوتن"),("BAR","بار"),
                ("MPA","مگاپاسکال"),("PA","پاسکال"),("NM","نیوتن‌متر"),
                ("V","ولت"),("A","آمپر"),("OHM","اهم"),
                ("C","درجه سلسیوس"),("MS","میلی‌ثانیه"),("S","ثانیه"),
                ("MIN","دقیقه"),("H","ساعت"),("PCT","درصد")
            };
            for(var i=0;i<options.Length;i++)
            {
                var (code,label)=options[i];
                migrationBuilder.InsertData(
                    table:"FactoryMasterOptions",
                    columns:new[]{"Id","Kind","Code","Label","IsActive","SortOrder",
                        "CreatedUtc","UpdatedUtc"},
                    columnTypes:new[]{"uniqueidentifier","nvarchar(40)","nvarchar(40)",
                        "nvarchar(100)","bit","int","datetime2","datetime2"},
                    values:new object[]{
                        Guid.Parse($"0e5a3500-0000-4000-8000-{(i+1):000000000000}"),
                        "UnitOfMeasure",code,label,true,(i+1)*10,
                        new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc),
                        new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc)
                    });
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversing a catalog consumed by actual production requires
            // plant-signed data backup/restore review. IDs are deterministic.
            for(var i=0;i<25;i++)
                migrationBuilder.Sql(
                    $"DELETE FROM [FactoryMasterOptions] WHERE [Id] = '0e5a3500-0000-4000-8000-{(i+1):000000000000}'");
        }
    }
}
