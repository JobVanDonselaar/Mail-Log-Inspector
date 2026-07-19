# Unified Imports And Admin Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the separate recent-import and Gmail-history tables with one import list, and move Gmail configuration to a `/admin` dialog that can also be opened from Help.

**Architecture:** Persist the imported source hash in the operational Gmail history and merge that history with normal imports in a focused presentation builder. Move all configuration controls and connection testing into `AdminSettingsWindow`; the main window retains only synchronization execution and displays progress in the existing manage status bar. Extend the single-instance pipe protocol with `activate` and `admin` requests.

**Tech Stack:** .NET 10, C#, WPF, Microsoft.Data.Sqlite, MailKit, xUnit.

## Global Constraints

- Do not edit production SQLite databases manually; schema additions and backfill run through the EXE.
- Preserve existing local changes and the automatic Gmail midnight policy.
- Normal startup must not show Gmail settings.
- `/admin` must show settings before the first main window.
- `/admin` against an existing instance must open one modal admin window in that instance.
- A blank password or client-secret field must preserve the encrypted stored value.
- Keep `Sync nu` available in the normal Manage actions.
- Update Help and add a small admin button at the bottom.
- Increase the application version to `0.182`.
- Build Debug only; do not publish.

---

### Task 1: Persist A Stable Gmail-To-Import Link

**Files:**
- Create: `src/MailLogInspector.App/GmailZipImportOutcome.cs`
- Modify: `src/MailLogInspector.App/IGmailZipImportRunner.cs`
- Modify: `src/MailLogInspector.App/GmailZipImportRunner.cs`
- Modify: `src/MailLogInspector.App/GmailReportSyncService.cs`
- Modify: `src/MailLogInspector.Storage/GmailReportHistoryRow.cs`
- Modify: `src/MailLogInspector.Storage/GmailReportOperationalStore.cs`
- Test: `tests/MailLogInspector.Storage.Tests/GmailReportOperationalStoreTests.cs`
- Test: `tests/MailLogInspector.Storage.Tests/GmailReportSyncServiceTests.cs`

**Interfaces:**
- Produces: `GmailZipImportOutcome(bool Success, string SourceHash)`.
- Produces: nullable `GmailReportHistoryRow.SourceHash`.
- Produces: `BackfillMissingSourceHashes(IReadOnlyList<MailLogInspectorImportedFile>)`.

- [ ] **Step 1: Write failing operational-store tests**

Add tests proving `source_hash` is created, round-tripped, and only backfilled when a ZIP filename maps to exactly one import.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj --filter "FullyQualifiedName~GmailReportOperationalStoreTests|FullyQualifiedName~GmailReportSyncServiceTests" --no-restore -v minimal -tl:off
```

Expected: compile failures for the missing source-hash APIs.

- [ ] **Step 3: Implement source-hash persistence**

Use an idempotent `EnsureColumnExists(..., "source_hash", "TEXT NULL")`, include the value in all Gmail history reads/upserts, and hash the downloaded ZIP before import:

```csharp
public sealed record GmailZipImportOutcome(bool Success, string SourceHash);

public interface IGmailZipImportRunner
{
    Task<GmailZipImportOutcome> ImportAsync(string zipPath, CancellationToken cancellationToken);
}
```

Backfill only exact, unique case-insensitive filename matches and never overwrite a non-empty hash.

- [ ] **Step 4: Run focused tests and verify GREEN**

Use the Task 1 command; all selected tests must pass.

---

### Task 2: Build One Import History

**Files:**
- Create: `src/MailLogInspector.App/ImportHistoryListItem.cs`
- Create: `src/MailLogInspector.App/ImportHistoryListBuilder.cs`
- Modify: `src/MailLogInspector.App/MainWindow.xaml.cs`
- Test: `tests/MailLogInspector.Storage.Tests/ImportHistoryListBuilderTests.cs`

**Interfaces:**
- Consumes: imports and Gmail history with optional source hashes.
- Produces: `ImportHistoryListBuilder.Build(imports, gmailHistory)`.

- [ ] **Step 1: Write failing merge tests**

Cover one-row Gmail success, manual import, download failure, deletion failure, duplicate suppression, report-period formatting, and descending date order.

- [ ] **Step 2: Run the focused tests and verify RED**

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj --filter "FullyQualifiedName~ImportHistoryListBuilderTests" --no-restore -v minimal -tl:off
```

Expected: compile failure because the builder does not exist.

- [ ] **Step 3: Implement the builder**

The item exposes real sortable values plus displays:

```csharp
internal sealed record ImportHistoryListItem(
    DateTime Timestamp,
    string Source,
    string FileName,
    string ReportPeriod,
    int? MailCount,
    int? DeliveredCount,
    int? BounceCount,
    int? UnderwayCount,
    string Status,
    string? ErrorText);
```

Successful unlinked legacy Gmail rows are suppressed; failed Gmail rows remain visible; `skip` and `duplicate` rows do not create imports.

