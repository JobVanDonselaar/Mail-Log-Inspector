using System.IO;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed class GmailReportSyncService : IGmailReportSyncService
{
    private readonly GmailReportOperationalStore _store;
    private readonly IGmailAccessTokenProvider _accessTokenProvider;
    private readonly IGmailImapReportClient _mailClient;
    private readonly IGmailZipDownloader _zipDownloader;
    private readonly IGmailZipImportRunner _zipImportRunner;
    private readonly MailLogInspectorWorkspacePaths _workspace;

    public GmailReportSyncService(
        GmailReportOperationalStore store,
        IGmailAccessTokenProvider accessTokenProvider,
        IGmailImapReportClient mailClient,
        IGmailZipDownloader zipDownloader,
        IGmailZipImportRunner zipImportRunner,
        MailLogInspectorWorkspacePaths workspace)
    {
        _store = store;
        _accessTokenProvider = accessTokenProvider;
        _mailClient = mailClient;
        _zipDownloader = zipDownloader;
        _zipImportRunner = zipImportRunner;
        _workspace = workspace;
    }

    public async Task<GmailReportSyncResult> SyncAsync(
        CancellationToken cancellationToken,
        IProgress<string>? progress = null,
        bool latestOnly = false,
        DateTime? minimumReportDayExclusive = null)
    {
        GmailReportConfig config = _store.LoadConfig();
        string sourceLabel = ReportImportSource.FromImapProvider(config.ImapProvider);
        if (string.IsNullOrWhiteSpace(config.AccountEmailAddress))
        {
            throw new InvalidOperationException("IMAP-rapportkoppeling is niet volledig geconfigureerd.");
        }

        GmailImapConnectionSettings connectionSettings = await BuildConnectionSettingsAsync(config, cancellationToken);

        List<GmailImapReportMessage> candidates = new();
        candidates.AddRange(await _mailClient.FetchInboxCandidatesAsync(connectionSettings, cancellationToken));
        if (candidates.Count == 0)
        {
            candidates.AddRange(await _mailClient.FetchCatchupCandidatesAsync(connectionSettings, DateTime.UtcNow.AddDays(-14), cancellationToken));
        }

        int imported = 0;
        int failed = 0;
        int skipped = 0;
        int deleted = 0;
        List<ReportImportedArtifact> importedArtifacts = [];
        IEnumerable<GmailImapReportMessage> orderedCandidates = candidates
            .OrderByDescending(static item => item.InternalDate)
            .DistinctBy(static item => item.GmailMessageId, StringComparer.Ordinal);
        if (minimumReportDayExclusive.HasValue)
        {
            orderedCandidates = orderedCandidates.Where(message =>
                InferReportDay(message.InternalDate) > minimumReportDayExclusive.Value.Date);
        }

        foreach (GmailImapReportMessage message in orderedCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!GmailReportMailParser.TryExtractZipUrl(message.Sender, message.HtmlBody, message.TextBody, out string? zipUrl) || string.IsNullOrWhiteSpace(zipUrl))
            {
                skipped++;
                _store.UpsertHistory(new GmailReportHistoryRow(
                    message.GmailMessageId,
                    message.InternalDate,
                    message.Sender,
                    message.Subject,
                    string.Empty,
                    "skip",
                    "skip",
                    false,
                    "Geen directe ZIP-link gevonden.",
                    DateTime.UtcNow,
                    DateTime.UtcNow));
                continue;
            }

            bool messageAlreadyImported = _store.HasSuccessfulMessageId(message.GmailMessageId);
            bool zipAlreadyImported =
                !latestOnly &&
                (messageAlreadyImported || _store.HasSuccessfulZipUrl(zipUrl));
            if (_store.WasMessagePermanentlyDeleted(message.GmailMessageId))
            {
                skipped++;
                continue;
            }

            if (zipAlreadyImported)
            {
                skipped++;
                bool reusedMessageDeleted = false;
                string? deleteError = null;
                try
                {
                    await _mailClient.DeleteMessagePermanentlyAsync(connectionSettings, message, cancellationToken);
                    reusedMessageDeleted = true;
                    deleted++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                MailLogInspectorLog.Error("gmail", "Gmail-syncbewerking mislukt", ex);
                    failed++;
                    deleteError = "Verwijderen van reeds geimporteerde Gmail-mail mislukte: " + ex.Message;
                }

                _store.UpsertHistory(new GmailReportHistoryRow(
                    message.GmailMessageId,
                    message.InternalDate,
                    message.Sender,
                    message.Subject,
                    zipUrl,
                    messageAlreadyImported ? "ok" : "reused",
                    messageAlreadyImported ? "ok" : "duplicate",
                    reusedMessageDeleted,
                    deleteError,
                    DateTime.UtcNow,
                    DateTime.UtcNow));
                if (latestOnly)
                {
                    break;
                }
                continue;
            }

            bool deletedSuccessfully = false;
            string downloadStatus = "ok";
            string importStatus = "pending";
            string? errorText = null;
            string? sourceHash = null;

            try
            {
                progress?.Report("Downloaden: " + Path.GetFileName(new Uri(zipUrl).AbsolutePath));
                string downloadedPath = await _zipDownloader.DownloadAsync(zipUrl, _workspace.GmailIncomingDirectory, cancellationToken);
                GmailZipImportOutcome importOutcome = await _zipImportRunner.ImportAsync(downloadedPath, cancellationToken);
                bool importedSuccessfully = importOutcome.Success;
                sourceHash = importOutcome.SourceHash;
                importStatus = importedSuccessfully ? "ok" : "failed";

                if (importedSuccessfully)
                {
                    imported++;
                    DateTime reportDay =
                        (importOutcome.ReportStart ?? InferReportDay(message.InternalDate)).Date;
                    importedArtifacts.Add(new ReportImportedArtifact(
                        sourceHash,
                        Path.GetFileName(downloadedPath),
                        reportDay,
                        importOutcome.AlreadyImported));
                    MailLogInspectorLog.Info(
                        "sync",
                        $"Bron={sourceLabel} | Rapport={reportDay:dd-MM-yyyy} | Import geslaagd");
                    try
                    {
                        await _mailClient.DeleteMessagePermanentlyAsync(connectionSettings, message, cancellationToken);
                        deletedSuccessfully = true;
                        deleted++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                MailLogInspectorLog.Error("gmail", "Gmail-syncbewerking mislukt", ex);
                        failed++;
                        errorText = "Import gelukt, maar verwijderen van Gmail-mail mislukte: " + ex.Message;
                    }
                }
                else
                {
                    failed++;
                    errorText = "Import mislukt.";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MailLogInspectorLog.Error("gmail", "Gmail-syncbewerking mislukt", ex);
                downloadStatus = "failed";
                importStatus = "failed";
                failed++;
                errorText = ex.Message;
            }

            _store.UpsertHistory(new GmailReportHistoryRow(
                message.GmailMessageId,
                message.InternalDate,
                message.Sender,
                message.Subject,
                zipUrl,
                downloadStatus,
                importStatus,
                deletedSuccessfully,
                errorText,
                DateTime.UtcNow,
                DateTime.UtcNow,
                sourceHash));
            if (latestOnly)
            {
                break;
            }
        }

        return new GmailReportSyncResult(imported, failed, skipped, deleted, importedArtifacts);
    }

    private async Task<GmailImapConnectionSettings> BuildConnectionSettingsAsync(GmailReportConfig config, CancellationToken cancellationToken)
    {
        ImapConnectionProfile profile = ImapProviderProfiles.Resolve(
            config.ImapProvider,
            config.ImapHost,
            config.ImapPort,
            config.ImapUseSsl);
        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            throw new InvalidOperationException("Vul voor Eigen IMAP-server eerst de servernaam in.");
        }
        string authenticationMode = string.IsNullOrWhiteSpace(config.AuthenticationMode)
            ? GmailAuthenticationMode.OAuth
            : config.AuthenticationMode;

        if (string.Equals(authenticationMode, GmailAuthenticationMode.AppPassword, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(config.EncryptedAppPassword))
            {
                throw new InvalidOperationException("IMAP-wachtwoord ontbreekt.");
            }

            return new GmailImapConnectionSettings(
                config.AccountEmailAddress!,
                GmailAuthenticationMode.AppPassword,
                null,
                GmailOAuthService.UnprotectSecret(config.EncryptedAppPassword),
                profile.Host,
                profile.Port,
                profile.UseSsl,
                config.ImapProvider);
        }

        if (string.IsNullOrWhiteSpace(config.ClientId) ||
            string.IsNullOrWhiteSpace(config.ClientSecret) ||
            string.IsNullOrWhiteSpace(config.EncryptedRefreshToken))
        {
            throw new InvalidOperationException("Gmail OAuth-configuratie is niet volledig.");
        }

        string accessToken = await _accessTokenProvider.GetAccessTokenAsync(
            new GmailOAuthConfig(config.AccountEmailAddress!, config.ClientId, GmailOAuthService.UnprotectClientSecret(config.ClientSecret), GmailOAuthService.UnprotectRefreshToken(config.EncryptedRefreshToken)),
            cancellationToken);

        return new GmailImapConnectionSettings(
            config.AccountEmailAddress!,
            GmailAuthenticationMode.OAuth,
            accessToken,
            null,
            profile.Host,
            profile.Port,
            profile.UseSsl,
            config.ImapProvider);
    }

    internal static DateTime InferReportDay(DateTimeOffset internalDate) =>
        internalDate.UtcDateTime.Date.AddDays(-1);
}
