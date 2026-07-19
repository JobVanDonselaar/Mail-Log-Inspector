# Mail Log Inspector overdrachts-ZIP

## Doel

Maak een veilige, compacte broncodesnapshot waarmee een nieuwe Codex-chat op
een andere locatie direct verder kan werken aan Mail Log Inspector versie
`0.194`.

## Doelbestand

`C:\Codex\MailLogInspector-Handoff-0.194.zip`

## Inhoud

- `src` met alle broncode en projectbestanden, zonder `bin` en `obj`.
- `tests` met alle testcode en testprojecten, zonder build- of testresultaten.
- `scripts`.
- `MailLogInspector.slnx` en `.gitignore`.
- `README.md`, `ROADMAP.md` en `AGENTS.md`.
- Actuele documentatie voor synchronisatie, SMTP.com, IMAP, Help en
  databasebeheer.
- Een nieuw `HANDOFF.md` met versie, commit, projectstructuur, werking,
  build- en testinstructies en veiligheidsregels.
- `MANIFEST-SHA256.txt` met een SHA-256-hash voor ieder bestand in de ZIP.

## Uitsluitingen

- `.git`, `.worktrees`, `.codex` en `.agents`.
- SQLite-databases, back-ups, WAL- en SHM-bestanden.
- `Archive`, `ArchiveDb`, `Incoming` en `Logs`.
- `bin`, `obj`, `artifacts`, testresultaten, EXE's en publishbestanden.
- WebView2-profielen, cookies, sessies, wachtwoorden, tokens en andere
  opgeslagen geheimen.
- Oude mockups, screenshots en achterhaalde ontwerpen.

## Actuele documentatie

Neem naast de rootdocumentatie minimaal mee:

- `docs/smtp-report-download-flow.md`
- `docs/migration.md`
- `docs/2026-07-05-current-gaps.md`
- de goedgekeurde ontwerpen en uitvoeringsplannen van 18 en 19 juli 2026 die
  overeenkomen met de huidige synchronisatie-, IMAP-, SMTP.com- en Help-code.

Laat het ingetrokken ontwerp voor handmatige SMTP.com-rapportselectie en oude
GUI-mockups buiten de ZIP.

## Validatie

- Pak de ZIP na creatie testmatig uit in een tijdelijke map.
- Controleer dat de solution, alle drie projecten, tests, scripts,
  `HANDOFF.md` en het manifest aanwezig zijn.
- Controleer dat geen uitgesloten map, database, EXE, DLL, PDB, ZIP of
  geheimenbestand aanwezig is.
- Controleer de manifesthashes tegen de uitgepakte bestanden.
- Rapporteer bestandenaantal, ZIP-grootte en SHA-256 van de ZIP.
