# IPPCS — نخستین نسخه قابل اجرا برای آزمون واقعی کاربر (First UAT 1)

**نسخه:** `0.8.0-first-uat1` | **شاخه ثابت پیشنهادی:** `uat/first-factory-test-2026-10-28`

این نسخه برای **تست دستی با داده واقعیِ کنترل‌شده در Visual Studio و SQL Server مجزا** آماده‌سازی شده است؛ برای استقرار دائمی روی سرور/IIS، بهره‌برداری خط تولید، اتصال به سامانه‌های سازمان یا تأیید رسمی ۱۸ ماژول **مجاز نیست**. همه رکوردهای تست باید با شماره `UAT-` مشخص شوند؛ داده یا نتیجه آزمایش ساختگی را سند تولید واقعی معرفی نکنید.

## چه زمانی دقیقاً «آماده تست» است؟

- تمام گیت‌های GitHub Actions همان Commit، Build، xUnit/Node، سازگاری EF، SQL Server، احراز هویت و E2E واقعی سبز باشد؛
- نصب یک‌فایل SQL Server `IPPCS_Dev_Complete_Install.sql` روی دیتابیس خالی و تکرار اجرای آن در SQL Server موقت موفق باشد؛
- آزمون CI تمام ۱۶ صفحه ضروری، ۱۸ مسیر ماژول، ورود مدیر، کاتالوگ واحدها و چاپ یک دستور SQL واقعی را کنترل کند؛
- شاخه مجزای `uat/first-factory-test-2026-10-28` پس از سبزشدن همه این گیت‌ها، روی همان Commit قفل شود؛
- اجرای واقعی روی رایانه شما **هنوز انجام نشده** و نتیجه تست دستی/تأیید کارخانه صفر است.

## ۱. دریافت نسخه ثابت، نه شاخه توسعه متغیر

**روش ساده‌تر بدون Git و بدون ساخت دستی پروژه:** در [Draft PR #1](https://github.com/msxxalpha/IPPCS/pull/1)، آخرین **GitHub Actions موفقِ همین نسخه** را باز کنید؛ از Artifacts بسته `IPPCS_First_UAT_1_VisualStudio_SQL_Complete` را دانلود و Extract کنید. بسته شامل **همه سورس‌های VS، فایل IPPCS.sln، فایل SQL کامل، اسکریپت‌های آزمون و این راهنما** است؛ فایل `TESTED_COMMIT.txt` شناسه Commit آزموده‌شده را نشان می‌دهد. **این بسته فاقد نام کاربری/رمز پیش‌فرض و داده عملیاتی شرکت است.** پوشه استخراج‌شده را در Visual Studio باز کنید. برای دریافت مجدد یا نگهداری در Git، روش بعد را دنبال کنید.

**روش دوم با Git و شاخه پایدار:** در Visual Studio یا PowerShell:

```powershell
git clone --branch uat/first-factory-test-2026-10-28 https://github.com/msxxalpha/IPPCS.git
cd IPPCS
git branch --show-current
git rev-parse HEAD
```

اگر قبلاً مخزن Clone شده:

```powershell
git fetch origin
git switch --track origin/uat/first-factory-test-2026-10-28
```

اگر شاخه قبلاً به‌صورت محلی موجود است، به جای دستور switch با `--track`، همان شاخه را Checkout و Pull کنید. **با `main` ادغام نکنید**. آخرین Commit را با شناسه ذکرشده در Draft PR مقایسه کنید.

## ۲. SQL Server 2022 آزمایشی و دیتابیس تازه

الف) اگر بسته کامل `IPPCS_First_UAT_1_VisualStudio_SQL_Complete` را دریافت کرده‌اید، فایل `database/IPPCS_Dev_Complete_Install.sql` از قبل داخل آن است. در غیر این صورت از آخرین GitHub Actions موفق همان Commit، Artifact `IPPCS_Dev_Complete_Install_SQL` را دانلود و از ZIP استخراج کنید. این اسکریپت شامل ساخت DB، تمام جدول‌های EF/Identity، روابط، Seed اطلاعات پایه و تاریخچه Migration است.

ب) در SSMS با اتصال به **SQL Server آزمایشی**، فایل `IPPCS_Dev_Complete_Install.sql` را باز و F5 اجرا کنید. قبل از اجرا باید `CREATE DATABASE [IPPCS_Dev]` را ببینید.