- [ ] **Step 4: Use the builder in Manage refresh**

Backfill missing hashes, read refreshed history, build once, and bind the merged list.

- [ ] **Step 5: Run focused tests and verify GREEN**

Use the Task 2 command.

---

### Task 3: Add The Admin Settings Window

**Files:**
- Create: `src/MailLogInspector.App/AdminSettingsWindow.xaml`
- Create: `src/MailLogInspector.App/AdminSettingsWindow.xaml.cs`
- Modify: `src/MailLogInspector.App/App.cs`
- Modify: `src/MailLogInspector.App/MainWindow.Tray.cs`
- Test: `tests/MailLogInspector.Storage.Tests/AdminStartupTests.cs`
- Test: `tests/MailLogInspector.Storage.Tests/SingleInstanceStartupTests.cs`

**Interfaces:**
- Produces: `App.ShowAdminSettings(Window? owner)`.
- Produces pipe requests `activate` and `admin`.
- Consumes `GmailReportOperationalStore`, `GmailOAuthService`, and `IGmailImapReportClient`.

- [ ] **Step 1: Write failing startup and layout tests**

Assert `/admin` parsing, pre-main modal flow, cancel shutdown, existing-instance `admin` request, password preservation, and the required controls.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj --filter "FullyQualifiedName~AdminStartupTests|FullyQualifiedName~SingleInstanceStartupTests" --no-restore -v minimal -tl:off
```

Expected: failures for missing admin window and request protocol.

- [ ] **Step 3: Implement the dialog**

Load non-secret fields, leave password and client-secret boxes empty, preserve stored encrypted values on blank input, test app-password IMAP directly, and retain the existing OAuth authorization flow.

- [ ] **Step 4: Implement startup and single-instance routing**

For the first `/admin` invocation, show the dialog before constructing `MainWindow`; cancel shuts down. For a running instance, dispatch `admin` on the UI thread, restore the owner, and activate an existing admin dialog instead of opening another.

- [ ] **Step 5: Run focused tests and verify GREEN**

Use the Task 3 command.

---

### Task 4: Simplify Manage And Update Help

**Files:**
- Modify: `src/MailLogInspector.App/MainWindow.xaml`
- Modify: `src/MailLogInspector.App/MainWindow.Gmail.cs`
- Modify: `src/MailLogInspector.App/MainWindow.xaml.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/MainWindowManageLayoutTests.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/MainWindowLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `App.ShowAdminSettings(this)` from the Help button.
- Consumes: merged `ImportHistoryListItem` rows.

- [ ] **Step 1: Replace obsolete layout tests with failing desired-layout tests**

Require one `ImportsGrid`, no Gmail settings/history controls in `MainWindow.xaml`, two-column-spanning import tile, nine agreed columns, `/admin` help text, and `OpenAdminSettingsButton`.

- [ ] **Step 2: Run focused layout tests and verify RED**

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj --filter "FullyQualifiedName~MainWindowManageLayoutTests|FullyQualifiedName~MainWindowLayoutConsistencyTests" --no-restore -v minimal -tl:off
```

Expected: assertions fail against the old Sync tile and two-list layout.

- [ ] **Step 3: Update XAML**

Span the `Imports` tile over columns 0 and 1, remove the Sync tile and Gmail history, bind the agreed columns, retain `Sync nu` in Actions, and add the small Help admin button at the bottom.

- [ ] **Step 4: Simplify main-window Gmail execution**

Remove settings-form handlers from `MainWindow.Gmail.cs`. Load the stored config for sync, show Gmail progress through `StatusTextBlock`, `ImportProgressBar`, and `ImportProgressTextBlock`, then refresh the merged list.

- [ ] **Step 5: Update Help copy**

Explain automatic synchronization, the consolidated import statuses, `/admin`, and the Help shortcut using short business-oriented bullets.

- [ ] **Step 6: Run focused layout tests and verify GREEN**

Use the Task 4 command.

---

### Task 5: Version And Full Verification

**Files:**
- Modify: `src/MailLogInspector.App/MailLogInspector.App.csproj`
- Modify: `src/MailLogInspector.App/MailLogInspectorVersion.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/MainWindowLayoutConsistencyTests.cs`

- [ ] **Step 1: Change expected version to `0.182` and verify RED**

Run the version test and confirm it fails while production still reports `0.181`.

- [ ] **Step 2: Bump all application version fields to `0.182`**

- [ ] **Step 3: Run all tests**

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj --no-restore -v minimal -tl:off
```

Expected: all tests pass.

- [ ] **Step 4: Build Debug**

```powershell
dotnet build src\MailLogInspector.App\MailLogInspector.App.csproj -c Debug --no-restore -v minimal -tl:off -m:1 -nr:false
```

Expected: zero warnings and zero errors.

- [ ] **Step 5: Verify output**

Run `git diff --check` and inspect the Debug EXE file version. Do not publish.
