using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MailLogInspector.Core;

namespace MailLogInspector.Storage;

public sealed class MailLogInspectorImportService
{
	private sealed class CallbackProgress<T> : IProgress<T>
	{
		private readonly Action<T> _callback;

		public CallbackProgress(Action<T> callback)
		{
			_callback = callback;
		}

		public void Report(T value)
		{
			_callback(value);
		}
	}

	private readonly MailLogInspectorStore _store;

	private readonly MailLogInspectorWorkspacePaths _workspace;

	private readonly HashSet<string> _pendingArchiveDatabasePaths = new(StringComparer.OrdinalIgnoreCase);

	public MailLogInspectorImportService(MailLogInspectorStore store, MailLogInspectorWorkspacePaths workspace)
	{
		_store = store;
		_workspace = workspace;
	}

	public Task<MailLogInspectorImportResult> ImportCsvAsync(string csvPath, CancellationToken cancellationToken, IProgress<MailLogInspectorImportProgress>? progress = null, bool finalizeBatch = true, bool archiveSource = true)
	{
		return Task.Run(() => ImportCsvCore(csvPath, cancellationToken, progress, finalizeBatch, archiveSource), cancellationToken);
	}

	public Task<MailLogInspectorImportResult> ImportZipAsync(string zipPath, CancellationToken cancellationToken, IProgress<MailLogInspectorImportProgress>? progress = null, bool finalizeBatch = true, bool archiveSource = true)
	{
		return Task.Run(() => ImportZipCore(zipPath, cancellationToken, progress, finalizeBatch, archiveSource), cancellationToken);
	}

