using DistributionSystem.Application.Exceptions;

namespace DistributionSystem.Application.Services.Implementations;

/// <summary>Pure validation rules for creating a SalesTarget, kept separate from RepService for unit testability.</summary>
public static class SalesTargetValidator
{
    /// <summary>A "Monthly" target must not span more than one calendar month. Existing historical rows are untouched.</summary>
    public static void ValidateTargetPeriod(string targetPeriod, DateTime startDate, DateTime endDate)
    {
        if (!string.Equals(targetPeriod, "Monthly", StringComparison.OrdinalIgnoreCase))
            return;

        if (startDate.Year != endDate.Year || startDate.Month != endDate.Month)
        {
            throw new BusinessException(
                $"A Monthly target must fall within a single calendar month. The selected range ({startDate:dd MMM yyyy} - {endDate:dd MMM yyyy}) spans more than one month.",
                "MONTHLY_TARGET_MULTIPLE_MONTHS");
        }
    }
}
