# Mail Log Inspector Handoff Archive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bouw en valideer `C:\Codex\MailLogInspector-Handoff-0.194.zip` als veilige, compacte overdracht van de actuele broncode en documentatie.

**Architecture:** Kopieer een expliciete allowlist naar een tijdelijke stagingmap, voeg daar `HANDOFF.md` en een SHA-256-manifest aan toe en maak pas daarna de ZIP. Pak de ZIP opnieuw uit in een aparte validatiemap en controleer aanwezigheid, uitsluitingen en alle manifesthashes.

**Tech Stack:** PowerShell 7, .NET-projectbestanden, SHA-256, ZIP.

## Global Constraints

- Broncommit is `98052f946ac1e2ce260b574d35ac21ce7e60e9c1` of een latere commit die uitsluitend deze overdrachtsdocumentatie toevoegt.
- Doelbestand is exact `C:\Codex\MailLogInspector-Handoff-0.194.zip`.
- Neem geen Git-metadata, databases, builds, logs, downloads, sessies of geheimen op.
- Gebruik een allowlist; kopieer nooit de volledige werkmap en filter daarna.
- Verander geen applicatiecode of productiedatabase.

---

### Task 1: Maak de overdrachtsinhoud

**Files:**
- Create in staging: `HANDOFF.md`
- Copy to staging: `src`, `tests`, `scripts`, `MailLogInspector.slnx`, `.gitignore`, `README.md`, `ROADMAP.md`, `AGENTS.md`
- Copy selected: actuele `docs`-bestanden uit de goedgekeurde specificatie

**Interfaces:**
- Consumes: schone repository op versie `0.194`
- Produces: tijdelijke stagingmap met uitsluitend toegestane bestanden

- [ ] **Step 1: Maak een lege stagingmap onder `C:\tmp`**

```powershell
$staging = 'C:\tmp\MailLogInspector-Handoff-0.194'
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging | Out-Null
```

- [ ] **Step 2: Kopieer rootbestanden, broncode, tests en scripts via een allowlist**

Kopieer rootbestanden expliciet. Kopieer directorybomen recursief, maar sla iedere map met naam `bin`, `obj`, `TestResults`, `.git`, `.worktrees`, `.codex`, `.agents` of `artifacts` over.

- [ ] **Step 3: Kopieer alleen actuele documentatie**

Neem op:

```text
docs/2026-07-05-current-gaps.md
docs/migration.md
docs/smtp-report-download-flow.md
docs/superpowers/specs/2026-07-18-import-quality-previous-week-shadow-design.md
docs/superpowers/specs/2026-07-18-sync-source-selection-design.md
docs/superpowers/specs/2026-07-18-unified-imports-list-design.md
docs/superpowers/specs/2026-07-19-business-help-tab-design.md
docs/superpowers/specs/2026-07-19-smtp-session-and-report-syntax-design.md
docs/superpowers/specs/2026-07-19-handoff-archive-design.md
docs/superpowers/plans/2026-07-18-import-quality-previous-week-shadow.md
docs/superpowers/plans/2026-07-18-sync-source-selection.md
docs/superpowers/plans/2026-07-18-unified-imports-admin-mode.md
docs/superpowers/plans/2026-07-19-business-help-tab.md
docs/superpowers/plans/2026-07-19-imap-provider-admin.md
docs/superpowers/plans/2026-07-19-smtp-session-and-report-syntax.md
docs/superpowers/plans/2026-07-19-handoff-archive.md
```

- [ ] **Step 4: Schrijf `HANDOFF.md`**

Beschrijf minimaal:

```text
Versie 0.194
Commit en branch
Doel van de applicatie
Projectstructuur
Actuele synchronisatiebronnen en planning
Build-, test- en Debug-EXE-commando's
Database- en publishveiligheidsregels
Status: geen verplichte onafgemaakte taak
```

---

### Task 2: Maak manifest en ZIP

**Files:**
- Create in staging: `MANIFEST-SHA256.txt`
- Create: `C:\Codex\MailLogInspector-Handoff-0.194.zip`

**Interfaces:**
- Consumes: gevalideerde stagingmap
- Produces: overdrachts-ZIP met reproduceerbare bestandslijst

- [ ] **Step 1: Controleer de stagingmap op verboden patronen**

Laat de controle falen bij directories met uitgesloten namen of bestanden met extensies:

```text
.sqlite .sqlite-shm .sqlite-wal .db .exe .dll .pdb .zip .log
```

Controleer daarnaast dat geen pad `WebView2`, `Cookies`, `Incoming`, `ArchiveDb` of `Logs` bevat.

- [ ] **Step 2: Genereer het manifest**

Sorteer alle stagingbestanden behalve `MANIFEST-SHA256.txt` op relatief pad en schrijf regels als:

```text
<lowercase-sha256>  <relatief/pad/met/slashes>
```

- [ ] **Step 3: Maak de ZIP**

Verwijder alleen een bestaand bestand op het exacte doelpad en gebruik daarna:

```powershell
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath 'C:\Codex\MailLogInspector-Handoff-0.194.zip' -CompressionLevel Optimal
```

---

### Task 3: Valideer de overdracht

**Files:**
- Verify: `C:\Codex\MailLogInspector-Handoff-0.194.zip`
- Temporary: `C:\tmp\MailLogInspector-Handoff-0.194-verify`

**Interfaces:**
- Consumes: gemaakte ZIP en manifest
- Produces: gecontroleerde ZIP-grootte, bestandenaantal en ZIP-hash

- [ ] **Step 1: Pak de ZIP uit in een lege validatiemap**

```powershell
Expand-Archive -LiteralPath 'C:\Codex\MailLogInspector-Handoff-0.194.zip' -DestinationPath $verify
```

- [ ] **Step 2: Controleer vereiste bestanden**

Controleer minimaal:

```text
HANDOFF.md
MANIFEST-SHA256.txt
MailLogInspector.slnx
src/MailLogInspector.App/MailLogInspector.App.csproj
src/MailLogInspector.Core/MailLogInspector.Core.csproj
src/MailLogInspector.Storage/MailLogInspector.Storage.csproj
tests/MailLogInspector.Storage.Tests/MailLogInspector.Storage.Tests.csproj
scripts/Publish-MailLogInspector.ps1
```

- [ ] **Step 3: Herhaal de uitsluitingscontrole op de uitgepakte ZIP**

Expected: nul verboden directories, extensies of runtimepaden.

- [ ] **Step 4: Verifieer iedere manifesthash**

Parse iedere regel, bereken `Get-FileHash -Algorithm SHA256` op het uitgepakte bestand en stop bij de eerste afwijking.

- [ ] **Step 5: Rapporteer resultaat**

Rapporteer:

```text
ZIP-pad
bestandenaantal
ZIP-grootte
SHA-256 van de ZIP
broncommit
```