ج) برای اعتبارسنجی فقط‌خواندنی:

```sql
SELECT COUNT(*) AS AppliedMigrations
FROM [IPPCS_Dev].[dbo].[__EFMigrationsHistory];

SELECT name FROM [IPPCS_Dev].sys.tables
WHERE name IN (N'AspNetUsers', N'ManufacturingOrders',
               N'FactoryMasterOptions', N'ManufacturingInspectionResults',
               N'FactoryApsRuns');
```

**مسیر جایگزین:** `dotnet ef database update`، فقط پس از ثبت User Secrets. Design-time factory اکنون **همان User Secrets** را می‌خواند و اجرای اشتباه روی DB غیر از `IPPCS_*` را رد می‌کند. برای اولین نصب فقط یکی از مسیرهای SSMS یا EF را انتخاب کنید. اطلاعات پایگاه‌های انبار، خرید، فروش، منابع انسانی یا کالیبراسیون اصلاً دریافت نمی‌شوند.

## ۳. تنظیمات محلی و اجرای Visual Studio

```powershell
dotnet user-secrets set "ConnectionStrings:Production" "Server=localhost;Database=IPPCS_Dev;Trusted_Connection=True;TrustServerCertificate=True;" --project .\src\IPPCS.Web\IPPCS.Web.csproj
dotnet user-secrets set "Bootstrap:AdminUsername" "ippcs-local-admin" --project .\src\IPPCS.Web\IPPCS.Web.csproj
dotnet user-secrets set "Bootstrap:AdminPassword" "<YOUR-UNIQUE-STRONG-PASSWORD>" --project .\src\IPPCS.Web\IPPCS.Web.csproj
dotnet dev-certs https --trust
```

رمز فقط در User Secrets خودتان قرار گیرد؛ آن را در پیام، Issue، عکس، کد یا GitHub Commit ننویسید. برای SQL Express، `Server=localhost\SQLEXPRESS` یا سرور آزمایشی صحیح را جایگزین کنید. SQL account با دسترسی کافی یا Windows Authentication صحیح نیاز است.

`IPPCS.sln` را باز کنید، `IPPCS.Web` را **Set as Startup Project** قرار دهید، پروفایل **IPPCS.Web (HTTPS)** را انتخاب و **F5** بزنید. ورود: `https://localhost:7295/login.html`. روی صفحه ورود باید `0.8.0-first-uat1` درج شده باشد.

پس از ورود، `https://localhost:7295/health` باید JSON با `"status":"ready"` و همین نسخه نشان دهد. اگر خطا دارد، ابتدا اتصال SQL، نصب همه Migrationها، تنظیمات و خروجی Visual Studio را کنترل کنید.

## ۴. آزمون خودکار **روی همان کامپیوتر** (خواندنی؛ بدون تغییر اطلاعات)

