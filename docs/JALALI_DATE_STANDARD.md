# IPPCS — Persian (Jalali) calendar contract

## Source of approach
Adapted from the reusable `jalali-datepicker.js` already present in the user's **EmployeePerformanceSystem** and **Supply-Chain-Payment-System** repositories. Unlike a one-off per-page widget, this is the central date input implementation for all 18 manufacturing modules.

## Mandatory UI markup
```html
<label>تاریخ
  <span class="jalali-field">
    <input name="dueDateJalali" data-jalali-date required placeholder="انتخاب تاریخ">
    <button class="date-trigger" data-jalali-trigger type="button">▣</button>
  </span>
</label>
```
Load `/js/jalali-datepicker.js` after `/css/production.css`. Existing inputs are registered on DOMContentLoaded. If a dynamic form is inserted, call `window.IPPCSJalali.init(parentElement)`.

## Correctness
- Inputs: `yyyy/MM/dd` Jalali; accepts Persian, Arabic-Indic, Latin numerals.
- Real Jalali picker: year/month selection, forward/back, today, clear, day selection, close on outside click/Escape.
- Client `Intl.DateTimeFormat('en-US-u-ca-persian-nu-latn')` fixed to UTC; all conversions checked by roundtrip; guards invalid months, dates and leap years.
- Server validation: `System.Globalization.PersianCalendar` using `PersianPlanningDates.TryParse`. Never trust the browser to validate dates.
- SQL persistence: SQL `date`/`datetime2` Gregorian; never duplicate Jalali year/month fields in a domain record.
- Period boundaries are inclusive date-only. Manufacturing timestamps require an explicit configurable plant timezone in later modules; do **not** repurpose UTC midnight as shift-local timestamp.
- Dates in Excel exports and reports must use the same formatter and timezone policy. Never parse a displayed Persian text as an ISO date.
- For real scheduling, store deadline date and plant timezone separately; the current `ProductionPlan.DueDateUtc` is **prototype debt** to be migrated to a date-only planning field.

## Tests
`node --test tests/jalali.test.cjs` and `dotnet test tests/IPPCS.Tests/IPPCS.Tests.csproj`.
Automated conversion tests check digits, invalid input, leap years, and every day in 1405.

## Limits
The widget's navigation is intentionally constrained to Jalali years 1200–1599. This is a UI range, not a database range. Cross-midnight shifts and timezone-aware APS are still future work.
