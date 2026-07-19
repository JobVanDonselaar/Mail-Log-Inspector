# Zakelijke Help-tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Vervang de screenshot-gedreven Help-tab door een compacte zakelijke naslag die alle actuele functies precies beschrijft.

**Architecture:** De bestaande `HelpTab` blijft onderdeel van `MainWindow.xaml`; alleen de inhoud binnen de tab wordt vervangen door een eenkolomsindeling met negen tekstsecties. De bestaande `/admin`-knop en eventhandler blijven intact. Layouttests bewaken secties, kerntermen, afwezigheid van afbeeldingen en versie `0.194`.

**Tech Stack:** .NET 10, WPF XAML, xUnit, PowerShell, Git.

## Global Constraints

- Wijzig geen functionele applicatielogica of databases.
- Behoud `OpenAdminSettingsButton` en `OpenAdminSettingsButton_Click`.
- Verwijder alle Help-screenshots en projectverwijzingen daarnaar wanneer `rg` bevestigt dat ze nergens anders worden gebruikt.
- Verhoog de applicatieversie naar exact `0.194`.
- Publiceer niet; bouw alleen de Debug-EXE voor de gebruiker.
- Behoud alle reeds aanwezige, niet-gerelateerde lokale wijzigingen.

---

### Task 1: Leg de zakelijke Help-structuur vast in regressietests

**Files:**
- Modify: `tests/MailLogInspector.Storage.Tests/MainWindowLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `MainWindow.xaml`, `MailLogInspector.App.csproj`, `MailLogInspectorVersion.cs`
- Produces: layoutcontract voor de negen secties, afwezige Help-afbeeldingen, behouden admin-knop en versie `0.194`

- [ ] **Step 1: Vervang de screenshotverwachtingen door het nieuwe Help-contract**

Werk `HelpTabDescribesAllCurrentUserWorkflows` bij zodat minimaal deze exacte teksten worden gecontroleerd:

```csharp
Assert.Contains("Doel en snel beginnen", helpXaml, StringComparison.Ordinal);
Assert.Contains("Zoeken", helpXaml, StringComparison.Ordinal);
Assert.Contains("Domeinanalyse en Excel", helpXaml, StringComparison.Ordinal);
Assert.Contains("Analyse", helpXaml, StringComparison.Ordinal);
Assert.Contains("Dashboard", helpXaml, StringComparison.Ordinal);
Assert.Contains("Synchronisatie en beheerdersinstellingen", helpXaml, StringComparison.Ordinal);
Assert.Contains("Database en maandarchieven", helpXaml, StringComparison.Ordinal);
Assert.Contains("Systeemvak en afsluiten", helpXaml, StringComparison.Ordinal);
Assert.Contains("Veelvoorkomende situaties", helpXaml, StringComparison.Ordinal);
Assert.Contains("Resultaten", helpXaml, StringComparison.Ordinal);
Assert.Contains("Meer laden", helpXaml, StringComparison.Ordinal);
Assert.Contains("Domeinanalyse tonen", helpXaml, StringComparison.Ordinal);
Assert.Contains("SMTP-responsen", helpXaml, StringComparison.Ordinal);
Assert.Contains("Nu synchroniseren", helpXaml, StringComparison.Ordinal);
Assert.Contains("Proefdownload", helpXaml, StringComparison.Ordinal);
Assert.Contains("Standaardsyntax", helpXaml, StringComparison.Ordinal);
Assert.Contains("Maandarchieven", helpXaml, StringComparison.Ordinal);
Assert.Contains("Name=\"OpenAdminSettingsButton\"", helpXaml, StringComparison.Ordinal);
Assert.DoesNotContain("<Image", helpXaml, StringComparison.Ordinal);
Assert.DoesNotContain("Assets/Help", helpXaml, StringComparison.Ordinal);
```

Verwijder `HelpScreenshotsExistAndUseOnlyDemoIdentifiers`, omdat screenshots geen onderdeel meer zijn van de applicatie.

- [ ] **Step 2: Verhoog de verwachte versie in de test naar 0.194**

```csharp
Assert.Contains("<InformationalVersion>0.194</InformationalVersion>", project, StringComparison.Ordinal);
Assert.Contains("SemanticVersion = \"0.194\"", version, StringComparison.Ordinal);
```

- [ ] **Step 3: Draai de gerichte test en bevestig dat hij rood is**

Run:

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj -m:1 -p:NuGetAudit=false --filter "FullyQualifiedName~MainWindowLayoutConsistencyTests"
```

