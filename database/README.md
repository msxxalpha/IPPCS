# نصب کامل SQL Server برای اولین اجرای IPPCS

**پیشنهاد برای کسی که هنوز SQL و Visual Studio پروژه را اجرا نکرده است:** فایل `IPPCS_Dev_Complete_Install.sql`، خروجی قابل اجرای مستقیم در **SQL Server Management Studio** است. این فایل از تمام Migrationهای EF Core پروژه، نه یک مدل دست‌ساز و مجزا، ساخته می‌شود.

## فایل SQL یکپارچه را از کجا دریافت کنیم؟

در GitHub Actions، آخرین اجرای **موفق** `IPPCS consolidated acceptance` روی Draft PR #1 را باز کنید؛ در قسمت **Artifacts** فایل `IPPCS_Dev_Complete_Install_SQL` را دریافت و ZIP آن را استخراج کنید. داخل آن یک فایل `IPPCS_Dev_Complete_Install.sql` قرار دارد. CI نه فقط تولید فایل، بلکه اجرای همان نوع اسکریپت روی یک SQL Server 2022 موقت، بررسی ایجاد تمام جدول‌های اصلی و اجرای دوباره بدون حذف داده را آزمایش می‌کند.

این Artifact موقتی است (دوره نگهداری CI: ۳۰ روز) اما **تولیدکننده فایل همیشه در Git باقی می‌ماند**:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.0
powershell -ExecutionPolicy Bypass -File .\scripts\Export-IPPCSDatabaseSql.ps1 -Database IPPCS_Dev
```

پیش‌نیاز تولید محلی: .NET SDK 10، EF tool 10 و Python 3. ساخت فایل SQL به هیچ SQL Server متصل نمی‌شود و داده‌ای را تغییر نمی‌دهد.

## اجرای فایل در SSMS

1. فقط به **SQL Server مستقل توسعه/تست** متصل شوید. با اکانتی که اجازه `CREATE DATABASE` دارد، SSMS را باز کنید.
2. فایل کامل `IPPCS_Dev_Complete_Install.sql` را در SSMS با **File > Open > File** باز کنید. فایل UTF-8 با BOM است تا عنوان‌های فارسی Seed سالم باشند.
3. مطمئن شوید ابتدای آن دقیقاً `USE [master]` و `CREATE DATABASE [IPPCS_Dev]` و `USE [IPPCS_Dev]` آمده است؛ آدرس سرور، نام فایل و Script را کنترل کنید. هیچ ارتباطی با بانک انبار، فروش، خرید، HR یا سامانه کالیبراسیون وجود ندارد.
4. **Execute (F5)** را فقط یک بار اجرا کنید. اسکریپت اگر DB وجود ندارد آن را می‌سازد، Migrationهای پایه و افزایشی را به‌ترتیب اجرا می‌کند، نسخه‌ها را در `__EFMigrationsHistory` ثبت و فهرست‌های پیش‌فرض واحد اندازه‌گیری/علت را درج می‌کند. اگر DB از قبل جدول‌های ناشناخته بدون تاریخچه EF داشته باشد، با خطا متوقف می‌شود.
5. برای مشاهده وضعیت، این Query فقط‌خواندنی را اجرا کنید:

```sql
SELECT DB_NAME() AS CurrentDatabase;
SELECT [MigrationId], [ProductVersion]
FROM [IPPCS_Dev].[dbo].[__EFMigrationsHistory]
ORDER BY [MigrationId];
SELECT name FROM [IPPCS_Dev].sys.tables
WHERE name IN (N'AspNetUsers', N'ManufacturingOrders',
               N'FactoryMasterOptions', N'ManufacturingInspectionResults',
               N'FactoryApsRuns')
ORDER BY name;
```

مقایسه تاریخچه Migration و وجود ۵ جدول کلیدی، **بررسی حداقلی** است؛ گزارش موفق CI و آزمون عملی ۱۸ ماژول همچنان لازم‌اند.

## اتصال Visual Studio و ورود اولیه

```powershell
dotnet user-secrets set "ConnectionStrings:Production" "Server=localhost;Database=IPPCS_Dev;Trusted_Connection=True;TrustServerCertificate=True;" --project .\src\IPPCS.Web\IPPCS.Web.csproj
dotnet user-secrets set "Bootstrap:AdminUsername" "ippcs-local-admin" --project .\src\IPPCS.Web\IPPCS.Web.csproj
dotnet user-secrets set "Bootstrap:AdminPassword" "<YOUR-UNIQUE-STRONG-PASSWORD>" --project .\src\IPPCS.Web\IPPCS.Web.csproj
```

بعد از نصب SQL و تعریف تنظیمات خصوصی بالا، `IPPCS.sln` را در Visual Studio باز کنید، `IPPCS.Web` را Startup Project بگذارید، با پروفایل HTTPS در **Development** اجرا کنید و وارد `https://localhost:7295/login.html` شوید. حساب مدیر **در SQL Script ایجاد نمی‌شود**: اولین اجرای برنامه پس از اتمام Migration آن را از تنظیمات خصوصی ایجاد می‌کند. نام کلید `Production` نام تنظیمات قدیمی کد است و به معنی اتصال به سرور اصلی شرکت نیست.

**توجه مهم:** اسکریپت idempotent است؛ اجرای دوباره روی همان DB دارای تاریخچه صحیح EF، Migrationهای اعمال‌شده را تکرار نمی‌کند. این ویژگی به معنی مجوز تغییر بانک دارای داده واقعی نیست: از هر بانک موجود پیش از ارتقا Backup بگیرید، اسکریپت را بازبینی کنید، و فقط در محیط کنترل‌شده اجرا کنید.

## جایگزین سریع بدون SSMS

اگر فقط قصد اجرای توسعه محلی را دارید، همان `dotnet ef database update` تمام Migrationها را روی Connection String تنظیم‌شده اعمال می‌کند. دو روش را با هم برای ساخت یک دیتابیس جدید لازم نیست اجرا کنید؛ مسیر SSMS برای فایل SQL یکپارچه و مسیر EF برای توسعه‌دهندگان قابل انتخاب‌اند.

## عدم اتصال به سامانه‌های موجود

هیچ Script برای ساخت بانک مستقل انبار/فروش/خرید/منابع انسانی/کالیبراسیون نوشته نشده است؛ فقط Schema خود IPPCS ایجاد می‌شود. این نصب **صرفاً محیط Development** است، نه مجوز انتشار در شبکه کارخانه.
