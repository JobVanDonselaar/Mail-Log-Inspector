# SMTP Portal Session and Report Syntax Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reuse the existing persistent SMTP.com portal session without a keepalive timer, make the daily report-name syntax safely configurable in `/admin`, and use the same syntax for probe and production downloads.

**Architecture:** A new `SmtpPortalReportNameSyntax` component validates a user-friendly literal template and compiles it to an anchored regex. The operational SMTP portal configuration stores the syntax choice and the last successful `My reports` read. Probe and production sources resolve the same effective template. The existing persistent WebView2 profile remains the session mechanism; no timer or background refresh is added.

**Tech Stack:** .NET 10, C# 14, WPF, WebView2, Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- Do not edit or migrate the production mail SQLite database directly.
- Only `smtp_portal_config` in the operational configuration database may be extended.
- Preserve the persistent WebView2 profile at `%LOCALAPPDATA%\Mail Log Inspector\WebView2\SmtpPortal`.
- Do not add a portal keepalive timer or periodic browser refresh.
- Never log credentials, MFA secrets, TOTP values, cookies, DOM content, or account details.
- Keep `AdminSettingsWindow` fixed at `1060x720` and do not add a `ScrollViewer`.
- Bump the application version from `0.191` to `0.192`.
- Do not publish. Produce and verify only the Debug build.
- The worktree already contains approved, uncommitted synchronization changes. Before each commit, review the complete diff and stage only the files named in that task. Never revert unrelated local changes.

---

## Task 1: Add Safe Report-Name Template Parsing

**Files:**

- Create: `src/MailLogInspector.App/SmtpPortalReportNameSyntax.cs`
- Modify: `src/MailLogInspector.App/SmtpPortalReportMatcher.cs`
- Create: `tests/MailLogInspector.Storage.Tests/SmtpPortalReportNameSyntaxTests.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/SmtpPortalReportMatcherTests.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/SmtpPortalReportSelectionTests.cs`

- [ ] **Step 1: Write failing template-validation tests**

Add tests for:

- `DefaultTemplate` equals:
  `NextGen_{start}(00)_{end}(00) (delivered + bounced + queue) (raw_event_stream)`.
- A template with exactly one `{start}` and one `{end}` is valid.
- Missing `{start}` or `{end}` is rejected with a Dutch validation message.
- Duplicate `{start}` or `{end}` is rejected.
- Unknown placeholders such as `{date}` are rejected.
- Unbalanced braces are rejected.
- An empty template and a template longer than 300 characters are rejected.
- Literal regex characters such as `(`, `)`, `+`, `.`, and `[` are escaped rather than interpreted.
- `BuildExample` replaces the placeholders with `2026-07-17` and `2026-07-18`.

- [ ] **Step 2: Write failing matcher tests**

Add overload tests proving:

- The existing default overload still accepts
  `NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)`.
- A custom template such as
  `Exquise-{start}-tot-{end}-dagrapport.zip`
  accepts `Exquise-2026-07-17-tot-2026-07-18-dagrapport.zip`.
- The custom template does not accept the default name.
- `Ready` remains mandatory.
- End date must be later than start date.
- `SelectNewest` and `SelectRequired` use the supplied custom template.

- [ ] **Step 3: Run the focused tests and confirm failure**

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj -m:1 -p:NuGetAudit=false --filter "FullyQualifiedName~SmtpPortalReportNameSyntaxTests|FullyQualifiedName~SmtpPortalReportMatcherTests|FullyQualifiedName~SmtpPortalReportSelectionTests"
```

Expected: compilation or assertion failures because `SmtpPortalReportNameSyntax` and matcher overloads do not exist.

- [ ] **Step 4: Implement `SmtpPortalReportNameSyntax`**

Use this public surface:

```csharp
public sealed record SmtpPortalReportSyntaxValidation(
    bool IsValid,
    string? ErrorMessage);

