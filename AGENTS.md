# AGENTS.md

## Doel

Deze repo bevat `Mail Log Inspector`, een standalone Windows-app voor import, zoekacties en analyse van mail log CSV- en ZIP-bestanden.

## Werkregels

- Werk in deze repo en houd `Mail Log Inspector` gescheiden van oudere toolbox-projecten of productiepaden.
- Gebruik `main` als stabiele basis tenzij de gebruiker expliciet iets anders vraagt.
- Laat oude of parallelle implementaties vallen als de nieuwe flow de definitieve keuze is.
- Doe geen handmatige ingrepen in de SQLite-database.
- Als een datastructuur moet veranderen, werk via import of rebuild.
- Verhoog na elke code- of gedragsaanpassing altijd de app-versie voordat het werk als klaar wordt gemeld.

## Build en test

- Voor handmatig testen gebruikt de gebruiker de Debug-EXE: `C:\Codex\Mail Log Inspector\src\MailLogInspector.App\bin\Debug\net10.0-windows\MailLogInspector.exe`.
- Codex mag de Debug-build bijwerken met `dotnet build src\MailLogInspector.App\MailLogInspector.App.csproj -c Debug`, maar publiceert niet zelf.
- Draai gerichte tests via `dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj --filter <filter>`.
- Gebruik Release-build alleen als verificatie of voorbereiding, niet als impliciete publish.

## Publish

- De standaard publish-doelmap is `C:\Apps\Mail Log Inspector`.
- Publiceren gebeurt door de gebruiker via `C:\Codex\Publish-MailLogInspector.bat`.
- Codex voert geen publish uit en start geen productie-EXE, tenzij de gebruiker dat in die beurt expliciet vraagt.

## Git

- Houd branches kort en duidelijk.
- Gebruik `codex/` als prefix voor nieuwe feature branches als er een branch nodig is.
- Verwijder alleen branches die al gemerged zijn of waarvan de gebruiker expliciet heeft gevraagd ze op te ruimen.

## Documentatie

- Werk `README.md`, `ROADMAP.md` en relevante docs bij als de workflow of het gedrag verandert.
- Leg nieuwe repo-regels hier vast als ze blijvend zijn.