وقتی پروژه با F5 اجرا است، از **دومین پنجره PowerShell در ریشه مخزن**:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Test-IPPCS-FirstTest.ps1 -Username ippcs-local-admin
```

رمز مدیر محلی را به صورت امن در پنجره وارد کنید. برنامه **هیچ داده تولیدی تغییر نمی‌دهد**؛ فقط `/health`، نسخه، HTTPS، ۱۶ صفحه، ورود، مسیرهای ۱۸ ماژول، Master Data و گزارش را می‌سنجد. در پایان `PASS` و نسبت گیت‌های اطلاعات پایه آماده را چاپ می‌کند. این آزمون خودش عملیات تولید انجام نمی‌دهد و جانشین بررسی دستی نیست.

**اگر پاس نشد:** خروجی خطا، `dotnet --info`، `git rev-parse HEAD`، نام اینستنس SQL (بدون رمز)، URL و گام متوقف‌شده را ارسال کنید. **رمز عبور یا Connection String دارای پسورد را ارسال نکنید**.

## ۵. اولین سناریوی تست با داده واقعی تحت کنترل

| گام | صفحه و اقدام | نتیجه‌ای که باید بررسی شود |
|---|---|---|
| ۱ | `/factory-master-data.html`؛ واحدهای فنی، دلیل توقف و قرنطینه را بازبینی کنید | کدهای Master Data از لیست انتخاب شوند، ورود آزاد برای کدهای مرجع الزامی نباشد |
| ۲ | `/app.html`؛ محصول `UAT-SHOCK-001` و نیمه‌ساخته `UAT-SEMI-001`، مرکز و یک شیفت آزمایشی تعریف کنید | نام، کد، واحد و نوع بعد از ذخیره مجدداً در لیست دیده شوند |
| ۳ | `/app.html`؛ Routing نسخه‌دار دارای عملیات و زمان استاندارد ایجاد و بعد از بررسی تصویب کنید | تاریخ‌های شمسی معتبر؛ عملیات و وضعیت مصوب ذخیره شوند |
| ۴ | `/planning.html`؛ BOM نسخه‌دار متناسب با محصول را ثبت و تصویب کنید | ترکیب قطعات، ضریب مصرف، بازه اعتبار و تصویب با داده واقعی مطابقت داشته باشد |
| ۵ | `/control-plan.html`؛ کنترل پلن بر مسیر مصوب تعریف، حد پایین/بالای مشخصه و واحد انتخابی را وارد و تصویب کنید | کنترل پلن قبل از آزادسازی دستور ثبت شده باشد تا ویژگی‌ها به دستور Snapshot شوند |
| ۶ | `/planning.html`؛ یک نسخه MPS آزمایشی با ردیف محصول/مقدار/موعد ایجاد و تصویب کنید | در چاپ MPS نسخه، مقدار، موعد و وضعیت صحیح درج شود |
| ۷ | `/execution.html`؛ دستور با شماره `UAT-WO-001` ایجاد، ابتدا Release Check و سپس آزادسازی کنید | شماره یکتا، BOM/Routing منجمد، عملیات و وضعیت Released نمایش داده شود |
| ۸ | `/execution.html`؛ شروع/پایان عملیات و سالم/ضایعات/بازکاری و کد بچ را ثبت کنید؛ ابتدا یک حالت نامنطبق را بررسی کنید | موانع کیفیت به‌جای دورزدن گیت نمایش یابند؛ هیچ رکورد بدون مجوز نهایی نشود |
| ۹ | `/control-plan.html`؛ نتایج بازرسی، قرنطینه و ردیابی بچ‌های مرحله‌ای را اجرا کنید | نتیجه واقعی Pass/Fail، قرنطینه حل‌نشده و اتصال بچ قابل پیگیری باشد |
| ۱۰ | `/factory-reports.html` و بخش چاپ دستور؛ بازه شمسی و فیلتر را اعمال، PDF/Print تهیه کنید | داده چاپی با فرم و SQL یکی باشد، سربرگ، واترمارک، قلم فارسی و چندصفحه درست چاپ شوند |
| ۱۱ | `/factory-release-readiness.html`؛ ۱۸ ماژول و گیت‌ها را مرور کنید | تعداد گیت‌های اطلاعات پایه تغییر کند؛ **Factory accepted همچنان صفر** بماند |

بعد از این مسیر، تست عمیق‌تر **مونتاژ نهایی، گواهی EOL، سریال، شجره نیمه‌ساخته، APS، MRP، CAPA، OEE و What-if** را طبق راهنمای `/help.html` و چک‌لیست `docs/FACTORY_UAT_RELEASE_CHECKLIST.md` انجام دهید. گواهی EOL فقط پس از ثبت کنترل‌های واقعی و شجره معتبر، نه با داده ساختگی، قابل تأیید است.

## حدهای نسخه نخست

- نسخه برای **تست واقعی فرآیند با کاربران کارخانه، در محیط Development بسته** است، نه عملیاتی کردن IIS.
- کالیبراسیون، انبار، خرید، فروش و منابع انسانی خارج از دامنه و بدون هیچ ارتباط مستقیم با این نسخه‌اند.
- طرح نمونه‌برداری نسخه‌دار و MSA مستقل، اصلاح پیچیده بچ، بهینه‌سازی مجدد منابع و تعریف/پذیرش KPI واقعی هنوز گیت باز هستند؛ ممکن است برای پذیرش نهایی بعضی ماژول‌ها مانع باشند ولی مانع شروع تست **سناریوی مقدماتی** نیستند.
- **نتیجه واقعی Visual Studio و آزمون کارخانه ۰/۱۸** است تا شما آنها را اجرا و گزارش کنید.
