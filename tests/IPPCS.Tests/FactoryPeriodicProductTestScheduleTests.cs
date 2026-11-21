using Xunit;

public sealed class FactoryPeriodicProductTestScheduleTests
{
    private static DateOnly J(string value)
    {
        Assert.True(PersianPlanningDates.TryParse(value, out var date), "Invalid test Jalali date: " + value);
        return date;
    }

    [Fact]
    public void Annual_tests_repeat_on_the_original_Jalali_anniversary_and_respect_valid_through()
    {
        var plan = new FactoryPeriodicTestPlan
        {
            Code = "DURABILITY-ANNUAL",
            Cadence = FactoryPeriodicTestCadence.Annual,
            IntervalMonths = 12,
            FirstDueOn = J("1404/07/15"),
            ValidThrough = J("1407/07/14"),
            IsActive = true
        };

        var dates = FactoryPeriodicTestRules.DueDates(plan, J("1405/01/01"), J("1407/12/29"));

        Assert.Equal(new[] { J("1405/07/15"), J("1406/07/15") }, dates);
    }

    [Fact]
    public void Periodic_tests_calculate_each_month_end_from_the_original_anchor()
    {
        var plan = new FactoryPeriodicTestPlan
        {
            Code = "DAMPING-FORCE-QTR",
            Cadence = FactoryPeriodicTestCadence.Periodic,
            IntervalMonths = 3,
            FirstDueOn = J("1405/01/31"),
            IsActive = true
        };

        var dates = FactoryPeriodicTestRules.DueDates(plan, J("1405/01/01"), J("1405/10/30"));

        Assert.Equal(new[]
        {
            J("1405/01/31"), J("1405/04/31"), J("1405/07/30"), J("1405/10/30")
        }, dates);
    }

    [Fact]
    public void Inactive_or_expired_test_plans_do_not_generate_due_dates()
    {
        var plan = new FactoryPeriodicTestPlan
        {
            Cadence = FactoryPeriodicTestCadence.Periodic,
            IntervalMonths = 1,
            FirstDueOn = J("1405/01/01"),
            ValidThrough = J("1405/03/01"),
            IsActive = false
        };

        Assert.Empty(FactoryPeriodicTestRules.DueDates(plan, J("1405/01/01"), J("1405/03/01")));
        plan.IsActive = true;
        Assert.Empty(FactoryPeriodicTestRules.DueDates(plan, J("1405/04/01"), J("1405/06/01")));
    }

    [Fact]
    public void Annual_cadence_must_use_twelve_months_and_periodic_cadence_must_be_shorter()
    {
        var plan = new FactoryPeriodicTestPlan
        {
            Cadence = FactoryPeriodicTestCadence.Annual,
            IntervalMonths = 6,
            FirstDueOn = J("1405/01/01"),
            IsActive = true
        };
        Assert.Throws<InvalidOperationException>(() =>
            FactoryPeriodicTestRules.DueDates(plan, J("1405/01/01"), J("1405/12/29")));
        plan.Cadence = FactoryPeriodicTestCadence.Periodic;
        plan.IntervalMonths = 12;
        Assert.Throws<InvalidOperationException>(() =>
            FactoryPeriodicTestRules.DueDates(plan, J("1405/01/01"), J("1405/12/29")));
    }

    [Fact]
    public void A_test_outside_numeric_limits_cannot_be_recorded_as_passed()
    {
        var plan = new FactoryPeriodicTestPlan { Minimum = 50m, Maximum = 80m };
        Assert.NotNull(FactoryPeriodicTestRules.ValidateOutcome(
            plan, 81m, FactoryPeriodicTestOutcome.Passed, null));
        Assert.Null(FactoryPeriodicTestRules.ValidateOutcome(
            plan, 81m, FactoryPeriodicTestOutcome.Failed, "خروج از حد بالای مشخصات"));
    }

    [Fact]
    public void Numeric_tests_cannot_pass_without_measurement_and_failed_results_need_auditable_notes()
    {
        var plan = new FactoryPeriodicTestPlan { Minimum = 0m, Unit = "N" };
        Assert.NotNull(FactoryPeriodicTestRules.ValidateOutcome(
            plan, null, FactoryPeriodicTestOutcome.Passed, null));
        Assert.NotNull(FactoryPeriodicTestRules.ValidateOutcome(
            plan, 20m, FactoryPeriodicTestOutcome.Failed, null));
        Assert.NotNull(FactoryPeriodicTestRules.ValidateOutcome(
            plan, 20m, FactoryPeriodicTestOutcome.Inconclusive, ""));
        Assert.Null(FactoryPeriodicTestRules.ValidateOutcome(
            plan, 20m, FactoryPeriodicTestOutcome.Passed, null));
    }

    [Fact]
    public void Qualitative_tests_can_pass_without_a_numeric_value_but_failed_results_require_notes()
    {
        var plan = new FactoryPeriodicTestPlan
        {
            AcceptanceCriteria = "No visible oil leakage",
            Minimum = null,
            Maximum = null
        };
        Assert.Null(FactoryPeriodicTestRules.ValidateOutcome(
            plan, null, FactoryPeriodicTestOutcome.Passed, null));
        Assert.NotNull(FactoryPeriodicTestRules.ValidateOutcome(
            plan, null, FactoryPeriodicTestOutcome.Failed, null));
        Assert.Null(FactoryPeriodicTestRules.ValidateOutcome(
            plan, null, FactoryPeriodicTestOutcome.Failed, "نشت روغن مشاهده شد"));
    }
}
