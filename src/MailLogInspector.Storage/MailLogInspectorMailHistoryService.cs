using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using MailLogInspector.Core;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

/// <summary>
/// Leest op aanvraag de volledige logregels van één mail terug uit de gearchiveerde rapporten.
/// De database bevat bewust alleen de eindstand per mail; de tussenliggende afleverpogingen
/// blijven in de originele ZIP-bestanden staan zodat de database klein en snel blijft.
/// </summary>
public sealed class MailLogInspectorMailHistoryService
{
    private readonly MailLogInspectorStore _store;

    public MailLogInspectorMailHistoryService(MailLogInspectorStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Onderzoek moet compleet zijn, dus we doorzoeken elk archief. De goedkope voorscan maakt een
    /// archief zonder treffer bijna gratis; alleen archieven die de mail echt bevatten worden
    /// volledig geparseerd.
    /// </summary>
    public MailLogInspectorMailHistory ReadHistory(
        string trackingId,
        string recipient,
        IProgress<MailLogInspectorMailHistoryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ReadHistory(trackingId, recipient, null, null, progress, cancellationToken);
    }

    public MailLogInspectorMailHistory ReadHistory(
        string trackingId,
        string recipient,
        DateTime? fromInclusive,
        DateTime? throughInclusive,
        IProgress<MailLogInspectorMailHistoryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ReadHistories(
            [new MailLogInspectorMailHistoryRequest(trackingId, recipient, fromInclusive, throughInclusive)],
            progress,
            cancellationToken)[0];
    }

    /// <summary>
    /// Leest de historie voor meerdere mails in één doorgang. Dit voorkomt dat een Excel-export
    /// dezelfde ZIP- en CSV-archieven opnieuw doorzoekt voor iedere regel in de topselectie.
    /// </summary>
    public IReadOnlyList<MailLogInspectorMailHistory> ReadHistories(
        IReadOnlyList<MailLogInspectorMailHistoryRequest> requests,
        IProgress<MailLogInspectorMailHistoryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return Array.Empty<MailLogInspectorMailHistory>();
        }

        List<HistoryAccumulator> accumulators = requests
            .Select((request, index) => HistoryAccumulator.Create(index, request))
            .ToList();
        IReadOnlyList<HistoryAccumulator> validAccumulators = accumulators
            .Where(accumulator => accumulator.Target is not null)
            .ToArray();
        if (validAccumulators.Count == 0)
        {
            return accumulators.Select(accumulator => accumulator.ToHistory()).ToArray();
        }

        IReadOnlyDictionary<string, IReadOnlyList<HistoryAccumulator>> targetsByRecipient =
            validAccumulators
                .GroupBy(accumulator => accumulator.Target!.Recipient, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<HistoryAccumulator>)group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);
        DateTime? firstRelevantMoment = validAccumulators
            .Select(accumulator => accumulator.FromInclusive)
            .Where(moment => moment.HasValue)
            .OrderBy(moment => moment)
            .FirstOrDefault();
        DateTime? lastRelevantMoment = validAccumulators
            .Select(accumulator => accumulator.ThroughInclusive)
            .Where(moment => moment.HasValue)
            .OrderByDescending(moment => moment)
            .FirstOrDefault();
        DateTime? scopedFrom = firstRelevantMoment?.Date.AddDays(-1);
        DateTime? scopedThrough = lastRelevantMoment?.Date.AddDays(1).AddTicks(-1);
        IReadOnlyList<ArchiveCandidate> indexedCandidates = ResolveIndexedArchives(
            validAccumulators,
            scopedFrom,
            scopedThrough);
        IReadOnlyList<ArchiveCandidate> candidates = indexedCandidates.Count > 0
            ? MergeCandidates(indexedCandidates, ResolveLegacyArchivesWithoutLookup(scopedFrom, scopedThrough))
            : ResolveArchives(scopedFrom, scopedThrough);
        int completed = 0;
        object gate = new();

