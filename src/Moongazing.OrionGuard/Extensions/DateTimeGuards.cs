namespace Moongazing.OrionGuard.Extensions
{
    /// <summary>
    /// Provides validation methods for DateTime values.
    /// </summary>
    public static class DateTimeGuards
    {
        public static void AgainstPastDate(this DateTime date, string parameterName)
        {
            if (date < DateTime.UtcNow)
            {
                throw new ArgumentException($"{parameterName} cannot be in the past.", parameterName);
            }
        }

        public static void AgainstFutureDate(this DateTime date, string parameterName)
        {
            if (date > DateTime.UtcNow)
            {
                throw new ArgumentException($"{parameterName} cannot be in the future.", parameterName);
            }
        }
        public static void AgainstDateRange(this DateTime date, DateTime startDate, DateTime endDate, string parameterName)
        {
            if (date < startDate || date > endDate)
            {
                throw new ArgumentException($"{parameterName} must be between {startDate} and {endDate}.", parameterName);
            }
        }

        public static void AgainstWeekend(this DateTime date, string parameterName)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            {
                throw new ArgumentException($"{parameterName} cannot be on a weekend.", parameterName);
            }
        }

        public static void AgainstTimeRange(this DateTime date, TimeSpan startTime, TimeSpan endTime, string parameterName)
        {
            var time = date.TimeOfDay;
            if (time < startTime || time > endTime)
            {
                throw new ArgumentException($"{parameterName} must be between {startTime} and {endTime}.", parameterName);
            }
        }
        public static void AgainstNonToday(this DateTime date, string parameterName)
        {
            if (date.Date != DateTime.UtcNow.Date)
            {
                throw new ArgumentException($"{parameterName} must be today's date.", parameterName);
            }
        }
        public static void AgainstUnrealisticBirthDate(this DateTime date, string parameterName)
        {
            var now = DateTime.UtcNow;
            var maxDate = now.AddYears(-120);
            if (date > now || date < maxDate)
            {
                throw new ArgumentException($"{parameterName} is not a realistic birth date.", parameterName);
            }
        }
        public static void AgainstFuturePeriod(this DateTime date, TimeSpan period, string parameterName)
        {
            if (date > DateTime.UtcNow.Add(period))
            {
                throw new ArgumentException($"{parameterName} cannot be beyond {period} from now.", parameterName);
            }
        }
        public static void AgainstSpecificDay(this DateTime date, DayOfWeek day, string parameterName)
        {
            if (date.DayOfWeek == day)
            {
                throw new ArgumentException($"{parameterName} cannot be on a {day}.", parameterName);
            }
        }


    }
}
