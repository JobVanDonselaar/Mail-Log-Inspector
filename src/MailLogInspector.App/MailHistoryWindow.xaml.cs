using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

/// <summary>
/// Toont alle afleverpogingen van één mail. De database bewaart alleen de eindstand, dus de
/// tussenliggende pogingen worden hier op aanvraag uit de gearchiveerde rapporten gelezen.
/// </summary>
public partial class MailHistoryWindow : Window
{
	private readonly MailLogInspectorMailHistoryService _service;
	private readonly MailLogInspectorSearchRow _row;
	private readonly CancellationTokenSource _cancellation = new();
	private MailLogInspectorMailHistory? _history;

	public MailHistoryWindow(MailLogInspectorMailHistoryService service, MailLogInspectorSearchRow row)
	{
		_service = service;
		_row = row;
		InitializeComponent();

		SubtitleTextBlock.Text = $"{row.Sender}  →  {row.Recipient}";
		StatusTextBlock.Text = "Archief doorzoeken…";
		SearchProgressBar.Visibility = Visibility.Visible;
		CopyButton.IsEnabled = false;

		Loaded += MailHistoryWindow_Loaded;
		Closed += MailHistoryWindow_Closed;
	}

	private async void MailHistoryWindow_Loaded(object sender, RoutedEventArgs e)
	{
		await LoadHistoryAsync();
	}

	private void MailHistoryWindow_Closed(object? sender, EventArgs e)
	{
		_cancellation.Cancel();
		_cancellation.Dispose();
	}

	private async Task LoadHistoryAsync()
	{
		if (string.IsNullOrWhiteSpace(_row.TrackingId))
		{
			SearchProgressBar.Visibility = Visibility.Collapsed;
			StatusTextBlock.Text = "Deze regel heeft geen bruikbaar tracking-ID, daarom kan de historie niet opgezocht worden.";
			return;
		}

		Progress<MailLogInspectorMailHistoryProgress> progress = new(update =>
		{
			SearchProgressBar.Value = update.Fraction;
			StatusTextBlock.Text = update.Display;
		});

		try
		{
			CancellationToken token = _cancellation.Token;
			MailLogInspectorMailHistory history = await Task.Run(
				() => _service.ReadHistory(
					_row.TrackingId,
					_row.Recipient,
					_row.AcceptedAt,
					_row.LastSeenAt,
					progress,
					token),
				token);

			_history = history;
			ShowHistory(history);
		}
		catch (OperationCanceledException)
		{
			// Het venster is gesloten terwijl er nog gezocht werd.
		}
		catch (Exception exception)
		{
			SearchProgressBar.Visibility = Visibility.Collapsed;
			StatusTextBlock.Text = "Het archief kon niet gelezen worden: " + exception.Message;
		}
	}

	private void ShowHistory(MailLogInspectorMailHistory history)
	{
		SearchProgressBar.Visibility = Visibility.Collapsed;
		HistoryGrid.ItemsSource = history.Attempts;
		CopyButton.IsEnabled = history.HasAttempts;
		StatusTextBlock.Text = BuildStatusText(history);
		FooterTextBlock.Text = BuildFooterText(history);
	}

	internal static string BuildStatusText(MailLogInspectorMailHistory history)
	{
		if (!history.HasAttempts)
		{
			return "Geen logregels gevonden in het archief. Waarschijnlijk valt deze mail buiten de periode waarvan de rapporten nog bewaard zijn.";
		}

		int attempts = history.Attempts.Count;
		string label = attempts == 1 ? "1 logregel" : $"{attempts} logregels";
		return $"{label} gevonden. Elke regel is één afleverpoging zoals de mailserver die rapporteerde.";
	}

	internal static string BuildFooterText(MailLogInspectorMailHistory history)
	{
		List<string> parts = new()
		{
			history.SearchedArchives.Count == 1
				? "1 archiefbestand doorzocht"
				: $"{history.SearchedArchives.Count} archiefbestanden doorzocht"
		};

		if (history.MissingArchives.Count > 0)
		{
			parts.Add(history.MissingArchives.Count == 1
				? "1 archiefbestand ontbreekt en is overgeslagen"
				: $"{history.MissingArchives.Count} archiefbestanden ontbreken en zijn overgeslagen");
		}

		return string.Join(" · ", parts) + ".";
	}

	internal static string BuildClipboardText(MailLogInspectorSearchRow row, MailLogInspectorMailHistory history)
	{
		StringBuilder builder = new();
		builder.AppendLine("Volledige historie");
		builder.AppendLine($"Afzender  : {row.Sender}");
		builder.AppendLine($"Ontvanger : {row.Recipient}");
		builder.AppendLine($"Tracking  : {history.TrackingId}");
		builder.AppendLine();

		foreach (MailLogInspectorMailHistoryAttempt attempt in history.Attempts)
		{
			builder.AppendLine($"{attempt.MomentDisplay} | {attempt.StatusDisplay} | {attempt.ResponseCodeDisplay} | pogingen {attempt.TriesDisplay} | {attempt.ResponseMessage}");
		}

		return builder.ToString();
	}

	private void CopyButton_Click(object sender, RoutedEventArgs e)
	{
		if (_history is null)
		{
			return;
		}

		try
		{
			System.Windows.Clipboard.SetText(BuildClipboardText(_row, _history));
		}
		catch (Exception exception)
		{
			System.Windows.MessageBox.Show(this, "Kopiëren is niet gelukt: " + exception.Message, "Mail Log Inspector",
				MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
