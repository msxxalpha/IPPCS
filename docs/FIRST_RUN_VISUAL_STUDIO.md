# راه‌اندازی نخستین نسخهٔ آزمایشی IPPCS در Visual Studio

**اگر هدف فقط اولین اجرای واقعی و تست با کاربران کارخانه است، ابتدا [بسته ویژه First UAT 1](FIRST_UAT_TEST_PACK.md) را بخوانید.** نسخه قفل‌شده `0.8.0-first-uat1` روی شاخه `uat/first-factory-test-2026-10-28` پس از موفقیت آزمون‌های CI ایجاد می‌شود. برای اولین تست از آن شاخه استفاده کنید، نه توسعه متغیر PR.

**هدف:** مشاهده و آزمون ۱۸ حوزهٔ نرم‌افزاری با SQL Server *مجزا*، بدون اتصال یا تغییر سامانه‌های فعلی شرکت. این راهنما مجوز استقرار عملیاتی نیست.

## ۱. دریافت صحیح شاخه — بدون Merge با main

اگر مخزن را قبلاً Clone نکرده‌اید، از یک ترمینال در محل دلخواه:

```powershell
git clone --branch feature/production-platform-foundation https://github.com/msxxalpha/IPPCS.git
cd IPPCS
git branch --show-current
git rev-parse --short HEAD
```

خروجی شاخه باید `feature/production-platform-foundation` باشد. اگر مخزن قبلاً در Visual Studio وجود دارد، با **Git > Manage Branches** همان شاخه را **Checkout** و سپس Pull کنید. نیازی به Merge، Rebase یا دریافت PR در `main` نیست. PR شمارهٔ ۱ همچنان Draft است.

## ۲. پیش‌نیازهای ویندوز

- Visual Studio با پشتیبانی پروژه‌های **ASP.NET Core / .NET 10** و Workload مربوط؛ نصب **.NET SDK 10**.
- SQL Server محلی/آزمایشی در دسترس؛ برای شروع یک دیتابیس جدید با نام پیشنهادی `IPPCS_Dev` و دسترسی کاربر Windows کافی است.
- **Git**، **Node.js LTS** برای آزمون جاوااسکریپت و `dotnet-ef` نسخهٔ ۱۰. برای اجرای آزمون کامل SQL در Docker، ابزارهای اضافی راهنمای تست جداگانه لازم هستند.
- این نسخه در محیط `ASPNETCORE_ENVIRONMENT=Development` اجرا می‌شود. **آن را روی IIS عملیاتی و شبکهٔ باز کارخانه منتشر نکنید.**

## ۳. فقط اتصال دیتابیس آزمایشی مستقل

از ریشه مخزن در PowerShell (یا Terminal خود Visual Studio) اجرا کنید:

```powershell
dotnet --version
dotnet user-secrets set "ConnectionStrings:Production" "Server=localhost;Database=IPPCS_Dev;Trusted_Connection=True;TrustServerCertificate=True;" --project .\src\IPPCS.Web\IPPCS.Web.csproj
```

شناسهٔ غیرمحرمانهٔ User Secrets از پیش در فایل پروژه ثبت شده است؛ بنابراین به اجرای `dotnet user-secrets init` یا ویرایش فایل پروژه نیازی ندارید و پس از Pull، پروژه وضعیت تغییر محلی غیرضروری نخواهد داشت.

اگر از SQL Server Express استفاده می‌کنید، مقدار `Server=localhost\SQLEXPRESS` را متناسب با اینستنس واقعی تغییر دهید. این دستورها تنها **تنظیمات محلی User Secrets** پروژه را می‌نویسند؛ رمز SQL را در Git یا `appsettings.json` قرار ندهید. نام `Production` در کلید اتصال، نام داخلی تنظیمات است و **به معنای دیتابیس عملیاتی شرکت نیست**.

## ۴. تعریف مدیر آزمایشی محلی

یک نام کاربری دلخواه و یک رمز *قوی و اختصاصی تست* انتخاب کنید (حداقل ۱۲ نویسه، حرف بزرگ، حرف کوچک، عدد و نماد). به‌جای عبارت‌های نمونه، مقادیر واقعی را **فقط در ترمینال خودتان** وارد کنید:

```powershell
dotnet user-secrets set "Bootstrap:AdminUsername" "ippcs-local-admin" --project .\src\IPPCS.Web\IPPCS.Web.csproj
dotnet user-secrets set "Bootstrap:AdminPassword" "<YOUR-STRONG-LOCAL-TEST-PASSWORD>" --project .\src\IPPCS.Web\IPPCS.Web.csproj
```