        ParallelOptions options = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, 4)),
        };

        Parallel.ForEach(candidates, options, candidate =>
        {
            bool exists = !string.IsNullOrWhiteSpace(candidate.ArchivePath) && File.Exists(candidate.ArchivePath);
            if (exists)
            {
                ReadArchive(candidate, targetsByRecipient, cancellationToken);
            }

            lock (gate)
            {
                foreach (HistoryAccumulator accumulator in validAccumulators)
                {
                    if (exists)
                    {
                        accumulator.Searched.Add(candidate.SourceFileName);
                    }
                    else
                    {
                        accumulator.Missing.Add(candidate.SourceFileName);
                    }
                }

                progress?.Report(new MailLogInspectorMailHistoryProgress(++completed, candidates.Count, candidate.SourceFileName));
            }
        });

        return accumulators
            .OrderBy(accumulator => accumulator.Index)
            .Select(accumulator => accumulator.ToHistory())
            .ToArray();
    }

    /// <summary>
    /// Een import kan een ZIP met rapporten zijn of een los CSV-bestand; beide worden gearchiveerd,
    /// dus beide moeten hier gelezen kunnen worden.
    /// </summary>
    private static void ReadArchive(
        ArchiveCandidate candidate,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryAccumulator>> targetsByRecipient,
        CancellationToken cancellationToken)
    {
        if (candidate.ArchivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using ZipArchive archive = ZipFile.OpenRead(candidate.ArchivePath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CollectFromCsv(entry.Open, candidate, targetsByRecipient, cancellationToken);
            }

            return;
        }

        CollectFromCsv(
            () => File.Open(candidate.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read),
            candidate,
            targetsByRecipient,
            cancellationToken);
    }

    private static void CollectFromCsv(
        Func<Stream> openStream,
        ArchiveCandidate candidate,
        IReadOnlyDictionary<string, IReadOnlyList<HistoryAccumulator>> targetsByRecipient,
        CancellationToken cancellationToken)
    {
        // Een ruwe scan is goedkoper dan volledig parsen. Voor een batch volstaat één scan om te
        // bepalen of minstens één geselecteerde ontvanger in dit CSV-bestand voorkomt.
        if (!MayContainAnyTarget(
            openStream,
            targetsByRecipient.Keys,
            targetsByRecipient.Values.SelectMany(accumulators => accumulators).Select(accumulator => accumulator.Target!.TrackingId),
            cancellationToken))
        {
            return;
        }

        using Stream stream = openStream();
        using StreamReader reader = new(stream);
        foreach (SmtpLogEntry logEntry in SmtpCsvReader.Enumerate(reader, onError: null, cancellationToken))
        {
            string recipient = logEntry.Recipient?.Trim() ?? string.Empty;
            if (!targetsByRecipient.TryGetValue(recipient, out IReadOnlyList<HistoryAccumulator>? candidates))
            {
                continue;
            }

            byte[] key = MailLogInspectorStore.BuildTrackingKey(logEntry.TrackingId, recipient);
            foreach (HistoryAccumulator accumulator in candidates)
            {
                if (accumulator.Target!.Matches(key))
                {
                    accumulator.AddAttempt(new MailLogInspectorMailHistoryAttempt(
                        logEntry.AcceptedAt,
                        logEntry.DeliveredAt,
                        logEntry.MailFrom ?? string.Empty,
                        recipient,
                        logEntry.Status ?? string.Empty,
                        logEntry.ResponseCode ?? string.Empty,
                        logEntry.ResponseMessage ?? string.Empty,
                        logEntry.BounceClass ?? string.Empty,
                        logEntry.Tries,
                        logEntry.TrackingId ?? string.Empty,
                        candidate.SourceFileName));
                }
            }
        }
    }

    /// <summary>
    /// Voorselectie op ruwe tekst. We zoeken zowel op tracking-id als op ontvanger, omdat een
    /// tracking-id dat geen GUID is niet letterlijk in het rapport staat maar wel via de ontvanger
    /// te vinden is.
    /// </summary>
    private static bool MayContainAnyTarget(
        Func<Stream> openStream,
        IEnumerable<string> recipients,
        IEnumerable<string> trackingIds,
        CancellationToken cancellationToken)
    {
        string[] recipientTargets = recipients.Where(recipient => recipient.Length > 0).ToArray();
        string[] trackingIdTargets = trackingIds.Where(trackingId => trackingId.Length > 0).ToArray();
        using Stream stream = openStream();
        using StreamReader reader = new(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (recipientTargets.Any(recipient => line.Contains(recipient, StringComparison.OrdinalIgnoreCase)) ||
                trackingIdTargets.Any(trackingId => line.Contains(trackingId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record MatchTarget(string TrackingId, string Recipient, byte[] ExpectedKey)
    {
        public bool Matches(byte[] key) => key.AsSpan().SequenceEqual(ExpectedKey);
    }

    private sealed class HistoryAccumulator
    {
        private HistoryAccumulator(
            int index,
            string trackingId,
            string recipient,
            DateTime? fromInclusive,
            DateTime? throughInclusive,
            MatchTarget? target)
        {
            Index = index;
            TrackingId = trackingId;
            Recipient = recipient;
            FromInclusive = fromInclusive;
            ThroughInclusive = throughInclusive;
            Target = target;
        }

        public int Index { get; }
        public string TrackingId { get; }
        public string Recipient { get; }
        public DateTime? FromInclusive { get; }
        public DateTime? ThroughInclusive { get; }
        public MatchTarget? Target { get; }
        public List<MailLogInspectorMailHistoryAttempt> Attempts { get; } = new();
        public List<string> Searched { get; } = new();
        public List<string> Missing { get; } = new();
        private object Gate { get; } = new();

        public static HistoryAccumulator Create(int index, MailLogInspectorMailHistoryRequest request)
        {
            string trackingId = request.TrackingId?.Trim() ?? string.Empty;
            string recipient = request.Recipient?.Trim() ?? string.Empty;
            MatchTarget? target = Guid.TryParse(trackingId, out Guid guid)
                ? new MatchTarget(trackingId, recipient, guid.ToByteArray())
                : null;
            return new HistoryAccumulator(index, trackingId, recipient, request.FromInclusive, request.ThroughInclusive, target);
        }

        public void AddAttempt(MailLogInspectorMailHistoryAttempt attempt)
        {
            lock (Gate)
            {
                Attempts.Add(attempt);
            }
        }

        public MailLogInspectorMailHistory ToHistory()
        {
            MailLogInspectorMailHistoryAttempt[] attempts;
            lock (Gate)
            {
                attempts = Attempts.ToArray();
            }

            attempts = attempts
                .GroupBy(attempt => (attempt.AcceptedAt, attempt.DeliveredAt, attempt.Status, attempt.ResponseCode, attempt.Tries))
                .Select(group => group.First())
                .OrderBy(attempt => attempt.SortMoment)
                .ToArray();
            Searched.Sort(StringComparer.OrdinalIgnoreCase);
            Missing.Sort(StringComparer.OrdinalIgnoreCase);
            return new MailLogInspectorMailHistory(TrackingId, Recipient, attempts, Searched, Missing);
        }
    }

    private IReadOnlyList<ArchiveCandidate> ResolveArchives(DateTime? fromInclusive, DateTime? throughInclusive)
    {
        using SqliteConnection connection = _store.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_file_name, archive_path
            FROM imports
            WHERE archive_path IS NOT NULL AND archive_path <> ''
              AND (
                    $fromInclusive IS NULL
                    OR report_start IS NULL
                    OR report_end IS NULL
                    OR (report_end >= $fromInclusive AND report_start <= $throughInclusive)
                  )
            ORDER BY imported_at DESC;
            """;
        command.Parameters.AddWithValue("$fromInclusive", fromInclusive.HasValue ? fromInclusive.Value : DBNull.Value);
        command.Parameters.AddWithValue("$throughInclusive", throughInclusive.HasValue ? throughInclusive.Value : DBNull.Value);

        List<ArchiveCandidate> candidates = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        using SqliteDataReader reader = command.ExecuteReader();
        DbDataReader source = reader;
        while (source.Read())
        {
            string archivePath = source.GetString(1);
            if (seen.Add(archivePath))
            {
                candidates.Add(new ArchiveCandidate(source.GetString(0), archivePath));
            }
        }

        return candidates;
    }

    private IReadOnlyList<ArchiveCandidate> ResolveIndexedArchives(
        IReadOnlyList<HistoryAccumulator> accumulators,
        DateTime? fromInclusive,
        DateTime? throughInclusive)
    {
        List<(byte[] TrackingKey, string RecipientAddress, int TargetId)> targets = BuildLookupTargets(accumulators);
        if (targets.Count == 0)
        {
            return Array.Empty<ArchiveCandidate>();
        }

        using SqliteConnection connection = _store.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildLookupArchiveSql(targets.Count);
        command.Parameters.AddWithValue("$fromInclusive", fromInclusive.HasValue ? fromInclusive.Value : DBNull.Value);
        command.Parameters.AddWithValue("$throughInclusive", throughInclusive.HasValue ? throughInclusive.Value : DBNull.Value);
        for (int index = 0; index < targets.Count; index++)
        {
            command.Parameters.AddWithValue("$targetId" + index, targets[index].TargetId);
            command.Parameters.AddWithValue("$trackingKey" + index, targets[index].TrackingKey);
            command.Parameters.AddWithValue("$recipientAddress" + index, targets[index].RecipientAddress);
        }

        List<ArchiveCandidate> candidates = new();
        HashSet<string> seenArchives = new(StringComparer.OrdinalIgnoreCase);
        using SqliteDataReader reader = command.ExecuteReader();
        DbDataReader source = reader;
        while (source.Read())
        {
            string sourceFileName = source.GetString(0);
            string archivePath = source.GetString(1);
            if (seenArchives.Add(archivePath))
            {
                candidates.Add(new ArchiveCandidate(sourceFileName, archivePath));
            }
        }

        return candidates;
    }

    private static List<(byte[] TrackingKey, string RecipientAddress, int TargetId)> BuildLookupTargets(
        IReadOnlyList<HistoryAccumulator> accumulators)
    {
        List<(byte[] TrackingKey, string RecipientAddress, int TargetId)> targets = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        int targetId = 0;
        foreach (HistoryAccumulator accumulator in accumulators)
        {
            if (accumulator.Target is null)
            {
                continue;
            }

            string recipientAddress = NormalizeRecipientAddress(accumulator.Recipient);
            if (recipientAddress.Length == 0)
            {
                continue;
            }

            string key = Convert.ToHexString(accumulator.Target.ExpectedKey) + "|" + recipientAddress;
            if (!seen.Add(key))
            {
                continue;
            }

            targets.Add((accumulator.Target.ExpectedKey, recipientAddress, targetId));
            targetId++;
        }

        return targets;
    }

    private static string BuildLookupArchiveSql(int targetCount)
    {
        StringBuilder sql = new();
        sql.AppendLine("WITH targets(target_id, tracking_key, recipient_address) AS (VALUES");
        for (int index = 0; index < targetCount; index++)
        {
            if (index > 0)
            {
                sql.Append(",");
            }

            sql.AppendLine()
                .Append("    ($targetId")
                .Append(index)
                .Append(", $trackingKey")
                .Append(index)
                .Append(", $recipientAddress")
                .Append(index)
                .Append(')');
        }

        sql.AppendLine();
        sql.AppendLine(")");
        sql.AppendLine("SELECT DISTINCT imports.source_file_name, imports.archive_path");
        sql.AppendLine("FROM targets");
        sql.AppendLine("JOIN import_mail_lookup AS lookup");
        sql.AppendLine("  ON lookup.tracking_key = targets.tracking_key");
        sql.AppendLine(" AND lookup.recipient_address = targets.recipient_address");
        sql.AppendLine("JOIN imports ON imports.import_id = lookup.import_id");
        sql.AppendLine("WHERE imports.archive_path IS NOT NULL AND imports.archive_path <> ''");
        sql.AppendLine("  AND (");
        sql.AppendLine("        $fromInclusive IS NULL");
        sql.AppendLine("        OR imports.report_start IS NULL");
        sql.AppendLine("        OR imports.report_end IS NULL");
        sql.AppendLine("        OR (imports.report_end >= $fromInclusive AND imports.report_start <= $throughInclusive)");
        sql.AppendLine("      )");
        sql.AppendLine("ORDER BY imports.imported_at DESC;");
        return sql.ToString();
    }

    private IReadOnlyList<ArchiveCandidate> ResolveLegacyArchivesWithoutLookup(
        DateTime? fromInclusive,
        DateTime? throughInclusive)
    {
        using SqliteConnection connection = _store.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_file_name, archive_path
            FROM imports
            WHERE archive_path IS NOT NULL AND archive_path <> ''
              AND NOT EXISTS (
                    SELECT 1
                    FROM import_mail_lookup
                    WHERE import_mail_lookup.import_id = imports.import_id
              )
              AND (
                    $fromInclusive IS NULL
                    OR report_start IS NULL
                    OR report_end IS NULL
                    OR (report_end >= $fromInclusive AND report_start <= $throughInclusive)
                  )
            ORDER BY imported_at DESC;
            """;
        command.Parameters.AddWithValue("$fromInclusive", fromInclusive.HasValue ? fromInclusive.Value : DBNull.Value);
        command.Parameters.AddWithValue("$throughInclusive", throughInclusive.HasValue ? throughInclusive.Value : DBNull.Value);
        List<ArchiveCandidate> candidates = new();
        using SqliteDataReader reader = command.ExecuteReader();
        DbDataReader source = reader;
        while (source.Read())
        {
            candidates.Add(new ArchiveCandidate(source.GetString(0), source.GetString(1)));
        }

        return candidates;
    }

    private static IReadOnlyList<ArchiveCandidate> MergeCandidates(
        IReadOnlyList<ArchiveCandidate> primary,
        IReadOnlyList<ArchiveCandidate> fallback)
    {
        List<ArchiveCandidate> merged = new(primary.Count + fallback.Count);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (ArchiveCandidate candidate in primary)
        {
            if (seen.Add(candidate.ArchivePath))
            {
                merged.Add(candidate);
            }
        }

        foreach (ArchiveCandidate candidate in fallback)
        {
            if (seen.Add(candidate.ArchivePath))
            {
                merged.Add(candidate);
            }
        }

        return merged;
    }

    private static string NormalizeRecipientAddress(string? recipient)
    {
        return recipient?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private readonly record struct ArchiveCandidate(string SourceFileName, string ArchivePath);
}
