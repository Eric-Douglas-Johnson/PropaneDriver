using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Client.HelperClasses
{
    // Labels a fuel log by when it ran, in the viewer's local time, for the
    // past-log lists on the Fuel Log and Admin pages.
    public static class FuelLogPeriodFormatter
    {
        public static string Format(FuelLogSummaryDto fuelLog)
        {
            var startedAt = ToLocal(fuelLog.StartedAt);
            if (fuelLog.CompletedAt is null)
                return $"{startedAt:MMM d, yyyy h:mm tt} – in progress";

            var completedAt = ToLocal(fuelLog.CompletedAt.Value);
            return startedAt.Date == completedAt.Date
                ? $"{startedAt:MMM d, yyyy}, {startedAt:h:mm tt} – {completedAt:h:mm tt}"
                : $"{startedAt:MMM d, yyyy h:mm tt} – {completedAt:MMM d, yyyy h:mm tt}";
        }

        // The server stores UTC but SQL hands it back without a Kind.
        private static DateTime ToLocal(DateTime utcTimestamp) =>
            DateTime.SpecifyKind(utcTimestamp, DateTimeKind.Utc).ToLocalTime();
    }
}