public static class SmtpPortalReportNameSyntax
{
    public const string DefaultTemplate =
        "NextGen_{start}(00)_{end}(00) (delivered + bounced + queue) (raw_event_stream)";

    public static SmtpPortalReportSyntaxValidation Validate(string? template);
    public static string ResolveTemplate(bool useDefault, string? customTemplate);
    public static string BuildExample(
        string template,
        DateTime? start = null,
        DateTime? end = null);
    public static Regex BuildRegex(string template);
}
```

Implementation rules:

- Trim before validation and matching.
- Require exactly one `{start}` and exactly one `{end}`.
- Reject every other brace-delimited token and unbalanced braces.
- Escape the entire literal template with `Regex.Escape`.
- Replace only escaped `{start}` and `{end}` with named
  `yyyy-MM-dd` capture groups.
- Add `^` and `$` anchors and use `RegexOptions.CultureInvariant`.
- `ResolveTemplate(true, customTemplate)` always returns `DefaultTemplate`.
- `ResolveTemplate(false, invalidTemplate)` throws `InvalidOperationException`
  with the validation message.

- [ ] **Step 5: Update `SmtpPortalReportMatcher` without breaking callers**

Keep all existing overloads and add explicit template overloads:

```csharp
public static bool TryParse(
    string name,
    string status,
    string reportNameTemplate,
    out SmtpPortalReport? report,
    string rowKey = "");

public static SmtpPortalReport SelectNewest(
    IEnumerable<SmtpPortalReportRow> rows,
    string reportNameTemplate);

public static IReadOnlyList<SmtpPortalReport> SelectRequired(
    IEnumerable<SmtpPortalReportRow> rows,
    DateTime? latestReportDay,
    DateTime yesterday,
    bool latestOnly,
    string reportNameTemplate);
```

The existing overloads delegate to `DefaultTemplate`. Continue to parse dates
with `DateTime.TryParseExact` using format `yyyy-MM-dd`,
`CultureInfo.InvariantCulture`, and `DateTimeStyles.None`; continue to reject
`end <= start`.

- [ ] **Step 6: Run focused tests and confirm pass**

Run the command from Step 3.

Expected: all syntax, matcher, and report-selection tests pass.

- [ ] **Step 7: Commit only Task 1 files**

```powershell
git add src/MailLogInspector.App/SmtpPortalReportNameSyntax.cs src/MailLogInspector.App/SmtpPortalReportMatcher.cs tests/MailLogInspector.Storage.Tests/SmtpPortalReportNameSyntaxTests.cs tests/MailLogInspector.Storage.Tests/SmtpPortalReportMatcherTests.cs tests/MailLogInspector.Storage.Tests/SmtpPortalReportSelectionTests.cs
git diff --cached --check
git commit -m "feat: support configurable SMTP report syntax"
```

---

## Task 2: Persist Syntax Settings and Portal-Use Timestamp

**Files:**

- Modify: `src/MailLogInspector.Storage/SmtpPortalConfig.cs`
- Modify: `src/MailLogInspector.Storage/SmtpPortalOperationalStore.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/SmtpPortalOperationalStoreTests.cs`

- [ ] **Step 1: Write failing operational-store tests**

Extend the round-trip test and add a legacy-schema migration test for:

- `UseDefaultReportSyntax`.
- `CustomReportSyntax`.
- `LastSuccessfulPortalUseAtUtc`.
- Existing databases with the old five-column `smtp_portal_config` migrate
  idempotently when `Initialize()` runs.
- A migrated database defaults to standard syntax.
- Calling `Initialize()` twice remains safe.
- `RecordSuccessfulPortalUse(usedAtUtc)` updates only
  `last_successful_portal_use_at_utc` and preserves username, encrypted secrets,
  status, and syntax settings.

- [ ] **Step 2: Run the focused tests and confirm failure**

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj -m:1 -p:NuGetAudit=false --filter FullyQualifiedName~SmtpPortalOperationalStoreTests
```