	private MailLogInspectorImportResult ImportCsvCore(string csvPath, CancellationToken cancellationToken, IProgress<MailLogInspectorImportProgress>? progress, bool finalizeBatch, bool archiveSource)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Report(progress, MailLogInspectorImportStage.Preparing, "Bestand voorbereiden...", 0.0, 0L, 0L, 0);
		MailLogInspectorImportLimits.ValidateFileSize(csvPath, MailLogInspectorImportLimits.MaxCsvBytes, "Het CSV-bestand");
		string sourceHash = ComputeFileHash(csvPath);
		MailLogInspectorImportedFile? existingImport = _store.TryReadImportBySourceHash(sourceHash);
		if (existingImport != null)
		{
			return ToAlreadyImportedResult(existingImport);
		}
		string archivePath = archiveSource ? CopySourceToArchive(csvPath) : csvPath;
		List<SmtpParseError> list = new List<SmtpParseError>();
		List<SmtpLogEntry> entries = SmtpCsvReader.Enumerate(csvPath, list.Add, cancellationToken).ToList();
		ThrowIfAllDataRowsAreInvalid(entries.Count, list.Count);
		MailLogInspectorImportResult mailLogInspectorImportResult = SaveSplitImport(csvPath, sourceHash, archivePath, entries, list.Count, finalizeBatch, cancellationToken);
		Report(progress, MailLogInspectorImportStage.Completed, BuildCompletedMessage(mailLogInspectorImportResult), 100.0, 0L, 0L, mailLogInspectorImportResult.SourceRowCount);
		return mailLogInspectorImportResult;
	}

	private MailLogInspectorImportResult ImportZipCore(string zipPath, CancellationToken cancellationToken, IProgress<MailLogInspectorImportProgress>? progress, bool finalizeBatch, bool archiveSource)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Report(progress, MailLogInspectorImportStage.Preparing, "ZIP voorbereiden...", 0.0, 0L, 0L, 0);
		MailLogInspectorImportLimits.ValidateFileSize(zipPath, MailLogInspectorImportLimits.MaxZipBytes, "Het ZIP-bestand");
		string sourceHash = ComputeFileHash(zipPath);
		MailLogInspectorImportedFile? existingImport = _store.TryReadImportBySourceHash(sourceHash);
		if (existingImport != null)
		{
			return ToAlreadyImportedResult(existingImport);
		}
		string archivePath = archiveSource ? CopySourceToArchive(zipPath) : zipPath;
		string path = ExtractSingleCsv(zipPath);
		try
		{
			List<SmtpParseError> list = new List<SmtpParseError>();
			List<SmtpLogEntry> entries = SmtpCsvReader.Enumerate(path, list.Add, cancellationToken).ToList();
			ThrowIfAllDataRowsAreInvalid(entries.Count, list.Count);
			MailLogInspectorImportResult mailLogInspectorImportResult = SaveSplitImport(zipPath, sourceHash, archivePath, entries, list.Count, finalizeBatch, cancellationToken);
			Report(progress, MailLogInspectorImportStage.Completed, BuildCompletedMessage(mailLogInspectorImportResult), 100.0, 0L, 0L, mailLogInspectorImportResult.SourceRowCount);
			return mailLogInspectorImportResult;
		}
		finally
		{
			DeleteIfExists(path);
			string? directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(directoryName) && Directory.Exists(directoryName))
			{
				Directory.Delete(directoryName, recursive: true);
			}
		}
	}

	private MailLogInspectorImportResult SaveSplitImport(string sourcePath, string sourceHash, string? archivePath, IReadOnlyList<SmtpLogEntry> entries, int errorCount, bool finalizeBatch, CancellationToken cancellationToken)
	{
		DateTime cutoff = MailLogInspectorRetentionPolicy.ActiveCutoffDate(DateTime.Today);
		List<SmtpLogEntry> activeEntries = new();
		List<SmtpLogEntry> archiveEntries = new();
		foreach (SmtpLogEntry entry in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!entry.AcceptedAt.HasValue || entry.AcceptedAt.Value.Date >= cutoff)
			{
				activeEntries.Add(entry);
			}
			else
			{
				archiveEntries.Add(entry);
			}
		}
		MailLogInspectorImportResult activeResult = _store.SaveImport(sourcePath, sourceHash, archivePath, activeEntries, errorCount, rebuildAnalysis: finalizeBatch, cancellationToken);
		int archivedUpsertedCount = 0;
		bool allArchivesAlreadyImported = true;
		foreach (IGrouping<string, SmtpLogEntry> monthGroup in archiveEntries.GroupBy((SmtpLogEntry entry) => entry.AcceptedAt!.Value.ToString("yyyy-MM"), StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string archiveDatabasePath = GetArchiveDatabasePath(monthGroup.Key);
			MailLogInspectorStore archiveStore = new MailLogInspectorStore(archiveDatabasePath);
			archiveStore.Initialize();
			MailLogInspectorImportResult archiveResult = archiveStore.SaveImport(sourcePath, sourceHash, archivePath, monthGroup.ToList(), 0, rebuildAnalysis: finalizeBatch, cancellationToken);
			if (finalizeBatch)
			{
				archiveStore.OptimizeForReadPerformance();
			}
			else
			{
				_pendingArchiveDatabasePaths.Add(archiveDatabasePath);
			}
			archivedUpsertedCount += archiveResult.UpsertedCount;
			allArchivesAlreadyImported &= archiveResult.AlreadyImported;
		}
		if (finalizeBatch)
		{
			_store.ApplyRetention(cutoff);
		}
		return activeResult with
		{
			AlreadyImported = activeResult.AlreadyImported && (archiveEntries.Count == 0 || allArchivesAlreadyImported),
			SourceRowCount = entries.Count,
			ArchivedUpsertedCount = archivedUpsertedCount,
			SkippedOldRows = archiveEntries.Count
		};
	}

	public void FinalizeBatch()
	{
		_store.ApplyRetention(MailLogInspectorRetentionPolicy.ActiveCutoffDate(DateTime.Today));
		_store.RebuildAnalysisData();
		_store.OptimizeForReadPerformance();
		foreach (string archiveDatabasePath in _pendingArchiveDatabasePaths)
		{
			var archiveStore = new MailLogInspectorStore(archiveDatabasePath);
			archiveStore.RebuildAnalysisData();
			archiveStore.OptimizeForReadPerformance();
		}
		_pendingArchiveDatabasePaths.Clear();
	}
	private string GetArchiveDatabasePath(string monthKey)
	{
		Directory.CreateDirectory(_workspace.ArchiveDatabaseDirectory);
		return Path.Combine(_workspace.ArchiveDatabaseDirectory, monthKey + ".sqlite");
	}

	private string CopySourceToArchive(string sourcePath)
	{
		Directory.CreateDirectory(_workspace.ArchiveDirectory);
		string? directoryName = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
		string fullPath = Path.GetFullPath(_workspace.ArchiveDirectory);
		if (string.Equals(directoryName, fullPath, StringComparison.OrdinalIgnoreCase))
		{
			return sourcePath;
		}
		string path = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Path.GetFileName(sourcePath)}";
		string text = Path.Combine(_workspace.ArchiveDirectory, path);
		File.Copy(sourcePath, text, overwrite: false);
		return text;
	}

	private static string ExtractSingleCsv(string zipPath)
	{
		using ZipArchive zipArchive = ZipFile.OpenRead(zipPath);
		MailLogInspectorImportLimits.ValidateArchive(zipArchive);
		List<ZipArchiveEntry> list = (from entry in zipArchive.Entries
			where !string.IsNullOrWhiteSpace(entry.FullName)
			where Path.GetExtension(entry.FullName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
			select entry).ToList();
		if (list.Count == 0)
		{
			throw new InvalidDataException("Geen CSV-bestand gevonden in deze zip.");
		}
		if (list.Count > 1)
		{
			throw new InvalidDataException("Deze zip bevat meerdere CSV-bestanden. Gebruik een zip met precies een CSV.");
		}
		MailLogInspectorImportLimits.ValidateCsvEntry(list[0]);
		string text = Path.Combine(Path.GetTempPath(), "MailLogInspector", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, Path.GetFileName(list[0].FullName));
		try
		{
			list[0].ExtractToFile(text2, overwrite: true);
			return text2;
		}
		catch
		{
			Directory.Delete(text, recursive: true);
			throw;
		}
	}

	private static void ThrowIfAllDataRowsAreInvalid(int validRowCount, int errorCount)
	{
		if (validRowCount == 0 && errorCount > 0)
		{
			throw new InvalidDataException(
				"Geen geldige mailregels gevonden; alle gegevensregels bevatten fouten.");
		}
	}

	private static string ComputeFileHash(string path)
	{
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = File.OpenRead(path);
		return Convert.ToHexString(sHA.ComputeHash(inputStream));
	}

	private static void DeleteIfExists(string path)
	{
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	private static MailLogInspectorImportResult ToAlreadyImportedResult(MailLogInspectorImportedFile import)
	{
		return new MailLogInspectorImportResult(
			true,
			import.ImportId,
			import.SourcePath,
			import.RowCount,
			0,
			0,
			import.ReportStart,
			import.ReportEnd,
			import.ArchivePath);
	}
	private static string BuildCompletedMessage(MailLogInspectorImportResult result)
	{
		if (!result.AlreadyImported)
		{
			return $"Import gereed. {result.UpsertedCount:n0} actieve rijen bijgewerkt. {result.ArchivedUpsertedCount:n0} rijen naar maandarchief.";
		}
		return "Bestand was al geimporteerd.";
	}

	private static void Report(IProgress<MailLogInspectorImportProgress>? progress, MailLogInspectorImportStage stage, string message, double percentComplete, long bytesRead, long totalBytes, int rowsRead)
	{
		progress?.Report(new MailLogInspectorImportProgress(stage, message, percentComplete, bytesRead, totalBytes, rowsRead));
	}
}
