# IPPCS — ۱۸ دروازه پذیرش نهایی ماژول‌های کارخانه کمک‌فنر

**وضعیت سند: ۱۴۰۵/۰۸/۰۵ — در حال توسعه و آماده‌سازی آزمون کارخانه، نه مجوز استقرار عملیاتی.**

## تعریف «نهایی‌شده»
هر ماژول صرفاً در صورتی **نهایی** است که همه این پنج مدرک موجود باشند:
۱. سناریوی واقعی و معیار عددی مورد تأیید مسئول همان فرایند کارخانه؛
۲. فرایند و صفحه فارسی RTL همراه نقش و کنترل مجوز واقعی؛
۳. آزمون خودکار Build/EF/SQL Server شامل حالت سالم و ناسالم، بدون نیاز به بازسازی اطلاعات؛
۴. آزمون پذیرش کارخانه با کاربر واقعی، نمونه بچ/مدل و ثبت نتیجه؛
۵. تأیید مسئول فرایند و برنامه انتشار و بازگشت نسخه.

عبارت **«توسعه انجام شده»** در جدول به معنی اتمام دروازه‌های ۴ و ۵ نیست. تست SQL در CI پایگاه موقت دارد و هیچ آزمون فیزیکی خط، ابزار اندازه‌گیری یا داده ERP انجام نمی‌دهد.

| ردیف | ماژول | شواهد پیاده‌سازی فعلی | شرط باقی‌مانده برای پایان نهایی |
|---|---|---|---|
| ۰۱ | اطلاعات پایه، BOM و Routing | تأیید نسخه با قفل هم‌زمانی، جلوگیری از چرخه BOM و تداخل بازه | چرخه تغییر مهندسی ECO، صاحبان امضا و آزمون انواع کمک‌فنر |
| ۰۲ | تقاضا و پیش‌بینی | تقاضای مستقل، پیش‌بینی ماه شمسی، Backtest و WAPE | سیاست پذیرش دقت، سناریوهای فصلی و کنترل نسخه پیش‌بینی |
| ۰۳ | S&OP | گزارش تراز بار/تقاضا و تطبیق تصویر تاریخی با MPS | فرایند تصویب جلسات، بازنگری سناریو و توافق ظرفیت |
| ۰۴ | MPS | نسخه مصوب، ردیف‌های تاریخ‌دار و ممیزی ارجاع به تقاضا | انجماد برنامه، افق انتشار، ثبت علت تغییر و امضای مسئول |
| ۰۵ | MRP | انفجار BOM چندسطحی، خالص‌سازی و منشأ فرضیات برنامه | کنترل مواد جایگزین، روزهای کاری تأمین و تعهدپذیری فرضیات داخلی |
| ۰۶ | CRP | نیاز ظرفیت شیفت/مرکز، اضافه‌بار و رزرو محدود ماشین | شیفت‌های عبوری از نیمه‌شب، تقویم تعمیرات مصوب و منابع ابزار |
| ۰۷ | APS | تخصیص محدود بدون همپوشانی، سناریو و تابلوی اعزام | برنامه تأییدشده چندمنبعی/تغییر ابزار و آزمون پایداری خط |
| ۰۸ | مدیریت دستور ساخت | آزادسازی مشروط، اصلاح پیش‌نویس با ثبت علت، تاریخچه حالات | اختیار امضا و فرایند اصلاح دستور پس از آزادسازی |
| ۰۹ | تولید نیمه‌ساخته | عملکرد مراحل، QC/WIP و ممیزی پیوند مستند بچ‌های متوالی | انتقال بین ایستگاه‌ها، اصلاح اختلاط بچ و آزمون اپراتوری |
| ۱۰ | مونتاژ نهایی | منشأ نیمه‌ساخته به ازای بچ و BOM ثابت و گیت EOL داخلی | تنوع مدل، شماره سریال و آزمون پایان خط اختصاصی هر نوع |
| ۱۱ | مصرف مواد پای خط | مصرف داخلی، انتساب واقعی بچ و سند اصلاح برگشت‌ناپذیر | جانشینی مجاز، مصرف مرحله‌ای و واکنش به مغایرت استاندارد |
| ۱۲ | پایش برنامه/انحراف | انطباق MPS، علت، اقدام، بازنگری سابقه و تشدید پیگیری | تأیید مستقل اثربخشی اقدام و شاخص مهلت پاسخ |
| ۱۳ | کیفیت حین ساخت | کنترل پلن مصوب، ثبت مقادیر، قرنطینه و گیت EOL با امضا | MSA، کالیبراسیون، نمونه‌برداری و حدود کنترل SPC مصوب |
| ۱۴ | OEE | زمان واقعی کار، توقف‌ها و عملکرد/کیفیت روز | صحت‌سنجی همپوشانی توقف‌ها، شیفت واقعی و داده تجهیز |
| ۱۵ | شجره بچ | ارتباط گام‌ها، نیمه‌ساخته تا مونتاژ و ردیابی دوطرفه | شناسه سریال، اصلاح شجره با امضا و تمرین فراخوان محدود |
| ۱۶ | What-if | مقایسه ظرفیت، افق روزانه و سناریوی عدم‌دسترسی مرکز | سناریوی مواد/ابزار/تغییر مسیر و سیاست انتخاب سناریو |
| ۱۷ | هزینه تبدیل | نرخ داخلی مرکز و انحراف تبدیل واقعی به استاندارد | تاریخچه نرخ و تخصیص سربار مورد تأیید حسابداری (بدون ایجاد ارتباط نرم‌افزاری) |
| ۱۸ | داشبورد | KPI تولید، دیرکرد، قرنطینه، پیگیری و لینک به پرونده | حدود هدف مصوب، دسترسی نقش‌محور، خروجی و آزمون کاربران |