Expected: compilation or assertion failures because the new config properties,
columns, and update method do not exist.

- [ ] **Step 3: Extend `SmtpPortalConfig` source-compatibly**

Append optional positional properties so existing five-argument construction
continues to compile:

```csharp
public sealed record SmtpPortalConfig(
    string? Username,
    string? EncryptedPassword,
    string? EncryptedTotpSecret,
    string? ConnectionStatus,
    DateTime? LastProbeAtUtc,
    bool UseDefaultReportSyntax = true,
    string? CustomReportSyntax = null,
    DateTime? LastSuccessfulPortalUseAtUtc = null)
```

Update `Empty` to use the defaults.

- [ ] **Step 4: Add an idempotent operational migration**

After the existing `CREATE TABLE IF NOT EXISTS` statements, inspect
`PRAGMA table_info(smtp_portal_config)` and add missing columns one at a time:

```sql
ALTER TABLE smtp_portal_config
ADD COLUMN use_default_report_syntax INTEGER NOT NULL DEFAULT 1;

ALTER TABLE smtp_portal_config
ADD COLUMN custom_report_syntax TEXT NULL;

ALTER TABLE smtp_portal_config
ADD COLUMN last_successful_portal_use_at_utc TEXT NULL;
```

Run the column checks and `ALTER TABLE` statements inside one transaction.
Do not touch the production mail database.

- [ ] **Step 5: Extend config load/save and add runtime timestamp update**

Map all eight fields in `LoadConfig` and `SaveConfig`. Store booleans as `0/1`
and dates as round-trip UTC strings.

Add:

```csharp
public void RecordSuccessfulPortalUse(DateTime usedAtUtc);
```

Implement it as an upsert on `config_id = 1` that changes only
`last_successful_portal_use_at_utc`.

- [ ] **Step 6: Run focused tests and confirm pass**

Run the command from Step 2.

Expected: all operational-store tests pass, including legacy migration and
idempotence.

- [ ] **Step 7: Commit only Task 2 files**

```powershell
git add src/MailLogInspector.Storage/SmtpPortalConfig.cs src/MailLogInspector.Storage/SmtpPortalOperationalStore.cs tests/MailLogInspector.Storage.Tests/SmtpPortalOperationalStoreTests.cs
git diff --cached --check
git commit -m "feat: persist SMTP report syntax settings"
```

---

## Task 3: Apply One Syntax to Probe and Production Downloads

**Files:**

- Modify: `src/MailLogInspector.App/SmtpPortalProbeService.cs`
- Modify: `src/MailLogInspector.App/SmtpPortalReportSyncSource.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/SmtpPortalProbeServiceTests.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/SmtpPortalReportSyncSourceTests.cs`

- [ ] **Step 1: Write failing probe-service tests**

Add tests proving:

- A custom configured syntax selects and downloads a custom-named `Ready`
  report.
- A successful `ReadFirstPageReportsAsync` stores
  `LastSuccessfulPortalUseAtUtc`.
- A failure before or during `ReadFirstPageReportsAsync` does not update the
  timestamp.
- A failure after the reports page was read does retain the updated timestamp.
- The probe remains non-importing and keeps existing ZIP/hash behavior.

Inject a `Func<DateTime>` UTC clock into `SmtpPortalProbeService` so timestamp
assertions are deterministic.

- [ ] **Step 2: Write failing production-source tests**

Add tests proving:

- Production sync accepts a custom-named report when custom syntax is active.
- It rejects the default name when only the custom syntax is active.
- A successful first-page read stores `LastSuccessfulPortalUseAtUtc`, including
  the no-matching-report case.
- A report-read failure leaves the prior timestamp unchanged.
- No page beyond page 1 is requested and existing page-size policy remains
  unchanged.

Inject a `Func<DateTime>` UTC clock into `SmtpPortalReportSyncSource`.

