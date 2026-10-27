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


## مرحله ۲۵ — هم‌زمانی دستور، توقف کیفیت، هویت و هشدار اثربخشی (کد ثبت‌شده؛ CI هنوز نامعتبر)
- **ماژول ۰۸ و ۰۹:** `POST /manufacturing-orders/{id}/hold` و `.../resume` اکنون همراه `Serializable transaction` و همان `LockOrder` عملیات آزادسازی، رزرو، ثبت تولید و لغو عمل می‌کنند. توقف مجدد و ادامه دادن از وضعیت نامعتبر رد می‌شوند و تاریخچه قبلی پاک نمی‌شود.
- **ماژول ۱۳:** دستور معلق با مبدأ فعال می‌تواند پس از توقف ایمن اپراتور هنوز قرنطینه تولید ثبت کند. ازسرگیری توسط برنامه‌ریز تا تعیین تکلیف آخرین قرنطینه باز مسدود است. هر دو ترتیب ثبت «قرنطینه→توقف» و «توقف→قرنطینه» در سناریوی SQL `stage25_order_hold_interlock_checks.py` اضافه شده‌اند.
- **ماژول ۱۲ و ۱۸:** مختومه شدن اقدام اصلاحی دیگر باعث حذف خودکار هشدار نمی‌شود. `DeviationEffectivenessPending` برای پرونده‌های مختومه بدون اثربخشی مستقل در `factory-kpis/production-alerts` نمایش داده می‌شود؛ سابقه مخدوش با اولویت بحرانی مشخص می‌شود. پس از تأیید مستقل معتبر، هشدار رفع می‌شود. آزمون‌های xUnit و SQL قدیمی مطابق منطق جدید اصلاح شده‌اند.
- **امنیت و رابط فارسی:** کد ۴۰۱ برای نشست منقضی‌شده موجب ورود مجدد می‌شود؛ کد ۴۰۳ به معنای نقش فاقد مجوز است و نباید کاربر واردشده را به لاگین پرتاب کند. `/auth/me` فقط نقش‌های همان کاربر را برمی‌گرداند. اقدامات دستورات ساخت و گردش انحرافات با نقش‌های برنامه‌ریز، اپراتور و بازرس کیفیت در رابط نمایش/مخفی می‌شوند. مجوز معتبر همواره در API کنترل می‌شود.
- **کنترل کامپایل محلی بدون دستکاری SQL:** `scripts/Validate-IPPCS.ps1` برای اجرای Restore، Build، xUnit، بررسی نحوی تمام JavaScriptها، آزمون تقویم شمسی و مقایسه فقط‌خواندنی EF Model/Snapshot اضافه شد. اجرای آن هیچ مهاجرت دیتابیس، حذف اطلاعات یا اتصال ERP انجام نمی‌دهد؛ SQL disposable E2E، UAT، امنیت IIS و آزمون بازیابی پشتیبان همچنان ضروری است.
- **وضعیت نهایی:** مرحله ۲۵ در شاخه توسعه ثبت شده، ولی هیچ تأیید موفق Build/xUnit/SQL برای این SHA در اختیار نداریم. تمام ۱۸ ماژول تا اجرای CI و سناریوهای کارخانه **گیت پذیرش باز** دارند. `main` بدون تغییر است؛ ادغام و استقرار ممنوع.


## تشخیص قطعی خطای GitHub Actions — توقف به علت حساب و صورتحساب
- متن خطا در رابط GitHub: `The job was not started because recent account payments have failed or your spending limit needs to be increased.`
- این خطا **پیش از ورود Runner به مراحل Build/Test** رخ می‌دهد؛ بنابراین Failureهای اخیر شواهد خطای کامپایل C#، خرابی Migration یا شکست آزمون SQL نیستند. موفقیت این آزمون‌ها نیز هنوز اثبات نشده است.
- مالک حساب باید از GitHub → Settings → Billing & Licensing ابتدا `Payment information` و `Payment history` و سپس `Budgets and alerts` برای GitHub Actions را بررسی کند. بدون اجازه مالک حساب نباید بودجه یا روش پرداخت تغییر کند.
- پس از رفع محدودیت، همهٔ Workflowهای Build و SQL روی SHA واحد دوباره اجرا و نتیجه لاگ‌های واقعی ثبت شود. اگر صورتحساب سالم باشد ولی قفل ادامه یابد، درخواست بررسی قفل Billing در GitHub Support ثبت شود.
- برای آزمون محلی غیرمخرب در Visual Studio از `scripts/Validate-IPPCS.ps1` استفاده شود؛ این کار جایگزین SQL disposable E2E و UAT نهایی نیست.


