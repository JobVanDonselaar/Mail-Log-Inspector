# Mail Log Inspector

Standalone Windows-app voor het importeren, doorzoeken en analyseren van SMTP.com CSV- en ZIP-rapporten.

## Doel

Mail Log Inspector maakt grote mailrapporten snel doorzoekbaar, toont afleverstatus en bounce-oorzaken en bewaakt importkwaliteit via analyse- en beheerweergaven. Dagrapporten kunnen rechtstreeks uit het SMTP.com-portaal, via Gmail of handmatig worden geïmporteerd.

## Belangrijke paden

- Broncode: C:\GithubCoPilot\Mail Log Inspector
- Debug-testversie: `C:\GithubCoPilot\Mail Log Inspector\src\MailLogInspector.App\bin\Debug\net10.0-windows\MailLogInspector.exe`
- Productiemap: `C:\Apps\Mail Log Inspector`
- Productiedatabase: `C:\Apps\Mail Log Inspector\mail-log-inspector.sqlite`
- Publishstarter: `C:\GithubCoPilot\Mail Log Inspector\Publish-MailLogInspector.bat`

## Projecten

- `src\MailLogInspector.App`: WPF-interface, synchronisatie, SMTP.com-portaal, Gmail, tray en single-instance gedrag
- `src\MailLogInspector.Core`: gedeelde modellen, CSV-parser, workspace en lokale logging
- `src\MailLogInspector.Storage`: SQLite-schema, import, zoeken, analyse en veilige rebuild
- `tests\MailLogInspector.Storage.Tests`: integratie-, regressie- en layouttests

## Database

De EXE beheert de SQLite-database. Pas de productiedatabase nooit handmatig aan en verwijder hem niet om een schemawijziging af te dwingen.

Bij een nieuwe database-indeling bouwt de EXE een tijdelijke workspace vanuit de bestanden in `Archive`. Na integriteits- en totalencontroles wisselt de EXE de database en maandarchieven om. Bij fouten blijft de vorige workspace actief.

Bestaat er nog geen database, dan maakt de EXE die zelf aan. De eerste automatische synchronisatie importeert in dat geval alleen het nieuwste geldige dagrapport; oudere portaal- of Gmail-historie wordt niet automatisch als volledige backfill verwerkt.

## Dagelijks gebruik

- Importeer CSV/ZIP via `Dashboard` of gebruik `Sync nu`.
- Zoek op datum, afzender, ontvanger en status. Bij een afzenderdomein staat `Domeinanalyse tonen` standaard aan en opent het een snel dashboard met 30-daagse aflevertrend, aflevertijdverdeling en bounce-oorzaken. Bij één gevonden afzendergroep worden de ontvangers automatisch uitgeklapt.
- Excel exporteert de werkelijk zichtbare zoekregels. Met domeinanalyse actief staat als eerste werkblad een zakelijk Exquise Next Generation afleverrapport voor het praktijkdomein en verzending via SMTP.com. De filterbare zoekresultaten volgen op het tweede werkblad.
- Analyse toont totalen, domeinen, SMTP-responsen en bounceoorzaken.
- Dashboard toont importkwaliteit, één gecombineerde importlijst, acties en opslag. De importlijst vermeldt `SMTP.com direct`, `IMAP` of `Handmatig` als bron.
- Start `MailLogInspector.exe /admin` om bronkeuze, inloggegevens, automatische synchronisatie en systeemvakgedrag te beheren. Dezelfde instellingen zijn onderaan Help bereikbaar.
- Lokale diagnose staat in `Logs\mail-log-inspector.log` onder de workspace. Synchronisatie- en importregels bevatten altijd een bronlabel.

De EXE bouwt benodigde domeinaggregaties na een upgrade eenmalig en transactioneel op voor de actieve database en maandarchieven. Gewone zoekresultaten blijven detaildata gebruiken; dashboardqueries lezen daarna alleen de compacte dagtotalen.

## Synchronisatie en IMAP 0.197

Onder `/admin` zijn drie modi beschikbaar:

