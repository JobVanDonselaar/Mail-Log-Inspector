using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        if (string.IsNullOrWhiteSpace(trackingId) || !Guid.TryParse(trackingId.Trim(), out Guid trackingGuid))
        {
            return MailLogInspectorMailHistory.Empty(trackingId ?? string.Empty, recipient ?? string.Empty);
        }

        // De opgeslagen sleutel is de enige betrouwbare identificatie: bij een GUID-tracking-id is
        // dat de GUID zelf, anders een hash over tracking-id en ontvanger. Door in het archief
        // dezelfde sleutel te berekenen werken beide gevallen.
        byte[] expectedKey = trackingGuid.ToByteArray();
        MatchTarget target = new(trackingId.Trim(), (recipient ?? string.Empty).Trim(), expectedKey);

        IReadOnlyList<ArchiveCandidate> candidates = ResolveArchives();

        List<MailLogInspectorMailHistoryAttempt> attempts = new();
        List<string> searched = new();
        List<string> missing = new();
        int completed = 0;
        object gate = new();

        ParallelOptions options = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(2, Math.Min(Environment.ProcessorCount, 8)),
        };

        Parallel.ForEach(candidates, options, candidate =>
        {
            bool exists = !string.IsNullOrWhiteSpace(candidate.ArchivePath) && File.Exists(candidate.ArchivePath);
            List<MailLogInspectorMailHistoryAttempt>? found = exists
                ? ReadArchive(candidate, target, cancellationToken)
                : null;

            lock (gate)
            {
                if (found is null)
                {
                    missing.Add(candidate.SourceFileName);
                }
                else
                {
                    searched.Add(candidate.SourceFileName);
                    attempts.AddRange(found);
                }

                progress?.Report(new MailLogInspectorMailHistoryProgress(++completed, candidates.Count, candidate.SourceFileName));
            }
        });

        MailLogInspectorMailHistoryAttempt[] ordered = attempts
            .GroupBy(attempt => (attempt.AcceptedAt, attempt.DeliveredAt, attempt.Status, attempt.ResponseCode, attempt.Tries))
            .Select(group => group.First())
            .OrderBy(attempt => attempt.SortMoment)
            .ToArray();

        searched.Sort(StringComparer.OrdinalIgnoreCase);
        missing.Sort(StringComparer.OrdinalIgnoreCase);

        return new MailLogInspectorMailHistory(trackingId, target.Recipient, ordered, searched, missing);
    }

    /// <summary>
    /// Een import kan een ZIP met rapporten zijn of een los CSV-bestand; beide worden gearchiveerd,
    /// dus beide moeten hier gelezen kunnen worden.
    /// </summary>
    private static List<MailLogInspectorMailHistoryAttempt> ReadArchive(
        ArchiveCandidate candidate,
        MatchTarget target,
        CancellationToken cancellationToken)
    {
        List<MailLogInspectorMailHistoryAttempt> found = new();

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

                CollectFromCsv(entry.Open, candidate, target, found, cancellationToken);
            }

            return found;
        }

        CollectFromCsv(
            () => File.Open(candidate.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read),
            candidate,
            target,
            found,
            cancellationToken);

        return found;
    }

    private static void CollectFromCsv(
        Func<Stream> openStream,
        ArchiveCandidate candidate,
        MatchTarget target,
        List<MailLogInspectorMailHistoryAttempt> found,
        CancellationToken cancellationToken)
    {
        // Een ruwe regelscan is ruim tien keer sneller dan de volledige CSV-parser. De meeste
        // archieven bevatten deze mail niet, dus die slaan we zo goedkoop over.
        if (!MayContainMail(openStream, target, cancellationToken))
        {
            return;
        }

        using Stream stream = openStream();
        using StreamReader reader = new(stream);
        foreach (SmtpLogEntry logEntry in SmtpCsvReader.Enumerate(reader, onError: null, cancellationToken))
        {
            if (!target.Matches(logEntry))
            {
                continue;
            }

            found.Add(new MailLogInspectorMailHistoryAttempt(
                logEntry.AcceptedAt,
                logEntry.DeliveredAt,
                logEntry.MailFrom ?? string.Empty,
                logEntry.Recipient ?? string.Empty,
                logEntry.Status ?? string.Empty,
                logEntry.ResponseCode ?? string.Empty,
                logEntry.ResponseMessage ?? string.Empty,
                logEntry.BounceClass ?? string.Empty,
                logEntry.Tries,
                logEntry.TrackingId ?? string.Empty,
                candidate.SourceFileName));
        }
    }

    /// <summary>
    /// Voorselectie op ruwe tekst. We zoeken zowel op tracking-id als op ontvanger, omdat een
    /// tracking-id dat geen GUID is niet letterlijk in het rapport staat maar wel via de ontvanger
    /// te vinden is.
    /// </summary>
    private static bool MayContainMail(Func<Stream> openStream, MatchTarget target, CancellationToken cancellationToken)
    {
        using Stream stream = openStream();
        using StreamReader reader = new(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Contains(target.TrackingId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (target.Recipient.Length > 0 && line.Contains(target.Recipient, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record MatchTarget(string TrackingId, string Recipient, byte[] ExpectedKey)
    {
        public bool Matches(SmtpLogEntry entry)
        {
            if (Recipient.Length > 0 &&
                !string.Equals(entry.Recipient?.Trim(), Recipient, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            byte[] key = MailLogInspectorStore.BuildTrackingKey(entry.TrackingId, entry.Recipient);
            return key.AsSpan().SequenceEqual(ExpectedKey);
        }
    }

    private IReadOnlyList<ArchiveCandidate> ResolveArchives()
    {
        using SqliteConnection connection = _store.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_file_name, archive_path
            FROM imports
            WHERE archive_path IS NOT NULL AND archive_path <> ''
            ORDER BY imported_at DESC;
            """;

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

    private readonly record struct ArchiveCandidate(string SourceFileName, string ArchivePath);
}