## مرحله ۲۶ — ادامه توسعه بدون شارژ GitHub Actions

**واقعیت:** خطای `The job was not started because recent account payments have failed or your spending limit needs to be increased` قبل از اجرای Build رخ می‌دهد و ربط مستقیم اثبات‌شده‌ای به شکست کد ندارد. Free بودن حساب لزوماً مانع قفل‌شدن Actions به علت مجوز پرداخت یا بودجه نیست.

**فرایند جایگزین بدون استفاده از GitHub Actions:**
1. توسعه کد و آزمون‌ها در `feature/production-platform-foundation`، با نگهداری `main` بدون تغییر و `PR #1` به صورت Draft.
2. روی Windows با Visual Studio، .NET SDK 10، `dotnet-ef` 10، Node.js، Python 3، Docker Desktop در حالت Linux Containers، از ریشه پروژه اجرا شود:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\Validate-IPPCS.ps1`: Restore/Build/xUnit/JS/Jalali/EF-check بدون بازسازی دیتابیس.
   - `powershell -ExecutionPolicy Bypass -File .\scripts\Test-IPPCS-DisposableSql.ps1`: Build و unit/JS، ایجاد SQL Server 2022 توسعه‌ای در Docker با نام تصادفی، ایجاد دیتابیس تصادفی، Migration فقط همان دیتابیس، host محلی `127.0.0.1:5295`، تست کامل `tests/production_e2e_smoke.py` شامل Stages 11–25، توقف host و حذف Container در `finally`.
3. محدودیت مهم: دو پورت محلی `11433` برای SQL و `5295` برای وب باید آزاد باشند. برای عدم تداخل، می‌توان از `-SqlPort` استفاده کرد؛ تست Stage23 هنوز نشانی `5295` را مبنا می‌گیرد، پس `-WebPort` را تغییر ندهید مگر آن تست نیز اصلاح شود.
4. خروجی اجرای SQL، خطاهای Build و گزارش نتیجه دقیق Commit باید از دستگاه توسعه دریافت و در PR ثبت شود. تا دریافت شواهد اجرای واقعی، این اسکریپت صرفاً **آمادهٔ اجرا** است، نه «تست‌شده و تأییدشده».
5. نه SQL واقعی کارخانه و نه دیتابیس موجود کاربر برای تست، migration، reset یا import استفاده نشود. هیچ یک از ماژول‌های انبار، خرید، فروش یا HR توسعه یا متصل نشوند.
6. جایگزین کم‌اولویت: self-hosted GitHub runner از نظر دقیقه اجرای سرویس رایگان است، اما اگر خود حساب GitHub Actions در حالت قفل Billing باشد ممکن است Workflow حتی روی آن شروع نشود؛ بنابراین تست محلیِ مستقل مسیر اصلی است.
7. شرایط نهایی‌سازی تغییر نکرده: موفقیت Build، EF schema، SQL E2E، احراز هویت و نقش‌ها، بازیابی بکاپ، IIS و UAT میدانی هر ۱۸ ماژول. ادغام با `main` تا آن زمان انجام نشود.


## مرحله ۲۷ — یک اجرای متمرکز به‌جای چندین Workflow برای هر Commit

- `.github/workflows/build.yml` به **IPPCS consolidated acceptance** تبدیل شد. فقط در `pull_request` به `main` یا `workflow_dispatch` اجرا می‌شود و دو Job شامل Build/xUnit/Node/Jalali/EF و SQL Server 2022 disposable E2E را در یک Workflow انجام می‌دهد.
- `concurrency.group` بر اساس شناسه PR/ref است و `cancel-in-progress: true` دارد؛ با شروع PR جدید اجرای قدیمی همان دسته لغو می‌شود، پس از پایان مجموعه Commitها نتیجه آخرین SHA معتبر است. Jobهای SQL و Build مستقل می‌مانند تا هر دو اشکال را در یک اجرا گزارش کنند.
- `.github/workflows/sql-schema.yml` اکنون صرفاً `workflow_dispatch` پشتیبان است و دیگر با هر Push به شاخه توسعه تکرار نمی‌شود.
- **ماژول‌های ۷، ۸، ۹، ۱۳، ۱۴، ۱۸:** تابلوی اعزام تولید اکنون سفارش `OnHold` را حتی در نبود مانع ظرفیت یا کیفیت، آماده شروع نمی‌شمارد. تست واحد Stage14 وضعیت درست KPI و دستور معلق را بررسی می‌کند.
- **نتایج قبلی قابل اتکا:** SHA `cfe3d2429e88e40bca70c50d91a6400ac8dd55ea` قبل از تغییر Workflow دارای Build بدون هشدار، ۱۸۱/۱۸۱ آزمون و SQL E2E موفق بوده است. **تأیید مرحله ۲۷ فقط پس از پایان موفق Jobهای جدیدِ SHA نهایی مجاز است.**
- پذیرش ۱۸ ماژول همچنان علاوه بر CI نیازمند بررسی فیزیکی کارخانه، تأیید کارفرما، امنیت استقرار و داده واقعی است. `main` و محدوده عدم اتصال به انبار/خرید/فروش/منابع انسانی حفظ شود.


## مرحله ۳۰ — تقویت گیت‌های عملیاتی مرتبط (ماژول‌های ۱، ۸، ۹، ۱۰، ۱۳، ۱۴، ۱۵ و ۱۸)

- **انتقال بچ بین مراحل ۸/۹/۱۰/۱۵:** جمع پیوندهای منبع با خروجی تأییدپذیر عملیات و ظرفیت هر بچ مبدأ مقایسه می‌شود؛ جمع مقصد نیز نباید از خروجی هر بچ مقصد بیشتر شود. `handoff-audit` اکنون `pairsWithOverAllocation` و جزئیات `overAllocatedUpstreamGood`/`overAllocatedGood` را نشان می‌دهد. وضعیت `QualityGatePassed` در صورت مغایرت **false** است.
- **کیفیت پایان خط ۱۰/۱۳:** گواهی EOL با منشأ بیش‌ثبت‌شده به‌طور مشخص `OverAllocatedStageHandoff` و `UndocumentedStageHandoff` برمی‌گرداند. برای سند قبلاً ثبت‌شده، تکراری‌بودن رکورد یا ناسازگاری `Lots`/`Good` با بچ‌های واقعی `InvalidFinalCertificate` است.
- **ردیابی سریال ۱۵:** هم در زمان تخصیص سریال و هم هنگام بازسازی دفتر سریال، وجود یک سند داخلی EOL کافی نیست؛ یک سند یکتا، خوش‌ساخت و منطبق با تعداد بچ‌های آخرین عملیات و خروجی سالم الزامی است. تاریخچه ناسازگار **fail closed** می‌شود، نه این‌که گواهی مشتری یا موجودی انبار از آن استنباط شود.
- **کنترل پلن ۱۳:** قفل تصویب سراسری SQL تراکنشی برای جلوگیری از تصویب هم‌زمان کنترل پلن‌های متداخل، ضرورت مسیر مصوب و مطابقت تاریخ اعتبار/مشخصه‌ها با عملیات مسیر کنترل می‌شود. برنامه آزمایشی متداخل و تصویب زودهنگام مسیر تأییدنشده در SQL E2E رد می‌شوند.
- **OEE و گزارش‌های ۱۴/۱۸:** داده منفی تولید یا زمان سیکل استاندارد نامعلوم/صفر دیگر OEE عددی ساختگی نمی‌سازد. آزمون‌های xUnit و E2E در همان Workflow تجمیعی باقی می‌مانند.
- **مرز نهایی‌سازی:** این تغییرات کنترل‌های منطقی/SQL هستند، نه تأیید فیزیکی پایان خط، ردیابی برچسب، امضای دیجیتال دو‌نفره ECO، تأیید SPC/MSA یا UAT کارخانه. PR تا عبور گیت‌های کارخانه **Draft** می‌ماند.