## اولویت رفع گلوگاه‌های باقی‌مانده
۱. **گیت‌های خط تولید:** سناریوهای واقعی نیمه‌ساخته و مونتاژ، جداول آزمون EOL، ثبت شماره سریال داخلی در صورت الزام کارخانه.
۲. **برنامه‌ریزی قابل اتکا:** شیفت‌ها/تقویم مصوب و تصمیم تغییر MPS پس از انتشار، سناریوهای واقعی گلوگاه.
۳. **کنترل کیفیت رسمی:** MSA، مشخصه/فرکانس اندازه‌گیری برای کمک‌فنر و اختیارات تصمیم‌گیری قرنطینه.
۴. **امنیت و انتشار:** تست بار و رقابت ثبت، نسخه پشتیبان/بازیابی SQL Server، UAT با نقش‌ها و انتشار آزمایشی روی IIS.

این سند با هر Stage اصلاح می‌شود؛ جایگزین تأیید مسئول خط یا مشاهده میدانی نیست.

## مرز قطعی پروژه
هیچ ماژول انبار، خرید، فروش یا منابع انسانی تولید نمی‌شود؛ هیچ API، Job، View، Trigger یا ارتباطی با آن سامانه‌ها نباید ایجاد شود. فقط در صورت ارائه و تأیید صریح کارفرما می‌توان در آینده خواندن اطلاعات پایه کالا از یک View فقط‌خواندنی را بررسی کرد.

شاخه هدف `feature/production-platform-foundation` و Draft PR شماره ۱ است؛ تا تکمیل مراحل فوق هیچ Merge به `main` انجام نمی‌شود.

## Stage 17 addendum — governed MPS change lifecycle
The production program (04) has a controlled-copy revision path: approved source is never edited, one open Draft per original is enforced, approved action freezes actor and all program lines. Approving the revision captures actual recalculated MRP (05) and CRP (06) differences in immutable SQL JSON evidence and fails if the planning model is incomplete. The revised program remains an **internal** factory planning output until the S&OP/MPS owner formally authorizes its use. This stage supplies testable software evidence, **not** a signed factory planning change-control SOP.

## Stage 18 addendum — engineering and finished serial evidence
**Module 01** now offers an explicit engineering impact preflight (BOM part ratios/alternatives not yet authorized, routing center/cycle time and affected frozen work orders), but a signed change order/ECO release authority and controlled retirement/cutover are **still required** before final signoff.

**Module 15** now supports local, globally unique serial registration per *whole good finished unit* after internal EOL QA, with bounded reverse batch genealogy and no hidden stock/shipment assertion. Actual label issuance, scanning/serialization policy, customer fitment identifiers and recall drill remain unapproved/untested in a physical factory. The presence of the code and a green SQL CI is not permission to call serial recall ready.

## Stage 19: OEE and conversion rate gate update
**14 — OEE:** time-stamped manual start/end center downtime now shares locks with actual operation clocks; reconciliation includes closed operating time and independently timed stoppage with no double subtraction, and flags ambiguous legacy duration-only stops. Missing: real operator/shift confirmation, machine-time source verification, downtime taxonomy and plant-wide trial.

