using ExpenseTracker.Application.Common;
using Microsoft.Extensions.Time.Testing;

namespace ExpenseTracker.UnitTests.Services;

public sealed class AppClockTests
{
    [Fact]
    public void ResolveTimeZone_AsiaHoChiMinh_ResolvesUtcPlusSevenOffset()
    {
        var timeZone = AppClock.ResolveTimeZone("Asia/Ho_Chi_Minh");

        Assert.Equal(TimeSpan.FromHours(7), timeZone.BaseUtcOffset);
    }

    [Fact]
    public void ResolveTimeZone_UnknownTimeZone_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => AppClock.ResolveTimeZone("Invalid/Not_A_Zone"));
    }

    [Fact]
    public void Today_LateUtcTime_ReflectsNextDayInAsiaHoChiMinh()
    {
        var fakeTimeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 12, 15, 17, 30, 0, TimeSpan.Zero));
        var clock = new AppClock(fakeTimeProvider, AppClock.ResolveTimeZone("Asia/Ho_Chi_Minh"));

        Assert.Equal(new DateOnly(2026, 12, 16), clock.Today);
    }

    [Fact]
    public void UtcNow_UsesInjectedTimeProvider()
    {
        var expectedUtc = new DateTime(2026, 12, 15, 3, 0, 0, DateTimeKind.Utc);
        var fakeTimeProvider = new FakeTimeProvider(new DateTimeOffset(expectedUtc));
        var clock = new AppClock(fakeTimeProvider, AppClock.ResolveTimeZone("Asia/Ho_Chi_Minh"));

        Assert.Equal(expectedUtc, clock.UtcNow);
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
    }
}

