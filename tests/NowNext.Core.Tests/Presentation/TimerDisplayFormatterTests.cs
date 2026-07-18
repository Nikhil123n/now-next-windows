using NowNext.App.Presentation;
using NowNext.Core.Sessions;

namespace NowNext.Core.Tests.Presentation;

[TestClass]
public sealed class TimerDisplayFormatterTests
{
    [TestMethod]
    public void FormatCountUpFloorsAuthoritativeElapsedWithoutAddingATick()
    {
        var view = new SessionView(
            SessionStateKind.Focusing,
            new CountUpTimerReading(TimeSpan.FromSeconds(65.9), TimeSpan.FromMinutes(25)));

        TimerDisplayParts display = TimerDisplayFormatter.Format(view);

        Assert.AreEqual(string.Empty, display.Prefix);
        Assert.AreEqual("01", display.Minutes);
        Assert.AreEqual("05", display.Seconds);
        Assert.IsFalse(display.ShowHours);
    }

    [TestMethod]
    public void FormatCountdownRoundsPositiveRemainderUpToAvoidEarlyZero()
    {
        var view = new SessionView(
            SessionStateKind.Focusing,
            new CountdownTimerReading(TimeSpan.FromMilliseconds(1), TimeSpan.FromMinutes(25)));

        TimerDisplayParts display = TimerDisplayFormatter.Format(view);

        Assert.AreEqual("00", display.Minutes);
        Assert.AreEqual("01", display.Seconds);
    }

    [TestMethod]
    public void FormatOvertimeUsesPositivePrefix()
    {
        var view = new SessionView(
            SessionStateKind.Overtime,
            new OvertimeTimerReading(TimeSpan.FromSeconds(61)));

        TimerDisplayParts display = TimerDisplayFormatter.Format(view);

        Assert.AreEqual("+", display.Prefix);
        Assert.AreEqual("01", display.Minutes);
        Assert.AreEqual("01", display.Seconds);
        Assert.Contains("overtime", display.AccessibleText);
    }

    [TestMethod]
    public void FormatLongDurationUsesStableHoursSegments()
    {
        var view = new SessionView(
            SessionStateKind.Focusing,
            new CountUpTimerReading(TimeSpan.FromSeconds(3_661), TimeSpan.FromHours(2)));

        TimerDisplayParts display = TimerDisplayFormatter.Format(view);

        Assert.IsTrue(display.ShowHours);
        Assert.AreEqual("1", display.Hours);
        Assert.AreEqual("01", display.Minutes);
        Assert.AreEqual("01", display.Seconds);
    }
}