**17 — internal conversion cost:** factory standard rate is now captured per operation on release as immutable evidence, and subsequent center rate updates cannot silently change past orders; legacy orders without frozen evidence are explicitly marked historically unverified. Missing: history and authorization for finance-approved standard rate setting, depreciation/overhead policy, plant cost controller signoff and trial.

**Testing:** GitHub Actions runs associated with Stage19 failed immediately before normal build/test execution and the GitHub logs download reported BlobNotFound. Until exact-head build, unit/JS and authenticated disposable SQL E2E all succeed, this stage's code must not be deployed.

## Stage 20 addendum — night shift signoff
- **03 / 06 S&OP / CRP:** local-date center capacity includes carryover from the preceding night's shift and correct Saturday/Sunday boundary; production planner must certify weekly resource availability, exceptions and proposed breaks.
- **07 APS:** nonpreemptive overnight operations and all touched-day occupancy are calculated under versioned `FINITE-GREEDY-V2-NIGHT`; plant scenario owners must verify midnight, boundary blackouts, concurrent machine loads and workcenter downtime calendars.
- **08 Dispatch:** center reservations and immutable audit can cross midnight, including a displayed ending Jalali date; representative physical multi-shift operator UAT remains uncompleted.
- **CI:** test files exist but GitHub Actions still terminate in seconds with `steps:null`, `logs_url:null`. Until a successful exact-head Build/xUnit/JS/SQL run appears, Stage19–20 remain **untested in CI** and must not be deployed. No ERP links, schema reset, or main merge.


## مراحل ۲۱ و ۲۲ — پیشرفت گیت مهندسی و دستور ساخت (کد ثبت‌شده، CI هنوز نامعتبر)
- **ماژول‌های ۰۱ و ۰۸:** انتقال گروهی و صریح مرجع BOM/Routing برای حداکثر ۱۰۰ دستور *پیش‌نویس و بدون عملیات* همراه پیش‌نمایش مستقل، علت، ثبت تاریخچه و تراکنش اتمیک با قفل SQL. دستورات آزادشده یا در حال تولید تغییر نمی‌کنند. نسخه جایگزین باید مصوب و از تاریخ شروع هر دستور معتبر باشد.
- **برش تاریخ اعتبار مهندسی:** پیش‌نمایش و ثبت مرجع تأیید با تاریخ شمسی؛ کوتاه‌سازی زمان اعتبار نسخه قبلی به روز قبل و تصویب نسخه پیشنهادی دقیقاً در تاریخ برش، به‌صورت یک تراکنش همراه با جدول مستقل و افزایشی `FactoryEngineeringCutoverEvents`. فرایند از تصویب عادی نسخه‌های دارای همپوشانی جدا است؛ تداخل با نسخه سوم، چرخه BOM، تاریخ گذشته و وضعیت نسخه نادرست رد می‌شود.
- **عدم حذف/بازنویسی تاریخ:** تمام دستورات قبلاً آزادشده، حتی اگر شروع برنامه‌ای آنها پس از تاریخ برش باشد، ارجاع قبلی خود را حفظ می‌کنند. دستورات پیش‌نویس با ارجاع قبلی نیازمند بررسی و انتقال مجزا هستند. سوابق و داده ERP دست‌نخورده‌اند.
- **مجوز:** ایجاد، ویرایش و تصویب BOM/Routing و عملیات MPS/MRP و فرضیات داخلی برنامه‌ریزی به `IpPlanningWrite` (Planner/Administrator) محدود شدند. برنامه‌ریزی و اجرای کارخانه هنوز تفکیک امضای ECO چندنقشی ندارد.
- **آزمون‌های اضافه‌شده، نه موفق اعلام‌شده:** `FactoryStageTwentyOneDraftRebindTests` و `FactoryStageTwentyTwoCutoverTests`، `stage21_engineering_rebind_checks.py` و `stage22_engineering_cutover_checks.py` در زنجیره CI موقت SQL. سناریوی مثبت مسیر ساخت و BOM و سناریوی منفی انتقال و عدم تغییر دستور آزادشده لحاظ شده‌اند.
- **مسدودکنندهٔ نهایی:** GitHub Actions پس از مرحلهٔ ۱۸ در مدت چند ثانیه و بدون `steps` و لاگ معتبر قطع می‌شود؛ build/xUnit/EF و SQL برای مراحل ۱۹ تا ۲۲ **تأیید نشده‌اند**. قبل از استقرار باید `dotnet build`، `dotnet test`، `dotnet ef migrations has-pending-model-changes`، مهاجرت در SQL موقت و UAT کارخانه با موفقیت اجرا شوند.
- **هنوز باقی است:** گردش شماره ECO با هویت صاحب‌امضا و دو امضای مجزا، خط‌مشی اجازه برش، بازبینی برنامه‌های مرتبط و MPS/CRP/APS پس از برش، معیارهای تایید مدل واقعی کمک‌فنر، کنترل مستند بازگشت نسخه و امضای نهایی ۱۸ ماژول.

