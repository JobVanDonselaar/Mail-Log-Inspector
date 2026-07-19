# Synchronisatiebron en SMTP.com-productie-import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Voeg drie selecteerbare synchronisatiebronnen toe, importeer dagelijkse SMTP.com-rapporten rechtstreeks met Gmail-fallback en toon bij iedere import een eenduidige bron.

**Architecture:** Een brononafhankelijke `ReportSyncCoordinator` stuurt Gmail en SMTP.com direct aan. Een kleine operationele SQLite-store bewaart modus, productie-syncresultaten en de bron per importhash; de grote maildatabase krijgt geen extra gegevens per mail. De bestaande importservice blijft het enige pad dat ZIP-inhoud in de database schrijft.

**Tech Stack:** .NET 10, C#, WPF, Microsoft.Data.Sqlite, WebView2, xUnit.

## Global Constraints

- Modi: `Direct downloaden, bij fout Gmail`, `Alleen Gmail`, `Alleen direct downloaden`.
- Bestaande installaties starten op `Alleen Gmail`.
- Zonder importhistorie wordt uitsluitend het nieuwste geldige rapport verwerkt.
- Automatische eerste poging is dagelijks om `01:00` lokale tijd; daarna iedere 15 minuten tot gisteren is verwerkt.
- `Sync nu` voert altijd direct uit.
- Bronlabels zijn exact `SMTP.com direct`, `Gmail` en `Handmatig`.
- De SMTP.com-proefdownload blijft read-only ten opzichte van de importservice.
- Databasewijzigingen worden uitsluitend idempotent door de EXE uitgevoerd.
- Versie wordt `0.189`; Codex publiceert niet.

---

### Task 1: Operationele synchronisatieconfiguratie en bronhistorie

**Files:**
- Create: `src/MailLogInspector.Storage/ReportSyncMode.cs`
- Create: `src/MailLogInspector.Storage/ReportSyncConfig.cs`
- Create: `src/MailLogInspector.Storage/ReportImportSourceRow.cs`
- Create: `src/MailLogInspector.Storage/ReportSyncOperationalStore.cs`
- Create: `tests/MailLogInspector.Storage.Tests/ReportSyncOperationalStoreTests.cs`

**Interfaces:**
- Produces: `ReportSyncMode.Normalize(string?)`, `ReportSyncOperationalStore.LoadConfig()`, `SaveConfig(ReportSyncConfig)`, `RecordImportSource(ReportImportSourceRow)`, `ReadImportSources(int)`.
- Consumes: de bestaande operationele SQLite-database in `MailLogInspectorWorkspacePaths.GmailOperationalDatabasePath`.

- [ ] **Step 1: Schrijf falende storetests**

Test dat een nieuwe store standaard `GmailOnly` retourneert, alle drie modi bewaart en een hash met bron en rapportdag kan teruglezen.

- [ ] **Step 2: Draai de gerichte test**

Run:

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj --no-restore --filter "FullyQualifiedName~ReportSyncOperationalStoreTests" -m:1 -v minimal -tl:off
```

Expected: FAIL omdat de nieuwe typen ontbreken.

- [ ] **Step 3: Implementeer het minimale operationele schema**

Gebruik `CREATE TABLE IF NOT EXISTS report_sync_config` en `report_import_sources`. `source_hash` is de primaire sleutel van de brontabel. Schrijf geen geheimen naar deze tabellen.

- [ ] **Step 4: Draai de test opnieuw**

Expected: PASS.

### Task 2: Bronkeuze en fallbackcoordinator

**Files:**
- Create: `src/MailLogInspector.App/IReportSyncSource.cs`
- Create: `src/MailLogInspector.App/ReportSyncSourceResult.cs`
- Create: `src/MailLogInspector.App/ReportSyncCoordinator.cs`
- Create: `tests/MailLogInspector.Storage.Tests/ReportSyncCoordinatorTests.cs`

**Interfaces:**
- Produces:

```csharp
Task<ReportSyncSourceResult> SyncAsync(
    bool latestOnly,
    DateTime? minimumReportDayExclusive,
    CancellationToken cancellationToken,
    IProgress<string>? progress = null);
```

- Produces:

```csharp
Task<ReportSyncSourceResult> RunAsync(
    string mode,
    bool latestOnly,
    DateTime? minimumReportDayExclusive,
    CancellationToken cancellationToken,
    IProgress<string>? progress = null);