اطلاعات ورود را در چت یا GitHub ارسال نکنید. این حساب فقط در اولین راه‌اندازیِ موفق، پس از آماده‌سازی SQL ساخته می‌شود. روی حساب‌های قبلی رمز را بازنویسی نمی‌کند.

## ۵. ساخت دیتابیس یکپارچه IPPCS (پیشنهادی برای شروع شما)

اکنون **هنوز هیچ دیتابیسی ندارید**؛ نیازی نیست جدول‌ها یا دیتابیس را دستی بسازید. برای نخستین اجرا می‌توانید از فایل SQL یکپارچه و نسخه‌دار استفاده کنید:

1. در آخرین GitHub Actions موفق شاخه Draft، بخش **Artifacts** را باز کنید و `IPPCS_Dev_Complete_Install_SQL` را دریافت کنید؛ فایل داخل ZIP با نام `IPPCS_Dev_Complete_Install.sql` است.
2. در **SQL Server Management Studio** به اینستنس آزمایشی متصل شوید. فایل را باز و محتوای ابتدای آن، به‌خصوص `CREATE DATABASE [IPPCS_Dev]` را بازبینی کنید.
3. فایل را در SSMS اجرا کنید. دیتابیس IPPCS_Dev، جدول‌های Identity و کل مدل برنامه‌ریزی/کیفیت/تولید، ایندکس‌ها، کلیدهای خارجی و داده‌های مرجع پیش‌فرض به ترتیب Migration ایجاد می‌شوند. هیچ رمز مدیر در اسکریپت نیست.
4. اجرای موفق را با خواندن `IPPCS_Dev.dbo.__EFMigrationsHistory` و وجود `ManufacturingOrders` و `AspNetUsers` بررسی کنید.
5. Connection String تنظیم‌شده در گام ۳ باید **دقیقاً به همین دیتابیس `IPPCS_Dev`** متصل شود.

برای ساخت مجدد فایل از روی همین Commit بدون نیاز به دریافت Artifact می‌توانید اجرا کنید:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.0
powershell -ExecutionPolicy Bypass -File .\scripts\Export-IPPCSDatabaseSql.ps1 -Database IPPCS_Dev
```

تولید محلی فایل علاوه بر SDK 10 به **Python 3** نیاز دارد. این دستور فقط SQL ایجاد می‌کند، نه آن‌که آن را اجرا کند. روش جایگزین برای توسعه‌دهندگان: بعد از تنظیم Connection String، `dotnet ef database update --project .\src\IPPCS.Web\IPPCS.Web.csproj --startup-project .\src\IPPCS.Web\IPPCS.Web.csproj`. **برای ایجاد اولیه یکی از این دو مسیر کافی است.**

مطابق [راهنمای کامل دیتابیس](../database/README.md)، هرگز اسکریپت را روی بانک‌های موجود شرکت اجرا نکنید. اگر دیتابیس IPPCS از قبل داده دارد، قبل از ارتقا Backup بگیرید و Migrationها را بازبینی کنید.

## ۶. اجرای نسخه در Visual Studio

1. فایل **`IPPCS.sln`** را باز کنید.
2. در Solution Explorer روی پروژهٔ **`IPPCS.Web`** گزینهٔ **Set as Startup Project** را انتخاب کنید.
3. پروفایل **`IPPCS.Web (HTTPS)`** را انتخاب کنید؛ در صورت نیاز برای گواهی توسعه یک بار `dotnet dev-certs https --trust` را روی رایانه اجرا کنید.
4. با **F5** یا **Ctrl+F5** اجرا کنید. صفحهٔ اصلی در `https://localhost:7295/app.html` باز می‌شود و در صورت عدم ورود، به `/login.html` هدایت خواهید شد.
5. با نام کاربری/رمز آزمایشیِ تنظیم‌شده در گام ۴ وارد شوید.

اگر مرورگر باز نشد، URL را دستی وارد و ابتدا خروجی Terminal/Output ویژوال استودیو را بررسی کنید. برای خطاهای اتصال SQL، پیش از تغییر دادن کد، نام اینستنس و Windows Authentication و انجام Migration را بررسی کنید.

## ۷. ترتیب پیشنهادی آزمونِ سریع ولی قابل پیگیری

پس از ورود به `/help.html` بروید و **راهنمای ۶۴ فرم و ترتیب ۱۴گانه راه‌اندازی** را مرور کنید. سپس به `/factory-release-readiness.html` بروید. این صفحه فقط‌خواندنی است و ۱۸ ماژول، پیش‌نیازها و مسیر ورود به فرم‌های آنها را نشان می‌دهد.

