using System;
using System.IO;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MainWindowDatePopupLayoutTests
{
    [Fact]
    public void DatePopup_DoesNotUseSpacerColumnBetweenCalendars()
    {
        string xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "MainWindow.xaml"));
        string xaml = File.ReadAllText(xamlPath);

        int popupStart = xaml.IndexOf("<Popup Name=\"DateSelectionPopup\"", StringComparison.Ordinal);
        Assert.True(popupStart >= 0);
        int popupEnd = xaml.IndexOf("</Popup>", popupStart, StringComparison.Ordinal);
        Assert.True(popupEnd > popupStart);
        string popupBlock = xaml.Substring(popupStart, popupEnd - popupStart);

        Assert.DoesNotContain("<ColumnDefinition Width=\"12\" />", popupBlock, StringComparison.Ordinal);
        Assert.Contains("Name=\"RightMonthDaysItemsControl\" Grid.Row=\"2\" Grid.Column=\"2\"", popupBlock, StringComparison.Ordinal);
        Assert.Contains("Name=\"PopupCalendarMonthDivider\" Grid.Row=\"0\" Grid.RowSpan=\"3\" Grid.Column=\"2\"", popupBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void DatePopup_UsesOneCustomTwoMonthSurfaceWithOnlyOuterNavigationButtons()
    {
        string xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "MainWindow.xaml"));
        string xaml = File.ReadAllText(xamlPath);

        int popupStart = xaml.IndexOf("<Popup Name=\"DateSelectionPopup\"", StringComparison.Ordinal);
        Assert.True(popupStart >= 0);
        int popupEnd = xaml.IndexOf("</Popup>", popupStart, StringComparison.Ordinal);
        Assert.True(popupEnd > popupStart);
        string popupBlock = xaml.Substring(popupStart, popupEnd - popupStart);

        Assert.DoesNotContain("<Calendar Name=", popupBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayDateChanged=\"PopupCalendar_DisplayDateChanged\"", popupBlock, StringComparison.Ordinal);
        Assert.Contains("Name=\"PopupCalendarPreviousButton\"", popupBlock, StringComparison.Ordinal);
        Assert.Contains("Name=\"PopupCalendarNextButton\"", popupBlock, StringComparison.Ordinal);
        Assert.Contains("Name=\"LeftMonthDaysItemsControl\"", popupBlock, StringComparison.Ordinal);
        Assert.Contains("Name=\"RightMonthDaysItemsControl\"", popupBlock, StringComparison.Ordinal);
    }
    [Fact]
    public void DatePickerCalendarOpened_ClosesNativeDropdownAndDefersFallbackPopup()
    {
        string sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "MainWindow.DatePopup.cs"));
        string source = File.ReadAllText(sourcePath);

        int methodStart = source.IndexOf("private void DatePicker_CalendarOpened", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        int methodEnd = source.IndexOf("private void PopupCalendarDayButton_Click", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);
        string methodBlock = source.Substring(methodStart, methodEnd - methodStart);

        Assert.Contains("picker.IsDropDownOpen = false", methodBlock, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke", methodBlock, StringComparison.Ordinal);
        Assert.True(methodBlock.IndexOf("picker.IsDropDownOpen = false", StringComparison.Ordinal) < methodBlock.IndexOf("Dispatcher.BeginInvoke", StringComparison.Ordinal));
    }
    [Fact]
    public void DatePickers_InterceptNativeDropdownBeforeItOpens()
    {
        string xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "MainWindow.xaml"));
        string xaml = File.ReadAllText(xamlPath);

        foreach (string pickerName in new[] { "SearchFromDatePicker", "SearchThroughDatePicker", "AnalysisFromDatePicker", "AnalysisThroughDatePicker" })
        {
            int pickerStart = xaml.IndexOf($"<DatePicker Name=\"{pickerName}\"", StringComparison.Ordinal);
            Assert.True(pickerStart >= 0, pickerName);
            int pickerEnd = xaml.IndexOf("/>", pickerStart, StringComparison.Ordinal);
            Assert.True(pickerEnd > pickerStart, pickerName);
            string pickerBlock = xaml.Substring(pickerStart, pickerEnd - pickerStart);

            Assert.Contains("PreviewMouseDown=\"DatePicker_PreviewMouseDown\"", pickerBlock, StringComparison.Ordinal);
            Assert.Contains("PreviewMouseUp=\"DatePicker_PreviewMouseUp\"", pickerBlock, StringComparison.Ordinal);
            Assert.Contains("PreviewKeyDown=\"DatePicker_PreviewKeyDown\"", pickerBlock, StringComparison.Ordinal);
        }
    }
    [Fact]
    public void DatePickerPreviewMouseDown_BlocksNativeDropdownWithoutOpeningPopup()
    {
        string sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "MainWindow.DatePopup.cs"));
        string source = File.ReadAllText(sourcePath);

        int methodStart = source.IndexOf("private void DatePicker_PreviewMouseDown", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        int methodEnd = source.IndexOf("private void DatePicker_PreviewKeyDown", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);
        string methodBlock = source.Substring(methodStart, methodEnd - methodStart);

        Assert.Contains("e.Handled = true", methodBlock, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke", methodBlock, StringComparison.Ordinal);
        Assert.True(methodBlock.IndexOf("e.Handled = true", StringComparison.Ordinal) < methodBlock.IndexOf("Dispatcher.BeginInvoke", StringComparison.Ordinal));
    }
    [Fact]
    public void DatePickerPreviewMouseUp_DefersPopupUntilCurrentClickFinishes()
    {
        string sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "MainWindow.DatePopup.cs"));
        string source = File.ReadAllText(sourcePath);

        int methodStart = source.IndexOf("private void DatePicker_PreviewMouseUp", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        int methodEnd = source.IndexOf("private void DatePicker_PreviewKeyDown", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);
        string methodBlock = source.Substring(methodStart, methodEnd - methodStart);

        Assert.Contains("e.Handled = true", methodBlock, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke", methodBlock, StringComparison.Ordinal);
        Assert.True(methodBlock.IndexOf("e.Handled = true", StringComparison.Ordinal) < methodBlock.IndexOf("Dispatcher.BeginInvoke", StringComparison.Ordinal));
    }
}
