using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MailLogInspector.App;

public partial class MainWindow : Window
{
	private readonly MailLogInspectorWorkspacePaths _workspace;

	private MailLogInspectorStore _store;

	private readonly MailLogInspectorWorkspaceRebuilder _rebuilder;

	private MailLogInspectorImportService _importService;

	private MailLogInspectorSearchService _searchService;

	private MailLogInspectorAnalysisService _analysisService;

	private readonly GmailReportOperationalStore _gmailOperationalStore;

	private readonly SmtpPortalOperationalStore _smtpPortalOperationalStore;

	private readonly ReportSyncOperationalStore _reportSyncOperationalStore;
	private readonly ReportSyncRuntime _reportSyncRuntime;

	private readonly ReportSyncCoordinator _reportSyncCoordinator;

	private string? _activeArchiveMonthKey;

	private MailLogInspectorSearchCriteria? _lastSearchCriteria;

	private IReadOnlyList<MailLogInspectorSearchRow> _lastSearchRows = Array.Empty<MailLogInspectorSearchRow>();

	private IReadOnlyList<MailLogInspectorSearchRow> _lastVisibleSearchRows = Array.Empty<MailLogInspectorSearchRow>();

	private IReadOnlyList<SearchResultsListItem> _searchResultsListItems = Array.Empty<SearchResultsListItem>();

	private readonly HashSet<string> _expandedSearchGroups = new(StringComparer.OrdinalIgnoreCase);

	private System.Windows.Controls.ComboBox? _searchResultsStatusHeaderComboBox;

	private string? _selectedSearchStatus;

	private int _currentSearchLimit;

	private bool _searchIsRunning;

	private bool _analysisIsRunning;

	private bool _canLoadMoreSearchResults;

	private CancellationTokenSource? _searchCancellation;

	private CancellationTokenSource? _analysisRefreshCancellation;
	private CancellationTokenSource? _syncCancellation;

	private const int UnlimitedSearchLimit = 100000;

	private const int FixedGmailAutoSyncIntervalMinutes = 15;

	private DatePicker? _popupDatePicker;

	private DateTime _popupCalendarAnchorMonth;

	private bool _applyingDatePickerBounds;

	private bool _gmailSyncIsRunning;

	private readonly DispatcherTimer _gmailAutoSyncTimer;
	private Forms.NotifyIcon? _notifyIcon;

	private bool _allowCloseFromTray;
	private bool _closeToTrayEnabled;

