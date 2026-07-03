using NUnit.Framework;
using Nornis.Application.Services;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class TimePeriodCalculatorTests
{
    [TestFixture]
    public class GetTodayRangeTests
    {
        [Test]
        public void Start_IsMidnightUtcOfCurrentDate()
        {
            var (start, _) = TimePeriodCalculator.GetTodayRange();

            Assert.Multiple(() =>
            {
                Assert.That(start.Hour, Is.EqualTo(0));
                Assert.That(start.Minute, Is.EqualTo(0));
                Assert.That(start.Second, Is.EqualTo(0));
                Assert.That(start.Millisecond, Is.EqualTo(0));
                Assert.That(start.Offset, Is.EqualTo(TimeSpan.Zero));
                Assert.That(start.Date, Is.EqualTo(DateTimeOffset.UtcNow.Date));
            });
        }

        [Test]
        public void End_IsAtOrAfterStart()
        {
            var (start, end) = TimePeriodCalculator.GetTodayRange();

            Assert.That(end, Is.GreaterThanOrEqualTo(start));
        }

        [Test]
        public void End_HasUtcOffset()
        {
            var (_, end) = TimePeriodCalculator.GetTodayRange();

            Assert.That(end.Offset, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void Start_TimeOfDay_IsZero()
        {
            var (start, _) = TimePeriodCalculator.GetTodayRange();

            Assert.That(start.TimeOfDay, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [TestFixture]
    public class GetThisWeekRangeTests
    {
        [Test]
        public void Start_DayOfWeek_IsAlwaysMonday()
        {
            var (start, _) = TimePeriodCalculator.GetThisWeekRange();

            Assert.That(start.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        }

        [Test]
        public void Start_IsMidnightUtc()
        {
            var (start, _) = TimePeriodCalculator.GetThisWeekRange();

            Assert.Multiple(() =>
            {
                Assert.That(start.TimeOfDay, Is.EqualTo(TimeSpan.Zero));
                Assert.That(start.Offset, Is.EqualTo(TimeSpan.Zero));
            });
        }

        [Test]
        public void End_IsAtOrAfterStart()
        {
            var (start, end) = TimePeriodCalculator.GetThisWeekRange();

            Assert.That(end, Is.GreaterThanOrEqualTo(start));
        }

        [Test]
        public void Start_IsWithinSevenDaysOfToday()
        {
            var (start, _) = TimePeriodCalculator.GetThisWeekRange();
            var today = DateTimeOffset.UtcNow.Date;

            var daysDifference = (today - start.Date).Days;

            Assert.That(daysDifference, Is.InRange(0, 6));
        }

        [Test]
        public void WhenTodayIsMonday_Start_IsToday()
        {
            // This test verifies the structural property:
            // if today happens to be Monday, start should be today's midnight
            var (start, _) = TimePeriodCalculator.GetThisWeekRange();
            var today = DateTimeOffset.UtcNow;

            if (today.DayOfWeek == DayOfWeek.Monday)
            {
                Assert.That(start.Date, Is.EqualTo(today.Date));
            }
            else
            {
                // When not Monday, start is strictly before today
                Assert.That(start.Date, Is.LessThan(today.Date));
            }
        }

        [Test]
        public void WhenTodayIsSunday_Start_IsPreviousMonday()
        {
            // This test verifies the structural property:
            // if today is Sunday, start should be 6 days before today
            var (start, _) = TimePeriodCalculator.GetThisWeekRange();
            var today = DateTimeOffset.UtcNow;

            if (today.DayOfWeek == DayOfWeek.Sunday)
            {
                var expectedMonday = today.Date.AddDays(-6);
                Assert.That(start.Date, Is.EqualTo(expectedMonday));
            }
            else
            {
                // Just verify it's Monday regardless
                Assert.That(start.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            }
        }
    }

    [TestFixture]
    public class GetThisMonthRangeTests
    {
        [Test]
        public void Start_IsFirstDayOfCurrentMonth()
        {
            var (start, _) = TimePeriodCalculator.GetThisMonthRange();
            var now = DateTimeOffset.UtcNow;

            Assert.Multiple(() =>
            {
                Assert.That(start.Day, Is.EqualTo(1));
                Assert.That(start.Month, Is.EqualTo(now.Month));
                Assert.That(start.Year, Is.EqualTo(now.Year));
            });
        }

        [Test]
        public void Start_IsMidnightUtc()
        {
            var (start, _) = TimePeriodCalculator.GetThisMonthRange();

            Assert.Multiple(() =>
            {
                Assert.That(start.TimeOfDay, Is.EqualTo(TimeSpan.Zero));
                Assert.That(start.Offset, Is.EqualTo(TimeSpan.Zero));
            });
        }

        [Test]
        public void End_IsAtOrAfterStart()
        {
            var (start, end) = TimePeriodCalculator.GetThisMonthRange();

            Assert.That(end, Is.GreaterThanOrEqualTo(start));
        }

        [Test]
        public void End_HasUtcOffset()
        {
            var (_, end) = TimePeriodCalculator.GetThisMonthRange();

            Assert.That(end.Offset, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [TestFixture]
    public class YearBoundaryTests
    {
        [Test]
        public void GetTodayRange_OnJanuary1st_StartIsJanuary1st()
        {
            // Verify that if today is January 1st, the start is Jan 1 midnight UTC
            var (start, _) = TimePeriodCalculator.GetTodayRange();
            var now = DateTimeOffset.UtcNow;

            if (now.Month == 1 && now.Day == 1)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(start.Month, Is.EqualTo(1));
                    Assert.That(start.Day, Is.EqualTo(1));
                    Assert.That(start.Year, Is.EqualTo(now.Year));
                });
            }
            else
            {
                // On other days, just verify basic structural properties hold
                Assert.That(start.Date, Is.EqualTo(now.Date));
            }
        }

        [Test]
        public void GetThisMonthRange_OnJanuary1st_StartIsJanuary1stOfCurrentYear()
        {
            // Verify year boundary: first of month in January is same year, not previous
            var (start, _) = TimePeriodCalculator.GetThisMonthRange();
            var now = DateTimeOffset.UtcNow;

            if (now.Month == 1)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(start.Year, Is.EqualTo(now.Year));
                    Assert.That(start.Month, Is.EqualTo(1));
                    Assert.That(start.Day, Is.EqualTo(1));
                });
            }
            else
            {
                // On non-January months, month range start year equals current year
                Assert.That(start.Year, Is.EqualTo(now.Year));
            }
        }

        [Test]
        public void GetThisWeekRange_OnJanuary1st_StartMayBeInPreviousYear()
        {
            // If Jan 1 is not a Monday, the week start (Monday) may be in December of previous year
            var (start, _) = TimePeriodCalculator.GetThisWeekRange();
            var now = DateTimeOffset.UtcNow;

            if (now.Month == 1 && now.Day <= 6)
            {
                // Near year boundary, the Monday could be in the previous year
                // Verify it's still a Monday and within 6 days of today
                Assert.Multiple(() =>
                {
                    Assert.That(start.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
                    Assert.That((now.Date - start.Date).Days, Is.InRange(0, 6));
                });
            }
            else
            {
                // Standard case: Monday is in the same year
                Assert.That(start.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            }
        }

        [Test]
        public void AllRanges_MaintainStartBeforeOrEqualEnd_AtYearBoundary()
        {
            // Structural invariant: start <= end regardless of date
            var (todayStart, todayEnd) = TimePeriodCalculator.GetTodayRange();
            var (weekStart, weekEnd) = TimePeriodCalculator.GetThisWeekRange();
            var (monthStart, monthEnd) = TimePeriodCalculator.GetThisMonthRange();

            Assert.Multiple(() =>
            {
                Assert.That(todayEnd, Is.GreaterThanOrEqualTo(todayStart));
                Assert.That(weekEnd, Is.GreaterThanOrEqualTo(weekStart));
                Assert.That(monthEnd, Is.GreaterThanOrEqualTo(monthStart));
            });
        }
    }
}