Expected: FAIL omdat de huidige Help-tab nog screenshots bevat, secties mist en versie `0.193` gebruikt.

---

### Task 2: Vervang de Help-tab door de zakelijke eenkolomsnaslag

**Files:**
- Modify: `src/MailLogInspector.App/MainWindow.xaml`

**Interfaces:**
- Consumes: bestaande WPF-resources `PanelBorderStyle`, `SectionTitleStyle`, `MutedTextBrush`, `AccentBrush`, `AccentDarkBrush`, `SecondaryButtonStyle`
- Produces: tekstuele Help-tab zonder `Image`-elementen

- [ ] **Step 1: Vervang uitsluitend het bestaande `HelpTab`-inhoudsblok**

Gebruik:

```xml
<ScrollViewer Background="{StaticResource PanelSubtleFillBrush}" VerticalScrollBarVisibility="Auto">
  <StackPanel Name="HelpTabContent"
              MaxWidth="980"
              Margin="24,20,24,28"
              HorizontalAlignment="Center">
    <!-- Introductie en negen zakelijke secties -->
  </StackPanel>
</ScrollViewer>
```

Iedere sectie gebruikt een `Border` met `PanelBorderStyle`, `Padding="22"` en `Margin="0,0,0,14"`. Iedere bullet is een afzonderlijke `TextBlock` met `TextWrapping="Wrap"` en een compacte ondermarge.

- [ ] **Step 2: Schrijf de negen secties in de overeengekomen volgorde**

Neem de inhoud uit `docs/superpowers/specs/2026-07-19-business-help-tab-design.md` volledig op. Beschrijf concreet:

```text
Doel en snel beginnen
Zoeken
Domeinanalyse en Excel
Analyse
Dashboard
Synchronisatie en beheerdersinstellingen
Database en maandarchieven
Systeemvak en afsluiten
Veelvoorkomende situaties
```

Gebruik korte zakelijke zinnen. Benoem knop- en veldnamen exact zoals in de GUI. Leg statussen, resultaatlimiet, zichtbare Excel-regels, automatische synchronisatie vanaf 01:00, 15-minutenherhaling, bron/fallback, IMAP, SMTP.com, proefdownload, diagnose, rapportsyntax, retentie, archieven en lokaal log uit.

- [ ] **Step 3: Behoud de admin-knop onderaan**

```xml
<Button Name="OpenAdminSettingsButton"
        Style="{StaticResource SecondaryButtonStyle}"
        Padding="10,5"
        Margin="0,12,0,0"
        HorizontalAlignment="Left"
        Click="OpenAdminSettingsButton_Click">
  <TextBlock Text="Beheerdersinstellingen openen" />
</Button>
```

- [ ] **Step 4: Controleer statisch dat de Help-tab geen afbeeldingen bevat**

Run:

```powershell
$xaml = Get-Content -LiteralPath 'src\MailLogInspector.App\MainWindow.xaml' -Raw
$help = $xaml.Substring($xaml.IndexOf('<TabItem Name="HelpTab">'), $xaml.IndexOf('</TabItem>', $xaml.IndexOf('<TabItem Name="HelpTab">')) - $xaml.IndexOf('<TabItem Name="HelpTab">'))
if ($help -match '<Image|Assets/Help') { throw 'Help bevat nog een afbeeldingsverwijzing.' }
```

Expected: geen output en exitcode 0.

---

