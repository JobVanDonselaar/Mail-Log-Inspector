using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MailLogInspector.Core;

namespace MailLogInspector.App;

public partial class MainWindow
{	private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
	{
		if (sender is not DatePicker picker)
		{
			return;
		}

		ApplyDatePickerBounds(picker);
		if (ReferenceEquals(picker, SearchFromDatePicker) || ReferenceEquals(picker, SearchThroughDatePicker))
		{
			UpdateSenderDomainDashboardOptionState();
			if (GetDashboardForCurrentSearch() is null)
			{
				ApplySenderDomainDashboardLayout(false);
			}
		}
		if (ReferenceEquals(picker, AnalysisFromDatePicker) || ReferenceEquals(picker, AnalysisThroughDatePicker))
		{
			UpdateAnalysisExecutionState();
		}
	}

	private void DatePicker_PreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (sender is not DatePicker)
		{
			return;
		}

		e.Handled = true;
	}

	private void DatePicker_PreviewMouseUp(object sender, MouseButtonEventArgs e)
	{
		if (sender is not DatePicker picker)
		{
			return;
		}

		e.Handled = true;
		Dispatcher.BeginInvoke(new Action(() => OpenDateSelectionPopup(picker)), DispatcherPriority.Background);
	}

	private void DatePicker_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (sender is not DatePicker picker)
		{
			return;
		}

		if (e.Key != Key.Enter && e.Key != Key.Space && e.Key != Key.Down)
		{
			return;
		}

		e.Handled = true;
		OpenDateSelectionPopup(picker);
	}

	private void DatePicker_CalendarOpened(object sender, RoutedEventArgs e)
	{
		if (sender is not DatePicker picker)
		{
			return;
		}

		picker.IsDropDownOpen = false;
		Dispatcher.BeginInvoke(new Action(() => OpenDateSelectionPopup(picker)), DispatcherPriority.Input);
	}
	private void PopupCalendarDayButton_Click(object sender, RoutedEventArgs e)
	{
		if (_popupDatePicker is null || (sender as FrameworkElement)?.Tag is not DateTime date)
		{
			return;
		}

		(DateTime minDate, DateTime maxDate) = GetSelectableDateBounds();
		_popupDatePicker.SelectedDate = ClampDate(date.Date, minDate, maxDate);
		DateSelectionPopup.IsOpen = false;
	}

	private void PopupCalendarPreviousButton_Click(object sender, RoutedEventArgs e)
	{
		MovePopupCalendarByMonths(-2);
	}

	private void PopupCalendarNextButton_Click(object sender, RoutedEventArgs e)
	{
		MovePopupCalendarByMonths(2);
	}

	private void MovePopupCalendarByMonths(int monthOffset)
	{
		(DateTime minDate, DateTime maxDate) = GetSelectableDateBounds();
		DateTime selectedDate = ClampDate((_popupDatePicker?.SelectedDate ?? maxDate).Date, minDate, maxDate);
		DateTime nextAnchorMonth = ResolvePopupCalendarAnchorAfterButtonStep(_popupCalendarAnchorMonth, monthOffset, minDate, maxDate);
		ApplyPopupCalendarMonths(nextAnchorMonth, minDate, maxDate, selectedDate);
	}
	private void DateSelectionPopup_Closed(object sender, EventArgs e)
	{
		_popupDatePicker = null;
	}

	private void RefreshDatePickerConstraints()
	{
		ApplyDatePickerBounds(SearchFromDatePicker);
		ApplyDatePickerBounds(SearchThroughDatePicker);
		ApplyDatePickerBounds(AnalysisFromDatePicker);
		ApplyDatePickerBounds(AnalysisThroughDatePicker);
	}

	private void ApplyDatePickerBounds(DatePicker picker)
	{
		if (_applyingDatePickerBounds)
		{
			return;
		}

		(DateTime minDate, DateTime maxDate) = GetSelectableDateBounds();
		_applyingDatePickerBounds = true;
		try
		{
			picker.DisplayDateStart = minDate;
			picker.DisplayDateEnd = maxDate;
			picker.DisplayDate = ClampDate((picker.SelectedDate ?? maxDate).Date, minDate, maxDate);
			if (picker.SelectedDate.HasValue)
			{
				DateTime clampedDate = ClampDate(picker.SelectedDate.Value.Date, minDate, maxDate);
				if (clampedDate != picker.SelectedDate.Value.Date)
				{
					picker.SelectedDate = clampedDate;
				}
			}
		}
		finally
		{
			_applyingDatePickerBounds = false;
		}
	}

	private (DateTime MinDate, DateTime MaxDate) GetSelectableDateBounds()
	{
		if (!string.IsNullOrWhiteSpace(_activeArchiveMonthKey))
		{
			DateTime monthStart = DateTime.ParseExact(_activeArchiveMonthKey + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
			return (monthStart.Date, monthStart.AddMonths(1).AddDays(-1).Date);
		}

		return (DateTime.Today.AddDays(-MailLogInspectorRetentionPolicy.ActiveRetentionDays).Date, DateTime.Today.Date);
	}

	private void OpenDateSelectionPopup(DatePicker picker)
	{
		_popupDatePicker = picker;
		ConfigureDateSelectionPopup(picker);
		DateSelectionPopup.PlacementTarget = picker;
		DateSelectionPopup.IsOpen = true;
	}

	private void ConfigureDateSelectionPopup(DatePicker picker)
	{
		(DateTime minDate, DateTime maxDate) = GetSelectableDateBounds();
		DateTime selectedDate = ClampDate((picker.SelectedDate ?? maxDate).Date, minDate, maxDate);
		DateTime anchorMonth = ResolvePopupCalendarMonths(minDate, maxDate).RightMonth;
		ApplyPopupCalendarMonths(anchorMonth, minDate, maxDate, selectedDate);
	}

	private void ApplyPopupCalendarMonths(DateTime anchorMonth, DateTime minDate, DateTime maxDate, DateTime selectedDate)
	{
		DateTime normalizedAnchorMonth = ClampPopupCalendarAnchorMonth(anchorMonth, minDate, maxDate);
		DateTime leftMonth = normalizedAnchorMonth.AddMonths(-1);
		DateTime rightMonth = normalizedAnchorMonth;
		(bool canMoveBackward, bool canMoveForward) = ResolvePopupCalendarNavigationAvailability(normalizedAnchorMonth, minDate, maxDate);

		_popupCalendarAnchorMonth = normalizedAnchorMonth;
		LeftMonthTitleTextBlock.Text = FormatPopupMonthTitle(leftMonth);
		RightMonthTitleTextBlock.Text = FormatPopupMonthTitle(rightMonth);
		LeftMonthDaysItemsControl.ItemsSource = BuildPopupMonthDayCells(leftMonth, minDate, maxDate, selectedDate);
		RightMonthDaysItemsControl.ItemsSource = BuildPopupMonthDayCells(rightMonth, minDate, maxDate, selectedDate);
		PopupCalendarPreviousButton.IsEnabled = canMoveBackward;
		PopupCalendarNextButton.IsEnabled = canMoveForward;
	}

	public static (DateTime LeftMonth, DateTime RightMonth) ResolvePopupCalendarMonths(DateTime minDate, DateTime maxDate)
	{
		DateTime rightMonth = FirstDayOfMonth(maxDate);
		return (rightMonth.AddMonths(-1), rightMonth);
	}

	public static DateTime ResolvePopupCalendarAnchorAfterNavigation(DateTime currentAnchorMonth, DateTime requestedDisplayMonth, bool navigatedFromRightCalendar, DateTime minDate, DateTime maxDate)
	{
		DateTime normalizedAnchorMonth = ClampPopupCalendarAnchorMonth(currentAnchorMonth, minDate, maxDate);
		DateTime normalizedRequestedMonth = FirstDayOfMonth(requestedDisplayMonth);
		DateTime currentLeftMonth = normalizedAnchorMonth.AddMonths(-1);
		if (navigatedFromRightCalendar)
		{
			if (normalizedRequestedMonth < normalizedAnchorMonth)
			{
				return ResolvePopupCalendarAnchorAfterButtonStep(normalizedAnchorMonth, -2, minDate, maxDate);
			}
			if (normalizedRequestedMonth > normalizedAnchorMonth)
			{
				return ResolvePopupCalendarAnchorAfterButtonStep(normalizedAnchorMonth, 2, minDate, maxDate);
			}
		}
		else
		{
			if (normalizedRequestedMonth < currentLeftMonth)
			{
				return ResolvePopupCalendarAnchorAfterButtonStep(normalizedAnchorMonth, -2, minDate, maxDate);
			}
			if (normalizedRequestedMonth > currentLeftMonth)
			{
				return ResolvePopupCalendarAnchorAfterButtonStep(normalizedAnchorMonth, 2, minDate, maxDate);
			}
		}

		return normalizedAnchorMonth;
	}

	public static DateTime ResolvePopupCalendarAnchorAfterButtonStep(DateTime currentAnchorMonth, int monthOffset, DateTime minDate, DateTime maxDate)
	{
		return ClampPopupCalendarAnchorMonth(FirstDayOfMonth(currentAnchorMonth).AddMonths(monthOffset), minDate, maxDate);
	}

	public static (bool CanMoveBackward, bool CanMoveForward) ResolvePopupCalendarNavigationAvailability(DateTime anchorMonth, DateTime minDate, DateTime maxDate)
	{
		DateTime normalizedAnchorMonth = ClampPopupCalendarAnchorMonth(anchorMonth, minDate, maxDate);
		DateTime maxAnchorMonth = FirstDayOfMonth(maxDate);
		DateTime minAnchorMonth = FirstDayOfMonth(minDate).AddMonths(1);
		if (minAnchorMonth > maxAnchorMonth)
		{
			minAnchorMonth = maxAnchorMonth;
		}
		return (normalizedAnchorMonth > minAnchorMonth, normalizedAnchorMonth < maxAnchorMonth);
	}

	public static IReadOnlyList<PopupMonthDayCell> BuildPopupMonthDayCells(DateTime displayMonth, DateTime minDate, DateTime maxDate, DateTime selectedDate)
	{
		DateTime monthStart = FirstDayOfMonth(displayMonth);
		int firstDayOffset = ((int)monthStart.DayOfWeek + 6) % 7;
		DateTime firstCellDate = monthStart.AddDays(-firstDayOffset);
		List<PopupMonthDayCell> cells = new(42);
		for (int index = 0; index < 42; index++)
		{
			DateTime cellDate = firstCellDate.AddDays(index).Date;
			bool isInDisplayMonth = cellDate.Month == monthStart.Month && cellDate.Year == monthStart.Year;
			bool isSelectable = isInDisplayMonth && cellDate >= minDate.Date && cellDate <= maxDate.Date;
			cells.Add(new PopupMonthDayCell(
				isInDisplayMonth ? cellDate.Day.ToString(CultureInfo.InvariantCulture) : string.Empty,
				isInDisplayMonth ? cellDate : null,
				isInDisplayMonth,
				isSelectable,
				isSelectable && cellDate == selectedDate.Date));
		}

		return cells;
	}

	private static string FormatPopupMonthTitle(DateTime month)
	{
		return FirstDayOfMonth(month).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("nl-NL"));
	}

	private static DateTime ClampPopupCalendarAnchorMonth(DateTime anchorMonth, DateTime minDate, DateTime maxDate)
	{
		DateTime normalizedAnchorMonth = FirstDayOfMonth(anchorMonth);
		DateTime maxAnchorMonth = FirstDayOfMonth(maxDate);
		DateTime minAnchorMonth = FirstDayOfMonth(minDate).AddMonths(1);
		if (minAnchorMonth > maxAnchorMonth)
		{
			minAnchorMonth = maxAnchorMonth;
		}
		if (normalizedAnchorMonth < minAnchorMonth)
		{
			return minAnchorMonth;
		}
		if (normalizedAnchorMonth > maxAnchorMonth)
		{
			return maxAnchorMonth;
		}
		return normalizedAnchorMonth;
	}
	private static DateTime FirstDayOfMonth(DateTime value)
	{
		return new DateTime(value.Year, value.Month, 1);
	}

	private static DateTime ClampDate(DateTime value, DateTime minDate, DateTime maxDate)
	{
		if (value < minDate)
		{
			return minDate;
		}

		if (value > maxDate)
		{
			return maxDate;
		}

		return value;
	}

	public sealed record PopupMonthDayCell(string DisplayText, DateTime? Date, bool IsInDisplayMonth, bool IsSelectable, bool IsSelected);
}
