# IMAP Provider Admin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make SMTP.com the first admin configuration block and support Gmail, Microsoft 365 / Outlook.com, and a custom IMAP server without an admin-window scrollbar.

**Architecture:** Keep the existing operational Gmail configuration database and add provider and connection fields through idempotent EXE-managed migration. Gmail remains compatible, while provider profiles resolve known IMAP hosts and the existing IMAP client reads the resolved host, port and TLS settings.

**Tech Stack:** WPF, .NET 10, MailKit, SQLite, xUnit.

## Global Constraints

- Do not modify the production SQLite database manually; the EXE migrates operational configuration data.
- Preserve existing Gmail OAuth and app-password configurations.
- Store all IMAP passwords using the existing DPAPI protection.
- Increase the application version for the completed behavior change.

---

### Task 1: Persist IMAP provider settings

**Files:**
- Create: `src/MailLogInspector.Storage/ImapProvider.cs`
- Modify: `src/MailLogInspector.Storage/GmailReportConfig.cs`
- Modify: `src/MailLogInspector.Storage/GmailReportOperationalStore.cs`
- Test: `tests/MailLogInspector.Storage.Tests/GmailReportOperationalStoreTests.cs`

- [ ] Write a failing round-trip test for Gmail, Microsoft 365 and custom IMAP profile fields.
- [ ] Run the focused test and verify it fails because the fields do not exist.
- [ ] Add idempotent configuration columns and profile normalization.
- [ ] Run the focused test and verify it passes.

### Task 2: Apply IMAP profiles to connection and secrets

**Files:**
- Modify: `src/MailLogInspector.App/GmailAdminConfigBuilder.cs`
- Modify: `src/MailLogInspector.App/GmailImapConnectionSettings.cs`
- Modify: `src/MailLogInspector.App/GmailImapReportClient.cs`
- Modify: `src/MailLogInspector.App/GmailReportSyncService.cs`
- Test: `tests/MailLogInspector.Storage.Tests/AdminStartupTests.cs`

- [ ] Write failing tests for known profile defaults, custom IMAP values and preserving an existing encrypted password.
- [ ] Run the focused test and verify it fails.
- [ ] Resolve the configured host, port and TLS state in the existing IMAP flow; use the inbox for catch-up when an All Mail folder is unavailable.
- [ ] Run focused tests and verify they pass.

### Task 3: Rebuild the admin layout and rename the tab

**Files:**
- Modify: `src/MailLogInspector.App/AdminSettingsWindow.xaml`
- Modify: `src/MailLogInspector.App/AdminSettingsWindow.xaml.cs`
- Modify: `src/MailLogInspector.App/MainWindow.xaml`
- Modify: `tests/MailLogInspector.Storage.Tests/AdminStartupTests.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/MainWindowLayoutConsistencyTests.cs`

- [ ] Write failing layout tests for SMTP-first order, provider controls, secret status label and Dashboard tab text.
- [ ] Run focused layout tests and verify they fail.
- [ ] Build a fixed-height, two-column admin layout with no ScrollViewer.
- [ ] Run focused layout tests and verify they pass.

### Task 4: Version, help and verification

**Files:**
- Modify: `src/MailLogInspector.App/MailLogInspectorVersion.cs`
- Modify: `src/MailLogInspector.App/MailLogInspector.App.csproj`
- Modify: `README.md`
- Modify: `src/MailLogInspector.App/MainWindow.xaml`

- [ ] Update documentation and help copy for direct SMTP.com and IMAP provider setup.
- [ ] Increase the version to 0.191.
- [ ] Run full tests, Debug build and `git diff --check`.
