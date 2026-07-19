# Import Quality Previous Week Shadow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the misleading same-weekday average with an exact previous-week comparison shown as a shadow bar.

**Architecture:** Keep the comparison calculation in `MainWindow.BuildImportQualityComparisonGroups`. Select the latest daily report and aggregate only daily reports whose comparison date is exactly seven days earlier. Bind both bar heights into the existing WPF card template.

**Tech Stack:** .NET 10, C#, WPF/XAML, xUnit.

## Global Constraints

- Do not modify the SQLite database directly.
- Use existing recent-import data only.
- Preserve unrelated working-tree changes.
- Increase the application version from `0.177` to `0.178`.
- Do not publish.

---

### Task 1: Specify exact previous-week behavior

**Files:**
- Modify: `tests/MailLogInspector.Storage.Tests/MainWindowManageLayoutTests.cs`
- Modify: `tests/MailLogInspector.Storage.Tests/MainWindowLayoutConsistencyTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests proving that only the exact date seven days earlier is used, missing data produces no shadow, XAML overlays both bars, and version metadata is `0.178`.

- [ ] **Step 2: Run tests to verify RED**

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj --filter "FullyQualifiedName~ImportQuality|FullyQualifiedName~Version0178" --no-restore -v minimal -tl:off
```

Expected: failures because the previous-week properties, bindings, and version do not exist.

### Task 2: Implement comparison and shadow bars

**Files:**
- Modify: `src/MailLogInspector.App/MainWindow.xaml.cs`
- Modify: `src/MailLogInspector.App/MainWindow.xaml`

- [ ] **Step 1: Implement the minimal comparison**

Select the latest daily import, aggregate imports at `GetImportComparisonDate(latest).AddDays(-7)`, calculate shared scales, and return zero-height fallback data when absent.

- [ ] **Step 2: Implement the overlay**

Render a 56-pixel light-gray previous-week bar behind a 36-pixel current bar and remove the hardcoded fallback badge.

- [ ] **Step 3: Run targeted tests to verify GREEN**

Run the targeted command from Task 1. Expected: all selected tests pass.

### Task 3: Version and verify

**Files:**
- Modify: `src/MailLogInspector.App/MailLogInspector.App.csproj`
- Modify: `src/MailLogInspector.App/MailLogInspectorVersion.cs`

- [ ] **Step 1: Update all version surfaces**

Set package, assembly, file, informational, and semantic versions to `0.178`.

- [ ] **Step 2: Run full verification**

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj --no-restore -v minimal -tl:off
dotnet build src\MailLogInspector.App\MailLogInspector.App.csproj -c Debug --no-restore -v minimal -tl:off
git diff --check
```

Expected: all tests pass, Debug build has no errors, and diff check is clean.
