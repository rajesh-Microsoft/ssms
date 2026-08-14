using SMMS.Api.Services.Payments;
using Xunit;

namespace SMMS.Api.Tests;

public class PhonePeReconciliationScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    public void NotDue_BeforeFirstCheckWindow(int ageSeconds) =>
        Assert.False(PhonePeReconciliationService.IsDue(TimeSpan.FromSeconds(ageSeconds), default, Now));

    [Fact]
    public void Due_AtFirstCheckWindow_WhenNeverChecked() =>
        Assert.True(PhonePeReconciliationService.IsDue(TimeSpan.FromSeconds(21), default, Now));

    [Theory]
    [InlineData(30, 2, false)]   // early window polls every 3s
    [InlineData(30, 4, true)]
    [InlineData(90, 5, false)]   // then every 6s
    [InlineData(90, 7, true)]
    [InlineData(150, 9, false)]  // then every 10s
    [InlineData(150, 11, true)]
    [InlineData(200, 29, false)] // then every 30s
    [InlineData(200, 31, true)]
    [InlineData(600, 59, false)] // then once a minute
    [InlineData(600, 61, true)]
    public void RespectsEscalatingInterval(int ageSeconds, int secondsSinceLastCheck, bool expected)
    {
        var due = PhonePeReconciliationService.IsDue(
            TimeSpan.FromSeconds(ageSeconds),
            Now.AddSeconds(-secondsSinceLastCheck),
            Now);

        Assert.Equal(expected, due);
    }
}
