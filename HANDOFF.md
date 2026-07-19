# Mail Log Inspector - overdracht

## Status

- Applicatieversie: `0.194`
- Branch: `main`
- Broncommit: `09bb965` (`docs: define source handoff archive`)
- Remote: `https://github.com/skatejunk-ux/MailLogInspector.git`
- Verificatie voor overdracht: 241 tests geslaagd; Debug-build geslaagd met 0 waarschuwingen en 0 fouten; EXE-versie `0.194.0.0`.
- Er is geen verplichte onafgemaakte taak. Start nieuwe wijzigingen vanuit deze bronstaat.

Deze ZIP bevat geen Git-metadata. Maak op de nieuwe locatie desgewenst een nieuwe repository of koppel de map opnieuw aan de bovenstaande remote.

## Doel

Mail Log Inspector is een standalone Windows-app voor het importeren, doorzoeken en analyseren van SMTP.com CSV- en ZIP-rapporten. De app toont afleverstatus, aflevertijd, bounce-oorzaken, domeinanalyses en importkwaliteit.

## Projectstructuur

- `src/MailLogInspector.App`: WPF-interface, SMTP.com-portaal, IMAP, synchronisatie, Excel-export, tray en single-instance gedrag.
- `src/MailLogInspector.Core`: gedeelde modellen, CSV-parser, workspacepaden en lokale logging.
- `src/MailLogInspector.Storage`: SQLite-schema, import, zoeken, analyse, aggregaties, retentie en veilige rebuild.
- `tests/MailLogInspector.Storage.Tests`: unit-, integratie-, regressie- en layouttests.
- `scripts/Publish-MailLogInspector.ps1`: publishlogica achter de bestaande BAT op de oorspronkelijke computer.

## Synchronisatie

De beheerdersinstellingen ondersteunen drie bronnen:

1. `Direct downloaden, bij fout IMAP`.
2. `Alleen direct downloaden`.
3. `Alleen IMAP`.

Belangrijk gedrag:

- De eerste automatische poging start dagelijks om `01:00` lokale tijd.
- Zolang het rapport van gisteren ontbreekt, probeert de app het iedere 15 minuten opnieuw.
- Na een geslaagde dagimport wordt pas de volgende dag opnieuw automatisch gecontroleerd.
- SMTP.com direct gebruikt alleen een passend `Ready`-rapport met `delivered + bounced + queue` en `raw_event_stream`.
- IMAP ondersteunt Gmail, Microsoft 365 / Outlook.com en een eigen IMAP-server.
- Wachtwoorden, tokens en MFA-secrets worden Windows-gebruikergebonden versleuteld opgeslagen en zitten niet in deze ZIP.
- SHA-256 voorkomt dubbele imports tussen directe download, IMAP en handmatige import.

Lees `docs/smtp-report-download-flow.md` voor de actuele selectie-, fallback- en beveiligingsregels.

## Build en test

Vanaf de rootmap:

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj
dotnet build src\MailLogInspector.App\MailLogInspector.App.csproj -c Debug
```

Handmatige test-EXE na de Debug-build:

```text
src\MailLogInspector.App\bin\Debug\net10.0-windows\MailLogInspector.exe
```

## Werkregels

- Verhoog na iedere code- of gedragswijziging de versie in `MailLogInspector.App.csproj` en `MailLogInspectorVersion.cs`.
- Pas SQLite-databases nooit handmatig aan. Laat import, migratie, retentie en rebuild door de EXE uitvoeren.
- Publiceren gebeurt niet als onderdeel van gewone ontwikkeling. Op de oorspronkelijke computer gebruikt de gebruiker `C:\Codex\Publish-MailLogInspector.bat` en is het doel `C:\Apps\Mail Log Inspector`.
- Bewaar broncode, tests en documentatie gescheiden van databases, downloads, logs en publishoutput.
- Lees eerst `AGENTS.md`, `README.md`, `ROADMAP.md` en `docs/smtp-report-download-flow.md`.

## Inhoud en privacy

De overdracht bevat broncode, tests, scripts en actuele documentatie. Niet opgenomen zijn Git-metadata, databases, back-ups, logs, downloads, WebView2-profielen, cookies, sessies, opgeslagen geheimen, builds en publishbestanden.