### Task 3: Verwijder ongebruikte Help-assets en verhoog de versie

**Files:**
- Delete: `src/MailLogInspector.App/Assets/Help/help-search.png`
- Delete: `src/MailLogInspector.App/Assets/Help/help-domain-analysis.png`
- Delete: `src/MailLogInspector.App/Assets/Help/help-analysis.png`
- Delete: `src/MailLogInspector.App/Assets/Help/help-manage.png`
- Delete: `src/MailLogInspector.App/Assets/Help/help-archives.png`
- Modify: `src/MailLogInspector.App/MailLogInspector.App.csproj`
- Modify: `src/MailLogInspector.App/MailLogInspectorVersion.cs`

**Interfaces:**
- Consumes: het nieuwe Help-XAML zonder afbeeldingsverwijzingen
- Produces: applicatieversie `0.194` zonder ongebruikte Help-resources

- [ ] **Step 1: Controleer dat de vijf PNG-bestanden nergens anders worden gebruikt**

Run:

```powershell
rg -n "help-(search|domain-analysis|analysis|manage|archives)\.png|Assets[\\/]Help" .
```

Expected: alleen de te verwijderen projectresource, oude tests/documentatie of reeds vervangen Help-XAML; geen functionele runtimeverwijzingen.

- [ ] **Step 2: Verwijder de vijf Help-PNG-bestanden en de resource wildcard**

Verwijder:

```xml
<Resource Include="Assets\Help\*.png" />
```

Verwijder daarna alleen de vijf hierboven genoemde bestanden.

- [ ] **Step 3: Verhoog alle versievelden**

In `MailLogInspector.App.csproj`:

```xml
<Version>0.194.0</Version>
<AssemblyVersion>0.194.0.0</AssemblyVersion>
<FileVersion>0.194.0.0</FileVersion>
<InformationalVersion>0.194</InformationalVersion>
```

In `MailLogInspectorVersion.cs`:

```csharp
public const string SemanticVersion = "0.194";
```

- [ ] **Step 4: Draai de gerichte layouttests**

Run:

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj -m:1 -p:NuGetAudit=false --filter "FullyQualifiedName~MainWindowLayoutConsistencyTests"
```

Expected: alle `MainWindowLayoutConsistencyTests` PASS.

---

### Task 4: Verifieer de volledige wijziging

**Files:**
- Verify: `tests/MailLogInspector.Storage.Tests/MailLogInspector.Storage.Tests.csproj`
- Verify: `src/MailLogInspector.App/MailLogInspector.App.csproj`
- Verify: `src/MailLogInspector.App/bin/Debug/net10.0-windows/MailLogInspector.exe`

**Interfaces:**
- Consumes: Help-XAML, tests, projectresources en versie `0.194`
- Produces: aantoonbaar testbare Debug-build voor handmatige controle

- [ ] **Step 1: Draai de volledige testsuite**

Run:

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj -m:1 -p:NuGetAudit=false
```

Expected: alle tests PASS.

- [ ] **Step 2: Bouw de Debug-EXE**

Run:

```powershell
$env:MSBuildEnableWorkloadResolver='false'
dotnet build src\MailLogInspector.App\MailLogInspector.App.csproj -c Debug -m:1 -p:NuGetAudit=false
```

Expected: build geslaagd met 0 warnings en 0 errors.

- [ ] **Step 3: Controleer versie en diff-hygiëne**

Run:

```powershell
[System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path 'src\MailLogInspector.App\bin\Debug\net10.0-windows\MailLogInspector.exe')).FileVersion
git diff --check
```

Expected: `0.194.0.0` en geen `git diff --check`-uitvoer.

- [ ] **Step 4: Rapporteer de test-EXE zonder te publiceren**

Geef de gebruiker:

```text
C:\Codex\Mail Log Inspector\src\MailLogInspector.App\bin\Debug\net10.0-windows\MailLogInspector.exe
```

Meld expliciet dat niet is gepubliceerd.
