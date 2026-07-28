using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using FluentAssertions;

namespace DistributionSystem.UnitTests;

public class SalesTargetValidatorTests
{
    [Fact]
    public void MonthlyTarget_SpanningTwoCalendarMonths_IsRejected()
    {
        var act = () => SalesTargetValidator.ValidateTargetPeriod("Monthly", new DateTime(2026, 6, 1), new DateTime(2026, 7, 31));

        act.Should().Throw<BusinessException>()
            .Which.ErrorCode.Should().Be("MONTHLY_TARGET_MULTIPLE_MONTHS");
    }

    [Fact]
    public void MonthlyTarget_WithinSingleCalendarMonth_IsAccepted()
    {
        var act = () => SalesTargetValidator.ValidateTargetPeriod("Monthly", new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Quarterly")]
    [InlineData("Yearly")]
    public void NonMonthlyTarget_CanSpanMultipleMonths(string period)
    {
        var act = () => SalesTargetValidator.ValidateTargetPeriod(period, new DateTime(2026, 6, 1), new DateTime(2026, 8, 31));

        act.Should().NotThrow();
    }
}