ترتیب پیشنهادی داده‌سازی آزمایشی:

1. ابتدا در `/factory-master-data.html` فهرست‌های واحد اندازه‌گیری، علت توقف/قرنطینه و ابزار مشترک را مرور کنید؛ سپس در `/app.html` یک محصول و نیمه‌ساخته، مرکز و شیفت آزمایشی تعریف کنید.
2. از `/app.html` Routing نسخه‌دار را تعریف و تصویب و از `/planning.html` BOM نسخه‌دار را تصویب کنید. سپس قبل از صدور/آزادسازی دستور، Control Plan مرتبط را در `/control-plan.html` تصویب و MPS آزمایشی را در `/planning.html` ثبت کنید. موجودی برنامه‌ریزی صرفاً فرض داخلی IPPCS است؛ دادهٔ انبار واقعی وارد یا فراخوانی نمی‌شود.
3. از `/factory-master-data.html` دلایل توقف، دلایل قرنطینه، گروه‌های علت CAPA و نقش‌های مسئول پیگیری را مرور کنید.
4. از `/execution.html` دستور ساخت آزمایشی صادر و مسیر عملیات را ثبت کنید؛ وضعیت دستور و توقف/قرنطینه را ببینید.
5. از `/control-plan.html` با ویژگی‌های فرضی آزمایشی، کنترل پلن، ثبت نتیجه بازرسی، EOL و شجره سریال را مرور کنید. نتیجهٔ مصنوعی هرگز به‌عنوان سند کیفیت کارخانه استفاده نشود.
6. از `/production-deviations.html` پرونده اصلاحی با طبقه‌بندی مرجع بسازید و با نقش بازبین مستقل، گیت اثربخشی را بررسی کنید.
7. از `/planning-advanced.html` پیش‌بینی، S&OP، APS، What-if و هزینه تبدیل را آزمون کنید.

## ۸. آزمون خودکار نخستین اجرا در Visual Studio

پس از راه‌اندازی برنامه با F5، از PowerShell دیگری در ریشه مخزن اجرا کنید:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Test-IPPCS-FirstTest.ps1 -Username ippcs-local-admin
```

رمز تست محلی به صورت مخفی از شما درخواست می‌شود و ذخیره یا نمایش داده نمی‌شود. این فرمان فقط تست خواندنی است؛ نسخه و سلامت SQL، ۱۶ صفحه، ورود، مسیرهای ۱۸ ماژول و اطلاعات پایه را بررسی می‌کند. اگر `PASS` چاپ شد می‌توانید تست دستی سناریوی اول را از [بسته First UAT](FIRST_UAT_TEST_PACK.md) شروع کنید. **PASS این ابزار مجوز استقرار عملیاتی یا تأیید کاربران نیست.**

## ۹. آزمون اتوماتیک پیش از هر پذیرش

برای تست کد بدون دست‌زدن به DB اصلی:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Validate-IPPCS.ps1
```

این اسکریپت Build، xUnit، Node/Jalali و اختلاف مدل EF را آزمایش می‌کند؛ **هیچ Migration یا Reset انجام نمی‌دهد**. نتیجهٔ یک Commit سبز در GitHub Actions نیز فقط مربوط به SQL *موقت* Runner است و جایگزین اجرای Visual Studio یا UAT اپراتور نیست.

اگر در اجرای اولیه خطایی دیدید، متن دقیق خطا، نام فایل و شماره خط، خروجی `git rev-parse --short HEAD` و مرحلهٔ انجام‌شده را برای اصلاح ارسال کنید؛ نام حساب و رمز عبور را ارسال نکنید.

## ۱۰. چه زمانی ماژول نهایی محسوب می‌شود؟

هر یک از ۱۸ ماژول فقط پس از **Build/SQL سبز همان SHA + دادهٔ نمایندهٔ واقعی کارخانه + UAT با اپراتور و کارشناس + تأیید صاحب فرایند + طرح بکاپ/بازگشت** نهایی و قابل استقرار می‌شود. وضعیت فعلی نرم‌افزار «قابلیت‌های آزمایشی توسعه‌یافته» است و همهٔ تأییدهای میدانی باز هستند. چک‌لیست واقعی در `docs/FACTORY_UAT_RELEASE_CHECKLIST.md` قرار دارد.

**محدوده ممنوع:** تولید یا اتصال به انبار، خرید، فروش یا منابع انسانی؛ فقط اقلام محلی مستقل IPPCS مجاز هستند.