```

- [ ] **Step 1: Schrijf falende coordinatortests**

Test Gmail-only, direct-only, succesvolle directe sync, fallback na exception en fallback wanneer direct `NoReadyReport=true` retourneert. Bewijs dat direct-only Gmail nooit aanroept en Gmail-only direct nooit aanroept.

- [ ] **Step 2: Draai de coordinatortests**

Expected: FAIL omdat de coordinator ontbreekt.

- [ ] **Step 3: Implementeer de coordinator**

Log iedere bronpoging met `Bron=...`. In fallbackmodus wordt Gmail na iedere directe exception of `NoReadyReport` aangeroepen. Combineer resultaat- en foutteksten zonder geheimen.

- [ ] **Step 4: Draai de tests opnieuw**

Expected: PASS.

### Task 3: Directe SMTP.com-productiebron

**Files:**
- Modify: `src/MailLogInspector.App/SmtpPortalReportMatcher.cs`
- Create: `src/MailLogInspector.App/ISmtpPortalBrowserFactory.cs`
- Create: `src/MailLogInspector.App/SmtpPortalBrowserFactory.cs`
- Create: `src/MailLogInspector.App/SmtpPortalReportSyncSource.cs`
- Modify: `src/MailLogInspector.App/GmailZipImportOutcome.cs`
- Modify: `src/MailLogInspector.App/GmailZipImportRunner.cs`
- Create: `tests/MailLogInspector.Storage.Tests/SmtpPortalReportSyncSourceTests.cs`

**Interfaces:**
- `SmtpPortalReportMatcher.SelectRequired(rows, latestDay, yesterday, latestOnly)` retourneert rapporten oudste eerst.
- `GmailZipImportOutcome` bevat `Success`, `SourceHash`, `ReportStart`, `ReportEnd` en `AlreadyImported`.
- `SmtpPortalReportSyncSource` implementeert `IReportSyncSource`.

- [ ] **Step 1: Schrijf falende matcher- en brontests**

Test lege historie met alleen nieuwste rapport, achterstand oudste eerst, spam negeren, geen Ready-rapport, bekende hash en succesvolle import met bronregistratie.

- [ ] **Step 2: Draai de gerichte tests**

Expected: FAIL op ontbrekende selectie- en brontypen.

- [ ] **Step 3: Implementeer selectie en productie-import**

Gebruik pagina 1, standaard 10 resultaten en alleen bij meer dan drie dagen achterstand 100. Download naar `Incoming\SmtpPortal`, valideer ZIP, importeer via de bestaande runner en registreer `SMTP.com direct`.

- [ ] **Step 4: Draai de tests opnieuw**

Expected: PASS.

### Task 4: Gmail als coordinatorbron en begrensde eerste import

**Files:**
- Modify: `src/MailLogInspector.App/GmailReportSyncService.cs`
- Create: `src/MailLogInspector.App/GmailReportSyncSource.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/GmailReportSyncServiceTests.cs`
- Create: `tests/MailLogInspector.Storage.Tests/GmailReportSyncSourceTests.cs`

**Interfaces:**
- `GmailReportSyncService.SyncAsync(..., bool latestOnly = false, DateTime? minimumReportDayExclusive = null)` verwerkt bij `latestOnly` maximaal het nieuwste geldige rapport en gebruikt de Gmail-ontvangstdag minus één dag om oudere catch-upberichten vóór de basisdag uit te sluiten.
- `GmailReportSyncSource` implementeert `IReportSyncSource` en registreert geslaagde hashes als `Gmail`.

- [ ] **Step 1: Schrijf falende Gmail-tests**

Test dat een lege database met meerdere Gmail-kandidaten slechts het nieuwste geldige rapport importeert. Test bronregistratie en behoud van permanente verwijdering.

- [ ] **Step 2: Draai de tests**

Expected: FAIL omdat `latestOnly` en de bronadapter ontbreken.

- [ ] **Step 3: Implementeer de minimale begrenzing en adapter**

Stop bij `latestOnly` na de eerste succesvolle of reeds geïmporteerde geldige rapportmail. Laat bestaande delete-retrylogica intact.

- [ ] **Step 4: Draai de tests opnieuw**

Expected: PASS.

### Task 5: 01:00-planning en MainWindow-integratie

**Files:**
- Create: `src/MailLogInspector.App/ReportSyncSchedulePolicy.cs`
- Modify: `src/MailLogInspector.App/MainWindow.xaml.cs`
- Modify: `src/MailLogInspector.App/MainWindow.Gmail.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/GmailStartupSyncPolicyTests.cs`
- Create: `tests/MailLogInspector.Storage.Tests/ReportSyncSchedulePolicyTests.cs`

**Interfaces:**
- `ReportSyncSchedulePolicy.ShouldRunAutomatic(bool enabled, string? archiveMonth, DateTime? latestReportDay, DateTime utcNow, TimeZoneInfo zone)` bepaalt de automatische poging op basis van 01:00 en de vraag of gisteren al als dagrapport in de importtabel staat.
- `MainWindow` gebruikt één `RunReportSyncAsync(bool automatic)` voor startup, timer en `Sync nu`.

- [ ] **Step 1: Schrijf falende planningstests**

Test 00:59 false, 01:00 true, later true wanneer gisteren ontbreekt, vandaag al succesvol false en handmatige sync onafhankelijk van deze policy.

- [ ] **Step 2: Draai de planningstests**

Expected: FAIL omdat de nieuwe policy ontbreekt.

- [ ] **Step 3: Implementeer policy en vervang Gmail-specifieke orchestration**

Behoud de timer op 15 minuten. Lees voor iedere run modus en laatste geïmporteerde rapportdag; `latestOnly` is waar wanneer die dag ontbreekt.

- [ ] **Step 4: Draai de tests opnieuw**

Expected: PASS.

### Task 6: Admin-GUI en importbron in Beheer

**Files:**
- Modify: `src/MailLogInspector.App/AdminSettingsWindow.xaml`
- Modify: `src/MailLogInspector.App/AdminSettingsWindow.xaml.cs`
- Modify: `src/MailLogInspector.App/ImportHistoryListBuilder.cs`
- Modify: `src/MailLogInspector.App/MainWindow.xaml.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/AdminStartupTests.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/ImportHistoryListBuilderTests.cs`

**Interfaces:**
- `AdminSyncSourceComboBox` gebruikt Tags uit `ReportSyncMode`.
- `ImportHistoryListBuilder.Build` ontvangt operationele bronregels en toont exact `SMTP.com direct`, `Gmail` of `Handmatig`.

- [ ] **Step 1: Schrijf falende GUI- en importlijsttests**

Test drie keuzewaarden, standaard Gmail, opslaan/herladen en bronweergave voor portal-, Gmail- en handmatige hashes.

- [ ] **Step 2: Draai de gerichte tests**

Expected: FAIL op ontbrekende combobox en builderparameter.

- [ ] **Step 3: Implementeer GUI en brontoewijzing**

Plaats de bronkeuze boven de Gmail- en SMTP.com-kaarten. Hernoem geen bestaande proefknoppen en houd beide credentialblokken beschikbaar.

- [ ] **Step 4: Draai de tests opnieuw**

Expected: PASS.

### Task 7: Handmatige logging, documentatie en versie

**Files:**
- Modify: `src/MailLogInspector.App/MainWindow.xaml.cs`
- Modify: `src/MailLogInspector.App/MainWindow.xaml`
- Modify: `src/MailLogInspector.App/MailLogInspector.App.csproj`
- Modify: `src/MailLogInspector.App/MailLogInspectorVersion.cs`
- Modify: `README.md`
- Modify: `docs/smtp-report-download-flow.md`
- Modify: `tests/MailLogInspector.Storage.Tests/MainWindowLayoutConsistencyTests.cs`

- [ ] **Step 1: Voeg een falende versie- en helpteksttest toe**

Verwacht versie `0.189`, de drie bronmodi, 01:00 en de drie bronlabels.

- [ ] **Step 2: Draai de test**

Expected: FAIL op versie `0.188`.

- [ ] **Step 3: Werk logging, Help, README en versie bij**

Log handmatige imports met `Bron=Handmatig`. Beschrijf de fallback en dat proefdownload niet importeert.

- [ ] **Step 4: Volledige verificatie**

Run:

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj --no-restore -m:1 -v minimal -tl:off
dotnet build src\MailLogInspector.App\MailLogInspector.App.csproj -c Debug --no-restore -m:1 -v minimal -tl:off
git diff --check
```

Expected: alle tests geslaagd, 0 buildfouten, 0 buildwaarschuwingen en geen whitespacefouten.

### Task 8: Handmatige acceptatievoorbereiding

**Files:**
- No code changes.

- [ ] **Step 1: Controleer het Debug-EXE-versienummer**

Verwacht `0.189.0.0`.

- [ ] **Step 2: Rapporteer het exacte testpad**

Gebruik:

```text
C:\Codex\Mail Log Inspector\src\MailLogInspector.App\bin\Debug\net10.0-windows\MailLogInspector.exe
```

- [ ] **Step 3: Publiceer niet**

De gebruiker publiceert later via `C:\Codex\Publish-MailLogInspector.bat`.