- [ ] **Step 3: Run focused tests and confirm failure**

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj -m:1 -p:NuGetAudit=false --filter "FullyQualifiedName~SmtpPortalProbeServiceTests|FullyQualifiedName~SmtpPortalReportSyncSourceTests"
```

Expected: new syntax and timestamp assertions fail.

- [ ] **Step 4: Resolve the effective template in both services**

Immediately before report selection:

```csharp
string reportNameTemplate = SmtpPortalReportNameSyntax.ResolveTemplate(
    config.UseDefaultReportSyntax,
    config.CustomReportSyntax);
```

Pass this value to `SelectNewest` in the probe and `SelectRequired` in
production. Do not duplicate validation or regex construction in either
service.

- [ ] **Step 5: Record successful portal use at the correct boundary**

After `ReadFirstPageReportsAsync` returns successfully and before matching or
downloading:

```csharp
DateTime usedAtUtc = _utcNowProvider();
_operationalStore.RecordSuccessfulPortalUse(usedAtUtc);
config = config with { LastSuccessfulPortalUseAtUtc = usedAtUtc };
```

Use `_portalStore` in production. Keeping the updated local `config` in the
probe prevents later status saves from overwriting the timestamp.

Do not update the timestamp for initialization, login, MFA, or report-read
failures.

- [ ] **Step 6: Preserve existing session behavior**

Do not alter `SmtpPortalBrowserWindow` navigation or authentication. It already:

- uses the persistent profile path supplied by both services;
- opens `https://my.smtp.com/reporting?tab=reports`;
- checks for the reports page before submitting credentials;
- re-enters login/MFA only when the portal requires it.

Add no timer, dispatcher callback, hidden browser lifetime, or 30-minute
refresh.

- [ ] **Step 7: Run focused tests and confirm pass**

Run the command from Step 3.

Expected: all probe and production-source tests pass.

- [ ] **Step 8: Commit only Task 3 files**

```powershell
git add src/MailLogInspector.App/SmtpPortalProbeService.cs src/MailLogInspector.App/SmtpPortalReportSyncSource.cs tests/MailLogInspector.Storage.Tests/SmtpPortalProbeServiceTests.cs tests/MailLogInspector.Storage.Tests/SmtpPortalReportSyncSourceTests.cs
git diff --cached --check
git commit -m "feat: apply SMTP report syntax to all downloads"
```

---

## Task 4: Add Compact `/admin` Syntax Controls

**Files:**

- Modify: `src/MailLogInspector.App/SmtpPortalAdminConfigBuilder.cs`
- Modify: `src/MailLogInspector.App/AdminSettingsWindow.xaml`
- Modify: `src/MailLogInspector.App/AdminSettingsWindow.xaml.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/AdminStartupTests.cs`

- [ ] **Step 1: Write failing builder tests**

Append optional fields to `SmtpPortalAdminSettingsInput`:

```csharp
bool UseDefaultReportSyntax = true,
string CustomReportSyntax = ""
```

Test that:

- default mode stores `UseDefaultReportSyntax = true`;
- custom mode trims and stores a valid custom template;
- invalid custom mode throws `InvalidOperationException` with the validator
  message;
- stored encrypted credentials remain preserved as before;
- `LastSuccessfulPortalUseAtUtc` and runtime status fields are preserved.

- [ ] **Step 2: Write failing XAML contract tests**

Extend `AdminWindow_ContainsRequiredControls` for:

- `AdminDefaultReportSyntaxRadioButton`;
- `AdminCustomReportSyntaxRadioButton`;
- `AdminDefaultReportSyntaxTextBox`;
- `AdminCustomReportSyntaxTextBox`;
- `AdminReportSyntaxExplanationTextBlock`;
- `AdminReportSyntaxPreviewTextBlock`;
- `AdminReportSyntaxValidationTextBlock`;
- `AdminSmtpPortalLastUsedTextBlock`;
- no `<ScrollViewer`;
- unchanged `Width="1060"` and `Height="720"`.