دروازهٔ انتشار همچنان **بسته** است. صرف درج مرجع تأیید توسط یک برنامه‌ریز به‌معنای امضای نهایی مهندسی یا مجوز تغییر تجهیزات خط نیست.


## مراحل ۲۳ و ۲۴ — اثربخشی مستقل CAPA و تفکیک وظایف دستور تولید
- **ماژول ۱۲:** اختتام اولیه اقدام اصلاحی اکنون از «تأیید مستقل اثربخشی» جدا شده است. بازبین با نقش `IpQualityApprove` که ایجادکننده، تعیین‌کننده/بازنگر اقدام، مسئول ثبت‌شده یا مختومه‌کننده پرونده نبوده است می‌تواند نتیجه را با شناسه مدرک، مشاهده واقعی و نام خود ثبت کند. نتیجه تأیید در تاریخچه `ProductionDeviationEvents` ذخیره می‌شود؛ رد اثربخشی پرونده را به `ActionAssigned` بازمی‌گرداند و شواهد رد قبلی حفظ می‌شود. بازتأیید بعدی فقط پس از بازنگری کنترل‌شده اقدام و اختتام مجدد قابل بررسی است.
- **ماژول‌های ۰۸ و ۰۹:** ساخت/آزادسازی/لغو دستور ساخت و ازسرگیری دستور معلق اکنون مجوز `IpPlanningWrite` دارند؛ ثبت تولید و توقف مدت‌دار `IpShopFloorWrite`، نگهداشت توقف ایمن `IpProductionHold` و ایجاد قرنطینه اولیه `IpQualityHoldRaise` است. اپراتور نمی‌تواند برنامه یا دستور را تأیید کند؛ بازرس کیفیت می‌تواند عدم‌انطباق را اعلام کند ولی برنامه جدید صادر نمی‌کند.
- **ماژول ۱۳:** فقط بازرس کیفیت یا مدیر با `IpQualityApprove` تعیین تکلیف قرنطینه را انجام می‌دهد؛ مجوز اعلام قرنطینه از تصویب آن جدا است.
- **تاریخچه و داده:** از جداول و وضعیت‌های موجود استفاده شده است؛ مرحله ۲۳/۲۴ به بازسازی DB نیاز ندارد. هیچ ماژول یا ارتباط انبار/خرید/فروش/منابع انسانی ایجاد نشده است.
- **آزمون‌های اضافه‌شده، نه موفق اعلام‌شده:** `FactoryStageTwentyThreeEffectivenessTests.cs`، `stage23_deviation_effectiveness_checks.py` با دو کاربر مستقل کیفیت و بررسی نقش‌های ممنوع، و آزمون‌های مجوز اپراتور در `production_e2e_smoke.py`. مرحله‌های ۲۳ و ۲۴ در زنجیره SQL موقت قرار دارند. آزمون‌های قدیمی اختتام مرحله ۱۵ معتبرند اما اختتام دیگر به معنی تأیید مستقل نیست.
- **الگوی UAT:** `docs/FACTORY_UAT_RELEASE_CHECKLIST.md` شواهد لازم و سناریوهای کارخانه برای هر ۱۸ ماژول را تعریف می‌کند. تمام ۱۸ تأیید میدانی هنوز **باز** هستند.
- **CI و استقرار:** اجراهای GitHub Actions در سطح Job و بدون Step و لاگ قابل دریافت شکست می‌خورند؛ مرحله‌های ۱۹ تا ۲۴ بیلد و تست موفق تأییدشده ندارند. تا رفع این مانع، گیت نهایی ۱۸ ماژول همچنان بسته است. مرحلهٔ بعد باید بررسی runner/حساب GitHub و اجرای مجدد Build+xUnit+JS+EF+SQL در SHA واحد و سپس UAT کارخانه باشد.
