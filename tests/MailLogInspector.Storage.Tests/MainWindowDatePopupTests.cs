using System;
using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MainWindowDatePopupTests
{
    [Fact]
    public void ResolvePopupCalendarMonths_UsesCurrentAndPreviousMonth()
    {
        (DateTime left, DateTime right) = MainWindow.ResolvePopupCalendarMonths(
            new DateTime(2026, 4, 10),
            new DateTime(2026, 7, 9));

        Assert.Equal(new DateTime(2026, 6, 1), left);
        Assert.Equal(new DateTime(2026, 7, 1), right);
    }

    [Fact]
    public void ResolvePopupCalendarAnchorAfterNavigation_BackwardMovesTwoMonthsEarlier()
    {
        DateTime anchor = MainWindow.ResolvePopupCalendarAnchorAfterNavigation(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 6, 1),
            navigatedFromRightCalendar: true,
            new DateTime(2026, 4, 10),
            new DateTime(2026, 7, 9));

        Assert.Equal(new DateTime(2026, 5, 1), anchor);
    }

    [Fact]
    public void ResolvePopupCalendarAnchorAfterNavigation_ForwardMovesTwoMonthsLater()
    {
        DateTime anchor = MainWindow.ResolvePopupCalendarAnchorAfterNavigation(
            new DateTime(2026, 5, 1),
            new DateTime(2026, 6, 1),
            navigatedFromRightCalendar: true,
            new DateTime(2026, 4, 10),
            new DateTime(2026, 7, 9));

        Assert.Equal(new DateTime(2026, 7, 1), anchor);
    }

    [Fact]
    public void ResolvePopupCalendarNavigationAvailability_DisablesBackwardAtOldestPair()
    {
        (bool canMoveBackward, bool canMoveForward) = MainWindow.ResolvePopupCalendarNavigationAvailability(
            new DateTime(2026, 5, 1),
            new DateTime(2026, 4, 10),
            new DateTime(2026, 7, 9));

        Assert.False(canMoveBackward);
        Assert.True(canMoveForward);
    }

    [Fact]
    public void ResolvePopupCalendarNavigationAvailability_DisablesForwardAtNewestPair()
    {
        (bool canMoveBackward, bool canMoveForward) = MainWindow.ResolvePopupCalendarNavigationAvailability(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 4, 10),
            new DateTime(2026, 7, 9));

        Assert.True(canMoveBackward);
        Assert.False(canMoveForward);
    }
}
