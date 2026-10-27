using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPPCS.Web.Migrations.CI
{
    [DbContext(typeof(ProductionDb))]
    [Migration("20261027198000_StageThirtyFourReasonCatalogSeeds")]
    public partial class StageThirtyFourReasonCatalogSeeds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Master-data defaults only. The factory Administrator may relabel,
            // reorder or deactivate; existing production history remains untouched.
            var options=new[]{
                ("DowntimeReason","EQUIPMENT","خرابی ماشین یا تجهیزات",10),
                ("DowntimeReason","SETUP","تنظیم و آماده‌سازی",20),
                ("DowntimeReason","TOOLING","مشکل ابزار و فیکسچر",30),
                ("DowntimeReason","QUALITY","توقف ناشی از کنترل کیفیت",40),
                ("DowntimeReason","CAPACITY","محدودیت ظرفیت خط",50),
                ("DowntimeReason","OTHER","سایر علل توقف",90),
                ("QualityHoldReason","MEASUREMENT","مغایرت اندازه‌گیری",10),
                ("QualityHoldReason","PROCESS","عدم‌انطباق فرآیند",20),
                ("QualityHoldReason","APPEARANCE","عیب ظاهری قطعه",30),
                ("QualityHoldReason","MATERIAL","عدم‌انطباق ماده پای خط",40),
                ("QualityHoldReason","ASSEMBLY","عدم‌انطباق مونتاژ",50),
                ("QualityHoldReason","OTHER","سایر موارد کیفیت",90)
            };
            for(var i=0;i<options.Length;i++)
            {
                var (kind,code,label,sort)=options[i];
                migrationBuilder.InsertData(
                    table:"FactoryMasterOptions",
                    columns:new[]{"Id","Kind","Code","Label","IsActive","SortOrder","CreatedUtc","UpdatedUtc"},
                    values:new object[]{
                        Guid.Parse($"0e5a3400-0000-4000-8000-{(i+1):000000000000}"),
                        kind,code,label,true,sort,
                        new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc),
                        new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc)
                    });
            }
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data removal only for this seed migration's deterministic IDs.
            // Do not reverse this migration after actual shop-floor usage
            // without a factory-reviewed backup and audit-preserving strategy.
            for(var i=0;i<12;i++)
            {
                migrationBuilder.DeleteData("FactoryMasterOptions","Id",
                    Guid.Parse($"0e5a3400-0000-4000-8000-{(i+1):000000000000}"));
            }
        }
    }
}