Assert that both radio buttons share one `GroupName`, making them mutually
exclusive.

- [ ] **Step 3: Run focused tests and confirm failure**

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj -m:1 -p:NuGetAudit=false --filter FullyQualifiedName~AdminStartupTests
```

Expected: builder and XAML control assertions fail.

- [ ] **Step 4: Update the admin input and builder**

Use `SmtpPortalReportNameSyntax.ResolveTemplate` when custom mode is selected.
Persist the normalized custom text only in custom mode. Keep the stored custom
text when switching temporarily back to default mode so it is available if the
administrator selects custom again.

- [ ] **Step 5: Add compact syntax controls to the SMTP.com card**

Keep the two-column page and fixed window. Rework only the left card rows:

1. title and description;
2. username/password;
3. MFA/probe buttons;
4. one horizontal syntax-choice row;
5. one full-width syntax text field;
6. one compact explanation/preview/validation row;
7. portal status, last successful use, and progress.

Display the default template in a read-only text box. Enable the custom text box
only when `Aangepaste syntax` is selected.

Use these labels:

- `Standaardsyntax`
- `Aangepaste syntax`
- `{start} en {end} gebruiken yyyy-MM-dd. Alle andere tekens en spaties zijn letterlijk.`
- `Voorbeeld: NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)`
- `Portaalsessie laatst succesvol gebruikt: dd-MM-yyyy HH:mm`
- `Portaalsessie nog niet succesvol gebruikt.`

Fit the content without shrinking buttons below existing minimums and without
adding a scrollbar.

- [ ] **Step 6: Wire loading, preview, validation, saving, probe, and sync**

In `LoadConfig`:

- select the stored mode;
- populate both template fields;
- show the stored last-success time in local time.

Add one `Checked` handler for both radio buttons and one `TextChanged` handler
for the custom field. Both call an `UpdateReportSyntaxUi` helper that:

- enables/disables the custom field;
- validates the effective template;
- updates the example;
- shows a red business-style validation message when invalid.

Change saving to a guarded flow:

```csharp
private bool TrySaveCurrentSettings();
```

`SaveAdminSettingsButton_Click` closes only when it returns `true`.
`RunReportSyncNowButton_Click` stops before syncing when it returns `false`.
Probe buttons also validate and persist the current syntax before opening the
portal.

Build the portal config from `_smtpPortalStore.LoadConfig()` rather than a stale
dialog-open snapshot so a concurrent successful portal-use timestamp is not
lost.

- [ ] **Step 7: Run focused tests and confirm pass**

Run the command from Step 3.

Expected: all admin builder and XAML contract tests pass.

- [ ] **Step 8: Build the app to validate XAML compilation**

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet build src\MailLogInspector.App\MailLogInspector.App.csproj -c Debug -m:1 -p:NuGetAudit=false
```

Expected: build succeeds with no XAML name, event-handler, or binding errors.

- [ ] **Step 9: Commit only Task 4 files**

```powershell
git add src/MailLogInspector.App/SmtpPortalAdminConfigBuilder.cs src/MailLogInspector.App/AdminSettingsWindow.xaml src/MailLogInspector.App/AdminSettingsWindow.xaml.cs tests/MailLogInspector.Storage.Tests/AdminStartupTests.cs
git diff --cached --check
git commit -m "feat: configure SMTP report syntax in admin"
```

---

## Task 5: Help, Documentation, Version, and Full Verification

**Files:**

- Modify: `src/MailLogInspector.App/MainWindow.xaml`
- Modify: `README.md`
- Modify: `docs/smtp-report-download-flow.md`
- Modify: `src/MailLogInspector.App/MailLogInspector.App.csproj`
- Modify: `src/MailLogInspector.App/MailLogInspectorVersion.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/MainWindowLayoutConsistencyTests.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/MainWindowManageLayoutTests.cs` only if
  existing help/admin assertions require it