1. `Direct downloaden, bij fout IMAP`: probeert SMTP.com direct en gebruikt de ingestelde IMAP-mailbox bij een technische fout of wanneer geen passend `Ready`-rapport beschikbaar is.
2. `Alleen IMAP`: gebruikt uitsluitend de ingestelde IMAP-rapportflow.
3. `Alleen direct downloaden`: gebruikt uitsluitend het SMTP.com-portaal en valt nooit terug op IMAP.

De gekozen modus geldt voor automatische synchronisatie, `Sync nu` en de beheerdersactie `Nu synchroniseren`.

- De eerste automatische poging start dagelijks om `01:00` lokale tijd.
- Zolang het rapport van gisteren ontbreekt, volgt iedere 15 minuten een nieuwe poging.
- Zodra gisteren is geïmporteerd, stopt de controle tot de volgende dag om `01:00`.
- `Sync nu` en `Nu synchroniseren` voeren altijd direct een poging uit.
- Bij een lege database maakt de EXE de database aan en importeert de beheerdersactie alleen het nieuwste geldige rapport.
- Automatische synchronisatie en sluiten naar het systeemvak zijn algemene instellingen en kunnen zonder mailboxconfiguratie worden opgeslagen.
- De IMAP-koppeling biedt `Gmail`, `Microsoft 365 / Outlook.com` en `Eigen IMAP-server`. Gmail en Microsoft 365 vullen server, poort en TLS automatisch in; bij een eigen server vult de beheerder die waarden in.
- Google OAuth is alleen beschikbaar voor Gmail. App- en IMAP-wachtwoorden worden Windows-gebruikergebonden versleuteld opgeslagen. Alle IMAP-verbindingen gebruiken direct TLS of verplichte STARTTLS; plaintext fallback is niet toegestaan.
- Ontbrekende rapportdagen worden bij bestaande historie oudste eerst verwerkt.
- Rapportdagen worden uit UTC-tijdstempels bepaald, zodat lokale tijdzones geen verkeerde dagselectie veroorzaken.
- Een actieve synchronisatie kan via `Stop` worden geannuleerd; download, import en databasewrites ontvangen dezelfde annulering.
- De normale portalinstelling blijft doorgaans `10 / page`. Alleen bij meer dan drie kalenderdagen achterstand kiest de app `100 / page`.
- Alleen pagina 1 wordt gebruikt; de app navigeert nooit door rapportpagina's en klikt nooit op `Add CSV Report`.
- Alleen `Ready`-rapporten met `delivered + bounced + queue` en `raw_event_stream` worden geïmporteerd. Spamrapporten worden genegeerd.
- SHA-256 voorkomt dubbele imports, ongeacht de gekozen bron.

SMTP.com-gebruikersnaam, wachtwoord en MFA-secret worden Windows-gebruikergebonden versleuteld opgeslagen. De WebView2-sessie gebruikt een blijvend lokaal profiel. De knoppen `Proefdownload uitvoeren` en `Zichtbare diagnose` blijven veilige diagnostiek: ze valideren en bewaren een ZIP, maar roepen de importservice niet aan.

Downloads en imports zijn begrensd: maximaal 512 MB per ZIP en maximaal 3 GB per uitgepakte of losse CSV, maximaal 100 ZIP-entries en maximaal compressieverhouding 200. Een ZIP moet precies één niet-lege CSV bevatten. Een header-only rapport is een geldig nulrapport; een leeg of volledig foutief rapport wordt niet geregistreerd als succesvolle import.

Zie [docs/smtp-report-download-flow.md](docs/smtp-report-download-flow.md) voor de volledige selectie-, fallback- en beveiligingsregels.

## Build en test

```powershell
dotnet test tests\MailLogInspector.Storage.Tests\MailLogInspector.Storage.Tests.csproj
dotnet build src\MailLogInspector.App\MailLogInspector.App.csproj -c Debug
```

GitHub Copilot publiceert niet automatisch. Publiceren gebeurt door de gebruiker met `C:\GithubCoPilot\Mail Log Inspector\Publish-MailLogInspector.bat` naar `C:\Apps\Mail Log Inspector`.

## Werkregels

`AGENTS.md` is leidend voor database-, build-, publish-, versie- en Git-afspraken.