	public MainWindow()
	{
		InitializeComponent();
		base.Title = MailLogInspectorVersion.WindowTitle;
		_workspace = MailLogInspectorWorkspaceBootstrapper.Prepare();
		MailLogInspectorLog.Configure(_workspace.RootDirectory);
		MailLogInspectorLog.Info("startup", "Applicatie initialiseert versie " + MailLogInspectorVersion.SemanticVersion);
		_rebuilder = new MailLogInspectorWorkspaceRebuilder(_workspace);
		_store = new MailLogInspectorStore(_workspace.DatabasePath);
		_importService = new MailLogInspectorImportService(_store, _workspace);
		_searchService = new MailLogInspectorSearchService(_store);
		_analysisService = new MailLogInspectorAnalysisService(_store);
		_gmailOperationalStore = new GmailReportOperationalStore(_workspace.GmailOperationalDatabasePath);
		_gmailOperationalStore.Initialize();
		GmailOAuthService.MigrateLegacyClientSecret(_gmailOperationalStore);
		_smtpPortalOperationalStore = new SmtpPortalOperationalStore(_workspace.GmailOperationalDatabasePath);
		_smtpPortalOperationalStore.Initialize();
		_reportSyncOperationalStore = new ReportSyncOperationalStore(_workspace.GmailOperationalDatabasePath);
		_reportSyncOperationalStore.Initialize();
		_reportSyncRuntime = ReportSyncRuntime.Create(
			_workspace,
			_store,
			_importService,
			_gmailOperationalStore,
			_smtpPortalOperationalStore,
			_reportSyncOperationalStore);
		_reportSyncCoordinator = _reportSyncRuntime.Coordinator;
		_gmailAutoSyncTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMinutes(15.0)
		};
		_gmailAutoSyncTimer.Tick += GmailAutoSyncTimer_Tick;
		InitializeTrayIcon();
		base.Closing += MainWindow_Closing;
		base.Closed += delegate
		{
			_syncCancellation?.Cancel();
			DisposeTrayIcon();
			_reportSyncRuntime.Dispose();
		};
		// The active database may require an EXE-managed rebuild. Do not initialize
		// the legacy schema before the Loaded startup flow can perform that rebuild.
		SwitchActiveStore(_workspace.DatabasePath, null, initialize: false);
		SearchFromDatePicker.SelectedDate = DateTime.Today.AddDays(-1.0);
		SearchThroughDatePicker.SelectedDate = DateTime.Today;
		SenderDomainDashboardCheckBox.IsChecked = true;
		DateTime yesterday = DateTime.Today.AddDays(-1.0);
		AnalysisFromDatePicker.SelectedDate = yesterday;
		AnalysisThroughDatePicker.SelectedDate = yesterday;
		RefreshDatePickerConstraints();
		UpdateTopStatusPanelVisibility();
		UpdateAnalysisExecutionState();
		ResetAnalysisResults();
		base.Loaded += async delegate
		{
			ReportSyncConfig? syncConfig = null;
			try
			{
				StartupStatusTextBlock.Text = "Database voorbereiden...";
				await EnsureDatabaseReadyAsync();
				syncConfig = _reportSyncOperationalStore.LoadConfig();
				RefreshGmailSection();
				ApplyGmailAutoSyncSchedule(syncConfig);
				StartupStatusTextBlock.Text = "Klaar.";
			}
			catch (Exception ex)
			{
				MailLogInspectorLog.Error("startup", "Initialisatie na tonen van het venster mislukt", ex);
				StartupStatusTextBlock.Text = "Starten zonder volledige analysegegevens.";
			}
			finally
			{
				StartupOverlay.Visibility = Visibility.Collapsed;
			}

			if (syncConfig != null)
			{
				await RunPostStartupMaintenanceAsync(syncConfig);
			}
		};
	}

	private async Task RunPostStartupMaintenanceAsync(ReportSyncConfig syncConfig)
	{
		try
		{
			await RunGmailStartupSyncIfRequiredAsync(syncConfig);
			await EnsureDeliveryLatencyAggregatesAsync();
			await EnsureSenderDomainAnalyticsBackfillAsync();
		}
		catch (Exception ex)
		{
			MailLogInspectorLog.Error("startup", "Onderhoud na opstarten mislukt", ex);
		}
	}
	private async void SearchButton_Click(object sender, RoutedEventArgs e)
	{
		await RunSearchAsync(SearchRunReason.FreshSearch);
	}

	private void SearchCancelButton_Click(object sender, RoutedEventArgs e)
	{
		if (!_searchIsRunning)
		{
			return;
		}

		_searchCancellation?.Cancel();
		SearchRunStateTextBlock.Text = "Zoeken gestopt.";
		SearchRunDetailTextBlock.Text = "Pas filters aan en start opnieuw.";
		SetSearchExecutionState(isRunning: false);
	}

	private async void LoadMoreSearchResultsButton_Click(object sender, RoutedEventArgs e)
	{
		if (_lastSearchCriteria != null)
		{
			_currentSearchLimit = Math.Min(UnlimitedSearchLimit, _currentSearchLimit + ReadSearchLimit());
			await RunSearchAsync(SearchRunReason.LoadMore);
		}
	}

	private async void ExportSearchResultsButton_Click(object sender, RoutedEventArgs e)
	{
		IReadOnlyList<MailLogInspectorSearchRow> readOnlyList = _lastVisibleSearchRows;
		if (readOnlyList.Count == 0)
		{
			SearchRunStateTextBlock.Text = "Geen zoekresultaten om te exporteren.";
			SearchRunDetailTextBlock.Text = "Voer eerst een zoekopdracht uit.";
			return;
		}
		Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
		{
			Filter = "Excel workbook (*.xlsx)|*.xlsx",
			FileName = BuildSearchExportFileName(_lastSearchCriteria ?? BuildSearchCriteria()),
			DefaultExt = ".xlsx",
			AddExtension = true
		};
		if (saveFileDialog.ShowDialog(this) == true)
		{
			SearchResultsExcelExporter.Export(
				saveFileDialog.FileName,
				readOnlyList,
				GetDashboardForCurrentSearch());
			SearchRunStateTextBlock.Text = "Excel export gereed: " + Path.GetFileName(saveFileDialog.FileName);
			OpenExportedWorkbook(saveFileDialog.FileName);
		}
	}

	private static string BuildSearchExportFileName(MailLogInspectorSearchCriteria criteria)
	{
		List<string> parts = ["mail-log-inspector"];
		AddExportFilterPart(parts, "afzender", criteria.Sender ?? criteria.SenderDomain);
		AddExportFilterPart(parts, "ontvanger", criteria.Recipient ?? criteria.RecipientDomain);
		parts.Add("van");
		parts.Add(criteria.FromInclusive.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture));
		parts.Add("tot");
		parts.Add(criteria.ThroughInclusive.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture));
		return string.Join('-', parts) + ".xlsx";
	}

	private static void AddExportFilterPart(List<string> parts, string label, string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		string safeValue = SanitizeExportFileNamePart(value);
		if (safeValue.Length > 0)
		{
			parts.Add(label);
			parts.Add(safeValue);
		}
	}

	private static string SanitizeExportFileNamePart(string value)
	{
		HashSet<char> invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
		string safeValue = string.Concat(value.Trim().Select(character =>
			invalidCharacters.Contains(character) || char.IsWhiteSpace(character) ? '-' : character));
		while (safeValue.Contains("--", StringComparison.Ordinal))
		{
			safeValue = safeValue.Replace("--", "-", StringComparison.Ordinal);
		}

		return safeValue.Trim('-');
	}
	private void OpenExportedWorkbook(string path)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = path,
				UseShellExecute = true
			});
			SearchRunDetailTextBlock.Text = "Zoekresultaten geëxporteerd en geopend.";
		}
		catch (Exception ex)
		{
			MailLogInspectorLog.Error("export", "Excel-export kon niet automatisch worden geopend.", ex);
			SearchRunDetailTextBlock.Text = "Export opgeslagen, maar kon niet automatisch worden geopend.";
		}
	}

	private async void RefreshButton_Click(object sender, RoutedEventArgs e)
	{
		await RefreshDashboardAsync(invalidateDataViews: false);
	}

	private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!ReferenceEquals(sender, MainTabControl))
		{
			return;
		}

		UpdateTopStatusPanelVisibility();
	}

	private async void AnalysisRefreshButton_Click(object sender, RoutedEventArgs e)
	{
		await RefreshAnalysisAsync();
	}

	private void SearchResultsStatusHeaderComboBox_Loaded(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.ComboBox searchResultsStatusHeaderComboBox)
		{
			_searchResultsStatusHeaderComboBox = searchResultsStatusHeaderComboBox;
			SelectSearchStatusFilter(_selectedSearchStatus);
		}
	}

	private async void SearchResultsStatusHeaderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		string? selectedStatus = ((!((sender as System.Windows.Controls.ComboBox)?.SelectedItem is ComboBoxItem { Tag: var tag })) ? null : tag?.ToString());
		if (string.Equals(selectedStatus, _selectedSearchStatus, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		_selectedSearchStatus = selectedStatus;
		if (_lastSearchCriteria != null)
		{
			await RunSearchAsync(SearchRunReason.StatusChange);
		}
	}

	private void SearchResultToggleButton_Click(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.DataContext is not SearchResultsListItem { IsGroup: true, Group: not null } item)
		{
			return;
		}

		if (!_expandedSearchGroups.Add(item.Group.GroupKey))
		{
			_expandedSearchGroups.Remove(item.Group.GroupKey);
		}

		RebindSearchResultsFromCache(item.Group.GroupKey);
	}

	private async void AnalysisSenderGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if ((sender as System.Windows.Controls.DataGrid)?.SelectedItem is MailLogInspectorBreakdownRow row)
		{
			await OpenDomainInSearchAsync(row.Key, sender: true);
		}
	}

	private async void AnalysisRecipientGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if ((sender as System.Windows.Controls.DataGrid)?.SelectedItem is MailLogInspectorBreakdownRow row)
		{
			await OpenDomainInSearchAsync(row.Key, sender: false);
		}
	}

	private async void ImportButton_Click(object sender, RoutedEventArgs e)
	{
		Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
		{
			Multiselect = true,
			Filter = "CSV or ZIP (*.csv;*.zip)|*.csv;*.zip|CSV (*.csv)|*.csv|ZIP (*.zip)|*.zip",
			InitialDirectory = ResolveDefaultImportDirectory()
		};
		if (openFileDialog.ShowDialog(this) == true)
		{
			await ImportFilesAsync(openFileDialog.FileNames);
		}
	}

	private void ImportDropZone_DragOver(object sender, System.Windows.DragEventArgs e)
	{
		e.Effects = (_activeArchiveMonthKey == null && e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None);
		e.Handled = true;
	}

	private async void ImportDropZone_Drop(object sender, System.Windows.DragEventArgs e)
	{
		if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
		{
			string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
			await ImportFilesAsync(files);
		}
	}

	private async Task ImportFilesAsync(IEnumerable<string> files)
	{
		if (_activeArchiveMonthKey != null)
		{
			StatusTextBlock.Text = "Ga terug naar de actuele database om te importeren.";
			ImportProgressTextBlock.Text = "Importeren is uitgeschakeld in archiefmodus.";
			return;
		}

		List<string> importableFiles = files.Where((string path) => File.Exists(path)).Where(delegate(string path)
		{
			string extension = Path.GetExtension(path);
			return extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
		}).ToList();
		if (importableFiles.Count == 0)
		{
			StatusTextBlock.Text = "Geen CSV- of ZIP-bestanden geselecteerd.";
			return;
		}
		try
		{
			SetImportBusy(busy: true, $"Importeren: {importableFiles.Count} bestand(en)...");
			int importedRows = 0;
			int archivedRows = 0;
			int errorRows = 0;
			for (int index = 0; index < importableFiles.Count; index++)
			{
				string file = importableFiles[index];
				Progress<MailLogInspectorImportProgress> progress = new Progress<MailLogInspectorImportProgress>(delegate(MailLogInspectorImportProgress value)
				{
					ApplyImportProgress(value, index + 1, importableFiles.Count, Path.GetFileName(file));
				});
				string sourceHash = CalculateSourceHash(file);
				MailLogInspectorLog.Info(
					"import",
					$"Bron={ReportImportSource.Manual} | Bestand={Path.GetFileName(file)} | Import gestart");
				MailLogInspectorImportResult mailLogInspectorImportResult = ((!Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase)) ? (await _importService.ImportCsvAsync(file, CancellationToken.None, progress, finalizeBatch: false)) : (await _importService.ImportZipAsync(file, CancellationToken.None, progress, finalizeBatch: false)));
				if (!mailLogInspectorImportResult.AlreadyImported && mailLogInspectorImportResult.ImportId > 0)
				{
					_reportSyncOperationalStore.RecordImportSource(new ReportImportSourceRow(
						sourceHash,
						ReportImportSource.Manual,
						Path.GetFileName(file),
						mailLogInspectorImportResult.ReportStart?.Date,
						DateTime.UtcNow));
				}
				MailLogInspectorLog.Info(
					"import",
					$"Bron={ReportImportSource.Manual} | Bestand={Path.GetFileName(file)} | " +
					(mailLogInspectorImportResult.AlreadyImported ? "Reeds geïmporteerd" : "Import geslaagd"));
				importedRows += mailLogInspectorImportResult.UpsertedCount;
				archivedRows += mailLogInspectorImportResult.ArchivedUpsertedCount;
				errorRows += mailLogInspectorImportResult.ErrorCount;
			}
			StatusTextBlock.Text = $"Optimaliseren... {importedRows:n0} actieve rijen, {archivedRows:n0} archiefrijen bijgewerkt.";
			ImportProgressTextBlock.Text = "Database optimaliseren voor snel zoeken...";
			ImportProgressBar.IsIndeterminate = true;
			await Task.Run((Action)_importService.FinalizeBatch);
			StatusTextBlock.Text = $"Import gereed. {importedRows:n0} actieve rijen en {archivedRows:n0} archiefrijen bijgewerkt.";
			ImportProgressBar.Value = 100.0;
			ImportProgressTextBlock.Text = errorRows == 0 ? "Import gereed zonder rij-fouten." : $"Import gereed met {errorRows:n0} rij-fout(en).";
		}
		catch (Exception ex)
		{
			MailLogInspectorLog.Error("import", $"Bron={ReportImportSource.Manual} | Import mislukt", ex);
			StatusTextBlock.Text = "Import mislukt: " + ex.Message;
			ImportProgressTextBlock.Text = "Import afgebroken.";
		}
		finally
		{
			SetImportBusy(busy: false, StatusTextBlock.Text);
		}
		await RefreshDashboardAsync();
	}

	private static string CalculateSourceHash(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}

	private async Task RefreshDashboardAsync(bool invalidateDataViews = true)
	{
		if (invalidateDataViews)
		{
			InvalidateSenderDomainDashboard();
			ClearSearchResults();
			ResetAnalysisResults();
		}
		await Task.Run((Action)RefreshBeheer);
	}

	private async Task RunSearchAsync(SearchRunReason reason)
	{
		if (reason == SearchRunReason.FreshSearch)
		{
			ClearSearchResults();
		}
		MailLogInspectorSearchCriteria criteria = BuildSearchCriteria();
		int limit = ReadSearchLimit();
		if (reason == SearchRunReason.LoadMore && _lastSearchCriteria != null)
		{
			criteria = _lastSearchCriteria;
		}
		if (reason != SearchRunReason.FreshSearch && _currentSearchLimit > 0)
		{
			limit = _currentSearchLimit;
		}
		_lastSearchCriteria = criteria;
		_currentSearchLimit = limit;
		_searchCancellation?.Cancel();
		CancellationTokenSource searchCancellation = new CancellationTokenSource();
		_searchCancellation = searchCancellation;
		CancellationToken cancellationToken = searchCancellation.Token;
		SetSearchExecutionState(isRunning: true);
		SearchRunStateTextBlock.Text = "Zoeken draait direct op SQLite...";
		SearchRunDetailTextBlock.Text = "Resultaten worden geladen.";
		try
		{
		(IReadOnlyList<MailLogInspectorSearchRow> Rows, MailLogInspectorSearchSummary Summary) result = await Task.Run(delegate
			{
				using IDisposable measurement = MailLogInspectorLog.Measure("search", "Zoeken en samenvatten");
				cancellationToken.ThrowIfCancellationRequested();
				IReadOnlyList<MailLogInspectorSearchRow> rows = _searchService.Search(criteria, limit, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				MailLogInspectorSearchSummary summary = _searchService.ReadSummary(criteria, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				return (rows, summary);
			}, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			_lastSearchRows = result.Rows;
			_lastVisibleSearchRows = ApplySearchStatusFilter(result.Rows);
			_canLoadMoreSearchResults = limit < UnlimitedSearchLimit && result.Rows.Count == limit;
			RebindSearchResultsFromCache(
				expandSingleSenderGroup: reason == SearchRunReason.FreshSearch
					&& (criteria.Sender is not null || criteria.SenderDomain is not null));
			UpdateSearchSummary(result.Summary);
			SearchRunStateTextBlock.Text = result.Summary.TotalCount == 0
				? "Zoeken gereed. Geen resultaten gevonden."
				: ((result.Rows.Count == limit) ? $"Zoeken gereed. Eerste {_lastVisibleSearchRows.Count} van {result.Summary.TotalCount} resultaat(en) getoond." : $"Zoeken gereed. {_lastVisibleSearchRows.Count} van {result.Summary.TotalCount} resultaat(en) getoond.");
			SearchRunDetailTextBlock.Text = "Direct op SQLite, zonder cache-opwarming.";
			if (reason == SearchRunReason.FreshSearch)
			{
				await RefreshSenderDomainDashboardForFreshSearchAsync(criteria, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
			if (ReferenceEquals(_searchCancellation, searchCancellation))
			{
				SearchRunStateTextBlock.Text = "Zoeken gestopt.";
				SearchRunDetailTextBlock.Text = "Pas filters aan en start opnieuw.";
			}
		}
		catch (Exception ex)
		{
			MailLogInspectorLog.Error("search", "Zoekopdracht mislukt", ex);
			if (ReferenceEquals(_searchCancellation, searchCancellation))
			{
				SearchRunStateTextBlock.Text = "Zoeken mislukt: " + ex.Message;
				SearchRunDetailTextBlock.Text = "Zoekopdracht afgebroken.";
			}
		}
		finally
		{
			if (ReferenceEquals(_searchCancellation, searchCancellation))
			{
				SetSearchExecutionState(isRunning: false);
				_searchCancellation = null;
			}
			searchCancellation.Dispose();
		}
	}

	private async Task RefreshAnalysisAsync()
	{
		if (!TryBuildAnalysisCriteria(out MailLogInspectorSearchCriteria? criteria, out string? validationMessage))
		{
			AnalysisRunStateTextBlock.Text = validationMessage ?? "Kies eerst een geldige periode.";
			AnalysisRunDurationTextBlock.Text = "Nog niet uitgevoerd.";
			ResetAnalysisResults();
			UpdateAnalysisExecutionState();
			return;
		}

		_analysisRefreshCancellation?.Cancel();
		_analysisRefreshCancellation?.Dispose();
		_analysisRefreshCancellation = new CancellationTokenSource();
		CancellationToken cancellationToken = _analysisRefreshCancellation.Token;
		UpdateAnalysisExecutionState(isRunning: true);
		AnalysisRunStateTextBlock.Text = "Analyse draait direct op de database...";
		AnalysisDataCoverageTextBlock.Text = "Direct op SQLite, zonder cache-opwarming.";
		DateTime startedAt = DateTime.UtcNow;
		try
		{
			int limit = ReadAnalysisTopDomainLimit();
			MailLogInspectorAnalysisSummary summary = await Task.Run(() => _analysisService.BuildSummary(criteria!, limit, cancellationToken), cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			TimeSpan elapsed = DateTime.UtcNow - startedAt;
			ApplyAnalysisSummary(summary, elapsed);
			AnalysisRunStateTextBlock.Text = summary.TotalCount == 0 ? "Geen analysegegevens voor dit tijdvak." : "Analyse gereed.";
			AnalysisRunDurationTextBlock.Text = $"Analyse gereed in {elapsed.TotalSeconds:0.0}s";
		}
		catch (OperationCanceledException)
		{
			AnalysisRunStateTextBlock.Text = "Analyse geannuleerd.";
			AnalysisDataCoverageTextBlock.Text = "Direct op SQLite, zonder cache-opwarming.";
		}
		catch (Exception ex)
		{
			AnalysisRunStateTextBlock.Text = "Analyse mislukt: " + ex.Message;
			AnalysisDataCoverageTextBlock.Text = "Analyse afgebroken.";
		}
		finally
		{
			UpdateAnalysisExecutionState();
		}
	}

	private void RefreshBeheer()
	{
		MailLogInspectorDatabaseStats stats = _store.ReadDatabaseStats();
		IReadOnlyList<MailLogInspectorImportedFile> imports = _store.ReadRecentImports(100);
		IReadOnlyList<ArchiveMonthListItem> archiveMonths = ReadArchiveMonths();
		GmailReportConfig gmailConfig = _gmailOperationalStore.LoadConfig();
		_gmailOperationalStore.BackfillMissingSourceHashes(imports);
		IReadOnlyList<GmailReportHistoryRow> gmailHistory = _gmailOperationalStore.ReadHistory(100);
		IReadOnlyList<ReportImportSourceRow> importSources = _reportSyncOperationalStore.ReadImportSources(500);
		IReadOnlyList<ImportHistoryListItem> importHistory = ImportHistoryListBuilder.Build(imports, gmailHistory, importSources);
		bool archiveMode = _activeArchiveMonthKey != null;
		DateTime latencyThrough = archiveMode
			? stats.ImportThrough?.Date ?? DateTime.Today.AddDays(-1)
			: DateTime.Today.AddDays(-1);
		IReadOnlyList<MailLogInspectorDeliveryLatencyDay> deliveryLatency =
			_store.ReadDeliveryLatencyTrend(latencyThrough.AddDays(-29), latencyThrough);
		bool latencyPending = stats.MailItemCount > 0 && deliveryLatency.Count == 0;
		((DispatcherObject)this).Dispatcher.Invoke((Action)delegate
		{
			DatabaseSizeTextBlock.Text = FormatCompactBytes(stats.DatabaseSizeBytes);
			RecordCountTextBlock.Text = FormatCompactCount(stats.MailItemCount);
			ImportFromTextBlock.Text = MailLogInspectorDisplayFormats.DateTime(stats.ImportFrom);
			ImportThroughTextBlock.Text = MailLogInspectorDisplayFormats.DateTime(stats.ImportThrough);
			LastUpdateTextBlock.Text = MailLogInspectorDisplayFormats.DateTime(stats.LastUpdateAt);
			DatabaseHealthTextBlock.Text = archiveMode ? $"Archiefmodus: {_activeArchiveMonthKey}" : (stats.MailItemCount > 0 ? "Actuele database: laatste 90 dagen" : "Actuele database: leeg");
			ImportCountTextBlock.Text = stats.ImportCount.ToString();
			StorageWorkspaceTextBlock.Text = _store.DatabasePath;
			ArchivePathTextBlock.Text = _workspace.ArchiveDatabaseDirectory;
			ReturnToActiveDatabaseButton.Visibility = archiveMode ? Visibility.Visible : Visibility.Collapsed;
			MonthArchiveGrid.ItemsSource = archiveMonths;
			ImportsGrid.ItemsSource = importHistory;
			ApplyImportQualitySummary(imports);
			ApplyDeliveryLatencyTrend(deliveryLatency, latencyPending);
			SyncGmailReportsButton.IsEnabled = !_gmailSyncIsRunning && _activeArchiveMonthKey == null && IsReportSyncConfigurationReady(gmailConfig);
			UpdateImportControlsEnabled();
		});
	}

	private sealed record ImportQualityComparisonBar(
		string Label,
		string LatestDisplay,
		string PreviousWeekDisplay,
		double PreviousWeekBarHeight,
		double LatestBarHeight,
		System.Windows.Media.Brush LatestBrush);

	private sealed record ImportQualityComparisonData(
		IReadOnlyList<ImportQualityComparisonBar> Bars,
		int AcceptedScale,
		int BounceScale,
		int PreviousWeekAccepted,
		int PreviousWeekDelivered,
		int PreviousWeekBounce,
		double PreviousWeekDeliveredRatio,
		bool HasPreviousWeek);
	private void ApplyImportQualitySummary(IReadOnlyList<MailLogInspectorImportedFile> imports)
	{
		ImportQualityComparisonData comparison = BuildImportQualityComparisonGroups(imports);
		ImportQualityMeasureCardsItemsControl.ItemsSource = comparison.Bars;

		MailLogInspectorImportedFile? latest = imports.FirstOrDefault();
		if (latest is null)
		{
			ImportQualityBounceCauseItemsControl.ItemsSource = Array.Empty<MailLogInspectorImportCause>();
			return;
		}

		ImportQualityBounceCauseItemsControl.ItemsSource = latest.BounceCauses;
	}

	private static ImportQualityComparisonData BuildImportQualityComparisonGroups(IReadOnlyList<MailLogInspectorImportedFile> imports)
	{
		MailLogInspectorImportedFile? latest = imports.FirstOrDefault(IsDailyReportImport) ?? imports.FirstOrDefault();
		if (latest is null)
		{
			return new ImportQualityComparisonData(Array.Empty<ImportQualityComparisonBar>(), 0, 0, 0, 0, 0, 0.0, false);
		}

		DateTime previousWeekDate = GetImportComparisonDate(latest).AddDays(-7);
		MailLogInspectorImportedFile? previousWeekImport = imports
			.Where(import => import.ImportId != latest.ImportId
				&& IsDailyReportImport(import)
				&& GetImportComparisonDate(import) == previousWeekDate)
			.OrderByDescending(import => import.ImportedAt)
			.ThenByDescending(import => import.ImportId)
			.FirstOrDefault();
		bool hasPreviousWeek = previousWeekImport is not null;
		int previousWeekAccepted = previousWeekImport?.RowCount ?? 0;
		int previousWeekDelivered = previousWeekImport?.DeliveredCount ?? 0;
		int previousWeekBounce = previousWeekImport?.BounceCount ?? 0;
		double previousWeekDeliveredRatio = Ratio(previousWeekDelivered, previousWeekAccepted);

		int acceptedScale = Math.Max(Math.Max(latest.RowCount, latest.DeliveredCount), Math.Max(previousWeekAccepted, previousWeekDelivered));
		int bounceScale = Math.Max(latest.BounceCount, previousWeekBounce);
		acceptedScale = Math.Max(1, acceptedScale);
		bounceScale = Math.Max(1, bounceScale);

		List<ImportQualityComparisonBar> bars = new()
		{
			BuildImportQualityComparisonBar("Geaccepteerd", latest.RowCount, previousWeekAccepted, acceptedScale, CreateBrush("#1F5F8B"), hasPreviousWeek),
			BuildImportQualityComparisonBar("Afgeleverd", latest.DeliveredCount, previousWeekDelivered, acceptedScale, CreateBrush("#2E8B57"), hasPreviousWeek),
			BuildImportQualityComparisonBar("Bounced", latest.BounceCount, previousWeekBounce, bounceScale, CreateBrush("#D33A2C"), hasPreviousWeek)
		};

		return new ImportQualityComparisonData(bars, acceptedScale, bounceScale, previousWeekAccepted, previousWeekDelivered, previousWeekBounce, previousWeekDeliveredRatio, hasPreviousWeek);
	}

	private static DateTime GetImportComparisonDate(MailLogInspectorImportedFile import)
	{
		return (import.ReportEnd ?? import.ImportedAt).Date;
	}
	private static bool IsDailyReportImport(MailLogInspectorImportedFile import)
	{
		if (!import.ReportStart.HasValue || !import.ReportEnd.HasValue)
		{
			return false;
		}

		TimeSpan duration = import.ReportEnd.Value - import.ReportStart.Value;
		return duration > TimeSpan.Zero && duration <= TimeSpan.FromHours(48);
	}

	private static ImportQualityComparisonBar BuildImportQualityComparisonBar(string label, int latest, int previousWeek, int scale, System.Windows.Media.Brush latestBrush, bool hasPreviousWeek)
	{
		return new ImportQualityComparisonBar(
			label,
			FormatCompactCount(latest),
			hasPreviousWeek ? $"Vorige week {FormatCompactCount(previousWeek)}" : "Geen gegevens vorige week",
			hasPreviousWeek ? CalculateVolumeBarHeight(previousWeek, scale) : 0.0,
			CalculateVolumeBarHeight(latest, scale),
			latestBrush);
	}
	private static System.Windows.Media.Brush CreateBrush(string color)
	{
		System.Windows.Media.SolidColorBrush brush = new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
		brush.Freeze();
		return brush;
	}

	private static double CalculateVolumeBarHeight(int value, int scale)
	{
		const double maxHeight = 104.0;
		if (value <= 0 || scale <= 0)
		{
			return 2.0;
		}

		return Math.Max(4.0, Math.Min(maxHeight, value * maxHeight / scale));
	}

	private static double CalculateTrendBarHeight(int value, int scale)
	{
		const double maxHeight = 108.0;
		if (value <= 0 || scale <= 0)
		{
			return 2.0;
		}

		return Math.Max(3.0, Math.Min(maxHeight, value * maxHeight / scale));
	}

	private static double Ratio(int value, int total)
	{
		return total <= 0 ? 0.0 : (double)value / total;
	}
	private void SwitchActiveStore(string databasePath, string? archiveMonthKey, bool initialize = true)
	{
		InvalidateSenderDomainDashboard();
		_store = new MailLogInspectorStore(databasePath);
		if (initialize)
		{
			_store.Initialize();
		}
		_importService = new MailLogInspectorImportService(_store, _workspace);
		_searchService = new MailLogInspectorSearchService(_store);
		_analysisService = new MailLogInspectorAnalysisService(_store);
		_activeArchiveMonthKey = archiveMonthKey;
	}

	private IReadOnlyList<ArchiveMonthListItem> ReadArchiveMonths()
	{
		if (!Directory.Exists(_workspace.ArchiveDatabaseDirectory))
		{
			return Array.Empty<ArchiveMonthListItem>();
		}

		List<ArchiveMonthListItem> months = new();
		foreach (string databasePath in Directory.EnumerateFiles(_workspace.ArchiveDatabaseDirectory, "????-??.sqlite").OrderByDescending(Path.GetFileNameWithoutExtension))
		{
			try
			{
				MailLogInspectorStore archiveStore = new MailLogInspectorStore(databasePath);
				MailLogInspectorDatabaseStats stats = archiveStore.ReadDatabaseStats();
				string monthKey = Path.GetFileNameWithoutExtension(databasePath);
				months.Add(new ArchiveMonthListItem(
					monthKey,
					databasePath,
					stats.MailItemCount,
					FormatBytes(stats.DatabaseSizeBytes),
					MailLogInspectorDisplayFormats.DateTime(stats.ImportFrom),
					MailLogInspectorDisplayFormats.DateTime(stats.ImportThrough)));
			}
			catch
			{
				// Broken archive files should not block Beheer; they remain visible in the folder for manual inspection.
			}
		}

		return months;
	}

	private async void OpenArchiveMonthButton_Click(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.DataContext is not ArchiveMonthListItem archiveMonth || !File.Exists(archiveMonth.DatabasePath))
		{
			return;
		}

		SwitchActiveStore(archiveMonth.DatabasePath, archiveMonth.MonthKey);
		await EnsureCurrentSenderDomainAnalyticsReadyAsync();
		DateTime monthStart = DateTime.ParseExact(archiveMonth.MonthKey + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
		DateTime monthEnd = monthStart.AddMonths(1).AddDays(-1);
		SearchFromDatePicker.SelectedDate = monthStart;
		SearchThroughDatePicker.SelectedDate = monthEnd;
		AnalysisFromDatePicker.SelectedDate = monthStart;
		AnalysisThroughDatePicker.SelectedDate = monthEnd;
		RefreshDatePickerConstraints();
		ClearSearchResults();
		ResetAnalysisResults();
		StatusTextBlock.Text = $"Archiefmodus: {archiveMonth.MonthKey}";
		ImportProgressTextBlock.Text = "Zoeken en analyse gebruiken nu dit maandarchief.";
		await RefreshDashboardAsync();
		await RefreshAnalysisAsync();
	}

	private void OpenWorkspaceFolderButton_Click(object sender, RoutedEventArgs e)
	{
		Directory.CreateDirectory(_workspace.RootDirectory);
		Process.Start(new ProcessStartInfo
		{
			FileName = _workspace.RootDirectory,
			UseShellExecute = true
		});
	}

	private async void ReturnToActiveDatabaseButton_Click(object sender, RoutedEventArgs e)
	{
		// The active database may require an EXE-managed rebuild. Do not initialize
		// the legacy schema before the Loaded startup flow can perform that rebuild.
		SwitchActiveStore(_workspace.DatabasePath, null, initialize: false);
		await EnsureCurrentSenderDomainAnalyticsReadyAsync();
		DateTime yesterday = DateTime.Today.AddDays(-1.0);
		SearchFromDatePicker.SelectedDate = yesterday;
		SearchThroughDatePicker.SelectedDate = DateTime.Today;

		AnalysisFromDatePicker.SelectedDate = yesterday;
		AnalysisThroughDatePicker.SelectedDate = yesterday;
		RefreshDatePickerConstraints();
		StatusTextBlock.Text = "Actuele database geopend.";
		ImportProgressTextBlock.Text = "Importeren is weer beschikbaar.";
		await RefreshDashboardAsync();
		await RefreshAnalysisAsync();
	}

	private void UpdateImportControlsEnabled()
	{
		bool enabled = _activeArchiveMonthKey == null;
		ImportDropZone.IsEnabled = enabled;
		ImportFilesButton.IsEnabled = enabled;
	}

	private MailLogInspectorSearchCriteria BuildSearchCriteria()
	{
		var senderFilter = ParseAddressFilter(SenderTextBox.Text);
		var recipientFilter = ParseAddressFilter(RecipientTextBox.Text);
		return new MailLogInspectorSearchCriteria(SearchFromDatePicker.SelectedDate?.Date ?? DateTime.Today.AddDays(-90.0), EndOfDay(SearchThroughDatePicker.SelectedDate?.Date ?? DateTime.Today), senderFilter.Email, recipientFilter.Email, senderFilter.Domain, recipientFilter.Domain, _selectedSearchStatus);
	}

	private bool TryBuildAnalysisCriteria(out MailLogInspectorSearchCriteria? criteria, out string? validationMessage)
	{
		criteria = null;
		validationMessage = null;
		DateTime? fromDate = AnalysisFromDatePicker.SelectedDate?.Date;
		DateTime? throughDate = AnalysisThroughDatePicker.SelectedDate?.Date;
		if (!fromDate.HasValue || !throughDate.HasValue)
		{
			validationMessage = "Vul zowel Van als Tot en met in om de analyse te starten.";
			return false;
		}

		if (throughDate.Value.Date < fromDate.Value.Date)
		{
			validationMessage = "De einddatum moet gelijk aan of later zijn dan de startdatum.";
			return false;
		}

		var senderFilter = ParseAddressFilter(AnalysisSenderTextBox.Text);
		var recipientFilter = ParseAddressFilter(AnalysisRecipientTextBox.Text);
		criteria = new MailLogInspectorSearchCriteria(fromDate.Value, EndOfDay(throughDate.Value), senderFilter.Email, recipientFilter.Email, senderFilter.Domain, recipientFilter.Domain, null);
		return true;
	}

	private static (string? Email, string? Domain) ParseAddressFilter(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return (Email: null, Domain: null);
		}
		string text = value.Trim().TrimStart('@');
		if (!text.Contains('@', StringComparison.Ordinal))
		{
			return (Email: null, Domain: text.ToLowerInvariant());
		}
		return (Email: text, Domain: null);
	}

	private static DateTime EndOfDay(DateTime value)
	{
		return value.Date.AddDays(1.0).AddTicks(-1L);
	}

	private static string FormatBytes(long bytes)
	{
		if ((double)bytes >= 1048576.0)
		{
			return $"{(double)bytes / 1048576.0:0.0} MB";
		}
		return $"{(double)bytes / 1024.0:0.0} KB";
	}

	private static string FormatCompactBytes(long bytes)
	{
		if (bytes >= 1073741824L)
		{
			return $"{(double)bytes / 1073741824.0:0.0} GB";
		}

		return FormatBytes(bytes);
	}

	private static string FormatCompactCount(long value)
	{
		if (value >= 1000000L)
		{
			return $"{(double)value / 1000000.0:0.0}M";
		}

		return value.ToString("n0", MailLogInspectorDisplayFormats.Culture);
	}

	private string ResolveDefaultImportDirectory()
	{
		return _workspace.IncomingDirectory;
	}

	private async Task EnsureDatabaseReadyAsync()
	{
		try
		{
			SetImportBusy(busy: true, "Database voorbereiden...");
			Progress<MailLogInspectorImportProgress> progress = new Progress<MailLogInspectorImportProgress>(delegate(MailLogInspectorImportProgress value)
			{
				ApplyImportProgress(value, 1, 1, "database");
			});
			MailLogInspectorWorkspaceRebuildResult mailLogInspectorWorkspaceRebuildResult = await Task.Run(() => _rebuilder.RebuildIfRequiredAsync(CancellationToken.None, progress), CancellationToken.None);
			int removedRows = await Task.Run(() => _store.ApplyRetention(MailLogInspectorRetentionPolicy.ActiveCutoffDate(DateTime.Today)));
			StatusTextBlock.Text = mailLogInspectorWorkspaceRebuildResult.WasRebuilt
				? BuildRebuildCompletionText(mailLogInspectorWorkspaceRebuildResult)
				: (removedRows > 0 ? $"Database klaar. {removedRows:n0} oude actieve rij(en) opgeschoond." : "Database klaar.");
		}
		catch (Exception ex)
		{
			StatusTextBlock.Text = "Database voorbereiding mislukt: " + ex.Message;
			ImportProgressTextBlock.Text = "Database voorbereiding afgebroken.";
			return;
		}
		finally
		{
			SetImportBusy(busy: false, StatusTextBlock.Text);
		}
		await AdjustInitialDateRangeAsync();
		await RefreshDashboardAsync();
		UpdateAnalysisExecutionState();
		await RefreshAnalysisAsync();
	}


	private string BuildRebuildCompletionText(MailLogInspectorWorkspaceRebuildResult result)
	{
		string message = $"Database opnieuw opgebouwd. {result.ImportedRowCount:n0} rijen uit {result.ImportedFileCount} bestand(en).";
		string? comparisonDatabasePath = result.SourceDatabasePath ?? result.BackupDatabasePath;
		if (string.IsNullOrWhiteSpace(comparisonDatabasePath) || !File.Exists(comparisonDatabasePath) || !File.Exists(_workspace.DatabasePath))
		{
			return message;
		}

		long oldBytes = new FileInfo(comparisonDatabasePath).Length;
		long newBytes = new FileInfo(_workspace.DatabasePath).Length;
		double reduction = oldBytes <= 0 ? 0.0 : (oldBytes - newBytes) * 100.0 / oldBytes;
		string sizeMessage = $" Grootte: {FormatCompactBytes(oldBytes)} naar {FormatCompactBytes(newBytes)} ({reduction:0.0}% kleiner).";
		MailLogInspectorLog.Info("rebuild", message + sizeMessage);
		return message + sizeMessage;
	}
	private void ApplyImportProgress(MailLogInspectorImportProgress progress, int currentFile, int totalFiles, string fileName)
	{
		string stage = progress.Stage switch
		{
			MailLogInspectorImportStage.Preparing => "Voorbereiden",
			MailLogInspectorImportStage.Importing => "Lezen en opslaan",
			MailLogInspectorImportStage.Completed => "Afronden",
			_ => "Import"
		};
		StatusTextBlock.Text = totalFiles > 1 ? $"{stage}: bestand {currentFile} van {totalFiles}" : stage;
		ImportProgressBar.IsIndeterminate = progress.Stage == MailLogInspectorImportStage.Preparing;
		ImportProgressBar.Value = ((progress.Stage == MailLogInspectorImportStage.Preparing) ? 0.0 : Math.Max(0.0, Math.Min(100.0, progress.PercentComplete)));
		ImportProgressTextBlock.Text = ((progress.Stage == MailLogInspectorImportStage.Importing) ? $"{progress.PercentComplete:0.0}% | {progress.RowsRead:n0} rijen | {FormatBytes(progress.BytesRead)} / {FormatBytes(progress.TotalBytes)}" : $"{progress.Message} ({currentFile}/{totalFiles})");
	}

	private void SetImportBusy(bool busy, string text)
	{
		bool importEnabled = !busy && _activeArchiveMonthKey == null;
		ImportDropZone.IsEnabled = importEnabled;
		ImportFilesButton.IsEnabled = importEnabled;
		SearchButton.IsEnabled = !busy;
		LoadMoreSearchResultsButton.IsEnabled = !busy && _lastSearchCriteria != null && LoadMoreSearchResultsButton.IsEnabled;
		StatusTextBlock.Text = text;
		if (!busy)
		{
			ImportProgressBar.IsIndeterminate = false;
		}
	}

	private void ClearSearchResults()
	{
		SearchResultsGrid.ItemsSource = Array.Empty<SearchResultsListItem>();
		SearchResultsGrid.SelectedIndex = -1;
		_lastSearchRows = Array.Empty<MailLogInspectorSearchRow>();
		_lastVisibleSearchRows = Array.Empty<MailLogInspectorSearchRow>();
		_searchResultsListItems = Array.Empty<SearchResultsListItem>();
		_expandedSearchGroups.Clear();
		LoadMoreSearchResultsButton.IsEnabled = false;
		_selectedSearchStatus = null;
		SelectSearchStatusFilter(null);
		UpdateSearchSummary(new MailLogInspectorSearchSummary(0, 0, 0, 0));
		_canLoadMoreSearchResults = false;
		SetSearchExecutionState(false);
		SearchRunStateTextBlock.Text = "Kies filters en klik Zoeken.";
		SearchRunDetailTextBlock.Text = "Direct op SQLite, zonder cache-opwarming.";
	}

	private async Task OpenDomainInSearchAsync(string domain, bool sender)
	{
		if (string.IsNullOrWhiteSpace(domain) || domain.StartsWith("(", StringComparison.Ordinal))
		{
			return;
		}
		SearchFromDatePicker.SelectedDate = AnalysisFromDatePicker.SelectedDate;
		SearchThroughDatePicker.SelectedDate = AnalysisThroughDatePicker.SelectedDate;
		if (sender)
		{
			SenderTextBox.Text = domain;
			RecipientTextBox.Text = string.Empty;
		}
		else
		{
			SenderTextBox.Text = string.Empty;
			RecipientTextBox.Text = domain;
		}
		SelectSearchStatusFilter(null);
		SearchRunStateTextBlock.Text = sender ? $"Afzenderdomein overgenomen uit analyse: {domain}" : $"Ontvangerdomein overgenomen uit analyse: {domain}";
		SearchRunDetailTextBlock.Text = "Klik Zoeken om resultaten op te halen.";
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            MainTabControl.SelectedIndex = 0;
            MainTabControl.Focus();
        }));
	}

	private int ReadSearchLimit()
	{
		int configuredLimit = ReadComboTagAsInt(SearchLimitComboBox, 500);
		return configuredLimit < 0 ? UnlimitedSearchLimit : configuredLimit;
	}

	private int ReadAnalysisTopDomainLimit()
	{
		return ReadComboTagAsInt(AnalysisTopDomainLimitComboBox, 10);
	}

	private void AnalysisInputs_Changed(object sender, RoutedEventArgs e)
	{
		UpdateAnalysisExecutionState();
	}

	private void UpdateAnalysisExecutionState(bool isRunning = false)
	{
		_analysisIsRunning = isRunning;
		if (!AreAnalysisControlsReady())
		{
			return;
		}

		bool isReady = TryBuildAnalysisCriteria(out _, out string? validationMessage);
		AnalysisRunButton.IsEnabled = !isRunning && isReady;
		AnalysisRunProgressBar.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
		AnalysisRunProgressBar.IsIndeterminate = isRunning;
		if (!isRunning)
		{
			AnalysisRunProgressBar.Value = 0.0;
		}
		if (!isRunning && !isReady)
		{
			AnalysisRunStateTextBlock.Text = validationMessage ?? "Kies eerst een geldige periode.";
			AnalysisDataCoverageTextBlock.Text = "Direct op SQLite, zonder cache-opwarming.";
		}
	}

	private bool AreAnalysisControlsReady()
	{
		return AnalysisFromDatePicker != null && AnalysisThroughDatePicker != null && AnalysisSenderTextBox != null && AnalysisRecipientTextBox != null && AnalysisTopDomainLimitComboBox != null && AnalysisRunButton != null && AnalysisRunStateTextBlock != null && AnalysisRunProgressBar != null;
	}

	private void ResetAnalysisResults()
	{
		AnalysisTotalCountTextBlock.Text = "0";
		DeliveredCountTextBlock.Text = "0";
		AnalysisDeliveredPercentTextBlock.Text = "0%";
		UnderwayCountTextBlock.Text = "0";
		BounceCountTextBlock.Text = "0";
		SenderVolumeGrid.ItemsSource = Array.Empty<MailLogInspectorBreakdownRow>();
		SenderSuccessGrid.ItemsSource = Array.Empty<MailLogInspectorBreakdownRow>();
		RecipientProblemVolumeGrid.ItemsSource = Array.Empty<MailLogInspectorBreakdownRow>();
		RecipientProblemRateGrid.ItemsSource = Array.Empty<MailLogInspectorBreakdownRow>();
		ResponseCodesLeftGrid.ItemsSource = Array.Empty<MailLogInspectorValueMeaningCount>();
		ResponseCodesRightGrid.ItemsSource = Array.Empty<MailLogInspectorValueMeaningCount>();
	}

	private void ApplyAnalysisSummary(MailLogInspectorAnalysisSummary summary, TimeSpan elapsed)
	{
		AnalysisTotalCountTextBlock.Text = summary.TotalCount.ToString();
		DeliveredCountTextBlock.Text = summary.DeliveredCount.ToString();
		AnalysisDeliveredPercentTextBlock.Text = FormatDeliveredPercent(summary.DeliveredCount, summary.TotalCount);
		UnderwayCountTextBlock.Text = summary.UnderwayCount.ToString();
		BounceCountTextBlock.Text = summary.BounceCount.ToString();
		SenderVolumeGrid.ItemsSource = summary.SenderVolumeRows;
		SenderSuccessGrid.ItemsSource = summary.SenderLowestSuccessRows;
		RecipientProblemVolumeGrid.ItemsSource = summary.RecipientProblemVolumeRows;
		RecipientProblemRateGrid.ItemsSource = summary.RecipientHighestProblemRateRows;
		(IReadOnlyList<MailLogInspectorValueMeaningCount> left, IReadOnlyList<MailLogInspectorValueMeaningCount> right) = SplitResponseCodeRows(summary.TopResponseCodes);
		ResponseCodesLeftGrid.ItemsSource = left;
		ResponseCodesRightGrid.ItemsSource = right;
		AnalysisDataCoverageTextBlock.Text = $"Direct op SQLite, zonder cache-opwarming. {summary.TotalCount:n0} records in scope.";
		AnalysisRunStateTextBlock.Text = summary.TotalCount == 0
			? "Analyse gereed. Geen resultaten in het gekozen tijdvak."
			: $"Analyse gereed in {elapsed.TotalSeconds:0.0}s.";
	}

	public static (IReadOnlyList<MailLogInspectorValueMeaningCount> Left, IReadOnlyList<MailLogInspectorValueMeaningCount> Right) SplitResponseCodeRows(IReadOnlyList<MailLogInspectorValueMeaningCount> rows)
	{
		if (rows.Count == 0)
		{
			return (Array.Empty<MailLogInspectorValueMeaningCount>(), Array.Empty<MailLogInspectorValueMeaningCount>());
		}

		int midpoint = (rows.Count + 1) / 2;
		return (rows.Take(midpoint).ToArray(), rows.Skip(midpoint).ToArray());
	}

	private void SetSearchExecutionState(bool isRunning)
	{
		_searchIsRunning = isRunning;
		SearchButton.IsEnabled = !isRunning;
		LoadMoreSearchResultsButton.IsEnabled = !isRunning && _canLoadMoreSearchResults;
		ExportSearchResultsButton.IsEnabled = !isRunning && _lastVisibleSearchRows.Count > 0;
		SearchCancelButton.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
		SearchRunProgressBar.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
		SearchRunProgressBar.IsIndeterminate = isRunning;
		if (!isRunning)
		{
			SearchRunProgressBar.Value = 0.0;
		}
	}


	private static string NormalizeStatus(string? status)
	{
		return string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToLowerInvariant();
	}

	private static int ReadComboTagAsInt(System.Windows.Controls.ComboBox comboBox, int fallback)
	{
		if (comboBox.SelectedItem is ComboBoxItem { Tag: var tag } && int.TryParse(tag?.ToString(), out var result) && result != 0)
		{
			return result;
		}
		return fallback;
	}

	private void UpdateTopStatusPanelVisibility()
	{
		if (SearchTopStatusPanel == null || AnalysisTopStatusPanel == null || ManageTopStatusPanel == null || HelpTopStatusPanel == null || MainTabControl == null)
		{
			return;
		}

		int index = MainTabControl.SelectedIndex;
		SearchTopStatusPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
		AnalysisTopStatusPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
		ManageTopStatusPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
		HelpTopStatusPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
	}

	private void RebindSearchResultsFromCache(string? preferredSelectionKey = null, bool expandSingleSenderGroup = false)
	{
		if (SearchResultsGrid == null || _searchResultsStatusHeaderComboBox == null)
		{
			return;
		}

		_lastVisibleSearchRows = ApplySearchStatusFilter(_lastSearchRows);
		IReadOnlyList<SearchResultsGroup> groups = BuildSearchGroups(_lastVisibleSearchRows);
		if (expandSingleSenderGroup && groups.Count == 1)
		{
			_expandedSearchGroups.Add(groups[0].GroupKey);
		}
		_searchResultsListItems = FlattenSearchGroups(groups);
		SearchResultsGrid.ItemsSource = _searchResultsListItems;

		int selectedIndex = -1;
		if (!string.IsNullOrWhiteSpace(preferredSelectionKey))
		{
			selectedIndex = _searchResultsListItems
				.Select((item, index) => new { item, index })
				.FirstOrDefault(pair => string.Equals(pair.item.ItemKey, preferredSelectionKey, StringComparison.OrdinalIgnoreCase))?.index ?? -1;
		}

		if (selectedIndex < 0)
		{
			selectedIndex = _searchResultsListItems
				.Select((item, index) => new { item, index })
				.FirstOrDefault(static pair => !pair.item.IsGroup)?.index ?? (_searchResultsListItems.Count > 0 ? 0 : -1);
		}

		SearchResultsGrid.SelectedIndex = selectedIndex;
		if (selectedIndex >= 0)
		{
			SearchResultsGrid.ScrollIntoView(SearchResultsGrid.SelectedItem);
		}
	}

	private IReadOnlyList<MailLogInspectorSearchRow> ApplySearchStatusFilter(IReadOnlyList<MailLogInspectorSearchRow> rows)
	{
		if (!string.IsNullOrWhiteSpace(_selectedSearchStatus))
		{
			return rows.Where((MailLogInspectorSearchRow row) => string.Equals(row.Status, _selectedSearchStatus, StringComparison.OrdinalIgnoreCase)).ToList();
		}
		return rows;
	}

	private static IReadOnlyList<SearchResultsGroup> BuildSearchGroups(IReadOnlyList<MailLogInspectorSearchRow> rows)
	{
		return rows
			.GroupBy(static row => row.Sender, StringComparer.OrdinalIgnoreCase)
			.Select(static group => new SearchResultsGroup
			{
				GroupKey = group.Key,
				Sender = group.Key,
				Rows = group.OrderByDescending(static row => row.AcceptedAt ?? row.LastSeenAt).ThenBy(static row => row.Recipient, StringComparer.OrdinalIgnoreCase).ToList()
			})
			.OrderByDescending(static group => group.LatestRow.AcceptedAt ?? group.LatestRow.LastSeenAt)
			.ToList();
	}

	private IReadOnlyList<SearchResultsListItem> FlattenSearchGroups(IReadOnlyList<SearchResultsGroup> groups)
	{
		List<SearchResultsListItem> items = new();
		foreach (SearchResultsGroup group in groups)
		{
			bool isExpanded = _expandedSearchGroups.Contains(group.GroupKey);
			items.Add(new SearchResultsListItem
			{
				ItemKey = "group:" + group.GroupKey,
				IsGroup = true,
				IsExpanded = isExpanded,
				Level = 0,
				AcceptedDisplay = MailLogInspectorDisplayFormats.DateTime(group.LatestRow.AcceptedAt),
				SenderDisplay = group.Sender,
				RecipientDisplay = group.RecipientSummary,
				StatusDisplay = group.StatusSummary,
				DurationDisplay = string.Empty,
				CountDisplay = group.Rows.Count.ToString(),
				CountValue = group.Rows.Count,
				StatusSummary = group.StatusSummary,
				Group = group
			});

			if (!isExpanded)
			{
				continue;
			}

			foreach (MailLogInspectorSearchRow row in group.Rows)
			{
				items.Add(new SearchResultsListItem
				{
					ItemKey = "row:" + row.AcceptedAt?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + row.Sender + "|" + row.Recipient + "|" + row.LastSeenAt.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
					IsGroup = false,
					IsExpanded = false,
					Level = 1,
					AcceptedDisplay = MailLogInspectorDisplayFormats.DateTime(row.AcceptedAt),
					SenderDisplay = "    " + row.Sender,
					RecipientDisplay = row.Recipient,
					StatusDisplay = row.StatusDisplay,
					DurationDisplay = row.DurationDisplay,
					CountDisplay = string.Empty,
					CountValue = 0,
					StatusSummary = row.Status,
					Row = row
				});
			}
		}

		return items;
	}

	private void SelectSearchStatusFilter(string? status)
	{
		if (_searchResultsStatusHeaderComboBox == null)
		{
			_selectedSearchStatus = (string.IsNullOrWhiteSpace(status) ? null : status);
			return;
		}
		foreach (ComboBoxItem item in _searchResultsStatusHeaderComboBox.Items.OfType<ComboBoxItem>())
		{
			string? text = item.Tag?.ToString();
			if (string.Equals(text, status, StringComparison.OrdinalIgnoreCase) || (string.IsNullOrWhiteSpace(status) && string.IsNullOrWhiteSpace(text)))
			{
				_searchResultsStatusHeaderComboBox.SelectedItem = item;
				_selectedSearchStatus = (string.IsNullOrWhiteSpace(text) ? null : text);
				return;
			}
		}
		_searchResultsStatusHeaderComboBox.SelectedIndex = 0;
		_selectedSearchStatus = null;
	}

	private void UpdateSearchSummary(MailLogInspectorSearchSummary summary)
	{
		SearchTotalCountTextBlock.Text = summary.TotalCount.ToString();
		SearchDeliveredCountTextBlock.Text = summary.DeliveredCount.ToString();
		SearchDeliveredPercentTextBlock.Text = FormatDeliveredPercent(summary.DeliveredCount, summary.TotalCount);
		SearchUnderwayCountTextBlock.Text = summary.UnderwayCount.ToString();
		SearchBounceCountTextBlock.Text = summary.BounceCount.ToString();
	}

	private static string FormatDeliveredPercent(int deliveredCount, int totalCount)
	{
		return totalCount == 0 ? "0%" : $"{(double)deliveredCount / totalCount:P1}";
	}

	private async Task AdjustInitialDateRangeAsync()
	{
		DateTime desiredFrom = SearchFromDatePicker.SelectedDate?.Date ?? DateTime.Today.AddDays(-1.0);
		DateTime desiredThrough = EndOfDay(SearchThroughDatePicker.SelectedDate?.Date ?? DateTime.Today);
		if (!(await Task.Run(() => _store.HasMailInAcceptedRange(desiredFrom, desiredThrough))))
		{
			DateTime? dateTime = await Task.Run(() => _store.ReadLatestAcceptedAt());
			if (dateTime.HasValue)
			{
				DateTime date = dateTime.Value.Date;
				SearchFromDatePicker.SelectedDate = date;
				SearchThroughDatePicker.SelectedDate = date;
				StatusTextBlock.Text = $"Geen data in gisteren-vandaag. Laatste beschikbare dag geselecteerd: {MailLogInspectorDisplayFormats.Date(date)}.";
			}
		}

		RefreshDatePickerConstraints();
	}

	private sealed record ArchiveMonthListItem(string MonthKey, string DatabasePath, long MailItemCount, string DatabaseSizeDisplay, string ImportFromDisplay, string ImportThroughDisplay);
}