- [ ] **Step 1: Write failing documentation/version assertions**

Update layout/version tests to require:

- app version `0.192`;
- Help mentions persistent SMTP.com session reuse;
- Help mentions standard/custom syntax and `{start}`/`{end}`;
- Help explicitly states that the app logs in again only when the portal session
  has expired.

- [ ] **Step 2: Run focused tests and confirm failure**

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj -m:1 -p:NuGetAudit=false --filter "FullyQualifiedName~MainWindowLayoutConsistencyTests|FullyQualifiedName~MainWindowManageLayoutTests"
```

Expected: version and help-text assertions fail.

- [ ] **Step 3: Update Help and documentation**

Update Help from top to bottom with short business language:

- direct sync first reuses the saved portal session;
- expired sessions automatically follow login and MFA;
- no 30-minute keepalive is used;
- standard syntax remains the safe default;
- custom syntax uses exactly `{start}` and `{end}` in `yyyy-MM-dd`;
- probe and production sync use the same syntax;
- invalid syntax cannot be saved.

Update `README.md` synchronization section to `0.192`.
Update `docs/smtp-report-download-flow.md` with the same operational behavior,
template example, validation rules, and operational-only schema additions.

- [ ] **Step 4: Bump version consistently**

Set:

```xml
<Version>0.192.0</Version>
<AssemblyVersion>0.192.0.0</AssemblyVersion>
<FileVersion>0.192.0.0</FileVersion>
<InformationalVersion>0.192</InformationalVersion>
```

Set:

```csharp
public const string SemanticVersion = "0.192";
```

Update corresponding test literals.

- [ ] **Step 5: Run focused documentation/version tests**

Run the command from Step 2.

Expected: all layout, help, and version assertions pass.

- [ ] **Step 6: Run the complete test suite**

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj -m:1 -p:NuGetAudit=false
```

Expected: every test passes with zero failures.

- [ ] **Step 7: Produce and verify the Debug build**

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet build src\MailLogInspector.App\MailLogInspector.App.csproj -c Debug -m:1 -p:NuGetAudit=false
```

```powershell
(Get-Item 'src\MailLogInspector.App\bin\Debug\net10.0-windows\MailLogInspector.exe').VersionInfo | Select-Object FileVersion, ProductVersion
```

Expected: build succeeds and both displayed versions identify `0.192`.

- [ ] **Step 8: Verify repository hygiene**

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors. Review every remaining modified/untracked file
and distinguish this feature from pre-existing approved synchronization work.

- [ ] **Step 9: Commit documentation and version files**

```powershell
git add src/MailLogInspector.App/MainWindow.xaml README.md docs/smtp-report-download-flow.md src/MailLogInspector.App/MailLogInspector.App.csproj src/MailLogInspector.App/MailLogInspectorVersion.cs tests/MailLogInspector.Storage.Tests/MainWindowLayoutConsistencyTests.cs
git diff --cached --check
git commit -m "docs: explain SMTP portal session and syntax"
```

If `MainWindowManageLayoutTests.cs` was changed, add it explicitly before the
commit.

---

## Acceptance Checklist

- [ ] `/admin` shows standard and custom report syntax without a scrollbar.
- [ ] Default mode matches the current SMTP.com daily report exactly.
- [ ] Custom mode accepts only a valid literal template with one `{start}` and
  one `{end}`.
- [ ] Probe and production direct sync use the same effective template.
- [ ] Existing installations migrate to default syntax automatically.
- [ ] `My reports` successful reads update the visible last-use timestamp.
- [ ] Login/MFA is skipped while the persistent portal session remains valid.
- [ ] No keepalive timer or periodic portal refresh exists.
- [ ] Product mail databases and archives are never manually modified.
- [ ] All tests pass, Debug build succeeds, EXE reports version `0.192`, and no
  publish is performed.
